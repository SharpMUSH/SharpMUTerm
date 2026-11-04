using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui.Panels;

namespace SharpMUTerm.Tui;

/// <summary>
/// A collapsed panel drawn as a bar at the foot of its column (<see cref="HostChrome.Strips"/>, the window): the layout's
/// realiser gives such a panel a pane of its own (<see cref="LayoutRealiser.CollapsedPanePrefix"/>), and while it is not
/// expanded that pane is <see cref="CollapsedBarRows"/> rows of one <see cref="CollapsedPanelModel"/>, drawn by
/// <see cref="CollapsedBarRenderer"/> in cells and by the window's native bar over them. A click on either is
/// <see cref="ExpandCollapsedBar"/>: the pane shares its column like any other from then on, for the run, until a layout is
/// applied again. Activating a window the bar holds expands it too, so ⌥N never lands in a bar. A window in a bar is
/// marked scrolled back, so its lines are counted as unread, which is what the bar's <c>2 new</c> reads.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>
    /// The rows a collapsed panel's bar takes: two, the mockups' ~40 px footer — or one where the host draws the command
    /// line's field (<see cref="HostChrome.Input"/>), whose grid is shorter than its view by the field's padding and whose
    /// bar at the foot of a column runs down over that too.
    /// </summary>
    internal int CollapsedBarRows => Host.Chrome.Input ? 1 : 2;

    /// <summary>Collapsed panes the player has expanded this run, by pane id.</summary>
    private readonly HashSet<string> _expandedBars = new(StringComparer.Ordinal);

    /// <summary>Each collapsed bar's control, by pane id, with what it last drew.</summary>
    private readonly Dictionary<string, (MarkupControl Control, CollapsedPanelModel? Model, int Width)> _collapsedBarViews =
        new(StringComparer.Ordinal);

    /// <summary>The expand callback per pane, one instance each so a published overlay compares equal frame to frame.</summary>
    private readonly Dictionary<string, Action> _barExpanders = new(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="pane"/> is drawn as a collapsed panel's bar now.</summary>
    private bool IsCollapsedBar(PaneNode? pane) =>
        pane is not null
        && Host.Chrome.Strips
        && LayoutRealiser.CollapsedPanelOf(pane.Id) is not null
        && !_expandedBars.Contains(pane.Id)
        && pane.Tabs.Count > 0
        && _workspace.Layout.ZoomedPaneId is null;

    /// <summary>The panes as the tree holds them. Internal for tests.</summary>
    internal IReadOnlyList<PaneNode> PaneNodes => _workspace.Layout.Root.Panes().ToList();

    /// <summary>Whether a pane id is a collapsed bar now. Internal for tests.</summary>
    internal bool IsCollapsedBarPane(string paneId) => IsCollapsedBar(_workspace.Layout.FindPane(paneId));

    /// <summary>The model the bar in <paramref name="paneId"/> last drew, for the tests and the parity harness.</summary>
    internal CollapsedPanelModel? CollapsedBarModel(string paneId) => DrawnModel(SurfaceCatalog.CollapsedPanel, paneId) as CollapsedPanelModel;

    /// <summary>A click on a link in the bar's cells, as the framework raises it. The test seam.</summary>
    internal void SimulateCollapsedBarClick(string paneId, string url)
    {
        if (_collapsedBarViews.TryGetValue(paneId, out var view) && url == CollapsedBarRenderer.ExpandLink)
        {
            ExpandCollapsedBar(paneId);
        }
    }

    /// <summary>
    /// After <see cref="PanelSizing.ApplyAll"/>: every collapsed bar is <see cref="CollapsedBarRows"/> rows with no tab
    /// strip, and the windows in it count their lines as unread.
    /// </summary>
    private void ApplyCollapsedBars()
    {
        foreach (var pane in _workspace.Layout.Root.Panes())
        {
            if (!IsCollapsedBar(pane))
            {
                continue;
            }

            pane.FixedRows = CollapsedBarRows;
            pane.MinRows = null;
            pane.DrawsHeader = false;
            pane.DrawsHeading = false;
            foreach (var windowId in pane.Tabs)
            {
                _workspace.SetScrolledBack(windowId, true);
            }
        }
    }

    /// <summary>What the bar in <paramref name="pane"/> says: its first window's title, the unread across its windows, why.</summary>
    private CollapsedPanelModel CollapsedModelFor(PaneNode pane)
    {
        var windows = pane.Tabs.Select(_workspace.FindWindow).OfType<WorkspaceWindow>().ToList();
        var title = windows.FirstOrDefault()?.Title ?? LayoutRealiser.CollapsedPanelOf(pane.Id) ?? string.Empty;
        var layout = _liveLayout is { } live ? Layouts.Find(live.LayoutId)?.Name : null;
        return new CollapsedPanelModel(Snippet(title), windows.Sum(w => w.Unread), CollapsedPanelModel.NoteFor(layout));
    }

    /// <summary>
    /// The bar's cells: a tab control holding one tab (so the pane rectangle, the rail and every pane walk see a pane like
    /// any other), its header hidden, its page the bar's row. The windows the pane holds keep their tabs in the model and
    /// are drawn when it is expanded.
    /// </summary>
    private IWindowControl BuildCollapsedBar(PaneNode pane)
    {
        var paneId = pane.Id;
        if (!_collapsedBarViews.TryGetValue(paneId, out var view))
        {
            var control = new MarkupControl(new List<string>())
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Fill,
            };
            control.LinkClicked += (_, e) =>
            {
                if (e.Url == CollapsedBarRenderer.ExpandLink)
                {
                    ExpandCollapsedBar(paneId);
                }
            };
            view = (control, null, -1);
        }

        _collapsedBarViews[paneId] = view with { Model = null, Width = -1 };
        var tabs = Controls.TabControl().AddTab(string.Empty, view.Control).Fill().WithAlignment(HorizontalAlignment.Stretch).Build();
        if (Host.Chrome.PaneGutter)
        {
            tabs.Margin = new Margin(PaneGutterColumns, 0, PaneGutterColumns, 0);
        }

        tabs.TabPages[0].Tag = pane.ActiveTab;
        tabs.TabPages[0].IsClosable = false;
        tabs.ShowTabHeader = false;
        lock (_paneTabsLock)
        {
            _paneTabs[paneId] = tabs;
        }

        RefreshCollapsedBar(paneId);
        return tabs;
    }

    /// <summary>Redraws one bar when what it says or its width moved.</summary>
    private void RefreshCollapsedBar(string paneId)
    {
        if (!_collapsedBarViews.TryGetValue(paneId, out var view) || _workspace.Layout.FindPane(paneId) is not { } pane || !IsCollapsedBar(pane))
        {
            return;
        }

        var width = PaneOutputRects().TryGetValue(paneId, out var rect) && rect.Width > 0 ? rect.Width : 40;
        var model = Drawing(SurfaceCatalog.CollapsedPanel, paneId, CollapsedModelFor(pane));
        if (model == view.Model && width == view.Width)
        {
            return;
        }

        view.Control.SetContent([CollapsedBarRenderer.Render(model, width, _ink.Accent)]);
        _collapsedBarViews[paneId] = (view.Control, model, width);
    }

    /// <summary>Every bar on screen, redrawn as its unread or width moves. Rides <c>PostBufferPaint</c>.</summary>
    private void SyncCollapsedBars()
    {
        foreach (var paneId in _collapsedBarViews.Keys.ToList())
        {
            if (_workspace.Layout.FindPane(paneId) is null)
            {
                _collapsedBarViews.Remove(paneId);
                continue;
            }

            RefreshCollapsedBar(paneId);
        }
    }

    /// <summary>What the host is told about the bar in <paramref name="paneId"/>, or null when it is no bar.</summary>
    private PanelOverlay? CollapsedBarOverlay(string paneId, PaneRect rect)
    {
        if (_workspace.Layout.FindPane(paneId) is not { } pane || !IsCollapsedBar(pane) || pane.ActiveTab is not { } windowId)
        {
            return null;
        }

        var expand = _barExpanders.TryGetValue(paneId, out var known)
            ? known
            : _barExpanders[paneId] = () => OnUiThread(() => ExpandCollapsedBar(paneId));
        var model = DrawnModel(SurfaceCatalog.CollapsedPanel, paneId) as CollapsedPanelModel ?? CollapsedModelFor(pane);
        return new PanelOverlay(
            LayoutRealiser.CollapsedPanelOf(paneId)!, windowId, rect, PanelListInk(), null, Collapsed: model, Expand: expand);
    }

    /// <summary>
    /// Expands the collapsed panel in <paramref name="paneId"/>: the pane shares its column from now on and its window comes
    /// forward. What the cell row's link and the native bar's click both end in.
    /// </summary>
    internal void ExpandCollapsedBar(string paneId)
    {
        if (_workspace.Layout.FindPane(paneId) is not { } pane || !IsCollapsedBar(pane))
        {
            return;
        }

        _expandedBars.Add(paneId);
        _collapsedBarViews.Remove(paneId);
        RebuildPaneArea();
        if (pane.ActiveTab is { } windowId)
        {
            Activate(windowId);
            _workspace.SetScrolledBack(windowId, false);
            RefreshTabTitles();
        }
    }

    /// <summary>Expands the bar holding <paramref name="windowId"/>, if one does: a window brought forward is never left in a bar.</summary>
    private void ExpandBarHolding(string windowId)
    {
        if (!Host.Chrome.Strips || _workspace.Layout.FindWindow(windowId) is not { } pane || !IsCollapsedBar(pane))
        {
            return;
        }

        _expandedBars.Add(pane.Id);
        _collapsedBarViews.Remove(pane.Id);
        foreach (var id in pane.Tabs)
        {
            _workspace.SetScrolledBack(id, false);
        }

        RebuildPaneArea();
    }
}
