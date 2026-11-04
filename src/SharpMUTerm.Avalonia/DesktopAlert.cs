using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>Which desktop the window is on, as far as an alert cares.</summary>
internal enum DesktopPlatform
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>A fixed program and its arguments, each passed as one <c>argv</c> entry, never a shell string.</summary>
internal sealed record AlertLaunch(string Program, IReadOnlyList<string> Arguments);

/// <summary>
/// Everything an alert does to the machine, behind one seam so the decisions in <see cref="DesktopAlert"/> are tested
/// with a fake (<see cref="DesktopNative"/> is the real one).
/// </summary>
internal interface IDesktopNative
{
    /// <summary>Starts <paramref name="launch"/> and does not wait for it.</summary>
    void Launch(AlertLaunch launch);

    /// <summary>Flashes the taskbar button of <paramref name="hwnd"/> until it is brought forward (Windows).</summary>
    void Flash(IntPtr hwnd);

    /// <summary>Sets or clears the X11 urgency hint of the window <paramref name="xid"/>; false when it could not.</summary>
    bool SetUrgency(IntPtr xid, bool urgent);

    /// <summary>
    /// Whether the installer registered <see cref="WindowsAppId.Value"/> (Windows), which is what lets a toast be
    /// shown under it. A portable copy has none and gets the flash alone.
    /// </summary>
    bool ToastRegistered { get; }
}

/// <summary>
/// What the desktop does with an alert, per platform. Every way out of the process is a fixed program with the text as
/// separate arguments, and every failure is a quiet no-op: an alert is a courtesy, never an error.
/// <list type="bullet">
/// <item><b>Windows</b>: <c>FlashWindowEx</c> until the window is brought forward, and a toast through
/// <c>ToastNotificationManager</c> under the AppUserModelID the installer registered (<see cref="WindowsToast"/>). The
/// toast is shown by Windows PowerShell 5.1, which can reach WinRT from a script; the text travels base64-encoded inside
/// <c>-EncodedCommand</c>, so nothing a server sends can become script.</item>
/// <item><b>macOS</b>: <c>/usr/bin/osascript</c> with a fixed <c>display notification</c> script and the title and body
/// as <c>argv</c>. macOS files it under Script Editor and asks the player once whether to allow it.</item>
/// <item><b>Linux</b>: <c>notify-send</c>, and on X11 the window's urgency hint (<c>WM_HINTS</c> UrgencyHint and
/// <c>_NET_WM_STATE_DEMANDS_ATTENTION</c>, <see cref="X11Urgency"/>), cleared when the window is activated. Avalonia
/// has no Wayland back end, so under Wayland the window is an XWayland client and the hint goes through XWayland.</item>
/// </list>
/// </summary>
internal sealed class DesktopAlert(DesktopPlatform platform, IDesktopNative native)
{
    /// <summary>How much of a line goes into a notification; a toast or a bubble shows two or three lines at most.</summary>
    internal const int MaxBody = 400;

    private IntPtr _urgentWindow;

    /// <summary>The alert for the machine this runs on.</summary>
    public static DesktopAlert ForThisMachine() => new(
        OperatingSystem.IsWindows() ? DesktopPlatform.Windows
        : OperatingSystem.IsMacOS() ? DesktopPlatform.MacOS
        : DesktopPlatform.Linux,
        new DesktopNative());

