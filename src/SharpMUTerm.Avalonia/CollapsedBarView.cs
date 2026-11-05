using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// A collapsed panel's bar, over the pane whose cells draw the same <see cref="CollapsedPanelModel"/>: a rule along its
/// top, the panel's name, its unread count in the accent, and the note at the right — the mockups' <c>Chat  2 new ·····
/// collapsed in combat</c> footer of a side column. A left click anywhere on it is <see cref="PanelOverlay.Expand"/>, the
/// method the cell row's link ends in; the window builds no command. A right click is the client's, on the cell under it.
/// It never takes the keyboard. The bar is the mockup's <see cref="BarHeight"/> at the foot of what it covers (its cell
/// row and the field reserve under it, <c>DesktopLayer.BelowGrid</c>); anything above that is the column's plane, carrying
/// the panel above it down to the bar, and takes no click.
/// </summary>
internal sealed class CollapsedBarView : Control
{
    private readonly TerminalView _view;
    private CollapsedPanelModel _model = new(string.Empty, 0, string.Empty);
    private Color _plane = Colors.Black;
    private Color _text = Colors.White;
    private Color _accent = Colors.Teal;
    private Action? _expand;
    private bool _hot;

    public CollapsedBarView(TerminalView view)
    {
        _view = view;
        ClipToBounds = true;
        Focusable = false;
    }

    /// <summary>
    /// The bar's height: <c>Desktop-Combat</c>'s footer of the side column, 10 px either side of a 13 px line and its rule
    /// (the command line field's height, <see cref="NativeTokens.FieldHeight"/>).
    /// </summary>
    internal const double BarHeight = NativeTokens.FieldHeight;

    /// <summary>Where the bar starts, on a device pixel: the foot of the control, or its top when it is shorter. Internal for tests.</summary>
    internal double BarTop => NativeTokens.Snap(this, Math.Max(0, Bounds.Height - BarHeight));

    private bool InBar(Point at) => at.Y >= BarTop && new Rect(Bounds.Size).Contains(at);

    private void SetHot(bool hot)
    {
        if (hot == _hot)
        {
            return;
        }

        _hot = hot;
        Cursor = hot ? new Cursor(StandardCursorType.Hand) : null;
        ToolTip.SetTip(this, hot ? CollapsedPanelModel.ExpandHint : null);
        InvalidateVisual();
    }

    /// <summary>What it is showing. Internal for tests.</summary>
    internal CollapsedPanelModel Model => _model;

    public void Show(CollapsedPanelModel model, PanelInk ink, Color plane, Color text, Action? expand)
    {
        _expand = expand;
        var accent = PanelText.Parse(ink.Bar);
        if (model == _model && plane == _plane && text == _text && accent == _accent)
        {
            return;
        }

        _model = model;
        _plane = plane;
        _text = text;
        _accent = accent;
        InvalidateVisual();
    }

    /// <summary>Clicks the bar as a left press and release would. Internal for tests.</summary>
    internal void PerformClick() => _expand?.Invoke();

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        SetHot(InBar(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHot(false);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        e.Handled = true;
        SetHot(InBar(e.GetPosition(this)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _view.ForwardRightClick(e.GetPosition(_view), e.KeyModifiers);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        if (e.InitialPressMouseButton == MouseButton.Left && InBar(e.GetPosition(this)))
        {
            _expand?.Invoke();
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
        var top = BarTop;
        var bounds = new Rect(0, top, whole.Width, whole.Height - top);
        context.FillRectangle(new SolidColorBrush(_plane), whole);
        if (_hot)
        {
            context.FillRectangle(new SolidColorBrush(PanelText.Mix(_plane, _text, 0.05)), bounds);
        }

        context.FillRectangle(
            new SolidColorBrush(PanelText.Rule(_text, _plane)),
            NativeTokens.HorizontalRule(this, top, 0, bounds.Width));
        if (bounds.Width < 8)
        {
            return;
        }

        var middle = top + (bounds.Height / 2);
        var size = NativeTokens.Body; // the mockup's 13 px bar, as the rest of the side column
        var x = PanelText.Pad;
        var note = PanelText.Measure(_model.Note, NativeTokens.Caption, false);
        var right = bounds.Width - PanelText.Pad;
        var room = Math.Max(1, right - x - note - NativeTokens.Space16);
        x += PanelText.Draw(context, _model.Title, x, middle, size, _text, true, room);
        if (_model.UnreadText.Length > 0)
        {
            x += NativeTokens.Space8;
            PanelText.Draw(context, _model.UnreadText, x, middle, size, _accent, true, Math.Max(1, right - x - note - NativeTokens.Space8));
        }

        PanelText.Draw(context, _model.Note, right, middle, NativeTokens.Caption, PanelText.Dim(_text, _plane), false, note + 1, alignRight: true);
    }
}
