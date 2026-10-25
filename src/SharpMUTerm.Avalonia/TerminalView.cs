using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SharpMUTerm.Tui;
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
    private double _wheelRemainder;
    private StandardCursorType _cursor = StandardCursorType.Ibeam;
    private Point _pointerAt;

    // What the client says is under the pointer and where the panes are scrolled to, copied onto this thread.
    private PointerTarget _target = PointerTarget.Nothing;
    private IReadOnlyList<PaneScrollbar> _bars = [];
    private ScrollDrag? _scrollDrag;
    private string? _barHovered;

    private readonly Popup _tip;
    private readonly TextBlock _tipText;
    private readonly DispatcherTimer _tipTimer;

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

        _tipText = new TextBlock { Foreground = Brush(foreground), FontSize = 12, MaxWidth = 420, TextWrapping = TextWrapping.Wrap };
        _tip = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = false,
            IsHitTestVisible = false,
            Child = new Border
            {
                Background = Brush(Mix(background, foreground, 0.12f)),
                BorderBrush = Brush(Mix(background, foreground, 0.35f)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 4),
                Child = _tipText,
            },
        };
        LogicalChildren.Add(_tip);
        _tipTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Input, (_, _) => OpenTip());

        var pointer = _driver.Pointer;
        pointer.HoverChanged += _ => Dispatcher.UIThread.Post(() => OnHover(pointer.Target));
        pointer.ScrollbarsChanged += _ => Dispatcher.UIThread.Post(() =>
        {
            _bars = pointer.Bars;
            InvalidateVisual();
        });
        pointer.MenuRequested += (_, items) => Dispatcher.UIThread.Post(() => OpenMenu(items));
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
        DrawHover(context);
        DrawScrollbars(context);
    }

    /// <summary>
    /// A faint plate under whatever a click acts on — a link, a tab, a sidebar row — so it reads as a control
    /// before it is clicked. Drawn over the cells, never in them, so the frame is the terminal's frame.
    /// </summary>
    private void DrawHover(DrawingContext context)
    {
        if (_target.Kind is not (PointerKind.Link or PointerKind.Tab or PointerKind.RailRow) || _mouse.IsHeld || _target.Area.Width <= 0)
        {
            return;
        }

        var cell = CellSize;
        var area = _target.Area;
        var rect = new Rect(area.X * cell.Width, area.Y * cell.Height, area.Width * cell.Width, area.Height * cell.Height);
        context.DrawRectangle(Brush(_renderer.DefaultForeground, 0x24), null, rect.Inflate(new Thickness(1, 0)), 2, 2);
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
        var mapped = KeyMapper.FromKeyDown(e.Key, e.KeyModifiers);

        // The window's own chords (WindowHost): paste, and ⌥⏎ to maximise and restore. Read from the list the
        // client's surfaces are told the window keeps, so the two cannot disagree.
        if (mapped is { } paste && WindowHost.Paste.Any(r => r.Chord.Matches(paste)))
        {
            _ = PasteAsync();
            e.Handled = true;
            return;
        }

        if (mapped is { } chord && WindowHost.Maximise.Chord.Matches(chord))
        {
            ToggleMaximised();
            e.Handled = true;
            return;
        }

        if (mapped is { } key)
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

        CloseTip();
        if (button == PointerButton.Left && (TryWindowGesture(e, point.Position) || TryGrabScrollbar(e, point.Position)))
        {
            e.Handled = true;
            return;
        }

        var cell = Cell(point.Position);
        var modifiers = Modifiers(e.KeyModifiers);
        if (button == PointerButton.Right)
        {
            // The client answers a right press with a menu, which takes the pointer, so this view never sees the
            // release: the click is sent whole, and nothing is left held to repeat or to turn into a drag.
            _driver.RaiseMouse(_mouse.Pressed(button, cell, 1, modifiers), cell);
            if (_mouse.Released(button, cell, modifiers) is { } released)
            {
                _driver.RaiseMouse(released, cell);
            }

            e.Handled = true;
            return;
        }

        e.Pointer.Capture(this);
        _driver.RaiseMouse(_mouse.Pressed(button, cell, e.ClickCount, modifiers), cell);
        _repeat.Start();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        _pointerAt = position;
        if (_scrollDrag is { } drag)
        {
            DragScrollbar(drag, position.Y);
            e.Handled = true;
            return;
        }

        HoverScrollbar(position);
        UpdateCursor(position);
        var cell = Cell(position);
        if (_mouse.Moved(cell, Modifiers(e.KeyModifiers)) is { } flags)
        {
            _driver.RaiseMouse(flags, cell);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_scrollDrag is not null)
        {
            _scrollDrag = null;
            e.Pointer.Capture(null);
            HoverScrollbar(e.GetPosition(this));
            UpdateCursor(e.GetPosition(this));
            e.Handled = true;
            return;
        }

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
        CloseTip();
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

    /// <summary>
    /// The cursor for <paramref name="position"/>: a window edge's resize cursor first, then the arrow over a
    /// scrollbar, then whatever the client says the cell under it is.
    /// </summary>
    private void UpdateCursor(Point position)
    {
        var edge = Undecorated is { WindowState: WindowState.Normal } && !_mouse.IsHeld ? WindowChrome.EdgeAt(position, Bounds.Size) : null;
        var cursor = edge is { } e ? WindowChrome.CursorFor(e)
            : _scrollDrag is not null || _barHovered is not null ? StandardCursorType.Arrow
            : CursorFor(_target.Shape);
        if (cursor != _cursor)
        {
            _cursor = cursor;
            Cursor = new Cursor(cursor);
        }
    }

    internal static StandardCursorType CursorFor(PointerShape shape) => shape switch
    {
        PointerShape.Text => StandardCursorType.Ibeam,
        PointerShape.Hand => StandardCursorType.Hand,
        PointerShape.ResizeColumns => StandardCursorType.SizeWestEast,
        PointerShape.ResizeRows => StandardCursorType.SizeNorthSouth,
        _ => StandardCursorType.Arrow,
    };

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        CloseTip();
        if (_barHovered is not null && _scrollDrag is null)
        {
            _barHovered = null;
            InvalidateVisual();
        }
    }

    // ── What is under the pointer ───────────────────────────────────────────────────────────────

    private void OnHover(PointerTarget target)
    {
        if (target.Key == _target.Key && target.Tooltip == _target.Tooltip)
        {
            return;
        }

        _target = target;
        InvalidateVisual();
        UpdateCursor(_pointerAt);
        CloseTip();
        if (target.Tooltip is not null && !_mouse.IsHeld)
        {
            _tipTimer.Start();
        }
    }

    /// <summary>The cursor the view is showing. Internal for tests.</summary>
    internal StandardCursorType CursorType => _cursor;

    /// <summary>The tooltip, open or not. Internal for tests.</summary>
    internal (bool Open, string? Text) Tip => (_tip.IsOpen, _tipText.Text);

    /// <summary>The last right-click menu opened. Internal for tests.</summary>
    internal ContextMenu? OpenedMenu { get; private set; }

    /// <summary>Shows the hovered target's tooltip under the pointer, once it has rested there. Internal for tests.</summary>
    internal void OpenTip()
    {
        _tipTimer.Stop();
        if (_target.Tooltip is not { } text || _mouse.IsHeld || _scrollDrag is not null || !IsPointerOver)
        {
            return;
        }

        _tipText.Text = text;
        var cell = CellSize;
        _tip.PlacementRect = new Rect(_pointerAt.X - cell.Width / 2, _pointerAt.Y, cell.Width, cell.Height);
        _tip.IsOpen = true;
    }

    private void CloseTip()
    {
        _tipTimer.Stop();
        _tip.IsOpen = false;
    }

    /// <summary>The client's right-click menu, as a native one. Each entry runs on the client's thread by itself.</summary>
    private void OpenMenu(IReadOnlyList<PointerMenuItem> items)
    {
        CloseTip();
        var menu = new ContextMenu { Placement = PlacementMode.Pointer, PlacementTarget = this };
        Grid.SetIsSharedSizeScope(menu, true);
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var entry = new MenuItem { Header = MenuHeader(item), IsEnabled = item.Enabled };
            entry.Click += (_, _) => item.Run();
            menu.Items.Add(entry);
        }

        menu.Closed += (_, _) => Focus();
        OpenedMenu = menu;
        menu.Open(this);
    }

    /// <summary>A menu entry's label with its key after it, the keys lined up in a column of their own.</summary>
    private static Control MenuHeader(PointerMenuItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "label" });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "hint" });
        grid.Children.Add(new TextBlock { Text = item.Label, VerticalAlignment = VerticalAlignment.Center });
        if (item.Hint is { } hint)
        {
            var key = new TextBlock { Text = hint, Opacity = 0.6, Margin = new Thickness(28, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(key, 1);
            grid.Children.Add(key);
        }

        return grid;
    }

    // ── Scrollbars ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The scrollbars are drawn over the pane's right edge rather than in a column of it: a column would make
    /// the pane narrower in this host than in a terminal, and every connected world is told the pane's width.
    /// </summary>
    private const double BarWidth = 4, BarWidthHovered = 8, BarZone = 12, MinimumThumb = 18;

    private sealed record ScrollDrag(string WindowId, double Grab);

    /// <summary>A bar's track and thumb in logical pixels, or null when the pane has nothing to scroll.</summary>
    private (Rect Zone, Rect Thumb, double Travel)? BarGeometry(PaneScrollbar bar)
    {
        if (bar.ContentRows <= bar.ViewportRows || bar.ViewportRows <= 0)
        {
            return null;
        }

        var cell = CellSize;
        var right = (bar.Track.X + bar.Track.Width) * cell.Width;
        var top = bar.Track.Y * cell.Height;
        var height = bar.Track.Height * cell.Height;
        var thumb = Math.Min(height, Math.Max(MinimumThumb, height * bar.ViewportRows / bar.ContentRows));
        var travel = height - thumb;
        var hidden = bar.ContentRows - bar.ViewportRows;
        var y = top + travel * Math.Clamp(bar.Offset, 0, hidden) / hidden;
        var width = bar.WindowId == _barHovered ? BarWidthHovered : BarWidth;
        return (new Rect(right - BarZone, top, BarZone, height), new Rect(right - width - 2, y, width, thumb), travel);
    }

    private PaneScrollbar? BarAt(Point position) =>
        _bars.FirstOrDefault(bar => BarGeometry(bar) is { } g && g.Zone.Contains(position));

    private void HoverScrollbar(Point position)
    {
        var hovered = _mouse.IsHeld ? null : BarAt(position)?.WindowId;
        if (hovered != _barHovered)
        {
            _barHovered = hovered;
            InvalidateVisual();
        }
    }

    /// <summary>A press on a bar: the thumb is picked up where it was pressed; the track jumps the thumb to the pointer.</summary>
    private bool TryGrabScrollbar(PointerPressedEventArgs e, Point position)
    {
        if (BarAt(position) is not { } bar || BarGeometry(bar) is not { } g)
        {
            return false;
        }

        var grab = g.Thumb.Top <= position.Y && position.Y < g.Thumb.Bottom ? position.Y - g.Thumb.Top : g.Thumb.Height / 2;
        _scrollDrag = new ScrollDrag(bar.WindowId, grab);
        _barHovered = bar.WindowId;
        e.Pointer.Capture(this);
        DragScrollbar(_scrollDrag, position.Y);
        return true;
    }

    private void DragScrollbar(ScrollDrag drag, double y)
    {
        if (_bars.FirstOrDefault(b => b.WindowId == drag.WindowId) is not { } bar || BarGeometry(bar) is not { } g || g.Travel <= 0)
        {
            return;
        }

        var hidden = bar.ContentRows - bar.ViewportRows;
        var fraction = Math.Clamp((y - drag.Grab - g.Zone.Top) / g.Travel, 0, 1);
        bar.ScrollTo((int)Math.Round(fraction * hidden));
    }

    private void DrawScrollbars(DrawingContext context)
    {
        var colour = _renderer.DefaultForeground;
        foreach (var bar in _bars)
        {
            if (BarGeometry(bar) is not { } g)
            {
                continue;
            }

            var active = bar.WindowId == _barHovered;
            if (active)
            {
                var track = new Rect(g.Thumb.X, g.Zone.Y, g.Thumb.Width, g.Zone.Height);
                context.DrawRectangle(Brush(colour, 0x22), null, track, g.Thumb.Width / 2, g.Thumb.Width / 2);
            }

            context.DrawRectangle(Brush(colour, active ? (byte)0xB0 : (byte)0x60), null, g.Thumb, g.Thumb.Width / 2, g.Thumb.Width / 2);
        }
    }

    private static SolidColorBrush Brush(SKColor colour, byte alpha = 0xFF) =>
        new(Color.FromArgb(alpha, colour.Red, colour.Green, colour.Blue));

    private static SKColor Mix(SKColor from, SKColor to, float amount) => new(
        (byte)(from.Red + (to.Red - from.Red) * amount),
        (byte)(from.Green + (to.Green - from.Green) * amount),
        (byte)(from.Blue + (to.Blue - from.Blue) * amount));

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
