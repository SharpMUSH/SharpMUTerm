using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's half of <see cref="IAttention"/>. The main window says when it is activated and deactivated
/// (<see cref="SetFocused"/>); the client hands a request over on its own thread, and the main window, which
/// subscribes to <see cref="Requested"/>, puts it on screen on Avalonia's thread — but only while it is not the active
/// window, which is why <see cref="FiltersByFocus"/> is true.
/// </summary>
internal sealed class WindowAttention : IAttention
{
    private volatile int _focused = -1;

    public bool FiltersByFocus => true;

    public bool? Focused => _focused switch
    {
        0 => false,
        1 => true,
        _ => null,
    };

    public event Action<bool>? FocusChanged;

    /// <summary>Raised on the client's thread with each request.</summary>
    public event Action<AttentionRequest>? Requested;

    public void Request(AttentionRequest request) => Requested?.Invoke(request);

    /// <summary>Called by the main window on activation (true) and deactivation (false).</summary>
    public void SetFocused(bool focused)
    {
        var value = focused ? 1 : 0;
        if (Interlocked.Exchange(ref _focused, value) != value)
        {
            FocusChanged?.Invoke(focused);
        }
    }
}

/// <summary>
/// What the desktop does with an alert: a notification, and on Windows the taskbar button's flash. Every way out of
/// the process is a fixed program with the text as separate arguments (<see cref="ProcessStartInfo.ArgumentList"/>),
/// never a shell string, and a missing program is a quiet no-op: an alert is a courtesy, never an error.
/// <para>
/// Linux: <c>notify-send</c> (libnotify, on every desktop that runs a notification daemon). macOS: <c>osascript</c>,
/// the title and body passed as <c>argv</c> to a fixed script. Windows: <c>FlashWindowEx</c> until the window is
/// brought forward — a toast needs an installed AppUserModelID this client does not register. Avalonia has no
/// urgency API of its own, so an X11 window manager's urgency hint is not set.
/// </para>
/// </summary>
internal static class DesktopAlert
{
    /// <summary>Shows <paramref name="request"/> for the window behind <paramref name="handle"/> (null where there is none).</summary>
    public static void Show(AttentionRequest request, IntPtr? handle)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (handle is { } hwnd && hwnd != IntPtr.Zero)
                {
                    Flash(hwnd);
                }

                return;
            }

            if (OperatingSystem.IsMacOS())
            {
                Run("osascript",
                    ["-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv)", "-e", "end run",
                     request.Title, request.Body]);
                return;
            }

            Run("notify-send", ["--app-name=SharpMUTerm", "--", request.Title, request.Body]);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // No notifier on this machine: the title bar's count still says it.
        }
    }

    private static void Run(string program, IReadOnlyList<string> arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
    }

    private static void Flash(IntPtr hwnd)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = hwnd,
            Flags = FlashAll | FlashUntilForeground,
            Count = 0,
            Timeout = 0,
        };
        FlashWindowEx(ref info);
    }

    private const uint FlashAll = 3;
    private const uint FlashUntilForeground = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
