using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// A composer drawn by the window: a header (what it is, who it goes to, the escaping mode, the dictionary), a native
/// editor (<see cref="ComposerEditor"/>), and the key row — all read off the client's <see cref="ComposerModel"/>, the
/// model the cell composer is drawn from. Used for the docked panel, over its pane's cells, and for F1, over the whole
/// cell area (<see cref="ComposerOverlay"/>).
/// <para>
/// <b>Every gesture goes back through the <see cref="ComposerSession"/></b>, so it ends in the client method the cell
/// composer's key ends in: ⌃S sends (or saves a MOO edit), ⌥L flips the escaping, ⌥W adds the word at the caret, Esc
/// releases a docked composer to the command line or closes F1 keeping the draft. The text the editor holds is handed
/// back as it changes; the client's text replaces it only when the model's revision moved, so typing here never has
/// its caret or its undo taken away by its own echo.
/// </para>
/// <para>
/// <b>Arming is the client's one fact.</b> A click in a docked editor asks to arm it, and the editor takes the keyboard
/// when the model says it is armed and gives it back to the cells when the model says it is not. ⌥⏎ is the window's
/// (it maximises), so a newline is ⏎, ⌃⏎ or ⇧⏎ here. Chords the editor has no use for — ⌃P, ⌃O, ⌃↑/⌃↓, ⌥J, the
/// function keys — reach the client from a docked editor exactly as they would from its cells.
/// </para>
/// </summary>
internal sealed class ComposerView : Border
{
    private readonly TerminalView _view;
    private readonly bool _fullScreen;
    private readonly ComposerEditor _editor;
    private readonly MarkLayer _marks;
    private readonly Border _header;
    private readonly TextBlock _title;
    private readonly TextBlock _target;
    private readonly StackPanel _chips;
    private readonly TextBlock _count;
    private readonly Border _preview;
    private readonly TextBlock _previewHeading;
    private readonly TextBlock _previewText;
    private readonly Border _footer;
    private readonly WrapPanel _keys;
    private readonly Border _rule;
    private ComposerSession? _session;
    private ComposerModel? _model;
    private long _applied = -1;
    private bool _applying;
    private bool? _armed;
    private PanelInk _ink = new("#5fafff", "#ff5f5f", "#3a3a40");
    private Color _plane = Colors.Black;
    private Color _text = Colors.White;

