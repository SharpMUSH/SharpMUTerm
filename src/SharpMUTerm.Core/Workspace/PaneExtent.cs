namespace SharpMUTerm.Core.Workspaces;

/// <summary>
/// The rows a pane asks for in a stacked (<see cref="SplitDirection.Column"/>) split, and how a split's rows are
/// shared out once some of its panes have asked: a pane with <see cref="PaneNode.FixedRows"/> gets exactly that,
/// one with <see cref="PaneNode.MinRows"/> gets at least that, and the rest is shared by the split's fractions
/// among the panes that did not fix their height.
/// <para>
/// Only rows. A strip is one row of meters however wide it is drawn, so its width stays a share like any pane's.
/// </para>
/// </summary>
public static class PaneExtent
{
    /// <summary>
    /// The exact rows <paramref name="node"/> takes, or null when it shares. A stack of fixed panes is fixed at
    /// their sum and dividers; a side-by-side row of them at the tallest; a subtree with any sharing pane in it
    /// shares.
    /// </summary>
    public static int? FixedRows(LayoutNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is PaneNode pane)
        {
            return pane.FixedRows is > 0 ? pane.FixedRows : null;
        }

        var split = (SplitNode)node;
        var rows = new List<int>(split.Children.Count);
        foreach (var child in split.Children)
        {
            if (FixedRows(child) is not { } fixedRows)
            {
                return null;
            }

            rows.Add(fixedRows);
        }

        return split.Direction == SplitDirection.Row
            ? rows.Max()
            : rows.Sum() + Dividers(split);
    }

    /// <summary>The fewest rows <paramref name="node"/> is given: its fixed height, else its declared floor, else 0.</summary>
    public static int MinRows(LayoutNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is PaneNode pane)
        {
            return FixedRows(pane) ?? Math.Max(0, pane.MinRows ?? 0);
        }

        var split = (SplitNode)node;
        return split.Direction == SplitDirection.Row
            ? split.Children.Max(MinRows)
            : split.Children.Sum(MinRows) + Dividers(split);
    }

    /// <summary>
    /// The cells between child <paramref name="index"/> and child <paramref name="index"/> + 1 of
    /// <paramref name="split"/>. Side by side it is always a divider column. Stacked, a divider row is drawn only
    /// where nothing else already marks the boundary:
    /// <list type="bullet">
    /// <item>none beside a fixed-height strip, which sits flush and is told apart by its own plane — it cannot be
    /// resized, so a row there would be a rule with nothing to drag;</item>
    /// <item>none above a child whose top row is all tab strip (<see cref="HeaderPanes"/>): that strip is the
    /// boundary, and the host makes its empty run the divider's handle.</item>
    /// </list>
    /// </summary>
    public static int DividerRows(SplitNode split, int index)
    {
        ArgumentNullException.ThrowIfNull(split);
        if (index < 0 || index + 1 >= split.Children.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (split.Direction == SplitDirection.Row)
        {
            return LayoutSolver.DividerThickness;
        }

        var above = split.Children[index];
        var below = split.Children[index + 1];
        if (FixedRows(above) is not null || FixedRows(below) is not null || HeaderPanes(below).Count > 0)
        {
            return 0;
        }

        return LayoutSolver.DividerThickness;
    }

    /// <summary>Every divider cell along <paramref name="split"/>'s axis: the sum of <see cref="DividerRows"/>.</summary>
    public static int Dividers(SplitNode split)
    {
        ArgumentNullException.ThrowIfNull(split);
        var total = 0;
        for (var i = 0; i + 1 < split.Children.Count; i++)
        {
            total += DividerRows(split, i);
        }

        return total;
    }

    /// <summary>
    /// The panes whose tab strips run along <paramref name="node"/>'s whole top edge, or an empty list when any part
    /// of that edge is not a tab strip. A pane is its own answer; a stack is its first child's; a side-by-side row is
    /// every child's, and only when each of them has one.
    /// </summary>
    public static IReadOnlyList<PaneNode> HeaderPanes(LayoutNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node is PaneNode pane)
        {
            return pane.DrawsHeader ? [pane] : [];
        }

        var split = (SplitNode)node;
        if (split.Direction == SplitDirection.Column)
        {
            return HeaderPanes(split.Children[0]);
        }

        var panes = new List<PaneNode>();
        foreach (var child in split.Children)
        {
            var below = HeaderPanes(child);
            if (below.Count == 0)
            {
                return [];
            }

            panes.AddRange(below);
        }

        return panes;
    }

    /// <summary>
    /// Which children of <paramref name="split"/> are fixed in height. When every child is, the last is treated as
    /// sharing, so the rows left over have somewhere to go — and the terminal's grid is built the same way.
    /// </summary>
    public static int?[] FixedChildren(SplitNode split)
    {
        ArgumentNullException.ThrowIfNull(split);
        var fixedRows = split.Children.Select(FixedRows).ToArray();
        if (fixedRows.All(f => f is not null))
        {
            fixedRows[^1] = null;
        }

        return fixedRows;
    }

    /// <summary>
    /// Shares <paramref name="total"/> rows among a stacked split's children: fixed children first (in order, each
    /// clamped to what is left), then the rest by the sharing children's fractions, and then any sharing child
    /// under its floor is raised by taking from the sharing children with rows to spare. The result sums to
    /// <paramref name="total"/>.
    /// </summary>
    public static int[] DistributeRows(int total, SplitNode split)
    {
        ArgumentNullException.ThrowIfNull(split);
        var count = split.Children.Count;
        var result = new int[count];
        if (total <= 0)
        {
            return result;
        }

        var fixedRows = FixedChildren(split);
        var remaining = total;
        for (var i = 0; i < count; i++)
        {
            if (fixedRows[i] is { } rows)
            {
                result[i] = Math.Min(rows, remaining);
                remaining -= result[i];
            }
        }

        var sharing = Enumerable.Range(0, count).Where(i => fixedRows[i] is null).ToArray();
        // Distribute expects fractions of a whole, so the sharing children's are re-normalised among themselves.
        var weights = sharing.Select(i => Math.Max(0, split.Sizes[i])).ToArray();
        var sum = weights.Sum();
        var fractions = sum > 0
            ? weights.Select(w => w / sum).ToArray()
            : weights.Select(_ => 1.0 / weights.Length).ToArray();
        var cells = LayoutSolver.Distribute(remaining, fractions);
        for (var j = 0; j < sharing.Length; j++)
        {
            result[sharing[j]] = cells[j];
        }

        // Floors: raise each short child from whichever sharing sibling has the most to spare.
        foreach (var i in sharing)
        {
            var need = MinRows(split.Children[i]) - result[i];
            while (need > 0)
            {
                var donor = sharing
                    .Where(k => k != i && result[k] > MinRows(split.Children[k]))
                    .OrderByDescending(k => result[k] - MinRows(split.Children[k]))
                    .Cast<int?>()
                    .FirstOrDefault();
                if (donor is not { } d)
                {
                    break;
                }

                result[d]--;
                result[i]++;
                need--;
            }
        }

        return result;
    }
}
