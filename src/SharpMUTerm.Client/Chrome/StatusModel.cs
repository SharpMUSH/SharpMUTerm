using System.Globalization;

namespace SharpMUTerm.Client.Chrome;

/// <summary>One run of the status row: text in one ink and weight, and what a click on it does (null for nothing).</summary>
/// <param name="Text">The run's visible text, keys in the cells' glyphs (a native host spells them in words).</param>
/// <param name="Ink">
/// Its colour, <c>#rrggbb</c>, already held to the floor on the row's plane; null for the row's own ink
/// (<see cref="StatusModel.Ink"/>), which the cells draw by writing no colour at all.
/// </param>
/// <param name="Bold">Whether the cells draw it bold.</param>
/// <param name="Target">
/// What a click hands back: a target the client put here itself (the palette, a notice's action), never a command a
/// host spelt. Null for text a click does nothing on.
/// </param>
public sealed record StatusRun(string Text, string? Ink, bool Bold, string? Target = null);

/// <summary>What a segment of the status row is, so a host can tell the parts apart without reading their words.</summary>
public enum StatusRole
{
    /// <summary>Words with no other role (the not-connected line).</summary>
    Text,

    /// <summary>The connection: the world's dot, the character and its state (or the reconnect countdown).</summary>
    Identity,

    /// <summary>The game's meters (<c>hp 450/500</c>).</summary>
    Meters,

    /// <summary>Where the map says the character is.</summary>
    Room,

    /// <summary>What the MOO last put in its status line.</summary>
    Moo,

    /// <summary>How far the focused pane is scrolled back, and the key back.</summary>
    Scrollback,

    /// <summary><c>● REC</c>: a scene log is being recorded.</summary>
    Recording,

    /// <summary><c>♪ muted</c>.</summary>
    Muted,

    /// <summary>The encoding in force on the connection.</summary>
    Encoding,

    /// <summary>How long the armed line is, or the history-recall hint in its place.</summary>
    Count,

    /// <summary>The way to the command palette.</summary>
    Palette,

    /// <summary>The keys of the mode the keyboard is in (scrolled back, frozen, a search bar, the composer).</summary>
    ModeHint,

    /// <summary>The keys that move between panes, tabs and command lines.</summary>
    FocusHint,

    /// <summary>A notice laid over the row (<see cref="StatusModel.Notice"/>).</summary>
    Notice,

    /// <summary>A mode prompt that displaces the row (moving or dragging a window).</summary>
    Prompt,

    /// <summary>Whether the character in front is being logged (<c>Log off</c>, <c>Log html</c>): the compact row's.</summary>
    Log,

    /// <summary>Where unread lines are waiting (<c>2 new in Chat</c>): the compact row's.</summary>
    Activity,
}

/// <summary>One part of the status row: its role and its runs.</summary>
/// <param name="Role">What it is.</param>
/// <param name="Runs">How it reads.</param>
public sealed record StatusSegment(StatusRole Role, IReadOnlyList<StatusRun> Runs)
{
    /// <summary>The segment's visible text.</summary>
    public string Text => string.Concat(Runs.Select(r => r.Text));

    /// <summary>The cells the segment takes, counting text elements.</summary>
    public int Width => StatusModel.WidthOf(Text);

    /// <inheritdoc/>
    public bool Equals(StatusSegment? other) => other is not null && Role == other.Role && Runs.SequenceEqual(other.Runs);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Role, Runs.Count);
}

