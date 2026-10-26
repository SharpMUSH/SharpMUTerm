using Color = SharpConsoleUI.Color;
using Point = System.Drawing.Point;
using SharpConsoleUI;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Layout;
using SharpMUTerm.Tui;
using Size = SharpConsoleUI.Helpers.Size;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// A SharpConsoleUI console driver with no console behind it: the framework writes cells into a
/// <see cref="CellGrid"/>, <see cref="Flush"/> publishes them as a <see cref="ScreenFrame"/>, and a window paints
/// that frame. Input goes the other way — the window turns its own key and pointer events into the
/// <see cref="ConsoleKeyInfo"/>s and mouse flags a terminal driver would have parsed out of escapes, and raises
/// them here.
/// <para>
/// Nothing in it touches Avalonia, so it can be driven by a test, or by <c>--render-png</c>, with no window at
/// all. Both interfaces are public in the framework and nothing in the framework casts to a concrete driver,
/// which is what makes a third-party driver possible.
/// </para>
/// <para>
/// <b>It speaks the Kitty graphics protocol, and that is the point of it.</b> It reports
/// <see cref="SupportsKittyGraphics"/>, so the client sends a pane picture as pixels plus placeholder cells,
/// exactly as it would to kitty. A terminal on Windows cannot be reached that way (ConPTY drops the escapes);
/// this driver keeps the pixels in a <see cref="PictureStore"/> and the renderer draws them over the cells.
/// </para>
/// </summary>
internal sealed class AvaloniaConsoleDriver : IConsoleDriver, IGraphicsProtocol, IMouseInjector, IDisposable
{
    private readonly CellGrid _grid;
    private CaretState _caret;
    private ScreenFrame _frame;
    private long _frames;

    public AvaloniaConsoleDriver(int columns, int rows)
    {
        _grid = new CellGrid(columns, rows);
        _frame = ScreenFrame.Empty(_grid.Width, _grid.Height);
    }

    public event EventHandler<ConsoleKeyInfo>? KeyPressed;

    public event EventHandler<string>? Paste;

    public event IConsoleDriver.MouseEventHandler? MouseEvent;

    public event EventHandler<Size>? ScreenResized;

    /// <summary>Raised on the framework's render thread whenever a new frame is published.</summary>
    public event Action? FrameReady;

    /// <summary>Raised when the framework stops the driver — the loop is ending.</summary>
    public event Action? Stopped;

    public Size ScreenSize => new(_grid.Width, _grid.Height);

    /// <summary>The last published frame. Safe to read from any thread.</summary>
    public ScreenFrame Frame => Volatile.Read(ref _frame);

    /// <summary>How many frames have been published.</summary>
    public long FrameCount => Interlocked.Read(ref _frames);

    public PictureStore Pictures { get; } = new();

    /// <summary>What the client says is under the pointer, for the window to show (<see cref="WindowPointer"/>).</summary>
    public WindowPointer Pointer { get; } = new();

    /// <summary>The window's screen-reader regions; the window adds them beside the view.</summary>
    public WindowAnnouncer Announcer { get; } = new();

    public bool IsRunning { get; private set; }

    // ── IConsoleDriver: output ────────────────────────────────────────────────────────────────

    public void Initialize(ConsoleWindowSystem windowSystem)
    {
    }

    public void Start() => IsRunning = true;

    public void Stop()
    {
        IsRunning = false;
        Stopped?.Invoke();
    }

    public void Clear() => _grid.Clear();

    public void Flush()
    {
        Volatile.Write(ref _frame, _grid.Snapshot(_caret));
        Interlocked.Increment(ref _frames);
        FrameReady?.Invoke();
    }

    public void SetNarrowCell(int x, int y, char character, Color fg, Color bg) =>
        _grid.Set(x, y, new GridCell(new System.Text.Rune(character), null, fg, bg, TextDecoration.None, false));

    public void FillCells(int x, int y, int width, char character, Color fg, Color bg) =>
        _grid.Fill(x, y, width, new GridCell(new System.Text.Rune(character), null, fg, bg, TextDecoration.None, false));

    public void WriteBufferRegion(int destX, int destY, CharacterBuffer source, int srcX, int srcY, int width, Color fallbackBg) =>
        _grid.CopyFrom(destX, destY, source, srcX, srcY, width, fallbackBg);

    /// <summary>Every flush publishes the whole grid, so there is no dirty count to report.</summary>
    public int GetDirtyCharacterCount() => 0;

    public void SetCursorPosition(int x, int y) => _caret = _caret with { X = x, Y = y };

    public void SetCursorVisible(bool visible) => _caret = _caret with { Visible = visible };

    public void SetCursorShape(CursorShape shape) => _caret = _caret with { Shape = shape };

    public void ResetCursorShape() => _caret = _caret with { Shape = default };

    // ── IGraphicsProtocol ─────────────────────────────────────────────────────────────────────

    public bool SupportsKittyGraphics => true;

    public void TransmitImage(uint imageId, byte[] pngData, int columns, int rows) =>
        Pictures.AddEncoded(imageId, pngData, columns, rows);

    public void TransmitRawRgb(uint imageId, byte[] rgbData, int pixelWidth, int pixelHeight, int columns, int rows) =>
        Pictures.AddRgb(imageId, rgbData, pixelWidth, pixelHeight, columns, rows);

    public void UpdateRawRgbFrame(uint imageId, byte[] rgbData, int pixelWidth, int pixelHeight) =>
        Pictures.UpdateRgb(imageId, rgbData, pixelWidth, pixelHeight);

    public void DeleteImage(uint imageId) => Pictures.Remove(imageId);

    // ── Input, raised by the window ───────────────────────────────────────────────────────────

    public void RaiseKey(ConsoleKeyInfo key) => KeyPressed?.Invoke(this, key);

    public void RaiseMouse(List<MouseFlags> flags, Point cell) => MouseEvent?.Invoke(this, flags, cell);

    void IMouseInjector.InjectMouse(List<MouseFlags> flags, Point cell) => RaiseMouse(flags, cell);

    public void RaisePaste(string text) => Paste?.Invoke(this, text);

    /// <summary>The window changed size in cells. A no-op when the size is unchanged.</summary>
    public void Resize(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (columns == _grid.Width && rows == _grid.Height)
        {
            return;
        }

        _grid.Resize(columns, rows);
        ScreenResized?.Invoke(this, ScreenSize);
    }

    public void Dispose() => Pictures.Dispose();
}
