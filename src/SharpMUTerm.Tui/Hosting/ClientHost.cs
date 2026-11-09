using SharpConsoleUI.Drivers;
using SharpMUTerm.Audio;
using SharpMUTerm.Core.Accessibility;
using SharpMUTerm.Core.Media;
using SharpMUTerm.Core.Presence;
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

    /// <summary>
    /// Opens a file the client wrote (a saved scene log) in the desktop's viewer for it, or null for a client that refuses
    /// and says so. The client checks the file is its own (under the log root); the opener refuses anything but a log's
    /// own kinds of file (<see cref="ExternalBrowser.OpenFile"/>).
    /// </summary>
    public required Action<string>? OpenFile { get; init; }

    /// <summary>Whether to ask for terminal focus reports (<see cref="TerminalFocusWatcher"/>).</summary>
    public required bool FocusReporting { get; init; }

    /// <summary>
    /// What the host does with the pointer cues the client works out — a cursor shape, a tooltip, a right-click
    /// menu, scrollbars drawn over a pane — or null for a host that shows none of them.
    /// </summary>
    public required IPointerPresenter? Pointer { get; init; }

    /// <summary>Measured physical cell pixels, independent of pointer presentation; null when unavailable.</summary>
    public required CellMetrics? CellMetrics { get; init; }

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
    /// Opens the sound device, called the first time a game asks for a sound, so a client whose games send
    /// none never opens one. Null for no sound at all — the snapshot and the tests, which must never make a
    /// noise; a factory that returns null is a machine with no device.
    /// </summary>
    public required Func<IAudioOutput?>? Audio { get; init; }

    /// <summary>
    /// Opens where a game's Discord presence is shown, called the first time a world that allows it has something
    /// to show. Null for nowhere: the snapshot and the tests, which must never reach a Discord on the machine.
    /// </summary>
    public required Func<IPresence?>? Presence { get; init; }

    /// <summary>
    /// Where output lines, the client's messages and window changes go for a screen reader: the window's live
    /// region, or <c>--plain</c>'s stdout. Null in a terminal, whose screen a reader cannot follow, and in the
    /// snapshot and the tests.
    /// </summary>
    public required IAnnouncer? Announcer { get; init; }

    /// <summary>
    /// Whether anybody looks at the cells. False for <c>--plain</c>, whose screen is a headless buffer nobody
    /// reads: a surface the client would open as a modal there (the first-run welcome) is said as lines through
    /// <see cref="Announcer"/> and worked by typed commands instead, because a modal there would take the keys
    /// the reader types and show them nothing.
    /// </summary>
    public required bool ScreenIsSeen { get; init; }

    /// <summary>
    /// Draws the declared settings sections (F7, F8) with the host's own controls, or null for a host whose settings
    /// are the cell screens. Only a section that declares its fields goes here; the others keep their cell screen.
    /// Null in the terminal, the snapshot, the tests and the parity harness, so every cell frame is unchanged.
    /// </summary>
    public required INativeSettings? NativeSettings { get; init; }

    /// <summary>
    /// Which of the client's chrome the host draws itself, outside the cells (<see cref="HostChrome"/>). The client
    /// draws no cell for a part the host draws: no header row when <see cref="HostChrome.Header"/>, no rail column
    /// when <see cref="HostChrome.Rail"/>, so the cell grid the host hands the driver is only what is left. None in
    /// the terminal, the snapshot and the tests, so their frames are the ones they always were.
    /// </summary>
    public required HostChrome Chrome { get; init; }

    /// <summary>
    /// Draws ⌃P with the host's own controls, or null for a host whose palette is the cell popup. Every way in (⌃P,
    /// the header, the pointer menu's <em>More commands…</em>) goes through one funnel that asks this first. Null in
    /// the terminal, the snapshot, the tests and the parity harness, so every cell frame is unchanged.
    /// </summary>
    public required INativePalette? NativePalette { get; init; }

    /// <summary>
    /// Draws the composer — F1 and the docked panel — with the host's own editor, or null for a host whose composer is
    /// cells. F1 opens there instead of the cell modal; a docked composer keeps its cells and the host's editor lies
    /// over them, kept to the same draft. Null in the terminal, the snapshot, the tests and the parity harness, so every
    /// cell frame is unchanged.
    /// </summary>
    public required INativeComposer? NativeComposer { get; init; }

    /// <summary>
    /// Draws the client's modals — the quit prompt, ⌃F search, ⌃R history, the ⌥M layouts list and the first-run welcome —
    /// with the host's own controls, or null for a host whose modals are cell windows. One member for all five, because
    /// their stacking (the quit prompt always in front), focus and theme are the host's to get right once. Null in the
    /// terminal, the snapshot, the tests and the parity harness, so every cell frame is unchanged.
    /// </summary>
    public required INativeModals? NativeModals { get; init; }

    /// <summary>
    /// Where the title for what is in front goes (<c>Character@World — SharpMUTerm</c>,
    /// <see cref="SharpMUTerm.Client.Chrome.WindowTitle"/>): the desktop window's title bar, or the terminal's own
    /// title through OSC 2. Null in the snapshot, the tests and <c>--plain</c>, which have no title to set.
    /// </summary>
    public required IWindowTitle? Title { get; init; }

    /// <summary>
    /// Draws panels popped out into windows of their own — the map on a second monitor, vitals floating beside the
    /// game — or null for a host that cannot put a panel anywhere but its pane (the terminal, the snapshot, the tests,
    /// <c>--plain</c>). <b>One member is both the fact and the channel</b>: a flag beside a presenter could say "can
    /// float" with nothing to float into. Where it is null the pop-out commands are not offered (⌃P lists none, the pane
    /// menu has no entry, no native view carries the button) and a floating panel, from a layout or a workspace another
    /// host saved, is drawn in its region as cells. It is not part of <see cref="Chrome"/>: that says which of the
    /// client's own rows and columns the host draws beside the cells, while a floating panel leaves the cells entirely.
    /// </summary>
    public required IFloatingPanels? FloatingPanels { get; init; }

    /// <summary>
    /// How the host tells the desktop that a page, a mention or an alert trigger arrived while the player was in another
    /// window (<see cref="IAttention"/>): the window's notification and taskbar flash, the terminal's <c>OSC 99</c>,
    /// <c>OSC 777</c> or bell. Null in the snapshot, the tests and <c>--plain</c>, which must never reach a desktop.
    /// </summary>
    public required IAttention? Attention { get; init; }

    /// <summary>
    /// The desktop's file picker, for an import from another client (<see cref="IFilePicker"/>), or null where there is
    /// none: the terminal (a path is typed after <c>/import</c>), the snapshot, the tests and <c>--plain</c>.
    /// </summary>
    public required IFilePicker? FilePicker { get; init; }

    /// <summary>
    /// Full screen, windowed full screen, maximised and windowed, for a host that is a desktop window
    /// (<see cref="IWindowModes"/>); null in the terminal, the snapshot, the tests and <c>--plain</c>, where the client
    /// offers none of them.
    /// </summary>
    public required IWindowModes? WindowModes { get; init; }

    /// <summary>
    /// Draws F7's background transparency itself (<see cref="IWindowTranslucency"/>): the window, which paints every plane
    /// at the level over a translucent desktop window. Null where the backgrounds are a terminal's to show: the terminal
    /// (kitty is told the level through its color-control escape, any other terminal is handed its default background),
    /// the snapshot, the tests and <c>--plain</c>.
    /// </summary>
    public required IWindowTranslucency? Translucency { get; init; }

    /// <summary>
    /// Finding, downloading and applying a newer release (<see cref="VelopackUpdates"/>): only an installed copy of
    /// the terminal or the window has one. Null for a build run from a folder, the snapshot, the tests, <c>--plain</c>
    /// and the window's demo, so none of them asks the network anything.
    /// </summary>
    public required IUpdates? Updates { get; init; }

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
        OpenFile = ExternalBrowser.OpenFile,
        FocusReporting = PlatformDecodesFocusReports,
        Keys = HostKeys.Terminal,
        Pointer = TerminalPointer.Create(driver),
        CellMetrics = new TerminalCellMetrics(),
        RunsInline = false,
        SixelCell = SharpMUTermApp.SixelCellSize(Environment.GetEnvironmentVariable(SharpMUTermApp.SixelCellVariable)),
        Audio = () => DesktopAudio.TryCreate(),
        Presence = () => new DiscordIpc(),
        Announcer = null,
        ScreenIsSeen = true,
        NativeSettings = null,
        Chrome = HostChrome.None,
        NativePalette = null,
        NativeComposer = null,
        NativeModals = null,
        Title = new TerminalTitle(driver),
        FloatingPanels = null,
        Attention = new TerminalAttention(driver, TerminalAttention.Detect()),
        FilePicker = null,
        WindowModes = null,
        Translucency = null,
        Updates = VelopackUpdates.Detect(window: false),
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
        OpenFile = null,
        FocusReporting = false,
        Keys = HostKeys.Terminal,
        Pointer = null,
        CellMetrics = null,
        RunsInline = true,
        SixelCell = SharpMUTermApp.SixelCellSize(null),
        Audio = null,
        Presence = null,
        Announcer = null,
        ScreenIsSeen = true,
        NativeSettings = null,
        Chrome = HostChrome.None,
        NativePalette = null,
        NativeComposer = null,
        NativeModals = null,
        Title = null,
        FloatingPanels = null,
        Attention = null,
        FilePicker = null,
        WindowModes = null,
        Translucency = null,
        Updates = null,
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
            return HarnessTerminal(capabilities, new NetConsoleDriver(RenderMode.Buffer), focusReporting, openUrl, clipboard);
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

    /// <summary>
    /// <see cref="ForHarness"/> with no driver of its own: the terminal host on <paramref name="driver"/>, with
    /// what a test overrides. Apart so a test can read it on a driver that needs no console, because the
    /// framework's own driver throws on Windows when stdin and stdout are redirected, as they are in CI.
    /// </summary>
    internal static ClientHost HarnessTerminal(
        TerminalCapabilities capabilities,
        IConsoleDriver driver,
        bool? focusReporting,
        Action<string>? openUrl,
        Action<string>? clipboard) =>
        Terminal(capabilities, driver) with
        {
            Clipboard = clipboard,
            OpenUrl = openUrl,
            OpenFile = null, // a test never opens a viewer
            FocusReporting = focusReporting ?? PlatformDecodesFocusReports,
            Audio = null, // a test never makes a noise
            Presence = null, // nor talks to Discord
            Title = null, // nor retitles the terminal running the suite
            Attention = null, // nor rings its bell
            Updates = null, // nor asks GitHub for a release
        };
}

