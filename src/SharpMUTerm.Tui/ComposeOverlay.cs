using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Telnet;
using SharpMUTerm.Core.Text;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>What a composed post is, at the moment it is sent or put away.</summary>
/// <param name="Body">The editor's buffer, exactly as typed.</param>
/// <param name="Escaping">The window's escaping mode when it happened.</param>
/// <param name="Edit">The MOO buffer this is an edit of, or null for a post.</param>
internal readonly record struct ComposeResult(string Body, ComposeEscaping Escaping, MooEdit? Edit = null);

/// <summary>
/// The composer: a full-screen editor for writing a post before sending it, over the workspace.
/// <para>
/// <b>Why a full screen and not a bigger command line.</b> The command line is the same control
/// (<see cref="InputBarControl"/> is a <see cref="MultilineEditControl"/>) with ⏎ taken over to send. A
/// composer is the case the editor was built for: ⏎ inserts, the buffer is a document, and there is the
/// whole screen to write it in.
/// </para>
/// <para>
/// <b>Why a modal window.</b> The app pins keyboard focus to the armed command line on every focus
/// change, which an editor needing real focus would fight forever. It does not have to: the pin stands
/// down while the main window is inactive (<c>PinFocusToArmedBar</c> returns on
/// <c>!_window.GetIsActive()</c>), and a modal deactivates it. That is the same reason the settings
/// screens are modal, and it is why this window can simply hold a focusable control and let the
/// framework route typing, selection and paste to it.
/// </para>
/// <para>
/// <b>Paste is the framework's here, deliberately.</b> <see cref="SettingsOverlay"/> takes paste off
/// the driver because its screens are markup with no focusable target, and its own remarks warn that a
/// focusable <c>IPasteTarget</c> would make both paths fire. <see cref="MultilineEditControl"/> is
/// exactly such a target, so this window subscribes to nothing: the framework hands a paste to the
/// focused control and the editor inserts it as one atomic block. The two overlays are kept from being
/// open at once for that reason among others — see <c>SharpMUTermApp.ToggleComposer</c>.
/// </para>
/// </summary>
internal sealed class ComposeOverlay
{
    private readonly ConsoleWindowSystem _system;

    private Window? _window;
    private MultilineEditControl? _editor;
    private ComposeEscaping _escaping;
    private string _target = string.Empty;
    private bool _canSend;
    private MooEdit? _edit;
    private readonly Func<SpellChecker?>? _openSpelling;
    private SpellChecker? _spelling;
    private bool _spellingOpened;
    private MarkupControl? _headerBand;
    private MarkupControl? _previewBand;

    /// <param name="system">The window system the composer opens in.</param>
    /// <param name="spelling">
    /// Opens the dictionary, the first time the composer is opened and never again, so a client nobody writes a
    /// post in reads no dictionary. Null, or a null answer, is a composer with no spelling, which says so.
    /// </param>
    /// <param name="scheme">The settings screens' palette for the app's theme; the composer is one of them.</param>
    internal ComposeOverlay(ConsoleWindowSystem system, Func<SpellChecker?>? spelling = null, ScreenTheme? scheme = null)
    {
        _system = system;
        _openSpelling = spelling;
        _scheme = scheme ?? ScreenTheme.Dark;
    }

    private ScreenTheme _scheme;

    /// <summary>The palette it is painted in; the app replaces it when the theme changes, and the next time it is drawn follows.</summary>
    internal ScreenTheme Scheme { get => _scheme; set => _scheme = value ?? ScreenTheme.Dark; }

    /// <summary>
    /// The dictionary marking this post, or null: none was found, or this is a MOO edit, whose buffer is as often
    /// code as prose.
    /// </summary>
    internal SpellChecker? Spelling => _edit is null ? _spelling : null;

    /// <summary>⌥W in a post with no dictionary: the host offers to download one.</summary>
    internal event EventHandler? DictionaryWanted;

    /// <summary>Forgets the dictionary (or that there was none), so the next opening looks again — after a download.</summary>
    internal void ForgetSpelling()
    {
        _spellingOpened = false;
        _spelling = null;
    }

