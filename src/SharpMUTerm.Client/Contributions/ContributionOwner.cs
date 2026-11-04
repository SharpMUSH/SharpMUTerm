namespace SharpMUTerm.Client.Contributions;

/// <summary>
/// Who put a contribution into the registry: the client itself (<see cref="BuiltIn"/>), or a package, named by
/// its id. The owner is what lets a disabled or removed package take every one of its contributions with it
/// (<see cref="ContributionRegistry.RemoveOwnedBy"/>) without touching anything the client registered.
/// </summary>
/// <param name="PackageId">The package's id, or null for the client's own contributions.</param>
public sealed record ContributionOwner(string? PackageId)
{
    /// <summary>The client's own contributions.</summary>
    public static ContributionOwner BuiltIn { get; } = new((string?)null);

    /// <summary>A contribution from the package with this id.</summary>
    public static ContributionOwner Package(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        return new ContributionOwner(packageId);
    }

    /// <summary>Whether this is the client's own.</summary>
    public bool IsBuiltIn => PackageId is null;

    /// <inheritdoc />
    public override string ToString() => PackageId is null ? "built-in" : $"package:{PackageId}";
}

/// <summary>
/// The six kinds of thing a host's views read from the registry. Each kind has its own list in
/// <see cref="ContributionRegistry"/>, and ids are unique within a kind.
/// </summary>
public enum ContributionKind
{
    /// <summary>An action the command surface (⌃P) lists and a pointer menu can offer. See <see cref="CommandContribution"/>.</summary>
    Command,

    /// <summary>A view of data the client holds (game data, the map). See <see cref="PanelContribution"/>.</summary>
    Panel,

    /// <summary>A group of fields on a settings screen. See <see cref="SettingsSectionContribution"/>.</summary>
    SettingsSection,

    /// <summary>A cell of the status row. See <see cref="StatusCellContribution"/>.</summary>
    StatusCell,

    /// <summary>A modal surface drawn over the workspace. See <see cref="OverlayContribution"/>.</summary>
    Overlay,

    /// <summary>A step that changes a line of output on its way to a pane. See <see cref="OutputDecoratorContribution"/>.</summary>
    OutputDecorator,
}

/// <summary>What every contribution has: an id unique within its kind, and an owner.</summary>
public interface IContribution
{
    /// <summary>The stable id. Unique within the contribution's kind; a second registration under it is refused.</summary>
    string Id { get; }

    /// <summary>Who registered it.</summary>
    ContributionOwner Owner { get; }
}
