namespace SharpMUTerm.Tui;

/// <summary>
/// How a boundary bar (restore, away, activity, search) fits its pane: the trailing rule runs to the pane's edge,
/// and the words are elided before the bar is allowed to wrap. A wrapped bar is three rows — label, rule, blank —
/// where the pane can afford one, which is how the old fixed 48-cell rule broke every pane narrower than about 85
/// cells (80×24 included) and stopped short in every wider one. <see cref="ScrollSplitBarRenderer"/> already sized
/// itself this way.
/// </summary>
internal static class BarRule
{
    /// <summary>The rule when the pane's width is not known yet (nothing arranged), as every bar drew it before.</summary>
    internal const int Fallback = 48;

    /// <summary>The shortest rule kept before the words give way, so the bar still reads as a divider.</summary>
    internal const int Minimum = 3;

    /// <summary>
    /// The words and the rule length for a bar whose fixed parts take <paramref name="fixedCells"/> cells (label,
    /// glyphs, the spaces between), with <paramref name="text"/> the part that may be elided, in a pane
    /// <paramref name="width"/> cells wide. Width 0 means not arranged yet: the text is whole and the rule is
    /// <see cref="Fallback"/>.
    /// </summary>
    public static (string Text, int Rule) Fit(int fixedCells, string text, int width)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (width <= 0)
        {
            return (text, Fallback);
        }

        var room = width - fixedCells;
        if (text.Length + Minimum <= room)
        {
            return (text, room - text.Length);
        }

        // Not enough for the words and a short rule: the words give way first, down to nothing, and the rule keeps
        // what is left (possibly none) — never a cell past the edge.
        var keep = Math.Max(0, room - Minimum);
        var elided = keep <= 0 ? string.Empty
            : keep >= text.Length ? text
            : text[..Math.Max(0, keep - 1)].TrimEnd() + "…";
        return (elided, Math.Max(0, room - elided.Length));
    }

    /// <summary>
    /// <see cref="Fit(int, string, int)"/> over a ladder of wordings, longest first: the first that fits beside a short rule
    /// is used whole, so a bar the client words shortens its words rather than cutting them (wave 33). Only when the last
    /// does not fit is it elided.
    /// </summary>
    public static (string Text, int Rule) Fit(int fixedCells, IReadOnlyList<string> forms, int width)
    {
        ArgumentNullException.ThrowIfNull(forms);
        var room = width - fixedCells - Minimum;
        var form = width <= 0 ? forms[0] : forms.FirstOrDefault(f => f.Length <= room) ?? forms[^1];
        return Fit(fixedCells, form, width);
    }

    /// <summary>A run of <paramref name="cells"/> rule characters.</summary>
    public static string Rule(int cells) => new('─', Math.Max(0, cells));
}
