using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window host's <see cref="IFloatingPanels"/>: one <see cref="FloatingPanelWindow"/> per panel the client says is
/// popped out, owned by the main window, opened where it was left (clamped to the screens there are now) or beside the
/// main window, and closed when the client docks it. The client calls in on its own thread; everything here is posted
/// to the window's.
/// </summary>
internal sealed class FloatingPanelPresenter : IFloatingPanels
{
    private readonly Dictionary<string, FloatingPanelWindow> _windows = new(StringComparer.Ordinal);
    private IReadOnlyList<FloatingPanel> _panels = Array.Empty<FloatingPanel>();
    private Window? _owner;
    private TerminalView? _view;
    private bool _attached;

    /// <summary>What the client last said is floating; any thread.</summary>
    public IReadOnlyList<FloatingPanel> Panels => Volatile.Read(ref _panels);

    /// <summary>The open floating windows by panel window id. Window's thread; internal for tests.</summary>
    internal IReadOnlyDictionary<string, FloatingPanelWindow> Windows => _windows;

    /// <summary>
    /// The screens' working areas in screen pixels, which a remembered place is clamped to. The owner's screens by
    /// default; a test hands in its own.
    /// </summary>
    internal Func<IReadOnlyList<FloatingBounds>>? Screens { get; set; }

    /// <summary>Raised on the client's thread when <see cref="Panels"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Where the windows belong and whose cells their keys go to. On the window's thread.</summary>
    public void Attach(Window? owner, TerminalView view)
    {
        _owner = owner;
        _view = view;
        _attached = true;
        Sync();
    }

    /// <summary>The main window is going: every floating window closes with it and docks nothing.</summary>
    public void Detach()
    {
        _attached = false;
        foreach (var window in _windows.Values.ToList())
        {
            window.CloseForClient();
        }

        _windows.Clear();
    }

    public void Show(IReadOnlyList<FloatingPanel> panels)
    {
        Volatile.Write(ref _panels, panels);
        Changed?.Invoke();
        if (_attached)
        {
            Dispatcher.UIThread.Post(Sync);
        }
    }

    /// <summary>Opens, updates and closes windows to match what the client last said. On the window's thread.</summary>
    internal void Sync()
    {
        if (!_attached || _view is null)
        {
            return;
        }

        var panels = Panels;
        foreach (var gone in _windows.Keys.Where(id => panels.All(p => p.Model.WindowId != id)).ToList())
        {
            _windows[gone].CloseForClient();
            _windows.Remove(gone);
        }

        foreach (var panel in panels)
        {
            if (_windows.TryGetValue(panel.Model.WindowId, out var open))
            {
                open.Update(panel, _view.CellExtent.Height);
                if (panel.Model.Bounds.IsPlaced && !open.IsAt(panel.Model.Bounds))
                {
                    open.Place(Clamp(panel.Model.Bounds), Scaling());
                }

                continue;
            }

            var window = new FloatingPanelWindow(panel, _view);
            window.Update(panel, _view.CellExtent.Height);
            var bounds = panel.Model.Bounds.IsPlaced ? panel.Model.Bounds : BesideOwner(panel.Model.PanelId);
            window.Place(Clamp(bounds), Scaling());
            window.Closed += (_, _) =>
            {
                if (_windows.TryGetValue(panel.Model.WindowId, out var known) && ReferenceEquals(known, window))
                {
                    _windows.Remove(panel.Model.WindowId);
                }
            };
            _windows[panel.Model.WindowId] = window;

            // Opened without taking the keyboard: the player was typing on the command line, and still is.
            if (_owner is { IsVisible: true } owner)
            {
                window.Show(owner);
            }
            else
            {
                window.Show();
            }

            window.ReportPlace();
        }
    }

    private double Scaling() => _owner?.DesktopScaling is > 0 and var scale ? scale : 1;

    /// <summary>Clamped to the screens a person can see now (a monitor unplugged since is not one of them).</summary>
    private FloatingBounds Clamp(FloatingBounds bounds) => bounds.ClampTo(Screens?.Invoke() ?? OwnerScreens());

