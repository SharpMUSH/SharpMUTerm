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
/// Nothing here reads focus. Moving the selection between panes must not move a pane rectangle — every connected
/// server would be told a new size — so the column is a property of the arrangement, not of where the keyboard is.
/// </para>
/// </summary>
public static class InputColumn
{
    /// <summary>
    /// The node the command line goes under: the root when there is no anchor in the tree.
    /// </summary>
    /// <param name="root">The pane tree.</param>
    /// <param name="anchorPaneId">The pane the output is in, or null.</param>
    /// <param name="holdsOutput">Whether a pane holds output of its own (a character's window), as opposed to furniture.</param>
    public static LayoutNode Target(LayoutNode root, string? anchorPaneId, Func<PaneNode, bool> holdsOutput)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(holdsOutput);
        if (anchorPaneId is null || !root.Panes().Any(p => p.Id == anchorPaneId))
        {
            return root;
        }

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
