using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;

namespace SharpMUTerm.Client.Surfaces;

/// <summary>How a surface is drawn on one host.</summary>
[Flags]
public enum SurfaceDrawing
{
    /// <summary>Not drawn on this host; its actions are reached another way (<see cref="SurfaceDeclaration.KeyboardPath"/>).</summary>
    None = 0,

    /// <summary>Drawn as cells by the client.</summary>
    Cells = 1,

    /// <summary>Drawn by the host with its own controls.</summary>
    Native = 2,

    /// <summary>Drawn as cells, with the host's native view over them: the cells stay underneath.</summary>
    Both = Cells | Native,
}

/// <summary>What kind of input a surface hands back to the client.</summary>
public enum SurfaceActionKind
{
    /// <summary>A command id, run through the registry.</summary>
    Command,

    /// <summary>An index into the model the surface was drawn from (row N, item N).</summary>
    Pick,

    /// <summary>A click target the client put in the model.</summary>
    Target,

    /// <summary>A room id on the map.</summary>
    Walk,

    /// <summary>A change to a declared setting, committed through the client's save funnel.</summary>
    Edit,
}

/// <summary>
/// One thing a surface can ask the client to do, and the one client method every view of the surface ends in.
/// </summary>
/// <param name="Kind">What the view hands back.</param>
/// <param name="Name">A command id for <see cref="SurfaceActionKind.Command"/>; otherwise what is picked or targeted.</param>
/// <param name="Reaches">The client method the cells and the native view both end in.</param>
/// <param name="CellsVia">
/// Null when the surface's cells carry the action themselves. Otherwise the action is the native view's alone, and
/// this names where the cells offer the same thing: another surface's id, or a command id (a key, a ⌃P entry). A
/// native view may add a door; it may not add a room.
/// </param>
public sealed record SurfaceAction(SurfaceActionKind Kind, string Name, string Reaches, string? CellsVia = null);

/// <summary>The hosts a surface is declared for, by <c>ClientHost.Name</c>.</summary>
public static class SurfaceHosts
{
    /// <summary>The terminal: cells only, since nothing in a terminal can draw over a cell without taking it.</summary>
    public const string Terminal = "terminal";

    /// <summary>The desktop window: cells, and native views over them.</summary>
    public const string Window = "window";

    /// <summary>Every host a declaration states.</summary>
    public static IReadOnlyList<string> All { get; } = [Terminal, Window];
}

/// <summary>
/// One user-facing surface, declared once: its model, what it can ask for, and how each host draws it.
/// </summary>
/// <param name="Id">Stable id. For a panel, the layout's panel id (<see cref="PanelIds"/>).</param>
/// <param name="Model">The Client model every view of the surface is drawn from.</param>
/// <param name="Actions">What the surface can hand back to the client.</param>
/// <param name="Drawing">Per host (<see cref="SurfaceHosts"/>), how it is drawn.</param>
/// <param name="CellRenderer">
/// The Tui method that draws the cells, as <c>Namespace.Type.Method</c>; one of its overloads takes
/// <paramref name="Model"/>. Null only for a surface no host draws in cells.
/// </param>
/// <param name="NativeViews">The window's classes that draw it natively, as <c>Namespace.Type</c>.</param>
/// <param name="KeyboardPath">
/// For a surface no host draws in cells: the command id that reaches the same actions from the keyboard.
/// </param>
public sealed record SurfaceDeclaration(
    string Id,
    Type Model,
    IReadOnlyList<SurfaceAction> Actions,
    IReadOnlyDictionary<string, SurfaceDrawing> Drawing,
    string? CellRenderer,
    IReadOnlyList<string> NativeViews,
    string? KeyboardPath = null)
{
    /// <summary>How <paramref name="host"/> draws this surface.</summary>
    public SurfaceDrawing On(string host) => Drawing.TryGetValue(host, out var drawing) ? drawing : SurfaceDrawing.None;

    /// <summary>Whether any host draws it as cells.</summary>
    public bool HasCells => Drawing.Values.Any(d => d.HasFlag(SurfaceDrawing.Cells));

    /// <summary>Whether any host draws it natively.</summary>
    public bool HasNative => Drawing.Values.Any(d => d.HasFlag(SurfaceDrawing.Native));
}

