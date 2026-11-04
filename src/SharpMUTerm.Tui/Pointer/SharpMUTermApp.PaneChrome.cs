using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// The panes' own chrome as data, for a host that draws it natively over the cells: each pane's tab strip (only where
/// the host says it draws them, <see cref="HostChrome.Tabs"/>) and the band of each pane split by scrolling back.
/// <para>
/// <b>A strip keeps its row.</b> The header and the rail are taken out of the grid, but a pane's strip is one row of
/// one pane, and a uniform cell grid cannot be inset per pane. So the client lays the panes out exactly as in a
/// terminal — the pane rectangles, and therefore NAWS, are the terminal's — paints each strip's row blank after the
/// frame (<see cref="BlankNativeTabStrips"/>, the way the header dividers and the overflow chip are painted), and the
/// host draws its strip over exactly that row. A press the host lets through reaches only the client's own pointer
/// path (the divider, a window drag), never the framework's strip, whose labels are under the blank cells
/// (<see cref="TakesNativeStripPress"/>).
/// </para>
/// <para>
/// <b>Every gesture ends in the cell click's method</b>: a tab in <see cref="ActivateStripTab"/> (the cell strip's
/// <c>TabChanged</c>), its close in <see cref="CloseStripTab"/> (the <c>×</c>), a right click in
/// <see cref="OpenTabMenu"/> (the cell tab's menu), <c>+N ▾</c> in <see cref="OpenTabOverflow"/>, and the band's
/// button in <see cref="ReturnToLiveTail"/> (End). Each is refused unless the overlay last published offered it.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private PaneChromeOverlay? _paneChrome;

    private Action<string, string>? _stripActivate;
    private Action<string, string>? _stripClose;
    private Action<string, string>? _stripMenu;
    private Action<string>? _stripOverflow;
    private Action<string>? _bandJump;

    /// <summary>The strip rows the host draws over, in desktop cells, as the last frame painted them; read on the input thread.</summary>
    private volatile PaneRect[] _nativeStripRows = [];

    /// <summary>What the host was last told about the panes' strips and bands. Internal for tests.</summary>
    internal PaneChromeOverlay? PaneChromePublished => _paneChrome;

    /// <summary>
    /// Tells the host about the strips and bands on screen, when any changed. Rides the frame, after every strip has been
    /// re-titled and every band re-counted, and the modal stack, for <see cref="PublishPanels"/>' reason.
    /// </summary>
    private void PublishPaneChrome()
    {
        if (_pointer is null || _window is null)
        {
            return;
        }

        var overlay = BuildPaneChrome();
        if (overlay.Equals(_paneChrome))
        {
            return;
        }

        _paneChrome = overlay;
        _pointer.PaneChrome(overlay);
    }

    /// <summary>The strips and bands as they stand. Internal for tests.</summary>
    internal PaneChromeOverlay BuildPaneChrome()
    {
        _stripActivate ??= (pane, window) => OnUiThread(() => ActivateOfferedStripTab(pane, window));
        _stripClose ??= (pane, window) => OnUiThread(() => CloseOfferedStripTab(pane, window));
        _stripMenu ??= (pane, window) => OnUiThread(() => OpenTabMenu(pane, window));
        _stripOverflow ??= pane => OnUiThread(() => OpenOfferedTabOverflow(pane));
        _bandJump ??= window => OnUiThread(() => ReturnFromOfferedBand(window));

        // A modal over the main window is drawn in cells over the panes; a native strip would sit on top of it.
        var covered = _system.ModalStateService.HasModals || _nativeCompose is not null;
        var strips = new List<TabStripOverlay>();
        var bands = new List<ScrollBandOverlay>();
        if (!covered)
        {
            foreach (var (paneId, tabs, _) in RealisedPanes())
            {
                if (Host.Chrome.Tabs && tabs.ShowTabHeader && BoundsOf(tabs) is { Width: > 0 } box
                    && DrawnModel(SurfaceCatalog.TabStrip, paneId) is TabStripModel model)
                {
                    var surface = PaneSurfaceTone(paneId);
                    var chips = model.Tabs
                        .Select(t => t.Selected || _workspace.FindWindow(t.WindowId) is not { } w ? null : ChipFor(paneId, w).Plane)
                        .ToList();
                    strips.Add(new TabStripOverlay(
                        new PaneRect(box.X, box.Y, box.Width, Math.Max(1, tabs.TabHeaderHeight)),
                        model,
                        surface.ToHex(),
                        WorkspacePalette.Recessed(surface).ToHex(),
                        chips,
                        _tabOverflow.TryGetValue(paneId, out var overflow) ? overflow.Menu : null,
                        _stripActivate,
                        _stripClose,
                        _stripMenu,
                        _stripOverflow));
                }

                if (_workspace.Layout.FindPane(paneId)?.ActiveTab is { } windowId && _scrollSplits.ContainsKey(windowId)
                    && _scrollSplitBars.TryGetValue(windowId, out var bar) && BoundsOf(bar) is { Width: > 0 } row
                    && DrawnModel(SurfaceCatalog.ScrollBand, windowId) is ScrollBandModel band)
                {
                    // Across the pane's gutter too, edge to edge as the mockup draws it, its words starting where the output's do.
                    var gutter = Host.Chrome.PaneGutter ? PaneGutterColumns : 0;
                    bands.Add(new ScrollBandOverlay(
                        new PaneRect(row.X - gutter, row.Y, row.Width + (2 * gutter), ScrollBandRows),
                        band,
                        ScrollSplitBarRenderer.Count(band.RowsBack),
                        _bandJump,
                        gutter));
                }
            }
        }

        _nativeStripRows = [.. strips.Select(s => s.Row)];
        return new PaneChromeOverlay(strips, bands, _theme, MainWindowInFront());
    }

    /// <summary>
    /// Paints the row of every strip the host draws blank, after the frame, in the strip's own recessed plane: the
    /// framework's labels, separators and <c>×</c> go, the row stays, and the host's strip lies over it. Only where the
    /// host draws the strips.
    /// </summary>
    private void BlankNativeTabStrips(SharpConsoleUI.Layout.CharacterBuffer buffer)
    {
        if (!Host.Chrome.Tabs)
        {
            return;
        }

        var top = _window.Top + _system.DesktopUpperLeft.Y;
        foreach (var (paneId, tabs, _) in RealisedPanes())
        {
            if (!tabs.ShowTabHeader || BoundsOf(tabs) is not { Width: > 0 } strip)
            {
                continue;
            }

            var plane = ToColor(WorkspacePalette.Recessed(PaneSurfaceTone(paneId)));
            for (var y = strip.Y; y < strip.Y + Math.Max(1, tabs.TabHeaderHeight); y++)
            {
                var by = y - top;
                for (var x = strip.X; x < strip.X + strip.Width; x++)
                {
                    var bx = x - _window.Left;
                    if (bx >= 0 && by >= 0 && bx < buffer.Width && by < buffer.Height)
                    {
                        buffer.SetCell(bx, by, new SharpConsoleUI.Layout.Cell(' ', plane, plane));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Strikes the button flags of a frame on a strip row the host draws over, leaving the framework a movement. Called
    /// after this client's own handlers have read the frame whole. Internal for tests.
    /// </summary>
    internal bool TakesNativeStripPress(List<MouseFlags> flags, System.Drawing.Point point)
    {
        var rows = _nativeStripRows;
        if (rows.Length == 0 || !rows.Any(r => Contains(r, point)))
        {
            return false;
        }

        var removed = flags.RemoveAll(f => f is MouseFlags.Button1Pressed or MouseFlags.Button1Released
            or MouseFlags.Button1Clicked or MouseFlags.Button1DoubleClicked or MouseFlags.Button1TripleClicked
            or MouseFlags.Button1Dragged);
        if (removed > 0 && flags.Count == 0)
        {
            flags.Add(MouseFlags.ReportMousePosition);
        }

        return removed > 0;
    }

    /// <summary>
    /// A tab clicked, in either host: its pane takes the focus and its window comes forward, through the one activation
    /// path. The cell strip's <c>TabChanged</c> ends here too.
    /// </summary>
    internal void ActivateStripTab(string paneId, string windowId)
    {
        _workspace.Layout.Focus(paneId);
        Activate(windowId);
    }

    /// <summary>A tab's close, in either host: the cell <c>×</c> and the native one.</summary>
    internal void CloseStripTab(string windowId) => CloseWindow(windowId);

    /// <summary>
    /// A right click on a tab: the tab comes forward and the host shows the tab's menu (<see cref="AddTabMenu"/>), the
    /// menu a right click on a cell tab shows. Internal for tests; returns whether a menu was shown.
    /// </summary>
    internal bool OpenTabMenu(string paneId, string windowId)
    {
        if (_pointer is null || !Offered(paneId, windowId, out var strip) || !MainWindowInFront())
        {
            return false;
        }

        var items = new List<PointerMenuItem>();
        AddTabMenu(items, windowId);
        var tidy = Tidy(items);
        if (tidy.Count == 0)
        {
            return false;
        }

        _pointer.ShowMenu(new System.Drawing.Point(strip!.Row.X, strip.Row.Y), tidy);
        return true;
    }

    /// <summary>A native tab click, checked against what the strip offered. Internal for tests.</summary>
    internal bool ActivateOfferedStripTab(string paneId, string windowId)
    {
        if (!Offered(paneId, windowId, out _))
        {
            RefuseCommand($"the tab strip never offered {Snippet(windowId)}");
            return false;
        }

        if (!MainWindowInFront())
        {
            return false;
        }

        ActivateStripTab(paneId, windowId);
        return true;
    }

    /// <summary>A native tab's close, checked: only a tab the strip offered a close on. Internal for tests.</summary>
    internal bool CloseOfferedStripTab(string paneId, string windowId)
    {
        if (!Offered(paneId, windowId, out var strip) || strip!.Model.Find(windowId) is not { Closable: true })
        {
            RefuseCommand($"the tab strip never offered to close {Snippet(windowId)}");
            return false;
        }

        if (!MainWindowInFront())
        {
            return false;
        }

        CloseStripTab(windowId);
        return true;
    }

    /// <summary>A native <c>+N ▾</c>: the overflow list the cells recorded for that strip. Internal for tests.</summary>
    internal bool OpenOfferedTabOverflow(string paneId)
    {
        var offered = _paneChrome?.Strips.FirstOrDefault(s => s.Model.PaneId == paneId);
        if (offered?.Overflow is null)
        {
            RefuseCommand($"the tab strip never offered a list for {Snippet(paneId)}");
            return false;
        }

        return MainWindowInFront() && OpenTabOverflow(paneId, new System.Drawing.Point(offered.Row.X + offered.Row.Width - 1, offered.Row.Y));
    }

    /// <summary>A native band's <c>Jump to latest</c>, checked against the bands last published. Internal for tests.</summary>
    internal bool ReturnFromOfferedBand(string windowId)
    {
        if (_paneChrome?.Bands.Any(b => b.Model.WindowId == windowId) != true)
        {
            RefuseCommand($"no band offered to return {Snippet(windowId)}");
            return false;
        }

        return MainWindowInFront() && ReturnToLiveTail(windowId);
    }

    /// <summary>
    /// The first column of a pane's strip, relative to the pane, at which the framework's own hit test finds the tab
    /// showing <paramref name="windowId"/>, or -1. Internal for tests: where a cell click on that tab lands.
    /// </summary>
    internal int CellTabColumn(string paneId, string windowId)
    {
        SharpConsoleUI.Controls.TabControl? tabs;
        lock (_paneTabsLock)
        {
            if (!_paneTabs.TryGetValue(paneId, out tabs))
            {
                return -1;
            }
        }

        var index = _workspace.Layout.FindPane(paneId)?.Tabs.ToList().IndexOf(windowId) ?? -1;
        var width = BoundsOf(tabs)?.Width ?? 0;
        for (var x = 0; index >= 0 && x < width; x++)
        {
            if (FrameworkHitTests.TabAt(tabs, x) == index)
            {
                return x + 1;
            }
        }

        return -1;
    }

    private bool Offered(string paneId, string windowId, out TabStripOverlay? strip)
    {
        strip = (_paneChrome ?? BuildPaneChrome()).Strips.FirstOrDefault(s => s.Model.PaneId == paneId);
        return strip?.Model.Find(windowId) is not null;
    }
}

/// <summary>
/// The panes' own chrome, for a host that draws it natively over the cells: the tab strips (only where
/// <see cref="HostChrome.Tabs"/>) and the scrolled-back bands.
/// </summary>
/// <param name="Strips">One per pane that shows a strip, in pane order.</param>
/// <param name="Bands">One per pane whose front window is split by scrolling back.</param>
/// <param name="Theme">The client's theme, from which the host derives its inks.</param>
/// <param name="Interactive">Whether the main window is in front; under a modal the chrome takes no click.</param>
internal sealed record PaneChromeOverlay(
    IReadOnlyList<TabStripOverlay> Strips,
    IReadOnlyList<ScrollBandOverlay> Bands,
    Theme Theme,
    bool Interactive)
{
    public bool Equals(PaneChromeOverlay? other) =>
        other is not null && Strips.SequenceEqual(other.Strips) && Bands.SequenceEqual(other.Bands)
        && ReferenceEquals(Theme, other.Theme) && Interactive == other.Interactive;

    public override int GetHashCode() => HashCode.Combine(Strips.Count, Bands.Count, Interactive);
}

/// <summary>
/// One pane's tab strip, for a host that draws it over the strip's row.
/// </summary>
/// <param name="Row">The strip's row, in desktop cells; the client paints it blank.</param>
/// <param name="Model">The strip as the cells wrote it.</param>
/// <param name="Plane">The pane's own plane (its character's tint, the focus lift): the selected tab is drawn on it.</param>
/// <param name="StripPlane">The strip's plane, one step recessed from the pane's: what the cells' idle chips wear.</param>
/// <param name="Chips">Per tab, an unselected tab's own plane when it belongs to another character; else null.</param>
/// <param name="Overflow">The tabs the strip cannot show (<see cref="SurfaceCatalog.TabOverflow"/>), or null when all fit.</param>
/// <param name="Activate">Brings a tab forward by pane and window id; any thread.</param>
/// <param name="Close">Closes a tab the strip offered a close on, by pane and window id; any thread.</param>
/// <param name="Menu">Opens a tab's menu, by pane and window id; any thread.</param>
/// <param name="OpenOverflow">Opens the overflow list, by pane id; any thread.</param>
internal sealed record TabStripOverlay(
    PaneRect Row,
    TabStripModel Model,
    string Plane,
    string StripPlane,
    IReadOnlyList<string?> Chips,
    MenuModel? Overflow,
    Action<string, string> Activate,
    Action<string, string> Close,
    Action<string, string> Menu,
    Action<string> OpenOverflow)
{
    public bool Equals(TabStripOverlay? other) =>
        other is not null && Row == other.Row && Model.Equals(other.Model) && Plane == other.Plane
        && StripPlane == other.StripPlane && Chips.SequenceEqual(other.Chips) && Equals(Overflow, other.Overflow)
        && ReferenceEquals(Activate, other.Activate);

    public override int GetHashCode() => HashCode.Combine(Row, Model, Plane);
}

/// <summary>A pane's scrolled-back band, for a host that draws it over the band's row.</summary>
/// <param name="Row">The band's row, in desktop cells; its cells stay drawn under the host's band.</param>
/// <param name="Model">The band as the cells drew it.</param>
/// <param name="Count">The count as the cells show it, capped.</param>
/// <param name="Jump">Takes the window back to its live tail, by window id; any thread.</param>
/// <param name="Inset">Columns from the band's left edge to where the pane's output starts (its gutter): the host lines
/// the band's words up with the output's, and keeps the button as far from the right edge.</param>
internal sealed record ScrollBandOverlay(PaneRect Row, ScrollBandModel Model, string Count, Action<string> Jump, int Inset = 0);
