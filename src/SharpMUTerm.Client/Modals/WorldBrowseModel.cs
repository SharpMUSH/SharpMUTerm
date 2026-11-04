using System.Globalization;
using SharpMUTerm.Core.WorldIndex;

namespace SharpMUTerm.Client.Modals;

/// <summary>Where Browse worlds is: asking the directory, listing what it said, failed, or not able to ask at all.</summary>
public enum WorldBrowseState
{
    /// <summary>The list is being fetched.</summary>
    Loading,

    /// <summary>The rows are in.</summary>
    Ready,

    /// <summary>The directory did not answer, or answered with something the client cannot read.</summary>
    Failed,

    /// <summary>This run asks nothing (no fetch, or no https directory set), and says why.</summary>
    Unavailable,
}

/// <summary>
/// Browse worlds: the rows mu*index listed (<see cref="WorldDirectory"/>), and what the surface says about them. Drawn by
/// the cell surface (<c>WorldBrowseSurface.Render</c>) and by the window's native one; both list <see cref="View"/> for
/// what the player typed and the order chosen, so a query lists the same rows in both. What is typed, the order and the
/// row selected are each host's own; the model is what the client knows.
/// </summary>
/// <param name="State">Where the list is.</param>
/// <param name="Rows">Every row the directory sent, in its order (most played first).</param>
/// <param name="Total">How many worlds the directory holds for the question asked.</param>
/// <param name="AskedFor">The words the directory was last asked to search for, or empty for its most played.</param>
/// <param name="Problem">Why the list failed or cannot be asked, or why the last pick did not open; null when nothing.</param>
/// <param name="Opening">The world whose address is being asked for after a pick, or null.</param>
public sealed record WorldBrowseModel(
    WorldBrowseState State,
    IReadOnlyList<WorldListing> Rows,
    int Total,
    string AskedFor,
    string? Problem,
    string? Opening)
{
    // The surface's words, said once here so both hosts say them alike.

    /// <summary>The surface's title.</summary>
    public const string Heading = "Browse worlds";

    /// <summary>The credit the directory's licence asks for, on every listing.</summary>
    public static string AttributionLine =>
        $"Listing from {WorldDirectory.Name} · data {WorldDirectory.Licence}, {WorldDirectory.Attribution}";

    /// <summary>What the list says while it is fetched.</summary>
    public const string LoadingLine = "Asking mu*index for worlds…";

    /// <summary>What the list says when a pick is waiting on its address.</summary>
    public string OpeningLine => $"Asking mu*index where {Opening} is…";

    /// <summary>The search box's placeholder.</summary>
    public const string SearchPlaceholder = "Type to narrow by name, codebase or tagline";

    /// <summary>What a pick leads to, said under the list.</summary>
    public const string PickNote = "⏎ fills in the connect form; nothing is saved or dialled until you press ⏎ there.";

    /// <summary>What a count the game reports about itself is marked with.</summary>
    public const string DeclaredMark = "~";

    /// <summary>What the mark means, said only while a row carries it.</summary>
    public const string DeclaredNote = "~ a count the game reports about itself";

    /// <summary>The keys that act, in glyphs: the cells ladder them, the window spells them in words.</summary>
    public static IReadOnlyList<(string Key, string Label)> Keys { get; } =
    [
        ("↑↓", "pick"),
        ("⇥", "sort"),
        ("⏎", "connect form"),
        ("Esc", "closes"),
    ];

    /// <summary>A sort's name as both hosts say it.</summary>
    public static string SortName(WorldDirectorySort sort) => sort == WorldDirectorySort.Name ? "by name" : "by players";

    /// <summary>The other sort: what ⇥ and the window's toggle switch to.</summary>
    public static WorldDirectorySort Next(WorldDirectorySort sort) =>
        sort == WorldDirectorySort.Players ? WorldDirectorySort.Name : WorldDirectorySort.Players;

    /// <summary>A row's player count as both hosts write it: <c>12</c>, <c>~12</c> for a declared one, <c>–</c> for none.</summary>
    public static string Players(WorldListing row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.Players is not { } count ? "–"
            : (row.Count == WorldPlayerCount.Declared ? DeclaredMark : string.Empty) + count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The rows for <paramref name="query"/> in <paramref name="sort"/>'s order: each word of the query somewhere in a
    /// row's name, codebase or tagline. By players keeps the directory's order (its uncounted worlds follow as it put
    /// them); by name is alphabetical.
    /// </summary>
    public IReadOnlyList<WorldListing> View(string? query, WorldDirectorySort sort)
    {
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<WorldListing> rows = Rows;
        if (words.Length > 0)
        {
            rows = rows.Where(r => words.All(w =>
                r.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                || r.Codebase.Contains(w, StringComparison.OrdinalIgnoreCase)
                || r.Tagline.Contains(w, StringComparison.OrdinalIgnoreCase)));
        }

        if (sort == WorldDirectorySort.Name)
        {
            rows = rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
        }

        return rows.ToArray();
    }

    /// <summary>
    /// Whether ⏎ on <paramref name="query"/> asks the directory itself: nothing among the rows held matches, and the
    /// directory holds more than it sent, or was last asked something else.
    /// </summary>
    public bool AsksDirectory(string? query)
    {
        var words = (query ?? string.Empty).Trim();
        return State == WorldBrowseState.Ready && words.Length > 0 && View(words, WorldDirectorySort.Players).Count == 0
            && (Total > Rows.Count || !string.Equals(words, AskedFor, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What the list says in place of rows, or null when it has rows to show for <paramref name="query"/>.</summary>
    public string? Empty(string? query) => State switch
    {
        WorldBrowseState.Loading => LoadingLine,
        WorldBrowseState.Failed => $"mu*index did not answer: {Problem}",
        WorldBrowseState.Unavailable => Problem,
        _ when Rows.Count == 0 => AskedFor.Length > 0 ? $"mu*index lists no world for “{AskedFor}”" : "mu*index lists no worlds right now",
        _ when View(query, WorldDirectorySort.Players).Count == 0 => AsksDirectory(query)
            ? $"none of the {Rows.Count} listed match — ⏎ asks mu*index for “{query!.Trim()}”"
            : "nothing listed matches — ⌫ widens the search",
        _ => null,
    };

    /// <summary>The count line: how many rows are listed, of how many the directory holds.</summary>
    public string CountLine(string? query)
    {
        if (State != WorldBrowseState.Ready)
        {
            return string.Empty;
        }

        var shown = View(query, WorldDirectorySort.Players).Count;
        var of = Total > Rows.Count ? $"{Rows.Count} of {Total}" : Rows.Count.ToString(CultureInfo.InvariantCulture);
        return shown == Rows.Count ? $"{of} worlds" : $"{shown} of {of}";
    }

    /// <summary>Whether any row carries a count the game reports about itself, so <see cref="DeclaredNote"/> is said.</summary>
    public bool AnyDeclared => Rows.Any(r => r.Count == WorldPlayerCount.Declared);

    public bool Equals(WorldBrowseModel? other) =>
        other is not null && State == other.State && Total == other.Total && AskedFor == other.AskedFor
        && Problem == other.Problem && Opening == other.Opening && ModelLists.Same(Rows, other.Rows);

    public override int GetHashCode() => HashCode.Combine(State, Total, AskedFor, Problem, Opening, ModelLists.Hash(Rows));
}

/// <summary>
/// One opening of Browse worlds in a host's own controls. The host filters and sorts the model's rows itself
/// (<see cref="WorldBrowseModel.View"/>); a pick and a search the rows held cannot answer come back through here to the
/// methods the cell surface's ⏎ ends in. A pick does not close the surface: the client asks where the world is, and
/// closes it once the connect form opens in its place.
/// </summary>
public sealed class WorldBrowseSession : ModalSession<WorldBrowseModel>
{
    private readonly Action<int> _pick;
    private readonly Action<string> _ask;

    /// <param name="model">What the surface lists now.</param>
    /// <param name="isDark">Whether the client's theme is a dark one.</param>
    /// <param name="pick">Picks row N of <see cref="WorldBrowseModel.Rows"/>.</param>
    /// <param name="ask">Asks the directory to search for the words.</param>
    /// <param name="closed">Told the surface closed.</param>
    public WorldBrowseSession(WorldBrowseModel model, bool isDark, Action<int> pick, Action<string> ask, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentNullException.ThrowIfNull(ask);
        _pick = pick;
        _ask = ask;
    }

    public override string Surface => "browse-worlds";

    /// <summary>What the search box opens with typed in (<c>/browse words</c>).</summary>
    public string InitialQuery { get; init; } = string.Empty;

    /// <summary>The host's: ⏎ on row <paramref name="index"/> of <see cref="WorldBrowseModel.Rows"/>.</summary>
    public void Pick(int index)
    {
        if (!IsEnded && index >= 0 && index < Model.Rows.Count)
        {
            _pick(index);
        }
    }

    /// <summary>The host's: ⏎ on a query the rows held do not answer (<see cref="WorldBrowseModel.AsksDirectory"/>).</summary>
    public void Ask(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!IsEnded && Model.AsksDirectory(query))
        {
            _ask(query.Trim());
        }
    }
}
