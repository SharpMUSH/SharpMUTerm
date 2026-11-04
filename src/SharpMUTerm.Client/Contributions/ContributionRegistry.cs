namespace SharpMUTerm.Client.Contributions;

/// <summary>
/// Everything the client's surfaces list, held in one place so that a host's views read it rather than each
/// keeping its own copy, and so that a package (or a host) can add to it. Six kinds, one list each:
/// <list type="bullet">
/// <item><description><see cref="Commands"/>: actions the command surface (⌃P) lists and runs, and a pointer menu offers by id.</description></item>
/// <item><description><see cref="Panels"/>: views of data the client holds for a connection (game data, the map).</description></item>
/// <item><description><see cref="SettingsSections"/>: groups of field descriptors on a settings screen.</description></item>
/// <item><description><see cref="StatusCells"/>: segments of the status row, each with a reserved width and a drop order.</description></item>
/// <item><description><see cref="Overlays"/>: modal surfaces over the workspace.</description></item>
/// <item><description><see cref="OutputDecorators"/>: steps that change a line of output on its way to a pane.</description></item>
/// </list>
/// Every contribution has a stable id, unique within its kind (a duplicate is refused), and an owner, the
/// client or a package. Order is registration order unless a kind says otherwise, and always deterministic.
/// <para>
/// UI-agnostic: nothing here draws. The terminal reads it through SharpConsoleUI views, and the desktop
/// window will read the same registry through its own.
/// </para>
/// </summary>
public sealed class ContributionRegistry
{
    /// <summary>Actions the command surface lists and a pointer menu offers.</summary>
    public CommandList Commands { get; } = new();

    /// <summary>Views of data the client holds.</summary>
    public ContributionList<PanelContribution> Panels { get; } = new(ContributionKind.Panel);

    /// <summary>Groups of settings fields.</summary>
    public SettingsSectionList SettingsSections { get; } = new();

    /// <summary>Segments of the status row.</summary>
    public StatusCellList StatusCells { get; } = new();

    /// <summary>Modal surfaces over the workspace.</summary>
    public ContributionList<OverlayContribution> Overlays { get; } = new(ContributionKind.Overlay);

    /// <summary>Steps that change a line of output on its way to a pane.</summary>
    public OutputDecoratorList OutputDecorators { get; } = new();

    /// <summary>Removes every contribution of every kind this owner registered. Returns how many went.</summary>
    public int RemoveOwnedBy(ContributionOwner owner) =>
        Commands.RemoveOwnedBy(owner)
        + Panels.RemoveOwnedBy(owner)
        + SettingsSections.RemoveOwnedBy(owner)
        + StatusCells.RemoveOwnedBy(owner)
        + Overlays.RemoveOwnedBy(owner)
        + OutputDecorators.RemoveOwnedBy(owner);
}
