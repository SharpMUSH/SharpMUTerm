using System.Text;
using SharpMUTerm.Client.Palette;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// Draws the command surface (⌃P) from the shared view-model (<see cref="CommandPaletteModel"/>): a context
/// line, then each group's header and its rows, the selected row highlighted. Every row has three parts — the
/// title, with the characters the query matched in bold accent; the detail, dimmed; and the key, right-aligned
/// in a column of its own so every chord on the surface starts and ends in the same place. When the surface is
/// narrower than a row, the detail goes first and then the title is elided; the key never is, because the key
/// is the part a reader came to learn. Pure so it is unit-testable.
/// </summary>
internal static class CommandSurfaceRenderer
{
    /// <summary>Cells before the title: <c> ▸ </c> on the selected row, blanks elsewhere.</summary>
    private const int Lead = 3;

    /// <summary>Cells between the title and its detail.</summary>
    private const int DetailGap = 3;

    /// <summary>The fewest cells between the text and the key column.</summary>
    private const int KeyGap = 2;

    /// <summary>The fewest characters of a detail worth showing; below this it is left out rather than cut to a stub.</summary>
    private const int MinDetail = 4;

    /// <summary>The selected row's band: dark ink on the accent.</summary>
    private const string SelectedBand = "#18181c on #00f5b7";

    /// <summary>A matched character on an unselected row.</summary>
    private static readonly string MatchInk = $"bold {ScreenPalette.Accent}";

    /// <summary>
    /// Builds the markup lines. The entry at index <paramref name="selected"/> of <see cref="PaletteView.Entries"/>
    /// is highlighted. <paramref name="width"/> is the surface's content width; 0 sizes every row to its own
    /// natural width (<see cref="NaturalWidth"/>), which is what a test reads.
    /// </summary>
    public static List<string> Render(PaletteView view, int selected, string? context, int width = 0)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (width <= 0)
        {
            width = NaturalWidth(view);
        }

        var lines = new List<string>
        {
            $"[dim]{Escape(view.Entries.Count.ToString())} of {Escape(view.Total.ToString())}"
            + (string.IsNullOrEmpty(context) ? string.Empty : $"   acting on {Escape(context)}") + "[/]",
        };

        var keyWidth = view.KeyWidth;
        var index = 0;
        foreach (var row in view.Rows)
        {
            switch (row)
            {
                case PaletteHeader header:
                    lines.Add($"[dim]├ {Escape(header.Label)}[/]");
                    break;
                case PaletteEntry entry:
                    lines.Add(Row(entry, index == selected, width, keyWidth));
                    index++;
                    break;
            }
        }

        if (view.Entries.Count == 0)
        {
            lines.Add("[dim]  no matches[/]");
        }

        return lines;
    }

    /// <summary>
    /// The width every row fits in without eliding anything: lead, title, detail, and the key column at the
    /// widest key. The surface sizes itself to this, capped to the desktop.
    /// </summary>
    public static int NaturalWidth(PaletteView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var keyColumn = KeyColumn(view.KeyWidth);
        var widest = 0;
        foreach (var entry in view.Entries)
        {
            var text = entry.Item.Title.Length + (entry.Detail is null ? 0 : DetailGap + entry.Detail.Length);
            widest = Math.Max(widest, Lead + text + keyColumn);
        }

        return widest;
    }

    /// <summary>The visible width of the widest rendered line.</summary>
    public static int MaxWidth(IReadOnlyList<string> lines)
    {
        var max = 0;
        foreach (var line in lines)
        {
            max = Math.Max(max, VisibleLength(line));
        }

        return max;
    }

    // The key column: the gap, the widest key, and one cell before the edge. With no keys at all, one cell.
    private static int KeyColumn(int keyWidth) => keyWidth == 0 ? 1 : KeyGap + keyWidth + 1;

    private static string Row(PaletteEntry entry, bool selected, int width, int keyWidth)
    {
        var room = Math.Max(1, width - Lead - KeyColumn(keyWidth));

        // The title keeps every cell it needs before the detail gets any; past that it is elided.
        var title = entry.Item.Title;
        var titleShown = title.Length <= room ? title.Length : Math.Max(1, room - 1);
        var titleElided = titleShown < title.Length;
        var used = titleShown + (titleElided ? 1 : 0);

        string? detail = null;
        if (entry.Detail is { } d && room - used - DetailGap >= Math.Min(MinDetail, d.Length))
        {
            var detailRoom = room - used - DetailGap;
            detail = d.Length <= detailRoom ? d : string.Concat(d.AsSpan(0, detailRoom - 1), "…");
            used += DetailGap + detail.Length;
        }

        var pad = new string(' ', Math.Max(0, room - used));
        var key = entry.Key is null
            ? new string(' ', KeyColumn(keyWidth))
            : new string(' ', KeyGap + keyWidth - entry.Key.Length) + entry.Key + " ";

        var titleMarkup = Highlight(title, titleShown, entry.TitleMatches, selected ? "bold underline" : MatchInk)
            + (titleElided ? "…" : string.Empty);

        if (selected)
        {
            var detailPart = detail is null ? string.Empty : new string(' ', DetailGap) + Escape(detail);
            return $"[{SelectedBand}] ▸ {titleMarkup}{detailPart}{pad}{Escape(key)}[/]";
        }

        var detailMarkup = detail is null ? string.Empty : $"{new string(' ', DetailGap)}[dim]{Escape(detail)}[/]";
        var keyMarkup = entry.Key is null ? key : $"[{ScreenPalette.Muted}]{Escape(key)}[/]";
        return $"   {titleMarkup}{detailMarkup}{pad}{keyMarkup}";
    }

    /// <summary>The first <paramref name="shown"/> characters of <paramref name="text"/>, matched runs wrapped in <paramref name="style"/>.</summary>
    private static string Highlight(string text, int shown, IReadOnlyList<TextRange> matches, string style)
    {
        var sb = new StringBuilder();
        var at = 0;
        foreach (var range in matches)
        {
            var start = Math.Min(range.Start, shown);
            var end = Math.Min(range.Start + range.Length, shown);
            if (start < at || end <= start)
            {
                continue;
            }

            sb.Append(Escape(text[at..start]));
            sb.Append('[').Append(style).Append(']').Append(Escape(text[start..end])).Append("[/]");
            at = end;
        }

        sb.Append(Escape(text[at..shown]));
        return sb.ToString();
    }
}
