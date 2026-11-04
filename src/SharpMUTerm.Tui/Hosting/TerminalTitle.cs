using SharpConsoleUI.Drivers;

namespace SharpMUTerm.Tui;

/// <summary>Where the client's title goes (<see cref="SharpMUTerm.Client.Chrome.WindowTitle"/>), as a host shows it.</summary>
internal interface IWindowTitle
{
    /// <summary>Shows <paramref name="title"/>. Called on the client's thread, only when the title changed.</summary>
    void Show(string title);

    /// <summary>Gives the title back to whatever had it before the client: called once, as the client's loop ends.</summary>
    void Restore();
}

/// <summary>
/// The terminal's title (its window or tab), set with OSC 2 (<c>ESC ] 2 ; title ST</c>), which every terminal this
/// client runs in implements. Written through the driver's one raw write that is serialised against frame painting,
/// the path <see cref="TerminalFocusWatcher"/> uses for <c>?1004</c>.
/// <para>
/// Before the first title the terminal is asked to keep the one it had (xterm's title stack, <c>CSI 22 ; 2 t</c>), and
/// on the way out to put it back (<c>CSI 23 ; 2 t</c>), so the shell's own title returns when the client quits.
/// kitty, foot, WezTerm, xterm and VTE honour the stack; a terminal that does not ignores the request, and keeps the
/// client's last title until the shell sets its own.
/// </para>
/// </summary>
internal sealed class TerminalTitle(IConsoleDriver driver) : IWindowTitle
{
    private const string PushTitle = "\u001b[22;2t";
    private const string PopTitle = "\u001b[23;2t";
    private bool _pushed;

    public void Show(string title)
    {
        var prefix = _pushed ? string.Empty : PushTitle;
        _pushed = true;
        driver.WriteClipboardOsc52($"{prefix}{Sequence(title)}");
    }

    public void Restore()
    {
        if (_pushed)
        {
            _pushed = false;
            driver.WriteClipboardOsc52(PopTitle);
        }
    }

    /// <summary>The OSC 2 for <paramref name="title"/>, with every control character taken out so nothing ends it early.</summary>
    internal static string Sequence(string title)
    {
        var clean = new string(title.Where(c => !char.IsControl(c)).ToArray());
        return $"\u001b]2;{clean}\u001b\\";
    }
}
