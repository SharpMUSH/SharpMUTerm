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
/// client (<see cref="PanelOverlay.EditMap"/>), the funnel the cell map's keys end in.
/// </para>
/// </summary>
internal sealed class MapCanvas : Control
{
    /// <summary>Pixels between room centres at zoom 1.</summary>
    internal const double Spacing = 34;

    private const double MinZoom = 0.35, MaxZoom = 3, DragThreshold = 4;

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
    private Action<long>? _walkTo;
    private Action<long?>? _select;
    private Action<MapEdit>? _edit;
    private readonly MapInspector _inspector = new();
    private Vector _pan;
    private Point? _pressAt;
    private Vector _panAtPress;
    private bool _dragging;
    private long? _hovered;

    public MapCanvas(TerminalView view)
    {
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
    internal double Zoom { get; private set; } = 1;

    /// <summary>The pan from the character's room, in pixels. Internal for tests.</summary>
    internal Vector Pan => _pan;

    /// <summary>What it is showing. Internal for tests.</summary>
    internal MapViewModel Model => _model;

    public void Show(MapViewModel model, PanelInk ink, Color plane, Color text, Action<long>? walkTo, Action<long?>? select = null, Action<MapEdit>? edit = null)
    {
        _walkTo = walkTo;
        _select = select;
        _edit = edit;
        _inspector.Show(model, plane, text, edit, select);
        if (model.Equals(_model) && ink == _ink && plane == _plane && text == _text)
        {
            return;
        }

        _model = model;
        _ink = ink;
        _plane = plane;
        _text = text;
        InvalidateVisual();
    }

    /// <summary>Where a room's centre is drawn, in this control's coordinates.</summary>
    internal Point CentreOf(MapViewRoom room)
    {
        var anchor = _model.Current ?? _model.Rooms.FirstOrDefault();
        var step = Spacing * Zoom;
        var middle = Middle + _pan;
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

    private double RoomSize => Math.Max(5, Spacing * Zoom * 0.56);

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

        if (e.ClickCount == 2)
        {
            _pan = default;
            InvalidateVisual();
            return;
        }

        _pressAt = point.Position;
        _panAtPress = _pan;
        _dragging = false;
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
                _pan = _panAtPress + moved;
                Cursor = new Cursor(StandardCursorType.SizeAll);
                InvalidateVisual();
            }

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
        _pressAt = null;
        _dragging = false;
        e.Pointer.Capture(null);
        Cursor = new Cursor(StandardCursorType.Arrow);
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
        ZoomAbout(e.GetPosition(this), e.Delta.Y);
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
        var next = Math.Clamp(Zoom * Math.Pow(1.18, notches), MinZoom, MaxZoom);
        if (Math.Abs(next - Zoom) < 1e-9)
        {
            return;
        }

        // The centre is middle + pan; a point at offset d from it moves to d·ratio, so the pan takes up the difference.
        var middle = Middle;
        var ratio = next / Zoom;
        var fromCentre = point - (middle + _pan);
        _pan = point - middle - (fromCentre * ratio);
        Zoom = next;
        InvalidateVisual();
    }

    // ── Drawing ───────────────────────────────────────────────────────────────────────────────

    /// <summary>The zoom from which every room's name is written under it, not only the hovered one's.</summary>
    internal const double NamesFromZoom = 1.6;

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
            entries.Add(("stairs up / down", Faded(0.85)));
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

        var byId = _model.Rooms.ToDictionary(r => r.Id);
        var exitPen = new Pen(new SolidColorBrush(Faded(0.42)), Math.Clamp(1.6 * Zoom, 1, 2.5), lineCap: PenLineCap.Round);
        foreach (var exit in _model.Exits)
        {
            if (byId.TryGetValue(exit.From, out var a) && byId.TryGetValue(exit.To, out var b))
            {
                context.DrawLine(exitPen, CentreOf(a), CentreOf(b));
            }
        }

