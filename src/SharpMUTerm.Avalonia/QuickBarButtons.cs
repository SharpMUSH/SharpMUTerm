using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The quick bar as native buttons, over the pane whose cells draw the same <see cref="QuickBarModel"/>: one button
/// per binding (its key in the accent, then its name), a <c>+N</c> button holding the bindings that do not fit, and
/// <c>F4 edits keys</c> as a link at the right. Every button is drawn alike — the model marks no binding as special,
/// so neither does the window — except that a key this host cannot deliver is drawn dim, as the cells draw it.
/// <para>
/// <b>It takes the pointer</b>, as the map does, and never the keyboard. A click on a binding is
/// <see cref="PanelOverlay.Pick"/> with its index: the client runs that binding on the panel's own character through
/// the path the key takes. The <c>+N</c> menu's entries do the same. The link dispatches
/// <see cref="QuickBarModel.EditCommand"/> through <see cref="PanelOverlay.Dispatch"/>. The window never builds a
/// command. A right click is handed on to the client as a right click on the cell under it.
/// </para>
/// </summary>
internal sealed class QuickBarButtons : Control
{
    /// <summary>What a laid-out slot does when clicked.</summary>
    internal enum SlotKind
    {
        /// <summary>Runs the binding at <see cref="Slot.Index"/>.</summary>
        Item,

        /// <summary>Opens the menu of bindings that did not fit.</summary>
        More,

        /// <summary>Opens F4.</summary>
        Edit,
    }

    /// <summary>One slot of the bar, in this control's coordinates.</summary>
    internal readonly record struct Slot(SlotKind Kind, int Index, Rect Rect);

    /// <summary>The widest a binding's name is drawn before it is trimmed.</summary>
    internal const double MaxTitleWidth = 150;

    private const double Gap = 6, PadX = 8;

    private readonly TerminalView _view;
    private readonly List<Slot> _slots = [];
    private QuickBarModel _model = QuickBarModel.Empty;
    private PanelInk _ink = new("#5fafff", "#ff5f5f", "#3a3a40");
    private Color _plane = Colors.Black;
    private Color _text = Colors.White;
    private double _cellHeight = 16;
    private Action<int>? _pick;
    private Action<string>? _dispatch;
    private Size _laidOutFor;
    private QuickBarModel? _laidOutModel;
    private Slot? _hovered;
    private Slot? _pressed;

    public QuickBarButtons(TerminalView view)
    {
        _view = view;
        ClipToBounds = true;
        Focusable = false;
    }

    /// <summary>What it is showing. Internal for tests.</summary>
    internal QuickBarModel Model => _model;

    /// <summary>The slots laid out for the current size. Internal for tests.</summary>
    internal IReadOnlyList<Slot> Slots
    {
        get
        {
            Layout(new Size(Bounds.Width > 0 ? Bounds.Width : Width, Bounds.Height > 0 ? Bounds.Height : Height));
            return _slots;
        }
    }

    /// <summary>The indexes the <c>+N</c> button holds. Internal for tests.</summary>
    internal IReadOnlyList<int> Overflowed
    {
        get
        {
            var shown = Slots.Where(s => s.Kind == SlotKind.Item).Select(s => s.Index).ToHashSet();
            return Enumerable.Range(0, _model.Items.Count).Where(i => !shown.Contains(i)).ToList();
        }
    }

    /// <summary>The last <c>+N</c> menu opened. Internal for tests.</summary>
    internal ContextMenu? OpenedMenu { get; private set; }

    public void Show(QuickBarModel model, PanelInk ink, Color plane, Color text, double cellHeight, Action<int>? pick, Action<string>? dispatch)
    {
        _pick = pick;
        _dispatch = dispatch;
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
        Hover(null);
        InvalidateVisual();
    }

    private double FontSize => Math.Clamp(_cellHeight * 0.62, 9, 13);

    private static string Trim(QuickBarItem item) => item.Title;

