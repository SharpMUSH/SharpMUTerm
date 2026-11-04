using SharpMUTerm.Client.Modals;
using static SharpMUTerm.Tui.MarkupText;
using static SharpMUTerm.Tui.ScreenPalette;

namespace SharpMUTerm.Tui;

/// <summary>What one keystroke means to the open search surface.</summary>
internal enum SearchAction
{
    /// <summary>Nothing. The key is swallowed and the surface stays exactly as it is.</summary>
    None,

    /// <summary>The query, a toggle or the pointed-at row changed; re-search and redraw.</summary>
    Redraw,

    /// <summary>Go to the pointed-at line: activate its window, mark it, and close.</summary>
    Go,

    /// <summary>Close, changing nothing.</summary>
    Cancel,
}

/// <summary>The answer to one keystroke: what to do, and the state it leaves behind.</summary>
internal readonly record struct SearchDecision(
    SearchAction Action, string Query, int Selected, bool Regex, bool AllWindows, bool Older = false);

/// <summary>
/// The search surface (⌃F), minus the window: what a keystroke means to it, and what it says. Pure, for
/// the same reason <see cref="HistorySearchPrompt"/> is — the rules and the wording are exactly the part
/// a headless test can pin, and <see cref="SearchSurface"/> is left with nothing but framework calls.
/// <para>
/// <b>A results surface and not an in-pane bar.</b> A <c>less</c>-style bar under the pane has nowhere to
/// say <em>which</em> pane a hit is in, and searching more than one window is half of what was asked for.
/// This shape also inherits ⌃R's interaction, which the client already teaches.
/// </para>
/// <para>
/// <b>The header states the bound it searched.</b> ⌃F sees the lines the client is holding — the pane
/// buffer — and not a session's whole scrollback or the file-backed spill, because a spawn window's lines
/// exist in neither. So the count reads <c>12 of 38 · 4,812 lines held</c>: a reader who cannot find
/// something from an hour ago can see why on the frame rather than guessing.
/// </para>
/// <para>
/// <b>The footer names only keys that work.</b> ↑↓ move (wrapping, as ⌃P's and ⌃R's do), ⏎ goes, ⌥E
/// switches regex on and off, ⌥A widens to every window, Esc cancels and ⌃F closes. Printable characters
/// filter and ⌫ un-filters. Nothing else is bound, so nothing else is advertised — see
/// <c>SearchPromptTests</c>, which presses every key this string names.
/// </para>
/// </summary>
internal static class SearchPrompt
{
    /// <summary>
    /// The keys, and the whole set of them. Every one is honoured by <see cref="Interpret"/>. ⌫ is
    /// deliberately absent for <see cref="HistorySearchPrompt"/>'s reason: it is named on the
    /// <c>no matches</c> line instead, which is the only moment a reader needs telling that the query can
    /// be widened.
    /// </summary>
    internal const string Hints =
        "type to search · ↑↓ pick · ⏎ go · ⌥E regex · ⌥A all windows · ⌥H older history · Esc cancel · ⌃F closes";

    /// <summary>
    /// The footer's drop-order ladder, longest first: the status row's rule (<c>FocusHints</c>). <see cref="Hints"/>
    /// is 84 cells and an 80-column surface holds 74, where it used to be cut to a bare <c>⌃F</c>. The prose goes
    /// first, then the words after the toggles' keys, then Esc (⌃F also closes); every rung still names each key
    /// that does something different.
    /// </summary>
    internal static readonly string[] HintLadder =
    [
        Hints,
        "↑↓ pick · ⏎ go · ⌥E regex · ⌥A all windows · ⌥H older history · Esc cancel · ⌃F closes",
        "↑↓ pick · ⏎ go · ⌥E regex · ⌥A all · ⌥H older · Esc cancel · ⌃F closes",
        "↑↓ · ⏎ go · ⌥E regex · ⌥A all · ⌥H older · Esc · ⌃F",
    ];

    /// <summary>The longest rung of <see cref="HintLadder"/> that fits <paramref name="width"/> (all of it when unknown).</summary>
    internal static string HintsFor(int width) =>
        width <= 0 ? Hints : FirstFitting(width, HintLadder);

