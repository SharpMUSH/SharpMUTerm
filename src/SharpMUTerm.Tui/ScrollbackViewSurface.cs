using SharpMUTerm.Client.Modals;
using static SharpMUTerm.Tui.MarkupText;
using static SharpMUTerm.Tui.ScreenPalette;

namespace SharpMUTerm.Tui;

/// <summary>
/// The read-only history view in cells: a page of a session's scrollback (ring or spill) around the line ⌃F's older-history
/// search found, that line on the accent band and the query's run in every line in bold accent. Pure: the page is the
/// <see cref="ScrollbackViewModel"/> the window's native view draws too, and every move is the client's ranged read.
/// Always <see cref="ScrollbackViewModel.PageRows"/> rows of page, so the overlay sized once at open keeps fitting.
/// </summary>
internal static class ScrollbackViewSurface
{
    /// <summary>The width the view asks for; the overlay narrows it on a small terminal.</summary>
    internal const int PreferredWidth = 110;

    /// <summary>The view's lines at <paramref name="width"/>: title, position, a blank, the page, a blank, the keys.</summary>
    internal static List<string> Render(ScrollbackViewModel model, int width)
    {
        width = Math.Max(20, width);
        var lines = new List<string>
        {
            SpreadLR($"[{Label}]{Escape(model.Title)}[/]", $"[{Muted}]{Escape(model.Position)}[/]", width),
            string.Empty,
        };

        for (var i = 0; i < ScrollbackViewModel.PageRows; i++)
        {
            lines.Add(i < model.Lines.Count ? Row(model.Lines[i], model.Lines[i].Index == model.Marked, width) : string.Empty);
        }

        lines.Add(string.Empty);
        lines.Add($"[{Label}]{Escape(ElidePlain(ScrollbackViewModel.Keys, width))}[/]");
        return lines;
    }

    private static string Row(ScrollbackViewLine line, bool marked, int width)
    {
        var text = ElidePlain(line.Text.Replace('\t', ' '), width - 2);
        if (marked)
        {
            var pad = new string(' ', Math.Max(0, width - 2 - text.Length));
            return $"[{Ink} on {Accent}]▸ {Escape(text)}{pad}[/]";
        }

        if (line.MatchStart < 0 || line.MatchStart >= text.Length || line.MatchLength <= 0)
        {
            return $"  [{Value}]{Escape(text)}[/]";
        }

        var length = Math.Min(line.MatchLength, text.Length - line.MatchStart);
        var before = text[..line.MatchStart];
        var hit = text.Substring(line.MatchStart, length);
        var after = text[(line.MatchStart + length)..];
        return $"  [{Value}]{Escape(before)}[/][bold {Accent}]{Escape(hit)}[/][{Value}]{Escape(after)}[/]";
    }

    private static string ElidePlain(string text, int width) =>
        width <= 0 ? string.Empty : text.Length <= width ? text : text[..Math.Max(0, width - 1)] + "…";
}
