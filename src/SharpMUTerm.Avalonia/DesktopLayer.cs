using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Tui;
using SkiaSharp;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's native views, in a layer over the <see cref="TerminalView"/>: header buttons, and native panels
/// drawn over the panes the client says hold them. The client's cells are painted underneath exactly as in a
/// terminal — nothing here takes a cell, moves a pane or changes what any world is told about its pane's size —
/// so this layer is the window's presentation of the same frame, and the terminal-and-window parity, which
/// compares cells, does not see it.
/// <para>
/// <b>Input.</b> The layer itself has no background and so is never hit: a pointer anywhere it draws nothing goes
/// to the view, and on to the client, as before. A header button is the window's alone — its press, release,
/// movement and wheel stop at it and never reach the client, which is what lets it sit over cells the client
/// would otherwise read as a press on the header. The vitals gauges are a picture of their pane and take no input:
/// a press, a drag, a right-click or the wheel over them reaches the client as a press on the pane's cells, so
/// focus, the pane's menu and selection behave as they do in a terminal. The map, the who-here list and the quick
/// bar are used with the pointer, so they take it (<see cref="MapCanvas"/>: zoom, pan, click to walk;
/// <see cref="WhoHereList"/>: click a name; <see cref="QuickBarButtons"/>: click a binding) and hand only a right
/// click on to the client. None of them takes keyboard focus.
/// </para>
/// </summary>
internal sealed class DesktopLayer : Canvas
{
    /// <summary>How many header cells a button covers. The header always ends in three blank cells.</summary>
    internal const int ButtonCells = 3;

    private readonly TerminalView _view;
    private readonly WindowPointer _pointer;
    private readonly Dictionary<string, HeaderButton> _buttons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VitalsGauges> _gauges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MapCanvas> _maps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WhoHereList> _whoHere = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QuickBarButtons> _quickBars = new(StringComparer.Ordinal);

    public DesktopLayer(TerminalView view)
    {
        _view = view;
        _pointer = view.Driver.Pointer;
        ClipToBounds = true;
        _pointer.ActionsChanged += () => Dispatcher.UIThread.Post(Sync);
        _pointer.PanelsChanged += () => Dispatcher.UIThread.Post(Sync);
        _view.Repainted += Sync;
        Sync();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Sync();
    }

    /// <summary>The header button for an action, or null. Internal for tests.</summary>
    internal HeaderButton? Button(string actionId) => _buttons.GetValueOrDefault(actionId);

    /// <summary>The native vitals gauges by window id. Internal for tests.</summary>
    internal IReadOnlyDictionary<string, VitalsGauges> Gauges => _gauges;

    /// <summary>The native maps by window id. Internal for tests.</summary>
    internal IReadOnlyDictionary<string, MapCanvas> Maps => _maps;

    /// <summary>The native who-here lists by window id. Internal for tests.</summary>
    internal IReadOnlyDictionary<string, WhoHereList> WhoHere => _whoHere;

    /// <summary>The native quick bars by window id. Internal for tests.</summary>
    internal IReadOnlyDictionary<string, QuickBarButtons> QuickBars => _quickBars;

    /// <summary>The last header menu opened. Internal for tests.</summary>
    internal ContextMenu? OpenedMenu { get; private set; }

    /// <summary>Puts every native view where the client's last word and the last frame say it goes.</summary>
    internal void Sync()
    {
        var frame = _view.Driver.Frame;
        var cell = _view.CellExtent;
        SyncButtons(frame, cell);
        SyncPanels(frame, cell);
    }

    private void SyncButtons(ScreenFrame frame, Size cell)
    {
        var actions = _pointer.Actions;
        foreach (var stale in _buttons.Keys.Where(id => actions.All(a => a.Id != id)).ToList())
        {
            Children.Remove(_buttons[stale]);
            _buttons.Remove(stale);
        }

        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];
            if (!_buttons.TryGetValue(action.Id, out var button))
            {
                button = new HeaderButton(action.Id);
                button.Click += () => OpenMenu(action, button);
                AutomationProperties.SetName(button, action.Label);
                ToolTip.SetTip(button, action.Hint is { } hint ? $"{action.Label} ({hint})" : action.Label);
                _buttons[action.Id] = button;
                Children.Add(button);
            }

            // Right to left from the header's end, one slot of blank cells each. Shown only while every cell of
            // its slot is blank, so a button never covers a word of the header.
            var first = frame.Width - (ButtonCells * (i + 1));
            var blank = first >= 0 && frame.Height > 0
                && Enumerable.Range(first, ButtonCells).All(x => WindowChrome.IsMoveHandle(frame, new System.Drawing.Point(x, 0)));
            button.IsVisible = blank;
            if (!blank)
            {
                continue;
            }

