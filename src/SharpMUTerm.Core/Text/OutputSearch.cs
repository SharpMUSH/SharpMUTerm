using System.Text.RegularExpressions;

namespace SharpMUTerm.Core.Text;

/// <summary>
/// One line the search surface lists: where it sits in the buffer it came from, its text, and where the
/// query landed inside it.
/// </summary>
/// <param name="LineIndex">Its index in the list handed to <see cref="OutputSearch.Match"/>.</param>
/// <param name="Text">The line, verbatim.</param>
/// <param name="MatchStart">Where the query matched.</param>
/// <param name="MatchLength">How long the matched run is.</param>
public readonly record struct OutputMatch(int LineIndex, string Text, int MatchStart, int MatchLength);

/// <summary>
/// What one search came to: the lines it found, and — when the query could not be used at all — why.
/// </summary>
/// <param name="Matches">The matching lines, in the buffer's own order.</param>
/// <param name="Error">Why nothing could be searched for, or null when the query was usable.</param>
public readonly record struct OutputSearchResult(IReadOnlyList<OutputMatch> Matches, string? Error);

/// <summary>
/// Finds a query in a window's output. The matching half of ⌃F, and the whole of it that can be
/// reasoned about without a terminal.
/// <para>
/// <b>It takes plain text.</b> Nothing here knows about panes, markup or windows: the caller strips its
/// own lines and hands over strings. That is what keeps this in Core, and it is also the rule that makes
/// a match mean what it looks like — a colour tag in the middle of a word must not split a match, and a
/// reader must not be able to search for <c>#ff0000</c> and find every red line.
/// </para>
/// <para>
/// <b>Case is ignored, in both modes.</b> <see cref="Input.HistorySearch"/> ignores it too, and two
/// search surfaces in one client disagreeing about case is a bug report waiting to happen. Regex mode
/// says <c>(?-i)</c> inline when it wants otherwise, which is a documented .NET feature rather than one
/// invented here — and is why there is no third toggle on the surface.
/// </para>
/// <para>
/// <b>One match per line, the first.</b> The result is a list of lines to <em>go to</em>; the offsets
/// exist so a row can show why it is listed, exactly as <c>HistorySearchPrompt.Row</c> uses them.
/// </para>
/// <para>
/// <b>An empty query matches nothing</b>, which is where this parts company with
/// <see cref="Input.HistorySearch"/>: there an empty query is the opening chronological list, and a
/// command history is short enough to be one. A pane buffer is thousands of lines, and "everything,
/// oldest first" is not a result set anybody asked for — it is the pane they are already looking at.
/// </para>
/// </summary>
public static class OutputSearch
{
    /// <summary>
    /// The longest query this will look for. A search box is not a text editor, and an unbounded query
    /// is an unbounded pattern compiled on every keystroke.
    /// </summary>
    public const int MaxQueryLength = 200;

    /// <summary>
    /// How long one regex is given against one line. This runs on the UI thread, on every keystroke,
    /// over every line of every window — so a pattern that backtracks catastrophically has to come back
    /// as an error rather than wedge the client.
    /// </summary>
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// The lines of <paramref name="lines"/> matching <paramref name="query"/>, in the order they were
    /// given — the buffer's own, because these rows are a transcript rather than a ranking and the reader
    /// is looking for a place in it.
    /// </summary>
    /// <param name="lines">The plain text of each line, oldest first.</param>
    /// <param name="query">What to look for; empty finds nothing.</param>
    /// <param name="regex">Whether <paramref name="query"/> is a pattern rather than literal text.</param>
    public static OutputSearchResult Match(IReadOnlyList<string> lines, string query, bool regex)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(query);

        if (query.Length == 0)
        {
            return new OutputSearchResult(Array.Empty<OutputMatch>(), null);
        }

        if (query.Length > MaxQueryLength)
        {
            return new OutputSearchResult(
                Array.Empty<OutputMatch>(), $"query is longer than {MaxQueryLength} characters");
        }

        return regex ? ByPattern(lines, query) : ByText(lines, query);
    }

    private static OutputSearchResult ByText(IReadOnlyList<string> lines, string query)
    {
        var matches = new List<OutputMatch>();
        for (var index = 0; index < lines.Count; index++)
        {
            var text = lines[index] ?? string.Empty;
            var at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                matches.Add(new OutputMatch(index, text, at, query.Length));
            }
        }

        return new OutputSearchResult(matches, null);
    }

    private static OutputSearchResult ByPattern(IReadOnlyList<string> lines, string query)
    {
        Regex pattern;
        try
        {
            pattern = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        }
        catch (ArgumentException error)
        {
            // The whole of what "invalid pattern" means here. A regex is typed one character at a time,
            // so most of the time a regex query is being typed it is unparseable; the surface says so
            // and lists nothing, which is a state rather than a failure.
            return new OutputSearchResult(Array.Empty<OutputMatch>(), error.Message);
        }

        var matches = new List<OutputMatch>();
        for (var index = 0; index < lines.Count; index++)
        {
            var text = lines[index] ?? string.Empty;
            try
            {
                var found = pattern.Match(text);

                // A zero-width match marks nothing, so it lists nothing: `x*` matches at position 0 of
                // every line in the buffer, and a result set of "everything, highlighted nowhere" is
                // worse than no result at all.
                if (found.Success && found.Length > 0)
                {
                    matches.Add(new OutputMatch(index, text, found.Index, found.Length));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Reported against the whole search rather than skipping the line: a pattern that can
                // take this long on one line will take it on the next, and a partial result set that
                // silently omitted the expensive lines would be a search lying about what it found.
                return new OutputSearchResult(
                    Array.Empty<OutputMatch>(), "pattern took too long — try a simpler one");
            }
        }

        return new OutputSearchResult(matches, null);
    }
}
