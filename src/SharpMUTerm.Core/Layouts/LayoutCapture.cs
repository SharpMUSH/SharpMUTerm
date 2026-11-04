using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Layouts;

/// <summary>An arrangement read back into placements, for <see cref="LayoutLibrary.Update"/> or <see cref="LayoutLibrary.SaveAsNew"/>.</summary>
public sealed record CapturedLayout(
    IReadOnlyList<PanelPlacement> Placements,
    IReadOnlyDictionary<LayoutRegion, double> RegionShares);

/// <summary>
/// Reads the workspace the player has arranged back into a layout: "save changes" and "save as new". The pane
/// holding the output is <see cref="LayoutRegion.Main"/>; a pane entirely above it is the top strip, one entirely
/// to its right is the side column, one entirely below it is the bottom band, and anything else shares the main
/// area. A pane's first panel is its host and the rest are tabs of it (<see cref="PanelPlacement.TabOf"/>).
/// <para>
/// Geometry is read off <see cref="LayoutSolver"/> on a nominal 1000×1000 area, so it is the tree's own proportions
/// and not a terminal's size. Panels the previous layout placed that are not on screen now (a map on a session
/// that has recorded no room yet) keep their old placement, so saving from a quiet session does not lose them.
/// </para>
/// </summary>
public static class LayoutCapture
{
    private const int Extent = 1000;

    /// <summary>
    /// Captures <paramref name="root"/>. <paramref name="panelOf"/> names the panel a window draws, or null for a
    /// window that belongs to no panel of this session (another character's, the web view). Null when no pane holds
    /// the output, since a layout is arranged around it.
    /// </summary>
    public static CapturedLayout? Capture(LayoutNode root, Func<string, string?> panelOf, LayoutDefinition? previous = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(panelOf);

        var rects = LayoutSolver.Solve(root, new PaneRect(0, 0, Extent, Extent));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var panes = new List<(PaneRect Rect, List<string> Panels)>();
        foreach (var pane in root.Panes())
        {
            var panels = pane.Tabs.Select(panelOf).OfType<string>().Where(PanelIds.IsKnown).Where(seen.Add).ToList();
            if (panels.Count > 0 && rects.TryGetValue(pane.Id, out var rect))
            {
                panes.Add((rect, panels));
            }
        }

        var main = panes.FirstOrDefault(p => p.Panels.Contains(PanelIds.Output));
        if (main.Panels is null)
        {
            return null;
        }

        var m = main.Rect;
        LayoutRegion RegionOf(PaneRect r) =>
            r.Y + r.Height <= m.Y ? LayoutRegion.TopStrip
            : r.X >= m.X + m.Width ? LayoutRegion.Side
            : r.Y >= m.Y + m.Height ? LayoutRegion.Bottom
            : LayoutRegion.Main;

        var placements = new List<PanelPlacement>();
        foreach (var group in panes.GroupBy(p => RegionOf(p.Rect)))
        {
            var ordered = group.Key == LayoutRegion.Side
                ? group.OrderBy(p => p.Rect.Y).ThenBy(p => p.Rect.X)
                : group.OrderBy(p => p.Rect.X).ThenBy(p => p.Rect.Y);
            var order = 0;
            foreach (var (rect, panels) in ordered)
            {
                var size = group.Key == LayoutRegion.Side ? rect.Height : rect.Width;
                var host = panels[0];
                placements.Add(new PanelPlacement
                {
                    PanelId = host,
                    Region = group.Key,
                    Order = order,
                    Size = Math.Round(Math.Max(1, size) / 100.0, 2),
                });
                placements.AddRange(panels.Skip(1).Select(tab => new PanelPlacement
                {
                    PanelId = tab,
                    Region = group.Key,
                    Order = order,
                    TabOf = host,
                }));
                order++;
            }
        }

        if (previous is not null)
        {
            placements.AddRange(previous.KnownPlacements.Where(p => !seen.Contains(p.PanelId)));
        }

        return new CapturedLayout(placements, Shares(panes.Select(p => (p.Rect, RegionOf(p.Rect))).ToList(), m));
    }

    private static Dictionary<LayoutRegion, double> Shares(List<(PaneRect Rect, LayoutRegion Region)> panes, PaneRect main)
    {
        static double Clamp(double share) => Math.Round(Math.Clamp(share, 0.05, 0.9), 2);
        var shares = new Dictionary<LayoutRegion, double>();

        var side = panes.Where(p => p.Region == LayoutRegion.Side).ToList();
        if (side.Count > 0)
        {
            var left = side.Min(p => p.Rect.X);
            var right = side.Max(p => p.Rect.X + p.Rect.Width);
            shares[LayoutRegion.Side] = Clamp((right - left) / (double)Extent);
        }

        var bottom = panes.Where(p => p.Region == LayoutRegion.Bottom).ToList();
        if (bottom.Count > 0)
        {
            var top = bottom.Min(p => p.Rect.Y);
            var foot = bottom.Max(p => p.Rect.Y + p.Rect.Height);
            var whole = foot - main.Y;
            shares[LayoutRegion.Bottom] = Clamp(whole <= 0 ? LayoutFold.DefaultShare(LayoutRegion.Bottom) : (foot - top) / (double)whole);
        }

        var strip = panes.Where(p => p.Region == LayoutRegion.TopStrip).ToList();
        if (strip.Count > 0)
        {
            var foot = strip.Max(p => p.Rect.Y + p.Rect.Height);
            shares[LayoutRegion.TopStrip] = Clamp(foot / (double)Extent);
        }

        return shares;
    }
}
