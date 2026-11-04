using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The desktop window as a <see cref="ClientHost"/>: this driver, the window's clipboard, no focus reports, and
/// the keys a window delivers differently from a terminal. <see cref="TerminalView"/> acts on
/// <see cref="Maximise"/> and <see cref="Paste"/> by reading them from here, so the chords the window keeps and
/// the chords the client's surfaces are told it keeps are one list.
/// </summary>
internal static class WindowHost
{
    /// <summary>⌥⏎ maximises and restores, as in Windows Terminal. The client's ⌥⏎ newline is the cost.</summary>
    public static ReservedChord Maximise { get; } = new(
        new HostChord(ConsoleKey.Enter, ConsoleModifiers.Alt, "Alt+⏎", "Alt+Enter"), "maximises and restores the window");

    /// <summary>Paste is the window's, on the terminal conventions, so a bare ⌃V still reaches the client as a key.</summary>
    public static IReadOnlyList<ReservedChord> Paste { get; } =
    [
        new(new HostChord(ConsoleKey.V, ConsoleModifiers.Control | ConsoleModifiers.Shift, "Ctrl+Shift+V", "Ctrl+Shift+V"), "paste (a bare Ctrl+V reaches the client as a key)"),
        new(new HostChord(ConsoleKey.Insert, ConsoleModifiers.Shift, "Shift+Insert", "Shift+Insert"), "paste (a bare Ctrl+V reaches the client as a key)"),
    ];

    /// <summary>
    /// A window reports Ctrl and Shift on Enter, so either reaches the command line's modified-⏎ arm as a
    /// newline. ⌃⏎ is the one surfaces name.
    /// </summary>
    public static HostKeys Keys { get; } = new()
    {
        Newline = new HostChord(ConsoleKey.Enter, ConsoleModifiers.Control, "Ctrl+⏎", "Ctrl+Enter"),
        Reserved = [Maximise, .. Paste],

        // Avalonia reports the key and its modifiers: ⌃digit, ⌃⇧letter, ⌃Tab and ⌥O arrive as themselves.
        ReportsChordsWhole = true,

        // KeyMapper reads Key.NumPad0–9 (Num Lock on) and hands the client ConsoleKey.NumPad0–9 with the digit.
        DeliversNumpad = true,
    };

    /// <param name="driver">The window's driver.</param>
    /// <param name="clipboard">Where a copy goes.</param>
    /// <param name="chrome">
    /// Which chrome the window draws natively. The header and the rail by default, which is the window a person runs;
    /// <see cref="HostChrome.None"/> for <c>--render-png</c> and <c>--cell-chrome</c>, where the cells carry it all.
    /// </param>
    public static ClientHost Create(AvaloniaConsoleDriver driver, Action<string>? clipboard, HostChrome? chrome = null) => new()
    {
        Name = "window",
        Driver = driver,
        Capabilities = WindowOptions.Capabilities,
        Clipboard = clipboard,
        OpenUrl = ExternalBrowser.Open,

        // Focus reporting is a terminal escape, and its focus-in arrives disguised as a Tab: in a window it
        // would only ever turn a real Tab after a pause into a "you were away" bar.
        FocusReporting = false,
        Keys = Keys,
        RunsInline = false,
        Pointer = driver.Pointer,
        SixelCell = SharpMUTermApp.SixelCellSize(null),

        // The same desktop device as the terminal: both are desktop processes on the same machine.
        Audio = () => SharpMUTerm.Audio.DesktopAudio.TryCreate(),

        // Discord's IPC is a local socket either host can reach.
        Presence = () => new SharpMUTerm.Core.Presence.DiscordIpc(),

        // A live region a screen reader watches; the terminal has none, and --plain is its answer.
        Announcer = driver.Announcer,
        ScreenIsSeen = true,

        // F7 and F8 open a native settings window generated from their declarations; the rest stay cell screens.
        NativeSettings = driver.Settings,

        // The header band and the connections sidebar are the window's own controls, beside the cell grid rather
        // than over it (MainWindow, DesktopLayer): the client draws neither.
        Chrome = chrome ?? HostChrome.HeaderAndRail,
        // ⌃P is a native palette over the window, listing the same rows as the cell one.
        NativePalette = driver.Palette,

        // F1 and the docked composer are a native editor: a proportional or monospace face, IME, native selection and
        // a real underline under a misspelling. The draft, the send and the keys stay the client's.
        NativeComposer = driver.Composer,

        // The quit prompt, ⌃F, ⌃R, ⌥M and the welcome are native modals over the window, the prompt always in front.
        NativeModals = driver.Modals,
        // The title bar and the taskbar entry say who is in front: Character@World — SharpMUTerm.
        Title = driver.TitleBar,

        // A panel popped out is a window of its own, owned by the main one, for a second monitor.
        FloatingPanels = driver.Floating,

        // A page while the window is behind another: a desktop notification and the taskbar's attention state.
        Attention = driver.Attention,

        // An import from another client starts at the desktop's own file picker.
        FilePicker = driver.FilePicker,
    };
}