    private IReadOnlyList<FloatingBounds> OwnerScreens() =>
        _owner?.Screens.All.Select(s => new FloatingBounds(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height)).ToList()
        ?? (IReadOnlyList<FloatingBounds>)Array.Empty<FloatingBounds>();

    /// <summary>A first place for a panel: just right of the main window, top-aligned, sized for what it shows.</summary>
    private FloatingBounds BesideOwner(string panelId)
    {
        var scale = Scaling();
        var (width, height) = panelId switch
        {
            PanelIds.Map => (460.0, 460.0),
            PanelIds.Vitals => (300.0, 250.0),
            PanelIds.WhoHere => (280.0, 380.0),
            PanelIds.QuickBar => (560.0, 76.0),
            _ => (360.0, 300.0),
        };

        var x = 80.0;
        var y = 80.0;
        if (_owner is { } owner)
        {
            // Right of the main window; left of it when the right has no room on the screen and the left has, else
            // right anyway and the clamp brings it back over the main window's edge.
            var offset = _windows.Count * 28;
            var left = owner.Position.X;
            var right = owner.Position.X + (owner.Bounds.Width * scale);
            x = right + 8 + offset;
            y = owner.Position.Y + offset;
            var screens = Screens?.Invoke() ?? OwnerScreens();
            var screen = screens.FirstOrDefault(s => left >= s.X && left < s.X + s.Width) ?? screens.FirstOrDefault();
            if (screen is not null && x + (width * scale) > screen.X + screen.Width && left - 8 - (width * scale) - offset >= screen.X)
            {
                x = left - 8 - (width * scale) - offset;
            }
        }

        return new FloatingBounds(x, y, width * scale, height * scale);
    }
}

/// <summary>
/// A panel popped out into a window of its own: the panel's own native view (the map, the vitals gauges, who is here,
/// the quick bar), drawn from the same model and with the same actions as when it is docked, under a slim bar saying
/// whose it is with a way back. It never takes the keyboard on opening, and a key pressed while it is in front goes to
/// the command line, so the player keeps typing. Closing it docks the panel back.
/// </summary>
internal sealed class FloatingPanelWindow : Window
{
    private const double BarHeight = 26;

    private readonly TerminalView _view;
    private readonly Control _content;
    private readonly TextBlock _title;
    private readonly FrameButton _dock;
    private readonly Border _bar;
    private readonly DispatcherTimer _report;
    private bool _closingForClient;
    private bool _placing;

    public FloatingPanelWindow(FloatingPanel panel, TerminalView view)
    {
        _view = view;
        Panel = panel;
        NativeTokens.ApplyTextRendering(this);
        AppIcon.Apply(this);
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = true;
        MinWidth = 160;
        MinHeight = 56;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _content = panel.Model.PanelId switch
        {
            PanelIds.Map => new MapCanvas(view) { RightClick = ShowOwnMenu },
            PanelIds.Vitals => new VitalsGauges(),
            PanelIds.WhoHere => new WhoHereList(view) { RightClick = ShowOwnMenu },
            PanelIds.QuickBar => new QuickBarButtons(view) { RightClick = ShowOwnMenu },
            _ => new Border(),
        };
        _content.Focusable = false;

        _title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = NativeTokens.Body, Margin = new Thickness(NativeTokens.Space10, 0, 0, 0) };
        _dock = new FrameButton("Dock") { Width = 52, Height = 20, Margin = new Thickness(0, 0, NativeTokens.Space6, 0), VerticalAlignment = VerticalAlignment.Center };
        _dock.Click += () => Panel.Dock();
        AutomationProperties.SetName(_dock, "Dock back into the layout");
        ToolTip.SetTip(_dock, "Dock back into its place in the layout");