    /// <summary>The longest an entry is drawn before it is elided, when no width is supplied.</summary>
    private const int DefaultEntryWidth = 72;

    /// <summary>
    /// The widest the window column is drawn in <c>⌥A</c> scope. Every row is padded to the widest label
    /// in the list, so an unbounded column is a column one long title can spend the whole row on — and a
    /// title is not this client's text to trust: <c>Snippet</c> caps it at sixty cells, which was sixty
    /// cells of blank against a twenty-cell result. A label past this is elided, which is the rail's rule
    /// (<c>RailRenderer</c>) one surface over.
    /// </summary>
    internal const int MaxLabelWidth = 18;

    /// <summary>
    /// What one keystroke does, and the state it leaves behind. <paramref name="count"/> is how many rows
    /// are listed, so the pointer wraps within what is actually on screen.
    /// <para>
    /// Anything unrecognised is swallowed rather than passed down: a modal surface that let stray keys
    /// through would be typing into the command line the reader cannot currently see.
    /// </para>
    /// </summary>
    internal static SearchDecision Interpret(
        ConsoleKeyInfo key, string query, int selected, int count, bool regex, bool all, bool older = false)
    {
        var state = new SearchDecision(SearchAction.None, query, selected, regex, all, older);

        if (key.Modifiers.HasFlag(ConsoleModifiers.Control))
        {
            // ⌃F is spelled out here as well as in the surface's toggle, for QuitPrompt's reason: this is
            // where "what does this keystroke mean" is answered, and a rule living only in the app's
            // shortcut table could not be read back by a test. In the running client a global shortcut
            // runs before any window, so the chord never reaches this handler — the toggle answers it,
            // the same way.
            return key.Key == ConsoleKey.F ? state with { Action = SearchAction.Cancel } : state;
        }

        if (key.Modifiers.HasFlag(ConsoleModifiers.Alt))
        {
            // The two toggles. Both re-search from the top, because both change what the list *is* — a
            // pointer kept at row 12 of a different result set points at nothing the reader chose.
            return key.Key switch
            {
                ConsoleKey.E => state with { Action = SearchAction.Redraw, Regex = !regex, Selected = 0 },
                ConsoleKey.A => state with { Action = SearchAction.Redraw, AllWindows = !all, Selected = 0 },

                // ⌥H: older history too — the sessions' scrollback and its spill, searched off the UI thread and listed
                // after the pane's rows as they are found. H for history; nothing in the app's shortcuts takes it.
                ConsoleKey.H => state with { Action = SearchAction.Redraw, Older = !older },
                _ => state,
            };
        }

        switch (key.Key)
        {
            case ConsoleKey.Escape:
                return state with { Action = SearchAction.Cancel };

            case ConsoleKey.Enter:
                // With nothing listed there is nowhere to go, so ⏎ does nothing and leaves the surface
                // up: the query is what needs fixing, and Esc is the advertised way out.
                return count == 0 ? state : state with { Action = SearchAction.Go };

            case ConsoleKey.UpArrow:
                return state with { Action = SearchAction.Redraw, Selected = Step(selected, -1, count) };

            case ConsoleKey.DownArrow:
                return state with { Action = SearchAction.Redraw, Selected = Step(selected, 1, count) };

            case ConsoleKey.Backspace:
                // The pointer goes back to the first match: widening the query changes what row 0 is.
                return query.Length == 0
                    ? state
                    : state with { Action = SearchAction.Redraw, Query = query[..^1], Selected = 0 };
        }

        // Typing searches. Control characters are excluded so an undecoded sequence cannot end up in the
        // query, and Tab (which arrives as one) is swallowed rather than becoming whitespace.
        if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0')
        {
            return state with { Action = SearchAction.Redraw, Query = query + key.KeyChar, Selected = 0 };
        }

        return state;
    }

    /// <summary>Moves the pointer, wrapping — the same behaviour ⌃P's and ⌃R's lists have.</summary>
    private static int Step(int selected, int delta, int count) =>
        count == 0 ? -1 : ((selected + delta) % count + count) % count;