    /// <summary>
    /// Shows <paramref name="request"/> for the window behind <paramref name="handle"/>, whose kind is
    /// <paramref name="descriptor"/> (Avalonia's <c>HWND</c>, <c>XID</c> or <c>NSWindow</c>), both null where there is none.
    /// </summary>
    public void Show(AttentionRequest request, IntPtr? handle, string? descriptor)
    {
        try
        {
            if (platform == DesktopPlatform.Windows && IsWindow(handle, descriptor, "HWND") is { } hwnd)
            {
                native.Flash(hwnd);
            }

            if (platform == DesktopPlatform.Linux && IsWindow(handle, descriptor, "XID") is { } xid && native.SetUrgency(xid, true))
            {
                _urgentWindow = xid;
            }

            foreach (var launch in Launches(platform, request, platform == DesktopPlatform.Windows && native.ToastRegistered))
            {
                native.Launch(launch);
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException
                                       or DllNotFoundException or EntryPointNotFoundException)
        {
            // No notifier on this machine: the title bar's count still says it.
        }
    }

    /// <summary>The window was brought forward: takes back an urgency hint this set. The flash stops by itself.</summary>
    public void Clear()
    {
        if (_urgentWindow == IntPtr.Zero)
        {
            return;
        }

        var xid = _urgentWindow;
        _urgentWindow = IntPtr.Zero;
        try
        {
            native.SetUrgency(xid, false);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Nothing was set.
        }
    }

    /// <summary>Whether an urgency hint this set is still up (tests).</summary>
    internal bool Urgent => _urgentWindow != IntPtr.Zero;

    /// <summary>The programs an alert starts on <paramref name="platform"/>.</summary>
    internal static IReadOnlyList<AlertLaunch> Launches(DesktopPlatform platform, AttentionRequest request, bool toastRegistered)
    {
        var title = Bound(request.Title, 120);
        var body = Bound(request.Body, MaxBody);
        return platform switch
        {
            DesktopPlatform.Windows => toastRegistered ? [WindowsToast.Launch(WindowsAppId.Value, title, body)] : [],
            DesktopPlatform.MacOS =>
            [
                new AlertLaunch("/usr/bin/osascript",
                    ["-e", "on run argv", "-e", "display notification (item 2 of argv) with title (item 1 of argv)", "-e", "end run",
                     title, body]),
            ],
            _ => [new AlertLaunch("notify-send", ["--app-name=SharpMUTerm", "--", title, body])],
        };
    }

    private static IntPtr? IsWindow(IntPtr? handle, string? descriptor, string kind) =>
        handle is { } value && value != IntPtr.Zero && string.Equals(descriptor, kind, StringComparison.Ordinal) ? value : null;

    /// <summary>Control characters out (a notification is one or two lines), and at most <paramref name="max"/> characters.</summary>
    internal static string Bound(string text, int max)
    {
        var clean = new StringBuilder(Math.Min(text.Length, max));
        foreach (var c in text)
        {
            if (clean.Length >= max)
            {
                break;
            }

            clean.Append(char.IsControl(c) ? ' ' : c);
        }

        if (clean.Length > 0 && char.IsHighSurrogate(clean[^1]))
        {
            clean.Length--;
        }

        return clean.ToString();
    }
}

/// <summary>
/// The AppUserModelID the window runs under on Windows. The process takes it at start
/// (<c>SetCurrentProcessExplicitAppUserModelID</c>), the installer's Start-menu shortcut carries it, and the installer
/// registers it under <c>Software\Classes\AppUserModelId</c> with a display name and an icon, which is what Windows needs
/// before a toast is shown under it. The program never registers it itself (the telnet-link rule).
/// </summary>
internal static class WindowsAppId
{
    /// <summary>The id. The installer (<c>packaging/windows/sharpmuterm.iss</c>) spells it too.</summary>
    public const string Value = "SharpMUTerm.Window";

    /// <summary>Takes <see cref="Value"/> for this process, so the taskbar groups the window with its shortcut. Windows only.</summary>
    public static void Adopt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _ = SetCurrentProcessExplicitAppUserModelID(Value);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Older than Windows 7: nothing to group by.
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}

/// <summary>
/// A Windows toast through WinRT's <c>ToastNotificationManager</c>, shown by Windows PowerShell 5.1 because .NET on the
/// plain <c>net10.0</c> target reaches no WinRT itself (that needs a <c>-windows</c> TFM, which the Linux build and the
/// shared assemblies cannot take). The script is fixed; the title, body and id are base64 inside it, decoded by the
/// script and added as text nodes, so neither PowerShell nor the toast's XML ever parses server text.
/// </summary>
internal static class WindowsToast
{
    /// <summary>The <c>powershell.exe</c> launch that shows one toast.</summary>
    public static AlertLaunch Launch(string appId, string title, string body) =>
        new("powershell.exe",
            ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden",
             "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(Script(appId, title, body)))]);

    /// <summary>The script <see cref="Launch"/> encodes.</summary>
    internal static string Script(string appId, string title, string body) =>
        string.Join('\n',
            "$ErrorActionPreference = 'Stop'",
            "[void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]",
            "[void][Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]",
            "function D($s) { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($s)) }",
            "$x = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02)",
            "$n = $x.GetElementsByTagName('text')",
            $"[void]$n.Item(0).AppendChild($x.CreateTextNode((D '{Base64(title)}')))",
            $"[void]$n.Item(1).AppendChild($x.CreateTextNode((D '{Base64(body)}')))",
            $"[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier((D '{Base64(appId)}')).Show([Windows.UI.Notifications.ToastNotification]::new($x))");

    private static string Base64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
}

/// <summary>The real <see cref="IDesktopNative"/>.</summary>
internal sealed class DesktopNative : IDesktopNative
{
    private bool? _toastRegistered;

    public bool ToastRegistered => _toastRegistered ??= ReadToastRegistered();

