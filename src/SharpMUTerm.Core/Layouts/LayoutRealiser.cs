using System.Text;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Layouts;

/// <summary>
/// A layout made into a workspace tree: the panes hold window ids, ready for
/// <see cref="WorkspaceLayout.ReplaceTree"/>.
/// </summary>
/// <param name="Root">The tree. Every pane has at least one tab.</param>
/// <param name="FocusedPaneId">The pane holding the output window, which is where the keyboard goes.</param>
/// <param name="PanelPanes">Which pane each drawn or collapsed panel ended up in, by panel id.</param>
/// <param name="Shape">
/// <see cref="LayoutRealiser.FoldShape"/> of the fold this was made from: what is drawn and where, with no window
/// ids in it. Two folds with one shape realise to the same panes, so a host re-folds without rebuilding anything
/// while the shape holds.
/// </param>
public sealed record RealisedLayout(
    LayoutNode Root,
    string FocusedPaneId,
    IReadOnlyDictionary<string, string> PanelPanes,
    string Shape);

/// <summary>
/// Turns a <see cref="LayoutFoldResult"/> (panes of <em>panel ids</em>) into a workspace tree (panes of
/// <em>window ids</em>), given what each panel is drawn by. Pure: the host says which windows a panel stands for
/// and which other windows must survive; this decides where everything goes.
/// <list type="bullet">
/// <item>A pane takes every window of every panel folded into it, in that order; a pane left with none is dropped
/// and its split collapsed.</item>
/// <item>A collapsed panel's windows are background tabs of the first pane in its region that is not a strip, else of the output pane:
/// a title the player can open, which is what collapsed means in a pane tree.</item>
/// <item>Where the host draws collapsed panels as bars (<c>collapsedAsBars</c>), a collapsed panel's windows are a pane of
/// their own instead, under that pane in its column, with the id <see cref="CollapsedPanePrefix"/> and the panel id: the
/// host draws it as a one-line bar that a click expands.</item>
/// <item>Every window in <c>keep</c> that no panel placed survives — in the pane it asks for when that pane still
/// exists, else in the output pane. Applying a layout never closes a window.</item>
/// </list>
/// </summary>
public static class LayoutRealiser
{
    /// <summary>The id prefix of a collapsed panel's own pane (<c>collapsedAsBars</c>), followed by the panel id.</summary>
    public const string CollapsedPanePrefix = "collapsed:";

    /// <summary>The share a collapsed panel's pane is given beside the pane above it, for when it is expanded.</summary>
    public const double CollapsedPaneShare = 0.5;

    /// <summary>The panel a collapsed panel's pane stands for, or null when <paramref name="paneId"/> is no such pane.</summary>
    public static string? CollapsedPanelOf(string? paneId) =>
        paneId is not null && paneId.StartsWith(CollapsedPanePrefix, StringComparison.Ordinal) && paneId.Length > CollapsedPanePrefix.Length
            ? paneId[CollapsedPanePrefix.Length..]
            : null;

    /// <summary>Realises <paramref name="fold"/>, or returns null when it draws nothing.</summary>
    /// <param name="layout">The layout folded, for the regions of collapsed panels.</param>
    /// <param name="fold">The fold.</param>
    /// <param name="windowsOf">The windows that draw a panel, in tab order; empty when nothing does yet.</param>
    /// <param name="outputWindowId">The session's own window: focused, and where strays go.</param>
    /// <param name="keep">Windows to keep, each with the pane id it would like to stay in (or null).</param>
    /// <param name="collapsedAsBars">Whether a collapsed panel gets a pane of its own under its region's pane (see above).</param>
    public static RealisedLayout? Realise(
        LayoutDefinition layout,
        LayoutFoldResult fold,
        Func<string, IReadOnlyList<string>> windowsOf,
        string outputWindowId,
        IEnumerable<(string WindowId, string? PaneId)>? keep = null,
        bool collapsedAsBars = false)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(fold);
        ArgumentNullException.ThrowIfNull(windowsOf);
        if (fold.Root is null)
        {
            return null;
        }

        var placed = new HashSet<string>(StringComparer.Ordinal);
        var panelPanes = new Dictionary<string, string>(StringComparer.Ordinal);
        var paneFirstPanel = new Dictionary<string, string>(StringComparer.Ordinal);

        LayoutNode? Build(LayoutNode node)
        {
            if (node is PaneNode pane)
            {
                var tabs = new List<string>();
                foreach (var panel in pane.Tabs)
                {
                    var windows = windowsOf(panel).Where(placed.Add).ToList();
                    if (windows.Count == 0)
                    {
                        continue;
                    }

                    tabs.AddRange(windows);
                    panelPanes[panel] = pane.Id;
                    paneFirstPanel.TryAdd(pane.Id, panel);
                }

                return tabs.Count == 0 ? null : new PaneNode(pane.Id, tabs, 0, false, pane.Sequence);
            }

            var split = (SplitNode)node;
            var children = new List<LayoutNode>();
            var sizes = new List<double>();
            for (var i = 0; i < split.Children.Count; i++)
            {
                if (Build(split.Children[i]) is { } child)
                {
                    children.Add(child);
                    sizes.Add(split.Sizes[i]);
                }
            }

            return children.Count switch
            {
                0 => null,
                1 => children[0],
                _ => new SplitNode(split.Direction, children, sizes),
            };
        }

