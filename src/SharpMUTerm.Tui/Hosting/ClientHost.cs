using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui;

/// <summary>
/// Everything about the place the client runs that the client itself must not decide: which driver draws the
/// cells, what that "terminal" can show, where a copy goes, how a link is opened, whether focus is reported, and
/// which keys the host keeps for itself.
/// <para>
/// <b>Every member is <c>required</c>, and that is the enforcement.</b> The terminal (<see cref="Terminal"/>),
/// the headless harness (<see cref="Headless"/>) and the desktop window (<c>WindowHost</c> in
/// <c>SharpMUTerm.Avalonia</c>) each build one with an object initializer, so a member added here fails to
/// compile until every host has said what it is — including a host that says null. The constructor parameters
/// this replaced were optional, each with a terminal default, which is how a host could be added without
/// anybody being made to think about the clipboard or the newline chord.
/// </para>
/// <para>
/// A new fact that differs between hosts goes here as a required member, never as an optional parameter on
/// <see cref="SharpMUTermApp"/>, and never as a check on the driver's concrete type or on the environment
/// inside the app — the banned-API analyzer refuses those outside <c>Hosting/</c>.
/// </para>
/// </summary>
internal sealed record ClientHost
{
    /// <summary>Which host this is, for diagnostics and for tests that run once per host.</summary>
    public required string Name { get; init; }

    /// <summary>The driver the framework paints through and reads input from.</summary>
    public required IConsoleDriver Driver { get; init; }

    /// <summary>What the driver can show: graphics protocol, truecolour, Sixel overlay, transparent colours.</summary>
    public required TerminalCapabilities Capabilities { get; init; }

    /// <summary>Where a copy goes, or null for a client that refuses to copy and says so.</summary>
    public required Action<string>? Clipboard { get; init; }

    /// <summary>Opens an http(s) link in the desktop's browser, or null for a client that refuses and says so.</summary>
    public required Action<string>? OpenUrl { get; init; }

    /// <summary>Whether to ask for terminal focus reports (<see cref="TerminalFocusWatcher"/>).</summary>
    public required bool FocusReporting { get; init; }

    /// <summary>
    /// What the host does with the pointer cues the client works out — a cursor shape, a tooltip, a right-click
    /// menu, scrollbars drawn over a pane — or null for a host that shows none of them.
    /// </summary>
    public required IPointerPresenter? Pointer { get; init; }

    /// <summary>The keys this host delivers differently from the others, or keeps for itself.</summary>
    public required HostKeys Keys { get; init; }

    /// <summary>
    /// True for a host with no main loop: UI work runs inline, the timestamp column shows a fixed time, and
    /// nothing animates, so a frame is the same on every run. The snapshot and the test suites; never a person.
    /// </summary>
    public required bool RunsInline { get; init; }

    /// <summary>Pixels per cell for the Sixel overlay; read only when the capabilities offer one.</summary>
    public required (int Width, int Height) SixelCell { get; init; }

    /// <summary>
    /// Whether the terminal driver's input path can decode a focus report. Not on Windows: the Windows branch of
    /// <c>NetConsoleDriver</c> is a <c>Console.ReadKey</c> loop with its own reassembly, and an <c>ESC [ I</c>
    /// arriving there would become something else entirely.
    /// </summary>
    public static bool PlatformDecodesFocusReports => !OperatingSystem.IsWindows();

    /// <summary>Snapshot host: a fresh headless driver of the given size.</summary>
    public static ClientHost Snapshot(int width, int height, TerminalCapabilities capabilities) =>
        Headless(new HeadlessConsoleDriver(width, height), capabilities);

    /// <summary>The terminal client as it always was: the framework's own driver, the system clipboard, focus decided by the platform.</summary>
    public static ClientHost Terminal(TerminalCapabilities capabilities)
    {
        var driver = new NetConsoleDriver(RenderMode.Buffer);
        return Terminal(capabilities, driver);
    }

    private static ClientHost Terminal(TerminalCapabilities capabilities, IConsoleDriver driver) => new()
    {
        Name = "terminal",
        Driver = driver,
        Capabilities = capabilities,

        // SharpConsoleUI's helper writes OSC 52 *and* mirrors to the platform tool (wl-copy / xclip / pbcopy /
        // Win32), so a copy lands whether the client is local or on the far end of an ssh session.
        Clipboard = SharpConsoleUI.Helpers.ClipboardHelper.SetText,
        OpenUrl = ExternalBrowser.Open,
        FocusReporting = PlatformDecodesFocusReports,
        Keys = HostKeys.Terminal,
        Pointer = TerminalPointer.Create(driver),
        RunsInline = false,
        SixelCell = SharpMUTermApp.SixelCellSize(Environment.GetEnvironmentVariable(SharpMUTermApp.SixelCellVariable)),
    };

    /// <summary>
    /// The snapshot's and the tests' host: whatever driver it is handed, no clipboard, no browser, no focus
    /// reports, and no loop. It reports the terminal's keys, because the snapshot is the terminal's gallery.
    /// </summary>
    public static ClientHost Headless(IConsoleDriver driver, TerminalCapabilities capabilities) => new()
    {
        Name = "headless",
        Driver = driver,
        Capabilities = capabilities,
        Clipboard = null,
        OpenUrl = null,
        FocusReporting = false,
        Keys = HostKeys.Terminal,
        Pointer = null,
        RunsInline = true,
        SixelCell = SharpMUTermApp.SixelCellSize(null),
    };

    /// <summary>
    /// The host behind <see cref="SharpMUTermApp"/>'s loose-parameter constructor, which the test suites use:
    /// headless when handed a <see cref="HeadlessConsoleDriver"/> (or nothing loop-free at all, the terminal), with
    /// whatever the test overrides. Not a host a launcher may use — those build theirs whole.
    /// </summary>
    public static ClientHost ForHarness(
        TerminalCapabilities capabilities,
        IConsoleDriver? driver,
        bool? focusReporting,
        Action<string>? openUrl,
        Action<string>? clipboard)
    {
        if (driver is null)
        {
            return Terminal(capabilities) with { Clipboard = clipboard, OpenUrl = openUrl, FocusReporting = focusReporting ?? PlatformDecodesFocusReports };
        }

        var inline = driver is HeadlessConsoleDriver;
        return Headless(driver, capabilities) with
        {
            Name = inline ? "headless" : "harness",
            Clipboard = clipboard,
            OpenUrl = openUrl,
            FocusReporting = focusReporting ?? false,
            RunsInline = inline,
        };
    }
}
