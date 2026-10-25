using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's one control: it paints the driver's latest <see cref="ScreenFrame"/> with Skia, sizes the grid to
/// itself, and turns key and pointer input into what the driver raises to the framework. The terminal emulator,
/// in other words, minus everything about escape sequences.
/// <para>
/// Cells are measured in <em>device</em> pixels and the font is sized for them, then the canvas is scaled back to
/// logical units for the draw. Measuring in logical units would make the cell a whole number of logical pixels and
/// a fraction of device ones on any scaled display, which is exactly the seam <see cref="CellFonts"/> exists to
/// avoid.
/// </para>
/// </summary>
internal sealed class TerminalView : Control
{
    private readonly AvaloniaConsoleDriver _driver;
    private readonly MouseMapper _mouse = new();
    private readonly DispatcherTimer _repeat;
    private readonly float _fontPoints;
    private readonly string? _family;
    private CellRenderer _renderer;
    private double _scale = 1;
    private int _invalidatePending;
    private char? _swallowText;
    private WindowEdge? _cursorEdge;
    private double _wheelRemainder;

    public TerminalView(AvaloniaConsoleDriver driver, SKColor foreground, SKColor background, float fontPoints, string? family)
    {
        _driver = driver;
        _fontPoints = fontPoints;
        _family = family;
        _renderer = new CellRenderer(new CellFonts(fontPoints, family), foreground, background);
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);