        var size = RoomSize;
        var accent = Parse(_ink.Bar);
        var path = Parse(_ink.Low);
        var radius = size * 0.22;
        var names = Zoom >= NamesFromZoom;
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
                var spur = new Pen(new SolidColorBrush(elsewhere), Math.Clamp(2 * Zoom, 1.5, 3), lineCap: PenLineCap.Round);
                context.DrawLine(spur, rect.BottomRight - new Vector(size * 0.1, size * 0.1), rect.BottomRight + new Vector(size * 0.32, size * 0.32));
                context.DrawEllipse(new SolidColorBrush(elsewhere), null, rect.BottomRight + new Vector(size * 0.36, size * 0.36), size * 0.12, size * 0.12);
            }

            var area = AreaColourOf(room);
            if (room.Current)
            {
                // A soft halo and a ring, then the room in the accent: the one thing the eye should find first.
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x38, accent.R, accent.G, accent.B)), null, rect.Inflate(size * 0.34), radius * 2, radius * 2);
                context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0xB0, accent.R, accent.G, accent.B)), 1.5), rect.Inflate(size * 0.2), radius * 1.6, radius * 1.6);
                context.DrawRectangle(new SolidColorBrush(accent), new Pen(new SolidColorBrush(Mix(accent, Colors.White, 0.35)), 1.5), rect, radius, radius);
            }
            else
            {
                var fill = new SolidColorBrush(Mix(_plane, area, room.Id == _hovered ? 0.55 : 0.3));
                var edge = room.Ahead ? new Pen(new SolidColorBrush(path), 2.2) : new Pen(new SolidColorBrush(area), 1.4);
                context.DrawRectangle(fill, edge, rect, radius, radius);
                if (room.Locked)
                {
                    var lockPen = new Pen(new SolidColorBrush(Faded(0.7)), 1.2);
                    context.DrawLine(lockPen, rect.TopLeft + new Vector(size * 0.25, size * 0.25), rect.BottomRight - new Vector(size * 0.25, size * 0.25));
                }
            }

            DrawStairs(context, room, rect, room.Current ? Contrasting(accent) : _text);
            if (room.Symbol is { } symbol && !room.Up && !room.Down)
            {
                var mark = PanelText.Format(symbol, Math.Max(8, size * 0.5), room.Current ? Contrasting(accent) : _text, bold: true, size);
                context.DrawText(mark, new Point(c.X - (mark.Width / 2), c.Y - (mark.Height / 2)));
            }

            if (room.Noted)
            {
                context.DrawEllipse(new SolidColorBrush(path), null, rect.TopRight + new Vector(-size * 0.12, size * 0.12), size * 0.1, size * 0.1);
            }

            if (room.Selected)
            {
                // The room being edited: a ring in the draft colour, outside any halo, so it reads on the current room too.
                var ring = new Pen(new SolidColorBrush(path), 2, new DashStyle([2, 1.5], 0));
                context.DrawRectangle(null, ring, rect.Inflate(size * 0.42), radius * 2.2, radius * 2.2);
            }

            if ((names || room.Id == _hovered) && room.Id != _hovered)
            {
                DrawName(context, room.Name, c, size, plate: false);
            }
        }

        // The hovered room's name last, on a plate, so it reads over its neighbours.
        if (_hovered is { } hoveredId && byId.TryGetValue(hoveredId, out var hovered))
        {
            DrawName(context, hovered.Current ? $"{hovered.Name} — you are here" : hovered.Name, CentreOf(hovered), size, plate: true);
        }

        // What and where, in the corner: the area and level, and a walk under way.
        var label = _model.Area is { Length: > 0 } areaName ? $"{areaName} · level {_model.Level}" : $"level {_model.Level}";
        DrawText(context, label, new Point(10, 8), 12, _model.AreaColour is { } areaHex ? Parse(areaHex) : Faded(0.75));
        if (_model.Walking is { } walking)
        {
            DrawText(context, walking, new Point(10, 26), 12, path);
        }
        else if (_model.Current is { } here)
        {
            DrawText(context, here.Name, new Point(10, 26), 12, accent);
        }

        DrawLegend(context, bounds);
    }

    /// <summary>Up and down marks: a small triangle at the top of the room for a way up, at the bottom for a way down.</summary>
    private void DrawStairs(DrawingContext context, MapViewRoom room, Rect rect, Color ink)
    {
        if (!room.Up && !room.Down)
        {
            return;
        }

        var brush = new SolidColorBrush(Color.FromArgb(0xE0, ink.R, ink.G, ink.B));
        var w = rect.Width * 0.26;
        var h = rect.Height * 0.2;
        var mid = rect.Center.X;
        if (room.Up)
        {
            var top = rect.Y + (rect.Height * 0.16);
            context.DrawGeometry(brush, null, Triangle(new Point(mid, top), new Point(mid - w, top + h), new Point(mid + w, top + h)));
        }

        if (room.Down)
        {
            var bottom = rect.Bottom - (rect.Height * 0.16);
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

    /// <summary>A room's name centred under it; on a plate of the pane's own colour when <paramref name="plate"/>.</summary>
    private void DrawName(DrawingContext context, string name, Point centre, double size, bool plate)
    {
        var maxWidth = plate ? 280 : Math.Max(30, (Spacing * Zoom) - 4);
        var text = PanelText.Format(name, 11, plate ? _text : Faded(0.8), bold: plate, maxWidth);
        var at = new Point(centre.X - (text.Width / 2), centre.Y + (size / 2) + 3);
        if (plate)
        {
            var back = new Rect(at.X - 6, at.Y - 2, text.Width + 12, text.Height + 4);
            context.DrawRectangle(new SolidColorBrush(Mix(_plane, _text, 0.12)), new Pen(new SolidColorBrush(Faded(0.35)), 1), back, 4, 4);
        }
        else
        {
            // The pane's own plane behind the words, so an exit line passing under a room does not strike them out.
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xD0, _plane.R, _plane.G, _plane.B)), null, new Rect(at.X - 2, at.Y, text.Width + 4, text.Height), 2, 2);
        }

        context.DrawText(text, at);
    }

    /// <summary>
    /// The legend, in the bottom-left corner on a plate: you, the stairs, a walk, and each area in its colour. Drawn only
    /// where it fits, and it never takes the pointer from a room under it (the canvas hit-tests rooms only).
    /// </summary>
    private const double LegendSize = 11, LegendRow = 16, LegendPad = 8, LegendSwatch = 9;

    /// <summary>
    /// Where the legend's plate is drawn in a canvas of <paramref name="size"/>, in the canvas's coordinates; null when
    /// none is drawn (no rooms, or it would not fit). The window keeps its toasts off it (<see cref="NoticeToasts.Avoid"/>).
    /// </summary>
    internal Rect? LegendRect(Size size)
    {
        if (_model.Rooms.Count == 0)
        {
            return null;
        }

        var entries = LegendEntries();
        var widest = entries.Max(e => PanelText.Measure(e.Label, LegendSize, bold: false));
        var width = LegendPad + LegendSwatch + 6 + widest + LegendPad;
        var height = (LegendPad * 2) + (entries.Count * LegendRow) - (LegendRow - LegendSize - 3);
        return width > size.Width * 0.6 || height > size.Height * 0.6 ? null : new Rect(10, size.Height - height - 10, width, height);
    }

    private void DrawLegend(DrawingContext context, Rect bounds)
    {
        if (LegendRect(bounds.Size) is not { } plate)
        {
            return;
        }

        var entries = LegendEntries();
        const double size = LegendSize, row = LegendRow, pad = LegendPad, swatch = LegendSwatch;
        var widest = entries.Max(e => PanelText.Measure(e.Label, size, bold: false));
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE6, _plane.R, _plane.G, _plane.B)), new Pen(new SolidColorBrush(Faded(0.25)), 1), plate, 5, 5);
        var y = plate.Y + pad;
        foreach (var (label, colour) in entries)
        {
            var mark = new Rect(plate.X + pad, y + 2, swatch, swatch);
            if (label == "stairs up / down")
            {
                var brush = new SolidColorBrush(colour);
                context.DrawGeometry(brush, null, Triangle(new Point(mark.Center.X, mark.Y), new Point(mark.X, mark.Center.Y - 0.5), new Point(mark.Right, mark.Center.Y - 0.5)));
                context.DrawGeometry(brush, null, Triangle(new Point(mark.Center.X, mark.Bottom), new Point(mark.X, mark.Center.Y + 0.5), new Point(mark.Right, mark.Center.Y + 0.5)));
            }
            else
            {
                context.DrawRectangle(new SolidColorBrush(colour), null, mark, 2, 2);
            }

            context.DrawText(PanelText.Format(label, size, Faded(0.85), bold: false, widest + 1), new Point(mark.Right + 6, y));
            y += row;
        }
    }

    /// <summary>Black or white, whichever reads on <paramref name="fill"/>: the stairs marks on the accent room.</summary>
    private static Color Contrasting(Color fill) =>
        (0.299 * fill.R) + (0.587 * fill.G) + (0.114 * fill.B) > 140 ? Color.FromRgb(0x10, 0x10, 0x14) : Colors.White;

    private static void DrawText(DrawingContext context, string text, Point at, double size, Color colour) =>
        context.DrawText(
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, size, new SolidColorBrush(colour))
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
