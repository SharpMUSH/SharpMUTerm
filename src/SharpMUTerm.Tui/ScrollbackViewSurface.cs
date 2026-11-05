using SharpMUTerm.Client.Modals;
using static SharpMUTerm.Tui.MarkupText;
using static SharpMUTerm.Tui.ScreenPalette;

namespace SharpMUTerm.Tui;

/// <summary>
/// The read-only history view in cells: a page of a session's scrollback (ring or spill) around the line ⌃F's older-history
/// search found, centred: that line on the accent band, its context (<see cref="ScrollbackViewModel.ContextRows"/> either side)
/// in the body ink behind a <c>│</c>, the rest of the page muted, and the query's run in every line in bold accent. Pure: the page is the
/// <see cref="ScrollbackViewModel"/> the window's native view draws too, and every move is the client's ranged read.
/// Always the model's <see cref="ScrollbackViewModel.PageRows"/> rows of page — sized from the screen, so the overlay sized
/// once at open keeps fitting, at 80×24 too — and the keys are pinned under the body (<see cref="Footer"/>), never scrolled.
/// </summary>
internal static class ScrollbackViewSurface
{
    /// <summary>The width the view asks for; the overlay narrows it on a small terminal.</summary>
    internal const int PreferredWidth = 110;

    /// <summary>
    /// The view's body at <paramref name="width"/>: the title and the position (one row, or a row each with
    /// <paramref name="twoTitleRows"/>, below <see cref="ScrollbackViewModel.WideColumns"/>), a blank, and the page.
    /// </summary>
    internal static List<string> Render(ScrollbackViewModel model, int width, bool twoTitleRows = false)
    {
        width = Math.Max(20, width);
        var lines = new List<string>();
        if (twoTitleRows)
        {
            lines.Add($"[{Label}]{Escape(ElidePlain(model.Title, width))}[/]");
            lines.Add($"[{Muted}]{Escape(ElidePlain(model.Position, width))}[/]");
        }
        else
        {
            lines.Add(SpreadLR($"[{Label}]{Escape(model.Title)}[/]", $"[{Muted}]{Escape(model.Position)}[/]", width));
        }

        lines.Add(string.Empty);
        for (var i = 0; i < model.PageRows; i++)
        {
            lines.Add(i < model.Lines.Count ? Row(model.Lines[i], model.Role(model.Lines[i]), width) : string.Empty);
        }

        return lines;
    }

    /// <summary>The pinned key row at <paramref name="width"/>: a blank, then the longest rung of the keys that fits.</summary>
    internal static List<string> Footer(int width)
    {
        var keys = ScrollbackViewModel.KeysLadder.FirstOrDefault(k => k.Length <= width) ?? ScrollbackViewModel.KeysLadder[^1];
        return [string.Empty, $"[{Label}]{Escape(keys)}[/]"];
    }

    private static string Row(ScrollbackViewLine line, ScrollbackLineRole role, int width)
    {
        var text = ElidePlain(line.Text.Replace('\t', ' '), width - 2);
        if (role == ScrollbackLineRole.Hit)
        {
            var pad = new string(' ', Math.Max(0, width - 2 - text.Length));
            return $"[{Ink} on {Accent}]▸ {Escape(text)}{pad}[/]";
        }

        if (line.MatchStart < 0 || line.MatchStart >= text.Length || line.MatchLength <= 0)
        {
            return role == ScrollbackLineRole.Context ? $"[{Muted}]│[/] [{Value}]{Escape(text)}[/]" : $"  [{Muted}]{Escape(text)}[/]";
        }

        var length = Math.Min(line.MatchLength, text.Length - line.MatchStart);
        var before = text[..line.MatchStart];
        var hit = text.Substring(line.MatchStart, length);
        var after = text[(line.MatchStart + length)..];
        var ink = role == ScrollbackLineRole.Context ? Value : Muted;
        var lead = role == ScrollbackLineRole.Context ? $"[{Muted}]│[/] " : "  ";
        return $"{lead}[{ink}]{Escape(before)}[/][bold {Accent}]{Escape(hit)}[/][{ink}]{Escape(after)}[/]";
    }

    private static string ElidePlain(string text, int width) => MarkupText.ElidePlain(text, width);
}
