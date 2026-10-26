using SharpConsoleUI.Drivers;

namespace SharpMUTerm.Tui;

/// <summary>
/// The terminal's share of the pointer cues: the pointer shape, through OSC 22 (<c>ESC ] 22 ; name ST</c>),
/// which kitty, Ghostty and foot implement. The shape is the only one of them a terminal can show — it has no
/// tooltip and no menu of its own, and a scrollbar drawn in the cells would narrow the pane. The divider
/// highlight needs none of this: it is drawn in the cells, so every terminal with motion reporting has it.
/// <para>
/// Other terminals get no presenter at all rather than the sequence, because an OSC a terminal does not know is
/// usually ignored and occasionally printed, and nothing here can tell which.
/// </para>
/// </summary>
internal sealed class TerminalPointer(IConsoleDriver driver) : IPointerPresenter
{
    private PointerShape _shape = PointerShape.Default;

    /// <summary>A presenter for this terminal, or null when it is not one known to take OSC 22.</summary>
    public static IPointerPresenter? Create(IConsoleDriver driver) => Supported() ? new TerminalPointer(driver) : null;

    private static bool Supported()
    {
        var term = Environment.GetEnvironmentVariable("TERM") ?? string.Empty;
        var program = Environment.GetEnvironmentVariable("TERM_PROGRAM") ?? string.Empty;
        return !OperatingSystem.IsWindows() && (
            term.Contains("kitty", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KITTY_WINDOW_ID"))
            || program.Equals("ghostty", StringComparison.OrdinalIgnoreCase)
            || term.StartsWith("foot", StringComparison.Ordinal));
    }

    /// <summary>The OSC 22 name for a shape: the CSS cursor names these terminals take.</summary>
    internal static string NameOf(PointerShape shape) => shape switch
    {
        PointerShape.Text => "text",
        PointerShape.Hand => "pointer",
        PointerShape.ResizeColumns => "col-resize",
        PointerShape.ResizeRows => "row-resize",
        _ => "default",
    };

    public void Hover(PointerTarget target)
    {
        if (target.Shape == _shape)
        {
            return;
        }

        _shape = target.Shape;

        // The framework's one public raw write that is serialised against frame painting; see TerminalFocusWatcher.
        driver.WriteClipboardOsc52($"\u001b]22;{NameOf(target.Shape)}\u001b\\");
    }

    public void ShowMenu(System.Drawing.Point cell, IReadOnlyList<PointerMenuItem> items)
    {
    }

    public bool DrawsMenus => false;

    public void Scrollbars(IReadOnlyList<PaneScrollbar> bars)
    {
    }
}