/// <summary>
/// <b>Every user-facing surface that a host may draw with its own controls, declared once.</b> The terminal and the
/// window run one client; what keeps their surfaces saying the same things is that each has one Client model, the
/// cells are drawn from it, the window's native view is drawn from the same instance, and every click comes back to
/// one client method. This catalog is where that is written down, and the tests read it: a native view no surface
/// declares, a declared surface whose cell renderer does not take its model, a native action that ends somewhere a
/// cell click does not, and a native model that differs from the one the cells drew in the same frame all fail.
/// <para>
/// <b>Adding a native surface</b>: give it a model here in Client, draw its cells from that model, publish the same
/// instance to the host, declare it below, and add its probes to the window suite's <c>SurfaceParityTests</c>
/// (which fails until it has them).
/// </para>
/// </summary>
public static class SurfaceCatalog
{
    /// <summary>The header band.</summary>
    public const string Header = "header";

    /// <summary>The connection sidebar.</summary>
    public const string Sidebar = "sidebar";

    /// <summary>The declared settings sections (F7, F8).</summary>
    public const string Settings = "settings";

    /// <summary>The Layouts button's menu.</summary>
    public const string LayoutsMenu = "layouts-menu";

    /// <summary>⌃P, the command palette.</summary>
    public const string Palette = "palette";

    /// <summary>The ⌃B keymap while the prefix is armed: a cell panel, or the window's native header.</summary>
    public const string Prefix = "prefix";

    /// <summary>The folded sidebar: a strip of dots in cells, the window's narrow native strip.</summary>
    public const string RailStrip = "rail-strip";

    /// <summary>
    /// The header and the rail: cells in a terminal; in the window (<c>HostChrome.HeaderAndRail</c>) native views
    /// beside the cells, which then leave them out.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, SurfaceDrawing> NativeChrome = new Dictionary<string, SurfaceDrawing>
    {
        [SurfaceHosts.Terminal] = SurfaceDrawing.Cells,
        [SurfaceHosts.Window] = SurfaceDrawing.Native,
    };

    private static readonly IReadOnlyDictionary<string, SurfaceDrawing> CellsUnderNative = new Dictionary<string, SurfaceDrawing>
    {
        [SurfaceHosts.Terminal] = SurfaceDrawing.Cells,
        [SurfaceHosts.Window] = SurfaceDrawing.Both,
    };