        var bar = new DockPanel { Height = BarHeight, LastChildFill = true };
        DockPanel.SetDock(_dock, Dock.Right);
        bar.Children.Add(_dock);
        bar.Children.Add(_title);
        _bar = new Border { Child = bar };

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_bar, Dock.Top);
        root.Children.Add(_bar);
        root.Children.Add(_content);
        Content = root;

        _report = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _report.Tick += (_, _) =>
        {
            _report.Stop();
            ReportPlace();
        };
        PositionChanged += (_, _) => Moved();
        Resized += (_, _) => Moved();
        Closing += (_, _) =>
        {
            // The player closed it: the panel goes back to its place, never away.
            if (!_closingForClient)
            {
                Panel.Dock();
            }
        };

        // A right click anywhere the panel's own view does not answer (the vitals gauges take no input) is the window's menu.
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                e.Handled = true;
                ShowOwnMenu(e.GetPosition(this));
            }
        };
    }

    /// <summary>What it is showing.</summary>
    public FloatingPanel Panel { get; private set; }

    /// <summary>The panel's own native view inside it. Internal for tests.</summary>
    internal Control PanelView => _content;

    /// <summary>The Dock button. Internal for tests.</summary>
    internal FrameButton DockButton => _dock;

    /// <summary>The last menu the window opened. Internal for tests.</summary>
    internal ContextMenu? OpenedMenu { get; private set; }

    /// <summary>Shows <paramref name="panel"/>: its title, the client's colours, and the panel's model and actions.</summary>
    public void Update(FloatingPanel panel, double cellHeight)
    {
        Panel = panel;
        Title = panel.Model.WindowTitle;
        var plane = Parse(panel.Plane, Colors.Black);
        var text = Parse(panel.Text, Colors.White);
        Background = new SolidColorBrush(plane);
        _bar.Background = new SolidColorBrush(Shade(plane, text, 0.08));
        _title.Foreground = new SolidColorBrush(Color.FromArgb(0xD0, text.R, text.G, text.B));
        _title.Text = panel.Model.WindowTitle;
        _dock.Ink = text;
        _dock.Plane = plane;

        var content = panel.Content;
        switch (_content)
        {
            case MapCanvas map when content.Map is { } model:
                map.Show(model, content.Ink, plane, text, content.WalkTo, content.SelectRoom, content.EditMap, content.LookMap, NativeTokens.LeanFor(plane));
                break;
            case VitalsGauges gauges when content.Vitals is { } vitals:
                gauges.Show(vitals, content.Ink, plane, text, cellHeight);
                break;
            case WhoHereList list when content.WhoHere is { } who:
                list.Show(who, content.Ink, plane, text, cellHeight, content.Pick);
                break;
            case QuickBarButtons bar when content.QuickBar is { } quick:
                bar.Show(quick, content.Ink, plane, text, cellHeight, content.Pick, content.Dispatch);
                break;
        }
    }

    /// <summary>Puts the window at <paramref name="bounds"/> (screen pixels), without reporting it back as a move.</summary>
    public void Place(FloatingBounds bounds, double scaling)
    {
        _placing = true;
        try
        {
            Position = new PixelPoint((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y));
            Width = Math.Max(MinWidth, bounds.Width / scaling);
            Height = Math.Max(MinHeight, bounds.Height / scaling);
        }
        finally
        {
            _placing = false;
        }
    }

    /// <summary>Where the window is now, in screen pixels.</summary>
    public FloatingBounds CurrentBounds()
    {
        var scaling = DesktopScaling > 0 ? DesktopScaling : 1;
        var size = ClientSize.Width > 0 ? ClientSize : new Size(Width, Height);
        return new FloatingBounds(Position.X, Position.Y, Math.Round(size.Width * scaling), Math.Round(size.Height * scaling));
    }

    /// <summary>Whether the window is already at <paramref name="bounds"/>, give or take a pixel.</summary>
    public bool IsAt(FloatingBounds bounds)
    {
        var now = CurrentBounds();
        return Math.Abs(now.X - bounds.X) <= 1 && Math.Abs(now.Y - bounds.Y) <= 1
            && Math.Abs(now.Width - bounds.Width) <= 2 && Math.Abs(now.Height - bounds.Height) <= 2;
    }

    /// <summary>Tells the client where the window is, so the workspace and a saved layout remember it.</summary>
    public void ReportPlace() => Panel.Moved(CurrentBounds());

    /// <summary>Closed because the client docked the panel (or the main window went): docks nothing.</summary>
    public void CloseForClient()
    {
        _closingForClient = true;
        _report.Stop();
        Close();
    }

    private void Moved()
    {
        if (_placing || _closingForClient)
        {
            return;
        }

        _report.Stop();
        _report.Start();
    }

    // Every key goes to the command line, the window's own chords included, as if the cells had it.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
        {
            _view.HandleKeyDown(e);
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (!e.Handled)
        {
            _view.HandleTextInput(e);
        }
    }

    /// <summary>The window's own menu: the way back, since there is no pane under it for the client's.</summary>
    private void ShowOwnMenu(Point at)
    {
        var items = new List<PointerMenuItem>
        {
            new($"Dock {Panel.Model.Title} back", null, () => Panel.Dock()),
        };
        var menu = NativeMenu.Build(items, PlacementMode.Pointer, this);
        OpenedMenu = menu;
        menu.Open(this);
    }

    private static Color Parse(string hex, Color fallback) => Color.TryParse(hex, out var colour) ? colour : fallback;

    /// <summary><paramref name="plane"/> moved a little toward <paramref name="ink"/>: the bar a step off the panel.</summary>
    private static Color Shade(Color plane, Color ink, double by) => Color.FromRgb(
        (byte)(plane.R + ((ink.R - plane.R) * by)),
        (byte)(plane.G + ((ink.G - plane.G) * by)),
        (byte)(plane.B + ((ink.B - plane.B) * by)));

    /// <summary>A small drawn text button, in the window's colours rather than a theme's; never takes the keyboard.</summary>
    internal sealed class FrameButton : Control
    {
        private readonly string _label;
        private bool _pressed;

        public FrameButton(string label)
        {
            _label = label;
            Focusable = false;
            Cursor = new Cursor(StandardCursorType.Hand);
        }

        public Color Ink { get; set; } = Colors.White;

        public Color Plane { get; set; } = Colors.Black;

        /// <summary>Raised on a left click.</summary>
        public event Action? Click;

        /// <summary>Clicks it, as a left press and release would. Internal for tests.</summary>
        internal void PerformClick() => Click?.Invoke();

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            _pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            e.Handled = true;
            var click = _pressed && e.InitialPressMouseButton == MouseButton.Left && new Rect(Bounds.Size).Contains(e.GetPosition(this));
            _pressed = false;
            InvalidateVisual();
            if (click)
            {
                Click?.Invoke();
            }
        }

        public override void Render(DrawingContext context)
        {
            var bounds = new Rect(Bounds.Size);
            var plate = (byte)(_pressed ? 0x55 : IsPointerOver ? 0x38 : 0x1c);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(plate, Ink.R, Ink.G, Ink.B)), null, bounds, 4, 4);
            var text = new FormattedText(
                _label,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                11.5,
                new SolidColorBrush(Color.FromArgb(0xE8, Ink.R, Ink.G, Ink.B)));
            context.DrawText(text, new Point(Math.Round((bounds.Width - text.Width) / 2), Math.Round((bounds.Height - text.Height) / 2)));
        }
    }
}

