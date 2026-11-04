using System.Text.RegularExpressions;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Automation;

/// <summary>
/// What one trigger pattern comes to against the output the client is holding: how many lines it
/// matched, the newest few of them, and — when the pattern cannot be used yet — why.
/// </summary>
/// <param name="Tested">How many lines were tried.</param>
/// <param name="Matched">How many of them matched.</param>
/// <param name="Samples">The newest matches, newest first, at most the number asked for.</param>
/// <param name="Problem">Why nothing could be tried, or null when the pattern was usable.</param>
public readonly record struct TriggerPreviewResult(
    int Tested, int Matched, IReadOnlyList<OutputMatch> Samples, string? Problem);

/// <summary>
/// The F2 editor's live test: a pattern run over recent output on every keystroke. The trigger engine's
/// rules, so a count here is what the rule would have done — the same regex
/// (<see cref="TriggerPatterns.ToRegex"/>), the same case rule, and a zero-width match counting, because
/// the engine fires on one. <see cref="OutputSearch"/>'s safeguards, because this too runs on the UI
/// thread per keystroke: a bounded number of lines, a match timeout, and an unparseable pattern returned as
/// a <em>state</em> — a regex is unparseable most of the time it is being typed.
/// </summary>
public static class TriggerPreview
{
    /// <summary>How many of the newest lines are tried.</summary>
    public const int MaxLines = 2000;

    /// <summary>How many matching lines are kept to show.</summary>
    public const int DefaultSamples = 3;

    /// <summary>Runs <paramref name="pattern"/>, read as <paramref name="type"/>, over the newest of <paramref name="lines"/>.</summary>
    /// <param name="lines">Plain text, oldest first.</param>
    public static TriggerPreviewResult Run(
        IReadOnlyList<string> lines,
        TriggerMatchType type,
        string pattern,
        bool caseSensitive,
        int samples = DefaultSamples)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(pattern);

        var first = Math.Max(0, lines.Count - MaxLines);
        var tested = lines.Count - first;

        if (TriggerPatterns.Validate(type, pattern) is { } problem)
        {
            return new TriggerPreviewResult(tested, 0, Array.Empty<OutputMatch>(), problem);
        }

        Regex regex;
        try
        {
            regex = new Regex(
                TriggerPatterns.ToRegex(type, pattern),
                RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
                OutputSearch.MatchTimeout);
        }
        catch (ArgumentException error)
        {
            return new TriggerPreviewResult(tested, 0, Array.Empty<OutputMatch>(), error.Message.Split('\n')[0].Trim());
        }

        var matched = 0;
        var kept = new List<OutputMatch>();
        for (var index = lines.Count - 1; index >= first; index--)
        {
            var text = lines[index] ?? string.Empty;
            try
            {
                var found = regex.Match(text);
                if (!found.Success)
                {
                    continue;
                }

                matched++;
                if (kept.Count < samples)
                {
                    kept.Add(new OutputMatch(index, text, found.Index, found.Length));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // OutputSearch's reasoning: a count that silently skipped the expensive lines would be a
                // test lying about what the rule would do.
                return new TriggerPreviewResult(
                    tested, 0, Array.Empty<OutputMatch>(), "pattern took too long — try a simpler one");
            }
        }

        return new TriggerPreviewResult(tested, matched, kept, null);
    }
}
