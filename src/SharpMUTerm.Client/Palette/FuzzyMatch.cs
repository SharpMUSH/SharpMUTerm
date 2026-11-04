namespace SharpMUTerm.Client.Palette;

/// <summary>A match of a query against one piece of text: how good it is, and which characters it used.</summary>
/// <param name="Score">Higher is better. Only comparable between matches against texts of the same kind.</param>
/// <param name="Positions">The indices in the text of every matched character, ascending.</param>
public sealed record FuzzyResult(int Score, IReadOnlyList<int> Positions);

/// <summary>
/// Subsequence matching for the command surface. Every character of the query must appear in the text, in
/// order, ignoring case; a query with spaces is several words, each matched in order after the one before it,
/// so <c>trig sel</c> finds <em>Make a trigger from the selection</em>.
/// <para>
/// The score rewards what a reader means when they type a few letters: a character at the start of a word
/// (<see cref="WordStart"/>), one straight after the previous match (<see cref="Consecutive"/>), and the very
/// first character of the text (<see cref="TextStart"/>); it charges for each character skipped between two
/// matches (<see cref="GapPenalty"/>, capped per gap so one long skip is not worse than several short ones),
/// and adds <see cref="WholePrefix"/> when the text simply starts with the query. So <c>lay</c> scores
/// <em>Layouts</em> (a whole prefix) above <em>Clear window</em>'s scattered l-a-y, and <c>fp</c> prefers
/// <em>Freeze pane</em> (two word starts) to anything that merely contains an f and a p.
/// </para>
/// <para>
/// Each word is aligned by dynamic programming over every place its characters could land, so the best
/// alignment is found rather than the leftmost; if aligning the words one by one leaves a later word nowhere
/// to go, the earliest-ending alignment of each word in turn is scored instead.
/// </para>
/// <para>
/// <b>A short word may not scatter.</b> A query word of <see cref="StrictLength"/> letters or fewer matches only
/// where each of its letters begins a word of the text or follows the one before it: <c>trig</c> finds
/// <em>Make a trigger…</em> and <em>fp</em> still finds <em>Freeze pane</em>, but <c>trig</c> no longer finds
/// <em>Stop reading this window aloud</em> (a t, r, i and g spread over four words) or <em>Start logging</em>. A few
/// letters is a prefix or an acronym; scattered across a title they match nearly everything. A longer word keeps the
/// plain subsequence rule, where scattered letters are rare enough to mean something (<c>tstamp</c>).
/// </para>
/// </summary>
public static class FuzzyMatch
{
    /// <summary>Every matched character.</summary>
    public const int Matched = 1;

    /// <summary>A matched character that begins a word.</summary>
    public const int WordStart = 8;

    /// <summary>A matched character immediately after the previous one.</summary>
    public const int Consecutive = 6;

    /// <summary>A match on the text's first character.</summary>
    public const int TextStart = 4;

    /// <summary>Charged per character skipped between two matched characters of one word.</summary>
    public const int GapPenalty = 1;

    /// <summary>The most one gap is charged.</summary>
    public const int MaxGapPenalty = 4;

    /// <summary>Added when the text starts with the whole query, spaces and all.</summary>
    public const int WholePrefix = 20;

    /// <summary>A query word this long or shorter matches only at word starts and runs (see the class remarks).</summary>
    public const int StrictLength = 4;

