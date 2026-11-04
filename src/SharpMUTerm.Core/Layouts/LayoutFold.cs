using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Layouts;

/// <summary>
/// A layout folded into the workspace's own split tree, for one session.
/// </summary>
/// <param name="Root">
/// The tree, built from <see cref="PaneNode"/> and <see cref="SplitNode"/> exactly as a workspace's is, except that a
/// pane's <see cref="PaneNode.Tabs"/> are <em>panel ids</em> rather than window ids — the host maps each to whatever
/// draws it. Pane ids are <c>panel:&lt;id of the pane's first panel&gt;</c>. Null only when nothing is drawn.
/// </param>
/// <param name="Collapsed">Placed collapsed and with data: the host shows each as a title the player can open.</param>
/// <param name="Waiting">Placed, known, and without data yet: drawn once <see cref="IPanelAvailability"/> says so.</param>
public sealed record LayoutFoldResult(
    LayoutNode? Root,
    IReadOnlyList<PanelPlacement> Collapsed,
    IReadOnlyList<PanelPlacement> Waiting);

/// <summary>
/// Folds a <see cref="LayoutDefinition"/> into a split tree, the shape the terminal's workspace already draws:
/// <code>
/// Column[ TopStrip, Row[ Column[ Main, Bottom ], Side ] ]
/// </code>
/// with any empty region left out. Panels in one region share it side by side (main, bottom, top strip) or stacked
/// (side), weighted by <see cref="PanelPlacement.Size"/>. Unknown panel ids are skipped, a collapsed panel is listed
/// rather than given a pane, and a panel without data is left out until it has some.
/// <para>
/// A tab joins its host's pane only when the host has a pane of its own (is drawn and is not itself a tab);
/// otherwise it takes a pane in its own region. One level, so there are no chains or cycles to resolve.
/// </para>
/// </summary>
public static class LayoutFold
{
    /// <summary>The share of the window a region takes when the layout does not say.</summary>
    public static double DefaultShare(LayoutRegion region) => region switch
    {
        LayoutRegion.Side => 0.3,
        LayoutRegion.Bottom => 0.2,
        LayoutRegion.TopStrip => 0.1,
        _ => 1.0,
    };

    /// <summary>The prefix of every pane id the fold makes.</summary>
    public const string PanePrefix = "panel:";

    /// <summary>Folds <paramref name="layout"/>; with no <paramref name="availability"/>, every known panel has data.</summary>
    public static LayoutFoldResult Fold(LayoutDefinition layout, IPanelAvailability? availability = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var known = layout.KnownPlacements
            .Select((p, index) => (Placement: p, Index: index))
            .OrderBy(x => x.Placement.Order)
            .ThenBy(x => x.Index)
            .Select(x => x.Placement)
            .ToList();

        bool HasData(PanelPlacement p) => availability?.HasData(p.PanelId) ?? true;

        var collapsed = known.Where(p => p.Collapsed && HasData(p)).ToList();
        var waiting = known.Where(p => !HasData(p)).ToList();
        var drawn = known.Where(p => !p.Collapsed && HasData(p)).ToList();

        // A panel drawn twice would be two panes holding one thing; the first placement wins.
        drawn = drawn.DistinctBy(p => p.PanelId, StringComparer.Ordinal).ToList();

        var hosts = drawn.Where(p => p.TabOf is null).Select(p => p.PanelId).ToHashSet(StringComparer.Ordinal);
        bool IsTab(PanelPlacement p) => p.TabOf is not null && hosts.Contains(p.TabOf)
            && !string.Equals(p.TabOf, p.PanelId, StringComparison.Ordinal);

        var sequence = 0;
        LayoutNode? Region(LayoutRegion region, SplitDirection direction)
        {
            var panes = new List<(PaneNode Pane, double Size)>();
            foreach (var slot in drawn.Where(p => p.Region == region && !IsTab(p)))
            {
                var tabs = new List<string> { slot.PanelId };
                tabs.AddRange(drawn.Where(p => IsTab(p) && string.Equals(p.TabOf, slot.PanelId, StringComparison.Ordinal))
                    .Select(p => p.PanelId));
                panes.Add((new PaneNode(PanePrefix + slot.PanelId, tabs, sequence: ++sequence), slot.Size > 0 ? slot.Size : 1));
            }

            return panes.Count switch
            {
                0 => null,
                1 => panes[0].Pane,
                _ => new SplitNode(direction, panes.Select(p => (LayoutNode)p.Pane).ToList(), panes.Select(p => p.Size).ToList()),
            };
        }

        double Share(LayoutRegion region) =>
            Math.Clamp(layout.RegionShares?.GetValueOrDefault(region, DefaultShare(region)) ?? DefaultShare(region), 0.05, 0.9);

        // Numbered in the order a reader meets them: main first, then what is around it.
        var main = Region(LayoutRegion.Main, SplitDirection.Row);
        var bottom = Region(LayoutRegion.Bottom, SplitDirection.Row);
        var side = Region(LayoutRegion.Side, SplitDirection.Column);
        var top = Region(LayoutRegion.TopStrip, SplitDirection.Row);

        var centre = Join(main, bottom, SplitDirection.Column, Share(LayoutRegion.Bottom), secondIsShare: true);
        var body = Join(centre, side, SplitDirection.Row, Share(LayoutRegion.Side), secondIsShare: true);
        var root = Join(top, body, SplitDirection.Column, Share(LayoutRegion.TopStrip), secondIsShare: false);

        return new LayoutFoldResult(root, collapsed, waiting);
    }

    /// <summary>
    /// Two nodes along one axis, either of which may be missing. <paramref name="share"/> is the fraction the
    /// region-shaped one takes: the second when <paramref name="secondIsShare"/>, else the first.
    /// </summary>
    private static LayoutNode? Join(LayoutNode? first, LayoutNode? second, SplitDirection direction, double share, bool secondIsShare)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        var sizes = secondIsShare ? new[] { 1 - share, share } : new[] { share, 1 - share };
        return new SplitNode(direction, [first, second], sizes);
    }
}