    /// <summary>Raised when ⌃S sends. The host decides where it goes and what it does about failure.</summary>
    internal event EventHandler<ComposeResult>? Send;

    /// <summary>
    /// Raised as the window goes away, carrying the buffer so the host can keep the draft. Raised on
    /// every close — Esc, the F-key, and a send — because the host's draft store is the only thing that
    /// remembers, and a close that stayed silent would lose the post.
    /// </summary>
    internal event EventHandler<ComposeResult>? Closed;

    internal bool IsOpen => _window is not null;

    /// <summary>
    /// The buffer as it stands, or empty when the window is shut, with its lines broken by <c>\n</c>. The editor
    /// joins them with <see cref="Environment.NewLine"/>, so on Windows a post or an edit would otherwise carry
    /// CRLF in one host and LF in the other.
    /// </summary>
    internal string Body => _editor?.GetContent().Replace("\r\n", "\n", StringComparison.Ordinal) ?? string.Empty;

    /// <summary>The escaping mode as it stands — the window's own toggle, not a saved setting.</summary>
    internal ComposeEscaping Escaping => _escaping;

    /// <summary>
    /// The MOO buffer being edited, or null for a post. An edit is saved as its lines, with no <c>%r</c> join and
    /// no escaping, so ⌥L does nothing while it is open and the footer does not offer it.
    /// </summary>
    internal MooEdit? Edit => _edit;

    /// <summary>The header row's markup, so a test can read who the window says it will send to.</summary>
    internal string HeaderMarkup => Render(Model).Header;

    /// <summary>
    /// What the window draws, as the shared model a native composer draws: the target, the buffer as it stands, the
    /// mode, the edit, the dictionary and its marks, and the key row.
    /// </summary>
    internal ComposerModel Model => ModelFor(_target, Body, _escaping, _edit, Spelling, _canSend, Preview);

    /// <summary>
    /// Whether F1 shows the post as it will be sent (⌥P), under the editor. One fact for the run in both hosts — the
    /// native F1 reads and flips this same property — and never saved: it is a way of looking, not a setting.
    /// </summary>
    internal bool Preview { get; set; }

    /// <summary>How many rows the as-sent preview takes when it is showing: a heading row and the command under it.</summary>
    internal const int PreviewRows = 4;

    /// <summary>
    /// F1's model for a draft, in either host: the cell window builds its own from this, and a native F1 is opened on
    /// it, so the two say the same thing for the same draft. Revision 1: F1 opens on a draft and nothing replaces it
    /// while it is open.
    /// </summary>
    internal static ComposerModel ModelFor(string target, string body, ComposeEscaping escaping, MooEdit? edit, SpellChecker? spelling, bool canSend, bool preview = false) =>
        ComposerModel.Create(
            ComposerPlace.FullScreen,
            target,
            body,
            escaping,
            edit is null ? null : new ComposerEdit(edit.Name, edit.Type),
            spelling,
            canSend,
            armed: true,
            revision: 1,
            preview: preview);

