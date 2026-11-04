namespace SharpMUTerm.Core.Workspaces;

/// <summary>
/// Which part of the pane tree the command line sits under, for a host that puts it in the output's column rather than
/// across the whole window (the desktop window, as its mockups draw it: the side column runs down to the footer and
/// the command line is as wide as the output above it).
/// <para>
/// The answer is a node of the tree, and the bars go under it. It is found from an <em>anchor</em> pane — the pane
/// the output is in — by walking down from the root and stopping at the first node the bars cannot go further into
/// while still touching the bottom of the pane area:
/// </para>
/// <list type="bullet">
/// <item>a pane is the answer;</item>
/// <item>side by side (<see cref="SplitDirection.Row"/>), every child reaches the bottom, so the walk goes into the
/// anchor's child — unless another child holds output too (two characters side by side), and then the bars span the
/// split, because neither column is <em>the</em> output column;</item>
/// <item>stacked (<see cref="SplitDirection.Column"/>), only the last child reaches the bottom, so the walk goes into
/// it only when the anchor is there; otherwise the split is the answer (the output with a quick bar under it).</item>
/// </list>
/// <para>
/// A column has a floor (<see cref="MinimumColumns"/>): when the walk would go into a side-by-side child narrower than
/// that, it stops at the split instead, so three panes side by side share one bar that can still be typed in rather
/// than a field showing the prompt and two letters. The width is the arrangement's, not the keyboard's, so the floor
/// moves nothing on a focus change either.
/// </para>
/// <para>
/// Nothing here reads focus. Moving the selection between panes must not move a pane rectangle — every connected
/// server would be told a new size — so the column is a property of the arrangement, not of where the keyboard is.
/// </para>
/// </summary>
public static class InputColumn
{
    /// <summary>
    /// The narrowest column, in cells, the command line is put under: the field's padding either side (3 + 3), a prompt
    /// such as <c>Corvid · Aetherfall › </c> (22) and 26 cells to type in before the bar wraps (it then grows a row).
    /// Measured on the window's default 120-column grid and the built-in layouts: half of it is 59 and keeps its column,
    /// as do Roleplay's and Combat's output at 100 columns (65, 70) and Roleplay's narrow arrangement at 80 (55); a
    /// three-way split of it is 39, where the bar showed the prompt and two letters, and falls back to a wider node.
    /// </summary>
    public const int MinimumColumns = 54;

    /// <summary>
    /// The node the command line goes under, with no width floor: the root when there is no anchor in the tree.
    /// </summary>
    /// <param name="root">The pane tree.</param>
    /// <param name="anchorPaneId">The pane the output is in, or null.</param>
    /// <param name="holdsOutput">Whether a pane holds output of its own (a character's window), as opposed to furniture.</param>
    public static LayoutNode Target(LayoutNode root, string? anchorPaneId, Func<PaneNode, bool> holdsOutput) =>
        Target(root, anchorPaneId, holdsOutput, int.MaxValue, 0);

    /// <summary>
    /// The node the command line goes under when the pane area is <paramref name="columns"/> wide: as
    /// <see cref="Target(LayoutNode, string?, Func{PaneNode, bool})"/>, except that the walk never goes into a
    /// side-by-side child narrower than <paramref name="minimumColumns"/> — the split is the answer then, the widest
    /// node on the way that still fits a usable bar (the root, if nothing narrower does).
    /// </summary>
    /// <param name="root">The pane tree.</param>
    /// <param name="anchorPaneId">The pane the output is in, or null.</param>
    /// <param name="holdsOutput">Whether a pane holds output of its own (a character's window), as opposed to furniture.</param>
    /// <param name="columns">The pane area's width in cells; zero or less (not arranged yet) applies no floor.</param>
    /// <param name="minimumColumns">The floor, <see cref="MinimumColumns"/> by default.</param>
    public static LayoutNode Target(
        LayoutNode root, string? anchorPaneId, Func<PaneNode, bool> holdsOutput, int columns, int minimumColumns = MinimumColumns)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(holdsOutput);
        if (anchorPaneId is null || !root.Panes().Any(p => p.Id == anchorPaneId))
        {
            return root;
        }

        var floor = columns > 0 ? minimumColumns : 0;
        var width = columns > 0 ? columns : int.MaxValue;
        var node = root;
        while (node is SplitNode split)
        {
            var index = split.Children.FindIndex(c => c.Panes().Any(p => p.Id == anchorPaneId));
            if (split.Direction == SplitDirection.Row)
            {
                var sharing = split.Children.Where((c, i) => i != index && c.Panes().Any(holdsOutput)).Any();
                if (sharing)
                {
                    return split;
                }

                if (width != int.MaxValue)
                {
                    // The solver's own arithmetic, so the floor is measured on the width the child is really given.
                    width = LayoutSolver.Distribute(Math.Max(0, width - PaneExtent.Dividers(split)), split.Sizes)[index];
                    if (width < floor)
                    {
                        return split;
                    }
                }
            }
            else if (index != split.Children.Count - 1)
            {
                return split;
            }

            node = split.Children[index];
        }

        return node;
    }

    /// <summary>
    /// The pane the command line is anchored to: <paramref name="preferred"/> (the live layout's output pane) when it is
    /// in the tree, else the first pane in pane order that holds output, else none.
    /// </summary>
    public static string? Anchor(WorkspaceLayout layout, string? preferred, Func<PaneNode, bool> holdsOutput)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(holdsOutput);
        if (preferred is not null && layout.FindPane(preferred) is not null)
        {
            return preferred;
        }

        return layout.Panes.FirstOrDefault(holdsOutput)?.Id;
    }
}
