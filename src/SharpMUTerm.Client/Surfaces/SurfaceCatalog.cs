using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Modals;
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

    /// <summary>
    /// A composer's gesture — its text sent, the escaping flipped, a word added, the keyboard released or taken —
    /// handed back through the composer's session (<c>ComposerSession</c>).
    /// </summary>
    Compose,

    /// <summary>
    /// A modal's gesture — a query typed, a toggle flipped, a row chosen, an answer given — handed back through the
    /// modal's session (<c>ModalSession</c>).
    /// </summary>
    Modal,
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
/// <param name="OnlyOn">
/// Null when every host that draws the surface offers the action. Otherwise the one host (<see cref="SurfaceHosts"/>)
/// that can do it at all — popping a panel out into a window of its own is the window's, since a terminal has nowhere
/// to put a second window — and <paramref name="CellsVia"/> must then name the command that reaches it from the cells
/// on that host.
/// </param>
public sealed record SurfaceAction(SurfaceActionKind Kind, string Name, string Reaches, string? CellsVia = null, string? OnlyOn = null);

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

    /// <summary>A notice: the status row's message in cells, and in the window a toast over them as well.</summary>
    public const string Notice = "notice";

    /// <summary>F1, the full-screen composer: a cell modal in a terminal, the window's native editor in its place.</summary>
    public const string Compose = "compose";

    /// <summary>The ⌃Q confirmation: a cell modal in a terminal, a native prompt in the window.</summary>
    public const string Quit = "quit";

    /// <summary>⌃F, the search over the pane buffers.</summary>
    public const string Search = "search";

    /// <summary>⌃R, the armed command line's history.</summary>
    public const string History = "history";

    /// <summary>⌥M, the layouts list.</summary>
    public const string Layouts = "layouts";

    /// <summary>The first-run welcome and its connect form.</summary>
    public const string Welcome = "welcome";

    /// <summary>Help: keys, slash commands and how-to pages (⌃P ▸ Help, <c>/help</c>).</summary>
    public const string Help = "help";

    /// <summary>The spelling dictionary offer: the consent step before anything is downloaded.</summary>
    public const string Dictionary = "dictionary";

    /// <summary>A character's script console: its scripts' output and errors, a Lua prompt, and its <c>store.*</c>.</summary>
    public const string ScriptConsole = "script-console";

    /// <summary>
    /// The <c>+3 ▾</c> chip a tab strip carries when its tabs do not fit even at their floors, and the list of the tabs
    /// past its edges it opens: a cell list in a terminal, a native menu in the window (the chip itself is cells in both).
    /// </summary>
    public const string TabOverflow = "tab-overflow";

    /// <summary>
    /// A panel popped out into a window of its own (the desktop window only; a terminal keeps every panel in its
    /// region). The frame around the panel's own native view: its title, whose it is, and the way back.
    /// </summary>
    public const string FloatingPanel = "floating-panel";

    /// <summary>
    /// Pop the panel out into a window of its own: the window's alone, offered by a button on the panel's native view,
    /// the pane's right-click menu and ⌃P (<c>panel:popout:&lt;window&gt;</c>), all ending in one client method.
    /// </summary>
    private static readonly SurfaceAction PopOut = new(
        SurfaceActionKind.Command, PanelFloatCommands.PopOutPrefix, "PopOutPanel", CellsVia: PanelFloatCommands.PopOutPrefix, OnlyOn: SurfaceHosts.Window);

    /// <summary>The window's views of a panel: its own view, and the pop-out button over it.</summary>
    private static string[] PanelViews(string view) => ["SharpMUTerm.Avalonia." + view, "SharpMUTerm.Avalonia.PopOutButton"];

    /// <summary>The modals the window draws with its own controls in place of the cell modal (<c>ClientHost.NativeModals</c>).</summary>
    public static IReadOnlyList<string> Modals { get; } = [Quit, Search, History, Layouts, Welcome, Help, Dictionary, ScriptConsole];

    /// <summary>A cell modal in a terminal; the window's own modal in its place.</summary>
    private static readonly IReadOnlyDictionary<string, SurfaceDrawing> NativeModal = new Dictionary<string, SurfaceDrawing>
    {
        [SurfaceHosts.Terminal] = SurfaceDrawing.Cells,
        [SurfaceHosts.Window] = SurfaceDrawing.Native,
    };

    /// <summary>The window's overlay every native modal is drawn in, and the view of one.</summary>
    private static string[] ModalViews(string view) => ["SharpMUTerm.Avalonia.ModalOverlay", "SharpMUTerm.Avalonia." + view];

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

                // A right click on a row: entries the client builds for that row, each a registry command with its
                // key (Disconnect is one), so every entry is also a ⌃P entry and a chord in the terminal.
                new(SurfaceActionKind.Pick, "row menu entry", "RunSidebarMenuEntry", CellsVia: "world:disconnect"),
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
            [PopOut],
            CellsUnderNative,
            "SharpMUTerm.Tui.Panels.VitalsStripRenderer.Render",
            PanelViews("VitalsGauges")),
        new(
            PanelIds.Map,
            typeof(MapViewModel),
            [new(SurfaceActionKind.Walk, "room", "WalkFromMap"), PopOut],
            CellsUnderNative,
            "SharpMUTerm.Tui.MapRenderer.Render",
            PanelViews("MapCanvas")),
        new(
            PanelIds.WhoHere,
            typeof(WhoHereModel),
            [new(SurfaceActionKind.Pick, "row", "OfferLook"), PopOut],
            CellsUnderNative,
            "SharpMUTerm.Tui.Panels.WhoHereRenderer.Render",
            PanelViews("WhoHereList")),
        new(
            PanelIds.QuickBar,
            typeof(QuickBarModel),
            [
                new(SurfaceActionKind.Pick, "item", "RunQuickMacro"),
                new(SurfaceActionKind.Command, QuickBarModel.EditCommand, "DispatchCommand"),
                PopOut,
            ],
            CellsUnderNative,
            "SharpMUTerm.Tui.Panels.QuickBarRenderer.Render",
            PanelViews("QuickBarButtons")),
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
            ["SharpMUTerm.Avalonia.SettingsWindow", "SharpMUTerm.Avalonia.ThemeCardView", "SharpMUTerm.Avalonia.ListEditorView", "SharpMUTerm.Avalonia.PackagePageView"]),
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
        new(
            Notice,
            typeof(NoticeModel),
            [
                // The toast's click runs the action the client put in the notice. The status row is not a button; the
                // action is a registry command, so the cells reach it as a ⌃P entry, and the message log is where a
                // notice that has gone is found again.
                new(SurfaceActionKind.Target, "action", "RunNoticeAction", CellsVia: "term:messages"),
            ],
            CellsUnderNative,
            "SharpMUTerm.Tui.SharpMUTermApp.NoticeMarkup",
            ["SharpMUTerm.Avalonia.NoticeToasts"]),
        new(
            Compose,
            typeof(ComposerModel),
            [
                // ⌃S: the buffer through ComposeMessage.Build to the window's character (a MOO edit saves as lines).
                new(SurfaceActionKind.Compose, "send", "SendComposed"),

                // Esc: the window closes and the buffer is kept as the character's draft.
                new(SurfaceActionKind.Compose, "close", "CloseComposer"),
                new(SurfaceActionKind.Compose, "add word", "AddComposerWord"),

                // ⌥W with no dictionary: F1 closes keeping the draft, and the dictionary offer opens.
                new(SurfaceActionKind.Compose, "get dictionary", "OfferDictionary"),
            ],
            new Dictionary<string, SurfaceDrawing>
            {
                [SurfaceHosts.Terminal] = SurfaceDrawing.Cells,

                // A window with a native composer opens F1 there instead of the cell modal (ClientHost.NativeComposer).
                [SurfaceHosts.Window] = SurfaceDrawing.Native,
            },
            "SharpMUTerm.Tui.ComposeOverlay.Render",
            ["SharpMUTerm.Avalonia.ComposerOverlay", "SharpMUTerm.Avalonia.ComposerView", "SharpMUTerm.Avalonia.ComposerEditor"]),
        new(
            PanelIds.Composer,
            typeof(ComposerModel),
            [
                new(SurfaceActionKind.Compose, "send", "SendDocked"),
                new(SurfaceActionKind.Compose, "release", "ReleaseComposer"),

                // A click on the cell editor arms it through the framework's focus (ArmClickedComposer); the native
                // editor asks for the same thing by name.
                new(SurfaceActionKind.Compose, "arm", "ArmComposer"),
                new(SurfaceActionKind.Compose, "add word", "AddComposerWord"),
            ],
            CellsUnderNative,
            "SharpMUTerm.Tui.Panels.DockedComposer.Render",
            ["SharpMUTerm.Avalonia.ComposerView", "SharpMUTerm.Avalonia.ComposerEditor"]),
        new(
            Quit,
            typeof(QuitModel),
            [
                // y: the client's quit. n, Esc and ⌃Q again: the prompt closes and nothing else happens.
                new(SurfaceActionKind.Modal, "quit", "Quit"),
                new(SurfaceActionKind.Modal, "stay", "QuitPromptClosed"),
            ],
            NativeModal,
            "SharpMUTerm.Tui.QuitPrompt.Render",
            ModalViews("QuitView")),
        new(
            Search,
            typeof(SearchModel),
            [
                // Typing, ⌥E and ⌥A re-run the search over the pane buffers; ⏎ goes to the row through the one path to a hit.
                new(SurfaceActionKind.Modal, "query", "SearchModelFor"),
                new(SurfaceActionKind.Modal, "go", "GoToSearchHit"),
            ],
            NativeModal,
            "SharpMUTerm.Tui.SearchPrompt.Render",
            ModalViews("SearchView")),
        new(
            History,
            typeof(HistoryModel),
            [new(SurfaceActionKind.Modal, "insert", "InsertHistoryEntry")],
            NativeModal,
            "SharpMUTerm.Tui.HistorySearchPrompt.Render",
            ModalViews("HistoryView")),
        new(
            Layouts,
            typeof(LayoutsModel),
            [
                new(SurfaceActionKind.Modal, nameof(LayoutVerb.Apply), "RunLayoutsVerb"),
                new(SurfaceActionKind.Modal, nameof(LayoutVerb.SaveChanges), "RunLayoutsVerb"),
                new(SurfaceActionKind.Modal, nameof(LayoutVerb.SaveAsNew), "RunLayoutsVerb"),
                new(SurfaceActionKind.Modal, nameof(LayoutVerb.UseForWorld), "RunLayoutsVerb"),
                new(SurfaceActionKind.Modal, nameof(LayoutVerb.UseForCharacter), "RunLayoutsVerb"),
                new(SurfaceActionKind.Modal, nameof(LayoutVerb.Rename), "RunLayoutsVerb"),
                new(SurfaceActionKind.Modal, nameof(LayoutVerb.Delete), "RunLayoutsVerb"),
            ],
            NativeModal,
            "SharpMUTerm.Tui.SharpMUTermApp.LayoutsListLines",
            ModalViews("LayoutsView")),
        new(
            Welcome,
            typeof(WelcomeModel),
            [
                // Browse and Open settings close it and open what they name; Connect opens the form.
                new(SurfaceActionKind.Modal, "choose", "RunWelcomeAction"),
                new(SurfaceActionKind.Modal, "layout", "SetWelcomeLayout"),

                // The form goes through /connect's own path (ConnectCommand), so it can add nothing the command line cannot.
                new(SurfaceActionKind.Modal, "connect", "ConnectFromWelcome"),
            ],
            NativeModal,
            "SharpMUTerm.Tui.WelcomeSurface.Render",
            ModalViews("WelcomeView")),
        new(
            Help,
            typeof(HelpModel),
            [
                // A slash command is put on the command line, never sent; a key runs its registry command. A page is the
                // view's own to show and hands nothing back.
                new(SurfaceActionKind.Modal, "place", "RunHelpEntry"),
                new(SurfaceActionKind.Modal, "run", "RunHelpEntry"),
            ],
            NativeModal,
            "SharpMUTerm.Tui.HelpSurface.Render",
            ModalViews("HelpView")),
        new(
            Dictionary,
            typeof(DictionaryOfferModel),
            [new(SurfaceActionKind.Modal, "accept", "InstallDictionary")],
            NativeModal,
            "SharpMUTerm.Tui.DictionaryOffer.Render",
            ModalViews("DictionaryOfferView")),
        new(
            ScriptConsole,
            typeof(ScriptConsoleModel),
            [
                // A line at the prompt runs in the character's sandbox; a store key is deleted; the scripts are reloaded.
                new(SurfaceActionKind.Modal, "run", "RunConsoleLine"),
                new(SurfaceActionKind.Modal, "delete key", "DeleteStoreKey"),
                new(SurfaceActionKind.Modal, "reload", "ReloadConsoleScripts"),
            ],
            NativeModal,
            "SharpMUTerm.Tui.ScriptConsoleSurface.Render",
            ModalViews("ScriptConsoleView")),
        new(
            TabOverflow,
            typeof(MenuModel),
            [
                // Each entry is a hidden tab's window, run through the registry (win:<id>, the ⌃P "Go to …" entry), and
                // refused if the chip did not offer it.
                new(SurfaceActionKind.Command, SharpMUTerm.Core.Commands.CommandIds.WindowPrefix, "RunTabOverflowEntry"),
            ],
            CellsUnderNative,
            "SharpMUTerm.Tui.SharpMUTermApp.TabOverflowLines",
            ["SharpMUTerm.Avalonia.NativeMenu"]),
        new(
            FloatingPanel,
            typeof(FloatingPanelModel),
            [
                // Its Dock button and closing the window both dock it back; the cells' way is the ⌃P entry, offered
                // only where the host can float a panel at all.
                new(SurfaceActionKind.Command, PanelFloatCommands.DockPrefix, "DockPanel", CellsVia: PanelFloatCommands.DockPrefix, OnlyOn: SurfaceHosts.Window),
            ],
            new Dictionary<string, SurfaceDrawing>
            {
                // A terminal has nowhere to put a second window: a floating panel is drawn in its region there, as cells.
                [SurfaceHosts.Terminal] = SurfaceDrawing.None,
                [SurfaceHosts.Window] = SurfaceDrawing.Native,
            },
            null,
            ["SharpMUTerm.Avalonia.FloatingPanelWindow"],
            KeyboardPath: PanelFloatCommands.DockPrefix),
    ];

    /// <summary>The surface with <paramref name="id"/>, or null.</summary>
    public static SurfaceDeclaration? Find(string id) => All.FirstOrDefault(s => s.Id == id);
}
