namespace SharpMUTerm.Core.Text;

/// <summary>
/// Finds the URLs a server printed as plain text and makes them clickable, by giving the characters
/// they cover a <see cref="SpanInteraction"/> of their own. Nothing else about the line changes: the
/// text is identical, every character keeps its style, and only the span boundaries move.
/// <para>
/// <strong>This exists because the terminal's own URL detection cannot survive a pane.</strong> Kitty,
/// WezTerm and Ghostty all find URLs in the cells they are painting and make them clickable — over the
/// whole terminal row. An output pane is narrower than the row, so a long URL wraps, and the emulator
/// then sees <c>https://exa</c> on one row and <c>mple.com/page</c> on the next with a divider and
/// possibly another pane's output between them. Neither half is a URL, so neither is clickable, and the
/// failure is silent. Marking the span ourselves moves the decision to the one layer that knows where
/// the line really ends: <c>MarkupParser</c> splits a <c>[link=…]</c> span across every row it wraps
/// onto and <c>MarkupControl</c> hit-tests each row, so a click anywhere in the URL lands.
/// </para>
/// <para>
/// It runs over the <em>whole line</em> rather than span by span, because a server is free to change
/// colour in the middle of a URL — a highlight trigger recolouring one word, or a game that paints the
/// scheme differently from the host. Matching per span would find two half-URLs and produce two links
/// to two truncated targets, which is the same defect as the wrap one layer down. The line's own
/// <see cref="StyledLine.Text"/> is the subject; the spans are rebuilt around what it found.
/// </para>
/// </summary>
public static class UrlDetector
{
    /// <summary>
    /// The longest target this will produce. A line is bounded but not short, and a link is a thing
    /// handed to another application — a server that prints ten kilobytes of URL-shaped bytes should
    /// get no link at all rather than one nothing can act on sensibly.
    /// </summary>
    public const int MaxTargetLength = 2048;

    /// <summary>
    /// The two schemes recognised, written out in full. Deliberately not a general "scheme:" rule and
    /// deliberately not <c>www.</c>: what this produces is eventually handed to the desktop's URL
    /// handler, so the set of things it can name is a security property rather than a convenience. A
    /// detector that could emit <c>file:</c>, <c>javascript:</c> or an OS-registered scheme like
    /// <c>ms-msdt:</c> would let a world choose which application runs. See the app's own scheme gate
    /// at the moment of opening, which re-checks this for links the *server* marked up.
    /// </summary>
    private static readonly string[] Schemes = ["http://", "https://"];

    /// <summary>
    /// Trailing characters a URL gives back to the sentence around it. A URL at the end of a sentence
    /// is the common case and eating the full stop into the target is the classic version of this bug;
    /// the closing brackets are given back only when unbalanced, so a link with parentheses in its path
    /// (a wiki article, most often) keeps them.
    /// </summary>
    private const string TrailingPunctuation = ".,;:!?'\"";

    /// <summary>
    /// Returns <paramref name="line"/> with every plain-text <c>http(s)</c> URL marked clickable, or the
    /// same line when there is nothing to mark.
    /// <para>
    /// A run overlapping a character that is already interactive is skipped: a server's own
    /// <c>&lt;SEND&gt;</c> or <c>&lt;A HREF&gt;</c> said what that text does, and this may neither
    /// replace it nor wrap it in a second link. What MXP marked up is MXP's.
    /// </para>
    /// </summary>
    public static StyledLine ApplyToLine(StyledLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.IsEmpty)
        {
            return line;
        }

