using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpMUTerm.Core.Commands;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>What a composed post is, at the moment it is sent or put away.</summary>
/// <param name="Body">The editor's buffer, exactly as typed.</param>
/// <param name="Escaping">The window's escaping mode when it happened.</param>
internal readonly record struct ComposeResult(string Body, ComposeEscaping Escaping);

/// <summary>
/// The composer: a full-screen editor for writing a post before sending it, over the workspace.
/// <para>
/// <b>Why an editor and not a bigger command line.</b> The command line is deliberately ours
/// (<see cref="InputBarControl"/>) because ⏎ has to send rather than insert, which rules the framework's
/// text controls out for it. A composer is the opposite case and the one
/// <see cref="MultilineEditControl"/> was built for: ⏎ inserts, the buffer is a document, and undo,
/// find, selection, mouse and a caret over wrapped rows all come with it rather than being written
/// again here.
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

    internal ComposeOverlay(ConsoleWindowSystem system) => _system = system;

    /// <summary>Raised when ⌃S sends. The host decides where it goes and what it does about failure.</summary>
    internal event EventHandler<ComposeResult>? Send;

    /// <summary>
    /// Raised as the window goes away, carrying the buffer so the host can keep the draft. Raised on
    /// every close — Esc, the F-key, and a send — because the host's draft store is the only thing that
    /// remembers, and a close that stayed silent would lose the post.
    /// </summary>
    internal event EventHandler<ComposeResult>? Closed;

    internal bool IsOpen => _window is not null;

    /// <summary>The buffer as it stands, or empty when the window is shut. Internal for the tests.</summary>
    internal string Body => _editor?.GetContent() ?? string.Empty;

    /// <summary>The escaping mode as it stands — the window's own toggle, not a saved setting.</summary>
    internal ComposeEscaping Escaping => _escaping;

    /// <summary>The header row's markup, so a test can read who the window says it will send to.</summary>
    internal string HeaderMarkup => Header();

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
    internal void Toggle(string target, string draft, ComposeEscaping escaping, bool canSend)
    {
        if (_window is not null)
        {
            Close();
            return;
        }

        _target = target;
        _escaping = escaping;
        _canSend = canSend;

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
            PlaceholderText = "Write your post. ⏎ starts a new line; the game gets one command with %r breaks.",

            // The screens' own edit colours, so the composer reads as part of this client rather than as
            // the control's grey default sitting in a themed window. Both pairs are set: this control
            // paints from the *focused* pair whenever it has focus, and it always has focus here, so
            // setting only the unfocused one leaves the default grey on screen for the whole session —
            // which is exactly what the first frame of this window showed.
            BackgroundColor = new Color(ScreenPalette.EditBg),
            FocusedBackgroundColor = new Color(ScreenPalette.EditBg),
            ForegroundColor = new Color(ScreenPalette.Value),
            FocusedForegroundColor = new Color(ScreenPalette.Value),
            SelectionBackgroundColor = new Color(ScreenPalette.CursorBg),
            SelectionForegroundColor = new Color(ScreenPalette.Value),
        };

        _editor.SetContent(draft);
        FitEditor();

        _window = new WindowBuilder(_system)
            .AsModal()
            .Maximized()
            .Frameless()
            .WithColors(new Color(ScreenPalette.PanelFg), new Color(ScreenPalette.PanelBg))
            .AddControl(HeaderBand())
            .AddControl(_editor)
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
    }

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
        if (alt && key.Key == ConsoleKey.L)
        {
            e.Handled = true;
            _escaping = _escaping == ComposeEscaping.Literal ? ComposeEscaping.AsTyped : ComposeEscaping.Literal;
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
        Send?.Invoke(this, new ComposeResult(_editor.GetContent(), _escaping));
    }

    /// <summary>Shuts the window, handing the buffer back for the host to keep.</summary>
    internal void Close()
    {
        if (_window is not { } window)
        {
            return;
        }

        var result = new ComposeResult(Body, _escaping);
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
        window.AddControl(FooterBand());
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
        var band = ScreenChrome.Band(Header(), ScreenPalette.HeaderBg);
        band.StickyPosition = StickyPosition.Top;
        return band;
    }

    private MarkupControl FooterBand()
    {
        var band = ScreenChrome.Band(Footer(), ScreenPalette.FooterBg);
        band.StickyPosition = StickyPosition.Bottom;
        return band;
    }

    /// <summary>
    /// The title row: what this is, who it sends to, and which mode it is in. The target is escaped
    /// because a character's name is configuration and a world's name can be anything.
    /// </summary>
    private string Header()
    {
        var target = _target.Length == 0
            ? $"[{ScreenPalette.Warn}]no connection[/]"
            : $"[{ScreenPalette.Value}]{Escape(_target)}[/]";

        var mode = _escaping == ComposeEscaping.Literal
            ? $"[{ScreenPalette.Accent}]literal[/]"
            : $"[{ScreenPalette.Value}]as typed[/]";

        return $"[{ScreenPalette.Accent}]COMPOSE[/][{ScreenPalette.Label}]  to [/]{target}"
            + $"[{ScreenPalette.Label}]  ·  text [/]{mode}";
    }

    /// <summary>
    /// The key row. It names ⌃S, ⌥L and Esc and nothing else, because this screen is held to the same
    /// honesty rule as the settings screens: every key printed here works, and none that works is
    /// hidden. There is deliberately no <c>⌃F find</c> — the control has a find API but no chord bound
    /// to it, and advertising one would send a reader to press a key that does nothing.
    /// </summary>
    private string Footer()
    {
        var send = _canSend
            ? $"[{ScreenPalette.Accent}]⌃S[/][{ScreenPalette.Label}] send[/]"
            : $"[{ScreenPalette.Accent}]⌃S[/][{ScreenPalette.Label}] send (nothing connected)[/]";

        return send
            + $"[{ScreenPalette.Label}] · [/][{ScreenPalette.Accent}]⌥L[/][{ScreenPalette.Label}] escaping[/]"
            + $"[{ScreenPalette.Label}] · [/][{ScreenPalette.Accent}]Esc[/][{ScreenPalette.Label}] close (keeps the draft)[/]";
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

        _editor.ViewportHeight = Math.Max(1, _system.ConsoleDriver.ScreenSize.Height - ChromeRows);
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
    }
}
