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
/// <param name="Script">The script file this is an edit of, or null.</param>
/// <param name="Verb">The command field (<see cref="ComposerModel.Verb"/>): empty for none, and for an edit.</param>
internal readonly record struct ComposeResult(string Body, ComposeEscaping Escaping, MooEdit? Edit = null, ScriptEdit? Script = null, string Verb = "");

/// <summary>
/// A script file open in the composer: its name under the scripts folder (or <c>package:&lt;id&gt;/&lt;file&gt;</c>), whose
/// scripts a save reloads, whether it can only be read, what the last save said and the line the caret opens on.
/// </summary>
/// <param name="Name">The script's name.</param>
/// <param name="Owner">The session key of the character whose scripts a save reloads; null for none.</param>
/// <param name="ReadOnly">A package's script: shown, never saved.</param>
/// <param name="Status">What the last save said, or null.</param>
/// <param name="Line">The line the caret opens on, from 1; 0 for the top.</param>
/// <param name="ErrorLine">The line the last load error named in this file, from 1; 0 for none.</param>
internal sealed record ScriptEdit(string Name, string? Owner, bool ReadOnly, string? Status = null, int Line = 0, int ErrorLine = 0)
{
    /// <summary>What the editor says this is: the MOO edit's shape, read by both hosts.</summary>
    internal ComposerEdit Describe() => new(Name, "lua", Script: true, ReadOnly, Status, Line, ErrorLine);

