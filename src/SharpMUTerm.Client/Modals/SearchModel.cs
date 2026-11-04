namespace SharpMUTerm.Client.Modals;

/// <summary>
/// One row of ⌃F's results: which window the line is in, where it sits in that window's buffer, and where the query
/// landed inside it.
/// </summary>
public readonly record struct SearchRow(
    string WindowId, string WindowLabel, int LineIndex, string Text, int MatchStart, int MatchLength);

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
/// <param name="Rows">The matches, in buffer order.</param>
public sealed record SearchModel(
    string Query,
    bool Regex,
    bool AllWindows,
    string Scope,
    int Held,
    string? Error,
    IReadOnlyList<SearchRow> Rows)
{
    /// <summary>The two toggles and what they mean now, in words: <c>text · Corvid</c>, <c>regex · every window</c>.</summary>
    public string StateText => $"{(Regex ? "regex" : "text")} · {(AllWindows ? "every window" : Scope)}";

    /// <summary>
    /// How many lines matched out of how many were looked at — <c>12 found · 4,812 lines held</c> — so a reader who
    /// cannot find an old line sees the bound rather than concluding the search is broken.
    /// </summary>
    public string CountText
    {
        get
        {
            var searched = $"{Held:n0} line{(Held == 1 ? string.Empty : "s")} held";
            return Error is not null ? searched : $"{Rows.Count} found · {searched}";
        }
    }

    /// <summary>What the list says when it lists nothing, or null when it lists something.</summary>
    public string? EmptyText =>
        Error is not null ? Error
        : Query.Length == 0 ? $"type to search {(AllWindows ? "every window" : Scope)}"
        : Rows.Count == 0 ? "no matches — ⌫ widens the query"
        : null;

    public bool Equals(SearchModel? other) =>
        other is not null
        && Query == other.Query && Regex == other.Regex && AllWindows == other.AllWindows
        && Scope == other.Scope && Held == other.Held && Error == other.Error
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
    private readonly Action<string, bool, bool> _search;
    private readonly Action<SearchModel, int> _go;

    /// <param name="model">The empty search it opens on.</param>
    /// <param name="isDark">Whether the client's theme is a dark one.</param>
    /// <param name="search">Re-runs the search for a query and the two toggles; called from any thread.</param>
    /// <param name="go">Goes to row N of the model handed with it, after the surface has closed.</param>
    /// <param name="closed">Told the surface closed.</param>
    public SearchSession(SearchModel model, bool isDark, Action<string, bool, bool> search, Action<SearchModel, int> go, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(go);
        _search = search;
        _go = go;
    }

    public override string Surface => "search";

    /// <summary>The host's: the query or a toggle changed.</summary>
    public void Search(string query, bool regex, bool allWindows)
    {
        if (!IsEnded)
        {
            _search(query ?? string.Empty, regex, allWindows);
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