    /// <summary>
    /// Matches <paramref name="query"/> against <paramref name="text"/>, or returns null when it is not a
    /// subsequence of it. An empty or blank query matches everything with a score of zero.
    /// </summary>
    public static FuzzyResult? Match(string query, string text)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(text);

        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return new FuzzyResult(0, Array.Empty<int>());
        }

        var lower = text.ToLowerInvariant();
        var positions = new List<int>();
        var score = 0;
        var from = 0;
        var aligned = true;
        foreach (var word in words)
        {
            var best = AlignWord(word.ToLowerInvariant(), lower, text, from, word.Length <= StrictLength, earliest: false);
            if (best is null)
            {
                aligned = false;
                break;
            }

            score += best.Value.Score;
            positions.AddRange(best.Value.Positions);
            from = best.Value.Positions[^1] + 1;
        }

        if (!aligned)
        {
            // A word aligned for its own best score can leave the next one nowhere to go; the earliest-ending
            // alignment of every word in turn leaves each later word the most room, so it finds a match whenever
            // there is one.
            positions.Clear();
            score = 0;
            from = 0;
            foreach (var word in words)
            {
                var early = AlignWord(word.ToLowerInvariant(), lower, text, from, word.Length <= StrictLength, earliest: true);
                if (early is null)
                {
                    return null;
                }

                score += early.Value.Score;
                positions.AddRange(early.Value.Positions);
                from = early.Value.Positions[^1] + 1;
            }
        }

        var normalised = string.Join(' ', words).ToLowerInvariant();
        if (lower.StartsWith(normalised, StringComparison.Ordinal))
        {
            score += WholePrefix;
        }

        return new FuzzyResult(score, positions);
    }

    /// <summary>Whether index <paramref name="i"/> of <paramref name="text"/> begins a word.</summary>
    public static bool IsWordStart(string text, int i)
    {
        if (i == 0)
        {
            return true;
        }

        var previous = text[i - 1];
        var current = text[i];
        return (!char.IsLetterOrDigit(previous) && char.IsLetterOrDigit(current))
            || (char.IsLower(previous) && char.IsUpper(current));
    }

    private static int CharScore(string text, int i) =>
        Matched + (IsWordStart(text, i) ? WordStart : 0) + (i == 0 ? TextStart : 0);

    private static int Link(int previous, int next) =>
        next == previous + 1 ? Consecutive : -Math.Min(MaxGapPenalty, (next - previous - 1) * GapPenalty);

    // Scored against the original text, so a capital inside a word (camelCase) counts as a word start.
    // ToLowerInvariant maps char for char, so an index in the lowered text is the same index here.
    private static (int Score, int[] Positions)? AlignWord(string word, string text, string original, int from, bool strict, bool earliest)
    {
        var m = word.Length;
        var n = text.Length;
        if (m == 0 || n - from < m)
        {
            return null;
        }

        // best[i, j]: the best score for word[0..i] with word[i] matched at text[j]; back[i, j] the j of word[i-1].
        var best = new int[m, n];
        var back = new int[m, n];
        const int none = int.MinValue;
        for (var i = 0; i < m; i++)
        {
            for (var j = 0; j < n; j++)
            {
                best[i, j] = none;
            }
        }

        for (var j = from; j < n; j++)
        {
            if (text[j] == word[0] && (!strict || IsWordStart(original, j)))
            {
                best[0, j] = CharScore(original, j);
                back[0, j] = -1;
            }
        }

        for (var i = 1; i < m; i++)
        {
            for (var j = from + i; j < n; j++)
            {
                if (text[j] != word[i])
                {
                    continue;
                }

                var wordStart = IsWordStart(original, j);
                var top = none;
                var arg = -1;
                for (var k = from + i - 1; k < j; k++)
                {
                    if (best[i - 1, k] == none || (strict && !wordStart && k != j - 1))
                    {
                        continue;
                    }

                    var candidate = best[i - 1, k] + Link(k, j);
                    if (candidate > top)
                    {
                        top = candidate;
                        arg = k;
                    }
                }

                if (arg >= 0)
                {
                    best[i, j] = top + CharScore(original, j);
                    back[i, j] = arg;
                }
            }
        }

        var end = -1;
        var score = none;
        for (var j = from; j < n; j++)
        {
            // Strictly greater, so the earliest of equal endings wins and later words keep the most room; asked for
            // the earliest outright, the first ending at all.
            if (best[m - 1, j] != none && best[m - 1, j] > score)
            {
                score = best[m - 1, j];
                end = j;
                if (earliest)
                {
                    break;
                }
            }
        }

        if (end < 0)
        {
            return null;
        }

        var positions = new int[m];
        for (int i = m - 1, j = end; i >= 0; i--)
        {
            positions[i] = j;
            j = back[i, j];
        }

        return (score, positions);
    }
}