    /// <summary>
    /// The whole surface as markup lines: the query line with its state, the rows, and the keys.
    /// <para>
    /// <paramref name="listRows"/> is how many rows the list area holds and <paramref name="first"/> which
    /// match is at the top of it, which is what makes this a viewport rather than a dump. The area is
    /// always drawn at full height — padded with blank rows — so the footer does not walk up and down the
    /// screen on every keystroke.
    /// </para>
    /// </summary>
    /// <param name="rows">The matches, in buffer order.</param>
    /// <param name="query">The query as typed so far.</param>
    /// <param name="error">Why the query could not be used, or null.</param>
    /// <param name="regex">Whether the query is being read as a pattern.</param>
    /// <param name="all">Whether every window is being searched, rather than the focused one.</param>
    /// <param name="scope">What the focused window is called, for the one-window case.</param>
    /// <param name="held">How many lines were searched — the bound, stated rather than implied.</param>
    /// <param name="selected">Which row the pointer is on.</param>
    /// <param name="width">The surface's content width, when known.</param>
    /// <param name="listRows">How many rows the list area holds; zero means "as many as there are".</param>
    /// <param name="first">Which match is drawn at the top of the list area.</param>
    internal static List<string> Render(
        IReadOnlyList<SearchRow> rows,
        string query,
        string? error,
        bool regex,
        bool all,
        string scope,
        int held,
        int selected,
        int width = 0,
        int listRows = 0,
        int first = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(query);
        return Render(new SearchModel(query, regex, all, scope, held, error, rows), selected, width, listRows, first);
    }

    /// <summary>
    /// The surface for <paramref name="model"/> — the cells of the model the window's native search draws too.
    /// </summary>
    /// <param name="model">The query, the toggles, the bound and the matches.</param>
    /// <param name="selected">Which row the pointer is on.</param>
    /// <param name="width">The surface's content width, when known.</param>
    /// <param name="listRows">How many rows the list area holds; zero means "as many as there are".</param>
    /// <param name="first">Which match is drawn at the top of the list area.</param>
    internal static List<string> Render(SearchModel model, int selected, int width = 0, int listRows = 0, int first = 0)
    {
        ArgumentNullException.ThrowIfNull(model);
        var rows = model.Rows;
        var query = model.Query;
        var all = model.AllWindows;

        var labelWidth = all || rows.Any(r => r.IsOlder)
            ? Math.Min(MaxLabelWidth, rows.Select(r => RowLabel(r, all).Length).DefaultIfEmpty(0).Max())
            : 0;
        var entryWidth = (width > 0 ? width : DefaultEntryWidth) - 4 - (labelWidth > 0 ? labelWidth + 2 : 0);

        var lines = new List<string>
        {
            SpreadLR($"[{Label}]search[/] {QueryMarkup(query)}", $"[{Label}]{Escape(model.StateText)}[/]", width),
            SpreadLR(
                model.HistoryNote is { } note ? $"[{(model.HistoryRunning ? Accent : Muted)}]{Escape(note)}[/]" : string.Empty,
                $"[{Label}]{Escape(model.CountText)}[/]",
                width),
        };

        var body = new List<string>();
        if (model.EmptyText is { } empty)
        {
            body.Add($"[{Muted}]  {Escape(empty)}[/]");
        }

        var top = Math.Clamp(first, 0, Math.Max(0, rows.Count - 1));
        var last = listRows > 0 ? Math.Min(rows.Count, top + listRows - body.Count) : rows.Count;
        for (var i = top; i < last; i++)
        {
            body.Add(Row(rows[i], i == selected, entryWidth, labelWidth, width, all));
        }

        while (body.Count < listRows)
        {
            body.Add(string.Empty);
        }

        lines.AddRange(body);
        lines.Add(string.Empty);
        lines.Add($"[{Label}]{Escape(HintsFor(width))}[/]");
        return lines;
    }

    /// <summary>
    /// Where the list area's top must sit for <paramref name="selected"/> to be inside it, moving as
    /// little as possible from <paramref name="first"/>.
    /// </summary>
    internal static int Scroll(int first, int selected, int count, int listRows)
    {
        if (listRows <= 0 || count <= listRows || selected < 0)
        {
            return 0;
        }

        var top = Math.Clamp(first, selected - listRows + 1, selected);
        return Math.Clamp(top, 0, count - listRows);
    }

