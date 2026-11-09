using System.Runtime.InteropServices;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Tui;

/// <summary>
/// Reads the Unix terminal's published pixel extent without touching its input stream. Some emulators
/// publish no pixels; Windows pseudoconsoles do not expose reliable renderer metrics. Those stay unknown.
/// </summary>
internal sealed class TerminalCellMetrics : CellMetrics
{
    private readonly Func<TerminalCellSize?> _read;
    private Timer? _timer;
    private readonly object _gate = new();
    private bool _disposed;

    public TerminalCellMetrics() : this(ReadTerminal) { }

    internal TerminalCellMetrics(Func<TerminalCellSize?> read) => _read = read;

    public override void Start()
    {
        lock (_gate)
        {
            if (_disposed || _timer is not null) { return; }
            Sample();
            // Pixel-only zoom need not resize the character grid.
            _timer = new Timer(_ => Sample(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }
    }

    internal void Sample()
    {
        lock (_gate)
        {
            if (!_disposed) { Current = _read(); }
        }
    }

    public override void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    internal static TerminalCellSize? FromWindowSize(int columns, int rows, int width, int height)
    {
        if (columns <= 0 || rows <= 0 || width <= 0 || height <= 0 || width % columns != 0 || height % rows != 0)
        {
            return null;
        }

        var cell = new TerminalCellSize(width / columns, height / rows);
        return cell.IsValid ? cell : null;
    }

    private static TerminalCellSize? ReadTerminal()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) { return null; }
        // Query stdout only: a redirected stream must not silently borrow a different terminal's dimensions.
        var request = OperatingSystem.IsMacOS() ? 0x40087468UL : 0x5413UL;
        WindowSize size;
        // macOS ARM64 passes variadic ioctl arguments on the stack, not in x2.
        var result = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? IoctlArm64(1, (nuint)request, 0, 0, 0, 0, 0, 0, out size)
            : Ioctl(1, (nuint)request, out size);
        return result == 0
            ? FromWindowSize(size.Columns, size.Rows, size.Width, size.Height)
            : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowSize
    {
        public ushort Rows;
        public ushort Columns;
        public ushort Width;
        public ushort Height;
    }

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int IoctlArm64(int descriptor, nuint request,
        ulong x2, ulong x3, ulong x4, ulong x5, ulong x6, ulong x7, out WindowSize size);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int Ioctl(int descriptor, nuint request, out WindowSize size);
}