    public void Launch(AlertLaunch launch)
    {
        var start = new ProcessStartInfo(launch.Program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in launch.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
    }

    public void Flash(IntPtr hwnd)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsFlash.Flash(hwnd);
        }
    }

    public bool SetUrgency(IntPtr xid, bool urgent) =>
        OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD() ? X11Urgency.Set(xid, urgent) : false;

    private static bool ReadToastRegistered()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            const string key = @"Software\Classes\AppUserModelId\" + WindowsAppId.Value;
            using var user = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key);
            using var machine = user is null ? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key) : null;
            return user is not null || machine is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}

/// <summary><c>FlashWindowEx</c>.</summary>
internal static class WindowsFlash
{
    public static void Flash(IntPtr hwnd)
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

/// <summary>
/// The X11 urgency hint, set the two ways window managers read it: the ICCCM <c>WM_HINTS</c> UrgencyHint flag (which
/// the window manager may clear on focus) and the EWMH <c>_NET_WM_STATE_DEMANDS_ATTENTION</c> state, asked of the root
/// window with a client message as EWMH requires. Each call opens its own display connection (window ids are
/// server-wide, so Avalonia's own connection is not needed) and closes it after a flush. 64-bit only, which is every
/// platform the window ships for; anywhere else it says it did nothing.
/// </summary>
internal static class X11Urgency
{
    private const long UrgencyHint = 1L << 8;
    private const long SubstructureNotifyMask = 1L << 19;
    private const long SubstructureRedirectMask = 1L << 20;
    private const int ClientMessage = 33;
    private const long StateRemove = 0;
    private const long StateAdd = 1;
    private const long SourceApplication = 1;

    public static bool Set(IntPtr window, bool urgent)
    {
        if (!Environment.Is64BitProcess || window == IntPtr.Zero)
        {
            return false;
        }

        var display = XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var hints = XGetWMHints(display, window);
            var owned = hints == IntPtr.Zero;
            if (owned)
            {
                hints = XAllocWMHints();
            }

            if (hints != IntPtr.Zero)
            {
                var flags = Marshal.ReadInt64(hints);
                Marshal.WriteInt64(hints, urgent ? flags | UrgencyHint : flags & ~UrgencyHint);
                XSetWMHints(display, window, hints);
                XFree(hints);
            }

            var message = new ClientMessageEvent
            {
                Type = ClientMessage,
                SendEvent = 1,
                Display = display,
                Window = window,
                MessageType = XInternAtom(display, "_NET_WM_STATE", false),
                Format = 32,
                Data0 = (IntPtr)(urgent ? StateAdd : StateRemove),
                Data1 = XInternAtom(display, "_NET_WM_STATE_DEMANDS_ATTENTION", false),
                Data2 = IntPtr.Zero,
                Data3 = (IntPtr)SourceApplication,
            };
            XSendEvent(display, XDefaultRootWindow(display), false,
                (IntPtr)(SubstructureNotifyMask | SubstructureRedirectMask), ref message);
            XFlush(display);
            return true;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    /// <summary>XClientMessageEvent, padded to the size of the XEvent union (24 longs) that XSendEvent reads.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    private struct ClientMessageEvent
    {
        [FieldOffset(0)] public int Type;
        [FieldOffset(8)] public IntPtr Serial;
        [FieldOffset(16)] public int SendEvent;
        [FieldOffset(24)] public IntPtr Display;
        [FieldOffset(32)] public IntPtr Window;
        [FieldOffset(40)] public IntPtr MessageType;
        [FieldOffset(48)] public int Format;
        [FieldOffset(56)] public IntPtr Data0;
        [FieldOffset(64)] public IntPtr Data1;
        [FieldOffset(72)] public IntPtr Data2;
        [FieldOffset(80)] public IntPtr Data3;
        [FieldOffset(88)] public IntPtr Data4;
    }

    private const string LibX11 = "libX11.so.6";

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr name);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11, CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern IntPtr XInternAtom(IntPtr display, string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);

    [DllImport(LibX11)]
    private static extern IntPtr XGetWMHints(IntPtr display, IntPtr window);

    [DllImport(LibX11)]
    private static extern IntPtr XAllocWMHints();

    [DllImport(LibX11)]
    private static extern int XSetWMHints(IntPtr display, IntPtr window, IntPtr hints);

    [DllImport(LibX11)]
    private static extern int XFree(IntPtr data);

    [DllImport(LibX11)]
    private static extern int XSendEvent(IntPtr display, IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool propagate, IntPtr mask, ref ClientMessageEvent message);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);
}
