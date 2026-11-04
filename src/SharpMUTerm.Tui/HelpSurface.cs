using SharpMUTerm.Client.Modals;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// Help in cells: a search line, the matching rows of <see cref="HelpModel"/> under their headings, and a how-to page
/// in place of the list once one is chosen. The state is the reader's (what is typed, which row, which page, how far
/// down it); what it lists is the model's <see cref="HelpModel.View"/>, so the window's native help lists the same rows
/// for the same query. <see cref="SharpMUTermApp"/> hosts it in a <see cref="ListOverlay"/>, which is sized once when it
/// opens, so <see cref="Render(HelpModel, string, int, string?, int, int, int)"/> always draws exactly the rows it is given.
/// </summary>
internal sealed class HelpSurface
{
    /// <summary>The width help asks for; the overlay narrows it on a small terminal.</summary>
    internal const int PreferredWidth = 96;

    public HelpSurface(HelpModel model, string query = "", string? topic = null)
    {
        Model = model;
        Query = query;
        Topic = model.Topic(topic)?.Id;
    }

    public HelpModel Model { get; }

    public string Query { get; private set; }

    /// <summary>The row selected in the filtered list.</summary>
    public int Selected { get; private set; }

    /// <summary>The page showing, or null for the list.</summary>
    public string? Topic { get; private set; }

    /// <summary>How far down the page is scrolled, in rows.</summary>
    public int Scroll { get; private set; }

    /// <summary>The rows listed for the query.</summary>
    public IReadOnlyList<HelpEntry> Rows => Model.View(Query);

    /// <summary>The index in <see cref="HelpModel.Entries"/> of the selected row, or -1.</summary>
    public int SelectedEntry => IndexOf(Model, Rows, Selected);

    /// <summary>Esc: a page goes back to the list (true, help stays open); from the list help closes (false).</summary>
    public bool Back()
    {
        if (Topic is null)
        {
            return false;
        }

        Topic = null;
        Scroll = 0;
        return true;
    }

