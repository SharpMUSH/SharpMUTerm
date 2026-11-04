namespace SharpMUTerm.Core.Layouts;

/// <summary>
/// The layouts every client has. Read-only: they are not in <c>config.json</c>, <see cref="LayoutLibrary"/>
/// refuses to change them, and changing one is saving a copy.
/// <para>
/// Each has a <em>narrow</em> arrangement (<see cref="ForWidth"/>) for a window below 100 columns, where a side
/// column is about twenty cells: the panel that needs width moves out of it. Same id, same name — it is the same
/// layout drawn in less room — and only a built-in has one; a layout of the player's own is drawn as saved.
/// </para>
/// </summary>
public static class BuiltInLayouts
{
    /// <summary>The id of <see cref="Roleplay"/>.</summary>
    public const string RoleplayId = "roleplay";

    /// <summary>The id of <see cref="Combat"/>.</summary>
    public const string CombatId = "combat";

    /// <summary>
    /// For posing: the output with the capture windows as tabs behind it, and a column beside it holding the composer
    /// over who is in the room. What every world gets when nothing else applies.
    /// </summary>
    public static LayoutDefinition Roleplay { get; } = new()
    {
        Id = RoleplayId,
        Name = "Roleplay",
        IsBuiltIn = true,
        Placements =
        [
            new PanelPlacement { PanelId = PanelIds.Output, Region = LayoutRegion.Main, Order = 0 },
            new PanelPlacement { PanelId = PanelIds.Chat, Region = LayoutRegion.Main, Order = 1, TabOf = PanelIds.Output },
            new PanelPlacement { PanelId = PanelIds.Composer, Region = LayoutRegion.Side, Order = 0, Size = 3 },
            new PanelPlacement { PanelId = PanelIds.WhoHere, Region = LayoutRegion.Side, Order = 1, Size = 2 },
        ],
        RegionShares = new Dictionary<LayoutRegion, double> { [LayoutRegion.Side] = 0.35 },
    };

    /// <summary>
    /// For fighting: the output with a quick bar under it, and a column beside it holding the vitals card over the
    /// map, the capture windows a tab behind the map.
    /// </summary>
    public static LayoutDefinition Combat { get; } = new()
    {
        Id = CombatId,
        Name = "Combat",
        IsBuiltIn = true,
        Placements =
        [
            new PanelPlacement { PanelId = PanelIds.Output, Region = LayoutRegion.Main, Order = 0 },
            new PanelPlacement { PanelId = PanelIds.Vitals, Region = LayoutRegion.Side, Order = 0 },
            new PanelPlacement { PanelId = PanelIds.Map, Region = LayoutRegion.Side, Order = 1, Size = 2 },
            new PanelPlacement { PanelId = PanelIds.Chat, Region = LayoutRegion.Side, Order = 2, Collapsed = true },
            new PanelPlacement { PanelId = PanelIds.QuickBar, Region = LayoutRegion.Bottom, Order = 0 },
        ],
    };

    /// <summary>
    /// <see cref="Roleplay"/> below 100 columns: the composer goes under the output, where it has the pane's width to
    /// write in, and who is here keeps the column.
    /// </summary>
    public static LayoutDefinition RoleplayNarrow { get; } = Roleplay with
    {
        Placements =
        [
            new PanelPlacement { PanelId = PanelIds.Output, Region = LayoutRegion.Main, Order = 0 },
            new PanelPlacement { PanelId = PanelIds.Chat, Region = LayoutRegion.Main, Order = 1, TabOf = PanelIds.Output },
            new PanelPlacement { PanelId = PanelIds.Composer, Region = LayoutRegion.Bottom, Order = 0 },
            new PanelPlacement { PanelId = PanelIds.WhoHere, Region = LayoutRegion.Side, Order = 0 },
        ],
        RegionShares = null,
    };

    /// <summary>
    /// <see cref="Combat"/> below 100 columns: the vitals card becomes a one-row strip across the top, so the column's
    /// rows go to the map.
    /// </summary>
    public static LayoutDefinition CombatNarrow { get; } = Combat with
    {
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

    /// <summary>
    /// The arrangement to fold <paramref name="layout"/> in: its narrow one when <paramref name="narrow"/> and it is a
    /// built-in, else the layout itself. A saved copy of a built-in is the player's and is never swapped.
    /// </summary>
    public static LayoutDefinition ForWidth(LayoutDefinition layout, bool narrow)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (!narrow || !layout.IsBuiltIn)
        {
            return layout;
        }

        return layout.Id switch
        {
            RoleplayId => RoleplayNarrow,
            CombatId => CombatNarrow,
            _ => layout,
        };
    }
}
