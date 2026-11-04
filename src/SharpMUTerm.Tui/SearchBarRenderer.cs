namespace SharpMUTerm.Tui;

/// <summary>
/// Renders the bar drawn above the line ⌃F sent you to: the one row saying which hit this is, out of how
/// many, and which key goes to the next.
/// <para>
/// Fourth of the boundary bars, and it earns its row the same way <see cref="FreezeBarRenderer"/>,
/// <c>RestoreBarRenderer</c> and <see cref="AwayBarRenderer"/> do: mark the <em>boundary</em>, never
/// restyle the content. Painting the matched span itself was the obvious alternative and is the wrong
/// one — the line is worth having because it is the game's own text in the game's own colours, and a
/// highlight over it would destroy the thing being pointed at. It also costs nothing in cells inside the
/// line, so a pane's rectangle does not move and no server is told its terminal changed size.
/// </para>
/// <para>
/// The query is escaped, because it is the reader's own text going into markup: a search for
/// <c>[public]</c> must appear on the bar rather than be eaten as a tag.
/// </para>
/// Pure, so the markup is unit-testable without a terminal.
/// </summary>
internal static class SearchBarRenderer
{
    /// <summary>The chord the bar names, and the only one it can: see <c>MacroKeys</c> for why ⌥⇧G is not here.</summary>
    internal const string NextChord = "⌥G next";

    /// <summary>
    /// The bar for hit <paramref name="ordinal"/> of <paramref name="total"/> for
    /// <paramref name="query"/>, on an already-resolved <c>#rrggbb</c> accent.
    /// </summary>
    public static string Bar(string query, int ordinal, int total, string accentHex, int width = 0)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrEmpty(accentHex);

        // The ordinal is what makes ⌥G legible: without it the bar moves and nothing says whether you
        // are getting closer to the end of the results or going round in circles.
        // The query gives way before the count and the chord: they are what make the bar usable, the query is
        // what the reader just typed.
        var counted = $" ({ordinal} of {total})";
        var (text, rule) = BarRule.Fit(2 + counted.Length + 2 + NextChord.Length, query, width);
        return $"[{accentHex}]{Glyphs.Search} {MarkupText.Escape(text + counted)}[/] [dim]{BarRule.Rule(rule)} {NextChord}[/]";
    }
}
