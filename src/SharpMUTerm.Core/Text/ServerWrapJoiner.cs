namespace SharpMUTerm.Core.Text;

/// <summary>
/// Joins back together the lines a server broke at its own wrap column, so a paragraph reflows to the pane
/// it is read in rather than to the 78 or 80 columns the game assumed (F5 ▸ <c>join wraps</c>, per world,
/// off by default; <see cref="Configuration.WorldDefinition.JoinWrappedLines"/>).
/// <para>
/// <b>A false join is worse than a missed one</b>, so every condition below is a reason <em>not</em> to join
/// and they all have to pass. A missed join leaves the text exactly as the server sent it, which is what
/// every client shows today; a false join glues a room's exits onto its description or a channel line onto
/// the one above it, and nothing on screen says it happened. The rule, exactly:
/// </para>
/// <list type="number">
/// <item>The wrap column is known: it is the <em>third longest</em> of the last <see cref="SampleWindow"/>
/// non-blank lines (trailing spaces ignored), once there are <see cref="MinimumSamples"/> of them, and only
/// if it is at least <see cref="MinimumWrapColumn"/>. Third rather than first so one or two over-long lines
/// do not move it; a block of wide ASCII art does, upwards, which stops joining while it is in the window —
/// the safe direction to fail in.</item>
/// <item>Neither line is a prompt, blank, carries a rule colour, or holds an interactive span (an MXP
/// <c>&lt;SEND&gt;</c>, link or image): structured lines are the server's layout, not its wrapping.</item>
/// <item>Neither line has a run of three spaces inside it — a column layout (a who list, a score sheet)
/// is not prose.</item>
/// <item>The first line is <em>full</em>: no longer than the wrap column, no shorter than it by more than
/// <see cref="Slack"/>, <b>and</b> the next line's first word would not have fitted on it (within one cell,
/// since the column is the longest row seen and can fall a cell short of the server's) — the signature of
/// a greedy word wrap, and the one condition that tells a wrapped line from a short line that happens to
/// end unpunctuated.</item>
/// <item>The first line ends in a letter, a digit or a comma. Not a full stop, colon, closing quote,
/// bracket or hyphen: a hyphen would have to be either kept or dropped, and both are wrong half the
/// time.</item>
/// <item>The next line starts, with no indentation, with a lower-case letter.</item>
/// <item>The style the first line ends in is the style the next one starts in.</item>
/// </list>
/// <para>
/// Lines are joined with one space. A paragraph of several rows chains, each decision taken against the
/// previous <em>physical</em> row (its length is what the server measured), never against the joined line.
/// </para>
/// <para>
/// It is a stream: a line that could be continued is held until the next one says whether it is. The
/// session releases a held line after a short quiet period too (<c>WorldSession.ReflowHold</c>), so only
/// the last full row of a burst is ever delayed — the last row of a paragraph is short, and a short row is
/// never held.
/// </para>
/// </summary>
public sealed class ServerWrapJoiner
{
    /// <summary>How many recent lines the wrap column is estimated from.</summary>
    public const int SampleWindow = 128;

    /// <summary>How many non-blank lines must have been seen before anything is joined.</summary>
    public const int MinimumSamples = 8;

    /// <summary>Below this a "wrap column" is short output, not wrapping, and nothing is joined.</summary>
    public const int MinimumWrapColumn = 40;

    private readonly Queue<int> _lengths = new();
    private StyledLine? _held;
    private StyledLine? _lastPhysical;

    /// <summary>The wrap column the stream has shown so far, or null while it is unknown.</summary>
    public int? WrapColumn
    {
        get
        {
            if (_lengths.Count < MinimumSamples)
            {
                return null;
            }

            var third = _lengths.OrderByDescending(l => l).Skip(2).First();
            return third >= MinimumWrapColumn ? third : null;
        }
    }

    /// <summary>How far short of the wrap column a row may end and still be called full.</summary>
    public static int Slack(int wrapColumn) => Math.Max(8, wrapColumn / 6);

    /// <summary>Whether a line is waiting for the next one to say whether it continues.</summary>
    public bool Holding => _held is not null;

    /// <summary>
    /// Takes the next line of the stream and returns the lines that are now final, in order: none while a
    /// line is held, the held line (joined or not) once it is settled, and then possibly this one.
    /// </summary>
    public IReadOnlyList<StyledLine> Push(StyledLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        Observe(line);

        var ready = new List<StyledLine>(2);
        if (_held is { } held && _lastPhysical is { } last)
        {
            if (WrapColumn is { } column && Continues(last, line, column))
            {
                _held = Join(held, line);
                _lastPhysical = line;
                if (!IsFull(line, column))
                {
                    ready.Add(_held);
                    _held = null;
                    _lastPhysical = null;
                }

                return ready;
            }

            ready.Add(held);
            _held = null;
            _lastPhysical = null;
        }

        if (WrapColumn is { } wrap && IsFull(line, wrap))
        {
            _held = line;
            _lastPhysical = line;
        }
        else
        {
            ready.Add(line);
        }

        return ready;
    }

