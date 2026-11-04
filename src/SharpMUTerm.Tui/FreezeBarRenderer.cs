namespace SharpMUTerm.Tui;

/// <summary>
/// Renders the freeze split bar (<c>▲ FROZEN ⌥F</c>) that divides a frozen pane's pinned scrollback
/// (above) from its live tail (below), per the design's freeze interaction. The label takes the
/// frozen-chrome accent (design token <c>#c678dd</c> / ANSI 5, resolved through the theme); the hint
/// is dim. Pure so the markup is unit-testable without a terminal.
/// </summary>
internal static class FreezeBarRenderer
{
    /// <summary>The label's cells, the space after it included.</summary>
    private const int LabelCells = 12;

    /// <summary>
    /// Builds the frozen-split bar markup from an already-resolved <c>#rrggbb</c> accent, its rule running to the edge
    /// of a pane <paramref name="width"/> cells wide (<see cref="BarRule"/>'s rule: 0 is not arranged yet, and draws
    /// the old 48 cells). A fixed 48 wrapped in every pane narrower than 61 cells, and the bar's one row then showed
    /// no rule at all; in a wide pane it stopped short.
    /// </summary>
    public static string Bar(string accentHex, int width = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(accentHex);

        // A single line that is both the label and the divider border between pinned scrollback and the
        // live tail: the accented "❄ FROZEN ⌥F" label followed by a dim rule.
        var rule = width > 0 ? Math.Max(0, width - LabelCells) : BarRule.Fallback;
        return $"[{accentHex}]{Glyphs.Freeze} FROZEN ⌥F[/] [dim]{BarRule.Rule(rule)}[/]";
    }
}
