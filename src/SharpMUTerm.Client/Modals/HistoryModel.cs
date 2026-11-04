using SharpMUTerm.Core.Input;

namespace SharpMUTerm.Client.Modals;

/// <summary>
/// ⌃R's model: the armed command line's history as it stood when the surface opened, and whose list it is. Drawn by
/// the cell surface (<c>HistorySearchPrompt.Render</c>) and by the window's native one; both filter it with
/// <see cref="HistorySearch.Match"/>, so the same query lists the same rows in the same order.
/// </summary>
/// <param name="Entries">The entries, oldest first, as <see cref="InputHistory.Entries"/> holds them.</param>
/// <param name="BarLabel">Whose list this is — the character and the bar, as the cell surface's header says.</param>
public sealed record HistoryModel(IReadOnlyList<string> Entries, string BarLabel)
{
    /// <summary>What the list says with nothing ever entered.</summary>
    public const string NothingEntered = "nothing has been entered on this command line yet";

    /// <summary>What the list says when a filter leaves nothing.</summary>
    public const string NoMatches = "no matches — ⌫ widens the filter";

    /// <summary>The rows for <paramref name="query"/>, newest first: exactly what the cell surface lists for it.</summary>
    public IReadOnlyList<HistoryMatch> View(string query) => HistorySearch.Match(Entries, query ?? string.Empty);

    /// <summary>How many rows are listed out of how many the command line has ever held: <c>3 of 41</c>, <c>41 entries</c>.</summary>
    public string CountText(int shown)
    {
        var total = Entries.Count;
        return shown == total ? $"{total} entr{(total == 1 ? "y" : "ies")}" : $"{shown} of {total}";
    }

    public bool Equals(HistoryModel? other) =>
        other is not null && BarLabel == other.BarLabel && ModelLists.Same(Entries, other.Entries);

    public override int GetHashCode() => HashCode.Combine(BarLabel, ModelLists.Hash(Entries));
}

/// <summary>
/// One opening of ⌃R in a host's own controls. The history is captured when it opens, so the host filters on its own
/// thread with nothing to lock; <see cref="Insert"/> hands back the chosen entry's index into
/// <see cref="HistoryModel.Entries"/>, which the client puts on the command line through the cell surface's own path —
/// placed, never sent.
/// </summary>
public sealed class HistorySession : ModalSession<HistoryModel>
{
    private readonly Action<int> _insert;

    /// <param name="model">The history it lists.</param>
    /// <param name="isDark">Whether the client's theme is a dark one.</param>
    /// <param name="insert">Places entry N on the command line, after the surface has closed.</param>
    /// <param name="closed">Told the surface closed.</param>
    public HistorySession(HistoryModel model, bool isDark, Action<int> insert, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(insert);
        _insert = insert;
    }

    public override string Surface => "history";

    /// <summary>The host's: ⏎ on the entry at <paramref name="entryIndex"/> in <see cref="HistoryModel.Entries"/>.</summary>
    public void Insert(int entryIndex)
    {
        if (entryIndex >= 0 && entryIndex < Model.Entries.Count)
        {
            CloseThen(() => _insert(entryIndex));
        }
    }
}
