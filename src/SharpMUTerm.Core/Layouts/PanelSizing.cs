using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Layouts;

/// <summary>
/// How tall a panel is drawn, in rows of content. Most panels share the window like any pane; a <em>strip</em> is
/// one row however much room there is (the vitals meters, the quick bar), and an editor has a floor below which it
/// stops being one (the docked composer).
/// </summary>
public static class PanelSizing
{
    /// <summary>The rows of content a docked composer is never squeezed below: its header, four rows of post and its key hints.</summary>
    public const int ComposerMinRows = 6;

    /// <summary>The exact rows of content <paramref name="panelId"/> draws, or null when it shares the window.</summary>
    public static int? FixedRows(string? panelId) => panelId switch
    {
        PanelIds.Vitals => 1,
        PanelIds.QuickBar => 1,
        _ => null,
    };

    /// <summary>The fewest rows of content <paramref name="panelId"/> is given, or null for the ordinary floor.</summary>
    public static int? MinRows(string? panelId) => panelId switch
    {
        PanelIds.Composer => ComposerMinRows,
        _ => null,
    };

    /// <summary>
    /// Whether a pane holding <paramref name="panels"/> (one entry per tab: its panel id, or null for a window no
    /// panel draws) is a strip: every tab a fixed-height panel. A strip with one tab shows no tab strip, because
    /// its content already says what it is.
    /// </summary>
    public static bool IsStrip(IReadOnlyList<string?> panels) =>
        panels.Count > 0 && panels.All(p => FixedRows(p) is not null);

    /// <summary>Whether a pane holding <paramref name="panels"/> draws its tab strip: anything but a one-tab strip.</summary>
    public static bool ShowsTabStrip(IReadOnlyList<string?> panels) => !(panels.Count == 1 && IsStrip(panels));

    /// <summary>
    /// Sets <paramref name="pane"/>'s <see cref="PaneNode.FixedRows"/> and <see cref="PaneNode.MinRows"/> from what it
    /// holds. <paramref name="panelOf"/> names the panel each tab draws (null for an ordinary window), and
    /// <paramref name="tabStripRows"/> is the height of the tab strip the host draws over a pane that shows one.
    /// A strip is as tall as its tallest panel (plus the tab strip when it holds several); any other pane shares,
    /// with a floor from the tallest floor among its panels.
    /// </summary>
    public static void Apply(PaneNode pane, Func<string, string?> panelOf, int tabStripRows)
    {
        ArgumentNullException.ThrowIfNull(pane);
        ArgumentNullException.ThrowIfNull(panelOf);
        var panels = pane.Tabs.Select(panelOf).ToList();
        var chrome = ShowsTabStrip(panels) ? Math.Max(0, tabStripRows) : 0;
        pane.DrawsHeader = chrome > 0;

        if (IsStrip(panels))
        {
            pane.FixedRows = panels.Max(p => FixedRows(p)!.Value) + chrome;
            pane.MinRows = null;
            return;
        }

        pane.FixedRows = null;
        var floor = panels.Select(MinRows).Where(m => m is not null).Select(m => m!.Value).DefaultIfEmpty(0).Max();
        pane.MinRows = floor > 0 ? floor + chrome : null;
    }
}
