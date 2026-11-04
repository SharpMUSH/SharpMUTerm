using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// A tab dragged off the window's native strip (<see cref="HostChrome.Tabs"/>). The window only reports the pointer —
/// the tab it started from and the desktop cell it is over, plus the slot among its own strip's tabs while it is over that
/// strip — and the client resolves and makes the drop, through the cell drag's own pieces:
/// <list type="bullet">
/// <item>over a pane, <see cref="DropZones"/> against the geometry frozen when the drag began (<see cref="PaneSnapshot"/>,
/// as <see cref="PaneDragTracker"/> freezes it), the cells' drop preview and <c>DRAG … → …</c> prompt while it moves, and
/// on release <see cref="DropWindowOnPane"/>, the method the cell drag's commit ends in: an edge splits, the centre moves
/// it in as a tab;</item>
/// <item>over its own strip, <see cref="ReorderStripTab"/>, which brings the tab forward and moves it with
/// <see cref="ReorderFocusedTab"/>, the method ⌃B &lt; and ⌃B &gt; end in.</item>
/// </list>
/// What the drop would do is published back (<see cref="PaneChromeOverlay.Drag"/>, a <see cref="TabDragModel"/>) and the
/// window draws its hint from that. A drag the strip never offered — a tab not on it, a window under a modal — is refused,
/// and a slot is used only while the cell is on that strip's row and the slot exists.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private Action<TabDragStep>? _stripDrag;

    /// <summary>The native tab drag in flight, or null.</summary>
    private NativeTabDrag? _tabDrag;

    /// <summary>The native tab drag as the client last resolved it, or null. Internal for tests.</summary>
    internal TabDragModel? TabDragNow => _tabDrag?.Model;

    /// <summary>
    /// A step of a native tab drag, checked: a drag begins only on a tab the strip offered, with the main window in front
    /// and nothing else in charge of the pointer; later steps act only on the drag that began. Internal for tests;
    /// returns whether the step did anything.
    /// </summary>
    internal bool DragStripTab(TabDragStep step)
    {
        switch (step.Phase)
        {
            case TabDragPhase.Begin:
                return BeginStripDrag(step);

            case TabDragPhase.Move:
                if (!IsThisDrag(step))
                {
                    return false;
                }

                ResolveStripDrag(step);
                return true;

            case TabDragPhase.Drop:
                if (!IsThisDrag(step))
                {
                    return false;
                }

                ResolveStripDrag(step);
                var drag = _tabDrag!;
                var done = drag.Model.ReorderIndex is { } index
                    ? ReorderStripTab(drag.Model.SourcePaneId, drag.Model.WindowId, index)
                    : drag.Model.TargetPaneId is { } pane && DropWindowOnPane(drag.Model.WindowId, pane, drag.Model.Edge);
                EndDrag();
                return done;

            default:
                if (!IsThisDrag(step))
                {
                    return false;
                }

                EndDrag();
                return true;
        }
    }

    private bool IsThisDrag(TabDragStep step) =>
        _tabDrag is { } drag
        && string.Equals(drag.Model.WindowId, step.WindowId, StringComparison.Ordinal)
        && string.Equals(drag.Model.SourcePaneId, step.PaneId, StringComparison.Ordinal);

    private bool BeginStripDrag(TabDragStep step)
    {
        if (!Offered(step.PaneId, step.WindowId, out var strip))
        {
            RefuseCommand($"the tab strip never offered to drag {Snippet(step.WindowId)}");
            return false;
        }

        if (!MainWindowInFront() || AnyOverlayOpen || _moveMode || _dragActive || _tabDrag is not null)
        {
            return false;
        }

        _tabDrag = new NativeTabDrag(
            PaneSnapshot(),
            strip!.Row,
            strip.Model.Tabs.Count,
            new TabDragModel(step.WindowId, step.PaneId, null, null, null, null, DropLabel(null, null)));
        ResolveStripDrag(step);
        return true;
    }

    /// <summary>
    /// Where the drag would land with the pointer at the step's cell: a slot on its own strip, else the pane and edge under
    /// it. Shows it as the cells show a drag — the drop preview over the panes and the prompt on the status row, or, for a
    /// slot, the real panes and a prompt naming the slot — and publishes it.
    /// </summary>
    private void ResolveStripDrag(TabDragStep step)
    {
        var drag = _tabDrag!;
        var onStrip = step.X >= drag.StripRow.X && step.X < drag.StripRow.X + drag.StripRow.Width
            && step.Y >= drag.StripRow.Y && step.Y < drag.StripRow.Y + drag.StripRow.Height;
        if (onStrip && step.ReorderIndex is { } slot && slot >= 0 && slot < drag.TabCount)
        {
            drag.Model = drag.Model with
            {
                TargetPaneId = drag.Model.SourcePaneId,
                Edge = null,
                ReorderIndex = slot,
                Zone = null,
                Label = SlotLabel(drag.Model.SourcePaneId, drag.Model.WindowId, slot),
            };

            if (_dragActive)
            {
                // Back on its own strip from over the panes: the strip comes back to show where the tab would go.
                _dragActive = false;
                _dragTargetPaneId = null;
                _dragEdge = null;
                RebuildPaneArea();
            }

            SetStatus(DragPromptRow(drag.Model.WindowId, drag.Model.Label), displace: true);
            PublishPaneChrome();
            return;
        }

        string? target = null;
        Edge? edge = null;
        PaneRect? zone = null;
        if (drag.Surface.PaneAt(step.X, step.Y) is { } paneId && drag.Surface.RectOf(paneId) is { } rect)
        {
            target = paneId;
            edge = DropZones.Resolve(rect.X, rect.Y, rect.Width, rect.Height, step.X, step.Y);
            zone = TabDragModel.ZoneOf(rect, edge);
        }

        var changed = !_dragActive || target != _dragTargetPaneId || edge != _dragEdge;
        drag.Model = drag.Model with { TargetPaneId = target, Edge = edge, ReorderIndex = null, Zone = zone, Label = DropLabel(target, edge) };
        if (changed)
        {
            // The cell drag's preview exactly: ApplyDragResult's Begin and Update.
            _dragActive = true;
            _dragTargetPaneId = target;
            _dragEdge = edge;
            RebuildPaneArea();
            SetStatus(DragPromptRow(drag.Model.WindowId, target, edge), displace: true);
        }

        PublishPaneChrome();
    }

    /// <summary>
    /// A window dropped on a pane, in either host: an edge splits the pane and puts the window in the new one, the centre
    /// moves it in as a tab, and it comes forward. The cell drag's commit and the native strip's drop both end here.
    /// </summary>
    internal bool DropWindowOnPane(string windowId, string paneId, Edge? edge)
    {
        if (!PaneDrop.Apply(_workspace.Layout, windowId, paneId, edge))
        {
            return false;
        }

        _workspace.ActivateWindow(windowId);
        return true;
    }

    /// <summary>
    /// Moves a tab to <paramref name="index"/> in its own strip: it comes forward (the tab click's path) and is moved one
    /// slot at a time by <see cref="ReorderFocusedTab"/>, so a drop is exactly the ⌃B &lt; / ⌃B &gt; presses it stands for.
    /// Internal for tests.
    /// </summary>
    internal bool ReorderStripTab(string paneId, string windowId, int index)
    {
        if (_workspace.Layout.FindPane(paneId) is not { } pane)
        {
            return false;
        }

        var from = pane.Tabs.IndexOf(windowId);
        if (from < 0 || index < 0 || index >= pane.Tabs.Count)
        {
            return false;
        }

        ActivateStripTab(paneId, windowId);
        return from != index && ReorderFocusedTab(index - from);
    }

    /// <summary>
    /// Moves the focused pane's front tab <paramref name="delta"/> slots along its strip (⌃B &lt; is -1, ⌃B &gt; is +1), then
    /// rebuilds the strip. Returns false, moving nothing, when it is already at that end or the pane holds one tab.
    /// </summary>
    internal bool ReorderFocusedTab(int delta)
    {
        var moved = false;
        for (var i = 0; i < Math.Abs(delta); i++)
        {
            if (!_workspace.Layout.ReorderActiveTab(Math.Sign(delta)))
            {
                break;
            }

            moved = true;
        }

        if (moved)
        {
            // The strip has to be rebuilt, not retitled. A reorder changes the *order* of the pane's tabs, and
            // `TabControl` has no way to move a page — `TabPages` is a copy and the only mutators are Add/Insert — so
            // `RefreshTabTitles`, which repaints each page by its own `Tag`, left the strip exactly as it was. That is
            // not a cosmetic lag: the model then holds an order the screen does not, and the *refusal* is read against
            // the model. Reordering the middle of three tabs looked like nothing happened, and the next press —
            // genuinely at the end, in a strip still showing it in the middle — said "the tab is already at that end of
            // the strip", which is how it was reported.
            RebuildPaneArea();
        }

        return moved;
    }

    /// <summary>
    /// The split tree, every pane's tabs in order with its front one marked, the focused pane and the window in front, as
    /// one string: what a drag can change. Internal for tests, which compare two clients by it.
    /// </summary>
    internal string WorkspaceDigest()
    {
        static string Node(LayoutNode node) => node switch
        {
            PaneNode pane => "[" + string.Join(",", pane.Tabs.Select((t, i) => i == pane.ActiveIndex ? "*" + t : t)) + "]",
            SplitNode split => (split.Direction == SplitDirection.Row ? "row(" : "col(") + string.Join(" ", split.Children.Select(Node)) + ")",
            _ => "?",
        };

        var focused = _workspace.Layout.FocusedPane;
        return $"{Node(_workspace.Layout.Root)} focus={string.Join(",", focused.Tabs)} window={ActiveWindowId()}";
    }

    /// <summary>What dropping a tab at <paramref name="slot"/> of its own strip does, in the prompt's words.</summary>
    private string SlotLabel(string paneId, string windowId, int slot)
    {
        var tabs = _workspace.Layout.FindPane(paneId)?.Tabs ?? [];
        var pane = PaneLabel(paneId);
        return tabs.IndexOf(windowId) == slot ? $"same place in {pane}" : $"place {slot + 1} of {tabs.Count} in {pane}";
    }

    /// <summary>The status line while a native tab is dragged along its own strip.</summary>
    private StatusModel DragPromptRow(string windowId, string where)
    {
        var name = _workspace.FindWindow(windowId) is { } window ? window.Title : "window";
        return StatusRowModel(
        [
            DropPrompt("DRAG", name, where),
            Segment(StatusRole.ModeHint, Dim("release to drop · Esc cancel")),
        ]);
    }

    /// <summary>A native tab drag: the geometry frozen when it began, the strip it began on, and where it would land.</summary>
    private sealed class NativeTabDrag(PaneDragSurface surface, PaneRect stripRow, int tabCount, TabDragModel model)
    {
        public PaneDragSurface Surface { get; } = surface;

        public PaneRect StripRow { get; } = stripRow;

        public int TabCount { get; } = tabCount;

        public TabDragModel Model { get; set; } = model;
    }
}