    /// <summary>
    /// The query, with the accent block caret after it — the same caret the settings screens draw on an
    /// open field, so this reads as something being typed into.
    /// </summary>
    private static string QueryMarkup(string query) =>
        query.Length == 0
            ? $"[{Ink} on {Accent}] [/]"
            : $"[{Value}]{Escape(query)}[/][{Ink} on {Accent}] [/]";

    /// <summary>
    /// What a row's window column says: the window's name in ⌥A scope, and <c>older</c> on a row from older history — the
    /// only place a reader can tell a line the pane holds from one ⏎ will open in the history view.
    /// </summary>
    private static string RowLabel(SearchRow row, bool all) =>
        row.IsOlder ? (all ? $"{row.WindowLabel} · older" : "older") : all ? row.WindowLabel : string.Empty;

    private static string Row(SearchRow row, bool selected, int entryWidth, int labelWidth, int width, bool all)
    {
        var (text, matchStart, matchLength) = Elide(row, entryWidth);
        var label = labelWidth > 0 ? LabelCell(RowLabel(row, all), labelWidth) : string.Empty;

        if (selected)
        {
            // One continuous accent bar across the row, padded to the surface width. The matched run is
            // not separately marked here: it would be a highlight inside a highlight, and the pointer is
            // the fact this row is drawing.
            var body = $" ▸ {label}{Escape(text)} ";
            var pad = width > VisibleLength(body) ? new string(' ', width - VisibleLength(body)) : string.Empty;
            return $"[{Ink} on {Accent}]{body}{pad}[/]";
        }

        var prefix = labelWidth > 0 ? $"[{Muted}]   {label}[/]" : "   ";
        if (matchStart < 0 || matchLength == 0)
        {
            return $"{prefix}[{Value}]{Escape(text)}[/]";
        }

        // Mark where the query matched, so a row shows *why* it is in the list.
        var before = Escape(text[..matchStart]);
        var hit = Escape(text.Substring(matchStart, matchLength));
        var after = Escape(text[(matchStart + matchLength)..]);
        return $"{prefix}[{Value}]{before}[/][bold {Accent}]{hit}[/][{Value}]{after}[/]";
    }

    /// <summary>
    /// One window-column cell: the label elided to the column and padded out to it, then the two-cell
    /// gap. Padded by <em>visible</em> width, because a window may be called <c>[Chat]</c> and an escaped
    /// bracket is two characters standing for one cell — <c>PadRight</c> shortened the column by one per
    /// bracket and left that row's text a cell adrift of every other row's.
    /// </summary>
    private static string LabelCell(string label, int labelWidth)
    {
        var shown = label.Length <= labelWidth ? label : label[..(labelWidth - 1)] + "…";
        return PadVisible(Escape(shown), labelWidth) + "  ";
    }

    /// <summary>
    /// Shortens an over-long line to the surface's width, keeping the matched run visible: a pose is
    /// hundreds of cells long, and a row clipped at the left edge would hide the very text the query
    /// found.
    /// </summary>
    private static (string Text, int MatchStart, int MatchLength) Elide(SearchRow row, int entryWidth)
    {
        const string Ellipsis = "…";
        var text = row.Text;
        if (entryWidth <= Ellipsis.Length || text.Length <= entryWidth)
        {
            return (text, row.MatchStart, row.MatchLength);
        }

        var matchEnd = row.MatchStart < 0 ? 0 : row.MatchStart + row.MatchLength;
        var start = Math.Max(0, Math.Min(matchEnd - entryWidth + Ellipsis.Length, text.Length - entryWidth));
        var length = Math.Min(entryWidth - Ellipsis.Length, text.Length - start);

        var head = start > 0 ? Ellipsis : string.Empty;
        var tail = start + length < text.Length ? Ellipsis : string.Empty;
        var shown = head + text.Substring(start, length) + tail;

        if (row.MatchStart < 0)
        {
            return (shown, -1, 0);
        }

        var shifted = row.MatchStart - start + head.Length;
        var visible = Math.Max(0, Math.Min(row.MatchLength, shown.Length - tail.Length - shifted));
        return shifted < 0 ? (shown, -1, 0) : (shown, shifted, visible);
    }
}