    private double ItemWidth(QuickBarItem item)
    {
        var size = FontSize;
        var title = Math.Min(MaxTitleWidth, PanelText.Measure(Trim(item), size, false));
        return PadX + PanelText.Measure(item.Key, size, true) + 5 + title + PadX;
    }

    private double MoreWidth(int hidden) => PadX + PanelText.Measure($"+{hidden.ToString(CultureInfo.InvariantCulture)}", FontSize, true) + PadX;

    private double EditWidth => PanelText.Measure(QuickBarModel.EditHint, FontSize, false) + (PadX * 2);

    /// <summary>
    /// The cells' rule, in pixels: the link first, then as many bindings as fit in order, with <c>+N</c> for the
    /// rest; without room for the link, the bindings alone.
    /// </summary>
    private void Layout(Size size)
    {
        if (size == _laidOutFor && ReferenceEquals(_laidOutModel, _model))
        {
            return;
        }

        _laidOutFor = size;
        _laidOutModel = _model;
        _slots.Clear();
        if (size.Width < 8 || size.Height < 4)
        {
            return;
        }

        var height = Math.Max(4, size.Height - 4);
        var top = (size.Height - height) / 2;
        var items = _model.Items;
        var widths = items.Select(ItemWidth).ToList();
        foreach (var withEdit in new[] { true, false })
        {
            var room = size.Width - (Gap * 2) - (withEdit ? EditWidth + Gap : 0);
            for (var take = items.Count; take >= 0 && room >= 0; take--)
            {
                var hidden = items.Count - take;
                var used = widths.Take(take).Sum() + (Gap * Math.Max(0, take - 1))
                    + (hidden > 0 ? (take > 0 ? Gap : 0) + MoreWidth(hidden) : 0);
                if (used > room)
                {
                    continue;
                }

                var x = Gap;
                for (var i = 0; i < take; i++)
                {
                    _slots.Add(new Slot(SlotKind.Item, i, new Rect(x, top, widths[i], height)));
                    x += widths[i] + Gap;
                }

                if (hidden > 0)
                {
                    _slots.Add(new Slot(SlotKind.More, hidden, new Rect(x, top, MoreWidth(hidden), height)));
                }

                if (withEdit)
                {
                    _slots.Add(new Slot(SlotKind.Edit, -1, new Rect(size.Width - Gap - EditWidth, top, EditWidth, height)));
                }

                return;
            }
        }
    }

    private Slot? SlotAt(Point point)
    {
        Layout(Bounds.Size);
        foreach (var slot in _slots)
        {
            if (slot.Rect.Contains(point))
            {
                return slot;
            }
        }

        return null;
    }