/// <summary>
/// The status row as data, built first by <c>SharpMUTermApp.PaintStatus</c>'s callers — the connection, the mode's keys,
/// meters, reconnect, <c>● REC</c>, <c>♪ muted</c>, the scrollback segment, or a notice or prompt laid over them — as the
/// segments the row lays out from the left and the ones it right-aligns, already cut to what fits the row's width with
/// its reserved fields and drop order. The terminal's cells are rendered from this; the window
/// (<c>HostChrome.Status</c>) leaves no cell for the row and draws the same instance as its footer.
/// </summary>
/// <param name="LeftSegments">The segments from the left edge, a <see cref="Separator"/> apart.</param>
/// <param name="RightSegments">The segments that end at the right edge, a <see cref="Separator"/> apart.</param>
/// <param name="Plane">The row's plane, <c>#rrggbb</c>.</param>
/// <param name="Ink">The row's own ink, <c>#rrggbb</c>: the colour of a run whose <see cref="StatusRun.Ink"/> is null.</param>
public sealed record StatusModel(
    IReadOnlyList<StatusSegment> LeftSegments,
    IReadOnlyList<StatusSegment> RightSegments,
    string Plane,
    string Ink)
{
    /// <summary>What is written between two segments of one cluster.</summary>
    public const string Separator = "   ";

    /// <summary>The least space kept between the clusters.</summary>
    public const int MinGap = 3;

    /// <summary>A status row with nothing on it.</summary>
    public static StatusModel Empty { get; } = new([], [], "#000000", "#ffffff");

    /// <summary>The cells the row was laid out in; the right cluster ends there. Zero when not known.</summary>
    public int Width { get; init; }

    /// <summary>The notice laid over the row, when the row is one.</summary>
    public NoticeModel? Notice { get; init; }

    /// <summary>The left cluster as runs, with a run of the row's ink between segments.</summary>
    public IReadOnlyList<StatusRun> Left => Flatten(LeftSegments);

    /// <summary>The right cluster as runs, with a run of the row's ink between segments.</summary>
    public IReadOnlyList<StatusRun> Right => Flatten(RightSegments);

    /// <summary>The cells a cluster of segments takes, separators included.</summary>
    public static int WidthOf(IReadOnlyCollection<StatusSegment> segments) =>
        segments.Count == 0 ? 0 : segments.Sum(s => s.Width) + (Separator.Length * (segments.Count - 1));

    /// <summary>The cells a text takes, counting text elements.</summary>
    public static int WidthOf(string text) => new StringInfo(text).LengthInTextElements;

    /// <summary>Every target a click on this row may hand back.</summary>
    public IEnumerable<string> Targets() =>
        Left.Concat(Right).Select(r => r.Target).OfType<string>().Distinct(StringComparer.Ordinal);

    /// <summary>The row as plain text, the two clusters joined by a gap: what a test or a screen reader reads.</summary>
    public string Text =>
        string.Concat(Left.Select(r => r.Text))
        + (RightSegments.Count > 0 ? Separator + string.Concat(Right.Select(r => r.Text)) : string.Empty);

    /// <summary>
    /// The row as the cells lay it out: the left cluster, then spaces to push the right cluster to <see cref="Width"/>
    /// (at least <see cref="MinGap"/>), as runs. Runs of the same ink and weight are not merged here; a renderer may.
    /// </summary>
    public IReadOnlyList<StatusRun> Row()
    {
        var runs = new List<StatusRun>(Left);
        if (RightSegments.Count > 0)
        {
            var gap = Math.Max(MinGap, Width - WidthOf(LeftSegments) - WidthOf(RightSegments));
            runs.Add(new StatusRun(new string(' ', gap), null, false));
            runs.AddRange(Right);
        }

        return runs;
    }

    /// <summary>
    /// <see cref="Row"/> cut to <paramref name="width"/> cells, ending in <c>…</c> in the ink of the run it cut into;
    /// unchanged when it fits or the width is not known. The row is one row in every host: a notice is a sentence, and a
    /// row wider than its window wraps and takes a row off the command line.
    /// </summary>
    public IReadOnlyList<StatusRun> Fit(int width)
    {
        var runs = Row();
        if (width <= 0 || WidthOf(string.Concat(runs.Select(r => r.Text))) <= width)
        {
            return runs;
        }

        var budget = Math.Max(0, width - 1);
        var result = new List<StatusRun>();
        var shown = 0;
        foreach (var run in runs)
        {
            if (shown + run.Text.Length <= budget)
            {
                result.Add(run);
                shown += run.Text.Length;
                continue;
            }

            result.Add(run with { Text = run.Text[..(budget - shown)] + "…" });
            break;
        }

        return result;
    }

    /// <inheritdoc/>
    public bool Equals(StatusModel? other) =>
        other is not null && Plane == other.Plane && Ink == other.Ink && Width == other.Width
        && Equals(Notice, other.Notice)
        && LeftSegments.SequenceEqual(other.LeftSegments) && RightSegments.SequenceEqual(other.RightSegments);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Plane, Ink, Width, LeftSegments.Count, RightSegments.Count);

    private static List<StatusRun> Flatten(IReadOnlyList<StatusSegment> segments)
    {
        var runs = new List<StatusRun>();
        for (var i = 0; i < segments.Count; i++)
        {
            if (i > 0)
            {
                runs.Add(new StatusRun(Separator, null, false));
            }

            runs.AddRange(segments[i].Runs);
        }

        return runs;
    }
}