    /// <summary>
    /// Opens the composer on <paramref name="draft"/>, or closes it when it is already open — the same
    /// toggle every other surface in this client is on, so the key that opened it puts it away.
    /// </summary>
    /// <param name="target">Who this will send to, for the header. Empty when nothing is connected.</param>
    /// <param name="draft">The character's kept draft, or empty.</param>
    /// <param name="escaping">The mode that draft was last left in.</param>
    /// <param name="canSend">
    /// Whether there is a connection to send to. False still opens the window — writing a post with the
    /// world briefly down is a real thing to be doing — and refuses at the moment of sending instead,
    /// which is the same rule ⏎ follows on the command line.
    /// </param>
    /// <param name="edit">The MOO buffer this opens on, or null for a post.</param>
    internal void Toggle(string target, string draft, ComposeEscaping escaping, bool canSend, MooEdit? edit = null)
    {
        if (_window is not null)
        {
            Close();
            return;
        }

        _target = target;
        _escaping = escaping;
        _canSend = canSend;
        _edit = edit;

        _editor = new MultilineEditControl
        {
            WrapMode = WrapMode.WrapWords,
            ShowLineNumbers = false,
            IsEditing = true,

            // Fill, so the editor is the window rather than the control's ten-row default sitting in the
            // top corner of a maximised one — GetEffectiveViewportHeight only reads the arranged bounds
            // when the alignment asks it to. Stretch for the same reason horizontally: this framework's
            // controls self-size to their content unless told to fill, which on an empty buffer is a
            // window you cannot see the caret in.
            VerticalAlignment = VerticalAlignment.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,

            // Esc closes the window, so it must not first be spent dropping the editor from edit mode
            // into browse mode — which would make the key mean two different things depending on a
            // state the writer has no reason to be tracking.
            EscapeExitsEditMode = false,
            PlaceholderText = edit is null
                ? "Write your post. ⏎ starts a new line; the game gets one command with %r breaks."
                : "Empty. ⌃S saves it empty.",

            // The screens' own edit colours, so the composer reads as part of this client rather than as
            // the control's grey default sitting in a themed window. Both pairs are set: this control
            // paints from the *focused* pair whenever it has focus, and it always has focus here, so
            // setting only the unfocused one leaves the default grey on screen for the whole session —
            // which is exactly what the first frame of this window showed.
            BackgroundColor = _scheme.Colour(ScreenPalette.EditBg),
            FocusedBackgroundColor = _scheme.Colour(ScreenPalette.EditBg),
            ForegroundColor = _scheme.Colour(ScreenPalette.Value),
            FocusedForegroundColor = _scheme.Colour(ScreenPalette.Value),
            SelectionBackgroundColor = _scheme.Colour(ScreenPalette.CursorBg),
            SelectionForegroundColor = _scheme.Colour(ScreenPalette.Value),
        };

        if (!_spellingOpened)
        {
            _spellingOpened = true;
            _spelling = _openSpelling?.Invoke();
        }

        if (Spelling is { } checker)
        {
            _editor.SyntaxHighlighter = new SpellingHighlighter(checker, _scheme.Colour(ScreenPalette.Warn));
        }

        _editor.SetContent(draft);
        _editor.ContentChanged += (_, _) => RefreshBands();
        FitEditor();

        var builder = new WindowBuilder(_system)
            .AsModal()
            .Maximized()
            .Frameless()
            .WithColors(_scheme.Colour(ScreenPalette.PanelFg), _scheme.Colour(ScreenPalette.PanelBg))
            .AddControl(HeaderBand())
            .AddControl(_editor);
        if (Model.Preview)
        {
            builder.AddControl(PreviewBand());
        }

        _window = builder
            .AddControl(FooterBand())
            .OnClosed((_, _) => Reset())
            .Build();

        _window.PreviewKeyPressed += OnKey;
        _system.ConsoleDriver.ScreenResized += OnScreenResized;
        _system.AddWindow(_window);

        // Nothing focuses a control for you in this framework, and an unfocused editor takes no
        // keystrokes at all — the window would open looking right and swallow everything typed into it.
        _window.FocusManager.SetFocus(_editor, FocusReason.Programmatic);
    }

    /// <summary>
    /// Feeds one key through the very handler <c>PreviewKeyPressed</c> raises, so a headless test and a
    /// snapshot can drive this window. The framework only pumps input inside <c>Run()</c>, which neither
    /// enters — the same reason <see cref="SettingsOverlay.SimulateKey"/> exists.
    /// </summary>
    internal bool SimulateKey(ConsoleKeyInfo key)
    {
        var args = new KeyPressedEventArgs(key, false);
        OnKey(this, args);
        return args.Handled;
    }

    /// <summary>Types text into the editor, the way the framework's own key path would.</summary>
    internal void SimulateTyping(string text)
    {
        if (_editor is null)
        {
            return;
        }

        _editor.SetContent(_editor.GetContent() + text);
        RefreshBands();
    }

    /// <summary>
    /// Re-says what changes as the post is typed — the header's length cell and the as-sent preview — without touching
    /// the editor, so the caret and the undo stack stay where they were.
    /// </summary>
    private void RefreshBands()
    {
        if (_window is null)
        {
            return;
        }

        var model = Model;
        _headerBand?.SetContent(new List<string> { _scheme.Paint(Render(model, Width).Header, ScreenPalette.HeaderBg) });
        _previewBand?.SetContent(_scheme.Lines(PreviewLines(model, Width), ScreenPalette.FooterBg));
    }

