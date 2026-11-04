using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// The colour vocabulary the settings screens edit in: a short palette of named colours the choice
/// machinery can step through with ↑↓, plus the round trip between a <see cref="TerminalColor"/> and
/// the text a field shows for it.
/// <para>
/// The palette is deliberately small. <see cref="TerminalColor"/> spans the terminal default, 256
/// palette indices, and all 24 bits of RGB, and a picker for that is a screen of its own; a highlight
/// colour is picked once and then lived with, so the proportionate affordance is a handful of legible
/// choices one keypress apart. Nothing is *lost* by that: <see cref="Parse"/> also accepts
/// <c>#rrggbb</c> and <c>idx:N</c> typed in full, and <see cref="Format"/> renders a colour the
/// palette doesn't name in the same syntax — so a config holding an arbitrary colour opens showing
/// that colour, and committing it unchanged writes it back unchanged.
/// </para>
/// </summary>
internal static class ScreenColours
{
    /// <summary>What "no colour" reads as — <see cref="HighlightColours.None"/>.</summary>
    internal const string None = HighlightColours.None;

    /// <summary>The terminal's default colour, as a name.</summary>
    internal const string DefaultName = HighlightColours.DefaultName;

    /// <summary>The names ↑↓ steps through — the Client's palette, so both hosts offer the same.</summary>
    internal static readonly IReadOnlyList<string> Palette = HighlightColours.Palette;

    /// <summary>A colour as text (<see cref="HighlightColours.Format"/>).</summary>
    internal static string Format(TerminalColor? colour) => HighlightColours.Format(colour);

    /// <summary>Reads a colour (<see cref="HighlightColours.TryParse"/>).</summary>
    internal static bool TryParse(string text, out TerminalColor? colour) => HighlightColours.TryParse(text, out colour);

    internal static string Hex(TerminalColor colour, string fallback) => colour.Kind switch
    {
        TerminalColorKind.Rgb => $"#{colour.R:x2}{colour.G:x2}{colour.B:x2}",
        TerminalColorKind.Indexed => AnsiPalette.ToRgb(colour.Index).ToHex(),
        _ => fallback,
    };
}
