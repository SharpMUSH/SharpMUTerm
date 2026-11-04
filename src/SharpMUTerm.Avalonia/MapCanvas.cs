using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
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
/// </summary>
internal sealed class MapCanvas : Control
{
    /// <summary>Pixels between room centres at zoom 1.</summary>
    internal const double Spacing = 34;

    private const double MinZoom = 0.35, MaxZoom = 3, DragThreshold = 4;

    private readonly TerminalView _view;
    private MapViewModel _model = new(null, 0, [], [], null);
    private PanelInk _ink = new("#5fafff", "#ffaf5f", "#6a6a72");
    private Color _plane = Colors.Black;
    private Color _text = Colors.White;
    private Action<long>? _walkTo;
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
    }

    /// <summary>The zoom, 1 at <see cref="Spacing"/> pixels between rooms. Internal for tests.</summary>
    internal double Zoom { get; private set; } = 1;

    /// <summary>The pan from the character's room, in pixels. Internal for tests.</summary>
    internal Vector Pan => _pan;

    /// <summary>What it is showing. Internal for tests.</summary>
    internal MapViewModel Model => _model;

    public void Show(MapViewModel model, PanelInk ink, Color plane, Color text, Action<long>? walkTo)
    {
        _walkTo = walkTo;
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
        var middle = new Point(Bounds.Width / 2, Bounds.Height / 2) + _pan;
        return anchor is null
            ? middle
            : new Point(middle.X + ((room.X - anchor.X) * step), middle.Y + ((room.Y - anchor.Y) * step));
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        e.Handled = true;
        if (point.Properties.IsRightButtonPressed)
        {
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
        e.Handled = true;
        var position = e.GetPosition(this);
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
            ToolTip.SetTip(this, room is null ? null : room.Current ? $"{room.Name} — you are here" : $"{room.Name} — click to walk here");
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
        if (!wasDrag && e.InitialPressMouseButton == MouseButton.Left && RoomAt(e.GetPosition(this)) is { Current: false } room)
        {
            _walkTo?.Invoke(room.Id);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
        ZoomAbout(e.GetPosition(this), e.Delta.Y);
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
        var middle = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var ratio = next / Zoom;
        var fromCentre = point - (middle + _pan);
        _pan = point - middle - (fromCentre * ratio);
        Zoom = next;
        InvalidateVisual();
    }

    // ── Drawing ───────────────────────────────────────────────────────────────────────────────

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
        var plainFill = new SolidColorBrush(Mix(_plane, _text, 0.16));
        var plainEdge = new Pen(new SolidColorBrush(Faded(0.55)), 1.2);
        var radius = size * 0.22;
        foreach (var room in _model.Rooms)
        {
            var c = CentreOf(room);
            var rect = new Rect(c.X - (size / 2), c.Y - (size / 2), size, size);
            if (!rect.Intersects(bounds))
            {
                continue;
            }

            if (room.Current)
            {
                // A soft halo, then the room in the accent: the one thing the eye should find first.
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B)), null, rect.Inflate(size * 0.22), radius * 1.6, radius * 1.6);
                context.DrawRectangle(new SolidColorBrush(accent), new Pen(new SolidColorBrush(Mix(accent, Colors.White, 0.35)), 1.5), rect, radius, radius);
                continue;
            }

            var fill = room.Id == _hovered ? new SolidColorBrush(Mix(_plane, _text, 0.3)) : plainFill;
            var edge = room.Ahead ? new Pen(new SolidColorBrush(path), 2) : plainEdge;
            context.DrawRectangle(fill, edge, rect, radius, radius);
            if (room.Locked)
            {
                var lockPen = new Pen(new SolidColorBrush(Faded(0.6)), 1.2);
                context.DrawLine(lockPen, rect.TopLeft + new Vector(size * 0.25, size * 0.25), rect.BottomRight - new Vector(size * 0.25, size * 0.25));
            }
        }

        // What and where, in the corner: the area and level, and a walk under way.
        var label = _model.Area is { Length: > 0 } area ? $"{area} · level {_model.Level}" : $"level {_model.Level}";
        DrawText(context, label, new Point(10, 8), 12, Faded(0.75));
        if (_model.Walking is { } walking)
        {
            DrawText(context, walking, new Point(10, 26), 12, path);
        }
        else if (_model.Current is { } here)
        {
            DrawText(context, here.Name, new Point(10, 26), 12, accent);
        }
    }

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
