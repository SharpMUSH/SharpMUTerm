using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The map as a native canvas, over the pane whose cells draw the same recorder: rooms as rounded squares joined by
/// their exits, the current room in the accent, the rooms a walk still goes through outlined in the draft colour.
/// <para>
/// <b>It takes the pointer</b>, unlike the vitals gauges, because a map is used with it: the wheel zooms about the
/// pointer, a drag pans, and a click on a room walks there — through <see cref="PanelOverlay.WalkTo"/>, which is the
/// client's speedwalk for the map's own character, the path a click on a cell map takes. The window never builds a
/// command. A right click is handed on to the client as a right click on the cell under it
/// (<see cref="TerminalView.ForwardRightClick"/>), so the pane's menu is the same here as over cells. The view is
/// centred on the character and follows them; a pan is an offset from there, and a double-click puts it back.
/// </para>
/// <para>
/// <b>Editing.</b> A right click on a room selects it for editing (<see cref="PanelOverlay.SelectRoom"/>) and opens the
/// client's room menu (<see cref="MapEditing.Menu"/>); while a room is selected a click selects instead of walking, and the
/// <see cref="MapInspector"/> card in the top right corner edits it. Every edit is a <see cref="MapEdit"/> handed to the
/// client (<see cref="PanelOverlay.EditMap"/>), the funnel the cell map's keys end in. While editing, a room dragged to
/// another grid cell is moved there on the drop: one <see cref="MapEdit.Move"/>, so one undo, the cell map's ⇧arrows' edit.
/// </para>
/// <para>
/// <b>Other levels and areas.</b> Buttons under the corner label step a level up or down and open the area menu
/// (<see cref="MapLooking.AreaMenu"/>); ⇧ with the wheel steps levels; a click on another area in the legend shows it; and
/// while the view is elsewhere a button goes back to where the character is. Each is a <see cref="MapLook"/> handed to the
/// client (<see cref="PanelOverlay.LookMap"/>), which redraws both hosts from the one model.
/// </para>
/// </summary>
internal sealed class MapCanvas : Control, IEdgeRules
{
    /// <summary>The heading's rule, once there are rooms to head.</summary>
    public IEnumerable<EdgeRule> EdgeRules(Size size)
    {
        if (_model.Rooms.Count > 0)
        {
            yield return PanelText.HeadingRule(this, size.Width, _plane, _text);
        }
    }

    /// <summary>Pixels between room centres at zoom 1.</summary>
    internal const double Spacing = 34;

    private const double MinZoom = 0.35, MaxZoom = 3, DragThreshold = 4;

    /// <summary>The most the fit zooms in: past it a small area's rooms are boxes the size of buttons.</summary>
    internal const double FitMaxZoom = 90 / Spacing;

    /// <summary>How many grid steps from the character's room the fit takes in; a larger area is fitted around them.</summary>
    internal const int FitRadius = 6;

    private readonly TerminalView _view;

    /// <summary>
    /// What a right click does instead of reaching the cell under it: set in a floating panel's window, which has no
    /// cell under it, to open that window's own menu. Null over a pane, where the client's menu for the pane opens.
    /// </summary>
    internal Action<Point>? RightClick { get; set; }
    private MapViewModel _model = new(null, 0, [], [], null);
    private PanelInk _ink = new("#5fafff", "#ffaf5f", "#6a6a72");
    private Color _plane = Colors.Black;
    private Color _text = Colors.White;
    private Color? _lean;

    /// <summary>The plane it paints, read off the cells under it. Internal for tests.</summary>
    internal Color Plane => _plane;

    /// <summary>The colour the map's shades lean toward: the tokens' focus edge, or null for the pane's ink alone. Internal for tests.</summary>
    internal Color? Lean => _lean;
    private Action<long>? _walkTo;
    private Action<long?>? _select;
    private Action<MapEdit>? _edit;
    private Action<MapLook>? _look;
    private MapViewRoom? _dragRoom;
    private Vector _dragBy;
    private readonly MapInspector _inspector = new();
    private Vector _pan;

    /// <summary>
    /// The offset and zoom that fit the area to the canvas, recomputed whenever the picture or the canvas changes until the
    /// player zooms or pans (<see cref="_moved"/>); a double-click or another level or area fits it again.
    /// </summary>
    private Vector _fit;
    private bool _moved;
    private (MapViewModel Model, Size Size, bool Inspector)? _fitFor;
    private Point? _pressAt;
    private Vector _panAtPress;
    private bool _dragging;
    private long? _hovered;
    private string? _buttonHovered;