    /// <summary>Every declared surface.</summary>
    public static IReadOnlyList<SurfaceDeclaration> All { get; } =
    [
        new(
            Header,
            typeof(HeaderModel),
            [
                new(SurfaceActionKind.Target, HeaderModel.PaletteTarget, "ToggleMenu"),
                new(SurfaceActionKind.Target, "character", "DispatchRailTarget", CellsVia: Sidebar),
            ],
            NativeChrome,
            "SharpMUTerm.Tui.SharpMUTermApp.HeaderMarkup",
            ["SharpMUTerm.Avalonia.NativeHeader"]),
        new(
            Sidebar,
            typeof(SidebarModel),
            [
                new(SurfaceActionKind.Target, "world, character or window", "DispatchRailTarget"),
                new(SurfaceActionKind.Command, SidebarModel.AddGameTarget, "DispatchCommand", CellsVia: SidebarModel.AddGameTarget),
            ],
            NativeChrome,
            "SharpMUTerm.Tui.RailRenderer.Render",
            ["SharpMUTerm.Avalonia.NativeSidebar"]),
        new(
            RailStrip,
            typeof(SidebarModel),
            [new(SurfaceActionKind.Target, SidebarModel.CollapseTarget, "ToggleRail", CellsVia: "layout:toggle-rail")],
            NativeChrome,
            "SharpMUTerm.Tui.RailRenderer.Render",
            ["SharpMUTerm.Avalonia.NativeSidebar"]),
        new(
            Prefix,
            typeof(HeaderPrefix),
            [],
            NativeChrome,
            "SharpMUTerm.Tui.PrefixPanel.Render",
            ["SharpMUTerm.Avalonia.NativeHeader"]),
        new(
            PanelIds.Vitals,
            typeof(VitalsModel),
            [],
            CellsUnderNative,
            "SharpMUTerm.Tui.Panels.VitalsStripRenderer.Render",
            ["SharpMUTerm.Avalonia.VitalsGauges"]),
        new(
            PanelIds.Map,
            typeof(MapViewModel),
            [new(SurfaceActionKind.Walk, "room", "WalkFromMap")],
            CellsUnderNative,
            "SharpMUTerm.Tui.MapRenderer.Render",
            ["SharpMUTerm.Avalonia.MapCanvas"]),
        new(
            PanelIds.WhoHere,
            typeof(WhoHereModel),
            [new(SurfaceActionKind.Pick, "row", "OfferLook")],
            CellsUnderNative,
            "SharpMUTerm.Tui.Panels.WhoHereRenderer.Render",
            ["SharpMUTerm.Avalonia.WhoHereList"]),
        new(
            PanelIds.QuickBar,
            typeof(QuickBarModel),
            [
                new(SurfaceActionKind.Pick, "item", "RunQuickMacro"),
                new(SurfaceActionKind.Command, QuickBarModel.EditCommand, "DispatchCommand"),
            ],
            CellsUnderNative,
            "SharpMUTerm.Tui.Panels.QuickBarRenderer.Render",
            ["SharpMUTerm.Avalonia.QuickBarButtons"]),
        new(
            Settings,
            typeof(SettingsSectionContribution),
            [new(SurfaceActionKind.Edit, "field", "ScreenEdits.Apply")],
            new Dictionary<string, SurfaceDrawing>
            {
                [SurfaceHosts.Terminal] = SurfaceDrawing.Cells,

                // A declared section opens the native settings window instead of its cell screen.
                [SurfaceHosts.Window] = SurfaceDrawing.Native,
            },
            "SharpMUTerm.Tui.OptionsScreenRenderer.FromSection",
            ["SharpMUTerm.Avalonia.SettingsWindow", "SharpMUTerm.Avalonia.ThemeCardView"]),
        new(
            LayoutsMenu,
            typeof(MenuModel),
            [
                new(SurfaceActionKind.Command, LayoutCommands.Apply(BuiltInLayouts.CombatId), "RunLayoutCommand"),
                new(SurfaceActionKind.Command, LayoutCommands.SaveChanges, "RunLayoutCommand"),
                new(SurfaceActionKind.Command, LayoutCommands.SaveAsNew, "RunLayoutCommand"),
                new(SurfaceActionKind.Command, LayoutCommands.UseForWorld, "RunLayoutCommand"),
                new(SurfaceActionKind.Command, LayoutCommands.UseForCharacter, "RunLayoutCommand"),
                new(SurfaceActionKind.Command, LayoutCommands.Manage, "DispatchCommand"),
            ],
            new Dictionary<string, SurfaceDrawing>
            {
                // A terminal has no header buttons; ⌥M's list is the same commands from the keyboard.
                [SurfaceHosts.Terminal] = SurfaceDrawing.None,
                [SurfaceHosts.Window] = SurfaceDrawing.Native,
            },
            null,
            ["SharpMUTerm.Avalonia.HeaderButton", "SharpMUTerm.Avalonia.NativeMenu"],
            KeyboardPath: LayoutCommands.Manage),
        new(
            Palette,
            typeof(PaletteView),
            [new(SurfaceActionKind.Pick, "entry", "RunFromPalette")],
            new Dictionary<string, SurfaceDrawing>
            {
                [SurfaceHosts.Terminal] = SurfaceDrawing.Cells,

                // A window with a native palette opens it instead of the cell popup (ClientHost.NativePalette).
                [SurfaceHosts.Window] = SurfaceDrawing.Native,
            },
            "SharpMUTerm.Tui.CommandSurfaceRenderer.Render",
            ["SharpMUTerm.Avalonia.PaletteOverlay"]),
    ];

    /// <summary>The surface with <paramref name="id"/>, or null.</summary>
    public static SurfaceDeclaration? Find(string id) => All.FirstOrDefault(s => s.Id == id);
}
