using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.WorldIndex;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>What a key on Browse worlds asks the app to do.</summary>
internal enum WorldBrowseAction
{
    /// <summary>Nothing beyond a repaint.</summary>
    Redraw,

    /// <summary>Ask where the selected row is (<see cref="WorldBrowseSurface.SelectedRow"/>), then open the connect form.</summary>
    Pick,

    /// <summary>Ask the directory itself for what is typed: nothing among the rows held matches it.</summary>
    Ask,

    /// <summary>Open the connect form with the address chosen (<see cref="WorldBrowseSurface.Endpoint"/>) of the world being chosen for.</summary>
    Choose,
}

/// <summary>
/// Browse worlds in cells: a search line, mu*index's rows for what is typed, the credit its licence asks for, and the
/// keys. The state is the reader's (what is typed, the order, which row); what it lists is the model's
/// <see cref="WorldBrowseModel.View"/>, so the window's native list shows the same rows for the same query. Hosted in a
/// <see cref="ListOverlay"/>, which is sized once when it opens, so <see cref="Render(WorldBrowseModel, string, WorldDirectorySort, int, int, int)"/>
/// always draws exactly the rows it is given — loading, failed or listed.
/// </summary>
internal sealed class WorldBrowseSurface
{
    /// <summary>The width the surface asks for; the overlay narrows it on a small terminal.</summary>
    internal const int PreferredWidth = 100;

    public WorldBrowseSurface(string query = "") => Query = query.Length > WorldDirectory.MaxQuery ? query[..WorldDirectory.MaxQuery] : query;

    public string Query { get; private set; }

    public WorldDirectorySort Sort { get; private set; } = WorldDirectorySort.Players;

    /// <summary>The row selected in the listed rows.</summary>
    public int Selected { get; private set; }

    /// <summary>The address selected while a picked world's addresses are being chosen from (0 is the default, TLS first).</summary>
    public int Endpoint => _choiceFor is null ? 0 : _endpoint;

    private int _endpoint;
    private WorldGame? _choiceFor;

    /// <summary>Starts the address choice at its first row whenever a different world's addresses are shown.</summary>
    private void Follow(WorldBrowseModel model)
    {
        if (!ReferenceEquals(model.Choosing, _choiceFor))
        {
            _choiceFor = model.Choosing;
            _endpoint = Math.Max(0, model.Choosing?.FirstConnectable ?? 0);
        }
    }