    public MapCanvas(TerminalView view)
    {
        // Never focusable (the keyboard stays on the command line), but named for a screen reader exploring the window.
        global::Avalonia.Automation.AutomationProperties.SetName(this, "Map");
        _view = view;
        ClipToBounds = true;
        Focusable = false;
        Cursor = new Cursor(StandardCursorType.Arrow);
        VisualChildren.Add(_inspector);
        LogicalChildren.Add(_inspector);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _inspector.Measure(availableSize);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // The card arranges itself in the top right corner by its own alignment and margin.
        _inspector.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <summary>The room inspector card. Internal for tests.</summary>
    internal MapInspector Inspector => _inspector;

    /// <summary>The last room menu the canvas opened. Internal for tests.</summary>
    internal MenuModel? OpenedRoomMenu { get; private set; }

    /// <summary>The zoom, 1 at <see cref="Spacing"/> pixels between rooms. Internal for tests.</summary>
    internal double Zoom
    {
        get
        {
            Fit();
            return _zoom;
        }

        private set => _zoom = value;
    }

    private double _zoom = 1;

    /// <summary>Whether the view is the fit (true) or where the player zoomed or panned it to. Internal for tests.</summary>
    internal bool Fitted => !_moved;

    /// <summary>The pan from the character's room, in pixels. Internal for tests.</summary>
    internal Vector Pan => _pan;

    /// <summary>What it is showing. Internal for tests.</summary>
    internal MapViewModel Model => _model;

    public void Show(
        MapViewModel model, PanelInk ink, Color plane, Color text, Action<long>? walkTo, Action<long?>? select = null, Action<MapEdit>? edit = null,
        Action<MapLook>? look = null, Color? lean = null)
    {
        _walkTo = walkTo;
        _select = select;
        _edit = edit;
        _look = look;
        _inspector.Show(model, plane, text, edit, select);
        if (model.Equals(_model) && ink == _ink && plane == _plane && text == _text && lean == _lean)
        {
            return;
        }

        // Another level or area is another picture: the pan from the old one means nothing on it, and it is fitted afresh.
        if (model.Level != _model.Level || model.Area != _model.Area || model.Viewing != _model.Viewing)
        {
            _pan = default;
            _moved = false;
        }

        _model = model;
        _ink = ink;
        _plane = plane;
        _text = text;
        _lean = lean;
        InvalidateVisual();
    }

    /// <summary>Where a room's centre is drawn, in this control's coordinates.</summary>
    internal Point CentreOf(MapViewRoom room)
    {
        var anchor = _model.Current ?? _model.Rooms.FirstOrDefault();
        var step = Spacing * Zoom;
        var middle = Middle + _fit + _pan;
        return anchor is null
            ? middle
            : new Point(middle.X + ((room.X - anchor.X) * step), middle.Y + ((room.Y - anchor.Y) * step));
    }

    /// <summary>The middle of the map's open part: left of the inspector while it is up and the pane is wide enough to spare it.</summary>
    private Point Middle
    {
        get
        {
            var open = _inspector.IsVisible && Bounds.Width > MapInspector.CardWidth * 2
                ? Bounds.Width - MapInspector.CardWidth - 16
                : Bounds.Width;
            return new Point(open / 2, Bounds.Height / 2);
        }
    }

    /// <summary>The side the mockup draws a room at (32 px, its rooms 90 px apart).</summary>
    internal const double RoomTarget = 32;

    /// <summary>
    /// A room's side: the mockup's <see cref="RoomTarget"/> wherever the step leaves an exit between boxes (a little over half the
    /// step at most), growing past it only once the player zooms in far, so the exits read as lines between boxes rather than
    /// boxes touching. Internal for tests.
    /// </summary>
    internal double RoomSize
    {
        get
        {
            var step = Spacing * Zoom;
            return Math.Max(5, Math.Min(step * 0.56, Math.Max(step * 0.35, RoomTarget)));
        }
    }

    /// <summary>
    /// Fits the area to the canvas: the rooms within <see cref="FitRadius"/> steps of the character's, centred in the open
    /// part below the corner label, at the largest zoom up to <see cref="FitMaxZoom"/> that holds them with a room's width
    /// to spare. Only until the player zooms or pans; then the view is theirs.
    /// </summary>
    private void Fit()
    {
        if (_moved)
        {
            return;
        }

        var key = (_model, Bounds.Size, _inspector.IsVisible);
        if (_fitFor is { } was && ReferenceEquals(was.Model, key._model) && was.Size == key.Size && was.Inspector == key.IsVisible)
        {
            return;
        }

        _fitFor = (key._model, key.Size, key.IsVisible);
        var anchor = _model.Current ?? _model.Rooms.FirstOrDefault();
        if (anchor is null || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _zoom = 1;
            _fit = default;
            return;
        }

        var near = _model.Rooms.Where(r => Math.Abs(r.X - anchor.X) <= FitRadius && Math.Abs(r.Y - anchor.Y) <= FitRadius).ToList();
        var minX = near.Min(r => r.X - anchor.X);
        var maxX = near.Max(r => r.X - anchor.X);
        var minY = near.Min(r => r.Y - anchor.Y);
        var maxY = near.Max(r => r.Y - anchor.Y);
        var open = Middle.X * 2;

        // The rooms are fitted between what is drawn above them (the heading, a walk line, the look buttons) and below
        // them (the legend and the hint), so neither ever covers a room of the fitted view.
        var top = TopReserve();
        var bottom = BottomReserve(Bounds.Size);
        // Down, the span of rows plus a room (up to 0.56 of a step) and a name under the lowest; across, the span plus a
        // room and room for the outer names (labels.Fit keeps any name that would leave the canvas inside it).
        var across = Math.Max(1, open - 24) / ((maxX - minX + 1.3) * Spacing);
        var down = Math.Max(1, Bounds.Height - top - bottom - NameRow) / ((maxY - minY + 0.6) * Spacing);
        _zoom = Math.Clamp(Math.Min(across, down), MinZoom, FitMaxZoom);
        var step = Spacing * _zoom;
        _fit = new Vector(-(minX + maxX) / 2.0 * step, ((top - bottom - NameRow) / 2) - ((minY + maxY) / 2.0 * step));
    }

    /// <summary>The player took the view: the fit stops following, and stays where it was.</summary>
    private void TakeView()
    {
        Fit();
        _moved = true;
    }

    /// <summary>The room under a point, or null.</summary>
    internal MapViewRoom? RoomAt(Point point)
    {
        var half = (RoomSize / 2) + 2;
        return _model.Rooms.FirstOrDefault(r =>
        {
            var c = CentreOf(r);
            return Math.Abs(point.X - c.X) <= half && Math.Abs(point.Y - c.Y) <= half;
        });
    }

    // ── Input: the canvas's own, never routed on to the cells (bar the right click) ──────────────

    /// <summary>Whether a point is on the inspector card, which takes its own input.</summary>
    private bool OnInspector(Point point) => _inspector.IsVisible && _inspector.Bounds.Contains(point);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (OnInspector(point.Position))
        {
            return;
        }

        e.Handled = true;
        if (point.Properties.IsRightButtonPressed)
        {
            if (_select is not null && RoomAt(point.Position) is { } room)
            {
                OpenRoomMenu(room);
                return;
            }

            if (RightClick is { } own)
            {
                own(e.GetPosition(this));
                return;
            }

            _view.ForwardRightClick(e.GetPosition(_view), e.KeyModifiers);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (LookButtonAt(point.Position) is { } button)
        {
            if (button.Enabled)
            {
                PressLookButton(button.Id);
            }

            return;
        }

        if (LegendAreaAt(point.Position) is { } area)
        {
            _look?.Invoke(MapLook.ToArea(area));
            return;
        }

        if (e.ClickCount == 2)
        {
            // Re-centre: the fit again, as when the map opened.
            _pan = default;
            _moved = false;
            _fitFor = null;
            InvalidateVisual();
            return;
        }

        _pressAt = point.Position;
        _panAtPress = _pan;
        _dragging = false;
        _dragBy = default;
        _dragRoom = _model.Editing && _edit is not null ? RoomAt(point.Position) : null;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_pressAt is null && OnInspector(position))
        {
            return;
        }

        e.Handled = true;
        if (_pressAt is { } from)
        {
            var moved = position - from;
            if (_dragging || Math.Abs(moved.X) + Math.Abs(moved.Y) > DragThreshold)
            {
                _dragging = true;
                if (_dragRoom is not null)
                {
                    _dragBy = moved; // a room being moved: drawn at the cell it would land in, the map stays put
                }
                else
                {
                    TakeView();
                    _pan = _panAtPress + moved;
                }

                Cursor = new Cursor(StandardCursorType.SizeAll);
                InvalidateVisual();
            }

            return;
        }

        var button = LookButtonAt(position);
        if (button?.Id != _buttonHovered)
        {
            _buttonHovered = button?.Id;
            Cursor = new Cursor(button is { Enabled: true } ? StandardCursorType.Hand : StandardCursorType.Arrow);
            InvalidateVisual();
        }

        if (button is not null)
        {
            return;
        }

        var room = RoomAt(position);
        if (room?.Id != _hovered)
        {
            _hovered = room?.Id;
            Cursor = new Cursor(room is null ? StandardCursorType.Arrow : StandardCursorType.Hand);
            // The name is drawn on the canvas beside the room; the tip says only what a click does.
            ToolTip.SetTip(this, room is null ? null
                : _model.Editing && _select is not null ? "Click to edit this room"
                : room.Current ? "Right-click to edit this room" : "Click to walk here · right-click to edit");
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        if (_pressAt is null)
        {
            return;
        }

        var wasDrag = _dragging;
        var dragged = _dragRoom;
        var by = _dragBy;
        _pressAt = null;
        _dragging = false;
        _dragRoom = null;
        _dragBy = default;
        e.Pointer.Capture(null);
        Cursor = new Cursor(StandardCursorType.Arrow);
        if (wasDrag && dragged is not null)
        {
            DropRoom(dragged, by);
            InvalidateVisual();
            return;
        }

        if (wasDrag || e.InitialPressMouseButton != MouseButton.Left || RoomAt(e.GetPosition(this)) is not { } room)
        {
            return;
        }

        // While a room is being edited a click picks the room to edit; otherwise it walks, as it always has.
        if (_model.Editing && _select is not null)
        {
            _select(room.Id);
        }
        else if (!room.Current)
        {
            _walkTo?.Invoke(room.Id);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (OnInspector(e.GetPosition(this)))
        {
            return;
        }

        e.Handled = true;
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
        {
            // ⇧ with the wheel steps levels; some platforms turn ⇧-wheel into a sideways scroll, so either axis counts.
            var notches = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
            if (notches > 0 && _model.LevelAbove)
            {
                _look?.Invoke(MapLook.Up);
            }
            else if (notches < 0 && _model.LevelBelow)
            {
                _look?.Invoke(MapLook.Down);
            }

            return;
        }

        ZoomAbout(e.GetPosition(this), e.Delta.Y);
    }

    /// <summary>The grid cells a drag of <paramref name="moved"/> pixels comes to, at the zoom shown.</summary>
    internal (int Dx, int Dy) SnapOf(Vector moved)
    {
        var step = Spacing * Zoom;
        return ((int)Math.Round(moved.X / step), (int)Math.Round(moved.Y / step));
    }

    /// <summary>
    /// A room dropped <paramref name="by"/> pixels from where it was picked up: selected, and moved to the grid cell it came
    /// to as one edit (none when it came back to its own cell, which is a click that selects it). Internal for tests,
    /// which drop a room the way a release does.
    /// </summary>
    internal void DropRoom(MapViewRoom room, Vector by)
    {
        _select?.Invoke(room.Id);
        var (dx, dy) = SnapOf(by);
        if ((dx, dy) != (0, 0))
        {
            _edit?.Invoke(MapEdit.Move(room.Id, dx, dy));
        }
    }

    // ── The look buttons: a level up or down, the area menu, and back to where the character is ──────────────

    /// <summary>A look button: what it is, where it is drawn, its words, and whether it does anything now.</summary>
    internal sealed record LookButton(string Id, Rect Rect, string Label, bool Enabled);

    internal const string UpButton = "up", DownButton = "down", AreasButton = "areas", HereButton = "here";

    /// <summary>
    /// The look buttons the view offers, left to right under the corner label: ▲ ▼ while the area has more than one level,
    /// Areas ▾ while the map has more than one area, and Back to where I am while the view is elsewhere. Internal for tests.
    /// </summary>
    internal IReadOnlyList<LookButton> LookButtons()
    {
        var buttons = new List<LookButton>();
        if (_look is null || _model.Rooms.Count == 0)
        {
            return buttons;
        }

        var x = PanelText.Pad;
        var y = UnderHeading();
        void Add(string id, string label, bool enabled)
        {
            var width = PanelText.Measure(label, ButtonSize, bold: false) + (2 * NativeTokens.Space10);
            buttons.Add(new LookButton(id, new Rect(x, y, width, ButtonHeight), label, enabled));
            x += width + NativeTokens.Space6;
        }

        if (_model.Levels.Count > 1)
        {
            Add(UpButton, "▲ Up", _model.LevelAbove);
            Add(DownButton, "▼ Down", _model.LevelBelow);
        }

        if (_model.AreaChoices.Count > 1)
        {
            Add(AreasButton, "Areas ▾", true);
        }

        if (_model.Viewing)
        {
            Add(HereButton, "Back to where I am", true);
        }

        return buttons;
    }

    /// <summary>The look buttons' type and height: the window's small controls (<see cref="NativeTokens.ControlHeightSmall"/>).</summary>
    private const double ButtonSize = NativeTokens.Body, ButtonHeight = NativeTokens.ControlHeightSmall;

    /// <summary>The first row under the panel heading: the walk line while one is under way, else the look buttons.</summary>
    private double UnderHeading() =>
        PanelText.HeadingHeight(CellHeight) + NativeTokens.Space10 + (_model.Walking is not null ? NativeTokens.Space20 : 0);

    /// <summary>What is drawn above the rooms: the heading, a walk line, and the look buttons while there are any.</summary>
    private double TopReserve()
    {
        var top = UnderHeading();
        return LookButtons().Count > 0 ? top + ButtonHeight + NativeTokens.Space8 : top;
    }

    /// <summary>What is drawn below the rooms: the legend and the hint, with a gap above them.</summary>
    private double BottomReserve(Size size) =>
        (LegendRect(size) is { } legend ? size.Height - legend.Top : HintBand()) + NativeTokens.Space12;

    /// <summary>The cell height the panel sits on, which sizes its heading as every panel's.</summary>
    private double CellHeight => _view.CellExtent.Height > 0 ? _view.CellExtent.Height : 17;

    private LookButton? LookButtonAt(Point point) => LookButtons().FirstOrDefault(b => b.Rect.Contains(point));

    /// <summary>What a look button does. Internal for tests, which press one the way a click does.</summary>
    internal void PressLookButton(string id)
    {
        switch (id)
        {
            case UpButton:
                _look?.Invoke(MapLook.Up);
                break;
            case DownButton:
                _look?.Invoke(MapLook.Down);
                break;
            case HereButton:
                _look?.Invoke(MapLook.Here);
                break;
            case AreasButton:
                OpenAreaMenu();
                break;
        }
    }

    /// <summary>The last area menu the canvas opened. Internal for tests.</summary>
    internal MenuModel? OpenedAreaMenu { get; private set; }

    /// <summary>Opens the client's area menu: each area the map has, and the way back while the view is elsewhere.</summary>
    internal void OpenAreaMenu()
    {
        var model = MapLooking.AreaMenu(_model);
        OpenedAreaMenu = model;
        var view = _model;
        var menu = NativeMenu.Build(model, verb => RunAreaVerb(view, verb), global::Avalonia.Controls.PlacementMode.Pointer, this);
        menu.Open(this);
    }

    /// <summary>What an area menu entry does. Internal for tests, which choose an entry the way a click on it would.</summary>
    internal void RunAreaVerb(MapViewModel view, string verb)
    {
        if (MapLooking.LookFor(verb, view) is { } look)
        {
            _look?.Invoke(look);
        }
    }

    /// <summary>The other area whose legend row is under a point, or null: a click there shows that area.</summary>
    internal string? LegendAreaAt(Point point)
    {
        if (_look is null || LegendRect(Bounds.Size) is not { } plate || !plate.Contains(point))
        {
            return null;
        }

        var entries = LegendEntries();
        var hit = LegendLayout(Bounds.Size).FirstOrDefault(e => e.Area.Contains(point));
        if (hit.Area.Width <= 0)
        {
            return null;
        }

        var first = entries.Count - _model.Areas.Count;
        return hit.Index >= first && !_model.Areas[hit.Index - first].Drawn ? _model.Areas[hit.Index - first].Name : null;
    }

    /// <summary>
    /// Selects <paramref name="room"/> and opens the client's menu for it: walk there, each edit, undo. An edit that needs
    /// words opens the inspector at its field; the rest are handed to the client as they are.
    /// </summary>
    internal void OpenRoomMenu(MapViewRoom room)
    {
        _select?.Invoke(room.Id);
        var model = MapEditing.Menu(room, _model.UndoLabel);
        OpenedRoomMenu = model;
        var menu = NativeMenu.Build(model, verb => RunMenuVerb(room.Id, verb), global::Avalonia.Controls.PlacementMode.Pointer, this);
        menu.Open(this);
    }

    /// <summary>What a room menu entry does. Internal for tests, which choose an entry the way a click on it would.</summary>
    internal void RunMenuVerb(long room, string verb)
    {
        if (verb == MapEditing.WalkVerb)
        {
            _walkTo?.Invoke(room);
        }
        else if (MapEditing.TextVerbs.Contains(verb))
        {
            // The inspector shows the room once the client has published it selected; the field takes the keyboard then.
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => _inspector.FocusField(verb), global::Avalonia.Threading.DispatcherPriority.Background);
        }
        else if (MapEditing.EditFor(verb, room) is { } edit)
        {
            _edit?.Invoke(edit);
        }
    }

    /// <summary>Zooms by <paramref name="notches"/> about <paramref name="point"/>, keeping what is under it there.</summary>
    internal void ZoomAbout(Point point, double notches)
    {
        TakeView();
        var next = Math.Clamp(Zoom * Math.Pow(1.18, notches), MinZoom, MaxZoom);
        if (Math.Abs(next - Zoom) < 1e-9)
        {
            return;
        }

        // The centre is middle + pan; a point at offset d from it moves to d·ratio, so the pan takes up the difference.
        var middle = Middle;
        var ratio = next / Zoom;
        var fromCentre = point - (middle + _fit + _pan);
        _pan = point - middle - _fit - (fromCentre * ratio);
        Zoom = next;
        InvalidateVisual();
    }

    // ── Drawing ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The pixels between room centres from which every room's name is written under it: wherever a few letters fit
    /// between two rooms. Below it only the character's own room is named (and the hovered one, on a plate).
    /// </summary>
    internal const double NamesFromStep = 40;

    /// <summary>Whether every room is named at the zoom shown. Internal for tests.</summary>
    internal bool NamesShown => Spacing * Zoom >= NamesFromStep;

    /// <summary>
    /// The colour a room is drawn in: its area's, as the view gives it (<see cref="MapViewModel.AreaColour"/>), or the
    /// text colour faded when the area has no name. Internal for tests, which hold it to the model's hex.
    /// </summary>
    internal Color AreaColourOf(MapViewRoom room) =>
        _model.AreaColour is { } hex ? Parse(hex) : Faded(0.55);

    /// <summary>The colour of the area a room's way out leads into, or null. Internal for tests.</summary>
    internal Color? ElsewhereColourOf(MapViewRoom room) =>
        _model.ColourOf(room.Elsewhere) is { } hex ? Parse(hex) : null;

    /// <summary>
    /// The legend's entries, in the order drawn: you, the stairs marks when a room has them, a walk under way, then
    /// every area the view names in its colour. Internal for tests.
    /// </summary>
    internal IReadOnlyList<(string Label, Color Swatch)> LegendEntries()
    {
        var entries = new List<(string, Color)> { ("you", Parse(_ink.Bar)) };
        if (_model.Rooms.Any(r => r.Up || r.Down))
        {
            entries.Add((StairsLabel, DimInk));
        }

        if (_model.Rooms.Any(r => r.Ahead))
        {
            entries.Add(("walk", Parse(_ink.Low)));
        }

        entries.AddRange(_model.Areas.Select(a => (a.Name, Parse(a.Colour))));
        return entries;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(_plane), bounds);
        if (_model.Rooms.Count == 0)
        {
            DrawText(context, "No rooms mapped yet — walk somewhere.", new Point(12, 10), 12, Faded(0.7));
            return;
        }

        // The mockup's map: rooms a step off the pane's plane with a rule round them, exits as rules between them, the
        // character's room ringed in the accent. Every shade is a mix of the pane's own plane and ink, so it is the same
        // drawing on every theme; only the accent, the walk and the areas' marks are colours.
        var rule = RuleColour;
        var byId = _model.Rooms.ToDictionary(r => r.Id);
        var exitWidth = ExitWidth;
        var exitPen = new Pen(new SolidColorBrush(rule), exitWidth, lineCap: PenLineCap.Round);
        foreach (var exit in _model.Exits)
        {
            if (byId.TryGetValue(exit.From, out var a) && byId.TryGetValue(exit.To, out var b))
            {
                var (from, to) = AlignExit(CentreOf(a), CentreOf(b), exitWidth);
                context.DrawLine(exitPen, from, to);
            }
        }

        var size = RoomSize;
        var accent = Parse(_ink.Bar);
        var path = Parse(_ink.Low);
        var radius = size * 0.19;
        var edge = new Pen(new SolidColorBrush(rule), Math.Clamp(size / 22, 1, 1.5));
        foreach (var room in _model.Rooms)
        {
            var c = CentreOf(room);
            var rect = new Rect(c.X - (size / 2), c.Y - (size / 2), size, size);
            if (!rect.Inflate(size).Intersects(bounds))
            {
                continue;
            }

            // A way into another area: a short spur off the room's corner, in that area's colour.
            if (ElsewhereColourOf(room) is { } elsewhere)
            {
                var spur = new Pen(new SolidColorBrush(elsewhere), Math.Clamp(1.6 * Zoom, 1.25, 2.5), lineCap: PenLineCap.Round);
                context.DrawLine(spur, rect.BottomRight - new Vector(size * 0.1, size * 0.1), rect.BottomRight + new Vector(size * 0.3, size * 0.3));
                var dot = Math.Max(2, size * 0.11);
                context.DrawEllipse(new SolidColorBrush(elsewhere), null, rect.BottomRight + new Vector(size * 0.34, size * 0.34), dot, dot);
            }

            if (room.Current)
            {
                // The accent at a fifth over the plane, a two-pixel ring of it, a touch larger than its neighbours: the one
                // thing the eye should find first, without a solid block of colour.
                var ring = rect.Inflate(size * 0.06);
                context.DrawRectangle(
                    new SolidColorBrush(Mix(_plane, accent, 0.2)), new Pen(new SolidColorBrush(accent), NativeTokens.BorderStrong), ring, radius * 1.15, radius * 1.15);
            }
            else
            {
                var fill = new SolidColorBrush(Shade(room.Id == _hovered ? RoomHoverShade : RoomShade));
                context.DrawRectangle(fill, room.Ahead ? new Pen(new SolidColorBrush(path), NativeTokens.BorderStrong) : edge, rect, radius, radius);
                if (room.Locked)
                {
                    var lockPen = new Pen(new SolidColorBrush(Faded(0.6)), 1.2);
                    context.DrawLine(lockPen, rect.TopLeft + new Vector(size * 0.25, size * 0.25), rect.BottomRight - new Vector(size * 0.25, size * 0.25));
                }
            }

            DrawStairs(context, room, rect, room.Current ? _text : DimInk);
            var mark = room.Symbol ?? (room.Current ? YouGlyph : null);
            if (mark is not null && !room.Up && !room.Down && size >= 10)
            {
                var glyph = new FormattedText(
                    mark, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(NativeTokens.MonoFont), Math.Max(8, size * 0.42), new SolidColorBrush(_text))
                {
                    MaxLineCount = 1,
                };
                context.DrawText(glyph, new Point(c.X - (glyph.Width / 2), c.Y - (glyph.Height / 2)));
            }

            if (room.Noted)
            {
                context.DrawEllipse(new SolidColorBrush(path), null, rect.TopRight + new Vector(-size * 0.12, size * 0.12), size * 0.1, size * 0.1);
            }

            if (room.Selected)
            {
                // The room being edited: a ring in the draft colour, outside the room's own, so it reads on the current room too.
                var ring = new Pen(new SolidColorBrush(path), 2, new DashStyle([2, 1.5], 0));
                context.DrawRectangle(null, ring, rect.Inflate(size * 0.3), radius * 2, radius * 2);
            }

        }

        // Names after every room, where the layout found them a place that covers no room and no other name.
        foreach (var label in Labels(bounds))
        {
            if (label.Room != _hovered)
            {
                context.DrawRectangle(new SolidColorBrush(_plane), null, label.Rect.Inflate(new Thickness(NativeTokens.Space2, 0)), 2, 2);
                context.DrawText(label.Text, label.Rect.TopLeft);
            }
        }

        // The hovered room's name last, on a plate, so it reads over its neighbours.
        if (_hovered is { } hoveredId && byId.TryGetValue(hoveredId, out var hovered))
        {
            DrawName(context, hovered.Current ? $"{hovered.Name} — you are here" : hovered.Name, CentreOf(hovered), size, plate: true);
        }

        // What and where: the panel heading (Map, the area and level) with the area's colour as a mark before its name's
        // line, then a walk under way, under it. The character's room is named under its own box.
        var heading = PanelText.Heading(context, PanelHeading.For(_model), bounds.Width, CellHeight, _plane, _text, right: NativeTokens.Space20, rule: NativeTokens.DevicePixel(this));
        if (_model.AreaColour is { } areaHex)
        {
            context.DrawRectangle(new SolidColorBrush(Parse(areaHex)), null, new Rect(PanelText.Pad - 9, (heading / 2) - 5, 3, 10), 1.5, 1.5);
        }

        if (_model.Walking is { } walking)
        {
            DrawText(context, walking, new Point(PanelText.Pad, heading + NativeTokens.Space8), NativeTokens.Body, path);
        }

        if (_dragRoom is { } moving && byId.TryGetValue(moving.Id, out var shown))
        {
            // Where the room would land: an outline on the grid cell the drag has come to, joined to where it is now.
            var (dx, dy) = SnapOf(_dragBy);
            var from = CentreOf(shown);
            var step = Spacing * Zoom;
            var to = new Point(from.X + (dx * step), from.Y + (dy * step));
            var ghost = new Pen(new SolidColorBrush(path), 2, new DashStyle([3, 2], 0));
            context.DrawLine(new Pen(new SolidColorBrush(Faded(0.5)), 1, new DashStyle([2, 2], 0)), from, to);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x40, path.R, path.G, path.B)), ghost, new Rect(to.X - (size / 2), to.Y - (size / 2), size, size), radius, radius);
        }

        DrawLookButtons(context);
        DrawLegend(context, bounds);
        DrawHint(context, bounds);
    }

    /// <summary>The mark in the character's room when the game gives it none: the roguelike's "you".</summary>
    internal const string YouGlyph = "@";

    /// <summary>
    /// The map's shades are steps of the pane's plane, so they sit on whatever the pane wears (a tint, Light, Solarized).
    /// With a <see cref="Lean"/> (the tokens' focus edge, the mockups' blue) the steps go toward it: a room's fill toward the
    /// edge itself, a rule toward the pane's ink leaned <see cref="LeanShare"/> of the way to it, which on Dark lands the
    /// rooms and exits on <c>Desktop-Combat</c>'s <c>#1d2333</c> and <c>#3a4257</c> rather than a neutral grey. With none
    /// (a host that said no theme), the steps go toward the pane's ink alone, as they always did.
    /// </summary>
    internal const double LeanShare = 0.7;

    /// <summary>A room's fill, its hover, a name's plate: steps toward the pane's ink; with a lean, <see cref="FillLean"/> times as far toward it.</summary>
    internal const double RoomShade = 0.07, RoomHoverShade = 0.13, PlateShade = 0.1;

    /// <summary>How much further a fill steps toward the lean than toward the ink: the edge is far darker than the ink.</summary>
    internal const double FillLean = 2.8;

    /// <summary>A rule's step: toward the ink, or with a lean toward the leaned ink.</summary>
    internal const double RuleShade = 0.24, LeanedRuleShade = 0.44;

    private Color Shade(double amount) =>
        _lean is { } lean ? Mix(_plane, lean, Math.Min(1, amount * FillLean)) : Mix(_plane, _text, amount);

    /// <summary>The map's rule: a room's outline and an exit. Internal for tests.</summary>
    internal Color RuleColour =>
        _lean is { } lean ? Mix(_plane, Mix(_text, lean, LeanShare), LeanedRuleShade) : Mix(_plane, _text, RuleShade);

    /// <summary>A room's fill. Internal for tests.</summary>
    internal Color RoomFill => Shade(RoomShade);

    /// <summary>The map's secondary ink (names, the legend, the hint): opaque, so it reads the same over a rule.</summary>
    private Color DimInk => Mix(_plane, _text, 0.62);

    private void DrawLookButtons(DrawingContext context)
    {
        foreach (var button in LookButtons())
        {
            var hovered = button.Enabled && _buttonHovered == button.Id;
            context.DrawRectangle(
                new SolidColorBrush(Shade(hovered ? RoomHoverShade : RoomShade)),
                new Pen(new SolidColorBrush(RuleColour), NativeTokens.DevicePixel(this)),
                NativeTokens.StrokeRect(this, button.Rect),
                NativeTokens.RadiusControl,
                NativeTokens.RadiusControl);
            var ink = button.Enabled ? _text : Mix(_plane, _text, 0.4);
            var label = PanelText.Format(button.Label, ButtonSize, ink, bold: false, button.Rect.Width);
            context.DrawText(label, new Point(Math.Round(button.Rect.X + NativeTokens.Space10), Math.Round(button.Rect.Y + ((button.Rect.Height - label.Height) / 2))));
        }
    }

    /// <summary>Up and down marks: a small triangle at the top of the room for a way up, at the bottom for a way down.</summary>
    private static void DrawStairs(DrawingContext context, MapViewRoom room, Rect rect, Color ink)
    {
        if (!room.Up && !room.Down)
        {
            return;
        }

        var brush = new SolidColorBrush(ink);
        var w = rect.Width * 0.22;
        var h = rect.Height * 0.18;
        var mid = rect.Center.X;
        if (room.Up)
        {
            var top = rect.Y + (rect.Height * 0.18);
            context.DrawGeometry(brush, null, Triangle(new Point(mid, top), new Point(mid - w, top + h), new Point(mid + w, top + h)));
        }

        if (room.Down)
        {
            var bottom = rect.Bottom - (rect.Height * 0.18);
            context.DrawGeometry(brush, null, Triangle(new Point(mid, bottom), new Point(mid - w, bottom - h), new Point(mid + w, bottom - h)));
        }
    }

    private static StreamGeometry Triangle(Point a, Point b, Point c)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(a, isFilled: true);
        g.LineTo(b);
        g.LineTo(c);
        g.EndFigure(isClosed: true);
        return geometry;
    }

    // ── Names: wider than the room, placed so that no two collide ──────────────────────────────────

    /// <summary>A room's name where the layout put it: the room, the box it takes, the words as drawn (elided only when cut).</summary>
    internal sealed record PlacedName(long Room, Rect Rect, FormattedText Text, bool Elided);

    /// <summary>The height a name row takes under a room, its gap included: what the fit leaves under the lowest row.</summary>
    private const double NameRow = NativeTokens.Space4 + 15;

    /// <summary>The widest a name is drawn before it is elided even with the room for it.</summary>
    internal const double NameMaxWidth = 168;

    /// <summary>The narrowest an elided name is drawn: below it the name is left to the hover.</summary>
    private const double NameMinWidth = 34;

    private (MapViewModel Model, double Zoom, Vector Offset, Size Size, Color Plane, Color Text)? _labelsFor;
    private List<PlacedName> _labels = [];

    /// <summary>
    /// Where each room's name is drawn at the view shown. The character's room is placed first, then the others nearest it
    /// first. A name goes where its plate covers no room, no name already placed and no line the map draws (every exit and
    /// spur, <see cref="ExitSegments"/>, not only its own room's), on the canvas: under its room, over it or beside it,
    /// else pulled to one side. A name that fits nowhere whole is elided to the widest that fits in one of those places;
    /// one that has not <see cref="NameMinWidth"/> even then is left to the hover's plate. A name never crosses a line.
    /// Below <see cref="NamesFromStep"/> only the character's room is named. Internal for tests.
    /// </summary>
    internal IReadOnlyList<PlacedName> Labels(Rect bounds)
    {
        Fit();
        var key = (_model, _zoom, _fit + _pan, bounds.Size, _plane, _text);
        if (_labelsFor is { } was && ReferenceEquals(was.Model, key._model) && was.Zoom == key._zoom && was.Offset == key.Item3
            && was.Size == key.Size && was.Plane == key._plane && was.Text == key._text)
        {
            return _labels;
        }

        _labelsFor = (key._model, key._zoom, key.Item3, key.Size, key._plane, key._text);
        _labels = LayOutNames(bounds);
        return _labels;
    }

    private List<PlacedName> LayOutNames(Rect bounds)
    {
        var placed = new List<PlacedName>();
        if (_model.Rooms.Count == 0)
        {
            return placed;
        }

        var size = RoomSize;
        // Between what is drawn above the rooms (the heading and the look buttons) and below them (the legend and the hint).
        var top = TopReserve() - NativeTokens.Space4;
        var bottom = bounds.Height - BottomReserve(bounds.Size) + NativeTokens.Space8;
        var area = new Rect(bounds.X + 2, bounds.Y + top, Math.Max(0, bounds.Width - 4), Math.Max(0, bottom - top));
        var visible = _model.Rooms
            .Select(r => (Room: r, Centre: CentreOf(r)))
            .Where(r => bounds.Inflate(size).Contains(r.Centre))
            .ToList();
        var boxes = visible.Select(r => new Rect(r.Centre.X - (size / 2) - 2, r.Centre.Y - (size / 2) - 2, size + 4, size + 4)).ToList();
        var lines = ExitSegments();

        var anchor = _model.Current ?? _model.Rooms[0];
        var order = visible
            .Where(r => NamesShown || r.Room.Current)
            .OrderBy(r => r.Room.Current ? 0 : 1)
            .ThenBy(r => Math.Abs(r.Room.X - anchor.X) + Math.Abs(r.Room.Y - anchor.Y))
            .ThenBy(r => r.Room.Y)
            .ThenBy(r => r.Room.X);
        foreach (var (room, c) in order)
        {
            var full = PanelText.Format(room.Name, NativeTokens.Small, DimInk, bold: false, NameMaxWidth);
            var w = full.Width;
            var h = full.Height;
            bool Free(Rect r) =>
                area.Contains(r)
                && !boxes.Any(b => b.Intersects(r))
                && !placed.Any(p => p.Rect.Inflate(new Thickness(NativeTokens.Space4, NativeTokens.Space2)).Intersects(r))
                && !lines.Any(l => Crosses(l.From, l.To, PlateOf(r)));

            var below = c.Y + (size / 2) + NativeTokens.Space4;
            var above = c.Y - (size / 2) - NativeTokens.Space4 - h;
            var middle = c.Y - (h / 2);
            Rect[] Places(double width) =>
            [
                new(c.X - (width / 2), below, width, h),
                new(c.X - (width / 2), above, width, h),
                new(c.X + (size / 2) + 6, middle, width, h),
                new(c.X - (size / 2) - 6 - width, middle, width, h),
                new(c.X - (size / 2), below, width, h),
                new(c.X + (size / 2) - width, below, width, h),
                new(c.X - (size / 2), above, width, h),
                new(c.X + (size / 2) - width, above, width, h),

                // Clear of a line straight down or up out of the room: just to one side of its middle.
                new(c.X + NativeTokens.Space4, below, width, h),
                new(c.X - NativeTokens.Space4 - width, below, width, h),
                new(c.X + NativeTokens.Space4, above, width, h),
                new(c.X - NativeTokens.Space4 - width, above, width, h),
            ];

            // Whole, under, over or beside the room, then pulled to one side; never across a line, a room or a name.
            var spot = Places(w).Select(r => Snap(Inside(r, area))).Cast<Rect?>().FirstOrDefault(r => Free(r!.Value));
            if (spot is { } found)
            {
                placed.Add(new PlacedName(room.Id, found, full, Elided: false));
                continue;
            }

            // Nowhere whole: the widest elision that fits in any of those places. None, and the name is left to the hover.
            for (var width = Math.Floor(w) - 6; width >= NameMinWidth; width -= 6)
            {
                var cut = PanelText.Format(room.Name, NativeTokens.Small, DimInk, bold: false, width);
                var fits = Places(cut.Width).Select(r => Snap(Inside(r, area))).Cast<Rect?>().FirstOrDefault(r => Free(r!.Value));
                if (fits is { } at)
                {
                    placed.Add(new PlacedName(room.Id, at, cut, Elided: true));
                    break;
                }
            }
        }

        return placed;
    }

    /// <summary>
    /// The box a name's plate takes on the canvas (<see cref="Render"/> draws it <see cref="NativeTokens.Space2"/> wider each
    /// side), plus a pixel for the exit pen's width: what must miss every line. Internal for tests.
    /// </summary>
    internal static Rect PlateOf(Rect name) => name.Inflate(new Thickness(NativeTokens.Space2 + PenAllowance, PenAllowance));

    /// <summary>
    /// Half the widest exit pen (<see cref="NativeTokens.MapExit"/>, up to a device pixel more once rounded) plus the half
    /// device pixel <see cref="AlignExit"/> may move it, kept clear of a name's plate.
    /// </summary>
    private const double PenAllowance = 1.5;

    /// <summary>
    /// An exit's width: the mockup's 2 px wherever names are drawn (a step of <see cref="NamesFromStep"/> or more), thinner
    /// only when zoomed out past that, in whole device pixels so a straight exit is a crisp line. Internal for tests.
    /// </summary>
    internal double ExitWidth => NativeTokens.LineWidth(this, NativeTokens.MapExit * Math.Clamp(Spacing * Zoom / NamesFromStep, 0.5, 1));

    /// <summary>
    /// A straight (level or upright) exit moved by up to half a device pixel so its edges fall on device pixels, as the
    /// mockup's 2 px lines do; a diagonal is left where it is. Internal for tests.
    /// </summary>
    internal (Point From, Point To) AlignExit(Point from, Point to, double width)
    {
        if (Math.Abs(from.X - to.X) < 0.5)
        {
            var x = NativeTokens.LineCentre(this, (from.X + to.X) / 2, width);
            return (new Point(x, from.Y), new Point(x, to.Y));
        }

        if (Math.Abs(from.Y - to.Y) < 0.5)
        {
            var y = NativeTokens.LineCentre(this, (from.Y + to.Y) / 2, width);
            return (new Point(from.X, y), new Point(to.X, y));
        }

        return (from, to);
    }

    /// <summary>
    /// Every line the map draws between rooms or off one, at the view shown: each exit between two rooms of the view (on
    /// screen or not, since a line from an off-screen room still crosses the canvas) and each spur into another area. A
    /// name crosses none of them. Internal for tests.
    /// </summary>
    internal List<(Point From, Point To)> ExitSegments()
    {
        var size = RoomSize;
        var centres = _model.Rooms.ToDictionary(r => r.Id, CentreOf);
        var segments = _model.Exits
            .Where(e => centres.ContainsKey(e.From) && centres.ContainsKey(e.To))
            .Select(e => (centres[e.From], centres[e.To]))
            .ToList();
        foreach (var room in _model.Rooms.Where(r => ElsewhereColourOf(r) is not null))
        {
            var corner = centres[room.Id] + new Vector(size / 2, size / 2);
            var dot = Math.Max(2, size * 0.11);
            segments.Add((corner - new Vector(size * 0.1, size * 0.1), corner + new Vector((size * 0.34) + dot, (size * 0.34) + dot)));
        }

        return segments;
    }

    /// <summary><paramref name="r"/> moved the least distance that puts it inside <paramref name="area"/> across (rows stay).</summary>
    private static Rect Inside(Rect r, Rect area)
    {
        var x = Math.Min(Math.Max(r.X, area.X), Math.Max(area.X, area.Right - r.Width));
        return new Rect(x, r.Y, r.Width, r.Height);
    }

    private static Rect Snap(Rect r) => new(Math.Round(r.X), Math.Round(r.Y), r.Width, r.Height);

    /// <summary>Whether the segment from <paramref name="a"/> to <paramref name="b"/> passes through <paramref name="r"/> (Liang–Barsky).</summary>
    internal static bool Crosses(Point a, Point b, Rect r)
    {
        double t0 = 0, t1 = 1;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-9)
            {
                return q >= 0;
            }

            var t = q / p;
            if (p < 0)
            {
                if (t > t1)
                {
                    return false;
                }

                t0 = Math.Max(t0, t);
            }
            else
            {
                if (t < t0)
                {
                    return false;
                }

                t1 = Math.Min(t1, t);
            }

            return true;
        }

        return Clip(-dx, a.X - r.Left) && Clip(dx, r.Right - a.X) && Clip(-dy, a.Y - r.Top) && Clip(dy, r.Bottom - a.Y) && t0 <= t1;
    }

    /// <summary>A room's name centred under it; on a plate of the pane's own colour when <paramref name="plate"/>.</summary>
    private void DrawName(DrawingContext context, string name, Point centre, double size, bool plate)
    {
        var maxWidth = plate ? 280 : Math.Max(30, (Spacing * Zoom) - 4);
        var text = PanelText.Format(name, NativeTokens.Small, plate ? _text : DimInk, bold: plate, maxWidth);
        var at = new Point(Math.Round(centre.X - (text.Width / 2)), Math.Round(centre.Y + (size / 2) + NativeTokens.Space4));
        if (plate)
        {
            var back = new Rect(at.X - 6, at.Y - 2, text.Width + 12, text.Height + 4);
            context.DrawRectangle(new SolidColorBrush(Shade(PlateShade)), new Pen(new SolidColorBrush(RuleColour), 1), back, NativeTokens.RadiusS, NativeTokens.RadiusS);
        }
        else
        {
            // The pane's own plane behind the words, so an exit line passing under a room does not strike them out.
            context.DrawRectangle(new SolidColorBrush(_plane), null, new Rect(at.X - 2, at.Y, text.Width + 4, text.Height), 2, 2);
        }

        context.DrawText(text, at);
    }

    /// <summary>
    /// The legend, a strip along the foot of the map above the hint: you, the stairs, a walk, and each area in its colour,
    /// laid left to right and wrapped. The fit keeps the rooms above it (<see cref="BottomReserve"/>), so it covers none.
    /// </summary>
    private const double LegendSize = NativeTokens.Small, LegendRow = 18, LegendSwatch = 8, LegendGap = NativeTokens.Space14;

    /// <summary>The hint's band at the foot of the canvas, its gaps included; a small margin when there is no hint.</summary>
    private double HintBand() => _model.Hint is null ? NativeTokens.Space8 : NativeTokens.Space12 + NativeTokens.Space20;

    /// <summary>Each legend entry's place in a canvas of <paramref name="size"/>, by its index in <see cref="LegendEntries"/>.</summary>
    private List<(Rect Area, int Index)> LegendLayout(Size size)
    {
        var entries = LegendEntries();
        var placed = new List<(Rect, int)>();
        var x = PanelText.Pad;
        var row = 0;
        var right = size.Width - PanelText.Pad;
        for (var i = 0; i < entries.Count; i++)
        {
            var width = LegendSwatch + NativeTokens.Space6 + PanelText.Measure(entries[i].Label, LegendSize, bold: false);
            if (x > PanelText.Pad && x + width > right)
            {
                x = PanelText.Pad;
                row++;
            }

            placed.Add((new Rect(x, row * LegendRow, Math.Max(1, Math.Min(width, right - x)), LegendRow), i));
            x += width + LegendGap;
        }

        // Laid out from the top of the strip, then moved down so its last row ends above the hint.
        var top = size.Height - HintBand() - ((row + 1) * LegendRow);
        return placed.Select(p => (p.Item1.Translate(new Vector(0, top)), p.Item2)).ToList();
    }

    /// <summary>
    /// Where the legend is drawn in a canvas of <paramref name="size"/>, in the canvas's coordinates; null when none is
    /// drawn (no rooms, or it would take more than a third of the height). The window keeps its toasts off it
    /// (<see cref="NoticeToasts.Avoid"/>).
    /// </summary>
    internal Rect? LegendRect(Size size)
    {
        if (_model.Rooms.Count == 0 || size.Width <= 0)
        {
            return null;
        }

        var area = LegendLayout(size).Select(e => e.Area).Aggregate((a, b) => a.Union(b));
        return area.Height > size.Height / 3 ? null : area.Inflate(new Thickness(NativeTokens.Space4, 0));
    }

    private void DrawLegend(DrawingContext context, Rect bounds)
    {
        if (LegendRect(bounds.Size) is null)
        {
            return;
        }

        var entries = LegendEntries();
        foreach (var (area, index) in LegendLayout(bounds.Size))
        {
            var (label, colour) = entries[index];
            var mark = new Rect(area.X, area.Y + ((area.Height - LegendSwatch) / 2), LegendSwatch, LegendSwatch);
            if (label == StairsLabel)
            {
                var brush = new SolidColorBrush(colour);
                context.DrawGeometry(brush, null, Triangle(new Point(mark.Center.X, mark.Y), new Point(mark.X, mark.Center.Y - 0.5), new Point(mark.Right, mark.Center.Y - 0.5)));
                context.DrawGeometry(brush, null, Triangle(new Point(mark.Center.X, mark.Bottom), new Point(mark.X, mark.Center.Y + 0.5), new Point(mark.Right, mark.Center.Y + 0.5)));
            }
            else
            {
                context.DrawRectangle(new SolidColorBrush(colour), null, mark, 2, 2);
            }

            PanelText.Draw(context, label, mark.Right + NativeTokens.Space6, area.Center.Y, LegendSize, DimInk, bold: false, area.Right - mark.Right - NativeTokens.Space6 + 1);
        }
    }

    /// <summary>The model's hint, centred at the foot of the map: what a click on a room does.</summary>
    private void DrawHint(DrawingContext context, Rect bounds)
    {
        if (_model.Hint is not { } hint)
        {
            return;
        }

        var text = PanelText.Format(hint, NativeTokens.Small, DimInk, bold: false, bounds.Width - (2 * PanelText.Pad));
        context.DrawText(text, new Point(Math.Round((bounds.Width - text.Width) / 2), Math.Round(bounds.Height - NativeTokens.Space12 - text.Height)));
    }

    /// <summary>The legend's label for the stairs marks.</summary>
    internal const string StairsLabel = "stairs up / down";

    private static void DrawText(DrawingContext context, string text, Point at, double size, Color colour) =>
        context.DrawText(
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(NativeTokens.UiFont), size, new SolidColorBrush(colour))
            {
                MaxLineCount = 1,
            },
            at);

    private Color Faded(double opacity) => Color.FromArgb((byte)(opacity * 255), _text.R, _text.G, _text.B);

    private static Color Parse(string hex) => Color.TryParse(hex, out var colour) ? colour : Colors.Gray;

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)(from.R + ((to.R - from.R) * amount)),
        (byte)(from.G + ((to.G - from.G) * amount)),
        (byte)(from.B + ((to.B - from.B) * amount)));
}