/// <summary>
/// The small button over a docked native panel's top-right corner that pops it out into a window of its own — only
/// where the window can float that panel (the client hands a callback). Drawn, never focused; its presses are the
/// window's and never reach the cells under it.
/// </summary>
internal sealed class PopOutButton : Control
{
    private bool _pressed;

    public PopOutButton()
    {
        Focusable = false;
        Cursor = new Cursor(StandardCursorType.Hand);
        AutomationProperties.SetName(this, "Pop out into its own window");
        ToolTip.SetTip(this, "Pop out into its own window");
    }

    /// <summary>The panel it pops out, as the client last published it.</summary>
    public PanelOverlay? Panel { get; private set; }

    /// <summary>The panel's rectangle in the layer, which the pointer must be over for the button to show.</summary>
    internal Rect PanelBounds { get; set; }

    /// <summary>Whether the button holds a one-row strip's reserved end, which stays (as <see cref="Plane"/>) when the icon goes.</summary>
    internal bool Reserved { get; set; }

    /// <summary>The pane's plane, painted under a reserved button so the cells under the strip's end do not show; else null.</summary>
    internal Color? Plane { get; set; }

    /// <summary>Whether the pointer is over the panel, which is when the icon is drawn.</summary>
    internal bool Hovering
    {
        get => _hovering;
        set
        {
            if (_hovering != value)
            {
                _hovering = value;
                InvalidateVisual();
            }
        }
    }

