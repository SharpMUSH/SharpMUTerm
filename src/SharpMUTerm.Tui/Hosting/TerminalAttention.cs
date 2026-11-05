using System.Text;
using SharpConsoleUI.Drivers;

namespace SharpMUTerm.Tui;

/// <summary>An alert for the desktop: a short title (<c>Corvid · Aetherfall — page</c>) and the line that raised it.</summary>
internal sealed record AttentionRequest(string Title, string Body);

/// <summary>
/// How a host tells the desktop that something wants the player while they are in another window
/// (<see cref="ClientHost.Attention"/>): a notification and a taskbar flash in the window, a notification escape or a
/// bell in a terminal. The client decides <em>whether</em> to ask — a rule or a world asked, F7 allows it, the rate
/// limit has room, the player is not looking — and the host only says it.
/// </summary>
internal interface IAttention
{
    /// <summary>
    /// True when the host itself shows a request only while it is not in front (kitty's <c>o=unfocused</c>, the
    /// window's own focus), so the client need not guess at absence from the time since the last key.
    /// </summary>
    bool FiltersByFocus { get; }

    /// <summary>Whether the host is in front, as last known; null when it cannot tell (a terminal).</summary>
    bool? Focused { get; }

    /// <summary>Raised, on any thread, when <see cref="Focused"/> changes.</summary>
    event Action<bool>? FocusChanged;

    /// <summary>Says <paramref name="request"/>. Called on the client's thread.</summary>
    void Request(AttentionRequest request);
}

/// <summary>Which escape a terminal shows a notification for.</summary>
internal enum TerminalNotify
{
    /// <summary>BEL: every terminal; most mark the window urgent while it is not in front.</summary>
    Bell,

    /// <summary><c>OSC 777 ; notify ; title ; body</c>: foot, WezTerm, Ghostty, urxvt's extension.</summary>
    Osc777,

    /// <summary><c>OSC 99</c>, kitty's desktop notifications, shown only while the window is not in front.</summary>
    Osc99,
}

/// <summary>
/// The terminal's way to get attention: kitty's <c>OSC 99</c> where the terminal is kitty, <c>OSC 777</c> where it is
/// one of the terminals that read it, else a bell. Written through the driver's one raw write that is serialised
/// against frame painting, the path <see cref="TerminalTitle"/> and <see cref="TerminalFocusWatcher"/> use.
/// <para>
/// <b>A terminal cannot tell the client it is in the background</b> (its focus-out report is dropped by the
/// framework's parser, see <see cref="TerminalFocusWatcher"/>), so <see cref="Focused"/> is null. kitty decides for
/// itself (<c>o=unfocused</c>); for the others the client asks only after a stretch with no input.
/// </para>
/// </summary>
internal sealed class TerminalAttention(IConsoleDriver driver, TerminalNotify kind) : IAttention
{
    /// <summary>The longest a title or body is written into an escape.</summary>
    internal const int MaxText = 200;

    private int _sequence;

    public bool FiltersByFocus => kind == TerminalNotify.Osc99;

    public bool? Focused => null;

    public event Action<bool>? FocusChanged
    {
        add { }
        remove { }
    }

    /// <summary>Which escape this terminal reads, from what it says about itself.</summary>
    public static TerminalNotify Detect()
    {
        var term = Environment.GetEnvironmentVariable("TERM") ?? string.Empty;
        var program = Environment.GetEnvironmentVariable("TERM_PROGRAM") ?? string.Empty;
        if (term == "xterm-kitty" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KITTY_WINDOW_ID")))
        {
            return TerminalNotify.Osc99;
        }

        return program.Equals("WezTerm", StringComparison.OrdinalIgnoreCase)
            || program.Equals("ghostty", StringComparison.OrdinalIgnoreCase)
            || term.StartsWith("foot", StringComparison.Ordinal)
            || term == "xterm-ghostty"
                ? TerminalNotify.Osc777
                : TerminalNotify.Bell;
    }

    public void Request(AttentionRequest request) =>
        driver.WriteClipboardOsc52(Sequence(kind, request, ++_sequence));

    /// <summary>The bytes for <paramref name="request"/> in <paramref name="kind"/>'s escape.</summary>
    internal static string Sequence(TerminalNotify kind, AttentionRequest request, int id) => kind switch
    {
        // kitty: the title, then the body, under one id; base64 so nothing in a game's line can end the escape early.
        TerminalNotify.Osc99 =>
            $"\u001b]99;i=smt{id}:d=0:o=unfocused:e=1;{Base64(request.Title)}\u001b\\"
            + $"\u001b]99;i=smt{id}:d=1:o=unfocused:p=body:e=1;{Base64(request.Body)}\u001b\\",

        // A ';' would start a new field, so it is written as a comma.
        TerminalNotify.Osc777 => $"\u001b]777;notify;{Field(request.Title)};{Field(request.Body)}\u001b\\",
        _ => "\a",
    };

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(Clean(text)));

    private static string Field(string text) => Clean(text).Replace(';', ',');

    private static string Clean(string text)
    {
        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > MaxText ? clean[..(MaxText - 1)] + "…" : clean;
    }
}
