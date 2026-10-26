namespace SharpMUTerm.Tui;

/// <summary>
/// ⇥ on the command line completes the word before the caret from what the connection the bar sends to has
/// seen (<see cref="Core.Commands.CompletionIndex"/>), and ⇥ again cycles through the rest, back round to what
/// was typed.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>
    /// The cycle ⇥ is in: the text before the word, the choices (what was typed last), which one is shown,
    /// and the whole text as it was left. Any edit makes the text differ, which ends the cycle.
    /// </summary>
    private (string Before, IReadOnlyList<string> Choices, int Index, string Placed)? _completion;

    private string? CompleteWord(string text)
    {
        if (_completion is { } cycle && string.Equals(cycle.Placed, text, StringComparison.Ordinal))
        {
            var next = (cycle.Index + 1) % cycle.Choices.Count;
            var placed = cycle.Before + cycle.Choices[next];
            _completion = cycle with { Index = next, Placed = placed };
            return placed;
        }

        _completion = null;
        var start = text.Length;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '\'' or '-'))
        {
            start--;
        }

        var prefix = text[start..];
        if (prefix.Length == 0 || SendTarget()?.Completions.Candidates(prefix) is not { Count: > 0 } found)
        {
            return null;
        }

        var choices = found.Append(prefix).ToList();
        var first = text[..start] + choices[0];
        _completion = (text[..start], choices, 0, first);
        return first;
    }
}