        var matches = Find(line);
        return matches.Count == 0 ? line : Rebuild(line, matches);
    }

    /// <summary>
    /// The URLs in a line, as half-open character ranges over <see cref="StyledLine.Text"/>, in the
    /// order they appear. Public so the detection can be asserted directly — a test that had to read
    /// span boundaries back out to find out what matched would be testing two things at once.
    /// </summary>
    public static IReadOnlyList<UrlMatch> Find(StyledLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var matches = new List<UrlMatch>();
        var text = line.Text;
        var index = 0;

        while (index < text.Length)
        {
            var start = NextSchemeStart(text, index);
            if (start < 0)
            {
                break;
            }

            var end = EndOfUrl(text, start);
            index = end;

            // A scheme with nothing after it is not a URL, only the word "https://" in a sentence.
            var scheme = SchemeAt(text, start)!;
            if (end - start <= scheme.Length || end - start > MaxTargetLength)
            {
                continue;
            }

            if (!IsFreeOfInteraction(line, start, end))
            {
                continue;
            }

            matches.Add(new UrlMatch(start, end, text[start..end]));
        }

        return matches;
    }

    /// <summary>
    /// The index of the next scheme, at a boundary. The boundary check is what stops
    /// <c>xhttp://…</c> and, more usefully, stops a second match being found inside a URL that itself
    /// contains one (a redirector's <c>?url=https://…</c> is part of the first link, not a link of its
    /// own).
    /// </summary>
    private static int NextSchemeStart(string text, int from)
    {
        for (var i = from; i < text.Length; i++)
        {
            if (SchemeAt(text, i) is null)
            {
                continue;
            }

            if (i == 0 || !IsUrlBodyChar(text[i - 1]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The scheme starting at <paramref name="index"/>, case-insensitively, or null.</summary>
    private static string? SchemeAt(string text, int index)
    {
        foreach (var scheme in Schemes)
        {
            if (index + scheme.Length <= text.Length &&
                string.Compare(text, index, scheme, 0, scheme.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return scheme;
            }
        }

        return null;
    }

    /// <summary>
    /// Where the URL starting at <paramref name="start"/> ends: the first character that cannot be in
    /// one, then trailing punctuation and unbalanced closers handed back to the surrounding text.
    /// </summary>
    private static int EndOfUrl(string text, int start)
    {
        var end = start;
        while (end < text.Length && IsUrlBodyChar(text[end]))
        {
            end++;
        }

        while (end > start)
        {
            var last = text[end - 1];
            if (TrailingPunctuation.Contains(last, StringComparison.Ordinal))
            {
                end--;
                continue;
            }

            if (last is ')' or ']' or '}' && !IsBalanced(text, start, end, last))
            {
                end--;
                continue;
            }

            break;
        }

        return end;
    }

    /// <summary>
    /// Whether the closer at the end of <c>[start, end)</c> has an opener inside the same range — the
    /// test that keeps <c>…/Nested_(disambiguation)</c> whole while giving back the <c>)</c> of
    /// <c>(see https://example.com/page)</c>.
    /// </summary>
    private static bool IsBalanced(string text, int start, int end, char closer)
    {
        var opener = closer switch { ')' => '(', ']' => '[', _ => '{' };
        var depth = 0;
        for (var i = start; i < end; i++)
        {
            if (text[i] == opener)
            {
                depth++;
            }
            else if (text[i] == closer)
            {
                depth--;
            }
        }

        return depth >= 0;
    }

    /// <summary>
    /// Whether a character can sit inside a URL. Whitespace and control characters end one; so does
    /// anything RFC 3986 does not allow unescaped, which is what keeps a URL out of the quotation marks,
    /// angle brackets and backticks a game puts around it.
    /// </summary>
    private static bool IsUrlBodyChar(char c)
    {
        if (char.IsWhiteSpace(c) || char.IsControl(c))
        {
            return false;
        }

        return !(c is '"' or '\'' or '<' or '>' or '`' or '|' or '\\' or '^' or '{' or '}');
    }

    /// <summary>Whether every character of <c>[start, end)</c> is plain — nothing already clickable.</summary>
    private static bool IsFreeOfInteraction(StyledLine line, int start, int end)
    {
        var offset = 0;
        foreach (var span in line.Spans)
        {
            var spanEnd = offset + span.Length;
            if (span.Interaction is not null && offset < end && spanEnd > start)
            {
                return false;
            }

            offset = spanEnd;
        }

        return true;
    }

    /// <summary>
    /// Rebuilds the line's spans so each match's characters carry its interaction. Spans are split at
    /// match edges and nowhere else, so a URL crossing a colour change becomes two spans sharing one
    /// interaction — two pieces of one link, which is exactly what the renderer needs to keep both
    /// colours and still hit-test as a single target.
    /// </summary>
    private static StyledLine Rebuild(StyledLine line, IReadOnlyList<UrlMatch> matches)
    {
        var spans = new List<StyledSpan>(line.Spans.Count + (matches.Count * 2));
        var offset = 0;

        foreach (var span in line.Spans)
        {
            var spanStart = offset;
            var spanEnd = offset + span.Length;
            offset = spanEnd;

            var cut = spanStart;
            foreach (var match in matches)
            {
                if (match.End <= cut || match.Start >= spanEnd)
                {
                    continue;
                }

                var from = Math.Max(match.Start, cut);
                var to = Math.Min(match.End, spanEnd);

                if (from > cut)
                {
                    spans.Add(new StyledSpan(span.Text[(cut - spanStart)..(from - spanStart)], span.Style, span.Interaction));
                }

                spans.Add(new StyledSpan(
                    span.Text[(from - spanStart)..(to - spanStart)],
                    span.Style,
                    SpanInteraction.Link(match.Url)));
                cut = to;
            }

            if (cut < spanEnd)
            {
                spans.Add(new StyledSpan(span.Text[(cut - spanStart)..], span.Style, span.Interaction));
            }
        }

        return new StyledLine(spans, line.RuleColor);
    }
}

/// <summary>One detected URL: where it sits in the line's text, and the target it names.</summary>
/// <param name="Start">Index of the first character, inclusive.</param>
/// <param name="End">Index one past the last character.</param>
/// <param name="Url">The target — always exactly the text between those two indices.</param>
public readonly record struct UrlMatch(int Start, int End, string Url);
