using System.Text.Json.Serialization;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Layouts;

/// <summary>
/// The area of the window a panel is placed in. Host-neutral: the terminal folds these into its pane split tree
/// (<see cref="LayoutFold"/>), the desktop window into dock panes.
/// </summary>
public enum LayoutRegion
{
    /// <summary>The centre, where the output sits. Several main panels share it side by side.</summary>
    Main,

    /// <summary>A column on the right of the main area. Several side panels stack top to bottom.</summary>
    Side,

    /// <summary>A band under the main area (not under the side column). Several sit side by side.</summary>
    Bottom,

    /// <summary>A thin band across the top of the whole window, for a meter strip. Several sit side by side.</summary>
    TopStrip,
}

/// <summary>One panel's place in a <see cref="LayoutDefinition"/>.</summary>
public sealed record PanelPlacement
{
    /// <summary>The panel, one of <see cref="PanelIds"/> (an unknown id is kept and skipped).</summary>
    public string PanelId { get; init; } = string.Empty;

    /// <summary>Which area it sits in.</summary>
    public LayoutRegion Region { get; init; } = LayoutRegion.Main;

    /// <summary>Its position among the panels of its region, lowest first. Ties keep list order.</summary>
    public int Order { get; init; }

    /// <summary>
    /// Its share of its region against the other panes there, as a weight: two panes of 1 and 3 take a quarter
    /// and three quarters. Zero or less is read as 1. A tab's size is not read (its pane's is).
    /// </summary>
    public double Size { get; init; } = 1;

    /// <summary>Whether it starts collapsed: placed, but shown as a title the player can open.</summary>
    public bool Collapsed { get; init; }

    /// <summary>
    /// The panel whose pane this one joins as a tab, or null for a pane of its own. When that panel is not drawn
    /// (unknown, collapsed, or without data yet), this one takes a pane of its own in its own region.
    /// </summary>
    public string? TabOf { get; init; }

    /// <summary>
    /// Non-null when the panel is popped out into a window of its own, with where that window sits
    /// (<see cref="FloatingBounds.Unplaced"/> when no place was remembered). <b>Host-neutral, and read only by a host
    /// that can float a panel.</b> <see cref="LayoutFold"/> ignores it and places the panel in its
    /// <see cref="Region"/> as always, so a host that cannot float (the terminal) draws a floating panel in its region;
    /// a host that can withholds the panel's windows from that pane and draws them in a window of their own instead.
    /// The region, order and size are therefore kept for a floating panel too: they are where it docks back to.
    /// </summary>
    public FloatingBounds? Floating { get; init; }
}

/// <summary>
/// A saved arrangement of panels: which exist and where they sit. Whether a placed panel is actually drawn is a
/// separate question, answered per session by <see cref="IPanelAvailability"/>.
/// <para>
/// Immutable: <see cref="LayoutLibrary"/> changes a layout by replacing it, so a built-in handed out by
/// <see cref="BuiltInLayouts"/> cannot be edited in place.
/// </para>
/// </summary>
public sealed record LayoutDefinition
{
    /// <summary>The stable id characters and worlds refer to. Never shown; survives a rename.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The name the player sees and picks by.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Whether this is one of <see cref="BuiltInLayouts"/>. Never read from or written to a file, so a
    /// hand-edited configuration cannot make a layout of its own read-only.
    /// </summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; init; }

    /// <summary>The panels, in no particular order (each carries its <see cref="PanelPlacement.Order"/>).</summary>
    public IReadOnlyList<PanelPlacement> Placements { get; init; } = [];

    /// <summary>
    /// How much of the window each region takes, as a fraction of the axis it divides: <see cref="LayoutRegion.Side"/>
    /// of the width, <see cref="LayoutRegion.Bottom"/> and <see cref="LayoutRegion.TopStrip"/> of the height. A
    /// region left out takes <see cref="LayoutFold.DefaultShare"/>'s value. <see cref="LayoutRegion.Main"/> takes
    /// what is left and is not read from here. Null (the default) is "all defaults".
    /// </summary>
    public IReadOnlyDictionary<LayoutRegion, double>? RegionShares { get; init; }

    /// <summary>The placements naming a panel this client can draw (<see cref="PanelIds.Known"/>).</summary>
    [JsonIgnore]
    public IEnumerable<PanelPlacement> KnownPlacements => Placements.Where(p => PanelIds.IsKnown(p.PanelId));

    /// <summary>The known placement for <paramref name="panelId"/>, or null when the layout does not place it.</summary>
    public PanelPlacement? PlacementOf(string panelId) =>
        KnownPlacements.FirstOrDefault(p => string.Equals(p.PanelId, panelId, StringComparison.Ordinal));
}
