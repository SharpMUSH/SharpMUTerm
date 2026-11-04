namespace SharpMUTerm.Client.Modals;

/// <summary>What the ⌥M list can do to the layout it is pointing at.</summary>
public enum LayoutVerb
{
    /// <summary>⏎: arrange the panes by it now. The list closes.</summary>
    Apply,

    /// <summary><c>s</c>: save the panes as they are into it (a built-in saves a copy).</summary>
    SaveChanges,

    /// <summary><c>n</c>: save the panes as they are as a new layout.</summary>
    SaveAsNew,

    /// <summary><c>w</c>: make it the world's default.</summary>
    UseForWorld,

    /// <summary><c>c</c>: make it this character's own.</summary>
    UseForCharacter,

    /// <summary><c>r</c> then ⏎: give a layout of the player's own a new name.</summary>
    Rename,

    /// <summary><c>d</c> then <c>y</c>: delete a layout of the player's own.</summary>
    Delete,
}

/// <summary>One layout as the ⌥M list shows it.</summary>
/// <param name="Id">The layout's id, what a verb names.</param>
/// <param name="Name">Its name, bounded.</param>
/// <param name="Description">Its own words for a built-in, its panels for a saved one.</param>
/// <param name="BuiltIn">Whether it ships with the client (never renamed, deleted or changed in place).</param>
/// <param name="InUse">Whether the character's panes are arranged by it now.</param>
/// <param name="WorldDefault">Whether it is the character's world's default.</param>
public sealed record LayoutRow(string Id, string Name, string Description, bool BuiltIn, bool InUse, bool WorldDefault);

/// <summary>
/// The ⌥M list's model: every layout for one character, which is pointed at, and what the last action said. Drawn by
/// the cell list (<c>SharpMUTermApp.LayoutsListLines</c>) and by the window's native one; every action in either ends
/// in the client's one verb method, whose result (a <c>LayoutActionResult</c>) becomes <see cref="Status"/>.
/// </summary>
/// <param name="Subject">Whose layouts these are: the character's session key.</param>
/// <param name="Rows">The layouts, built-ins first, in the library's order.</param>
/// <param name="Selected">Which row is pointed at.</param>
/// <param name="Status">What the last action said, or null.</param>
/// <param name="Renaming">The name being typed in the cell list, or null when not renaming.</param>
/// <param name="ConfirmingDelete">Whether the cell list is asking "delete …?".</param>
public sealed record LayoutsModel(
    string Subject,
    IReadOnlyList<LayoutRow> Rows,
    int Selected,
    string? Status,
    string? Renaming,
    bool ConfirmingDelete)
{
    /// <summary>The row pointed at, or null with none.</summary>
    public LayoutRow? Current => Selected >= 0 && Selected < Rows.Count ? Rows[Selected] : null;

    public bool Equals(LayoutsModel? other) =>
        other is not null
        && Subject == other.Subject && Selected == other.Selected && Status == other.Status
        && Renaming == other.Renaming && ConfirmingDelete == other.ConfirmingDelete
        && ModelLists.Same(Rows, other.Rows);

    public override int GetHashCode() => HashCode.Combine(Subject, Selected, Status, Renaming, ConfirmingDelete, ModelLists.Hash(Rows));
}

/// <summary>
/// One opening of ⌥M in a host's own controls. Every button and key is a <see cref="LayoutVerb"/> on a layout id; the
/// client runs it through the method the cell list's key runs and publishes the list again with what it said. Apply
/// closes the list, as ⏎ does in cells.
/// </summary>
public sealed class LayoutsSession : ModalSession<LayoutsModel>
{
    private readonly Action<LayoutVerb, string, string?> _run;

    /// <param name="model">The list it opens on.</param>
    /// <param name="isDark">Whether the client's theme is a dark one.</param>
    /// <param name="run">Runs a verb on a layout id, with the new name for a rename; called from any thread.</param>
    /// <param name="closed">Told the list closed.</param>
    public LayoutsSession(LayoutsModel model, bool isDark, Action<LayoutVerb, string, string?> run, Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(run);
        _run = run;
    }

    public override string Surface => "layouts";

    /// <summary>The host's: <paramref name="verb"/> on the layout <paramref name="layoutId"/>.</summary>
    public void Run(LayoutVerb verb, string layoutId, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(layoutId);
        if (IsEnded)
        {
            return;
        }

        if (verb == LayoutVerb.Apply)
        {
            CloseThen(() => _run(verb, layoutId, name));
        }
        else
        {
            _run(verb, layoutId, name);
        }
    }
}
