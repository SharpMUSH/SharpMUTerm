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
internal sealed class ComposerView : Border, IEdgeRules
{
    /// <summary>The docked heading's rule: every panel's, its border's last device pixel.</summary>
    public IEnumerable<EdgeRule> EdgeRules(Size size)
    {
        if (!_fullScreen && _header.IsVisible)
        {
            yield return PanelText.HeadingRule(this, size.Width, _plane, _text);
        }
    }

    private readonly TerminalView _view;
    private (double Height, double TextSize) _fontMetrics;
    private readonly bool _fullScreen;
    private readonly ComposerEditor _editor;
    private readonly MarkLayer _marks;
    private readonly LineGutter _gutter;
    private readonly Border _header;
    private readonly TextBlock _title;

    /// <summary>
    /// "Click to write a post", in the UI face over the empty idle editor. The editor's own placeholder would be in the
    /// editor's face, the post's monospace; a hint is the panel speaking, not the post. Never hit: a click goes to the editor.
    /// </summary>
    private readonly TextBlock _placeholder;

    private bool _placeholderWanted;
    private readonly TextBlock _target;
    private readonly StackPanel _chips;
    private readonly TextBlock _count;
    private readonly Border _preview;
    private readonly TextBlock _previewHeading;
    private readonly TextBlock _previewText;
    private readonly Border _footer;
    private readonly WrapPanel _keys;
    private readonly Border _rule;

    // The command field above the text (ComposerModel.Verb): a one-line box, its label, and the text's label.
    private readonly TextBox _command;
    private readonly Border _commandWell;
    private readonly TextBlock _commandLabel;
    private readonly TextBlock _textLabel;
    private readonly Control _commandRow;