    /// <param name="view">The cells: where the keyboard goes back to, and the face the editor's monospace text uses.</param>
    /// <param name="fullScreen">F1 rather than the docked panel.</param>
    public ComposerView(TerminalView view, bool fullScreen)
    {
        _view = view;
        _fullScreen = fullScreen;
        ClipToBounds = true;
        Focusable = false;

        _editor = new ComposerEditor
        {
            Background = Brushes.Transparent,
            Padding = fullScreen ? new Thickness(22, 14) : new Thickness(12, 6),
        };
        AutomationProperties.SetName(_editor, fullScreen ? "Compose a post" : "Docked composer");
        _editor.TextChanged += (_, _) => OnTextChanged();
        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        _editor.AddHandler(PointerPressedEvent, (_, _) => AskToArm(), RoutingStrategies.Tunnel, handledEventsToo: true);
        _editor.GotFocus += (_, _) => AskToArm();
        _editor.ContextRequested += OnContextRequested;
        _marks = new MarkLayer(_editor);

        _title = new TextBlock { FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        _target = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        _count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var headerRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_title, Dock.Left);
        DockPanel.SetDock(_count, Dock.Right);
        DockPanel.SetDock(_chips, Dock.Right);
        headerRow.Children.Add(_title);
        headerRow.Children.Add(_count);
        headerRow.Children.Add(_chips);
        headerRow.Children.Add(_target);
        _header = new Border { Child = headerRow, Padding = fullScreen ? new Thickness(22, 10) : new Thickness(12, 3, 8, 3) };

        _keys = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _footer = new Border { Child = _keys, Padding = fullScreen ? new Thickness(22, 8) : new Thickness(12, 2, 8, 3) };

        // F1's as-sent preview (⌥P): the one command ⌃S sends, in the editor's face, under the editor.
        _previewHeading = new TextBlock { Text = "AS SENT", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) };
        _previewText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis };
        _preview = new Border
        {
            Child = new StackPanel { Children = { _previewHeading, _previewText } },
            Padding = new Thickness(22, 8),
            IsVisible = false,
        };
        AutomationProperties.SetName(_preview, "As sent");

        // The armed rule down the docked editor's left edge: the cell composer's ▌ marker, in the accent.
        _rule = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };

        var body = new Panel();
        body.Children.Add(_editor);
        body.Children.Add(_marks);
        body.Children.Add(_rule);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        Grid.SetRow(_header, 0);
        Grid.SetRow(body, 1);
        Grid.SetRow(_preview, 2);
        Grid.SetRow(_footer, 3);
        grid.Children.Add(_header);
        grid.Children.Add(body);
        grid.Children.Add(_preview);
        grid.Children.Add(_footer);
        Child = grid;
        ApplyFont();
    }

    /// <summary>
    /// Whether this editor writes in the UI's proportional face rather than the cells' monospace one: its character's
    /// choice, kept in the configuration (<c>CharacterDefinition.ComposerProportional</c>) and chosen from the editor's
    /// right-click menu. Until the session says (and with no session) it is the last face chosen in this window, so a
    /// choice shows at once rather than when the client has stored it.
    /// </summary>
    internal bool Proportional => _faceChosen ?? _session?.Proportional ?? false;

    /// <summary>Raised when a face is chosen, so every composer on screen re-reads its own.</summary>
    internal static event Action? FaceChanged;

    // The face just chosen here, until the client has it; cleared when any face changes.
    private bool? _faceChosen;

    /// <summary>The editor. Internal for tests.</summary>
    internal ComposerEditor Editor => _editor;

    /// <summary>What it shows now. Internal for tests.</summary>
    internal ComposerModel? Model => _model;

    /// <summary>The session it hands gestures to. Internal for tests.</summary>
    internal ComposerSession? Session => _session;

    /// <summary>The header's words, joined, for a test.</summary>
    internal string HeaderText => string.Join("  ", new[] { _title.Text, _target.Text, _count.Text }
        .Concat(_chips.Children.OfType<Border>().Select(b => (b.Child as TextBlock)?.Text)).Where(t => !string.IsNullOrEmpty(t)));

    /// <summary>The key row's words, joined, for a test.</summary>
    internal string FooterText => string.Join(" · ", _keys.Children.OfType<StackPanel>()
        .Select(p => string.Join(" ", p.Children.OfType<Control>().Select(Words).Where(t => t.Length > 0))));

    /// <summary>The menu the editor last opened. Internal for tests.</summary>
    internal ContextMenu? OpenedMenu { get; private set; }

    private static string Words(Control control) => control switch
    {
        TextBlock t => t.Text ?? string.Empty,
        Border { Child: TextBlock t } => t.Text ?? string.Empty,
        _ => string.Empty,
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FaceChanged += ApplyFont;
        if (_model is { Armed: true })
        {
            Dispatcher.UIThread.Post(() => _editor.Focus(), DispatcherPriority.Input);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FaceChanged -= ApplyFont;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>
    /// Shows <paramref name="model"/>, handing gestures to <paramref name="session"/>, in the pane's colours: the accent
    /// and warning of <paramref name="ink"/>, on <paramref name="plane"/>, in <paramref name="text"/>.
    /// </summary>
    public void Show(ComposerModel model, ComposerSession session, PanelInk ink, Color plane, Color text)
    {
        if (!ReferenceEquals(session, _session))
        {
            _session = session;
            _applied = -1;
            _armed = null;

            // Another character's composer, or the same one opened again: its face is the session's.
            _faceChosen = null;
            ApplyFont();
        }

        var coloursChanged = ink != _ink || plane != _plane || text != _text;
        _ink = ink;
        _plane = plane;
        _text = text;
        var previous = _model;
        _model = model;

        if (model.Revision != _applied)
        {
            _applied = model.Revision;
            if (!string.Equals(_editor.Text ?? string.Empty, model.Body, StringComparison.Ordinal))
            {
                _applying = true;
                try
                {
                    _editor.Text = model.Body;
                    _editor.CaretIndex = model.Body.Length;
                    _editor.ClearSelection();
                }
                finally
                {
                    _applying = false;
                }
            }
        }

        if (previous is null || !previous.Equals(model))
        {
            _editor.Marks = session.Marks(_editor.Text ?? string.Empty);
        }

        if (coloursChanged || previous is null)
        {
            Paint();
        }

        if (previous is null || !SameChrome(previous, model) || coloursChanged)
        {
            Label(model);
        }

        _rule.IsVisible = !_fullScreen && model.Armed;
        if (_armed != model.Armed)
        {
            var was = _armed;
            _armed = model.Armed;
            if (model.Armed)
            {
                if (VisualRoot is not null)
                {
                    Dispatcher.UIThread.Post(() => _editor.Focus(), DispatcherPriority.Input);
                }
            }
            else if (was == true && _editor.IsKeyboardFocusWithin)
            {
                _view.Focus();
            }
        }
    }

    /// <summary>The header's length cell as drawn. Internal for tests.</summary>
    internal string CountText => _count.Text ?? string.Empty;

    /// <summary>The as-sent preview's command, or null while it is hidden. Internal for tests.</summary>
    internal string? PreviewText => _preview.IsVisible ? _previewText.Text : null;

    private static bool SameChrome(ComposerModel a, ComposerModel b) =>
        a.Preview == b.Preview && a.Target == b.Target && a.Escaping == b.Escaping && a.Edit == b.Edit && a.Dictionary == b.Dictionary
        && a.CanSend == b.CanSend && a.Armed == b.Armed && a.Keys.SequenceEqual(b.Keys) && a.Verb == b.Verb;

    // ── drawing ──────────────────────────────────────────────────────────────────────────────

    private Color Accent => Parse(_ink.Bar, Color.FromRgb(0x5f, 0xaf, 0xff));

    private Color Warn => Parse(_ink.Low, Color.FromRgb(0xff, 0x6b, 0x6b));

    private Color Dim => Mix(_text, _plane, 0.42);

    /// <summary>The bands' plane: the pane's, one step toward the text, so the editor reads as the page between them.</summary>
    private Color Band => Mix(_plane, _text, _fullScreen ? 0.06 : 0.045);

    private void Paint()
    {
        Background = new SolidColorBrush(_plane);
        _header.Background = new SolidColorBrush(Band);
        _footer.Background = new SolidColorBrush(Band);
        _header.BorderBrush = new SolidColorBrush(Mix(_plane, _text, 0.12));
        _header.BorderThickness = new Thickness(0, 0, 0, 1);
        _footer.BorderBrush = _header.BorderBrush;
        _footer.BorderThickness = new Thickness(0, 1, 0, 0);
        _rule.Background = new SolidColorBrush(Accent);
        _editor.Foreground = new SolidColorBrush(_text);
        _editor.CaretBrush = new SolidColorBrush(Accent);
        _editor.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x70, Accent.R, Accent.G, Accent.B));
        _editor.SelectionForegroundBrush = new SolidColorBrush(_text);
        _editor.MarkColour = Warn;
        _title.Foreground = new SolidColorBrush(Accent);
        _target.Foreground = new SolidColorBrush(_text);
        _count.Foreground = new SolidColorBrush(Dim);
        _preview.Background = new SolidColorBrush(Band);
        _preview.BorderBrush = _header.BorderBrush;
        _preview.BorderThickness = new Thickness(0, 1, 0, 0);
        _previewHeading.Foreground = new SolidColorBrush(Accent);
        _previewText.Foreground = new SolidColorBrush(_text);
        _marks.InvalidateVisual();
    }

    private void ApplyFont()
    {
        var cell = _view.CellExtent.Height > 0 ? _view.CellExtent.Height : 17;
        var ui = Math.Clamp(cell * 0.72, 11, 15);
        _title.FontSize = ui;
        _target.FontSize = ui;
        _count.FontSize = ui - 1;
        _previewHeading.FontSize = ui - 1.5;
        _previewText.FontSize = Math.Clamp(cell * 0.72, 11, 16);
        _previewText.FontFamily = new FontFamily($"{_view.Renderer.Fonts.FamilyName}, {CellFonts.BundledFamily}, DejaVu Sans Mono, Liberation Mono, Consolas, Menlo, monospace");
        _editor.FontSize = Proportional ? Math.Clamp(cell * 0.86, 12, 19) : Math.Clamp(cell * 0.8, 11, 18);
        _editor.FontFamily = Proportional
            ? FontFamily.Default
            : new FontFamily($"{_view.Renderer.Fonts.FamilyName}, {CellFonts.BundledFamily}, DejaVu Sans Mono, Liberation Mono, Consolas, Menlo, monospace");
        if (_model is { } model)
        {
            Label(model);
        }
    }

    /// <summary>The header and the key row, from the model.</summary>
    private void Label(ComposerModel model)
    {
        var ui = _title.FontSize;
        if (model.Edit is { } edit)
        {
            _title.Text = "EDIT";
            _target.Text = $"{edit.Name}   {edit.Type} · from {(model.Target.Length == 0 ? "no connection" : model.Target)}";
        }
        else
        {
            _title.Text = "COMPOSE";
            var verb = !_fullScreen && model.Verb.Length > 0 ? $"{model.Verb}   " : string.Empty;
            _target.Text = model.Target.Length == 0 ? $"{verb}no connection" : $"{verb}to {model.Target}";
        }

        _target.Foreground = new SolidColorBrush(model.Target.Length == 0 ? Warn : _text);

        _chips.Children.Clear();
        if (model.Edit is null)
        {
            _chips.Children.Add(Chip(model.Escaping == ComposeEscaping.Literal ? "literal" : "as typed",
                model.Escaping == ComposeEscaping.Literal ? Accent : _text, ui - 1.5));
            if (_fullScreen || model.Armed)
            {
                _chips.Children.Add(Chip(model.Dictionary ?? "no dictionary", model.Dictionary is null ? Dim : _text, ui - 1.5));
            }
        }

        _preview.IsVisible = _fullScreen && model.Preview && model.Edit is null;
        SyncLength();

        _keys.Children.Clear();
        var narrow = !_fullScreen && Bounds.Width is > 0 and < 560;
        // The idle editor's placeholder says "Click to write a post"; the row's keyless hint saying it again was the
        // panel saying one thing twice.
        var placeholder = !model.Armed && !_fullScreen;
        var placeholderShown = placeholder && model.Body.Length == 0;
        foreach (var key in model.Keys)
        {
            if (placeholderShown && key.Key.Length == 0 && key.Label == ComposerModel.ClickHint)
            {
                continue;
            }

            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 1, 14, 1) };
            if (key.Key.Length > 0)
            {
                item.Children.Add(Chip(NativeKeys.Words(key.Key), Accent, ui - 1.5));
            }

            item.Children.Add(new TextBlock
            {
                Text = NativeKeys.Words(narrow && key.Short is { } brief ? brief : key.Label),
                FontSize = ui - 1,
                Foreground = new SolidColorBrush(Dim),
                VerticalAlignment = VerticalAlignment.Center,
            });
            _keys.Children.Add(item);
        }

        // A docked composer too short for its key row keeps the editor and drops the row.
        _footer.IsVisible = _fullScreen || Bounds.Height is 0 or >= 64;
        _editor.PlaceholderText = placeholder ? ComposerModel.ClickToWrite : null;
        _editor.PlaceholderForeground = new SolidColorBrush(Dim);
    }

    private Border Chip(string words, Color ink, double size) => new()
    {
        Background = new SolidColorBrush(Mix(_plane, ink, 0.14)),
        BorderBrush = new SolidColorBrush(Mix(_plane, ink, 0.32)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(NativeTokens.RadiusS),
        Padding = new Thickness(6, 0, 6, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = words, FontSize = Math.Max(9, size), Foreground = new SolidColorBrush(ink) },
    };

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_model is { } model)
        {
            Label(model);
        }
    }

    // ── text ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The length cell and the preview, from the editor's own text: the model catches up with typing a moment later, and
    /// a count that lagged a keystroke would read as wrong. Counted by <see cref="ComposerModel.SentLength"/>, the cells' rule.
    /// </summary>
    private void SyncLength()
    {
        if (_model is not { } model)
        {
            return;
        }

        var text = _editor.Text ?? string.Empty;
        _count.Text = ComposerModel.CharactersLabel(ComposerModel.SentLength(text, model.Escaping, model.Edit is not null));
        if (_preview.IsVisible)
        {
            var command = ComposeMessage.Build(text.Replace("\r\n", "\n", StringComparison.Ordinal), model.Escaping);
            _previewText.Text = command ?? "nothing yet";
        }
    }

    private void OnTextChanged()
    {
        SyncLength();
        var text = _editor.Text ?? string.Empty;
        if (_session is { } session)
        {
            _editor.Marks = session.Marks(text);
            if (!_applying)
            {
                session.Edited(text);
            }
        }
    }

    private void AskToArm()
    {
        if (!_fullScreen && _model is { Armed: false } && _session is { } session)
        {
            session.Arm();
        }
    }

    // ── keys ─────────────────────────────────────────────────────────────────────────────────

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_session is not { } session || _model is not { } model)
        {
            return;
        }

        var mods = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
        var text = _editor.Text ?? string.Empty;
        var mapped = KeyMapper.FromKeyDown(e.Key, e.KeyModifiers);

        // ⌥⏎ is the window's: it maximises here, so the client's ⌥⏎ newline is ⌃⏎ or ⇧⏎ (and ⏎ itself, in an editor).
        if (mapped is { } chord && WindowHost.Maximise.Chord.Matches(chord))
        {
            e.Handled = true;
            _view.ToggleMaximised();
            return;
        }

        switch (e.Key, mods)
        {
            case (Key.S, KeyModifiers.Control):
                e.Handled = true;
                session.Send(text);
                return;

            case (Key.L, KeyModifiers.Alt):
                e.Handled = true;
                session.ToggleEscaping();
                return;

            case (Key.P, KeyModifiers.Alt):
                e.Handled = true;
                if (_fullScreen)
                {
                    session.TogglePreview();
                }

                return;

            case (Key.W, KeyModifiers.Alt):
                e.Handled = true;
                if (session.CanAddWords && ComposerMarks.WordAt(text, _editor.CaretIndex) is { } word)
                {
                    session.AddWord(word);
                }

                return;

            case (Key.Escape, KeyModifiers.None):
                e.Handled = true;
                if (_fullScreen)
                {
                    session.Close(text);
                }
                else
                {
                    session.Release();
                    _view.Focus();
                }

                return;

            case (Key.Enter, KeyModifiers.Control):
            case (Key.Enter, KeyModifiers.Shift):
                e.Handled = true;
                Type("\n");
                return;

            case (Key.V, KeyModifiers.Control | KeyModifiers.Shift):
                e.Handled = true;
                _editor.Paste();
                return;
        }

        if (_fullScreen)
        {
            // F1 again closes it, as the cell window's F1 does. ⌃Q goes to the client and leaves F1 open: the quit prompt
            // is drawn in front of it (ModalPresenter), so Stay comes back to the post.
            if (e.Key == Key.F1 && mods == KeyModifiers.None)
            {
                e.Handled = true;
                session.Close(text);
            }
            else if (e.Key == Key.Q && mods == KeyModifiers.Control && mapped is { } quit)
            {
                e.Handled = true;
                _view.Driver.RaiseKey(quit);
            }

            return;
        }

        if (mapped is { } forward && ReachesTheClient(e.Key, mods))
        {
            e.Handled = true;
            _view.Driver.RaiseKey(forward);
        }
    }

    /// <summary>
    /// Whether a docked editor hands a key on to the client: the function keys, and every Ctrl or Alt chord the editor
    /// has no use for of its own — so ⌃P, ⌃O, ⌃B, ⌃↑/⌃↓, ⌃←/⌃→, ⌥J/⌥K and ⌥1–⌥9 do from here what they do from the
    /// cells. Typing, the caret keys, selection, ⌃A/⌃C/⌃X/⌃V/⌃Z/⌃Y and word deletion stay the editor's.
    /// </summary>
    internal static bool ReachesTheClient(Key key, KeyModifiers mods)
    {
        if (key is >= Key.F1 and <= Key.F24)
        {
            return true;
        }

        var ctrl = (mods & KeyModifiers.Control) != 0;
        var alt = (mods & KeyModifiers.Alt) != 0;
        var shift = (mods & KeyModifiers.Shift) != 0;
        if (alt && !ctrl)
        {
            // ⌥←/→ are word movement in the editor, as on the command line.
            return key is not (Key.Left or Key.Right or Key.Back or Key.Delete);
        }

        if (!ctrl || alt)
        {
            return false;
        }

        if (shift)
        {
            // ⌃⇧←/→ extend by a word; ⌃⇧Z is redo.
            return key is not (Key.Left or Key.Right or Key.Home or Key.End or Key.Z);
        }

        return key is not (Key.A or Key.C or Key.X or Key.V or Key.Z or Key.Y or Key.Home or Key.End
            or Key.Back or Key.Delete or Key.Insert);
    }

    /// <summary>Types text at the caret through the editor's own input path, so it is one undo step like any typing.</summary>
    private void Type(string text) => _editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = TextInputEvent, Text = text });

    // ── the right-click menu ─────────────────────────────────────────────────────────────────

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        var at = e.TryGetPosition(_editor, out var point) ? _editor.OffsetAt(point) : _editor.CaretIndex;
        OpenMenu(at ?? _editor.CaretIndex);
    }

    /// <summary>
    /// Opens the editor's menu for the text at <paramref name="offset"/>: the dictionary's suggestions and Add to
    /// dictionary when a marked word is there, then the editor's own edits and the face. Internal for tests.
    /// </summary>
    internal ContextMenu OpenMenu(int offset)
    {
        var text = _editor.Text ?? string.Empty;
        var items = new List<Control>();
        var mark = _editor.Marks.FirstOrDefault(m => offset >= m.Start && offset <= m.Start + m.Length);
        if (_session is { } session && mark.Length > 0 && mark.Start + mark.Length <= text.Length)
        {
            var word = text.Substring(mark.Start, mark.Length);
            var suggestions = session.Suggestions(word);
            foreach (var suggestion in suggestions)
            {
                var replacement = suggestion;
                items.Add(Item(replacement, () => Replace(mark, replacement), bold: true));
            }

            if (suggestions.Count == 0)
            {
                items.Add(new MenuItem { Header = "No suggestions", IsEnabled = false });
            }

            if (session.CanAddWords)
            {
                items.Add(Item($"Add “{word}” to dictionary", () => session.AddWord(word)));
            }

            items.Add(new Separator());
        }

        items.Add(Item("Undo", _editor.Undo, enabled: _editor.CanUndo));
        items.Add(Item("Redo", _editor.Redo, enabled: _editor.CanRedo));
        items.Add(new Separator());
        items.Add(Item("Cut", _editor.Cut, enabled: _editor.CanCut));
        items.Add(Item("Copy", _editor.Copy, enabled: _editor.CanCopy));
        items.Add(Item("Paste", _editor.Paste, enabled: _editor.CanPaste));
        items.Add(Item("Select all", _editor.SelectAll));
        items.Add(new Separator());
        items.Add(Item(Proportional ? "Monospace font" : "Proportional font", () => ChooseFace(!Proportional)));

        var menu = new ContextMenu { ItemsSource = items };
        menu.Closed += (_, _) => _editor.Focus();
        OpenedMenu = menu;
        menu.Open(_editor);
        return menu;
    }

    /// <summary>The menu's face entry: the character keeps it (through the session), and every composer re-reads its own.</summary>
    internal void ChooseFace(bool proportional)
    {
        _faceChosen = proportional;
        _session?.ChooseFace(proportional);
        FaceChanged?.Invoke();
        ApplyFont();
    }

    private static MenuItem Item(string header, Action act, bool enabled = true, bool bold = false)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (bold)
        {
            item.FontWeight = FontWeight.SemiBold;
        }

        item.Click += (_, _) => act();
        return item;
    }

    /// <summary>Replaces a marked word with a suggestion, through the editor's input path so it can be undone.</summary>
    private void Replace(SpellRange mark, string replacement)
    {
        _editor.SelectionStart = mark.Start;
        _editor.SelectionEnd = mark.Start + mark.Length;
        Type(replacement);
    }

    // ── colours ──────────────────────────────────────────────────────────────────────────────

    internal static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + ((b.R - a.R) * t)),
        (byte)Math.Round(a.G + ((b.G - a.G) * t)),
        (byte)Math.Round(a.B + ((b.B - a.B) * t)));

    private static Color Parse(string hex, Color fallback) => Color.TryParse(hex, out var colour) ? colour : fallback;

    /// <summary>
    /// The wavy lines under the misspelt words, drawn over the editor: the editor's own template paints its background
    /// and text after anything the editor draws itself. It takes no input.
    /// </summary>
    internal sealed class MarkLayer : Control
    {
        private readonly ComposerEditor _editor;

        public MarkLayer(ComposerEditor editor)
        {
            _editor = editor;
            IsHitTestVisible = false;
            Focusable = false;
            ClipToBounds = true;
            editor.MarksMoved += InvalidateVisual;
        }

        public override void Render(DrawingContext context) => _editor.DrawMarks(context, this);
    }
}
