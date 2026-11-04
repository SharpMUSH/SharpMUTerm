using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The who-here panel as a native list, over the pane whose cells draw the same <see cref="WhoHereModel"/>: the room
/// under <c>HERE</c>, then a row per person — a presence dot in the accent (dim when idle), the name, how long they
/// have been idle — with creatures dimmed under the players. A row lights under the pointer and its tooltip is the
/// full name.
/// <para>
/// <b>It takes the pointer</b>, as the map does. A left click on a row is <see cref="PanelOverlay.Pick"/> with the
/// row's index — the client puts <c>look</c> and the name on the panel's own character's command line, the action a
/// click on the cells takes; the window never builds the line. A right click is handed on to the client as a right
/// click on the cell under it (<see cref="TerminalView.ForwardRightClick"/>), so the pane's menu is the same here as
/// over cells. It never takes keyboard focus.
/// </para>
/// </summary>
internal sealed class WhoHereList : Control
{
    private readonly TerminalView _view;

    /// <summary>
    /// What a right click does instead of reaching the cell under it: set in a floating panel's window, which has no
    /// cell under it, to open that window's own menu. Null over a pane, where the client's menu for the pane opens.
    /// </summary>
    internal Action<Point>? RightClick { get; set; }
    private readonly List<(int Index, Rect Rect)> _rows = [];
    private WhoHereModel _model = WhoHereModel.Unknown;
    private PanelInk _ink = new("#5fafff", "#ff5f5f", "#3a3a40");
    private Color _plane = Colors.Black;
    private Color _text = Colors.White;
    private double _cellHeight = 16;
    private Action<int>? _pick;
    private Size _laidOutFor;
    private WhoHereModel? _laidOutModel;
    private int? _hovered;
    private int? _pressed;
    private string? _overflow;

    public WhoHereList(TerminalView view)
    {
        _view = view;
        ClipToBounds = true;
        Focusable = false;
    }

    /// <summary>What it is showing. Internal for tests.</summary>
    internal WhoHereModel Model => _model;

    /// <summary>The rows laid out, by model index, in this control's coordinates. Internal for tests.</summary>
    internal IReadOnlyList<(int Index, Rect Rect)> Rows
    {
        get
        {
            Layout(new Size(Bounds.Width > 0 ? Bounds.Width : Width, Bounds.Height > 0 ? Bounds.Height : Height));
            return _rows;
        }
    }

    /// <summary>The row under the pointer, or null. Internal for tests.</summary>
    internal int? Hovered => _hovered;

    /// <summary>The <c>… and N more</c> line, when the pane is too short for everyone. Internal for tests.</summary>
    internal string? Overflow
    {
        get
        {
            _ = Rows;
            return _overflow;
        }
    }

    public void Show(WhoHereModel model, PanelInk ink, Color plane, Color text, double cellHeight, Action<int>? pick)
    {
        _pick = pick;
        if (model.Equals(_model) && ink == _ink && plane == _plane && text == _text && Math.Abs(cellHeight - _cellHeight) < 0.01)
        {
            return;
        }

        _model = model;
        _ink = ink;
        _plane = plane;
        _text = text;
        _cellHeight = cellHeight;
        _laidOutModel = null;
        if (_hovered >= model.Rows.Count)
        {
            Hover(null);
        }

        InvalidateVisual();
    }

    private double FontSize => Math.Clamp(_cellHeight * 0.7, 10, 15);

    private double HeaderHeight => Math.Max(22, _cellHeight * 1.5);

    private double RowHeight => Math.Max(22, _cellHeight * 1.4);

    private const double Pad = 8;

    /// <summary>Which rows fit, and where, for a size. Every row is the same height, so it is arithmetic.</summary>
    private void Layout(Size size)
    {
        if (size == _laidOutFor && ReferenceEquals(_laidOutModel, _model))
        {
            return;
        }

        _laidOutFor = size;
        _laidOutModel = _model;
        _rows.Clear();
        _overflow = null;
        var count = _model.Rows.Count;
        if (!_model.Known || count == 0)
        {
            return;
        }

        var top = HeaderHeight + 2;
        var room = Math.Max(0, (int)((size.Height - top - 4) / RowHeight));
        var fits = count <= room ? count : Math.Max(0, room - 1);
        for (var i = 0; i < fits; i++)
        {
            _rows.Add((i, new Rect(Pad / 2, top + (i * RowHeight), Math.Max(0, size.Width - Pad), RowHeight)));
        }

        if (fits < count)
        {
            _overflow = $"… and {(count - fits).ToString(CultureInfo.InvariantCulture)} more";
        }
    }

    private int? RowAt(Point point)
    {
        Layout(Bounds.Size);
        foreach (var (index, rect) in _rows)
        {
            if (rect.Contains(point))
            {
                return index;
            }
        }

        return null;
    }