    /// <summary>The screen's width in cells, which the header and the preview are laid out to.</summary>
    private int Width => _system.ConsoleDriver.ScreenSize.Width;

    private void OnKey(object? sender, KeyPressedEventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        var key = e.KeyInfo;
        var ctrl = key.Modifiers.HasFlag(ConsoleModifiers.Control);
        var alt = key.Modifiers.HasFlag(ConsoleModifiers.Alt);

        // ⌃S sends. Safe here for the reason ⌃Q is safe as the quit chord: TerminalRawMode clears IXON,
        // so this is not the terminal's flow-control stop. The editor's own Ctrl table (A/C/X/Z/D/Y)
        // does not claim it, and everything it does not claim bubbles up — but this handler runs first
        // in any case, so the two can never disagree about who gets it.
        if (ctrl && key.Key == ConsoleKey.S)
        {
            e.Handled = true;
            SendNow();
            return;
        }

        // ⌥L flips the escaping. Alt rather than Ctrl by this codebase's rule — ESC + a printable byte
        // arrives intact, while half the Ctrl alphabet collapses onto bytes a terminal already spends.
        if (alt && key.Key == ConsoleKey.L && _edit is null)
        {
            e.Handled = true;
            _escaping = _escaping == ComposeEscaping.Literal ? ComposeEscaping.AsTyped : ComposeEscaping.Literal;
            Redraw();
            return;
        }

        // ⌥W adds the word at the caret to the dictionary. Alt by the same rule as ⌥L, and W because nothing
        // in MacroKeys.AppShortcuts takes it — a global shortcut would run before this window ever saw it.
        if (alt && key.Key == ConsoleKey.W && Spelling is not null)
        {
            e.Handled = true;
            AddWordAtCaret();
            return;
        }

        // ⌥W with no dictionary is the way to one: the host closes this window (keeping the draft) and asks first.
        if (alt && key.Key == ConsoleKey.W && _edit is null)
        {
            e.Handled = true;
            DictionaryWanted?.Invoke(this, EventArgs.Empty);
            return;
        }

        // ⌥P shows the post as it will be sent, and hides it again. Alt by the same rule; P for preview, and nothing in
        // MacroKeys.AppShortcuts, the bar's table or a host's reserved chords takes it. A MOO edit is sent as its lines,
        // which the editor already shows, so it has no preview.
        if (alt && key.Key == ConsoleKey.P && _edit is null)
        {
            e.Handled = true;
            Preview = !Preview;
            Redraw();
            return;
        }

        if (key.Key == ConsoleKey.Escape && !ctrl && !alt)
        {
            e.Handled = true;
            Close();
        }

        // Everything else is the editor's, including ⏎, the arrows, ⌃Z and a selection drag.
    }

    /// <summary>
    /// Adds the word the caret is in, or just after, to the player's word list and repaints the marks. Nothing
    /// happens on a caret in no word.
    /// </summary>
    private void AddWordAtCaret()
    {
        if (_editor is null || Spelling is not { } checker)
        {
            return;
        }

        var lines = _editor.GetContent().Split('\n');
        var row = _editor.CurrentLine - 1;
        if (row < 0 || row >= lines.Length || SpellChecker.WordAt(lines[row], _editor.CurrentColumn - 1) is not { } word)
        {
            return;
        }

        if (checker.Add(word))
        {
            _editor.RefreshSyntaxHighlighting();
        }
    }

    private void SendNow()
    {
        if (_editor is null)
        {
            return;
        }

        // The window closes on a send, and the host clears the draft when it accepts one. Raised before
        // the close so the handler sees the buffer rather than an empty window — and Closed still
        // follows, so a send the host refuses (no connection) keeps the post rather than dropping it on
        // the floor.
        Send?.Invoke(this, new ComposeResult(_editor.GetContent(), _escaping, _edit));
    }