/// <summary>
/// The parts of the client's chrome a host draws with its own controls, outside the cell grid. The space they take is
/// the host's business, in its own units (the window's header is 42 pixels, not a row): the client is told only
/// that the part is not its to draw, and leaves no cell for it. So nothing is drawn twice, nothing native sits over a
/// cell it hides, and every count the client makes of its own rows — the input-height veto, the layout, the pane
/// rectangles and therefore NAWS — is made over the cells that really exist.
/// </summary>
/// <param name="Header">The host draws the header band (the client's first row).</param>
/// <param name="Rail">The host draws the connection rail (the client's left column, with its divider and spacer).</param>
/// <param name="Status">
/// The host draws the status row (the client's last row) from <see cref="SharpMUTerm.Client.Chrome.StatusModel"/>,
/// published through <see cref="IPointerPresenter.Status"/>.
/// </param>
/// <param name="Input">
/// The host draws the command line's field around the cell editor: the client keeps the editor, its keys and its band
/// colours, pads it (<see cref="InputLayout.HostFieldPad"/> band-coloured columns either side) and says where its rows are
/// each frame (<see cref="IPointerPresenter.InputBand"/>), so the host can draw the field's frame and padding in its own
/// units around those rows. No keystroke path changes.
/// </param>
/// <param name="Tabs">
/// The host draws each pane's tab strip. A strip's row cannot be taken out of one pane of a uniform cell grid, so this
/// one part keeps its row: the client paints it blank and publishes the strip as data (<c>TabStripModel</c>), and the
/// host draws over exactly those rows. The strip is two rows (the framework's <c>Separator</c> header style), so the
/// host has the depth a desktop tab wants; the pane rectangles, and so NAWS, are the panes less those two rows.
/// </param>
/// <param name="PaneGutter">
/// The client leaves two blank columns of the pane's own plane either side of every pane's content (the tab control's
/// margin) and a blank row above an output window's first line (not above a panel or a map, which the host draws over
/// the whole pane), so output starts the mockup's ~20 px in from the pane's edges. The cells are ones the pane really
/// loses: the output rectangles, and so NAWS, are the smaller ones.
/// </param>
/// <param name="InputInColumn">
/// The command line (its prompt row and both bars) is a row of the output's column rather than a band across every pane:
/// as wide as the column the output is in (<see cref="SharpMUTerm.Core.Workspaces.InputColumn"/>), with the panes beside
/// it running down to the status row. Only cells move; the pane rectangles, and so NAWS, are what is arranged.
/// </param>
/// <param name="Strips">
/// The host draws the strip panels as desktop strips: the quick bar gets <c>PanelSizing.QuickBarStripRows</c> rows for
/// its buttons, and a panel a layout collapses gets a pane of its own at the foot of its column, drawn as a one-line bar
/// (<c>CollapsedPanelModel</c>) a click expands, rather than a background tab of the pane above it. Rows only; the pane
/// rectangles, and so NAWS, are what is arranged.
/// </param>
internal sealed record HostChrome(
    bool Header, bool Rail, bool Status = false, bool Input = false, bool Tabs = false, bool PaneGutter = false, bool InputInColumn = false,
    bool Strips = false)
{
    /// <summary>The client draws all of its chrome in cells: the terminal, the snapshot, the tests.</summary>
    public static HostChrome None { get; } = new(false, false);

    /// <summary>The host draws the header and the rail natively.</summary>
    public static HostChrome HeaderAndRail { get; } = new(true, true);

    /// <summary>
    /// The host draws the header, the rail, the status footer, the command line's field and every pane's tab strip, and
    /// the client pads every pane with a gutter and puts the command line in the output's column: the desktop window.
    /// </summary>
    public static HostChrome Desktop { get; } = new(true, true, true, true, true, true, true, true);

    /// <summary>The same arrangement as <see cref="Desktop"/>; the name the tab strip's tests use.</summary>
    public static HostChrome Native => Desktop;

    /// <summary>Whether the host draws anything at all.</summary>
    public bool Any => Header || Rail || Status || Input || Tabs;
}
