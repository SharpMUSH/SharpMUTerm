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
    /// <param name="fill">
    /// The band it is drawn on (<see cref="Panels.HeadingBand"/>'s inks: the label in the fill's accent, the rest in its
    /// text). It is a divider between two viewports rather than a row of either, so the terminal mockup fills it; null
    /// draws the bare rule the other boundary bars draw.
    /// </param>
    public static string Bar(SharpMUTerm.Client.Chrome.ScrollBandModel band, int width, string accentHex, Panels.HeadingBand.Ink? fill = null)
    {
        ArgumentNullException.ThrowIfNull(band);
        return Bar(band.RowsBack, width, accentHex, fill);
    }

    /// <summary>The count a band shows for <paramref name="rowsBack"/>: capped, as the status row caps it.</summary>
    public static string Count(int rowsBack) => UnreadBadge.Format(Math.Max(1, rowsBack));

    /// <summary>The bar for a count, as <see cref="Bar(SharpMUTerm.Client.Chrome.ScrollBandModel, int, string)"/> draws it.</summary>
    public static string Bar(int rowsBack, int width, string accentHex, Panels.HeadingBand.Ink? fill = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(accentHex);

        var count = Count(rowsBack).PadLeft(UnreadBadge.FieldWidth);
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
                return fill is { } band
                    // On the band every word is the band's ink, as the terminal mockup draws it: the band is already
                    // the cue, and a second colour on the count made it read as a notice.
                    ? $"[{band.Text} on {band.Band}]{lead}{MarkupText.Escape(label)}{MarkupText.Escape(hint)} {rule}[/]"
                    : $"[dim]{lead}[/][{accentHex}]{MarkupText.Escape(label)}[/][dim]{MarkupText.Escape(hint)} {rule}[/]";
            }
        }

        return string.Empty; // unreachable: keep == 0 always returns
    }
}