    private void Hover(int? index)
    {
        if (index == _hovered)
        {
            return;
        }

        _hovered = index;
        Cursor = index is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, index is { } i && i < _model.Rows.Count ? Tip(_model.Rows[i]) : null);
        InvalidateVisual();
    }

    private static string Tip(WhoHereRow row)
    {
        var who = row.Display == row.Name || row.IsNpc ? row.Display : $"{row.Display} ({row.Name})";
        return row.Idle is { } idle ? $"{who} · {idle}" : who;
    }

    // ── Input: the list's own, never routed on to the cells (bar the right click) ────────────────

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        e.Handled = true;
        Hover(RowAt(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Hover(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            if (RightClick is { } own)
            {
                own(e.GetPosition(this));
                return;
            }

            _view.ForwardRightClick(e.GetPosition(_view), e.KeyModifiers);
            return;
        }

        _pressed = point.Properties.IsLeftButtonPressed ? RowAt(point.Position) : null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        var pressed = _pressed;
        _pressed = null;
        InvalidateVisual();
        if (e.InitialPressMouseButton == MouseButton.Left && pressed is { } index && RowAt(e.GetPosition(this)) == index)
        {
            _pick?.Invoke(index);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
    }

    /// <summary>Clicks row <paramref name="index"/> as a left press and release over it would. Internal for tests.</summary>
    internal void PerformClick(int index)
    {
        if (Rows.Any(r => r.Index == index))
        {
            _pick?.Invoke(index);
        }
    }

    // ── Drawing ──────────────────────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(_plane), bounds);
        if (bounds.Width < 16 || bounds.Height < 8)
        {
            return;
        }

        Layout(bounds.Size);
        var size = FontSize;
        var dim = PanelText.Alpha(_text, 0xA0);
        var accent = PanelText.Parse(_ink.Bar);
        var left = Pad + 4;
        var right = bounds.Width - Pad - 4;

        if (!_model.Known)
        {
            PanelText.Draw(context, WhoHereModel.UnknownText, left, HeaderHeight / 2, size, dim, false, right - left);
            return;
        }

        // HERE  The Grand Plaza · 4
        var middle = HeaderHeight / 2;
        var x = left;
        x += PanelText.Draw(context, "HERE", x, middle, size * 0.82, accent, true, right - x) + 10;
        var count = $"· {_model.Rows.Count.ToString(CultureInfo.InvariantCulture)}";
        var countWidth = PanelText.Measure(count, size, false);
        if (_model.Room is { } room)
        {
            x += PanelText.Draw(context, room, x, middle, size, _text, true, right - x - countWidth - 6) + 6;
        }

        PanelText.Draw(context, count, x, middle, size, dim, false, right - x);
        context.DrawLine(
            new Pen(new SolidColorBrush(PanelText.Alpha(_text, 0x22)), 1),
            new Point(Pad / 2, HeaderHeight + 0.5),
            new Point(bounds.Width - (Pad / 2), HeaderHeight + 0.5));

        if (_model.Rows.Count == 0)
        {
            PanelText.Draw(context, WhoHereModel.EmptyText, left, HeaderHeight + 2 + (RowHeight / 2), size, dim, false, right - left);
            return;
        }

        var idleWidth = _model.ShowsIdle ? PanelText.Measure("idle 59m", size * 0.9, false) + 8 : 0;
        foreach (var (index, rect) in _rows)
        {
            var row = _model.Rows[index];
            if (index == _hovered || index == _pressed)
            {
                var plate = (byte)(index == _pressed ? 0x38 : 0x1E);
                context.DrawRectangle(new SolidColorBrush(PanelText.Alpha(_text, plate)), null, rect, 4, 4);
            }

            var centre = rect.Center.Y;
            var dot = new Point(left + 4, centre);
            if (row.IsNpc)
            {
                context.DrawEllipse(null, new Pen(new SolidColorBrush(PanelText.Alpha(_text, 0x70)), 1.2), dot, 3, 3);
            }
            else
            {
                var colour = row.State == WhoPresence.Idle ? PanelText.Alpha(_text, 0x60) : accent;
                context.DrawEllipse(new SolidColorBrush(colour), null, dot, 4, 4);
            }

            var nameLeft = left + 16;
            var nameRoom = right - nameLeft - idleWidth;
            PanelText.Draw(context, row.Display, nameLeft, centre, size, row.IsNpc ? PanelText.Alpha(_text, 0x88) : _text, false, nameRoom);
            if (row.Idle is { } idle && idleWidth > 0)
            {
                var active = row.State == WhoPresence.Active;
                PanelText.Draw(context, idle, right, centre, size * 0.9, active ? PanelText.Alpha(accent, 0xD0) : dim, false, idleWidth, alignRight: true);
            }
        }

        if (_overflow is { } more)
        {
            var y = HeaderHeight + 2 + (_rows.Count * RowHeight) + (RowHeight / 2);
            PanelText.Draw(context, more, left, y, size * 0.9, dim, false, right - left);
        }
    }
}