    private void Hover(Slot? slot)
    {
        if (slot == _hovered)
        {
            return;
        }

        _hovered = slot;
        Cursor = slot is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, slot is { } s ? Tip(s) : null);
        InvalidateVisual();
    }

    private string? Tip(Slot slot) => slot.Kind switch
    {
        SlotKind.Item when slot.Index < _model.Items.Count => $"{_model.Items[slot.Index].Title} ({_model.Items[slot.Index].Key})",
        SlotKind.More => $"{slot.Index.ToString(CultureInfo.InvariantCulture)} more",
        SlotKind.Edit => "Edit the keys (F4)",
        _ => null,
    };

    // ── Input: the bar's own, never routed on to the cells (bar the right click) ─────────────────

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        e.Handled = true;
        Hover(SlotAt(e.GetPosition(this)));
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
            _view.ForwardRightClick(e.GetPosition(_view), e.KeyModifiers);
            return;
        }

        _pressed = point.Properties.IsLeftButtonPressed ? SlotAt(point.Position) : null;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        var pressed = _pressed;
        _pressed = null;
        InvalidateVisual();
        if (e.InitialPressMouseButton == MouseButton.Left && pressed is { } slot && SlotAt(e.GetPosition(this)) == slot)
        {
            Activate(slot);
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
    }

    /// <summary>Clicks a slot as a left press and release over it would. Internal for tests.</summary>
    internal void PerformClick(Slot slot) => Activate(slot);

    private void Activate(Slot slot)
    {
        switch (slot.Kind)
        {
            case SlotKind.Item:
                _pick?.Invoke(slot.Index);
                break;
            case SlotKind.Edit:
                _dispatch?.Invoke(QuickBarModel.EditCommand);
                break;
            case SlotKind.More:
                OpenMore(slot);
                break;
        }
    }

    /// <summary>The bindings that did not fit, as a native menu over the <c>+N</c> button; each entry is a pick.</summary>
    private void OpenMore(Slot slot)
    {
        var pick = _pick;
        var items = Overflowed
            .Select(i => new PointerMenuItem(_model.Items[i].Title, _model.Items[i].Key, () => pick?.Invoke(i)))
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        var menu = NativeMenu.Build(items, PlacementMode.TopEdgeAlignedRight, this);
        menu.PlacementRect = slot.Rect;
        menu.Closed += (_, _) => _view.Focus();
        OpenedMenu = menu;
        menu.Open(this);
    }

    // ── Drawing ──────────────────────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(_plane), bounds);
        Layout(bounds.Size);
        var size = FontSize;
        var accent = PanelText.Parse(_ink.Bar);
        var dim = PanelText.Alpha(_text, 0xA0);
        var middle = bounds.Height / 2;

        if (_model.Items.Count == 0)
        {
            var editLeft = _slots.FirstOrDefault(s => s.Kind == SlotKind.Edit).Rect.Left;
            PanelText.Draw(context, QuickBarModel.EmptyText, Gap + 2, middle, size, dim, false, (editLeft > 0 ? editLeft : bounds.Width) - Gap - 8);
        }

        foreach (var slot in _slots)
        {
            var hot = slot == _hovered;
            var down = slot == _pressed;
            var rect = slot.Rect;
            switch (slot.Kind)
            {
                case SlotKind.Item:
                {
                    var item = _model.Items[slot.Index];
                    Plate(context, rect, hot, down);
                    var keyInk = item.Fires ? accent : dim;
                    var titleInk = item.Fires ? _text : dim;
                    var x = rect.Left + PadX;
                    x += PanelText.Draw(context, item.Key, x, middle, size, keyInk, true, rect.Width) + 5;
                    PanelText.Draw(context, Trim(item), x, middle, size, titleInk, false, rect.Right - PadX - x + 0.5);
                    break;
                }

                case SlotKind.More:
                    Plate(context, rect, hot, down);
                    PanelText.Draw(context, $"+{slot.Index.ToString(CultureInfo.InvariantCulture)}", rect.Left + PadX, middle, size, _text, true, rect.Width);
                    break;

                case SlotKind.Edit:
                {
                    // A link, not a button: the accent key, the words in the dim ink, underlined under the pointer.
                    var x = rect.Left + PadX;
                    var keyWidth = PanelText.Draw(context, QuickBarModel.EditKey, x, middle, size, accent, true, rect.Width);
                    var rest = QuickBarModel.EditHint[QuickBarModel.EditKey.Length..];
                    var restWidth = PanelText.Draw(context, rest, x + keyWidth, middle, size, hot ? _text : dim, false, rect.Right - x - keyWidth);
                    if (hot)
                    {
                        var y = Math.Round(middle + (size * 0.62)) + 0.5;
                        context.DrawLine(new Pen(new SolidColorBrush(PanelText.Alpha(_text, 0xC0)), 1), new Point(x, y), new Point(x + keyWidth + restWidth, y));
                    }

                    break;
                }
            }
        }
    }

    /// <summary>A button's plate: the text colour at a low alpha, stronger under the pointer and while pressed.</summary>
    private void Plate(DrawingContext context, Rect rect, bool hot, bool down)
    {
        var alpha = (byte)(down ? 0x48 : hot ? 0x30 : 0x18);
        context.DrawRectangle(
            new SolidColorBrush(PanelText.Alpha(_text, alpha)),
            new Pen(new SolidColorBrush(PanelText.Alpha(_text, 0x26)), 1),
            rect.Deflate(0.5),
            4,
            4);
    }
}
