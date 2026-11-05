using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using SharpMUTerm.Client.Chrome;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// Dragging a tab off a native strip. The strip starts it (<see cref="NativeTabStrip.DragStarted"/>) and the layer takes
/// the pointer, because the client tears the strips down to show its drop preview and a pointer held by a strip would be
/// lost with it. Every move is handed to the client as a desktop cell, plus the slot among the strip's tabs while the
/// pointer is over its own strip (the tabs as laid out when the drag began); the client resolves the drop, shows the
/// cells' preview and publishes back what the drop would do (<see cref="PaneChromeOverlay.Drag"/>), which
/// <see cref="TabDragView"/> and the strip draw. The release asks the client to drop; a right press or the pointer lost
/// abandons it. The window decides nothing.
/// </summary>
internal sealed partial class DesktopLayer
{
    private readonly TabDragView _dragView = new();
    private TabDragGesture? _gesture;

    /// <summary>The dragged tab's hint. Internal for tests.</summary>
    internal TabDragView DragView => _dragView;

    /// <summary>Whether a native tab is being dragged. Internal for tests.</summary>
    internal bool TabDragging => _gesture is not null;

    private NativeTabStrip NewStrip()
    {
        var strip = new NativeTabStrip();
        strip.DragStarted += (s, tab, e) =>
        {
            if (StartTabDrag(s, tab, e.GetPosition(this)))
            {
                e.Pointer.Capture(this);
            }
        };
        return strip;
    }

    /// <summary>Starts dragging <paramref name="tab"/> off <paramref name="strip"/> with the pointer at <paramref name="at"/> (layer pixels). Internal for tests.</summary>
    internal bool StartTabDrag(NativeTabStrip strip, TabChipModel tab, Point at)
    {
        if (_gesture is not null || strip.Overlay is not { Drag: { } send } overlay)
        {
            return false;
        }

        var row = new Rect(GetLeft(strip), GetTop(strip), strip.Width, strip.Height);
        _gesture = new TabDragGesture(overlay.Model.PaneId, tab.WindowId, tab.Name, strip.TabAreas, row, send) { Pointer = at };
        Step(TabDragPhase.Begin, at);
        return true;
    }

    /// <summary>Moves the drag to <paramref name="at"/> (layer pixels). Internal for tests.</summary>
    internal void MoveTabDrag(Point at)
    {
        if (_gesture is not null)
        {
            Step(TabDragPhase.Move, at);
        }
    }

    /// <summary>Ends the drag at <paramref name="at"/>: a drop, or abandoned. Internal for tests.</summary>
    internal void EndTabDrag(Point at, bool drop)
    {
        if (_gesture is null)
        {
            return;
        }

        Step(drop ? TabDragPhase.Drop : TabDragPhase.Cancel, at);
        _gesture = null;
        SyncTabDrag(_view.CellExtent);
    }

    private void Step(TabDragPhase phase, Point at)
    {
        var g = _gesture!;
        g.Pointer = at;
        var origin = CellOrigin;
        var cell = _view.CellExtent;
        var x = cell.Width > 0 ? (int)Math.Floor((at.X - origin.X) / cell.Width) : 0;
        var y = cell.Height > 0 ? (int)Math.Floor((at.Y - origin.Y) / cell.Height) : 0;
        int? slot = g.Row.Contains(at) ? NativeTabStrip.Slot(g.Areas, g.WindowId, at.X - g.Row.X) : null;
        g.Send(new TabDragStep(phase, g.PaneId, g.WindowId, x, y, slot));
        SyncTabDrag(cell);
    }

    /// <summary>The hint over the cells: what the client last said the drop would do, and the tab under the pointer.</summary>
    private void SyncTabDrag(Size cell)
    {
        SetLeft(_dragView, 0);
        SetTop(_dragView, 0);
        _dragView.Width = Bounds.Width;
        _dragView.Height = Bounds.Height;
        var tokens = _paneTokens?.Tokens ?? NativeTokens.Dark;
        if (_gesture is not { } g)
        {
            _dragView.Show(null, tokens);
            return;
        }

        var drag = _pointer.PaneChrome?.Drag is { } said && said.WindowId == g.WindowId ? said : null;
        _dragView.Show((drag, CellOrigin, cell, g.Name, g.Pointer), tokens);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_gesture is not null)
        {
            MoveTabDrag(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_gesture is not null)
        {
            // A second button while dragging is the conventional "never mind", as in the cells.
            EndTabDrag(e.GetPosition(this), drop: false);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_gesture is not null)
        {
            EndTabDrag(e.GetPosition(this), drop: e.InitialPressMouseButton == MouseButton.Left);
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_gesture is { } g)
        {
            EndTabDrag(g.Pointer, drop: false);
        }
    }

    /// <summary>A drag in flight: the tab, the strip it left as laid out then, and where the pointer is.</summary>
    private sealed record TabDragGesture(
        string PaneId,
        string WindowId,
        string Name,
        IReadOnlyList<(string WindowId, Rect Area)> Areas,
        Rect Row,
        Action<TabDragStep> Send)
    {
        public Point Pointer { get; set; }
    }
}