    /// <summary>
    /// This edit after a save said <paramref name="status"/>: an error (<c>! …</c>) naming a line of this file marks that
    /// line; anything else clears the mark.
    /// </summary>
    internal ScriptEdit Said(string status) => this with
    {
        Status = status,
        ErrorLine = status.StartsWith('!') ? ScriptErrors.LineIn(status, Name) ?? 0 : 0,
    };
}

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
    private Panels.ComposerCommandRow? _command;

    /// <summary>Whether the keyboard is in the command field (⇥ moves it); only while a post is open.</summary>
    private bool _inCommand;
    private ComposeEscaping _escaping;
    private string _target = string.Empty;
    private bool _canSend;
    private MooEdit? _edit;
    private ScriptEdit? _script;
    private readonly Func<SpellChecker?>? _openSpelling;
    private SpellChecker? _spelling;
    private bool _spellingOpened;
    private MarkupControl? _headerBand;
    private MarkupControl? _footerBand;
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
    internal SpellChecker? Spelling => _edit is null && _script is null ? _spelling : null;

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

    /// <summary>The command field as it stands, one line; empty when shut, and for an edit, which has none.</summary>
    internal string Verb => _command?.Text ?? string.Empty;

    /// <summary>Whether the keyboard is in the command field, for the tests.</summary>
    internal bool InCommand => _command is not null && _inCommand;

    /// <summary>The escaping mode as it stands — the window's own toggle, not a saved setting.</summary>
    internal ComposeEscaping Escaping => _escaping;

    /// <summary>
    /// The MOO buffer being edited, or null for a post. An edit is saved as its lines, with no <c>%r</c> join and
    /// no escaping, so ⌥L does nothing while it is open and the footer does not offer it.
    /// </summary>
    internal MooEdit? Edit => _edit;

    /// <summary>The script file being edited, or null. Saved as its lines, like a MOO edit.</summary>
    internal ScriptEdit? Script => _script;

    /// <summary>Says what the last save of the open script did, in the header, without touching the editor.</summary>
    internal void SetScriptStatus(string status)
    {
        if (_script is null)
        {
            return;
        }

        _script = _script.Said(status);
        MarkErrorLine();
        RefreshBands();
    }

    /// <summary>The header row's markup, so a test can read who the window says it will send to.</summary>
    internal string HeaderMarkup => Render(Model).Header;

    /// <summary>
    /// What the window draws, as the shared model a native composer draws: the target, the buffer as it stands, the
    /// mode, the edit, the dictionary and its marks, and the key row.
    /// </summary>
    internal ComposerModel Model => ModelFor(_target, Body, _escaping, _edit, Spelling, _canSend, Preview, _script, Verb);

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
    internal static ComposerModel ModelFor(string target, string body, ComposeEscaping escaping, MooEdit? edit, SpellChecker? spelling, bool canSend, bool preview = false, ScriptEdit? script = null, string verb = "") =>
        ComposerModel.Create(
            ComposerPlace.FullScreen,
            target,
            body,
            escaping,
            script is not null ? script.Describe() : edit is null ? null : new ComposerEdit(edit.Name, edit.Type),
            spelling,
            canSend,
            armed: true,
            revision: 1,
            preview: preview,
            verb: verb);

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
    /// <param name="script">The script file this opens on, or null.</param>
    /// <param name="verb">The command field the draft was left with; a post only.</param>
    internal void Toggle(string target, string draft, ComposeEscaping escaping, bool canSend, MooEdit? edit = null, ScriptEdit? script = null, string verb = "")
    {
        if (_window is not null)
        {
            Close();
            return;
        }

        _target = target;
        _escaping = escaping;
        _canSend = canSend;
        _edit = script is null ? edit : null;
        _script = script;

        _editor = new MultilineEditControl
        {
            WrapMode = WrapMode.WrapWords,

            // A script is code: its lines are numbered, so a load error's file:line can be found, and a package's script is
            // shown read-only.
            ShowLineNumbers = script is not null,
            ReadOnly = script?.ReadOnly == true,
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
            PlaceholderText = script is not null
                ? "An empty script. ⌃S saves it and reloads this character's scripts."
                : edit is null
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

            // The script gutter's numbers in the screens' label ink, held to the floor on the well; the control's
            // default grey was 2.7:1 on a light theme's well.
            LineNumberColor = _scheme.Colour(ScreenPalette.Label),
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
        _inCommand = false;
        _command = null;
        if (edit is null && script is null)
        {
            // A post has a command field above its text; an edit is its lines alone.
            _command = new Panels.ComposerCommandRow(_scheme, ScreenPalette.FooterBg);
            _command.Set(verb);
            _command.Field.ContentChanged += (_, _) => RefreshBands();
        }
        if (script is { Line: > 0 } at)
        {
            _editor.GoToLine(at.Line);
        }

        MarkErrorLine();

        _editor.ContentChanged += (_, _) => RefreshBands();
        FitEditor();

        var builder = new WindowBuilder(_system)
            .AsModal()
            .Maximized()
            .Frameless()
            .WithColors(_scheme.Colour(ScreenPalette.PanelFg), _scheme.Colour(ScreenPalette.PanelBg))
            .AddControl(HeaderBand());
        if (CommandControl() is { } row)
        {
            builder.AddControl(row);
        }

        builder.AddControl(_editor);
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
        _window.FocusManager.SetFocus(FocusedField, FocusReason.Programmatic);
    }

    /// <summary>The field the keyboard is in: the command field after ⇥, else the text.</summary>
    private MultilineEditControl FocusedField => _inCommand && _command is not null ? _command.Field : _editor!;

    /// <summary>The command row, sticky under the header, or null for an edit.</summary>
    private IWindowControl? CommandControl()
    {
        if (_command is null)
        {
            return null;
        }

        _command.Control.StickyPosition = StickyPosition.Top;

        return _command.Control;
    }

    /// <summary>⇥ / ⇧⇥ / ⏎ in the command field: the keyboard to the other field.</summary>
    private void SwitchField(bool toCommand)
    {
        if (_window is null || _command is null)
        {
            return;
        }

        _inCommand = toCommand;
        _window.FocusManager.SetFocus(FocusedField, FocusReason.Programmatic);
        _window.Invalidate(redrawAll: true);
    }

    /// <summary>
    /// The line the last load error named (<see cref="ComposerEdit.ErrorLine"/>, the field the window's editor marks too)
    /// on a band behind it in the warning ink, the rest cleared. A background, because the editor's syntax tokens carry a
    /// foreground only.
    /// </summary>
    private void MarkErrorLine()
    {
        if (_editor is null)
        {
            return;
        }

        _editor.ClearLineHighlights();
        if (_script is { ErrorLine: > 0 } script)
        {
            _editor.SetLineHighlight(script.ErrorLine - 1, ErrorBand(_scheme));
        }
    }

    /// <summary>The error line's band: the warning ink part of the way over the editor's well, so the text on it stays legible.</summary>
    internal static Color ErrorBand(ScreenTheme scheme)
    {
        var well = scheme.Colour(ScreenPalette.EditBg);
        var warn = scheme.Colour(ScreenPalette.Warn);
        static byte Mix(byte a, byte b) => (byte)Math.Round(a + ((b - a) * 0.28));
        return new Color(Mix(well.R, warn.R), Mix(well.G, warn.G), Mix(well.B, warn.B));
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

    /// <summary>Types text into the field the keyboard is in, the way the framework's own key path would.</summary>
    internal void SimulateTyping(string text)
    {
        if (_editor is null)
        {
            return;
        }

        var field = FocusedField;
        field.SetContent(field.GetContent() + text);
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
        var (header, footer) = Render(model, Width);
        _headerBand?.SetContent(new List<string> { _scheme.Paint(header, ScreenPalette.HeaderBg) });
        _footerBand?.SetContent(new List<string> { _scheme.Paint(footer, ScreenPalette.FooterBg) });
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
        if (alt && key.Key == ConsoleKey.L && _edit is null && _script is null)
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
        if (alt && key.Key == ConsoleKey.W && _edit is null && _script is null)
        {
            e.Handled = true;
            DictionaryWanted?.Invoke(this, EventArgs.Empty);
            return;
        }

        // ⌥P shows the post as it will be sent, and hides it again. Alt by the same rule; P for preview, and nothing in
        // MacroKeys.AppShortcuts, the bar's table or a host's reserved chords takes it. A MOO edit is sent as its lines,
        // which the editor already shows, so it has no preview.
        if (alt && key.Key == ConsoleKey.P && _edit is null && _script is null)
        {
            e.Handled = true;
            Preview = !Preview;
            Redraw();
            return;
        }

        // ⇥ and ⇧⇥ move between the command field and the text, and ⏎ in the command field moves on to the text: a
        // command is one line, and a post's text has no use for a tab. An edit has one field and keeps its ⇥ (indent).
        if (_command is not null && !ctrl && !alt
            && (key.Key == ConsoleKey.Tab || (key.Key == ConsoleKey.Enter && _inCommand)))
        {
            e.Handled = true;
            SwitchField(key.Key == ConsoleKey.Tab && !_inCommand && !key.Modifiers.HasFlag(ConsoleModifiers.Shift));
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
        Send?.Invoke(this, new ComposeResult(Body, _escaping, _edit, _script, Verb));
    }

    /// <summary>Shuts the window, handing the buffer back for the host to keep.</summary>
    internal void Close()
    {
        if (_window is not { } window)
        {
            return;
        }

        var result = new ComposeResult(Body, _escaping, _edit, _script, Verb);
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
        if (CommandControl() is { } row)
        {
            window.AddControl(row);
        }

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
        window.FocusManager.SetFocus(FocusedField, FocusReason.Programmatic);
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
    /// The preview's rows for <paramref name="model"/>: a heading, then the command ⌃S would send, cut into rows of at
    /// most <paramref name="width"/> characters at a space (<see cref="WrapExact"/>) — the space kept at the end of its
    /// row, so the rows joined are exactly the characters the game gets — ending in <c>…</c> when it runs past the
    /// rows there are. Always <see cref="PreviewRows"/> rows, so typing never moves the editor.
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
        var pieces = WrapExact(command, width);
        for (var row = 0; row < rows; row++)
        {
            if (row >= pieces.Count)
            {
                lines.Add(row == 0 && command.Length == 0 ? $"[{ScreenPalette.Muted}]nothing yet[/]" : string.Empty);
                continue;
            }

            var piece = pieces[row];
            if (row == rows - 1 && pieces.Count > rows)
            {
                piece = (piece.Length >= width ? piece[..^1] : piece.TrimEnd()) + "…";
            }

            lines.Add($"[{ScreenPalette.Value}]{Escape(piece)}[/]");
        }

        return lines;
    }

    /// <summary>
    /// <paramref name="text"/> cut into rows of at most <paramref name="width"/> characters, each ending after the last
    /// space that fits (a word longer than a row is cut hard). Nothing is dropped or added: the rows joined are
    /// <paramref name="text"/>, so the preview never hides a space the game will get.
    /// </summary>
    internal static IReadOnlyList<string> WrapExact(string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        width = Math.Max(1, width);
        var rows = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(width, text.Length - start);
            if (start + length < text.Length)
            {
                // Break after the last space inside the row, so the next row starts with a word.
                var space = text.LastIndexOf(' ', start + length - 1, length);
                if (space > start)
                {
                    length = space - start + 1;
                }
            }

            rows.Add(text.Substring(start, length));
            start += length;
        }

        return rows;
    }

    private MarkupControl FooterBand()
    {
        var band = ScreenChrome.Band(Render(Model, Width).Footer, ScreenPalette.FooterBg, _scheme);
        _footerBand = band;
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

        var footerKeys = KeyLadder.Choose(FooterKeys(model), width);

        // Each header is a short ladder of its own (wave 33): at 60 columns the full one was cut mid-word ("spellin…").
        // The words that name a field go first, then the least needed field; what the screen is and who it sends to stay.
        string[] headers;
        if (model.Edit is { Script: true } script)
        {
            var status = script.Status is { } said
                ? $"[{ScreenPalette.Label}]  ·  [/][{(said.StartsWith('!') ? ScreenPalette.Warn : ScreenPalette.Value)}]{Escape(said)}[/]"
                : string.Empty;
            var name = $"[{ScreenPalette.Accent}]{(script.ReadOnly ? "READ ONLY" : "SCRIPT")}[/] [{ScreenPalette.Value}]{Escape(script.Name)}[/]";
            headers =
            [
                name
                    + $"[{ScreenPalette.Label}]  lua  ·  {(model.Target.Length == 0 ? "reloads nobody" : "reloads ")}[/]"
                    + (model.Target.Length == 0 ? string.Empty : $"[{ScreenPalette.Value}]{Escape(model.Target)}[/]")
                    + status,

                // The script error is the engine's text, not the client's: it may still be elided past this, and the
                // console says it whole.
                name + status,
            ];
        }
        else if (model.Edit is { } edit)
        {
            headers =
            [
                $"[{ScreenPalette.Accent}]EDIT[/] [{ScreenPalette.Value}]{Escape(edit.Name)}[/]"
                    + $"[{ScreenPalette.Label}]  {Escape(edit.Type)}  ·  from [/]{target}",
                $"[{ScreenPalette.Accent}]EDIT[/] [{ScreenPalette.Value}]{Escape(edit.Name)}[/][{ScreenPalette.Label}]  from [/]{target}",
            ];
        }
        else
        {
            var mode = model.Escaping == ComposeEscaping.Literal
                ? $"[{ScreenPalette.Accent}]literal[/]"
                : $"[{ScreenPalette.Value}]as typed[/]";

            // The key row names ⌥W (get a dictionary) and gives it up only after ⌥P and ⌥L, so the header does not name it
            // again: a second chip for one key cost the header its room at 80 columns, where it was cut to "⌥W…".
            var spelling = model.Dictionary is { } name
                ? $"[{ScreenPalette.Value}]{Escape(name)}[/]"
                : $"[{ScreenPalette.Muted}]no dictionary[/]";

            var head = $"[{ScreenPalette.Accent}]COMPOSE[/][{ScreenPalette.Label}]  to [/]{target}";
            headers =
            [
                head + $"[{ScreenPalette.Label}]  ·  text [/]{mode}" + $"[{ScreenPalette.Label}]  ·  spelling [/]{spelling}",
                head + $"[{ScreenPalette.Label}]  ·  [/]{mode}" + $"[{ScreenPalette.Label}]  ·  [/]{spelling}",
                head + $"[{ScreenPalette.Label}]  ·  [/]{mode}",
            ];
        }

        var length = $"[{ScreenPalette.Value}]{ComposerModel.CharactersLabel(model.Characters)}[/] ";
        var room = width - VisibleLength(length) - 1;
        var header = width > 0 ? headers.FirstOrDefault(h => VisibleLength(h) <= room) ?? headers[^1] : headers[0];
        header = width > 0 ? SpreadLR(header, length, width) : $"{header}[{ScreenPalette.Label}]  ·  [/]{length}";

        var footer = string.Join(
            $"[{ScreenPalette.Label}] · [/]",
            footerKeys.Select(k => $"[{ScreenPalette.Accent}]{Escape(k.Key)}[/][{ScreenPalette.Label}] {Escape(k.Label)}[/]"));
        return (header, footer);
    }

    /// <summary>
    /// The key row as a ladder: parentheticals go first (<c>close (keeps the draft)</c>), then ⌥P, ⌥L and ⌥W in that order.
    /// ⌃S and Esc are never given up — they are how a post leaves this screen.
    /// </summary>
    internal static IReadOnlyList<LadderKey> FooterKeys(ComposerModel model) =>
        model.Keys.Select(k => new LadderKey(k.Key, k.Label, k.Short, k.Key switch
        {
            "⌥P" => 1,
            "⌥L" => 2,
            "⌥W" => 3,
            "F1" => 4,
            _ => LadderKey.Keep,
        })).ToList();

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

        _editor.ViewportHeight = Math.Max(1, _system.ConsoleDriver.ScreenSize.Height - ChromeRows - (_command is null ? 0 : 1) - (Model.Preview ? PreviewRows : 0));
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
        _command = null;
        _inCommand = false;
        _edit = null;
        _script = null;
        _headerBand = null;
        _footerBand = null;
        _previewBand = null;
    }
}