    // The docked panel's own parts: the editor's framed well, and the footer's summary, Preview and Send.
    private readonly Border? _well;
    private readonly FittedText _summary = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _previewButton;
    private readonly Border _sendButton;
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
            Padding = fullScreen ? new Thickness(NativeTokens.Space24, NativeTokens.Space14) : new Thickness(NativeTokens.Space10, NativeTokens.Space8),
        };
        AutomationProperties.SetName(_editor, fullScreen ? "Compose a post" : "Docked composer");
        _editor.TextChanged += (_, _) => OnTextChanged();
        _editor.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        _editor.AddHandler(PointerPressedEvent, (_, _) => AskToArm(), RoutingStrategies.Tunnel, handledEventsToo: true);
        _editor.GotFocus += (_, _) => AskToArm();
        _editor.ContextRequested += OnContextRequested;
        _command = new TextBox
        {
            AcceptsReturn = false,
            AcceptsTab = false,
            Background = Brushes.Transparent,
            BorderThickness = default,
            Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space6),
            MinHeight = 0,
            VerticalContentAlignment = VerticalAlignment.Center,
            PlaceholderText = ComposerModel.CommandPlaceholder,
            MaxLength = 200,
        };
        AutomationProperties.SetName(_command, "Command");
        ToolTip.SetTip(_command, "The command the text is sent with: +pose, say, +bbpost 3= (Tab moves to the text)");
        _command.TextChanged += (_, _) => OnCommandChanged();
        _command.AddHandler(KeyDownEvent, OnCommandKeyDown, RoutingStrategies.Tunnel);
        _command.AddHandler(PointerPressedEvent, (_, _) => AskToArm(), RoutingStrategies.Tunnel, handledEventsToo: true);
        _command.GotFocus += (_, _) => AskToArm();
        _commandWell = new Border
        {
            Child = _command,
            CornerRadius = new CornerRadius(NativeTokens.RadiusControl),
            BorderThickness = new Thickness(NativeTokens.Hairline),
            ClipToBounds = true,
        };
        _commandLabel = new TextBlock { Text = ComposerModel.CommandLabel, VerticalAlignment = VerticalAlignment.Center };
        _textLabel = new TextBlock { Text = ComposerModel.TextLabel };
        if (fullScreen)
        {
            // F1: one row under the header, the label beside the box.
            var row = new DockPanel { LastChildFill = true };
            _commandLabel.Margin = new Thickness(0, 0, NativeTokens.Space10, 0);
            DockPanel.SetDock(_commandLabel, Dock.Left);
            row.Children.Add(_commandLabel);
            row.Children.Add(_commandWell);
            _commandRow = new Border { Child = row, Padding = new Thickness(NativeTokens.Space24, NativeTokens.Space8) };
        }
        else
        {
            // Docked, as the mockup's panel: Command over its box, then Text over the editor's well.
            _commandLabel.Margin = new Thickness(PanelText.Pad, NativeTokens.Space12, PanelText.Pad, 0);
            _commandWell.Margin = new Thickness(PanelText.Pad, NativeTokens.Space6, PanelText.Pad, 0);
            // The mockup's box: 8 px above and below the line, so it is as tall as theirs (34 px).
            _command.Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space8);
            _commandRow = new StackPanel { Children = { _commandLabel, _commandWell } };
            _textLabel.Margin = new Thickness(PanelText.Pad, NativeTokens.Space12, PanelText.Pad, 0);
        }

        _marks = new MarkLayer(_editor);
        _gutter = new LineGutter(_editor) { IsVisible = false };

        _title = new TextBlock { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, NativeTokens.Space8, 0) };
        _target = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space6, VerticalAlignment = VerticalAlignment.Center };
        _count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NativeTokens.Space10, 0, 0, 0) };
        var headerRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_title, Dock.Left);
        DockPanel.SetDock(_count, Dock.Right);
        DockPanel.SetDock(_chips, Dock.Right);
        headerRow.Children.Add(_title);
        headerRow.Children.Add(_count);
        headerRow.Children.Add(_chips);
        headerRow.Children.Add(_target);
        // The docked panel's heading is every panel's (PanelText.Heading): 16 px in, a rule under it, the pop-out button
        // clear at the right.
        _header = new Border { Child = headerRow, Padding = fullScreen ? new Thickness(NativeTokens.Space24, NativeTokens.Space10) : new Thickness(PanelText.Pad, 0, NativeTokens.Space32, 0) };
        if (!fullScreen)
        {
            // Every panel's heading height, so the composer's rule meets the one beside it (who is here, the map).
            _header.Height = PanelText.PanelHeadingHeight;
            headerRow.VerticalAlignment = VerticalAlignment.Center;
        }

        _keys = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // The docked footer: how long the post is, then Preview (F1 with its as-sent band) and Send, each the session's.
        _previewButton = ActionButton("Preview", "The post as it will be sent, in F1", () => _session?.TogglePreview());
        _previewButton.Name = "Preview";
        _sendButton = ActionButton("Send  Ctrl+S", "Send the post (Ctrl+S)", () => _session?.Send(_editor.Text ?? string.Empty));
        _sendButton.Name = "Send";
        var dockedFooter = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_sendButton, Dock.Right);
        DockPanel.SetDock(_previewButton, Dock.Right);
        _previewButton.Margin = new Thickness(NativeTokens.Space8, 0, NativeTokens.Space8, 0);
        dockedFooter.Children.Add(_sendButton);
        dockedFooter.Children.Add(_previewButton);
        dockedFooter.Children.Add(_summary);
        _footer = new Border
        {
            Child = fullScreen ? _keys : dockedFooter,
            Padding = fullScreen ? new Thickness(NativeTokens.Space24, NativeTokens.Space8) : new Thickness(PanelText.Pad, NativeTokens.Space10, PanelText.Pad, NativeTokens.Space14),
        };

        // F1's as-sent preview (⌥P): the one command ⌃S sends, in the editor's face, under the editor.
        _previewHeading = new TextBlock { Text = "AS SENT", FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, NativeTokens.Space4) };
        _previewText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis };
        _preview = new Border
        {
            Child = new StackPanel { Children = { _previewHeading, _previewText } },
            Padding = new Thickness(NativeTokens.Space24, NativeTokens.Space8),
            IsVisible = false,
        };
        AutomationProperties.SetName(_preview, "As sent");

        // The armed rule down the docked editor's left edge: the cell composer's ▌ marker, in the accent.
        _rule = new Border { Width = NativeTokens.MarkWidth, HorizontalAlignment = HorizontalAlignment.Left };

        _placeholder = new TextBlock
        {
            Text = ComposerModel.ClickToWrite,
            FontFamily = NativeTokens.UiFont,
            IsHitTestVisible = false,
            IsVisible = false,
            Margin = _editor.Padding + new Thickness(NativeTokens.Hairline),
        };

        var text = new Panel();
        text.Children.Add(_editor);
        text.Children.Add(_placeholder);
        text.Children.Add(_marks);
        text.Children.Add(_rule);

        // A script's lines are numbered in a gutter beside the editor, the cells' line numbers; the error line is marked there.
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Control page = text;
        if (!fullScreen)
        {
            // Docked, the editor is a framed well inside the panel, as the mockup's Text field is.
            _well = new Border
            {
                Child = text,
                Margin = new Thickness(PanelText.Pad, NativeTokens.Space6, PanelText.Pad, 0),
                CornerRadius = new CornerRadius(NativeTokens.RadiusControl),
                BorderThickness = new Thickness(NativeTokens.Hairline),
                ClipToBounds = true,
            };
            page = _well;
        }

        Grid.SetColumn(_gutter, 0);
        Grid.SetColumn(page, 1);
        body.Children.Add(_gutter);
        body.Children.Add(page);

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto,Auto") };
        Grid.SetRow(_header, 0);
        Grid.SetRow(_commandRow, 1);
        Grid.SetRow(_textLabel, 2);
        Grid.SetRow(body, 3);
        Grid.SetRow(_preview, 4);
        Grid.SetRow(_footer, 5);
        _textLabel.IsVisible = !fullScreen;
        grid.Children.Add(_header);
        grid.Children.Add(_commandRow);
        grid.Children.Add(_textLabel);
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
    internal string FooterText => !_fullScreen
        ? string.Join(" · ", new[] { _summary.Text, Words(_previewButton.Child!), Words(_sendButton.Child!) }.Where(t => !string.IsNullOrEmpty(t)))
        : string.Join(" · ", _keys.Children.OfType<StackPanel>()
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
        _view.Repainted += RefreshFontMetrics;
        RefreshFontMetrics();
        if (_model is { Armed: true })
        {
            Dispatcher.UIThread.Post(() => _editor.Focus(), DispatcherPriority.Input);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FaceChanged -= ApplyFont;
        _view.Repainted -= RefreshFontMetrics;
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
            if (!string.Equals(ComposeMessage.CleanCommand(_command.Text), model.Verb, StringComparison.Ordinal))
            {
                _applying = true;
                try
                {
                    _command.Text = model.Verb;
                    _command.CaretIndex = model.Verb.Length;
                }
                finally
                {
                    _applying = false;
                }
            }

            if (!string.Equals(_editor.Text ?? string.Empty, model.Body, StringComparison.Ordinal))
            {
                _applying = true;
                try
                {
                    _editor.Text = model.Body;
                    _editor.CaretIndex = model.Edit is { Line: > 0 } at ? LineStart(model.Body, at.Line) : model.Body.Length;
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

        // A package's script is only read: the editor shows it and keeps every edit out.
        _editor.IsReadOnly = model.Edit?.ReadOnly == true;

        // A script: its lines numbered, and the line the last load error named marked in the gutter and behind the text,
        // from the model's one field the cell editor marks too.
        _gutter.IsVisible = model.Edit is { Script: true };
        _editor.ErrorLine = model.Edit is { Script: true, ErrorLine: > 0 } failed ? failed.ErrorLine : 0;

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
            else if (was == true && (_editor.IsKeyboardFocusWithin || _command.IsKeyboardFocusWithin))
            {
                _view.Focus();
            }
        }
    }

    /// <summary>The gutter of line numbers, shown for a script. Internal for tests.</summary>
    internal LineGutter Gutter => _gutter;

    /// <summary>The command field. Internal for tests.</summary>
    internal TextBox CommandBox => _command;

    /// <summary>Whether the command field is shown (a post) and what it holds, for a test.</summary>
    internal string? CommandText => _commandRow.IsVisible ? _command.Text ?? string.Empty : null;

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

    /// <summary>The docked editor's well: a step deeper than the panel on a dark plane, lighter on a light one.</summary>
    private Color Well => Luma(_plane) < 0.5 ? Mix(_plane, Colors.Black, 0.25) : Mix(_plane, Colors.White, 0.7);

    private static double Luma(Color c) => ((0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B)) / 255;

    private void Paint()
    {
        Background = new SolidColorBrush(_plane);
        // Docked, the header and footer are the panel's own plane, which the view already paints: transparent rather than
        // the same colour again, so a translucent plane (F7's background transparency) is not drawn twice there.
        _header.Background = _fullScreen ? new SolidColorBrush(Band) : Brushes.Transparent;
        _footer.Background = _fullScreen ? new SolidColorBrush(Band) : Brushes.Transparent;
        _header.BorderBrush = new SolidColorBrush(_fullScreen ? Mix(_plane, _text, 0.12) : PanelText.Rule(_text, _plane));
        _header.BorderThickness = new Thickness(0, 0, 0, NativeTokens.Hairline);
        _footer.BorderBrush = _header.BorderBrush;
        _footer.BorderThickness = _fullScreen ? new Thickness(0, NativeTokens.Hairline, 0, 0) : default;
        var tokens = NativeTokens.For(null, Luma(_plane) < 0.5);
        tokens.ApplyFocusRing(this);
        if (_well is not null)
        {
            // The mockup's Text field: the well's ground, ringed in the focus edge, as the field the post is written in.
            _well.Background = new SolidColorBrush(Well);
            _well.BorderBrush = new SolidColorBrush(tokens.Focus);
        }

        // The Command field: the same ground, ringed in the quieter edge.
        _commandWell.Background = new SolidColorBrush(_fullScreen ? Mix(_plane, _text, 0.05) : Well);
        _commandWell.BorderBrush = new SolidColorBrush(tokens.Edge);
        _command.Foreground = new SolidColorBrush(_text);
        _command.CaretBrush = new SolidColorBrush(Accent);
        _command.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x70, Accent.R, Accent.G, Accent.B));
        _command.SelectionForegroundBrush = new SolidColorBrush(_text);
        _commandLabel.Foreground = new SolidColorBrush(Dim);
        _textLabel.Foreground = new SolidColorBrush(Dim);

        _summary.Foreground = new SolidColorBrush(Dim);
        PaintButton(_previewButton, accent: false);
        PaintButton(_sendButton, accent: true);
        _rule.Background = new SolidColorBrush(Accent);
        _editor.Foreground = new SolidColorBrush(_text);
        _editor.CaretBrush = new SolidColorBrush(Accent);
        _editor.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x70, Accent.R, Accent.G, Accent.B));
        _editor.SelectionForegroundBrush = new SolidColorBrush(_text);
        _editor.MarkColour = Warn;
        _editor.ErrorColour = Warn;
        _gutter.Ink = Dim;
        _gutter.Warn = Warn;
        _gutter.Background = new SolidColorBrush(Band);
        _gutter.InvalidateVisual();
        _title.Foreground = new SolidColorBrush(_text);
        _target.Foreground = new SolidColorBrush(Dim);
        _count.Foreground = new SolidColorBrush(Dim);
        _preview.Background = new SolidColorBrush(Band);
        _preview.BorderBrush = _header.BorderBrush;
        _preview.BorderThickness = new Thickness(0, NativeTokens.Hairline, 0, 0);
        _previewHeading.Foreground = new SolidColorBrush(Accent);
        _previewText.Foreground = new SolidColorBrush(_text);
        _marks.InvalidateVisual();
    }

    /// <summary>Refreshes an open composer after zoom without changing its selected face.</summary>
    private void RefreshFontMetrics()
    {
        if (_fontMetrics != (_view.CellExtent.Height, _view.CellTextSize))
        {
            ApplyFont();
        }
    }

    private void ApplyFont()
    {
        _fontMetrics = (_view.CellExtent.Height, _view.CellTextSize);
        var cell = _view.CellExtent.Height > 0 ? _view.CellExtent.Height : 17;
        var ui = Math.Clamp(cell * 0.72, 11, 15);
        _title.FontSize = _fullScreen ? ui : PanelText.Size;
        _target.FontSize = _fullScreen ? ui : PanelText.Size;
        _count.FontSize = ui - 1;
        _summary.FontSize = PanelText.Size - 1;
        _commandLabel.FontSize = _fullScreen ? ui - 1 : PanelText.Size - 1;
        _textLabel.FontSize = PanelText.Size - 1;
        // Game text in the cells' own face and size (the mockups' 13 px JetBrains Mono beside the output's), not a share
        // of the row: a row carries the line height's leading, which made the text a size larger than the output's.
        var text = _view.CellTextSize > 0 ? _view.CellTextSize : cell * 0.8;
        _command.FontSize = Math.Clamp(text, 11, 18);
        _command.FontFamily = NativeTokens.CellFace(_view.Renderer.Fonts.FamilyName);
        foreach (var button in new[] { _previewButton, _sendButton })
        {
            if (button.Child is TextBlock words)
            {
                words.FontSize = PanelText.Size - 1;
            }
        }
        _previewHeading.FontSize = ui - 1.5;
        _previewText.FontSize = Math.Clamp(cell * 0.72, 11, 16);
        _previewText.FontFamily = NativeTokens.CellFace(_view.Renderer.Fonts.FamilyName);
        _editor.FontSize = Proportional ? Math.Clamp(text * 1.08, 12, 19) : Math.Clamp(text, 11, 18);

        // Rows as far apart as the output's (the mockups' 20 px line), so a post reads at the pitch it will be read at.
        _editor.LineHeight = Proportional ? double.NaN : cell;
        _editor.FontFamily = Proportional
            ? NativeTokens.UiFont
            : NativeTokens.CellFace(_view.Renderer.Fonts.FamilyName);
        if (_model is { } model)
        {
            Label(model);
        }
    }

    /// <summary>Where line <paramref name="line"/> (from 1) of <paramref name="text"/> starts; the end when it has fewer.</summary>
    internal static int LineStart(string text, int line)
    {
        var at = 0;
        for (var i = 1; i < line; i++)
        {
            var next = text.IndexOf('\n', at);
            if (next < 0)
            {
                return text.Length;
            }

            at = next + 1;
        }

        return at;
    }

    /// <summary>The header and the key row, from the model.</summary>
    private void Label(ComposerModel model)
    {
        var ui = _title.FontSize;
        if (model.Edit is { Script: true } script)
        {
            // A script: its name, that a save reloads the character's scripts, and what the last save did.
            _title.Text = script.ReadOnly ? "Read only" : "Script";
            var whose = model.Target.Length == 0 ? "no connection" : model.Target;
            _target.Text = $"{script.Name}   lua · {(script.ReadOnly ? "a package's script" : $"reloads {whose}")}"
                + (script.Status is { } status ? $" · {status.TrimStart('!', ' ')}" : string.Empty);
        }
        else if (model.Edit is { } edit)
        {
            _title.Text = "Edit";
            _target.Text = $"{edit.Name}   {edit.Type} · from {(model.Target.Length == 0 ? "no connection" : model.Target)}";
        }
        else if (!_fullScreen)
        {
            // The panel heading: Compose for Corvid.
            var heading = SharpMUTerm.Client.Panels.PanelHeading.For(model);
            _title.Text = heading.Title;
            _target.Text = heading.Context ?? string.Empty;
        }
        else
        {
            _title.Text = "Compose";
            _target.Text = model.Target.Length == 0 ? "no connection" : $"to {model.Target}";
        }

        _target.Foreground = new SolidColorBrush(
            model.Target.Length == 0 || model.Edit?.Status?.StartsWith('!') == true ? Warn : Dim);

        _chips.Children.Clear();
        if (model.Edit is null && !_fullScreen)
        {
            // The escaping, as a chip that switches it (⌥L): Literal or As typed.
            var literal = model.Escaping == ComposeEscaping.Literal;
            var escaping = OutlinedChip(literal ? "Literal" : "As typed", literal ? Accent : Dim);
            escaping.Name = "Escaping";
            escaping.Cursor = new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(escaping, literal ? "Sent as typed, % [ ] escaped (Alt+L switches)" : "Sent exactly as typed (Alt+L switches)");
            escaping.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(escaping).Properties.IsLeftButtonPressed)
                {
                    e.Handled = true;
                    _session?.ToggleEscaping();
                }
            };
            _chips.Children.Add(escaping);
            if (model.Armed && model.Dictionary is { } named)
            {
                _chips.Children.Add(OutlinedChip(named, Dim));
            }

            _count.IsVisible = false;
            _sendButton.Opacity = model.CanSend ? 1 : 0.55;
        }
        else if (model.Edit is null)
        {
            _chips.Children.Add(Chip(model.Escaping == ComposeEscaping.Literal ? "literal" : "as typed",
                model.Escaping == ComposeEscaping.Literal ? Accent : _text, ui - 1.5));
            if (_fullScreen || model.Armed)
            {
                _chips.Children.Add(model.Dictionary is { } dictionary
                    ? Chip(dictionary, _text, ui - 1.5)
                    : DictionaryChip(ui - 1.5));
            }
        }

        _preview.IsVisible = _fullScreen && model.Preview && model.Edit is null;

        // The command field and the Text label: a post's, and in a docked pane only while there is room for them over an
        // editor worth writing in — the heading, both labels, the field, two lines of text and the footer are about 232 px.
        var roomForCommand = _fullScreen || Bounds.Height is 0 or >= 232;
        _commandRow.IsVisible = model.HasCommandField && roomForCommand;
        _textLabel.IsVisible = !_fullScreen && model.HasCommandField && roomForCommand;
        SyncLength();

        _keys.Children.Clear();
        if (!_fullScreen)
        {
            // A docked composer too short for its footer keeps the editor and drops the footer: the heading, the footer
            // and an editor of two lines need about 136 px, and below that the footer would squeeze the well shut.
            _footer.IsVisible = Bounds.Height is 0 or >= 136;
            ShowPlaceholder(!model.Armed);
            return;
        }

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

            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space4, Margin = new Thickness(0, NativeTokens.Hairline, NativeTokens.Space14, NativeTokens.Hairline) };
            if (key.Key.Length > 0)
            {
                // A key cap, the one every native list draws (⌃P, help, the modals), not a tinted word.
                item.Children.Add(ModalParts.Chip(NativeKeys.Words(key.Key), NativeTokens.For(null, Luma(_plane) < 0.5)));
            }

            item.Children.Add(new TextBlock
            {
                Text = NativeKeys.Words(key.Label),
                FontSize = ui - 1,
                Foreground = new SolidColorBrush(Dim),
                VerticalAlignment = VerticalAlignment.Center,
            });
            _keys.Children.Add(item);
        }

        // A docked composer too short for its key row keeps the editor and drops the row.
        _footer.IsVisible = _fullScreen || Bounds.Height is 0 or >= 64;
        ShowPlaceholder(placeholder);
    }

    /// <summary>The UI-face placeholder (<see cref="_placeholder"/>): wanted while idle, shown while the editor is empty.</summary>
    private void ShowPlaceholder(bool wanted)
    {
        _placeholderWanted = wanted;
        _placeholder.Foreground = new SolidColorBrush(Dim);
        _placeholder.FontSize = Math.Max(NativeTokens.Body, _editor.FontSize - 1);
        SyncPlaceholder();
    }

    private void SyncPlaceholder() => _placeholder.IsVisible = _placeholderWanted && string.IsNullOrEmpty(_editor.Text);

    /// <summary>What the idle editor says while empty, or null. Internal for tests.</summary>
    internal string? Placeholder => _placeholder.IsVisible ? _placeholder.Text : null;

    /// <summary>A header chip (escaping, dictionary): the docked heading's outline, so F1 and the panel wear one chip.</summary>
    private Border Chip(string words, Color ink, double size) => OutlinedChip(words, ink);

    /// <summary>
    /// The docked heading's chip, as the mockup's <c>Literal</c>: 11 px words in a hairline outline of the panel's rule,
    /// rounded as a control, on the panel's own plane.
    /// </summary>
    private Border OutlinedChip(string words, Color ink) => new()
    {
        Background = Brushes.Transparent,
        BorderBrush = new SolidColorBrush(PanelText.Rule(_text, _plane)),
        BorderThickness = new Thickness(NativeTokens.Hairline),
        CornerRadius = new CornerRadius(NativeTokens.RadiusControl),
        Padding = new Thickness(NativeTokens.Space6, NativeTokens.Space2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = words, FontSize = NativeTokens.Small, Foreground = new SolidColorBrush(ink) },
    };

    /// <summary>What the header's dictionary chip says while no dictionary marks the post.</summary>
    internal const string NoDictionaryChip = "no dictionary · get one";

    /// <summary>
    /// The header's chip while no dictionary marks the post: a button for the offer ⌥W opens
    /// (<see cref="ComposerSession.GetDictionary"/>), so the state names its own way out. Where the session cannot offer
    /// one (the docked panel, a MOO edit) it is the plain dim chip it always was. Internal for tests.
    /// </summary>
    internal Border DictionaryChip(double size)
    {
        if (_session is not { CanGetDictionary: true })
        {
            return Chip("no dictionary", Dim, size);
        }

        var chip = Chip(NoDictionaryChip, Accent, size);
        chip.Name = "GetDictionary";
        chip.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(chip, "Get a spelling dictionary (Alt+W)");
        AutomationProperties.SetName(chip, "Get a spelling dictionary");
        chip.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                _session?.GetDictionary();
            }
        };
        return chip;
    }

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
        var verb = model.HasCommandField ? _command.Text : null;
        _count.Text = ComposerModel.CharactersLabel(ComposerModel.SentLength(verb, text, model.Escaping, model.Edit is not null));
        if (!_fullScreen)
        {
            // 2 paragraphs · 31 words, the mockup's footer, with the length as sent in its tooltip (the chars count would
            // elide the words in a 380 px column); before anything is written, the keys that move in and out of it.
            // In the longest wording the footer holds (FittedText): the words alone where the paragraphs do not fit.
            var summary = ComposerModel.Summary(text);
            _summary.Forms = summary.Length > 0
                ? ComposerModel.SummaryForms(text)
                : model.Armed
                    ? [NativeKeys.Words("Esc command line · F1 full screen"), NativeKeys.Words("F1 full screen")]
                    : [NativeKeys.Words("F1 full screen")];
            ToolTip.SetTip(_summary, summary.Length > 0 ? $"{_count.Text} as sent" : null);
        }
        if (_preview.IsVisible)
        {
            var command = ComposeMessage.Build(verb, text.Replace("\r\n", "\n", StringComparison.Ordinal), model.Escaping);
            _previewText.Text = command ?? "nothing yet";
        }
    }

    private void OnTextChanged()
    {
        SyncLength();
        SyncPlaceholder();
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

    /// <summary>The command field changed: the length cell follows, and the client is told unless it wrote it.</summary>
    private void OnCommandChanged()
    {
        SyncLength();
        if (_session is { } session && !_applying)
        {
            session.EditedCommand(_command.Text ?? string.Empty);
        }
    }

    /// <summary>
    /// A key in the command field: ⏎ and ⇥ move on to the text (a command is one line), and every composer chord — ⌃S,
    /// ⌥L, ⌥P, Esc, the keys a docked editor hands the client — means what it means in the text.
    /// </summary>
    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        var mods = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift);
        if ((e.Key == Key.Enter && mods == KeyModifiers.None) || (e.Key == Key.Tab && mods == KeyModifiers.None))
        {
            e.Handled = true;
            _editor.Focus();
            return;
        }

        if (e.Key == Key.W && mods == KeyModifiers.Alt)
        {
            // ⌥W is a word of the text; in the command field it does nothing.
            e.Handled = true;
            return;
        }

        OnEditorKeyDown(sender, e);
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

        // F11 and ⇧F11 are the window's too: full screen and windowed full screen.
        if (mapped is { } screen && TerminalView.WindowModeFor(screen) is { } windowMode)
        {
            e.Handled = true;
            _view.ToggleMode(windowMode);
            return;
        }

        switch (e.Key, mods)
        {
            case (Key.S, KeyModifiers.Control):
                e.Handled = true;
                session.Send(text);
                return;

            case (Key.Tab, KeyModifiers.Shift) when model.HasCommandField && ReferenceEquals(sender, _editor):
                // ⇧⇥ from the text back to the command field; ⇥ from the command field is its own handler's.
                e.Handled = true;
                _command.Focus();
                return;

            case (Key.Tab, KeyModifiers.None) when model.HasCommandField && ReferenceEquals(sender, _editor):
                e.Handled = true;
                _command.Focus();
                _command.SelectAll();
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
                else if (session.CanGetDictionary)
                {
                    session.GetDictionary();
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
        if (_model?.Edit is not { Script: true })
        {
            // A script is code, and stays monospace.
            items.Add(new Separator());
            items.Add(Item(Proportional ? "Monospace font" : "Proportional font", () => ChooseFace(!Proportional)));
        }

        var menu = new ContextMenu { ItemsSource = items };
        menu.Closed += (_, _) => _editor.Focus();
        NativeMenu.KeepWhole(menu, _editor);
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

    /// <summary>A footer button drawn as a plate, as the panels' are: a click runs <paramref name="click"/>.</summary>
    private static Border ActionButton(string words, string tip, Action click)
    {
        var button = new Border
        {
            Child = new TextBlock { Text = words, VerticalAlignment = VerticalAlignment.Center },
            Padding = new Thickness(NativeTokens.Space10, 0),
            Height = NativeTokens.ControlHeightSmall,
            CornerRadius = new CornerRadius(NativeTokens.RadiusControl),
            BorderThickness = new Thickness(NativeTokens.Hairline),
            Cursor = new Cursor(StandardCursorType.Hand),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, words);
        button.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                click();
            }
        };
        return button;
    }

    private void PaintButton(Border button, bool accent)
    {
        button.Background = new SolidColorBrush(accent ? Accent : Mix(_plane, _text, 0.07));
        button.BorderBrush = new SolidColorBrush(accent ? Accent : Mix(_plane, _text, 0.18));
        if (button.Child is TextBlock words)
        {
            words.Foreground = new SolidColorBrush(accent ? (Luma(Accent) > 0.5 ? Mix(Accent, Colors.Black, 0.85) : Colors.White) : _text);
            words.FontWeight = accent ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }

    /// <summary>Presses the docked footer's Preview or Send button, as a click on it does. Internal for tests.</summary>
    internal void PressFooter(string name)
    {
        if (name == "Send")
        {
            _session?.Send(_editor.Text ?? string.Empty);
        }
        else
        {
            _session?.TogglePreview();
        }
    }

    /// <summary>The docked footer's summary as drawn. Internal for tests.</summary>
    internal string SummaryText => _summary.Text ?? string.Empty;

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

    /// <summary>
    /// A script's line numbers, beside the editor and level with each source line's first row (read off the editor's own
    /// layout, so a wrapped line is numbered once and scrolling moves the numbers): the window's form of the cell editor's
    /// numbered gutter. The line <see cref="ComposerEditor.ErrorLine"/> names is drawn in the warning ink with a bar at the
    /// edge. It takes no input.
    /// </summary>
    internal sealed class LineGutter : Control
    {
        private readonly ComposerEditor _editor;

        public LineGutter(ComposerEditor editor)
        {
            _editor = editor;
            IsHitTestVisible = false;
            Focusable = false;
            ClipToBounds = true;
            // Repainted whenever the text moves (the editor's layout, a scroll); re-measured only when the width it needs
            // changed — a digit more, another face or size — because the editor's own layout pass raises MarksMoved, and a
            // gutter that re-measured on every one would never let layout settle.
            editor.MarksMoved += () =>
            {
                if (_measured != (Digits, _editor.FontSize, _editor.FontFamily))
                {
                    InvalidateMeasure();
                }

                InvalidateVisual();
            };
        }

        private (int Digits, double Size, FontFamily Face) _measured;

        /// <summary>The numbers' ink.</summary>
        public Color Ink { get; set; } = Colors.Gray;

        /// <summary>The error line's ink.</summary>
        public Color Warn { get; set; } = Colors.IndianRed;

        /// <summary>The gutter's plane.</summary>
        public IBrush? Background { get; set; }

        /// <summary>The numbers drawn now, by line, for a test: the lines in view with the error line's flagged.</summary>
        public IReadOnlyList<(int Line, bool Error)> Numbers =>
            [.. _editor.LineSpans().Select(s => (s.Line, s.Line == _editor.ErrorLine))];

        private int Digits => Math.Max(2, (_editor.Text ?? string.Empty).Count(c => c == '\n') + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

        protected override Size MeasureOverride(Size availableSize)
        {
            _measured = (Digits, _editor.FontSize, _editor.FontFamily);
            var sample = Text(new string('8', Digits), Ink, FontWeight.Normal);
            return new Size(Math.Ceiling(sample.Width) + 22, 0);
        }

        public override void Render(DrawingContext context)
        {
            if (Background is { } plane)
            {
                context.FillRectangle(plane, new Rect(Bounds.Size));
            }

            foreach (var (line, top, bottom) in _editor.LineSpans())
            {
                if (_editor.TranslatePoint(new Point(0, top), this) is not { } at)
                {
                    continue;
                }

                var error = line == _editor.ErrorLine;
                var number = Text(line.ToString(System.Globalization.CultureInfo.InvariantCulture), error ? Warn : Ink, error ? FontWeight.Bold : FontWeight.Normal);
                context.DrawText(number, new Point(Bounds.Width - 10 - number.Width, at.Y));
                if (error)
                {
                    context.FillRectangle(new SolidColorBrush(Warn), new Rect(Bounds.Width - 4, at.Y, 3, bottom - top));
                }
            }
        }

        private FormattedText Text(string text, Color ink, FontWeight weight) =>
            new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(_editor.FontFamily, FontStyle.Normal, weight), _editor.FontSize, new SolidColorBrush(ink));
    }
}