    private bool _hovering;

    public Color Ink { get; set; } = Colors.White;

    /// <summary>Shows the button for <paramref name="panel"/>, in <paramref name="ink"/>.</summary>
    public void Show(PanelOverlay panel, Color ink)
    {
        Panel = panel;
        if (Ink != ink)
        {
            Ink = ink;
            InvalidateVisual();
        }
    }

    /// <summary>Clicks it, as a left press and release would. Internal for tests.</summary>
    internal void PerformClick() => Panel?.PopOut?.Invoke();

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _pressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        var click = _pressed && e.InitialPressMouseButton == MouseButton.Left && new Rect(Bounds.Size).Contains(e.GetPosition(this));
        _pressed = false;
        InvalidateVisual();
        if (click)
        {
            PerformClick();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var whole = new Rect(Bounds.Size);
        if (Plane is { } under)
        {
            context.FillRectangle(new SolidColorBrush(under), whole);
        }

        if (!Hovering && !IsPointerOver && !_pressed)
        {
            return;
        }

        // A reserved strip end draws the icon as a square at its right, vertically centred.
        var square = Math.Min(whole.Width, Math.Min(whole.Height, 18));
        var bounds = Reserved
            ? new Rect(whole.Width - square - 3, Math.Round((whole.Height - square) / 2), square, square)
            : whole;
        using var _ = context.PushTransform(Matrix.CreateTranslation(bounds.X, bounds.Y));
        bounds = new Rect(bounds.Size);
        var plate = (byte)(_pressed ? 0x60 : IsPointerOver ? 0x40 : 0x18);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(plate, Ink.R, Ink.G, Ink.B)), null, bounds, 3, 3);

        // A frame with an arrow leaving its corner: out into a window of its own.
        var alpha = (byte)(IsPointerOver ? 0xF0 : 0xA8);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, Ink.R, Ink.G, Ink.B)), 1.2);
        var side = Math.Min(bounds.Width, bounds.Height);
        var inset = Math.Round(side * 0.24) + 0.5;
        var box = new Rect(inset, inset + 1, side - (2 * inset) - 1, side - (2 * inset) - 1);
        var gap = box.Width * 0.45;
        context.DrawLine(pen, new Point(box.Right - gap, box.Top), new Point(box.Left, box.Top));
        context.DrawLine(pen, new Point(box.Left, box.Top), new Point(box.Left, box.Bottom));
        context.DrawLine(pen, new Point(box.Left, box.Bottom), new Point(box.Right, box.Bottom));
        context.DrawLine(pen, new Point(box.Right, box.Bottom), new Point(box.Right, box.Top + gap));
        var tip = new Point(box.Right + 1, box.Top - 1);
        context.DrawLine(pen, new Point(box.Left + (box.Width * 0.45), box.Bottom - (box.Height * 0.45)), tip);
        context.DrawLine(pen, tip, new Point(tip.X - (box.Width * 0.38), tip.Y));
        context.DrawLine(pen, tip, new Point(tip.X, tip.Y + (box.Height * 0.38)));
    }
}
