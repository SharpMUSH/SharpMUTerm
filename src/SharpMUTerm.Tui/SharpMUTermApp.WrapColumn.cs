using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Tui;

/// <summary>
/// F5 ▸ <c>wrap at</c>: a world's output wraps at a fixed column instead of the pane's width, and that column is what
/// NAWS tells the server (<see cref="Core.Configuration.WorldDefinition.WrapColumn"/>).
/// <para>
/// <b>The column is a right margin on the window's content, not a width of its own.</b> The margin sits on what a tab
/// page holds — the scroll viewport, or a frozen or split pane's grid of two — so every viewport inside it is the column
/// wide, and everything that measures by a viewport's width (bars, the away and NEW reveals, picture rows, the trigger
/// builder) measures at the column with no change of its own. The pane's rectangle does not move, so nothing else in the
/// layout does either; the cells right of the column are the pane's plane.
/// </para>
/// <para>
/// <b>A pane narrower than the column wraps at the pane.</b> There is no sideways scrolling, and a line cut off at the
/// pane's edge would be worse than one wrapped twice. NAWS still reports the column: the setting says what the server
/// should format for, and the player chose it.
/// </para>
/// <para>
/// The margin is set when a pane is built and kept in step on <c>PostBufferPaint</c> (<see cref="SyncWrapColumns"/>),
/// where the pane rectangles are fresh. It converges: the margin depends on the pane's width, which it cannot change.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>
    /// The column <paramref name="session"/>'s output wraps at and NAWS reports, or null when it follows the pane.
    /// </summary>
    private static int? WrapColumnOf(WorldSession? session) =>
        session?.World.WrapColumn is > 0 and var column ? column : null;

    /// <summary>
    /// The column a window's output wraps at, or null: a session window or capture whose world sets one. Never a layout
    /// panel, a map or the web view, which draw themselves to their pane.
    /// </summary>
    private int? WrapColumnOfWindow(string windowId) =>
        windowId == WebWindowId || IsMapWindow(windowId) || PanelIdOfWindow(windowId) is not null
            ? null
            : WorldOfWindow(windowId)?.WrapColumn is > 0 and var column ? column : null;

    /// <summary>
    /// The world a window's output comes from: its session's, else the configured world of the character the workspace
    /// records as its owner, so a window keeps its column while its character is offline.
    /// </summary>
    private Core.Configuration.WorldDefinition? WorldOfWindow(string windowId) =>
        WindowSession(windowId)?.World
        ?? (_workspace.FindWindow(windowId)?.SessionKey is { Length: > 0 } key ? LayoutOwner(key).World : null);

    /// <summary>The right margin that makes a <paramref name="paneWidth"/>-wide pane wrap <paramref name="windowId"/> at its column.</summary>
    private int WrapMarginFor(string windowId, int paneWidth) =>
        WrapColumnOfWindow(windowId) is { } column && paneWidth > column ? paneWidth - column : 0;

    /// <summary>
    /// The width a session is told over NAWS for a pane <paramref name="paneWidth"/> wide: its world's column when it
    /// sets one, else the pane's.
    /// </summary>
    private static int ReportedWidth(WorldSession session, int paneWidth) =>
        WrapColumnOf(session) ?? paneWidth;

    /// <summary>
    /// Sets a tab's content margin: the gutter's top row where the host keeps one, and the wrap column's right margin.
    /// Touches only a window that has either, so a panel's own margins are left alone.
    /// </summary>
    private void ApplyContentMargin(IWindowControl content, string windowId, int? paneWidth)
    {
        var wraps = WrapColumnOfWindow(windowId) is not null;
        if (!Host.Chrome.PaneGutter && !wraps && content.Margin.Right == 0)
        {
            return;
        }

        var top = Host.Chrome.PaneGutter ? PaneGutterRowsFor(windowId) : content.Margin.Top;
        var right = paneWidth is { } width ? WrapMarginFor(windowId, width) : content.Margin.Right;
        var margin = new Margin(content.Margin.Left, top, right, content.Margin.Bottom);
        if (!content.Margin.Equals(margin))
        {
            content.Margin = margin;
        }
    }

    /// <summary>
    /// Keeps every realised tab's wrap margin in step with its pane's width. Rides <c>PostBufferPaint</c>; a changed margin
    /// invalidates its control, so the next frame lays the output out at the column.
    /// </summary>
    private void SyncWrapColumns()
    {
        if (_window is null)
        {
            return;
        }

        var rects = PaneOutputRects();
        foreach (var (paneId, tabs, _) in RealisedPanes())
        {
            if (!rects.TryGetValue(paneId, out var rect) || rect.Width <= 0)
            {
                continue;
            }

            foreach (var page in tabs.TabPages)
            {
                if (page.Tag is string windowId && page.Content is { } content)
                {
                    ApplyContentMargin(content, windowId, rect.Width);
                }
            }
        }
    }

    /// <summary>The width a pane was last measured at, for a pane being rebuilt before it is arranged again.</summary>
    private int? LastPaneWidth(string paneId) =>
        PaneOutputRects().TryGetValue(paneId, out var rect) && rect.Width > 0 ? rect.Width : null;
}
