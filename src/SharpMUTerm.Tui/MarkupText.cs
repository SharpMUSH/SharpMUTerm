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
    /// The first of <paramref name="candidates"/> (longest first) whose visible length fits <paramref name="room"/>,
    /// else the last: a footer's drop-order ladder, so a narrow bar gives up whole parts in a chosen order rather
    /// than being cut off mid-word at its end.
    /// </summary>
    internal static string FirstFitting(int room, params string[] candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        foreach (var candidate in candidates)
        {
            if (VisibleLength(candidate) <= room)
            {
                return candidate;
            }
        }

        return candidates.Length == 0 ? string.Empty : candidates[^1];
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

        // When both do not fit, the left gives way: on every bar that calls this the right is the keys
        // (Esc, ⏎), and a bar that clipped them off the edge named an action without its key.
        var room = width - VisibleLength(right) - 1;
        if (VisibleLength(left) > room)
        {
            left = ElideVisible(left, room);
        }

        var gap = Math.Max(1, width - VisibleLength(left) - VisibleLength(right));
        return left + new string(' ', gap) + right;
    }

    /// <summary>
    /// <see cref="SpreadLR"/> for a screen's header, where the left is the screen's title and the right
    /// its key hints. Here the hints give way, from the middle: the title says where the reader is, and
    /// the last hint is the key that closes the screen, which is the one a lost reader needs.
    /// </summary>
    internal static string SpreadTitle(string title, string hints, int width)
    {
        var room = width - VisibleLength(title) - 1;
        if (width > 0 && VisibleLength(hints) > room)
        {
            var last = hints.LastIndexOf(" · ", StringComparison.Ordinal);
            var tail = last > 0 ? hints[last..] : string.Empty;
            hints = tail.Length > 0 && VisibleLength(tail) < room
                ? ElideVisible(hints[..last], room - VisibleLength(tail)) + tail
                : ElideVisible(hints, Math.Max(0, room));
        }

        return SpreadLR(title, hints, width);
    }

    /// <summary>
    /// Cuts a markup string to <paramref name="width"/> visible cells, ending in <c>…</c> when anything
    /// was cut. Tags are kept, including those after the cut, so every span that opened still closes.
    /// </summary>
    internal static string ElideVisible(string markup, int width) => ElideVisible(markup, width, mark: true);

    /// <summary>
    /// A row of a column <paramref name="width"/> cells wide: unchanged when it fits; cut without a mark when only its
    /// trailing blanks (a cursor band's padding) are past the edge; else elided with <c>…</c> — never clipped mid-word
    /// by the control, which says nothing about what was lost. Zero or less means the width is not known.
    /// </summary>
    internal static string FitVisible(string markup, int width) =>
        width <= 0 || VisibleLength(markup) <= width
            ? markup
            : ElideVisible(markup, width, mark: Plain(markup).TrimEnd().Length > width);

    private static string ElideVisible(string markup, int width, bool mark)
    {
        if (VisibleLength(markup) <= width)
        {
            return markup;
        }

        var budget = Math.Max(0, mark ? width - 1 : width);
        var result = new System.Text.StringBuilder(markup.Length);
        var shown = 0;
        var cut = false;

        void Visible(string text)
        {
            if (shown < budget)
            {
                result.Append(text);
                shown++;
            }
            else if (!cut)
            {
                cut = true;
                if (mark && width > 0)
                {
                    result.Append('…');
                }
            }
        }

        for (var i = 0; i < markup.Length; i++)
        {
            var c = markup[i];
            if ((c == '[' || c == ']') && i + 1 < markup.Length && markup[i + 1] == c)
            {
                Visible(new string(c, 2));
                i++;
                continue;
            }

            if (c == '[' && markup.IndexOf(']', i) is var close and > 0)
            {
                result.Append(markup, i, close - i + 1);
                i = close;
                continue;
            }

            Visible(c.ToString());
        }

        return result.ToString();
    }
}