        var root = Build(fold.Root);
        if (root is null)
        {
            return null;
        }

        var panes = root.Panes().ToList();
        var output = panes.FirstOrDefault(p => p.Tabs.Contains(outputWindowId)) ?? panes[0];

        foreach (var collapsed in fold.Collapsed)
        {
            var windows = windowsOf(collapsed.PanelId).Where(placed.Add).ToList();
            if (windows.Count == 0)
            {
                continue;
            }

            // Never a strip's pane (the vitals, the quick bar): a tab there would turn it into a pane that shares.
            var target = panes.FirstOrDefault(p =>
                    paneFirstPanel.TryGetValue(p.Id, out var first)
                    && PanelSizing.FixedRows(first) is null
                    && layout.PlacementOf(first)?.Region == collapsed.Region)
                ?? output;
            if (collapsedAsBars)
            {
                var bar = new PaneNode(CollapsedPanePrefix + collapsed.PanelId, windows);
                root = Below(root, target, bar);
                panelPanes[collapsed.PanelId] = bar.Id;
                continue;
            }

            target.Tabs.AddRange(windows);
            panelPanes[collapsed.PanelId] = target.Id;
        }

        foreach (var (windowId, paneId) in keep ?? [])
        {
            if (!placed.Add(windowId))
            {
                continue;
            }

            var target = paneId is null ? null : panes.FirstOrDefault(p => p.Id == paneId);
            (target ?? output).Tabs.Add(windowId);
        }

        var at = output.Tabs.IndexOf(outputWindowId);
        output.ActiveIndex = at >= 0 ? at : 0;
        return new RealisedLayout(root, output.Id, panelPanes, FoldShape(fold));
    }

    /// <summary>
    /// <paramref name="root"/> with <paramref name="bar"/> under <paramref name="target"/>: beside it in its column when its
    /// parent is one, else in a new column the two share (<see cref="CollapsedPaneShare"/>).
    /// </summary>
    private static LayoutNode Below(LayoutNode root, PaneNode target, PaneNode bar)
    {
        if (ReferenceEquals(root, target))
        {
            return new SplitNode(SplitDirection.Column, [target, bar], [1, CollapsedPaneShare]);
        }

        if (root is not SplitNode split)
        {
            return root;
        }

        var children = new List<LayoutNode>();
        var sizes = new List<double>();
        for (var i = 0; i < split.Children.Count; i++)
        {
            var child = split.Children[i];
            if (ReferenceEquals(child, target) && split.Direction == SplitDirection.Column)
            {
                children.Add(child);
                sizes.Add(split.Sizes[i]);
                children.Add(bar);
                sizes.Add(split.Sizes[i] * CollapsedPaneShare);
                continue;
            }

            children.Add(ReferenceEquals(child, target)
                ? new SplitNode(SplitDirection.Column, [target, bar], [1, CollapsedPaneShare])
                : Below(child, target, bar));
            sizes.Add(split.Sizes[i]);
        }

        return new SplitNode(split.Direction, children, sizes);
    }

    /// <summary>
    /// What a fold draws and where, as a string: the tree of panes with their panel ids, then the collapsed panels.
    /// No sizes and no window ids, so it changes only when a panel appears, goes, or moves.
    /// </summary>
    public static string FoldShape(LayoutFoldResult fold)
    {
        ArgumentNullException.ThrowIfNull(fold);
        var text = new StringBuilder();
        if (fold.Root is { } root)
        {
            Describe(root, text, n => string.Join(",", n.Tabs));
        }

        text.Append("|c:").Append(string.Join(",", fold.Collapsed.Select(c => c.PanelId)));
        return text.ToString();
    }

    /// <summary>
    /// The structure of a workspace tree — pane ids and how they nest, without tabs or sizes. A host compares it
    /// with the one it applied to tell whether the player has since split, closed or moved panes, in which case
    /// the arrangement is theirs and a re-fold must leave it alone.
    /// </summary>
    public static string TreeShape(LayoutNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var text = new StringBuilder();
        Describe(root, text, p => p.Id);
        return text.ToString();
    }

    private static void Describe(LayoutNode node, StringBuilder text, Func<PaneNode, string> pane)
    {
        if (node is PaneNode leaf)
        {
            text.Append('[').Append(pane(leaf)).Append(']');
            return;
        }

        var split = (SplitNode)node;
        text.Append(split.Direction == SplitDirection.Row ? "row(" : "col(");
        for (var i = 0; i < split.Children.Count; i++)
        {
            if (i > 0)
            {
                text.Append(' ');
            }

            Describe(split.Children[i], text, pane);
        }

        text.Append(')');
    }
}
