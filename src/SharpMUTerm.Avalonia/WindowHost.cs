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
    };

    public static ClientHost Create(AvaloniaConsoleDriver driver, Action<string>? clipboard) => new()
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
    };
}