    /// <summary>The index in <paramref name="model"/>'s rows of the row selected, or -1.</summary>
    public int SelectedRow(WorldBrowseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var view = model.View(Query, Sort);
        if (Selected < 0 || Selected >= view.Count)
        {
            return -1;
        }

        for (var i = 0; i < model.Rows.Count; i++)
        {
            if (ReferenceEquals(model.Rows[i], view[Selected]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>A key, read against what the surface lists now.</summary>
    public WorldBrowseAction Key(WorldBrowseModel model, ConsoleKeyInfo key)
    {
        ArgumentNullException.ThrowIfNull(model);
        Follow(model);
        if (model.Choosing is { } game)
        {
            var addresses = game.Endpoints.Count;
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    _endpoint = (_endpoint - 1 + addresses) % addresses;
                    break;
                case ConsoleKey.DownArrow:
                    _endpoint = (_endpoint + 1) % addresses;
                    break;
                case ConsoleKey.Enter:
                    return WorldBrowseAction.Choose;
            }

            return WorldBrowseAction.Redraw;
        }

        var count = model.View(Query, Sort).Count;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Selected = count == 0 ? 0 : (Selected - 1 + count) % count;
                return WorldBrowseAction.Redraw;
            case ConsoleKey.DownArrow:
                Selected = count == 0 ? 0 : (Selected + 1) % count;
                return WorldBrowseAction.Redraw;
            case ConsoleKey.PageUp:
                Selected = Math.Max(0, Selected - 10);
                return WorldBrowseAction.Redraw;
            case ConsoleKey.PageDown:
                Selected = Math.Min(Math.Max(0, count - 1), Selected + 10);
                return WorldBrowseAction.Redraw;
            case ConsoleKey.Tab:
                Sort = WorldBrowseModel.Next(Sort);
                Selected = 0;
                return WorldBrowseAction.Redraw;
            case ConsoleKey.Backspace:
                if (Query.Length > 0)
                {
                    Query = Query[..^1];
                    Selected = 0;
                }

                return WorldBrowseAction.Redraw;
            case ConsoleKey.Enter:
                if (model.Opening is not null)
                {
                    return WorldBrowseAction.Redraw;
                }

                return model.AsksDirectory(Query) ? WorldBrowseAction.Ask
                    : SelectedRow(model) >= 0 ? WorldBrowseAction.Pick
                    : WorldBrowseAction.Redraw;
        }

        if (key.KeyChar >= ' ' && !char.IsControl(key.KeyChar)
            && (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0
            && Query.Length < WorldDirectory.MaxQuery)
        {
            Query += key.KeyChar;
            Selected = 0;
        }

        return WorldBrowseAction.Redraw;
    }

    /// <summary>What the surface draws now, in <paramref name="rows"/> rows of <paramref name="width"/>.</summary>
    public List<string> Render(WorldBrowseModel model, int width, int rows)
    {
        ArgumentNullException.ThrowIfNull(model);
        Follow(model);
        return Render(model, Query, Sort, Selected, width, rows, Endpoint);
    }

    /// <summary>
    /// Browse worlds for <paramref name="query"/> in <paramref name="sort"/>'s order, row <paramref name="selected"/> marked,
    /// in exactly <paramref name="rows"/> rows: the search line, the credit, the list (or what stands for it), a note and
    /// the keys.
    /// </summary>
    internal static List<string> Render(WorldBrowseModel model, string query, WorldDirectorySort sort, int selected, int width, int rows, int endpoint = 0)
    {
        ArgumentNullException.ThrowIfNull(model);
        width = Math.Max(30, width);
        rows = Math.Max(9, rows);
        if (model.Choosing is not null)
        {
            return RenderChoice(model, endpoint, width, rows);
        }

        var lines = new List<string>
        {
            SpreadLR(
                $"[{ScreenPalette.Accent}]WORLDS[/]  [{ScreenPalette.Label}]search[/] [{ScreenPalette.Value}]{Escape(query)}▏[/]",
                $"[{ScreenPalette.Label}]{Escape(Join(model.CountLine(query), WorldBrowseModel.SortName(sort)))}[/]",
                width),
            ElideVisible($"[{ScreenPalette.Muted}]{Escape(WorldBrowseModel.AttributionLine)}[/]", width),
            string.Empty,
        };

        var view = model.View(query, sort);
        var note = model.Opening is not null ? model.OpeningLine
            : model.State == WorldBrowseState.Ready && model.Problem is { } problem ? problem
            : model.State == WorldBrowseState.Ready && view.Count > 0 ? model.MarksNote + WorldBrowseModel.PickNote
            : string.Empty;
        var noteTone = model.State == WorldBrowseState.Ready && model.Problem is not null && model.Opening is null ? ScreenPalette.Caution : ScreenPalette.Label;
        var noteRows = NoteRows(note, width, rows - lines.Count - 1 - MinListRows);
        var body = rows - lines.Count - 1 - Math.Max(1, noteRows.Count);
        if (model.Empty(query) is { } empty)
        {
            // Wrapped, not elided: a refusal cut mid-sentence ("the answer was to…") does not say why.
            var tone = model.State is WorldBrowseState.Failed or WorldBrowseState.Unavailable ? ScreenPalette.Caution : ScreenPalette.Label;
            lines.AddRange(NoteRows(empty, width - 2, body).Select(row => $"  [{tone}]{Escape(row)}[/]"));
        }
        else
        {
            var nameWidth = Math.Clamp(view.Max(r => r.Name.Length) + 1, 12, Math.Max(12, width * 30 / 100));
            var codebaseWidth = Math.Clamp(view.Max(r => r.Codebase.Length) + 1, 8, 14);
            var first = Math.Clamp(selected - (body / 2), 0, Math.Max(0, view.Count - body));
            for (var i = first; i < view.Count && i < first + body; i++)
            {
                lines.Add(Row(view[i], i == selected, width, nameWidth, codebaseWidth));
            }
        }

        AddNote(lines, noteRows, noteTone, rows);
        lines.Add(Footer(width));
        return lines;
    }

    /// <summary>
    /// A picked world's addresses, row <paramref name="endpoint"/> marked, in exactly <paramref name="rows"/> rows: whose
    /// they are, what mu*index says of its charset, one row per address (<c>host:port</c>, TLS or plain, its state), a note
    /// and the keys.
    /// </summary>
    private static List<string> RenderChoice(WorldBrowseModel model, int endpoint, int width, int rows)
    {
        var game = model.Choosing!;
        var lines = new List<string>
        {
            ElideVisible($"[{ScreenPalette.Accent}]ADDRESSES[/]  [{ScreenPalette.Value}]{Escape(model.ChoiceLine)}[/]", width),
            ElideVisible($"[{ScreenPalette.Muted}]{Escape(WorldBrowseModel.AttributionLine)}[/]", width),
            string.Empty,
        };

        // The charset sentence on one row where it fits, else broken at its " · " so the encoding is never elided away.
        var charset = model.CharsetLine;
        var pieces = charset.Length <= width - 3 ? [charset] : charset.Split(" · ", 2);
        lines.AddRange(pieces.Select(piece => $"  [{ScreenPalette.Label}]{Escape(Elide(piece, width - 3))}[/]"));
        lines.Add(string.Empty);

        var columns = game.Endpoints.Select(WorldBrowseModel.Endpoint).ToArray();
        var addressWidth = Math.Clamp(columns.Max(c => c.Address.Length) + 2, 12, Math.Max(12, width / 2));
        var noteRows = NoteRows(model.Problem ?? WorldBrowseModel.ChoiceNote, width, rows - lines.Count - 1 - Math.Min(columns.Length, MinListRows));
        for (var i = 0; i < columns.Length && lines.Count < rows - 1 - noteRows.Count; i++)
        {
            var (address, security, state) = columns[i];
            var chosen = i == endpoint;
            var mark = chosen ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            var weight = chosen ? "bold " : string.Empty;
            var stateTone = game.Endpoints[i].Stale || !game.Endpoints[i].Connectable ? ScreenPalette.Caution : ScreenPalette.Label;
            var stateText = Elide(state, Math.Max(0, width - 4 - addressWidth - 7));
            lines.Add($"{mark} [{weight}{ScreenPalette.Value}]{Escape(Elide(address, addressWidth - 1).PadRight(addressWidth))}[/]"
                + $"[{(game.Endpoints[i].Tls ? ScreenPalette.Accent : ScreenPalette.Label)}]{Escape(security.PadRight(7))}[/]"
                + $"[{stateTone}]{Escape(stateText)}[/]");
        }

        AddNote(lines, noteRows, model.Problem is null ? ScreenPalette.Label : ScreenPalette.Caution, rows);
        lines.Add(ChoiceFooter(width));
        return lines;
    }

    /// <summary>The list rows a note may not take: below this many it is elided rather than wrapped further.</summary>
    private const int MinListRows = 3;

    /// <summary>The note above the keys at most <see cref="MaxNoteRows"/> rows: at 60 columns the pick note is three.</summary>
    private const int MaxNoteRows = 3;

    /// <summary>
    /// <paramref name="note"/> broken to <paramref name="width"/> (less a cell) in at most <paramref name="most"/> rows (never
    /// fewer than one nor more than <see cref="MaxNoteRows"/>), the last elided only if it still does not fit — at 80 columns
    /// the pick note (which says nothing is saved before the form's ⏎) and a refusal were cut mid-sentence, and at 60 the
    /// ⏎ itself. Empty for an empty note.
    /// </summary>
    private static List<string> NoteRows(string note, int width, int most)
    {
        if (note.Length == 0)
        {
            return [];
        }

        most = Math.Clamp(most, 1, MaxNoteRows);
        var wrapped = note.Length <= width - 1 ? [note] : HelpSurface.Wrap(note, width - 1).ToList();
        if (wrapped.Count > most)
        {
            var tail = string.Join(' ', wrapped.Skip(most - 1));
            wrapped = [.. wrapped.Take(most - 1), Elide(tail, width - 1)];
        }

        return wrapped;
    }

    /// <summary>The note's rows just above the key row, closing <paramref name="lines"/> at <paramref name="rows"/> less it.</summary>
    private static void AddNote(List<string> lines, List<string> note, string tone, int rows)
    {
        Pad(lines, rows - 1 - Math.Max(1, note.Count));
        if (note.Count == 0)
        {
            lines.Add(string.Empty);
            return;
        }

        lines.AddRange(note.Select(row => $"[{tone}]{Escape(row)}[/]"));
    }

    /// <summary>The address choice's key row, laddered to one row: the choice and the way back are never given up.</summary>
    internal static string ChoiceFooter(int width)
    {
        var keys = WorldBrowseModel.ChoiceKeys.Select(k => k.Key switch
        {
            "⏎" or "Esc" => new LadderKey(k.Key, k.Label),
            _ => new LadderKey(k.Key, k.Label, Drop: 1),
        }).ToList();
        return KeyLadder.Fit(keys, width - 1, k => $"[{ScreenPalette.Accent}]{Escape(k)}[/]", l => $"[{ScreenPalette.Label}]{Escape(l)}[/]", $"[{ScreenPalette.Label}] · [/]");
    }

    /// <summary>The key row, laddered to one row: the way out and the pick are never given up.</summary>
    internal static string Footer(int width)
    {
        var keys = WorldBrowseModel.Keys.Select(k => k.Key switch
        {
            "⏎" or "Esc" => new LadderKey(k.Key, k.Label),
            "⇥" => new LadderKey(k.Key, k.Label, Drop: 1),
            _ => new LadderKey(k.Key, k.Label, Drop: 2),
        }).ToList();
        keys.Insert(0, new LadderKey("type", "to search", Drop: 0));
        return KeyLadder.Fit(keys, width - 1, k => $"[{ScreenPalette.Accent}]{Escape(k)}[/]", l => $"[{ScreenPalette.Label}]{Escape(l)}[/]", $"[{ScreenPalette.Label}] · [/]");
    }

    private static string Row(WorldListing row, bool selected, int width, int nameWidth, int codebaseWidth)
    {
        var mark = selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
        var weight = selected ? "bold " : string.Empty;
        var players = WorldBrowseModel.Players(row).PadLeft(5);
        var tagline = Elide(row.Tagline, Math.Max(0, width - 2 - nameWidth - 6 - codebaseWidth - 1));
        return $"{mark} [{weight}{ScreenPalette.Value}]{Escape(Elide(row.Name, nameWidth - 1).PadRight(nameWidth))}[/]"
            + $"[{ScreenPalette.Accent}]{Escape(players)}[/] "
            + $"[{ScreenPalette.Label}]{Escape(Elide(row.Codebase, codebaseWidth - 1).PadRight(codebaseWidth))}[/]"
            + $"[{ScreenPalette.Value}]{Escape(tagline)}[/]";
    }

    private static string Join(string a, string b) => a.Length == 0 ? b : $"{a} · {b}";

    private static string Elide(string text, int width) => MarkupText.ElidePlain(text, width);

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
}
