namespace SharpMUTerm.Client.Commands;

/// <summary>
/// One entry of a menu the client builds for a host to draw: what it says, the key that does the same, and the
/// command id it dispatches. A host never names a command of its own — it hands the id back to the client, which
/// runs it through the registry, as a key or a ⌃P entry would.
/// </summary>
/// <param name="Label">What the entry says; may carry the game's or the player's text, already bounded.</param>
/// <param name="Key">The key that does the same, for the entry's key column, or null.</param>
/// <param name="Command">The command id the entry dispatches, or null for an entry that does nothing.</param>
/// <param name="Checked">Whether the entry is ticked: the one in use.</param>
/// <param name="Enabled">Whether it can be chosen.</param>
public sealed record MenuEntry(string Label, string? Key, string? Command, bool Checked = false, bool Enabled = true)
{
    /// <summary>A rule between groups of entries.</summary>
    public static MenuEntry Separator { get; } = new("-", null, null, Enabled: false);

    /// <summary>Whether this is the rule between groups.</summary>
    public bool IsSeparator => Equals(Separator);
}

/// <summary>
/// A menu as data, built by the client and drawn by a host (the window's Layouts button draws it as a native
/// menu). Every entry carries the command id it runs, so the menu cannot offer what the registry does not have.
/// </summary>
public sealed record MenuModel(IReadOnlyList<MenuEntry> Entries)
{
    /// <summary>No entries.</summary>
    public static MenuModel Empty { get; } = new([]);

    /// <summary>Every command id the menu offers, so a host's answer can be checked against what was offered.</summary>
    public IEnumerable<string> Commands() =>
        Entries.Where(e => e.Enabled && e.Command is not null).Select(e => e.Command!);

    /// <summary>Value equality over the entries, which a record's list member does not give.</summary>
    public bool Equals(MenuModel? other) => other is not null && Entries.SequenceEqual(other.Entries);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var entry in Entries)
        {
            hash.Add(entry);
        }

        return hash.ToHashCode();
    }
}
