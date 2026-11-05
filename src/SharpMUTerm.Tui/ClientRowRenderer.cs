namespace SharpMUTerm.Tui;

/// <summary>
/// One row the client itself writes into a window — a reconnect counting down, a room a search found where no screen is
/// read — drawn the way the boundary bars are (<see cref="RestoreBarRenderer"/>): a label in the accent, the words muted,
/// a rule to the pane's edge. Not a session line: it goes into the pane buffer as chrome (<c>AppendClientRow</c>), so it
/// reaches no transcript, restore log, scene log, trigger or ⌃F search, and a theme change recolours it with the bars.
/// Replaces the <c>***</c> lines these used to print, which read as the game's own text and were logged as it.
/// Pure, so the markup is unit-testable without a terminal.
/// </summary>
internal static class ClientRowRenderer
{
    /// <summary>The row for <paramref name="label"/> and <paramref name="text"/>, on an already-resolved <c>#rrggbb</c> accent.</summary>
    public static string Bar(string label, string text, string accentHex, int width = 0) => Bar(label, [text], accentHex, width);

    /// <summary>The row with a ladder of wordings, longest first: the first that fits the pane is drawn whole (<see cref="BarRule"/>).</summary>
    public static string Bar(string label, IReadOnlyList<string> forms, string accentHex, int width = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(label);
        ArgumentException.ThrowIfNullOrEmpty(accentHex);
        var (words, rule) = BarRule.Fit(label.Length + 4, forms, width);
        return $"[{accentHex}]› {MarkupText.Escape(label)}[/] [dim]{MarkupText.Escape(words)} {BarRule.Rule(rule)}[/]";
    }
}
