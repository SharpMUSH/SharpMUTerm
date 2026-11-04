using SharpConsoleUI;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Surfaces;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Builders;

namespace SharpMUTerm.Tui;

/// <summary>
/// Split scrollback (F7 ▸ SCROLLBACK ▸ <c>split the pane when scrolled back</c>, on by default): a pane scrolled
/// back off its live tail splits into the history being read above, a band saying how far back it is, and the
/// newest rows still arriving below — what Mudlet and TinTin++ readers expect, where before the new lines
/// landed out of sight under the viewport.
/// <para>
/// <b>It is freeze's layout with a different trigger, not a third way of drawing a pane.</b> The same grid of
/// two scroll viewports and a one-row bar (<see cref="BuildFrozenContent"/>), the same <c>ScrollViewFor</c>
/// viewports, chosen by the same branch of <see cref="BuildTabContent"/>. What differs is which half is the
/// window's real control: freeze moves the live control to the bottom and pins a copy of the history above;
/// here the window's own control and its own viewport stay on top — so the reader's scroll position, the
/// scrollback keys, the wheel, ⌃F's landing and the status row's segment all keep working on the viewport
/// they always addressed — and the bottom is a <em>view</em> of the buffer's last <see cref="TailLines"/>
/// lines, re-fed from it. It indexes nothing, so no chrome row, trim or picture has a split point to move.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Windows currently split, and how many rows their live tail was given when the split opened.</summary>
    private readonly Dictionary<string, int> _scrollSplits = new(StringComparer.Ordinal);

    /// <summary>Each split window's live-tail control: a copy of the buffer's newest lines.</summary>
    private readonly Dictionary<string, MarkupControl> _scrollTails = new(StringComparer.Ordinal);

    /// <summary>Each split window's band.</summary>
    private readonly Dictionary<string, MarkupControl> _scrollSplitBars = new(StringComparer.Ordinal);

    /// <summary>What each band last said, so an unchanged count costs no repaint.</summary>
    private readonly Dictionary<string, string> _scrollSplitBarText = new(StringComparer.Ordinal);

    /// <summary>The share of a pane the live tail takes when a split opens.</summary>
    internal const double TailShare = 0.3;

    /// <summary>The fewest rows a live tail is given, and the fewest the history above it keeps.</summary>
    internal const int MinimumSplitRows = 3;

    /// <summary>
    /// How many of the newest lines the live tail holds. Far more than it shows — a line wraps into several
    /// rows in a narrow pane — and few enough that re-feeding it per line costs nothing worth measuring.
    /// </summary>
    internal const int TailLines = 64;

    /// <summary>
    /// The rows a live tail gets in a pane whose viewport is <paramref name="paneRows"/> tall, or zero when the
    /// pane is too short to split: 30% of it, at least <see cref="MinimumSplitRows"/>, leaving the history and
    /// the band at least that many and one.
    /// </summary>
    internal static int TailRowsFor(int paneRows)
    {
        var tail = Math.Max(MinimumSplitRows, (int)Math.Round(paneRows * TailShare));
        return paneRows - 1 - tail >= MinimumSplitRows ? tail : 0;
    }

    /// <summary>Whether the workspace counts a window as scrolled back, which is what unread badging reads.</summary>
    internal bool IsWindowScrolledBack(string windowId) => _workspace.FindWindow(windowId)?.ScrolledBack ?? false;

    /// <summary>Whether a window is drawn split by scrollback right now.</summary>
    internal bool IsScrollSplit(string windowId) => _scrollSplits.ContainsKey(windowId);

    /// <summary>
    /// Opens or closes a window's split to match its viewport: open while the viewport is off its live tail,
    /// closed once it is back (the reader scrolled the history down to its end, ⌃End, or End), when the pane is
    /// frozen (freeze is the other split, and takes over), and when the setting is off. Called from
    /// <see cref="SyncScrollbackState"/>, where every scroll route lands.
    /// </summary>
    private void SyncScrollSplit(string windowId)
    {
        var open = _scrollSplits.ContainsKey(windowId);
        var pane = _workspace.Layout.FindWindow(windowId);
        var live = _paneScrolls.GetValueOrDefault(windowId);
        var eligible = _config.Text.SplitScrollback
            && pane is { Frozen: false }
            && string.Equals(pane.ActiveTab, windowId, StringComparison.Ordinal)
            && _panes.ContainsKey(windowId)
            && !string.Equals(windowId, WebWindowId, StringComparison.Ordinal)
            && live is not null;
        var wanted = eligible && !live!.AutoScroll && Scrollable(live);

        if (open && !wanted)
        {
            CloseScrollSplit(windowId);
            RebuildPaneArea();
            return;
        }

        if (!open && wanted)
        {
            var tail = TailRowsFor(live!.ViewportHeight);
            if (tail == 0)
            {
                return;
            }

            _scrollSplits[windowId] = tail;
            RefreshScrollTail(windowId);
            RebuildPaneArea();
        }

        if (_scrollSplits.ContainsKey(windowId))
        {
            RefreshScrollSplitBar(windowId);
        }
    }

    /// <summary>Forgets a window's split without rebuilding; the caller rebuilds (or is about to).</summary>
    private void CloseScrollSplit(string windowId)
    {
        _scrollSplits.Remove(windowId);
        if (_scrollTails.TryGetValue(windowId, out var tail))
        {
            tail.ClearSelection();
            tail.SetContent(new List<string>());
        }
    }

    /// <summary>Drops everything a closed window kept for its split.</summary>
    private void ForgetScrollSplit(string windowId)
    {
        _scrollSplits.Remove(windowId);
        _scrollTails.Remove(windowId);
        _scrollSplitBars.Remove(windowId);
        _scrollSplitBarText.Remove(windowId);
        _paneScrolls.Remove(TailRegionKey(windowId));
    }

    /// <summary>The <see cref="_paneScrolls"/> key of a split window's live-tail half.</summary>
    private static string TailRegionKey(string windowId) => $"tail:{windowId}";

    /// <summary>
    /// A split window's content: the window's own control in its own viewport, the band, and the live tail.
    /// The tail's height is set when the split opens and kept for its life; history takes the rest.
    /// </summary>
    private IWindowControl BuildScrollSplitContent(string windowId, string title)
    {
        var live = PaneContentFor(windowId, title);
        var tail = ScrollTailFor(windowId);
        RefreshScrollTail(windowId);
        var bar = ScrollSplitBarFor(windowId);
        RefreshScrollSplitBar(windowId);

        var grid = Controls.Grid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill);
        grid.Rows(GridLength.Star(1), GridLength.Cells(1), GridLength.Cells(_scrollSplits[windowId]))
            .Columns(GridLength.Star(1));
        grid.Place(ScrollViewFor(windowId, live), 0, 0, 1, 1);
        grid.Place(bar, 1, 0, 1, 1);
        grid.Place(ScrollViewFor(TailRegionKey(windowId), tail), 2, 0, 1, 1);
        return grid.Build();
    }

    private MarkupControl ScrollTailFor(string windowId)
    {
        if (_scrollTails.TryGetValue(windowId, out var existing))
        {
            return existing;
        }

        var control = new MarkupControl(new List<string>());
        control.LinkClicked += (_, e) => OnLinkClicked(windowId, e.Url);
        EnableSelection(control);
        _scrollTails[windowId] = control;
        return control;
    }

    private MarkupControl ScrollSplitBarFor(string windowId)
    {
        if (_scrollSplitBars.TryGetValue(windowId, out var existing))
        {
            return existing;
        }

        var bar = new MarkupControl(new List<string>()) { Wrap = false };
        _scrollSplitBars[windowId] = bar;
        return bar;
    }

    /// <summary>
    /// Re-feeds a split window's live tail from the buffer's newest lines. Called after every change to the
    /// buffer the window's own control is told about — an appended line, a re-feed, a picture — so the two
    /// halves never disagree about the newest line.
    /// </summary>
    private void RefreshScrollTail(string windowId)
    {
        if (!_scrollSplits.ContainsKey(windowId) || !_lines.TryGetValue(windowId, out var buffer))
        {
            return;
        }

        var tail = ScrollTailFor(windowId);
        var from = Math.Max(0, buffer.Count - TailLines);
        FeedRange(tail, buffer, from, buffer.Count - from);
        if (_paneScrolls.GetValueOrDefault(TailRegionKey(windowId)) is { } panel)
        {
            panel.AutoScroll = true;
        }
    }

    /// <summary>
    /// Re-writes a split window's band from its history viewport: the rows below what the reader is looking
    /// at, the same figure the status row's scrollback segment reports, so the two never disagree.
    /// </summary>
    private bool RefreshScrollSplitBar(string windowId)
    {
        if (!_scrollSplits.ContainsKey(windowId) || _paneScrolls.GetValueOrDefault(windowId) is not { } panel)
        {
            return false;
        }

        var bar = ScrollSplitBarFor(windowId);
        var width = panel.ViewportWidth > 0 ? panel.ViewportWidth : 80;
        var below = panel.TotalContentHeight - panel.ViewportHeight - panel.VerticalScrollOffset;
        var band = Drawing(SurfaceCatalog.ScrollBand, windowId, new ScrollBandModel(windowId, below));
        var markup = ScrollSplitBarRenderer.Bar(band, width, _ink.Notice, SharpMUTerm.Tui.Panels.HeadingBand.For(_theme, _ink.Notice));
        if (!string.Equals(_scrollSplitBarText.GetValueOrDefault(windowId), markup, StringComparison.Ordinal))
        {
            _scrollSplitBarText[windowId] = markup;
            bar.SetContent(new List<string> { markup });
            return true;
        }

        return false;
    }

    /// <summary>
    /// Keeps every band's count current; rides <c>PostBufferPaint</c>, where viewport metrics are fresh. A count
    /// that moved is a count the status row's scrollback segment also reports, so the row is repainted with it
    /// and the two never disagree. It converges: the next frame finds both unchanged.
    /// </summary>
    private void SyncScrollSplitBars()
    {
        var changed = false;
        foreach (var windowId in _scrollSplits.Keys)
        {
            changed |= RefreshScrollSplitBar(windowId);
        }

        if (changed)
        {
            RefreshStatusRow();
        }
    }

    /// <summary>
    /// Bare End returns a split pane to its live tail, but only when the caret is already at the end of the
    /// command line: there End has nothing left to do in the editor, and that is where a caret rests after
    /// typing. Anywhere else End is the editor's, and a second End returns.
    /// </summary>
    private bool TryEndReturnsFromSplit(KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key != ConsoleKey.End || e.KeyInfo.Modifiers != 0)
        {
            return false;
        }

        var windowId = _workspace.Layout.FocusedPane.ActiveTab ?? MainWindowId;
        if (!_scrollSplits.ContainsKey(windowId) || !ActiveBar().CaretAtEnd()
            || _paneScrolls.GetValueOrDefault(windowId) is not { } panel)
        {
            return false;
        }

        e.Handled = true;
        ReturnToLiveTail(windowId);
        return true;
    }

    /// <summary>
    /// Takes a split window back to its live tail: End's work, and what the window's native band's <c>Jump to latest</c>
    /// ends in (<see cref="SurfaceCatalog.ScrollBand"/>). Internal so a test can drive it as the window does; returns
    /// whether the window was split and is now following its tail.
    /// </summary>
    internal bool ReturnToLiveTail(string windowId)
    {
        if (!_scrollSplits.ContainsKey(windowId) || _paneScrolls.GetValueOrDefault(windowId) is not { } panel)
        {
            return false;
        }

        BackToLive(panel);
        SyncScrollbackState();
        return true;
    }
}
