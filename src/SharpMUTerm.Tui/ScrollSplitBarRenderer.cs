namespace SharpMUTerm.Tui;

/// <summary>
/// The band across a pane split by scrolling back (<c>── scrolled back 120 rows · End returns · ⌥F freezes ──</c>),
/// between the history being read above and the live tail below. Fifth of the boundary bars, and like
/// <see cref="FreezeBarRenderer"/> it is both the label and the divider. Pure, so its width is testable.
/// </summary>
internal static class ScrollSplitBarRenderer
{
    /// <summary>
    /// The bar for a viewport <paramref name="rowsBack"/> display rows above its newest line, exactly
    /// <paramref name="width"/> cells wide. The count is a reserved field capped the way the status row's is
    /// (<see cref="UnreadBadge"/>), so the label does not move as output arrives; the hints are dropped, last
    /// first, when the pane is too narrow for them, and the rule takes whatever is left.
    /// </summary>
    public static string Bar(int rowsBack, int width, string accentHex)
    {
        ArgumentException.ThrowIfNullOrEmpty(accentHex);

        var count = UnreadBadge.Format(Math.Max(1, rowsBack)).PadLeft(UnreadBadge.FieldWidth);
        var label = $"{Glyphs.Scrollback} scrolled back {count} rows";
        string[] hints = ["End returns", "⌥F freezes"];

        for (var keep = hints.Length; keep >= 0; keep--)
        {
            var hint = keep == 0 ? string.Empty : " · " + string.Join(" · ", hints.Take(keep));
            var lead = keep > 0 || width >= label.Length + 4 ? "── " : string.Empty;
            var used = lead.Length + label.Length + hint.Length + 1; // lead + label + hints + " "
            if (used + 2 <= width || keep == 0)
            {
                var rule = new string('─', Math.Max(0, width - used));
                return $"[dim]{lead}[/][{accentHex}]{MarkupText.Escape(label)}[/][dim]{MarkupText.Escape(hint)} {rule}[/]";
            }
        }

        return string.Empty; // unreachable: keep == 0 always returns
    }
}