    /// <summary>Releases the held line, if there is one: the stream went quiet, or something else is about to be shown.</summary>
    public StyledLine? Flush()
    {
        var held = _held;
        _held = null;
        _lastPhysical = null;
        return held;
    }

    /// <summary>
    /// Whether <paramref name="line"/> is a row the server filled: the conditions that are about the first
    /// line alone (items 2–5 of the rule, less the next word's fit).
    /// </summary>
    public static bool IsFull(StyledLine line, int wrapColumn)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (!IsPlainProse(line))
        {
            return false;
        }

        var text = line.Text.TrimEnd();
        if (text.Length > wrapColumn || text.Length < wrapColumn - Slack(wrapColumn))
        {
            return false;
        }

        var last = text[^1];
        return char.IsLetterOrDigit(last) || last == ',';
    }

    /// <summary>
    /// Whether <paramref name="next"/> continues <paramref name="previous"/>, a row already known to be
    /// full: the conditions that need both lines (items 4, 6 and 7 of the rule).
    /// </summary>
    public static bool Continues(StyledLine previous, StyledLine next, int wrapColumn)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        if (!IsFull(previous, wrapColumn) || !IsPlainProse(next))
        {
            return false;
        }

        var text = next.Text;
        if (!char.IsLower(text[0]))
        {
            return false;
        }

        // The next line's first word would not have fitted on the previous one, so the server had to break.
        // One cell of tolerance, because the column is the longest row seen, which can fall a cell short of
        // the server's own.
        var firstWord = text.IndexOf(' ') is var space and >= 0 ? space : text.TrimEnd().Length;
        if (previous.Text.TrimEnd().Length + 1 + firstWord < wrapColumn)
        {
            return false;
        }

        return LastStyle(previous) == next.Spans[0].Style;
    }

    /// <summary>One line made of two, with a single space where the server broke it.</summary>
    public static StyledLine Join(StyledLine previous, StyledLine next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);

        var spans = new List<StyledSpan>(previous.Spans.Count + next.Spans.Count + 1);
        spans.AddRange(previous.Spans);

        // Trailing whitespace off the first line, so the join is exactly one space whatever the server padded with.
        while (spans.Count > 0 && spans[^1].Text.TrimEnd() is var trimmed && trimmed.Length < spans[^1].Text.Length)
        {
            var span = spans[^1];
            spans.RemoveAt(spans.Count - 1);
            if (trimmed.Length > 0)
            {
                spans.Add(new StyledSpan(trimmed, span.Style, span.Interaction));
                break;
            }
        }

        var style = spans.Count > 0 ? spans[^1].Style : TextStyle.Default;
        spans.Add(new StyledSpan(" ", style));
        spans.AddRange(next.Spans);
        return new StyledLine(spans, previous.RuleColor, previous.IsPrompt);
    }

    /// <summary>
    /// Records a line's length towards the wrap column. Blank lines say nothing about it. <see cref="Push"/> does this
    /// itself; a session that is not joining calls it alone, so the wrap column is known before the player turns joining on.
    /// </summary>
    public void Observe(StyledLine line)
    {
        var length = line.Text.TrimEnd().Length;
        if (length == 0 || line.IsPrompt)
        {
            return;
        }

        _lengths.Enqueue(length);
        while (_lengths.Count > SampleWindow)
        {
            _lengths.Dequeue();
        }
    }

    /// <summary>Item 2 and 3 of the rule: an ordinary run of text, not a prompt, a structure or a column layout.</summary>
    private static bool IsPlainProse(StyledLine line)
    {
        if (line.IsPrompt || line.RuleColor is not null || line.Spans.Count == 0)
        {
            return false;
        }

        foreach (var span in line.Spans)
        {
            if (span.IsInteractive)
            {
                return false;
            }
        }

        var text = line.Text;
        var start = 0;
        while (start < text.Length && text[start] == ' ')
        {
            start++;
        }

        var body = text.AsSpan(start).TrimEnd();
        return body.Length > 0 && body.IndexOf("   ", StringComparison.Ordinal) < 0;
    }

    /// <summary>The style of the last visible character of a line.</summary>
    private static TextStyle LastStyle(StyledLine line)
    {
        for (var i = line.Spans.Count - 1; i >= 0; i--)
        {
            if (line.Spans[i].Text.TrimEnd().Length > 0)
            {
                return line.Spans[i].Style;
            }
        }

        return line.Spans[^1].Style;
    }
}
