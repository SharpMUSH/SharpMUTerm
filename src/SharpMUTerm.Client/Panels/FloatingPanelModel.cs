using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Panels;

/// <summary>
/// The commands that pop a panel out into a window of its own and dock it back, and which panels can. Offered only by
/// a host that can float a panel (the desktop window); in a terminal a floating panel is simply in its region.
/// </summary>
public static class PanelFloatCommands
{
    /// <summary>The prefix of <c>panel:popout:&lt;window id&gt;</c>.</summary>
    public const string PopOutPrefix = "panel:popout:";

    /// <summary>The prefix of <c>panel:dock:&lt;window id&gt;</c>.</summary>
    public const string DockPrefix = "panel:dock:";

    /// <summary>The panels drawn natively from a model of their own, and so drawable in a window apart from the cells.</summary>
    public static IReadOnlySet<string> Floatable { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        PanelIds.Map, PanelIds.Vitals, PanelIds.WhoHere, PanelIds.QuickBar,
    };

    /// <summary>Pops the panel drawn in <paramref name="windowId"/> out into a window of its own.</summary>
    public static string PopOut(string windowId) => PopOutPrefix + windowId;

    /// <summary>Docks the floating panel in <paramref name="windowId"/> back into its region.</summary>
    public static string Dock(string windowId) => DockPrefix + windowId;
}

/// <summary>
/// One panel popped out into a window of its own: which panel, whose, and where its window sits. What the window shows
/// inside is the panel's own model (<see cref="VitalsModel"/>, <see cref="MapViewModel"/>, …), the same one its cells
/// draw when it is docked; this is the frame around it.
/// </summary>
/// <param name="WindowId">The panel's window in the workspace, out of the pane tree while it floats.</param>
/// <param name="PanelId">Which panel (<see cref="PanelIds"/>).</param>
/// <param name="Title">What it is called (<c>Map</c>).</param>
/// <param name="Character">Whose it is; its actions run on this character's session.</param>
/// <param name="Bounds">Where its window sits, in screen pixels; <see cref="FloatingBounds.Unplaced"/> until placed.</param>
public sealed record FloatingPanelModel(string WindowId, string PanelId, string Title, string Character, FloatingBounds Bounds)
{
    /// <summary>The command that docks it back, as the window's close and its Dock button run it.</summary>
    public string DockCommand => PanelFloatCommands.Dock(WindowId);

    /// <summary>The window's title: the panel, then whose.</summary>
    public string WindowTitle => $"{Title} — {Character}";
}
