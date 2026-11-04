using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui.Panels;

/// <summary>
/// A docked panel's heading as the terminal mockups draw it: one row on a band the panel's whole width —
/// <c> HERE  The Grand Plaza · 4 </c> — the label bold, everything in the band's own ink, a cell of margin at each end.
/// The band costs no row: it is the row the heading always took. Text on it is the band's ink or its accent, both held
/// to <see cref="WorkspacePalette.HeadingFloor"/> on it, because a heading's muted words would be a third ink measured
/// against nothing.
/// </summary>
internal static class HeadingBand
{
    /// <summary>The band and its two inks, as markup colours.</summary>
    internal readonly record struct Ink(string Band, string Text, string Accent);

    /// <summary>The band and inks for <paramref name="theme"/>, the accent held to the heading floor on the band.</summary>
    internal static Ink For(Theme theme, string accent)
    {
        var band = WorkspacePalette.HeadingBand(theme);
        return new Ink(
            band.ToHex(),
            WorkspacePalette.HeadingInk(theme).ToHex(),
            Contrast.Legible(RgbJsonConverter.Parse(accent), band, WorkspacePalette.HeadingFloor).ToHex());
    }

    /// <summary>
    /// The heading row for a panel <paramref name="width"/> cells wide: <paramref name="label"/> bold, then
    /// <paramref name="rest"/> (markup in the band's inks), and <paramref name="right"/> at the far end when both fit;
    /// the right part goes first, then the rest is elided. Never wider than <paramref name="width"/>. The first cell is
    /// the band's margin, or <paramref name="lead"/> (one cell of markup: the docked composer's armed <c>▌</c>) in it.
    /// </summary>
    internal static string Row(string label, string rest, string right, int width, Ink ink, string? lead = null)
    {
        width = Math.Max(1, width);
        var left = $"[bold]{label}[/]{rest}";
        var inner = Math.Max(1, width - 2);
        var body = right.Length > 0 && VisibleLength(left) + VisibleLength(right) + 2 <= inner
            ? SpreadLR(left, right, inner)
            : PadVisible(ElideVisible(left, inner), inner);
        return width < 3
            ? $"[{ink.Text} on {ink.Band}]{ElideVisible(left, width)}[/]"
            : $"[{ink.Text} on {ink.Band}]{lead ?? " "}{body} [/]";
    }
}
