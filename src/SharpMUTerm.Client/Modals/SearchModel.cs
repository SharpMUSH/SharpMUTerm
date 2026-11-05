using System.Globalization;

namespace SharpMUTerm.Client.Modals;

/// <summary>
/// One row of ⌃F's results: which window the line is in, where it sits in that window's buffer, and where the query
/// landed inside it.
/// </summary>
/// <remarks>
/// A row from older history (⌥H) has <see cref="History"/> at the line's absolute index in its session's scrollback and
/// <see cref="LineIndex"/> −1: it is not in the pane buffer, so ⏎ opens it in the read-only history view (or scrolls the
/// pane to it when the pane still holds it). Its <see cref="Age"/> says how far back it is (<see cref="SearchModel.AgeLabel"/>).
/// </remarks>
public readonly record struct SearchRow(
    string WindowId,
    string WindowLabel,
    int LineIndex,
    string Text,
    int MatchStart,
    int MatchLength,
    long History = -1,
    string? SessionKey = null,
    string? Age = null)
{
    /// <summary>Whether this row came from older history rather than the pane buffer.</summary>
    public bool IsOlder => History >= 0;
}

/// <summary>
/// ⌃F's results for one query — the search surface's model, drawn by the cell surface (<c>SearchPrompt.Render</c>)
/// and by the window's native one. Computed by the client over the pane buffers, re-read on every change, so the two
/// list the same hits for the same query and ⏎ on row N lands on the same line in both.
/// </summary>
/// <param name="Query">The query as typed so far.</param>
/// <param name="Regex">Whether the query is read as a pattern (⌥E).</param>
/// <param name="AllWindows">Whether every window is searched rather than the focused one (⌥A).</param>
/// <param name="Scope">What the focused window is called, for the one-window case.</param>
/// <param name="Held">How many lines were searched: the bound, stated rather than implied.</param>
/// <param name="Error">Why the query could not be used (an unparseable pattern), or null.</param>
/// <param name="Rows">The matches, in buffer order; then, with <paramref name="Older"/>, older history's, newest first.</param>
/// <param name="Older">Whether older history is searched too (⌥H): the session's scrollback and its spill.</param>
/// <param name="HistoryNote">
/// What older history holds or how far its search has got (<c>⌥H searches older history · 38,000 lines in Corvid</c>,
/// <c>older history · 7 found in 38,000 lines</c>), or why there is none (a capture window has no spill); null for nothing to say.
/// </param>
/// <param name="HistoryRunning">Whether the older-history search is still reading.</param>
public sealed record SearchModel(
    string Query,
    bool Regex,
    bool AllWindows,
    string Scope,
    int Held,
    string? Error,
    IReadOnlyList<SearchRow> Rows,
    bool Older = false,
    string? HistoryNote = null,
    bool HistoryRunning = false)
{
    /// <summary>The toggles and what they mean now, in words: <c>text · Corvid</c>, <c>regex · every window · older history</c>.</summary>
    public string StateText => $"{(Regex ? "regex" : "text")} · {(AllWindows ? "every window" : Scope)}{(Older ? " · older history" : string.Empty)}";

    /// <summary>
    /// How many lines the scrollback behind the searched windows holds that their panes no longer do — what the pane cap
    /// trimmed, and so what this search could not see. Set only while ⌥H is off; 0 when the panes hold everything.
    /// </summary>
    public long OlderLines { get; init; }

    /// <summary>Whose older history <see cref="OlderLines"/> counts: the window's name, or <c>3 windows</c>.</summary>
    public string OlderSource { get; init; } = string.Empty;

    /// <summary>
    /// Whether the status line offers ⌥H: a query was searched, ⌥H is off, and the pane cap trimmed lines the scrollback
    /// still holds, so this search found fewer hits than it might. A hint on the line under the query, never a modal.
    /// </summary>
    public bool OffersOlder => !Older && Error is null && Query.Length > 0 && OlderLines > 0;

    /// <summary>The offer's chord, which a host draws as a key.</summary>
    public const string OlderKey = "⌥H";

    /// <summary>
    /// The offer, longest first, for a status line too narrow for all of it: whose history and how much, then how much,
    /// then the offer alone, then the chord and two words. Every rung starts with <see cref="OlderKey"/>. Empty when
    /// nothing is offered.
    /// </summary>
    public IReadOnlyList<string> OlderHintLadder
    {
        get
        {
            if (!OffersOlder)
            {
                return [];
            }

            var lines = OlderLines.ToString("N0", CultureInfo.InvariantCulture);
            var more = $"{lines} more line{(OlderLines == 1 ? string.Empty : "s")}";
            return
            [
                OlderSource.Length > 0 ? $"{OlderKey} searches older history · {more} in {OlderSource}" : $"{OlderKey} searches older history · {more}",
                $"{OlderKey} searches older history · {more}",
                $"{OlderKey} searches older history",
                $"{OlderKey} older history",
            ];
        }
    }

    /// <summary>
    /// The status line's note in <paramref name="room"/> cells (0 for no limit): the longest rung of
    /// <see cref="OlderHintLadder"/> that fits while ⌥H is offered (its last rung if none does), else <see cref="HistoryNote"/>.
    /// </summary>
    public string? NoteFor(int room)
    {
        var ladder = OlderHintLadder;
        if (ladder.Count == 0)
        {
            return HistoryNote;
        }

        if (room <= 0)
        {
            return HistoryNote ?? ladder[0];
        }

        return ladder.FirstOrDefault(r => r.Length <= room) ?? ladder[^1];
    }

    /// <summary>How many rows are the pane buffer's (the rest are older history's, listed after them).</summary>
    public int HeldRows => Rows.Count(r => !r.IsOlder);

    /// <summary>How many older-history lines matched so far (⌥H): set by the client as the search streams in.</summary>
    public int OlderFound { get; init; }

    /// <summary>How many older-history lines have been read so far.</summary>
    public long OlderSearched { get; init; }

    /// <summary>How many lines the older-history search covers in all.</summary>
    public long OlderTotal { get; init; }

    /// <summary>Whether older history found more than the rows it lists.</summary>
    public bool OlderCapped { get; init; }

    /// <summary>
    /// How many lines matched out of how many were looked at, on one row — <c>12 found · 4,812 lines held</c>, and with
    /// older history (⌥H) both bounds side by side: <c>this window 3 of 39 · older 4 of 6,000</c> (while it reads,
    /// <c>older 2… 1,200 of 6,000 read</c>) — so a reader who cannot find an old line sees the bound rather than concluding
    /// the search is broken, and never two counts competing for the same row.
    /// </summary>
    public string CountText
    {
        get
        {
            var searched = $"{Held:n0} line{(Held == 1 ? string.Empty : "s")} held";
            if (Error is not null)
            {
                return searched;
            }

            if (!Older || OlderTotal <= 0)
            {
                return $"{HeldRows} found · {searched}";
            }

            var here = $"{(AllWindows ? "these windows" : "this window")} {HeldRows} of {Held:n0}";
            var older = HistoryRunning
                ? $"older {OlderFound}… {OlderSearched:n0} of {OlderTotal:n0} read"
                : $"older {OlderFound}{(OlderCapped ? "+" : string.Empty)} of {OlderTotal:n0}";
            return $"{here} · {older}";
        }
    }

    /// <summary>
    /// What a row's window column says, in both hosts: the window's name in ⌥A scope; on an older-history row how far back
    /// it is (<see cref="SearchRow.Age"/>); and <c>here</c> on a row the pane holds once older rows are listed beside it.
    /// </summary>
    public string LabelFor(SearchRow row)
    {
        if (row.IsOlder)
        {
            var age = row.Age ?? "older";
            return AllWindows ? $"{row.WindowLabel} · {age}" : age;
        }

        var olderListed = Rows.Any(r => r.IsOlder);
        return AllWindows ? (olderListed ? $"{row.WindowLabel} · here" : row.WindowLabel) : olderListed ? "here" : string.Empty;
    }

    /// <summary>Whether the rows carry a window column at all: in ⌥A scope, or once older history lists a row.</summary>
    public bool Labelled => AllWindows || Rows.Any(r => r.IsOlder);

    /// <summary>
    /// How far back an older-history line is, from how many lines arrived after it: <c>40 lines ago</c>, <c>1.6k lines ago</c>.
    /// The scrollback keeps no clock, so the age is in lines — the unit the history view's position says too.
    /// </summary>
    public static string AgeLabel(long linesAfter)
    {
        linesAfter = Math.Max(0, linesAfter);
        return linesAfter switch
        {
            0 => "the newest",
            1 => "1 line ago",
            < 1_000 => $"{linesAfter} lines ago",
            < 100_000 => (linesAfter / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k lines ago",
            _ => (linesAfter / 1000).ToString(CultureInfo.InvariantCulture) + "k lines ago",
        };
    }

    /// <summary>What the list says when it lists nothing, or null when it lists something.</summary>
    public string? EmptyText =>
        Error is not null ? Error
        : Query.Length == 0 ? $"type to search {(AllWindows ? "every window" : Scope)}"
        : Rows.Count == 0 && HistoryRunning ? "nothing in the lines held — searching older history…"
        : Rows.Count == 0 ? (Older ? "no matches here or in older history — ⌫ widens the query" : "no matches — ⌫ widens the query")
        : null;

    public bool Equals(SearchModel? other) =>
        other is not null
        && Query == other.Query && Regex == other.Regex && AllWindows == other.AllWindows
        && Scope == other.Scope && Held == other.Held && Error == other.Error
        && Older == other.Older && HistoryNote == other.HistoryNote && HistoryRunning == other.HistoryRunning
        && OlderLines == other.OlderLines && OlderSource == other.OlderSource
        && OlderFound == other.OlderFound && OlderSearched == other.OlderSearched && OlderTotal == other.OlderTotal && OlderCapped == other.OlderCapped
        && ModelLists.Same(Rows, other.Rows);

    public override int GetHashCode() => HashCode.Combine(Query, Regex, AllWindows, Scope, Held, Error, ModelLists.Hash(Rows));
}

/// <summary>
/// One opening of ⌃F in a host's own controls. The host types a query and flips the toggles (<see cref="Search"/>);
/// the client re-runs the search on its own thread and publishes the results. <see cref="Go"/> takes the reader to a
/// row of the model last published, through the client's one path to a hit.
/// </summary>
public sealed class SearchSession : ModalSession<SearchModel>
{
    private readonly Action<string, bool, bool, bool> _search;
    private readonly Action<SearchModel, int> _go;

    /// <param name="model">The empty search it opens on.</param>
    /// <param name="isDark">Whether the client's theme is a dark one.</param>
    /// <param name="search">Re-runs the search for a query and the three toggles (regex, every window, older history); called from any thread.</param>
    /// <param name="go">Goes to row N of the model handed with it, after the surface has closed.</param>
    /// <param name="closed">Told the surface closed.</param>
    public SearchSession(SearchModel model, bool isDark, Action<string, bool, bool, bool> search, Action<SearchModel, int> go, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(go);
        _search = search;
        _go = go;
    }

    public override string Surface => "search";

    /// <summary>The host's: the query or a toggle changed. Older history stays as the model has it.</summary>
    public void Search(string query, bool regex, bool allWindows) => Search(query, regex, allWindows, Model.Older);

    /// <summary>The host's: the query or a toggle changed, older history (⌥H) included.</summary>
    public void Search(string query, bool regex, bool allWindows, bool older)
    {
        if (!IsEnded)
        {
            _search(query ?? string.Empty, regex, allWindows, older);
        }
    }

    /// <summary>The host's: ⏎ on row <paramref name="index"/> of <see cref="ModalSession{TModel}.Model"/>. Ignored with nothing there.</summary>
    public void Go(int index)
    {
        var model = Model;
        if (index >= 0 && index < model.Rows.Count)
        {
            CloseThen(() => _go(model, index));
        }
    }
}
