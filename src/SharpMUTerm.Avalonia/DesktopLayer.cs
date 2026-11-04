using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;
using SkiaSharp;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's native views, in a layer over the whole window: the header band and the connections sidebar beside
/// the <see cref="TerminalView"/> (when the window draws them, <see cref="HostChrome"/>), the header buttons, and
/// native panels drawn over the panes the client says hold them. The header and the sidebar have no cells under
/// them — the client leaves no row or column for them, and this layer moves the view out of their way
/// (<see cref="NativeLayout"/>) — while a panel sits over its pane's cells, which are painted underneath exactly as in
/// a terminal, so nothing a panel does takes a cell, moves a pane or changes what any world is told.
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
    private readonly HostChrome _chrome;
    private readonly Dictionary<string, HeaderButton> _buttons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VitalsGauges> _gauges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MapCanvas> _maps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PopOutButton> _popOuts = new(StringComparer.Ordinal);
    private readonly NativeHeader _header;
    private readonly NativeSidebar _sidebar;
    private readonly Dictionary<string, WhoHereList> _whoHere = new(StringComparer.Ordinal);
    private readonly Dictionary<string, QuickBarButtons> _quickBars = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ComposerView> _composers = new(StringComparer.Ordinal);
    private readonly NoticeToasts _toasts;
    private int _noticeAboveRow;

    /// <param name="view">The cells.</param>
    /// <param name="chrome">
    /// Which chrome the window draws: the same value the client was told (<see cref="ClientHost.Chrome"/>), so the part
    /// the client leaves out of the cells is the part this layer draws, and nothing is drawn twice or not at all.
    /// </param>
    public DesktopLayer(TerminalView view, HostChrome chrome)
    {
        _view = view;
        _chrome = chrome;
        _pointer = view.Driver.Pointer;
        _view.CellsHoldHeader = !chrome.Header;
        ClipToBounds = true;
        _pointer.ActionsChanged += () => Dispatcher.UIThread.Post(Sync);
        _pointer.PanelsChanged += () => Dispatcher.UIThread.Post(Sync);
        _pointer.ChromeChanged += () => Dispatcher.UIThread.Post(Sync);

        // The chrome first, so every panel and button is drawn over it.
        _header = new NativeHeader(view) { IsVisible = false };
        _sidebar = new NativeSidebar(view) { IsVisible = false };
        Children.Add(_header);
        Children.Add(_sidebar);

        // The toasts last, over everything: a notice is the one thing the window says over a panel.
        _toasts = new NoticeToasts();
        Children.Add(_toasts);
        _pointer.NoticeSaid += notice => Dispatcher.UIThread.Post(() =>
        {
            _noticeAboveRow = notice.AboveRow;
            Sync();
            _toasts.Show(notice);
        });
        _view.Repainted += Sync;
        SyncInsets(collapsed: false);
        Sync();
    }

    /// <summary>Which chrome this layer draws. Internal for tests.</summary>
    internal HostChrome Chrome => _chrome;

    /// <summary>
    /// Where the cells start in this layer: past the native header and sidebar. Every view placed over cells is placed
    /// from here, so a panel sits on its pane however wide the sidebar is.
    /// </summary>
    internal Point CellOrigin => new(_view.Margin.Left, _view.Margin.Top);

    /// <summary>
    /// Moves the cells out of the way of the native header and sidebar: the view's margin is their size, so the view —
    /// and the grid it hands the driver, and so every pane the client lays out — is what is left of the window.
    /// </summary>
    private void SyncInsets(bool collapsed)
    {
        var insets = NativeLayout.Insets(_chrome, collapsed);
        if (_view.Margin != insets)
        {
            _view.Margin = insets;
        }
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

    /// <summary>The pop-out buttons, by the panel window each pops out. Internal for tests.</summary>
    internal IReadOnlyDictionary<string, PopOutButton> PopOuts => _popOuts;

    /// <summary>The native header. Internal for tests.</summary>
    internal NativeHeader Header => _header;

    /// <summary>The native sidebar. Internal for tests.</summary>
    internal NativeSidebar Sidebar => _sidebar;

    /// <summary>The native who-here lists by window id. Internal for tests.</summary>
    internal IReadOnlyDictionary<string, WhoHereList> WhoHere => _whoHere;

    /// <summary>The native quick bars by window id. Internal for tests.</summary>
    internal IReadOnlyDictionary<string, QuickBarButtons> QuickBars => _quickBars;

    /// <summary>The native docked composers by window id. Internal for tests.</summary>
    internal IReadOnlyDictionary<string, ComposerView> Composers => _composers;

    /// <summary>
    /// Gives the keyboard to the docked composer the client says is armed, or else to the cells: where it goes back to
    /// when ⌃P or F1 closes.
    /// </summary>
    internal void FocusArmedComposer()
    {
        if (_composers.Values.FirstOrDefault(c => c.Model is { Armed: true }) is { } armed)
        {
            armed.Editor.Focus();
            return;
        }

        _view.Focus();
    }

    /// <summary>The last header menu opened. Internal for tests.</summary>
    internal ContextMenu? OpenedMenu { get; private set; }

    /// <summary>Puts every native view where the client's last word and the last frame say it goes.</summary>
    internal void Sync()
    {
        var frame = _view.Driver.Frame;
        var cell = _view.CellExtent;
        SyncChrome();
        SyncButtons(frame, cell);
        SyncPanels(frame, cell);
        SyncToasts(frame, cell);
    }

    /// <summary>The toasts over the cell area, their stack sitting on the row the client said the pane area ends at.</summary>
    private void SyncToasts(ScreenFrame frame, Size cell)
    {
        var origin = CellOrigin;
        SetLeft(_toasts, origin.X);
        SetTop(_toasts, origin.Y);
        _toasts.Width = Math.Max(0, frame.Width * cell.Width);
        _toasts.Height = Math.Max(0, frame.Height * cell.Height);
        var above = _noticeAboveRow > 0 && _noticeAboveRow < frame.Height ? _noticeAboveRow : Math.Max(0, frame.Height - 2);
        _toasts.Bottom = above * cell.Height;
    }

    /// <summary>The notice toasts. Internal for tests.</summary>
    internal NoticeToasts Toasts => _toasts;

    /// <summary>
    /// The native header across the top and the sidebar down the left, when the window draws them — always, a modal in
    /// front included: the client draws its modals in the cells, which no longer reach under either, so the chrome
    /// stays where a desktop app's stays and only goes inert (<see cref="ChromeOverlay.Interactive"/>). ⌃B armed is said
    /// by the header itself, and a collapsed rail is the sidebar's slim strip.
    /// </summary>
    private void SyncChrome()
    {
        var chrome = _pointer.Chrome;
        SyncInsets(chrome?.Sidebar.Collapsed ?? false);
        var width = Bounds.Width;
        var height = Bounds.Height;
        var top = _chrome.Header ? NativeLayout.HeaderHeight : 0;

        // The header buttons' slots, plus the resize band, stay clear for the buttons drawn above the header.
        _header.ReserveRight = _chrome.Header
            ? (_pointer.Actions.Count * (NativeLayout.ButtonSize + NativeLayout.ButtonGap)) + WindowChrome.EdgeBand + 4
            : 0;

        // Before the client has said anything the bands are drawn empty, on the view's own colours, so a window never
        // opens with a hole where its header will be.
        var ink = chrome is null ? Colour(_view.Renderer.DefaultForeground) : NativeChromeView.Parse(chrome.Palette.Ink);
        Place(_header, _chrome.Header, new Rect(0, 0, width, NativeLayout.HeaderHeight), chrome,
            chrome is null ? Colour(_view.Renderer.DefaultBackground) : NativeChromeView.Parse(chrome.Palette.HeaderPlane), ink);
        Place(_sidebar, _chrome.Rail, new Rect(0, top, _view.Margin.Left, Math.Max(0, height - top)), chrome,
            chrome is null ? Colour(_view.Renderer.DefaultBackground) : NativeChromeView.Parse(chrome.Palette.RailPlane), ink);
    }

    private static void Place(NativeChromeView view, bool drawn, Rect area, ChromeOverlay? chrome, Color plane, Color ink)
    {
        if (!drawn || area.Width <= 0 || area.Height <= 0)
        {
            view.IsVisible = false;
            return;
        }

        SetLeft(view, area.X);
        SetTop(view, area.Y);
        view.Width = area.Width;
        view.Height = area.Height;
        view.Show(chrome, plane, ink);
        view.IsVisible = true;
    }

    private void SyncButtons(ScreenFrame frame, Size cell)
    {
        var actions = _pointer.Actions;
        foreach (var stale in _buttons.Keys.Where(id => actions.All(a => a.Id != id)).ToList())
        {
            Children.Remove(_buttons[stale]);
            _buttons.Remove(stale);
        }

        var origin = CellOrigin;
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

            if (_chrome.Header)
            {
                // In the window's own header: right to left from its end, clear of the resize band, on its plane.
                var side = NativeLayout.ButtonSize;
                button.Width = side;
                button.Height = side;
                SetLeft(button, Bounds.Width - WindowChrome.EdgeBand - 4 - ((i + 1) * (side + NativeLayout.ButtonGap)) + NativeLayout.ButtonGap);
                SetTop(button, Math.Round((NativeLayout.HeaderHeight - side) / 2));
                var said = _pointer.Chrome;
                button.Ink = said is null ? Colour(_view.Renderer.DefaultForeground) : NativeChromeView.Parse(said.Palette.Ink);
                button.Plane = said is null ? Colour(_view.Renderer.DefaultBackground) : NativeChromeView.Parse(said.Palette.HeaderPlane);
                button.IsVisible = Bounds.Width > 0;
                continue;
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
            SetLeft(button, origin.X + right - size);
            SetTop(button, origin.Y + 1);
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

        // The docked composer: a native editor over its pane, the cell editor still drawn (and still the draft) under it.
        Place(
            _composers,
            _pointer.Panels.Where(p => p.PanelId == PanelIds.Composer && p.Composer is not null && p.ComposerSession is not null),
            () => new ComposerView(_view, fullScreen: false),
            frame,
            cell,
            (composer, panel, plane) => composer.Show(panel.Composer!, panel.ComposerSession!, panel.Ink, plane, text));
        SyncPopOuts(frame, cell);
    }

    /// <summary>
    /// A pop-out button in the top right corner of every docked panel the client says can float, above the panel's
    /// own view and below the toasts. A panel that cannot float (or a host that cannot) publishes no callback, and
    /// gets no button.
    /// </summary>
    private void SyncPopOuts(ScreenFrame frame, Size cell)
    {
        var wanted = _pointer.Panels.Where(p => p.PopOut is not null && p.Rect.Width > 0 && p.Rect.Height > 0).ToList();
        foreach (var stale in _popOuts.Keys.Where(id => wanted.All(p => p.WindowId != id)).ToList())
        {
            Children.Remove(_popOuts[stale]);
            _popOuts.Remove(stale);
        }

        var origin = CellOrigin;
        foreach (var panel in wanted)
        {
            if (!_popOuts.TryGetValue(panel.WindowId, out var button))
            {
                button = new PopOutButton();
                _popOuts[panel.WindowId] = button;
                Children.Insert(Math.Max(0, Children.IndexOf(_toasts)), button);
            }

            var size = Math.Clamp(cell.Height - 2, 12, 18);
            var rect = panel.Rect;
            button.Width = size;
            button.Height = size;
            SetLeft(button, origin.X + ((rect.X + rect.Width) * cell.Width) - size - 3);
            SetTop(button, origin.Y + (rect.Y * cell.Height) + 2);
            button.Show(panel, Colour(_view.Renderer.DefaultForeground));
        }
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
            var origin = CellOrigin;
            SetLeft(control, origin.X + (rect.X * cell.Width));
            SetTop(control, origin.Y + (rect.Y * cell.Height));
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
        action.Menu(model => Dispatcher.UIThread.Post(() =>
        {
            if (model.Entries.Count == 0)
            {
                return;
            }

            var menu = NativeMenu.Build(model, action.Dispatch, PlacementMode.BottomEdgeAlignedRight, button);
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

/// <summary>
/// How big the window's own chrome is, in logical pixels: the header band across the top and the sidebar down the
/// left, each the size of a desktop app's rather than a cell's. The client is told only that it draws neither
/// (<see cref="HostChrome"/>); the sizes are the window's, and the cell grid is the rest of the window.
/// </summary>
internal static class NativeLayout
{
    /// <summary>The header band's height.</summary>
    public const double HeaderHeight = 42;

    /// <summary>The sidebar's width, open.</summary>
    public const double SidebarWidth = 232;

    /// <summary>The sidebar's width folded to its strip (⌃B b, or its own fold button).</summary>
    public const double SidebarSlim = 48;

    /// <summary>
    /// The sidebar's breathing room before the panes: its own plane carried a few pixels on, so a pane's first column
    /// of text does not sit hard against the sidebar's edge. Part of the sidebar, never of a pane, so it costs no cell.
    /// </summary>
    public const double SidebarGutter = 8;

    /// <summary>A header button's side.</summary>
    public const double ButtonSize = 30;

    /// <summary>The gap between two header buttons.</summary>
    public const double ButtonGap = 6;

    /// <summary>The cells' margin in the window: the space the native header and sidebar take.</summary>
    public static Thickness Insets(HostChrome chrome, bool collapsed) => new(
        chrome.Rail ? (collapsed ? SidebarSlim : SidebarWidth) + SidebarGutter : 0,
        chrome.Header ? HeaderHeight : 0,
        0,
        0);
}