    /// <summary>Shuts the window, handing the buffer back for the host to keep.</summary>
    internal void Close()
    {
        if (_window is not { } window)
        {
            return;
        }

        var result = new ComposeResult(Body, _escaping, _edit);
        _system.CloseModalWindow(window);
        Closed?.Invoke(this, result);
    }

    /// <summary>
    /// Repaints the bands after the mode changed. Only the two chrome rows are rebuilt: the editor is
    /// the control holding the text, the caret and the undo stack, and replacing it to redraw a header
    /// would throw all three away.
    /// </summary>
    private void Redraw()
    {
        if (_window is not { } window || _editor is null)
        {
            return;
        }

        window.ClearControls();
        window.AddControl(HeaderBand());
        window.AddControl(_editor);
        if (Model.Preview)
        {
            window.AddControl(PreviewBand());
        }
        else
        {
            _previewBand = null;
        }

        window.AddControl(FooterBand());
        FitEditor();
        window.Invalidate(redrawAll: true);
        window.FocusManager.SetFocus(_editor, FocusReason.Programmatic);
    }

    /// <summary>
    /// The two chrome rows, pinned to the top and bottom edges. Sticky rather than flowed because
    /// vertical space at a window's root is measured sticky-first and Fill-last: the editor takes what
    /// is left, so a footer that flowed would ride up under it on a short buffer and sit in the middle
    /// of the window.
    /// </summary>
    private MarkupControl HeaderBand()
    {
        var band = ScreenChrome.Band(Render(Model, Width).Header, ScreenPalette.HeaderBg, _scheme);
        band.StickyPosition = StickyPosition.Top;
        _headerBand = band;
        return band;
    }

    /// <summary>The as-sent preview, pinned above the footer: <see cref="PreviewRows"/> rows on the footer's plane.</summary>
    private MarkupControl PreviewBand()
    {
        var band = ScreenChrome.Panel(PreviewLines(Model, Width), _scheme, ScreenPalette.FooterBg);
        band.BackgroundColor = _scheme.Colour(ScreenPalette.FooterBg);
        band.HorizontalAlignment = HorizontalAlignment.Stretch;
        band.StickyPosition = StickyPosition.Bottom;
        _previewBand = band;
        return band;
    }

    /// <summary>
    /// The preview's rows for <paramref name="model"/>: a heading, then the command ⌃S would send, cut into rows of
    /// <paramref name="width"/> characters — hard, not at words, because this shows the exact characters the game gets
    /// and a wrap at a space would hide one — ending in <c>…</c> when it runs past the rows there are. Always
    /// <see cref="PreviewRows"/> rows, so typing never moves the editor.
    /// </summary>
    internal static IReadOnlyList<string> PreviewLines(ComposerModel model, int width)
    {
        width = Math.Max(10, width);
        var command = model.Command ?? string.Empty;
        var lines = new List<string>
        {
            $"[{ScreenPalette.Accent}]AS SENT[/][{ScreenPalette.Label}]  the one command ⌃S sends · ⌥P hides this[/]",
        };
        var rows = PreviewRows - 1;
        for (var row = 0; row < rows; row++)
        {
            var start = row * width;
            if (start >= command.Length)
            {
                lines.Add(row == 0 && command.Length == 0 ? $"[{ScreenPalette.Muted}]nothing yet[/]" : string.Empty);
                continue;
            }

            var piece = command.Substring(start, Math.Min(width, command.Length - start));
            if (row == rows - 1 && start + width < command.Length)
            {
                piece = piece[..^1] + "…";
            }

            lines.Add($"[{ScreenPalette.Value}]{Escape(piece)}[/]");
        }

        return lines;
    }

    private MarkupControl FooterBand()
    {
        var band = ScreenChrome.Band(Render(Model).Footer, ScreenPalette.FooterBg, _scheme);
        band.StickyPosition = StickyPosition.Bottom;
        return band;
    }

