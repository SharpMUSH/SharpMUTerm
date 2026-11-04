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
    private readonly float _lineHeight;
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

    public TerminalView(AvaloniaConsoleDriver driver, SKColor foreground, SKColor background, float fontPoints, string? family, float lineHeight = 1f)
    {
        _driver = driver;
        _fontPoints = fontPoints;
        _family = family;
        _lineHeight = lineHeight;
        _renderer = new CellRenderer(new CellFonts(fontPoints, family, lineHeight), foreground, background);
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);

        _driver.FrameReady += OnFrameReady;
        _repeat = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Input, (_, _) => RepeatPress());

        _tipText = new TextBlock { Foreground = Brush(foreground), FontSize = NativeTokens.Body, MaxWidth = 420, TextWrapping = TextWrapping.Wrap };
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
                CornerRadius = new CornerRadius(NativeTokens.RadiusS),
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
        pointer.ModelMenuRequested += (_, menu, dispatch) => Dispatcher.UIThread.Post(() => OpenMenu(NativeMenu.Build(menu, dispatch, PlacementMode.Pointer, this)));
    }

    /// <summary>
    /// A screen reader finds the window in front's newest lines here, as a read-only document
    /// (<see cref="TerminalViewAutomationPeer"/>), rather than an unnamed picture of cells.
    /// </summary>
    protected override global::Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() =>
        new TerminalViewAutomationPeer(this, _driver.Announcer);

    /// <summary>The cell size in logical units, for placing a native view over cells (<see cref="DesktopLayer"/>).</summary>
    internal Size CellExtent => CellSize;

    /// <summary>What draws the cells, for a native view that matches their colours.</summary>
    internal CellRenderer Renderer => _renderer;

    /// <summary>The driver this view paints.</summary>
    internal AvaloniaConsoleDriver Driver => _driver;

    /// <summary>
    /// Whether the client's header is the cells' first row, so a blank cell there moves the window as a title bar
    /// would. False when the window draws the header itself (<see cref="NativeHeader"/>): the first row is then a
    /// pane's tab strip, whose blank run is the client's and never a handle.
    /// </summary>
    internal bool CellsHoldHeader { get; set; } = true;

    /// <summary>
    /// Whether this view draws the command line's field around the bars' rows (<see cref="HostChrome.Input"/>): the
    /// grid is then <see cref="NativeLayout.FieldReserve"/> shorter, and a frame carrying a band
    /// (<see cref="ScreenFrame.Band"/>) draws its rows that much lower inside a padded, rounded field.
    /// </summary>
    internal bool DrawsInputField { get; set; }

    /// <summary>The plane the field's strip is painted on: the backdrop the status row and the sidebar sit on.</summary>
    internal SKColor? FooterPlane { get; set; }

    /// <summary>
    /// The hairline a pane divider is drawn as (<see cref="ScreenFrame.Dividers"/>): the native tokens' rule, set with the
    /// pane chrome. Null until then, when it is a faint step off the cells' background.
    /// </summary>
    internal SKColor? DividerRule { get; set; }

    /// <summary>The vertical pixels the field takes beside the rows it frames: none unless the view draws it.</summary>
    private double FieldReserve => DrawsInputField ? NativeLayout.FieldReserve : 0;

    /// <summary>The top of screen row <paramref name="row"/> in logical units: rows of a band sit lower by the field's top padding.</summary>
    internal double RowTop(int row)
    {
        var top = row * CellSize.Height;
        return DrawsInputField && _driver.Frame.Band is { } band && row >= band.Top ? top + NativeLayout.FieldTop : top;
    }

    /// <summary>
    /// Raised on the window's thread after a new frame is taken for painting, and when the cell size or the view's
    /// size changes: when anything placed over cells may need to move.
    /// </summary>
    internal event Action? Repainted;

    /// <summary>The cell size in logical units, for mapping a pointer position to a cell.</summary>
    private Size CellSize => new(_renderer.Fonts.CellWidth / _scale, _renderer.Fonts.CellHeight / _scale);

    /// <summary>The grid a window of <paramref name="size"/> holds, in whole cells.</summary>
    public (int Columns, int Rows) GridFor(Size size) =>
        (Math.Max(20, (int)(size.Width / CellSize.Width)), Math.Max(8, (int)((size.Height - FieldReserve) / CellSize.Height)));

    /// <summary>The window size that holds a grid of <paramref name="columns"/> × <paramref name="rows"/>.</summary>
    public Size SizeFor(int columns, int rows) => new(columns * CellSize.Width, (rows * CellSize.Height) + FieldReserve);

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

        // The width a terminal this window's size would have, sidebar and all, said before the grid moves: the
        // client's narrow layout reads it with the resize, and it does not change when the sidebar folds.
        _driver.Pointer.WindowColumnsMeasured = Math.Max(columns, (int)((e.NewSize.Width + Margin.Left + Margin.Right) / CellSize.Width));
        _driver.Resize(columns, rows);
        Repainted?.Invoke();
    }

    public override void Render(DrawingContext context)
    {
        var frame = _driver.Frame;
        var band = DrawsInputField ? frame.Band : null;
        IReadOnlyList<FieldBar> bars = band is null ? [] : BarsOf(frame, band);
        _fieldBars = bars;
        var rule = DividerRule ?? Mix(_renderer.DefaultBackground, _renderer.DefaultForeground, 0.12f);
        context.Custom(new FrameDraw(
            new Rect(Bounds.Size), frame, _driver.Pictures, _renderer, _scale, band, FooterPlane ?? Darker(_renderer.DefaultBackground), bars, rule));
        if (band is not null)
        {
            DrawFieldText(context, frame, band, bars);
        }

        DrawHover(context);
        DrawScrollbars(context);
    }

    /// <summary>
    /// One command line inside the field: its screen rows, the column its typed text starts at (past the prompt's cells),
    /// and how far that text is drawn to the left (<paramref name="Shift"/>, logical units) so it starts a mono space after
    /// the prompt drawn in the UI face, which is narrower than the cells it covers. The cells do not move: only the drawing,
    /// the caret drawn with it, and the pointer's mapping back to a cell (<see cref="Cell"/>) are shifted.
    /// </summary>
    internal readonly record struct FieldBar(int Top, int End, int TextStart, double PromptWidth, double Shift);

    /// <summary>The bars as last drawn, for mapping the pointer: the same shifts the paint used.</summary>
    private IReadOnlyList<FieldBar> _fieldBars = [];

    private (string Text, double Width) _promptMeasure = ("", 0);

    /// <summary>The prompt's width in the UI face it is drawn in, in logical units; the last one is remembered.</summary>
    private double PromptWidth(string text)
    {
        if (_promptMeasure.Text == text)
        {
            return _promptMeasure.Width;
        }

        var width = PromptText(text, SKColors.White, double.PositiveInfinity).WidthIncludingTrailingWhitespace;
        _promptMeasure = (text, width);
        return width;
    }

    private static FormattedText PromptText(string text, SKColor ink, double maxWidth) =>
        new(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(NativeTokens.UiFont, FontStyle.Normal, FontWeight.SemiBold), NativeTokens.Input, Brush(ink))
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

    /// <summary>
    /// Each bar in <paramref name="band"/>: from its prompt's row to the next prompt's (or the band's end), its text a
    /// mono space after the prompt as the UI face draws it. Internal for tests.
    /// </summary>
    internal IReadOnlyList<FieldBar> BarsOf(ScreenFrame frame, InputBand band)
    {
        var cell = CellSize;
        var end = Math.Min(frame.Height, band.Top + band.Rows);
        var prompts = band.Prompts.Where(p => p.Row >= band.Top && p.Row < end).OrderBy(p => p.Row).ToList();
        var bars = new List<FieldBar>(prompts.Count);
        for (var i = 0; i < prompts.Count; i++)
        {
            var prompt = prompts[i];
            var to = i + 1 < prompts.Count ? prompts[i + 1].Row : end;
            var start = Math.Min(frame.Width, band.Pad + prompt.Text.Length);
            var trimmed = prompt.Text.TrimEnd();
            var width = trimmed.Length == 0 ? 0 : PromptWidth(trimmed);
            var room = (start - band.Pad) * cell.Width;
            var shift = trimmed.Length == 0 ? 0 : Math.Max(0, room - width - cell.Width);

            // Whole device pixels, so the glyphs land on the pixel grid the unshifted ones would.
            shift = Math.Round(shift * _scale) / _scale;
            bars.Add(new FieldBar(prompt.Row, to, start, width, shift));
        }

        return bars;
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
        var rect = new Rect(area.X * cell.Width, RowTop(area.Y), area.Width * cell.Width, area.Height * cell.Height);
        context.DrawRectangle(Brush(_renderer.DefaultForeground, 0x24), null, rect.Inflate(new Thickness(1, 0)), 2, 2);
    }

    private void Rescale(double scale)
    {
        // The old fonts are not disposed: a frame already queued on the render thread may still be drawing
        // with them. A change of display scale is rare enough that one set of leaked fonts is the cheaper fix.
        _scale = scale;
        _renderer = new CellRenderer(
            new CellFonts((float)(_fontPoints * scale), _family, _lineHeight),
            _renderer.DefaultForeground,
            _renderer.DefaultBackground);
        Repainted?.Invoke();
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
                Repainted?.Invoke();
            }, DispatcherPriority.Render);
        }
    }

    // ── Keys ────────────────────────────────────────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        HandleKeyDown(e);
    }

    /// <summary>
    /// A key, as the cells take it: the window's own chords first, then the client. Also how a key pressed in another of
    /// the window's windows (a floating panel) reaches the command line, so the player keeps typing.
    /// </summary>
    internal void HandleKeyDown(KeyEventArgs e)
    {
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
            // So is a numpad digit, which KeyMapper has already sent with its digit.
            _swallowText = (key.Modifiers.HasFlag(ConsoleModifiers.Alt) && !key.Modifiers.HasFlag(ConsoleModifiers.Control))
                || key.Key is >= ConsoleKey.NumPad0 and <= ConsoleKey.NumPad9
                ? key.KeyChar
                : null;
            _driver.RaiseKey(key);
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        HandleTextInput(e);
    }

    /// <summary>Typed text, as the cells take it; also what a floating panel's window forwards.</summary>
    internal void HandleTextInput(TextInputEventArgs e)
    {
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
            SendRightClick(cell, modifiers);
            e.Handled = true;
            return;
        }

        e.Pointer.Capture(this);
        _driver.RaiseMouse(_mouse.Pressed(button, cell, e.ClickCount, modifiers), cell);
        _repeat.Start();
        e.Handled = true;
    }

    /// <summary>
    /// A right click on a cell, sent whole. The client answers a right press with a menu, which takes the pointer, so
    /// the release would never arrive: nothing is left held to repeat or to turn into a drag.
    /// </summary>
    private void SendRightClick(System.Drawing.Point cell, PointerModifiers modifiers)
    {
        _driver.RaiseMouse(_mouse.Pressed(PointerButton.Right, cell, 1, modifiers), cell);
        if (_mouse.Released(PointerButton.Right, cell, modifiers) is { } released)
        {
            _driver.RaiseMouse(released, cell);
        }
    }

    /// <summary>
    /// A right click a native view over the cells took and hands on (<see cref="MapCanvas"/>), so the pane's menu is
    /// the same over a native view as over its cells. <paramref name="position"/> is in this view's coordinates.
    /// </summary>
    internal void ForwardRightClick(Point position, KeyModifiers keys) => SendRightClick(Cell(position), Modifiers(keys));

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
    private bool TryWindowGesture(PointerPressedEventArgs e, Point position) => TryWindowGesture(e, position, emptyChrome: false);

    /// <summary>
    /// A left press on empty space of a native view over the header (<see cref="NativeHeader"/>): the window's edge
    /// band resizes, as anywhere, and the rest moves the window and double-click maximises — what a blank header cell
    /// does, whatever cell happens to lie under the native view. Returns false with a title bar, where it does nothing.
    /// </summary>
    internal bool WindowGestureFromChrome(PointerPressedEventArgs e) => TryWindowGesture(e, e.GetPosition(this), emptyChrome: true);

    private bool TryWindowGesture(PointerPressedEventArgs e, Point position, bool emptyChrome)
    {
        if (Undecorated is not { } window)
        {
            return false;
        }

        if (EdgeAt(position) is { } edge)
        {
            window.BeginResizeDrag(edge, e);
            return true;
        }

        if (!emptyChrome && (!CellsHoldHeader || !WindowChrome.IsMoveHandle(_driver.Frame, Cell(position))))
        {
            return false;
        }

        WindowGestures++;

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

    /// <summary>
    /// The window edge a pointer at <paramref name="position"/> (in this view's coordinates, which may lie outside it —
    /// a native view beside the cells asks with its own pointer) would resize, or null. Measured against the
    /// <em>window</em>: the view does not reach the window's top or left edge when the window draws its own header
    /// and sidebar there.
    /// </summary>
    private WindowEdge? EdgeAt(Point position) =>
        Undecorated is { WindowState: WindowState.Normal } window && this.TranslatePoint(position, window) is { } inWindow
            ? WindowChrome.EdgeAt(inWindow, window.ClientSize)
            : null;

    /// <summary>The resize cursor for a native view's pointer over a window edge, or null when it is not over one.</summary>
    internal StandardCursorType? EdgeCursorFromChrome(PointerEventArgs e) =>
        EdgeAt(e.GetPosition(this)) is { } edge ? WindowChrome.CursorFor(edge) : null;

    /// <summary>
    /// A left press on a native view beside the cells that lands in the window's resize band: begins the resize, as a
    /// press on a cell there does. Returns false off the band, and with a title bar.
    /// </summary>
    internal bool ResizeFromChrome(PointerPressedEventArgs e)
    {
        if (Undecorated is not { } window || EdgeAt(e.GetPosition(this)) is not { } edge)
        {
            return false;
        }

        window.BeginResizeDrag(edge, e);
        return true;
    }

    /// <summary>How many presses this view has turned into a move or a maximise. Internal for tests.</summary>
    internal int WindowGestures { get; private set; }

    internal void ToggleMaximised()
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
        var edge = !_mouse.IsHeld ? EdgeAt(position) : null;
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
    private void OpenMenu(IReadOnlyList<PointerMenuItem> items) => OpenMenu(NativeMenu.Build(items, PlacementMode.Pointer, this));

    private void OpenMenu(ContextMenu menu)
    {
        CloseTip();
        menu.Closed += (_, _) => Focus();
        OpenedMenu = menu;
        menu.Open(this);
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

        // Below the panes, the field: its padding is the band's first and last rows, and its rows sit lower.
        if (DrawsInputField && frame.Band is { } band && position.Y >= band.Top * size.Height)
        {
            var inside = (position.Y - (band.Top * size.Height) - NativeLayout.FieldTop) / size.Height;
            y = Math.Clamp(band.Top + (int)Math.Floor(inside), band.Top, Math.Min(frame.Height, band.Top + band.Rows) - 1);

            // The typed text is drawn shifted left to meet the prompt; a press on it is a press on the cell drawn there.
            foreach (var bar in _fieldBars)
            {
                if (y >= bar.Top && y < bar.End && bar.Shift > 0 && position.X >= (bar.TextStart * size.Width) - bar.Shift)
                {
                    x = Math.Clamp((int)((position.X + bar.Shift) / size.Width), Math.Min(bar.TextStart, frame.Width - 1), Math.Max(0, frame.Width - 1));
                }
            }
        }

        return new System.Drawing.Point(x, y);
    }

    private static PointerModifiers Modifiers(KeyModifiers keys) =>
        ((keys & KeyModifiers.Shift) != 0 ? PointerModifiers.Shift : 0)
        | ((keys & KeyModifiers.Alt) != 0 ? PointerModifiers.Alt : 0)
        | ((keys & KeyModifiers.Control) != 0 ? PointerModifiers.Ctrl : 0);

    /// <summary>One frame, drawn straight onto the compositor's Skia canvas.</summary>
    private sealed class FrameDraw(
        Rect bounds, ScreenFrame frame, PictureStore pictures, CellRenderer renderer, double scale, InputBand? band, SKColor plane,
        IReadOnlyList<FieldBar> bars, SKColor rule)
        : ICustomDrawOperation
    {
        public Rect Bounds { get; } = bounds;

        public bool HitTest(Point p) => Bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) =>
            other is FrameDraw draw && ReferenceEquals(draw.Frame, Frame) && draw.Bounds == Bounds && draw.Plane == Plane
            && draw.Rule == Rule && draw.Bars.SequenceEqual(Bars);

        private SKColor Rule => rule;

        private IReadOnlyList<FieldBar> Bars => bars;

        private SKColor Plane => plane;

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
            if (band is null)
            {
                renderer.Render(canvas, frame, pictures);
            }
            else
            {
                RenderWithField(canvas, band);
            }

            DrawDividers(canvas);
            canvas.Restore();
        }

        /// <summary>
        /// Each pane divider's cells as the two panes' planes meeting, read off the cells beside it, with the tokens'
        /// hairline centred where they meet; a lit divider (hovered or dragged) keeps the cells' own hover colour, wider.
        /// The cells themselves, and so the divider's hit area, are the client's as in any host. Device pixels.
        /// </summary>
        private void DrawDividers(SKCanvas canvas)
        {
            var dividers = frame.Dividers;
            if (dividers.Count == 0)
            {
                return;
            }

            var cw = renderer.Fonts.CellWidth;
            var ch = renderer.Fonts.CellHeight;
            var s = (float)scale;
            var hair = Math.Max(1f, MathF.Round(s));
            var wide = Math.Max(2f, MathF.Round(3 * s));
            using var paint = new SKPaint { IsAntialias = false, Style = SKPaintStyle.Fill };
            foreach (var divider in dividers)
            {
                var x0 = Math.Max(0, divider.Area.X);
                var y0 = Math.Max(0, divider.Area.Y);
                var x1 = Math.Min(frame.Width, divider.Area.X + divider.Area.Width);
                var y1 = Math.Min(frame.Height, divider.Area.Y + divider.Area.Height);
                if (band is { } field)
                {
                    y1 = Math.Min(y1, field.Top);
                }

                if (x1 <= x0 || y1 <= y0)
                {
                    continue;
                }

                var w = divider.Lit ? wide : hair;
                var colour = divider.Lit ? renderer.Background(frame[x0, y0]) : rule;
                if (divider.Vertical)
                {
                    float left = x0 * cw, right = x1 * cw;
                    var at = MathF.Round(left + ((right - left - w) / 2));
                    for (var y = y0; y < y1; y++)
                    {
                        paint.Color = PlaneBeside(x0 - 1, y, -1, 0);
                        canvas.DrawRect(left, y * ch, at - left, ch, paint);
                        paint.Color = PlaneBeside(x1, y, 1, 0);
                        canvas.DrawRect(at + w, y * ch, right - at - w, ch, paint);
                    }

                    paint.Color = colour;
                    canvas.DrawRect(at, y0 * ch, w, (y1 - y0) * ch, paint);
                }
                else
                {
                    float top = y0 * ch, bottom = y1 * ch;
                    var at = MathF.Round(top + ((bottom - top - w) / 2));
                    for (var x = x0; x < x1; x++)
                    {
                        paint.Color = PlaneBeside(x, y0 - 1, 0, -1);
                        canvas.DrawRect(x * cw, top, cw, at - top, paint);
                        paint.Color = PlaneBeside(x, y1, 0, 1);
                        canvas.DrawRect(x * cw, at + w, cw, bottom - at - w, paint);
                    }

                    paint.Color = colour;
                    canvas.DrawRect(x0 * cw, at, (x1 - x0) * cw, w, paint);
                }
            }
        }

        /// <summary>
        /// The plane of the pane beside a divider at (<paramref name="x"/>, <paramref name="y"/>), stepping along another
        /// divider that crosses there (along it, not across it) so a junction takes a pane's plane, not a rule's.
        /// </summary>
        private SKColor PlaneBeside(int x, int y, int dx, int dy)
        {
            for (var step = 0; step < 4; step++)
            {
                x = Math.Clamp(x, 0, frame.Width - 1);
                y = Math.Clamp(y, 0, frame.Height - 1);
                var cx = x;
                var cy = y;
                if (!frame.Dividers.Any(d => cx >= d.Area.X && cx < d.Area.X + d.Area.Width && cy >= d.Area.Y && cy < d.Area.Y + d.Area.Height))
                {
                    break;
                }

                if (dx != 0)
                {
                    y = y > 0 ? y - 1 : y + 1;
                }
                else
                {
                    x = x > 0 ? x - 1 : x + 1;
                }
            }

            return renderer.Background(frame[Math.Clamp(x, 0, frame.Width - 1), Math.Clamp(y, 0, frame.Height - 1)]);
        }

        /// <summary>
        /// The panes as they are, then the field: the strip across the bottom on the backdrop, the rounded field in the
        /// bars' own band colour (read off the cells, so armed, idle and a character's tint are the cells' own), the
        /// bars' rows drawn inside it lower by its top padding, and a one-pixel edge. Device pixels throughout.
        /// </summary>
        private void RenderWithField(SKCanvas canvas, InputBand band)
        {
            var ch = renderer.Fonts.CellHeight;
            var cw = renderer.Fonts.CellWidth;
            var s = (float)scale;
            var width = (float)(Bounds.Width * scale);
            var height = (float)(Bounds.Height * scale);
            var top = band.Top * ch;

            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, width, top));
            renderer.Render(canvas, frame, pictures);
            canvas.Restore();

            var field = FieldRect(band, cw, ch, frame.Width, s);
            var fill = renderer.Background(frame[Math.Min(frame.Width - 1, band.Pad), band.Top].Background);
            using var paint = new SKPaint { IsAntialias = true };
            paint.Color = plane;
            canvas.DrawRect(0, top, width, height - top, paint);
            paint.Color = Mix(plane, renderer.DefaultForeground, 0.10f);
            canvas.DrawRect(0, top, width, Math.Max(1, s), paint);

            var radius = (float)(NativeTokens.RadiusM * scale);
            var rounded = new SKRoundRect(field, radius);
            paint.Color = fill;
            canvas.DrawRoundRect(rounded, paint);

            canvas.Save();
            canvas.ClipRoundRect(rounded, SKClipOperation.Intersect, true);
            canvas.Translate(0, (float)(NativeLayout.FieldTop * scale));

            // The bars' rows only, and not the client's right padding, whose colour is the margin's rather than the band's.
            var textRight = (frame.Width - band.Pad) * cw;
            if (bars.Count == 0)
            {
                canvas.ClipRect(new SKRect(0, top, textRight, (band.Top + band.Rows) * ch));
                renderer.Render(canvas, frame, pictures);
            }
            else
            {
                // Each bar in two parts: the prompt's cells as they are (the UI-face prompt is drawn over them), then the
                // typed text drawn its shift to the left, to start a mono space after the prompt.
                foreach (var bar in bars)
                {
                    var shift = (float)(bar.Shift * scale);
                    var meet = (bar.TextStart * cw) - shift;
                    canvas.Save();
                    canvas.ClipRect(new SKRect(0, bar.Top * ch, meet, bar.End * ch));
                    renderer.Render(canvas, frame, pictures);
                    canvas.Restore();

                    canvas.Save();
                    // Short of the right padding by the shift, so what comes in from the right is the field's fill.
                    canvas.ClipRect(new SKRect(meet, bar.Top * ch, textRight - shift, bar.End * ch));
                    canvas.Translate(-shift, 0);
                    renderer.Render(canvas, frame, pictures);
                    canvas.Restore();
                }
            }

            canvas.Restore();

            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = Math.Max(1, s);
            paint.Color = Mix(fill, renderer.DefaultForeground, 0.22f);
            var edge = rounded;
            edge.Deflate(paint.StrokeWidth / 2, paint.StrokeWidth / 2);
            canvas.DrawRoundRect(edge, paint);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// The field's rectangle in device pixels: inset from the grid's edges so the client's band-coloured padding columns
    /// are its inner padding, from the strip's top padding to below the last row.
    /// </summary>
    internal static SKRect FieldRect(InputBand band, int cw, int ch, int columns, float scale)
    {
        var inset = Math.Max(4 * scale, (band.Pad * cw) - (float)(NativeLayout.FieldInsetX * scale));
        var top = (band.Top * ch) + (float)(NativeLayout.FieldOuter * scale);
        var bottom = ((band.Top + band.Rows) * ch) + (float)((NativeLayout.FieldTop + NativeLayout.FieldInner) * scale);
        return new SKRect(inset, top, (columns * cw) - inset, bottom);
    }

    /// <summary>
    /// The prompt in the UI face, semibold, over the prompt's own cells; and the newline hint at the right of the first
    /// row while the cells under it are blank — it stands down the moment the typed text reaches it.
    /// </summary>
    private void DrawFieldText(DrawingContext context, ScreenFrame frame, InputBand band, IReadOnlyList<FieldBar> bars)
    {
        var cell = CellSize;
        foreach (var prompt in band.Prompts)
        {
            var text = prompt.Text.TrimEnd();
            if (text.Length == 0 || prompt.Row < 0 || prompt.Row >= frame.Height || band.Pad >= frame.Width)
            {
                continue;
            }

            var first = frame[band.Pad, prompt.Row];
            var ink = Ink(frame, band.Pad, prompt.Row, text.Length);
            // The plate hides the prompt's mono cells up to where the shifted text begins (BarsOf), never over that text.
            var cells = Math.Min(text.Length, frame.Width - band.Pad) * cell.Width;
            var room = Math.Min(prompt.Text.Length, frame.Width - band.Pad) * cell.Width;
            var shift = bars.FirstOrDefault(b => b.Top == prompt.Row).Shift;
            var plate = new Rect(band.Pad * cell.Width, RowTop(prompt.Row), Math.Max(0, room - shift), cell.Height);
            context.FillRectangle(Brush(_renderer.Background(first.Background)), plate);
            var formatted = PromptText(text, ink, cells + (2 * cell.Width));

            // At the field's inner padding, as the mockup sets it, and the typed text a mono space after it (BarsOf).
            context.DrawText(formatted, new Point(Math.Round(plate.X), plate.Y + ((cell.Height - formatted.Height) / 2)));
        }

        if (band.Hint.Length == 0 || band.Top >= frame.Height)
        {
            return;
        }

        var hint = new FormattedText(
            NativeKeys.Words(band.Hint), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(NativeTokens.UiFont), NativeTokens.Small, Brush(SKColors.White));
        var right = (frame.Width - band.Pad) * cell.Width;
        var from = Math.Max(0, (int)Math.Floor((right - hint.Width) / cell.Width) - 2);
        var promptEnd = band.Pad + (band.Prompts.FirstOrDefault(p => p.Row == band.Top)?.Text.Length ?? 0);
        if (from <= promptEnd || frame.Caret is { Visible: true } caret && caret.Y == band.Top && caret.X >= from)
        {
            return;
        }

        for (var x = from; x < frame.Width - band.Pad; x++)
        {
            if (frame[x, band.Top].Rune.Value != ' ')
            {
                return;
            }
        }

        var bandColour = _renderer.Background(frame[band.Pad, band.Top].Background);
        hint.SetForegroundBrush(Brush(Mix(bandColour, _renderer.DefaultForeground, 0.6f)));
        context.DrawText(hint, new Point(right - hint.Width, RowTop(band.Top) + ((cell.Height - hint.Height) / 2)));
    }

    /// <summary>The ink of the first non-blank cell of a run, the run's own colour.</summary>
    private SKColor Ink(ScreenFrame frame, int x, int y, int length)
    {
        for (var i = x; i < Math.Min(frame.Width, x + length); i++)
        {
            if (frame[i, y].Rune.Value != ' ')
            {
                return _renderer.Foreground(frame[i, y].Foreground);
            }
        }

        return _renderer.DefaultForeground;
    }

    private static SKColor Darker(SKColor colour) => Mix(colour, SKColors.Black, 0.25f);
}
