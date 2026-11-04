namespace SharpMUTerm.Core.Text;

/// <summary>One line of a session's scrollback the deep search found: its absolute index, the line, and where it matched.</summary>
/// <param name="Index">The line's absolute index in the <see cref="ScrollbackBuffer"/> (stable while it is held).</param>
/// <param name="Line">The line itself — the ring's own instance while it is in memory, a decoded copy from the spill.</param>
/// <param name="MatchStart">Where the query matched in <see cref="StyledLine.Text"/>.</param>
/// <param name="MatchLength">How long the matched run is.</param>
public readonly record struct ScrollbackMatch(long Index, StyledLine Line, int MatchStart, int MatchLength);

/// <summary>Where a deep search has got to: what it found so far, how far it has read, and whether it has finished.</summary>
/// <param name="Found">Every match so far, newest first.</param>
/// <param name="Searched">How many lines have been read.</param>
/// <param name="Total">How many lines there were to read when it started.</param>
/// <param name="Done">Whether the walk has ended (finished, capped, failed — not cancelled).</param>
/// <param name="Capped">Whether it stopped at <see cref="ScrollbackSearch.MaxHits"/>.</param>
/// <param name="Error">Why the query could not be used, or null.</param>
public sealed record ScrollbackSearchProgress(
    IReadOnlyList<ScrollbackMatch> Found, long Searched, long Total, bool Done, bool Capped, string? Error);

/// <summary>
/// ⌃F's "search older history" step (⌥H): the session's whole <see cref="ScrollbackBuffer"/> — the in-memory ring and
/// the file-backed spill behind it — walked from the newest line back to the oldest in ranged reads of at most
/// <see cref="ScrollbackBuffer.MaxRangeLines"/>, each matched by <see cref="OutputSearch.Match"/> (so case, regex mode,
/// the query bound and the per-line match timeout are ⌃F's exactly).
/// <para>
/// <b>It runs off the UI thread</b>: a spill read is disk I/O, and tens of thousands of lines are more than one keystroke
/// can afford. The caller hands a token and gets <paramref name="progress"/> after every chunk, so results stream into the
/// surface as they are found; a cancelled walk reports nothing further.
/// </para>
/// <para>
/// <b>Lines the pane still holds are skipped</b> (<paramref name="skip"/>): the ordinary ⌃F rows already list them, and a
/// session window's pane shares the ring's instances, so the caller can say which by reference.
/// </para>
/// </summary>
public static class ScrollbackSearch
{
    /// <summary>The most hits one deep search keeps; past it the walk stops and says so.</summary>
    public const int MaxHits = 500;

    /// <summary>
    /// Walks <paramref name="buffer"/> from its newest line back to its oldest for <paramref name="query"/>.
    /// </summary>
    /// <param name="buffer">The session's scrollback.</param>
    /// <param name="query">What to look for; empty finds nothing.</param>
    /// <param name="regex">Whether it is a pattern.</param>
    /// <param name="skip">Lines not to list (the pane still holds them); null skips none.</param>
    /// <param name="progress">Told after every chunk, on the walking thread.</param>
    /// <param name="cancel">Ends the walk between chunks.</param>
    /// <param name="chunk">Lines per ranged read; at most <see cref="ScrollbackBuffer.MaxRangeLines"/>.</param>
    /// <returns>The last progress reported, or null when cancelled.</returns>
    public static ScrollbackSearchProgress? Run(
        ScrollbackBuffer buffer,
        string query,
        bool regex,
        Func<StyledLine, bool>? skip,
        Action<ScrollbackSearchProgress>? progress,
        CancellationToken cancel,
        int chunk = ScrollbackBuffer.MaxRangeLines)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(query);
        chunk = Math.Clamp(chunk, 1, ScrollbackBuffer.MaxRangeLines);

        var end = buffer.TotalLines;
        var oldest = buffer.OldestIndex;
        var total = Math.Max(0, end - oldest);
        var found = new List<ScrollbackMatch>();
        long searched = 0;

        if (query.Length == 0)
        {
            return Report(new ScrollbackSearchProgress([], 0, total, true, false, null));
        }

        var cursor = end;
        while (cursor > oldest)
        {
            if (cancel.IsCancellationRequested)
            {
                return null;
            }

            // The oldest line may move up while this walks (the spill trims its front); a range that starts below it
            // comes back short from the front, so each line's index is counted back from the end of the range.
            var start = Math.Max(oldest, cursor - chunk);
            var count = (int)(cursor - start);
            var lines = buffer.GetRange(start, count);
            var first = cursor - lines.Count;
            var texts = new string[lines.Count];
            for (var i = 0; i < lines.Count; i++)
            {
                texts[i] = skip?.Invoke(lines[i]) == true ? string.Empty : lines[i].Text;
            }

            var result = OutputSearch.Match(texts, query, regex);
            if (result.Error is { } error)
            {
                return Report(new ScrollbackSearchProgress([], searched, total, true, false, error));
            }

            var capped = false;
            for (var m = result.Matches.Count - 1; m >= 0; m--)
            {
                var match = result.Matches[m];
                found.Add(new ScrollbackMatch(first + match.LineIndex, lines[match.LineIndex], match.MatchStart, match.MatchLength));
                if (found.Count >= MaxHits)
                {
                    capped = true;
                    break;
                }
            }

            searched += count;
            cursor = start;
            if (capped)
            {
                return Report(new ScrollbackSearchProgress(found.ToArray(), searched, total, true, true, null));
            }

            if (cancel.IsCancellationRequested)
            {
                return null;
            }

            progress?.Invoke(new ScrollbackSearchProgress(found.ToArray(), searched, total, cursor <= oldest, false, null));
        }

        var last = new ScrollbackSearchProgress(found.ToArray(), searched, total, true, false, null);
        return searched == 0 ? Report(last) : last;

        ScrollbackSearchProgress Report(ScrollbackSearchProgress done)
        {
            progress?.Invoke(done);
            return done;
        }
    }
}
