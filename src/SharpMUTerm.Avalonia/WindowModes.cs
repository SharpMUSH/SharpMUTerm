using Avalonia.Threading;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The main window's full screen, windowed full screen, maximised and windowed states, as the client asks for them
/// (<see cref="ClientHost.WindowModes"/>: ⌃P, the right-click menu, the Window settings page). The window does the work
/// (<see cref="MainWindow.SetMode"/>) and reports every change back here, whoever made it — F11, ⌥⏎, the window manager.
/// Not available until the main window attaches, so a test, the parity harness and <c>--render-png</c> offer none of it.
/// </summary>
internal sealed class WindowModes : IWindowModes
{
    private MainWindow? _window;
    private int _mode;

    public bool Available => Volatile.Read(ref _window) is not null;

    public WindowMode Mode => (WindowMode)Volatile.Read(ref _mode);

    public event Action? Changed;

    /// <summary>The window the modes apply to.</summary>
    public void Attach(MainWindow window) => Volatile.Write(ref _window, window);

    public void Toggle(WindowMode mode) => Dispatcher.UIThread.Post(() => _window?.ToggleMode(mode));

    /// <summary>The window's mode changed; on the window's thread.</summary>
    internal void Note(WindowMode mode)
    {
        if (Interlocked.Exchange(ref _mode, (int)mode) != (int)mode)
        {
            Changed?.Invoke();
        }
    }
}