        _driver.FrameReady += OnFrameReady;
        _repeat = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Input, (_, _) => RepeatPress());
    }

    /// <summary>The cell size in logical units, for mapping a pointer position to a cell.</summary>
    private Size CellSize => new(_renderer.Fonts.CellWidth / _scale, _renderer.Fonts.CellHeight / _scale);

    /// <summary>The grid a window of <paramref name="size"/> holds, in whole cells.</summary>
    public (int Columns, int Rows) GridFor(Size size) =>
        (Math.Max(20, (int)(size.Width / CellSize.Width)), Math.Max(8, (int)(size.Height / CellSize.Height)));

    /// <summary>The window size that holds a grid of <paramref name="columns"/> × <paramref name="rows"/>.</summary>
    public Size SizeFor(int columns, int rows) => new(columns * CellSize.Width, rows * CellSize.Height);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevel.GetTopLevel(this) is { } top && Math.Abs(top.RenderScaling - _scale) > 0.001)
        {
            Rescale(top.RenderScaling);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var (columns, rows) = GridFor(e.NewSize);
        _driver.Resize(columns, rows);
    }

    public override void Render(DrawingContext context)
    {
        context.Custom(new FrameDraw(new Rect(Bounds.Size), _driver.Frame, _driver.Pictures, _renderer, _scale));
    }

    private void Rescale(double scale)
    {
        // The old fonts are not disposed: a frame already queued on the render thread may still be drawing
        // with them. A change of display scale is rare enough that one set of leaked fonts is the cheaper fix.
        _scale = scale;
        _renderer = new CellRenderer(
            new CellFonts((float)(_fontPoints * scale), _family),
            _renderer.DefaultForeground,
            _renderer.DefaultBackground);
    }

    private void OnFrameReady()
    {
        // The framework flushes on its own thread, sometimes many times a frame; one repaint covers them all.
        if (Interlocked.Exchange(ref _invalidatePending, 1) == 0)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _invalidatePending, 0);
                InvalidateVisual();
            }, DispatcherPriority.Render);
        }
    }

    // ── Keys ────────────────────────────────────────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _swallowText = null;
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        // Paste is the window's, as it is the terminal's: Ctrl+Shift+V and Shift+Insert, the terminal
        // conventions, so a bare Ctrl+V still reaches the client as the key it has always been.
        if ((ctrl && shift && e.Key == Key.V) || (shift && !ctrl && e.Key == Key.Insert))
        {
            _ = PasteAsync();
            e.Handled = true;
            return;
        }

        // Alt+Enter maximises and restores, as it does in Windows Terminal. The client's own ⌥⏎ (a newline in the
        // command line) is the cost; a window reports ⌃⏎ and ⇧⏎, and either is the newline here.
        if (e.KeyModifiers == KeyModifiers.Alt && e.Key == Key.Enter)
        {
            ToggleMaximised();
            e.Handled = true;
            return;
        }

        if (KeyMapper.FromKeyDown(e.Key, e.KeyModifiers) is { } key)
        {
            // An Alt chord can be followed by a TextInput for the same letter on some platforms.
            _swallowText = key.Modifiers.HasFlag(ConsoleModifiers.Alt) && !key.Modifiers.HasFlag(ConsoleModifiers.Control)
                ? key.KeyChar
                : null;
            _driver.RaiseKey(key);
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        foreach (var key in KeyMapper.FromText(e.Text))
        {
            if (_swallowText is { } swallowed && char.ToLowerInvariant(swallowed) == char.ToLowerInvariant(key.KeyChar))
            {
                _swallowText = null;
                continue;
            }

            _driver.RaiseKey(key);
        }

        e.Handled = true;
    }

    private async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard
            && await clipboard.TryGetTextAsync() is { Length: > 0 } text)
        {
            _driver.RaisePaste(text);
        }
    }

    // ── Pointer ─────────────────────────────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        var button = point.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => PointerButton.Left,
            PointerUpdateKind.MiddleButtonPressed => PointerButton.Middle,
            PointerUpdateKind.RightButtonPressed => PointerButton.Right,
            _ => PointerButton.None,
        };
        if (button == PointerButton.None)
        {
            return;
        }

        if (button == PointerButton.Left && TryWindowGesture(e, point.Position))
        {
            e.Handled = true;
            return;
        }

        e.Pointer.Capture(this);
        _driver.RaiseMouse(_mouse.Pressed(button, Cell(point.Position), e.ClickCount, Modifiers(e.KeyModifiers)), Cell(point.Position));
        _repeat.Start();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        UpdateEdgeCursor(e.GetPosition(this));
        var cell = Cell(e.GetPosition(this));
        if (_mouse.Moved(cell, Modifiers(e.KeyModifiers)) is { } flags)
        {
            _driver.RaiseMouse(flags, cell);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var button = e.InitialPressMouseButton switch
        {
            MouseButton.Left => PointerButton.Left,
            MouseButton.Middle => PointerButton.Middle,
            MouseButton.Right => PointerButton.Right,
            _ => PointerButton.None,
        };
        var cell = Cell(e.GetPosition(this));
        if (_mouse.Released(button, cell, Modifiers(e.KeyModifiers)) is { } flags)
        {
            _driver.RaiseMouse(flags, cell);
        }

        _repeat.Stop();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var cell = Cell(e.GetPosition(this));
        var notches = MouseMapper.WheelNotches(ref _wheelRemainder, e.Delta.Y);
        for (var i = 0; i < Math.Abs(notches); i++)
        {
            _driver.RaiseMouse(MouseMapper.Wheel(notches > 0, Modifiers(e.KeyModifiers)), cell);
        }

        e.Handled = true;
    }

    /// <summary>The window this view drives when it has no title bar, or null when it has one.</summary>
    private Window? Undecorated =>
        TopLevel.GetTopLevel(this) is Window { WindowDecorations: not WindowDecorations.Full } window ? window : null;

    /// <summary>A press that moves or resizes an undecorated window instead of reaching the client (<see cref="WindowChrome"/>).</summary>
    private bool TryWindowGesture(PointerPressedEventArgs e, Point position)
    {
        if (Undecorated is not { } window)
        {
            return false;
        }

        if (window.WindowState == WindowState.Normal && WindowChrome.EdgeAt(position, Bounds.Size) is { } edge)
        {
            window.BeginResizeDrag(edge, e);
            return true;
        }

        if (!WindowChrome.IsMoveHandle(_driver.Frame, Cell(position)))
        {
            return false;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximised();
        }
        else
        {
            window.BeginMoveDrag(e);
        }

        return true;
    }

    private void ToggleMaximised()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }

    private void UpdateEdgeCursor(Point position)
    {
        var edge = Undecorated is { WindowState: WindowState.Normal } && !_mouse.IsHeld ? WindowChrome.EdgeAt(position, Bounds.Size) : null;
        if (edge != _cursorEdge)
        {
            _cursorEdge = edge;
            Cursor = new Cursor(edge is { } e ? WindowChrome.CursorFor(e) : StandardCursorType.Ibeam);
        }
    }

    private void RepeatPress()
    {
        if (_mouse.Repeat() is { } repeat)
        {
            _driver.RaiseMouse(repeat.Flags, repeat.Cell);
        }
        else
        {
            _repeat.Stop();
        }
    }

    private System.Drawing.Point Cell(Point position)
    {
        var size = CellSize;
        var frame = _driver.Frame;
        var x = Math.Clamp((int)(position.X / size.Width), 0, Math.Max(0, frame.Width - 1));
        var y = Math.Clamp((int)(position.Y / size.Height), 0, Math.Max(0, frame.Height - 1));
        return new System.Drawing.Point(x, y);
    }

    private static PointerModifiers Modifiers(KeyModifiers keys) =>
        ((keys & KeyModifiers.Shift) != 0 ? PointerModifiers.Shift : 0)
        | ((keys & KeyModifiers.Alt) != 0 ? PointerModifiers.Alt : 0)
        | ((keys & KeyModifiers.Control) != 0 ? PointerModifiers.Ctrl : 0);

    /// <summary>One frame, drawn straight onto the compositor's Skia canvas.</summary>
    private sealed class FrameDraw(Rect bounds, ScreenFrame frame, PictureStore pictures, CellRenderer renderer, double scale)
        : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) =>
            other is FrameDraw draw && ReferenceEquals(draw.Frame, Frame) && draw.Bounds == Bounds;

        private ScreenFrame Frame => frame;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
            {
                return;
            }

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)Bounds.Width, (float)Bounds.Height));
            canvas.Scale((float)(1 / scale));
            renderer.Render(canvas, frame, pictures);
            canvas.Restore();
        }

        public void Dispose()
        {
        }
    }
}