            // Clear of the window's resize band at the right edge, which a press there is for, and a little over
            // half a cell clear of whatever the header says before its blank cells, so it never touches a word.
            var gridRight = (first + ButtonCells) * cell.Width;
            var right = (Bounds.Width > 0 ? Math.Min(gridRight, Bounds.Width) : gridRight) - WindowChrome.EdgeBand - 1;
            var size = Math.Max(8, Math.Min(cell.Height + 2, right - ((first + 0.6) * cell.Width)));
            button.Width = size;
            button.Height = Math.Max(4, cell.Height - 2);
            SetLeft(button, right - size);
            SetTop(button, 1);
            var behind = _view.Renderer.Background(frame[frame.Width - 1, 0]);
            button.Ink = Colour(_view.Renderer.DefaultForeground);
            button.Plane = Colour(behind);
        }
    }

    private void SyncPanels(ScreenFrame frame, Size cell)
    {
        var text = Colour(_view.Renderer.DefaultForeground);
        Place(
            _gauges,
            _pointer.Panels.Where(p => p.PanelId == PanelIds.Vitals && p.Vitals is { IsEmpty: false }),
            () => new VitalsGauges(),
            frame,
            cell,
            (gauges, panel, plane) => gauges.Show(panel.Vitals!, panel.Ink, plane, text, cell.Height));
        Place(
            _maps,
            _pointer.Panels.Where(p => p.PanelId == PanelIds.Map && p.Map is not null),
            () => new MapCanvas(_view),
            frame,
            cell,
            (map, panel, plane) => map.Show(panel.Map!, panel.Ink, plane, text, panel.WalkTo));
        Place(
            _whoHere,
            _pointer.Panels.Where(p => p.PanelId == PanelIds.WhoHere && p.WhoHere is not null),
            () => new WhoHereList(_view),
            frame,
            cell,
            (list, panel, plane) => list.Show(panel.WhoHere!, panel.Ink, plane, text, cell.Height, panel.Pick));
        Place(
            _quickBars,
            _pointer.Panels.Where(p => p.PanelId == PanelIds.QuickBar && p.QuickBar is not null),
            () => new QuickBarButtons(_view),
            frame,
            cell,
            (bar, panel, plane) => bar.Show(panel.QuickBar!, panel.Ink, plane, text, cell.Height, panel.Pick, panel.Dispatch));
    }

    /// <summary>
    /// One native control per panel window, over that pane's output rectangle; a control whose panel left the
    /// screen goes with it.
    /// </summary>
    private void Place<T>(
        Dictionary<string, T> controls,
        IEnumerable<PanelOverlay> panels,
        Func<T> create,
        ScreenFrame frame,
        Size cell,
        Action<T, PanelOverlay, Color> show)
        where T : Control
    {
        var wanted = panels.ToList();
        foreach (var stale in controls.Keys.Where(id => wanted.All(p => p.WindowId != id)).ToList())
        {
            Children.Remove(controls[stale]);
            controls.Remove(stale);
        }

        foreach (var panel in wanted)
        {
            if (!controls.TryGetValue(panel.WindowId, out var control))
            {
                control = create();
                controls[panel.WindowId] = control;

                // Under the header buttons, so a button is never covered by a panel.
                Children.Insert(0, control);
            }

            var rect = panel.Rect;
            SetLeft(control, rect.X * cell.Width);
            SetTop(control, rect.Y * cell.Height);
            control.Width = rect.Width * cell.Width;
            control.Height = rect.Height * cell.Height;

            // The pane's own plane, read off the frame, so the native view sits on whatever the pane wears — its
            // character's tint, the focus lift — without the client having to say it twice.
            var inside = rect.X >= 0 && rect.Y >= 0 && rect.X < frame.Width && rect.Y < frame.Height;
            var plane = inside ? _view.Renderer.Background(frame[rect.X, rect.Y]) : _view.Renderer.DefaultBackground;
            show(control, panel, Colour(plane));
        }
    }

    /// <summary>The client builds the menu on its thread; it is opened here, under the button.</summary>
    private void OpenMenu(HeaderAction action, HeaderButton button) =>
        action.Menu(items => Dispatcher.UIThread.Post(() =>
        {
            if (items.Count == 0)
            {
                return;
            }

            var menu = NativeMenu.Build(items, PlacementMode.BottomEdgeAlignedRight, button);
            menu.Closed += (_, _) => _view.Focus();
            OpenedMenu = menu;
            menu.Open(button);
        }));

    internal static Color Colour(SKColor colour) => Color.FromRgb(colour.Red, colour.Green, colour.Blue);
}

/// <summary>
/// A small icon-only button drawn by the window over blank header cells. Drawn rather than templated, so it looks
/// the same under any Avalonia theme and its hover plate is the header's own colour lifted, not a theme's grey.
/// It never takes keyboard focus: typing stays with the client.
/// </summary>
internal sealed class HeaderButton : Control
{
    private bool _pressed;

    public HeaderButton(string actionId)
    {
        ActionId = actionId;
        Focusable = false;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public string ActionId { get; }

    public Color Ink { get; set; } = Colors.White;

    public Color Plane { get; set; } = Colors.Black;

    /// <summary>Raised on a left click.</summary>
    public event Action? Click;

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

    // Every pointer event over the button is the window's: handled here, so nothing routes on to the client.
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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
    }

    /// <summary>Clicks the button as a left press and release would. Internal for tests.</summary>
    internal void PerformClick() => Click?.Invoke();

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);

        // A plate that says "this is a control" at rest, a stronger one under the pointer and while pressed.
        var plate = (byte)(_pressed ? 0x55 : IsPointerOver ? 0x38 : 0x16);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(plate, Ink.R, Ink.G, Ink.B)), null, bounds, 4, 4);

        // The layouts glyph: a frame split into a wide pane and a column of two, the shape of a layout.
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xE0, Ink.R, Ink.G, Ink.B)), 1.25);
        var w = Math.Min(bounds.Width - 6, 14);
        var h = Math.Min(bounds.Height - 6, 11);
        var x = Math.Round((bounds.Width - w) / 2) + 0.5;
        var y = Math.Round((bounds.Height - h) / 2) + 0.5;
        var icon = new Rect(x, y, w, h);
        context.DrawRectangle(null, pen, icon, 1.5, 1.5);
        var split = Math.Round(x + (w * 0.58)) + 0.5;
        context.DrawLine(pen, new Point(split, y), new Point(split, y + h));
        var middle = Math.Round(y + (h / 2)) + 0.5;
        context.DrawLine(pen, new Point(split, middle), new Point(x + w, middle));
    }
}
