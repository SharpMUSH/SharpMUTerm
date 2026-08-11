using System.Text.RegularExpressions;

namespace SharpMUTerm.Tui;

/// <summary>
/// Helpers for the Spectre-style markup the renderers emit: escaping literal brackets, measuring a
/// string's printable width past its <c>[tag]</c> wrappers, and laying text out to that width. Every
/// renderer that pads or right-aligns markup needs the same measurement, so it lives here rather than
/// being copied per screen — a divergent copy silently mis-pads a column.
/// </summary>
internal static class MarkupText
{
    private static readonly Regex TagPattern = new(@"\[[^\[\]]*\]", RegexOptions.Compiled);

    /// <summary>Escapes literal brackets so markup can't be injected by configured text.</summary>
    internal static string Escape(string text) => text.Replace("[", "[[").Replace("]", "]]");

    /// <summary>
    /// Counts the printable length of a markup string: escaped brackets (<c>[[</c>/<c>]]</c>) count
    /// as one literal character each, and <c>[tag]</c> wrappers are stripped entirely.
    /// </summary>
    internal static int VisibleLength(string markup)
    {
        var protectedText = markup.Replace("[[", "\u0001").Replace("]]", "\u0002");
        return TagPattern.Replace(protectedText, string.Empty).Length;
    }

    /// <summary>
    /// The text a markup string actually puts on the screen: <c>[tag]</c> wrappers removed, and escaped
    /// brackets (<c>[[</c>/<c>]]</c>) back to the single characters they stand for.
    /// <para>
    /// This is what ⌃F searches, and it searches this rather than the markup because a match has to mean
    /// what it looks like: a colour tag in the middle of a word must not split one, and a reader must not
    /// be able to search for <c>#ff0000</c> and find every red line. The same rule <c>UrlDetector</c>
    /// follows one layer down — run over the line, never over its pieces.
    /// </para>
    /// <para>
    /// It shares <see cref="VisibleLength"/>'s protect-then-strip shape deliberately, and the two are
    /// held together by test: <c>Plain(x).Length</c> equals <c>VisibleLength(x)</c> for every input. A
    /// divergence would put a match's offsets in a different coordinate system from the width every
    /// renderer measures with.
    /// </para>
    /// </summary>
    internal static string Plain(string markup)
    {
        var protectedText = markup.Replace("[[", "\u0001").Replace("]]", "\u0002");
        return TagPattern.Replace(protectedText, string.Empty)
            .Replace('\u0001', '[')
            .Replace('\u0002', ']');
    }

    /// <summary>Pads a markup string to a target *visible* column width, ignoring markup tags.</summary>
    internal static string PadVisible(string markup, int width)
    {
        var visible = VisibleLength(markup);
        return visible >= width ? markup : markup + new string(' ', width - visible);
    }

    /// <summary>
    /// Lays a left- and right-hand fragment on one line, right-aligning the right to
    /// <paramref name="width"/>. A non-positive width means "width unknown" (the unit-test path), which
    /// falls back to a fixed gap.
    /// </summary>
    internal static string SpreadLR(string left, string right, int width)
    {
        if (width <= 0)
        {
            return $"{left}   {right}";
        }

        var gap = Math.Max(1, width - VisibleLength(left) - VisibleLength(right));
        return left + new string(' ', gap) + right;
    }
}
