namespace SharpMUTerm.Client.Chrome;

/// <summary>One run of the status row: text in one ink and weight, and what a click on it does (null for nothing).</summary>
/// <param name="Text">The run's visible text, keys in the cells' glyphs (a native host spells them in words).</param>
/// <param name="Ink">Its colour, <c>#rrggbb</c>, already held to the floor on the row's plane.</param>
/// <param name="Bold">Whether the cells draw it bold.</param>
/// <param name="Target">
/// What a click hands back: a target the client put here itself (the palette, a notice's action), never a command a
/// host spelt. Null for text a click does nothing on.
/// </param>
public sealed record StatusRun(string Text, string Ink, bool Bold, string? Target = null);

/// <summary>
/// The status row as data: what <c>SharpMUTermApp.PaintStatus</c> writes into the cells — the connection, the mode's
/// keys, meters, reconnect, <c>● REC</c>, <c>♪ muted</c>, the scrollback segment, or a notice laid over them — split
/// into the cluster read from the left and the one read from the right. The terminal draws the row in cells; the window
/// (<c>HostChrome.Status</c>) leaves no cell for it and draws this as its footer, so the two cannot say different things.
/// </summary>
/// <param name="Left">The runs from the left edge.</param>
/// <param name="Right">The runs that end at the right edge.</param>
/// <param name="Plane">The row's plane, <c>#rrggbb</c>.</param>
public sealed record StatusModel(IReadOnlyList<StatusRun> Left, IReadOnlyList<StatusRun> Right, string Plane)
{
    /// <summary>A status row with nothing on it.</summary>
    public static StatusModel Empty { get; } = new([], [], "#000000");

    /// <summary>Every target a click on this row may hand back.</summary>
    public IEnumerable<string> Targets() =>
        Left.Concat(Right).Select(r => r.Target).OfType<string>().Distinct(StringComparer.Ordinal);

    /// <summary>The row as plain text, the two clusters joined by a gap: what a test or a screen reader reads.</summary>
    public string Text =>
        string.Concat(Left.Select(r => r.Text)).TrimEnd()
        + (Right.Count > 0 ? "   " + string.Concat(Right.Select(r => r.Text)).Trim() : string.Empty);

    /// <inheritdoc/>
    public bool Equals(StatusModel? other) =>
        other is not null && Plane == other.Plane && Left.SequenceEqual(other.Left) && Right.SequenceEqual(other.Right);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Plane, Left.Count, Right.Count);

    /// <summary>
    /// Splits one row of runs into its two clusters at the widest run of spaces — the padding the row's renderer puts
    /// between what it reads from the left and what it right-aligns — when that gap is at least three wide; otherwise
    /// the whole row reads from the left. Runs are split at the gap and the gap itself is dropped.
    /// </summary>
    public static StatusModel From(IReadOnlyList<StatusRun> runs, string plane)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var text = string.Concat(runs.Select(r => r.Text));
        var (gapAt, gapLength) = (-1, 0);
        for (var i = 0; i < text.Length;)
        {
            if (text[i] != ' ')
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && text[i] == ' ')
            {
                i++;
            }

            // Only a gap with text on both sides divides the row; trailing padding is not a right cluster.
            if (i < text.Length && start > 0 && i - start >= 3 && i - start > gapLength)
            {
                (gapAt, gapLength) = (start, i - start);
            }
        }

        if (gapAt < 0)
        {
            return new StatusModel(Trim(Slice(runs, 0, text.Length)), [], plane);
        }

        return new StatusModel(Trim(Slice(runs, 0, gapAt)), Trim(Slice(runs, gapAt + gapLength, text.Length)), plane);
    }

    private static List<StatusRun> Slice(IReadOnlyList<StatusRun> runs, int from, int to)
    {
        var result = new List<StatusRun>();
        var at = 0;
        foreach (var run in runs)
        {
            var (start, end) = (Math.Max(from, at), Math.Min(to, at + run.Text.Length));
            if (end > start)
            {
                result.Add(run with { Text = run.Text[(start - at)..(end - at)] });
            }

            at += run.Text.Length;
        }

        return result;
    }

    private static List<StatusRun> Trim(List<StatusRun> runs)
    {
        while (runs.Count > 0 && string.IsNullOrWhiteSpace(runs[0].Text))
        {
            runs.RemoveAt(0);
        }

        while (runs.Count > 0 && string.IsNullOrWhiteSpace(runs[^1].Text))
        {
            runs.RemoveAt(runs.Count - 1);
        }

        if (runs.Count > 0)
        {
            runs[0] = runs[0] with { Text = runs[0].Text.TrimStart() };
            runs[^1] = runs[^1] with { Text = runs[^1].Text.TrimEnd() };
        }

        return runs;
    }

    /// <summary>
    /// The runs with <paramref name="target"/> set on the characters of the last occurrence of <paramref name="text"/>,
    /// splitting runs at its edges; unchanged when the text is not there. How the client marks a hint or a notice's
    /// action as clickable after drawing it.
    /// </summary>
    public static IReadOnlyList<StatusRun> Mark(IReadOnlyList<StatusRun> runs, string text, string target)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var whole = string.Concat(runs.Select(r => r.Text));
        var at = string.IsNullOrEmpty(text) ? -1 : whole.LastIndexOf(text, StringComparison.Ordinal);
        if (at < 0)
        {
            return runs;
        }

        var end = at + text.Length;
        var result = new List<StatusRun>();
        var pos = 0;
        foreach (var run in runs)
        {
            var (rs, re) = (pos, pos + run.Text.Length);
            pos = re;
            if (re <= at || rs >= end)
            {
                result.Add(run);
                continue;
            }

            var (a, b) = (Math.Max(rs, at), Math.Min(re, end));
            if (a > rs)
            {
                result.Add(run with { Text = run.Text[..(a - rs)] });
            }

            result.Add(run with { Text = run.Text[(a - rs)..(b - rs)], Target = target });
            if (re > b)
            {
                result.Add(run with { Text = run.Text[(b - rs)..] });
            }
        }

        return result;
    }
}