    /// <summary>
    /// The two chrome rows for <paramref name="model"/>. The title row says what this is, who it sends to, and which
    /// mode it is in; the target is escaped because a character's name is configuration and a world's name can be
    /// anything. The key row is the model's keys and nothing else, because this screen is held to the same honesty
    /// rule as the settings screens: every key printed here works, and none that works is hidden. There is
    /// deliberately no <c>⌃F find</c> — the control has a find API but no chord bound to it.
    /// </summary>
    /// <param name="model">What to draw.</param>
    /// <param name="width">
    /// The row's width in cells: the length cell is set at its right edge and the rest gives way before it. Zero puts the
    /// length cell on the end, for a caller with no screen.
    /// </param>
    internal static (string Header, string Footer) Render(ComposerModel model, int width = 0)
    {
        var target = model.Target.Length == 0
            ? $"[{ScreenPalette.Warn}]no connection[/]"
            : $"[{ScreenPalette.Value}]{Escape(model.Target)}[/]";

        string header;
        if (model.Edit is { } edit)
        {
            header = $"[{ScreenPalette.Accent}]EDIT[/] [{ScreenPalette.Value}]{Escape(edit.Name)}[/]"
                + $"[{ScreenPalette.Label}]  {Escape(edit.Type)}  ·  from [/]{target}";
        }
        else
        {
            var mode = model.Escaping == ComposeEscaping.Literal
                ? $"[{ScreenPalette.Accent}]literal[/]"
                : $"[{ScreenPalette.Value}]as typed[/]";

            var spelling = model.Dictionary is { } name
                ? $"[{ScreenPalette.Value}]{Escape(name)}[/]"
                : $"[{ScreenPalette.Muted}]no dictionary[/]";

            header = $"[{ScreenPalette.Accent}]COMPOSE[/][{ScreenPalette.Label}]  to [/]{target}"
                + $"[{ScreenPalette.Label}]  ·  text [/]{mode}"
                + $"[{ScreenPalette.Label}]  ·  spelling [/]{spelling}";
        }

        var length = $"[{ScreenPalette.Value}]{ComposerModel.CharactersLabel(model.Characters)}[/] ";
        header = width > 0 ? SpreadLR(header, length, width) : $"{header}[{ScreenPalette.Label}]  ·  [/]{length}";

        var footer = string.Join(
            $"[{ScreenPalette.Label}] · [/]",
            model.Keys.Select(k => $"[{ScreenPalette.Accent}]{Escape(k.Key)}[/][{ScreenPalette.Label}] {Escape(k.Label)}[/]"));
        return (header, footer);
    }

    /// <summary>
    /// Sizes the editor to the window: every row the terminal has, less the two chrome bands.
    /// <para>
    /// Explicitly, because <c>VerticalAlignment.Fill</c> alone is not enough here — the control reads
    /// its arranged bounds only once it <em>has</em> been arranged, and the first frame is laid out
    /// against its ten-row default, which is what a maximised window holding a ten-row editor and a
    /// footer immediately under it looked like. The height comes from the driver rather than a literal,
    /// which is the rule the header already learned: the driver knows the terminal's size before any
    /// window does, and a literal is right until somebody runs this in a shorter terminal.
    /// </para>
    /// </summary>
    private void FitEditor()
    {
        if (_editor is null)
        {
            return;
        }

        _editor.ViewportHeight = Math.Max(1, _system.ConsoleDriver.ScreenSize.Height - ChromeRows - (Model.Preview ? PreviewRows : 0));
    }

    /// <summary>The header and footer bands, which the editor's height is whatever is left over from.</summary>
    private const int ChromeRows = 2;

    /// <summary>
    /// Re-fits the editor when the terminal changes size. Without it a window opened at one height keeps
    /// that height for as long as it is up — and a composer is a window somebody sits in for minutes,
    /// which is exactly long enough to resize a terminal underneath it.
    /// </summary>
    private void OnScreenResized(object? sender, SharpConsoleUI.Helpers.Size size) =>
        _system.EnqueueOnUIThread(() =>
        {
            FitEditor();
            _window?.Invalidate(redrawAll: true);
        });

    private void Reset()
    {
        // The driver outlives every window, so this comes off with the one it was put on for — the same
        // rule (and the same defect avoided) as SettingsOverlay's paste hook.
        _system.ConsoleDriver.ScreenResized -= OnScreenResized;
        _window = null;
        _editor = null;
        _edit = null;
        _headerBand = null;
        _previewBand = null;
    }
}