    /// <summary>
    /// A key. Returns the entry index ⏎ chose when it is a command or a key to act on (help then closes), -1 when help
    /// stays open (typing, moving, opening a page).
    /// </summary>
    public int Key(ConsoleKeyInfo key)
    {
        var rows = Rows;
        if (Topic is not null)
        {
            Scroll = key.Key switch
            {
                ConsoleKey.UpArrow => Math.Max(0, Scroll - 1),
                ConsoleKey.DownArrow => Scroll + 1,
                ConsoleKey.PageUp => Math.Max(0, Scroll - 10),
                ConsoleKey.PageDown or ConsoleKey.Spacebar => Scroll + 10,
                ConsoleKey.Home => 0,
                _ => Scroll,
            };
            if (key.Key is ConsoleKey.Backspace or ConsoleKey.LeftArrow)
            {
                Back();
            }

            return -1;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Selected = rows.Count == 0 ? 0 : (Selected - 1 + rows.Count) % rows.Count;
                return -1;
            case ConsoleKey.DownArrow:
                Selected = rows.Count == 0 ? 0 : (Selected + 1) % rows.Count;
                return -1;
            case ConsoleKey.PageUp:
                Selected = Math.Max(0, Selected - 10);
                return -1;
            case ConsoleKey.PageDown:
                Selected = Math.Min(Math.Max(0, rows.Count - 1), Selected + 10);
                return -1;
            case ConsoleKey.Backspace:
                if (Query.Length > 0)
                {
                    Query = Query[..^1];
                    Selected = 0;
                }

                return -1;
            case ConsoleKey.Enter:
                if (Selected < rows.Count)
                {
                    var entry = rows[Selected];
                    if (entry.Kind == HelpKind.Topic)
                    {
                        Topic = entry.Target;
                        Scroll = 0;
                        return -1;
                    }

                    return entry.Target.Length == 0 ? -1 : SelectedEntry;
                }

                return -1;
        }

        if (key.KeyChar >= ' ' && !char.IsControl(key.KeyChar)
            && (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0
            && Query.Length < 40)
        {
            Query += key.KeyChar;
            Selected = 0;
        }

        return -1;
    }

    /// <summary>What help draws now, in <paramref name="rows"/> rows of <paramref name="width"/>.</summary>
    public List<string> Render(int width, int rows) => Render(Model, Query, Selected, Topic, Scroll, width, rows);

    /// <summary>
    /// Help for <paramref name="query"/>, with row <paramref name="selected"/> marked — or page <paramref name="topic"/>
    /// scrolled <paramref name="scroll"/> rows down — in exactly <paramref name="rows"/> rows.
    /// </summary>
    internal static List<string> Render(HelpModel model, string query, int selected, string? topic, int scroll, int width, int rows)
    {
        width = Math.Max(30, width);
        rows = Math.Max(8, rows);
        var lines = new List<string>();
        var body = rows - 3;
        if (model.Topic(topic) is { } page)
        {
            lines.Add($"[{ScreenPalette.Accent}]HELP ▸[/] [bold {ScreenPalette.Value}]{Escape(page.Title)}[/]"
                + $"[{ScreenPalette.Label}]  ·  {Escape(page.Summary)}[/]");
            lines.Add(string.Empty);
            var text = page.Paragraphs.SelectMany(l => Wrap(l, width - 3)).ToList();
            var top = Math.Clamp(scroll, 0, Math.Max(0, text.Count - body));
            foreach (var line in text.Skip(top).Take(body))
            {
                lines.Add($" [{ScreenPalette.Value}]{Escape(line)}[/]");
            }

            Pad(lines, rows - 1);
            var more = top + body < text.Count ? "↓ more · " : string.Empty;
            lines.Add($"[{ScreenPalette.Label}]{more}↑↓ PgUp PgDn scroll · Esc back to the list[/]");
            return lines;
        }

        var view = model.View(query);
        var count = view.Count == model.Entries.Count ? $"{view.Count} entries" : $"{view.Count} of {model.Entries.Count}";
        lines.Add(SpreadLR(
            $"[{ScreenPalette.Accent}]HELP[/]  [{ScreenPalette.Label}]search[/] [{ScreenPalette.Value}]{Escape(query)}▏[/]",
            $"[{ScreenPalette.Label}]{count}[/]",
            width));
        lines.Add(string.Empty);

        // Each row is a heading (when its section starts) and the entry; the window of rows is chosen so the selected
        // entry is on it, then the headings are drawn above the entries they lead.
        var drawn = new List<(string Line, int Entry)>();
        var titleWidth = Math.Clamp(view.Count == 0 ? 16 : view.Max(e => e.Title.Length) + 1, 16, Math.Max(16, width * 45 / 100));
        string? section = null;
        for (var i = 0; i < view.Count; i++)
        {
            var entry = view[i];
            if (entry.Section != section)
            {
                section = entry.Section;
                drawn.Add(($"[{ScreenPalette.Muted}]{Escape(section)}[/]", -1));
            }

            drawn.Add((Row(entry, i == selected, width, titleWidth), i));
        }

        if (view.Count == 0)
        {
            drawn.Add(($"  [{ScreenPalette.Label}]{Escape(HelpModel.NoMatches)}[/]", -1));
        }

        var at = Math.Max(0, drawn.FindIndex(d => d.Entry == selected));
        var first = Math.Clamp(at - (body / 2), 0, Math.Max(0, drawn.Count - body));
        var shown = drawn.Skip(first).Take(body).ToList();
        if (shown.Count > 1 && shown[^1].Entry < 0 && first + shown.Count < drawn.Count)
        {
            // A heading on the last row would lead nothing on screen.
            shown.RemoveAt(shown.Count - 1);
        }

        lines.AddRange(shown.Select(d => d.Line));
        Pad(lines, rows - 1);
        lines.Add($"[{ScreenPalette.Label}]type to search · ↑↓ pick · ⏎ opens, places or runs it · Esc closes[/]");
        return lines;
    }

    private static string Row(HelpEntry entry, bool selected, int width, int titleWidth)
    {
        var mark = selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
        var key = entry.Key.Length == 0 ? string.Empty : entry.Key;
        var title = Fit(entry.Title, titleWidth);
        var room = Math.Max(0, width - 3 - titleWidth - (key.Length == 0 ? 0 : key.Length + 2));
        var detail = Fit(entry.Detail, room);
        var weight = selected ? "bold " : string.Empty;
        var left = $"{mark} [{weight}{ScreenPalette.Value}]{Escape(title.PadRight(titleWidth))}[/] [{ScreenPalette.Label}]{Escape(detail)}[/]";
        return key.Length == 0 ? left : SpreadLR(left, $"[{ScreenPalette.Accent}]{Escape(key)}[/]", width);
    }

    private static string Fit(string text, int width) =>
        width <= 0 ? string.Empty : text.Length <= width ? text : width == 1 ? "…" : text[..(width - 1)] + "…";

    private static void Pad(List<string> lines, int count)
    {
        while (lines.Count < count)
        {
            lines.Add(string.Empty);
        }

        if (lines.Count > count)
        {
            lines.RemoveRange(count, lines.Count - count);
        }
    }

    /// <summary>Plain text broken at spaces into rows no wider than <paramref name="width"/>; an empty line stays one.</summary>
    internal static IEnumerable<string> Wrap(string text, int width)
    {
        if (text.Length <= width)
        {
            yield return text;
            yield break;
        }

        var line = string.Empty;
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > width)
            {
                yield return line;
                line = string.Empty;
            }

            line = line.Length == 0 ? word : line + " " + word;
            while (line.Length > width)
            {
                yield return line[..width];
                line = line[width..];
            }
        }

        if (line.Length > 0)
        {
            yield return line;
        }
    }

    /// <summary>The index in <paramref name="model"/>'s entries of row <paramref name="row"/> of <paramref name="view"/>.</summary>
    internal static int IndexOf(HelpModel model, IReadOnlyList<HelpEntry> view, int row)
    {
        if (row < 0 || row >= view.Count)
        {
            return -1;
        }

        for (var i = 0; i < model.Entries.Count; i++)
        {
            if (ReferenceEquals(model.Entries[i], view[row]))
            {
                return i;
            }
        }

        return -1;
    }
}
