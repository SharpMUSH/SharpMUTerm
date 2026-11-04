namespace SharpMUTerm.Core.Layouts;

/// <summary>
/// The layouts every client has. Read-only: they are not in <c>config.json</c>, <see cref="LayoutLibrary"/>
/// refuses to change them, and changing one is saving a copy.
/// </summary>
public static class BuiltInLayouts
{
    /// <summary>The id of <see cref="Roleplay"/>.</summary>
    public const string RoleplayId = "roleplay";

    /// <summary>The id of <see cref="Combat"/>.</summary>
    public const string CombatId = "combat";

    /// <summary>
    /// For posing: the output with a composer docked under it, who is in the room beside it, and the capture
    /// windows as a tab behind who-here. What every world gets when nothing else applies.
    /// </summary>
    public static LayoutDefinition Roleplay { get; } = new()
    {
        Id = RoleplayId,
        Name = "Roleplay",
        IsBuiltIn = true,
        Placements =
        [
            new PanelPlacement { PanelId = PanelIds.Output, Region = LayoutRegion.Main, Order = 0 },
            new PanelPlacement { PanelId = PanelIds.Composer, Region = LayoutRegion.Bottom, Order = 0 },
            new PanelPlacement { PanelId = PanelIds.WhoHere, Region = LayoutRegion.Side, Order = 0 },
            new PanelPlacement { PanelId = PanelIds.Chat, Region = LayoutRegion.Side, Order = 1, TabOf = PanelIds.WhoHere },
        ],
    };

    /// <summary>
    /// For fighting: a meter strip across the top, the output, the map beside it, the capture windows collapsed
    /// under the map, and a quick bar under the output.
    /// </summary>
    public static LayoutDefinition Combat { get; } = new()
    {
        Id = CombatId,
        Name = "Combat",
        IsBuiltIn = true,
        Placements =
        [
            new PanelPlacement { PanelId = PanelIds.Vitals, Region = LayoutRegion.TopStrip, Order = 0 },
            new PanelPlacement { PanelId = PanelIds.Output, Region = LayoutRegion.Main, Order = 0 },
            new PanelPlacement { PanelId = PanelIds.Map, Region = LayoutRegion.Side, Order = 0, Size = 2 },
            new PanelPlacement { PanelId = PanelIds.Chat, Region = LayoutRegion.Side, Order = 1, Collapsed = true },
            new PanelPlacement { PanelId = PanelIds.QuickBar, Region = LayoutRegion.Bottom, Order = 0 },
        ],
    };

    /// <summary>Both, in the order a picker lists them.</summary>
    public static IReadOnlyList<LayoutDefinition> All { get; } = [Roleplay, Combat];

    /// <summary>The built-in with <paramref name="id"/>, or null.</summary>
    public static LayoutDefinition? Find(string? id) =>
        All.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.Ordinal));
}
