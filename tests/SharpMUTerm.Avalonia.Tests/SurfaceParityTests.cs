using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Tui;
using SharpMUTerm.Tui.Panels;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window's half of the surface catalog (<see cref="SurfaceCatalog"/>): every native view the window draws is a
/// declared surface, is handed that surface's model, is handed the very model the cells drew in the same frame, and
/// every click on it ends where the matching cell click ends. The terminal's half is <c>SurfaceCellTests</c>.
/// </summary>
[NotInParallel]
public class SurfaceParityTests
{
    /// <summary>Controls of the window that are the window itself rather than a surface of the client's.</summary>
    private static readonly HashSet<Type> Infrastructure = [typeof(TerminalView), typeof(DesktopLayer), typeof(MainWindow)];

    private static readonly Assembly Window = typeof(DesktopLayer).Assembly;

    // ── every native view is declared ────────────────────────────────────────────────────────────

    /// <summary>
    /// A control the window draws over the cells that no surface declares is a view nobody holds to the cells: it
    /// can say what it likes. Found by reflection, so a new one fails here until it is declared.
    /// </summary>
    [Test]
    public async Task EveryNativeViewIsADeclaredSurface()
    {
        var declared = SurfaceCatalog.All.SelectMany(s => s.NativeViews).ToHashSet(StringComparer.Ordinal);
        var views = Window.GetTypes()
            .Where(t => typeof(Control).IsAssignableFrom(t) && !t.IsAbstract && !Infrastructure.Contains(t))
            .Where(t => !t.IsNested)
            .ToList();

        await Assert.That(views.Count).IsGreaterThan(5).Because("the scan still finds the window's views");
        foreach (var view in views)
        {
            await Assert.That(declared).Contains(view.FullName!).Because($"{view.Name} draws over the cells and no surface declares it");
        }

        foreach (var name in declared)
        {
            await Assert.That(Window.GetType(name)).IsNotNull().Because($"{name} is declared and is not a type in the window");
        }
    }

    /// <summary>
    /// Some native view of each surface is handed the surface's model — as a parameter, or inside what it is handed
    /// (a property, a list, or a method of it that returns the model) — never state of its own.
    /// </summary>
    [Test]
    public async Task ANativeViewOfEachSurfaceReceivesItsModel()
    {
        foreach (var surface in SurfaceCatalog.All.Where(s => s.HasNative))
        {
            var takes = surface.NativeViews
                .Select(Window.GetType)
                .OfType<Type>()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                    .Cast<MethodBase>()
                    .Concat(t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)))
                .SelectMany(m => m.GetParameters())
                .Any(p => Carries(p.ParameterType, surface.Model));
            await Assert.That(takes).IsTrue().Because($"{surface.Id}: no view of it is handed a {surface.Model.Name}");
        }
    }

    private static bool Carries(Type type, Type model) =>
        type == model
        || type.GetProperties().Any(p => p.PropertyType == model || typeof(IEnumerable<>).MakeGenericType(model).IsAssignableFrom(p.PropertyType))
        || type.GetMethods(BindingFlags.Public | BindingFlags.Instance).Any(m => m.ReturnType == model);

    // ── the native model is the model the cells drew ─────────────────────────────────────────────

    /// <summary>
    /// What the window was told in a frame is what the cells were drawn from in that frame: every panel overlay, the
    /// header and the sidebar, the settings window's sections, the Layouts menu and ⌃P. A native view built from a
    /// model of its own would fail here before it showed something the terminal does not.
    /// </summary>
    [Test]
    public async Task NativeModelsAreTheModelsTheCellsDrew()
    {
        var covered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var view in new[] { "layout-combat", "layout-roleplay" })
        {
            using var driver = new AvaloniaConsoleDriver(140, 36);
            await using var app = App(driver, DemoScene.Build());
            app.RenderSnapshot(view);

            foreach (var panel in driver.Pointer.Panels)
            {
                object? model = (object?)panel.Vitals ?? (object?)panel.Map ?? (object?)panel.WhoHere ?? panel.QuickBar;
                if (model is null)
                {
                    continue;
                }

                await Assert.That(app.DrawnModel(panel.PanelId, panel.WindowId)).IsEqualTo(model).Because($"{view}: {panel.PanelId}");
                covered.Add(panel.PanelId);
            }

            var chrome = driver.Pointer.Chrome;
            await Assert.That(chrome).IsNotNull();
            await Assert.That(app.DrawnModel(SurfaceCatalog.Header)).IsEqualTo(chrome!.HeaderModel).Because($"{view}: header");
            await Assert.That(app.DrawnModel(SurfaceCatalog.Sidebar)).IsEqualTo(chrome.Sidebar).Because($"{view}: sidebar");
            covered.Add(SurfaceCatalog.Header);
            covered.Add(SurfaceCatalog.Sidebar);

            // The Layouts button's menu is the one the ⌥M commands answer for.
            MenuModel? offered = null;
            driver.Pointer.Actions.Single(a => a.Id == HeaderAction.LayoutsId).Menu(m => offered = m);
            await Assert.That(offered).IsEqualTo(app.LayoutsMenu()).Because($"{view}: layouts menu");
            covered.Add(SurfaceCatalog.LayoutsMenu);
        }

        // Settings: the window is handed the sections F7 and F8's cells are drawn from.
        {
            using var driver = new AvaloniaConsoleDriver(140, 36);
            var settings = new FakeSettings();
            await using var app = App(driver, DemoScene.Build(), settings: settings);
            app.RenderSnapshot(null);
            await Assert.That(app.DispatchCommand("screen:input")).IsTrue();
            await Assert.That(settings.Form).IsNotNull().Because("F8 opens the native settings window");
            await Assert.That(settings.Form!.Sections.SequenceEqual(app.Contributions.SettingsSections.Ordered)).IsTrue();
            covered.Add(SurfaceCatalog.Settings);
        }

        // ⌃P: the native palette's rows for a query are the cell palette's rows for it.
        foreach (var query in new[] { string.Empty, "split", "tog tim" })
        {
            using var nativeDriver = new AvaloniaConsoleDriver(140, 36);
            var palette = new FakePalette();
            await using var native = App(nativeDriver, DemoScene.Build(), palette: palette);
            native.RenderSnapshot(null);
            native.OpenMenu();
            await Assert.That(palette.Session).IsNotNull();

            using var cellDriver = new AvaloniaConsoleDriver(140, 36);
            await using var cells = App(cellDriver, DemoScene.Build());
            cells.RenderSnapshot(null);
            cells.OpenMenu();
            cells.SimulateCellPaletteQuery(query);

            var drawn = (PaletteView?)cells.DrawnModel(SurfaceCatalog.Palette);
            await Assert.That(drawn).IsNotNull();
            await Assert.That(Rows(palette.Session!.View(query))).IsEquivalentTo(Rows(drawn!), TUnit.Assertions.Enums.CollectionOrdering.Matching)
                .Because($"query '{query}'");
        }

        covered.Add(SurfaceCatalog.Palette);

        // ⌃B armed: the window's header lists the keymap the cell panel draws.
        {
            using var driver = new AvaloniaConsoleDriver(140, 36);
            await using var app = App(driver, DemoScene.Build());
            app.RenderSnapshot("prefix-panel");
            var prefix = driver.Pointer.Chrome?.HeaderModel.Prefix;
            await Assert.That(prefix).IsNotNull().Because("the native header shows the armed keymap");
            await Assert.That(app.DrawnModel(SurfaceCatalog.Prefix)).IsEqualTo(prefix).Because("prefix");
            covered.Add(SurfaceCatalog.Prefix);
        }

        // The folded rail: the window's strip is drawn from the sidebar model the cells drew folded.
        {
            using var driver = new AvaloniaConsoleDriver(140, 36);
            await using var app = App(driver, DemoScene.Build());
            app.RenderSnapshot(null);
            await Assert.That(app.DispatchCommand("layout:toggle-rail")).IsTrue();
            app.RenderSnapshot(null);
            var sidebar = driver.Pointer.Chrome!.Sidebar;
            await Assert.That(sidebar.Collapsed).IsTrue();
            await Assert.That(app.DrawnModel(SurfaceCatalog.Sidebar)).IsEqualTo(sidebar).Because("rail-strip");
            covered.Add(SurfaceCatalog.RailStrip);
        }

        var drawnNatively = SurfaceCatalog.All.Where(s => s.On(SurfaceHosts.Window).HasFlag(SurfaceDrawing.Native)).Select(s => s.Id);
        await Assert.That(covered).IsEquivalentTo(drawnNatively).Because("every surface the window draws natively has its model compared here");
    }

    private static string[] Rows(PaletteView view) =>
        view.Rows.Select(r => r is PaletteHeader h ? "# " + h.Label : ((PaletteEntry)r).Item.Id + " " + ((PaletteEntry)r).Item.Key).ToArray();

    // ── a native action ends where the cell action ends ──────────────────────────────────────────

    /// <summary>
    /// For each surface, its native action and the matching cell click are run on two clients built alike, and the
    /// client's state after each is the same — and is not the state before, so a path that does nothing on both
    /// sides does not pass.
    /// </summary>
    [Test]
    public async Task ANativeActionEndsWhereTheCellActionEnds()
    {
        var probed = new HashSet<string>(StringComparer.Ordinal);

        // Header: the search box opens ⌃P; the ☰ link does too. The character click is the sidebar's.
        await Same("header: palette", "layout-combat",
            (app, _) => app.ActivateChromeTarget(HeaderModel.PaletteTarget),
            (app, _) => app.SimulateHeaderLink(HeaderModel.PaletteTarget));
        await Same("header: character", "layout-combat",
            (app, _) => app.ActivateChromeTarget(app.BuildChromeOverlay().HeaderModel.Character!.Target),
            (app, _) => app.DispatchRailTarget(app.BuildChromeOverlay().HeaderModel.Character!.Target));
        probed.Add(SurfaceCatalog.Header);

        // Sidebar: a window row, and Add a game (⌃P ▸ Welcome in cells).
        await Same("sidebar: window", "layout-combat",
            (app, _) => app.ActivateChromeTarget(OtherWindow(app)),
            (app, _) => app.DispatchRailTarget(OtherWindow(app)));
        await Same("sidebar: add a game", "layout-combat",
            (app, _) => app.ActivateChromeTarget(SidebarModel.AddGameTarget),
            (app, _) => app.DispatchCommand(SidebarModel.AddGameTarget));
        probed.Add(SurfaceCatalog.Sidebar);

        // The folded rail: the strip's fold button, and the ⌃P command beside ⌃B b.
        await Same("rail-strip: fold", "layout-combat",
            (app, _) => app.ActivateChromeTarget(SidebarModel.CollapseTarget),
            (app, _) => app.DispatchCommand("layout:toggle-rail"));
        probed.Add(SurfaceCatalog.RailStrip);

        // Map: a room clicked on the canvas and the same room's cell link.
        await Same("map: walk", "layout-combat",
            (app, d) => Panel(d, PanelIds.Map).WalkTo!(OtherRoom(Panel(d, PanelIds.Map).Map!)),
            (app, d) => app.SimulateMapClick(Panel(d, PanelIds.Map).WindowId, MapRenderer.LinkPrefix + OtherRoom(Panel(d, PanelIds.Map).Map!)));
        probed.Add(PanelIds.Map);

        // Who is here: the first row.
        await Same("who-here: pick", "layout-roleplay",
            (app, d) => Panel(d, PanelIds.WhoHere).Pick!(0),
            (app, d) => app.SimulatePanelClick(Panel(d, PanelIds.WhoHere).WindowId, WhoHereRenderer.LinkPrefix + "0"));
        probed.Add(PanelIds.WhoHere);

        // Quick bar: the first key, and "F4 edits keys".
        await Same("quick-bar: pick", "layout-combat",
            (app, d) => Panel(d, PanelIds.QuickBar).Pick!(0),
            (app, d) => app.SimulatePanelClick(Panel(d, PanelIds.QuickBar).WindowId, QuickBarRenderer.LinkPrefix + "0"));
        await Same("quick-bar: edit", "layout-combat",
            (app, d) => Panel(d, PanelIds.QuickBar).Dispatch!(QuickBarModel.EditCommand),
            (app, d) => app.SimulatePanelClick(Panel(d, PanelIds.QuickBar).WindowId, QuickBarRenderer.EditLink));
        probed.Add(PanelIds.QuickBar);

        // Layouts: choosing Roleplay in the menu, and running the ⌥M list's command for it.
        await Same("layouts-menu: apply", "layout-combat",
            (app, d) =>
            {
                var action = d.Pointer.Actions.Single(a => a.Id == HeaderAction.LayoutsId);
                MenuModel? menu = null;
                action.Menu(m => menu = m);
                action.Dispatch(menu!.Entries.Single(e => e.Label == "Roleplay").Command!);
            },
            (app, _) => app.DispatchCommand(LayoutCommands.Apply(BuiltInLayouts.RoleplayId)));
        probed.Add(SurfaceCatalog.LayoutsMenu);

        // Settings: resetting a changed field in the window, and r on the same field of F8's cells.
        {
            var (nativeEcho, nativeSaves) = SettingsReset(native: true);
            var (cellEcho, cellSaves) = SettingsReset(native: false);
            await Assert.That(nativeEcho).IsTrue();
            await Assert.That(nativeEcho).IsEqualTo(cellEcho);
            await Assert.That(nativeSaves).IsEqualTo(cellSaves).Because("both go through the one save funnel, once");
            probed.Add(SurfaceCatalog.Settings);
        }

        // ⌃P: an entry run from the native palette, and the same entry chosen in the cell palette.
        {
            using var nativeDriver = new AvaloniaConsoleDriver(140, 36);
            var palette = new FakePalette();
            await using var native = App(nativeDriver, DemoScene.Build(), palette: palette);
            native.RenderSnapshot(null);
            var before = Outcome(native);
            native.OpenMenu();
            palette.Session!.Run("layout:split-right");

            using var cellDriver = new AvaloniaConsoleDriver(140, 36);
            await using var cells = App(cellDriver, DemoScene.Build());
            cells.RenderSnapshot(null);
            cells.OpenMenu();
            await Assert.That(cells.SimulateCellPaletteChoice("split right", "layout:split-right")).IsTrue();

            await Assert.That(Outcome(native)).IsNotEqualTo(before);
            await Assert.That(Outcome(native)).IsEqualTo(Outcome(cells)).Because("palette: run");
            probed.Add(SurfaceCatalog.Palette);
        }

        var acting = SurfaceCatalog.All.Where(s => s.HasNative && s.Actions.Count > 0).Select(s => s.Id);
        await Assert.That(probed).IsEquivalentTo(acting).Because("every native surface with actions has a probe here");
    }

    private static (bool Echo, int Saves) SettingsReset(bool native)
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        var config = DemoScene.Build();
        config.Input.LocalEcho = false;
        var saves = 0;
        var settings = native ? new FakeSettings() : null;
        var app = App(driver, config, settings: settings, save: _ => saves++);
        try
        {
            app.RenderSnapshot(null);
            app.DispatchCommand("screen:input");
            if (settings is not null)
            {
                var form = settings.Form!;
                var field = form.Section("input")!.Fields.First(f => f.Binding is not null && !form.IsDefault(f));
                form.Reset(field);
            }
            else
            {
                app.SimulateSettingsKey(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));
            }

            return (config.Input.LocalEcho, saves);
        }
        finally
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static async Task Same(
        string probe, string view, Action<SharpMUTermApp, AvaloniaConsoleDriver> native, Action<SharpMUTermApp, AvaloniaConsoleDriver> cells)
    {
        using var nativeDriver = new AvaloniaConsoleDriver(140, 36);
        await using var a = App(nativeDriver, DemoScene.Build());
        a.RenderSnapshot(view);
        var before = Outcome(a);
        native(a, nativeDriver);

        using var cellDriver = new AvaloniaConsoleDriver(140, 36);
        await using var b = App(cellDriver, DemoScene.Build());
        b.RenderSnapshot(view);
        cells(b, cellDriver);

        await Assert.That(Outcome(a)).IsNotEqualTo(before).Because($"{probe}: the native action did something");
        await Assert.That(Outcome(a)).IsEqualTo(Outcome(b)).Because(probe);
    }

    /// <summary>Everything a surface's action can change, read off the client.</summary>
    private static string Outcome(SharpMUTermApp app) => string.Join(
        " | ",
        "input=" + app.PrimaryInputText,
        "window=" + app.ActiveWindowId(),
        "panes=" + app.PaneIds.Count,
        "menu=" + app.MenuIsOpen,
        "screen=" + app.OpenSettingsKey,
        "layout=" + app.LiveLayoutId,
        "welcome=" + app.WelcomeIsOpen,
        "quick=" + app.LastQuickMacro,
        "walk=" + app.WalkState,
        "rail=" + (app.BuildChromeOverlay().Sidebar.Collapsed ? "folded" : "open"),
        "notice=" + app.LastNotice,
        "recent=" + string.Join(",", app.RecentCommandIds));

    private static PanelOverlay Panel(AvaloniaConsoleDriver driver, string id) =>
        driver.Pointer.Panels.First(p => p.PanelId == id);

    private static string OtherWindow(SharpMUTermApp app)
    {
        var sidebar = app.BuildChromeOverlay().Sidebar;
        var selected = CommandIds.Window(app.ActiveWindowId());
        return sidebar.Targets().Last(t => t.StartsWith(CommandIds.Window(string.Empty), StringComparison.Ordinal) && t != selected);
    }

    private static long OtherRoom(MapViewModel map) =>
        map.Rooms.First(r => !r.Current && r.Id != map.AnchorId).Id;

    // ── F4 and the quick bar, per host ──────────────────────────────────────────────────────────

    /// <summary>
    /// The window's verdicts: ⌃3 arrives here and fires, ⌥⏎ is the window's (it maximises) and never reaches a macro;
    /// a macro on ⌃3 runs from the key path and F4 does not mark it dead.
    /// </summary>
    [Test]
    public async Task F4TellsTheWindowsTruth()
    {
        await Assert.That(MacroKeys.Verdict(WindowHost.Keys, "Ctrl+3").Fires).IsTrue();
        await Assert.That(MacroKeys.Verdict(WindowHost.Keys, "Ctrl+Shift+Left").Fires).IsTrue();
        await Assert.That(MacroKeys.Verdict(WindowHost.Keys, "Alt+Enter").Delivery).IsEqualTo(MacroKeyDelivery.Taken);
        await Assert.That(MacroKeys.Verdict(WindowHost.Keys, "Alt+Enter").Reason).Contains("maximises");
        await Assert.That(MacroKeys.Verdict(WindowHost.Keys, "Ctrl+Enter").Fires).IsFalse().Because("Ctrl+⏎ is the window's newline");

        var config = DemoScene.Build();
        using var driver = new AvaloniaConsoleDriver(120, 34);
        await using var app = App(driver, config);
        app.BindWorldWithoutConnecting(config.Worlds[0]);
        var macro = config.TriggerSets.SelectMany(s => s.Macros).Single(m => m.Key == "Ctrl+F1");
        macro.Key = "Ctrl+3";

        var key = KeyMapper.FromKeyDown(Key.D3, KeyModifiers.Control);
        await Assert.That(key).IsNotNull();
        await Assert.That(app.SimulateKey(key!.Value)).IsEqualTo("score");

        var hotkeys = string.Join('\n', KeypadScreenRenderer.HotkeysColumn(app.Host.Keys, config.TriggerSets.SelectMany(s => s.Macros).ToList()));
        await Assert.That(hotkeys).Contains("Ctrl+3");
        await Assert.That(hotkeys).DoesNotContain("▲ Ctrl+3");
    }

    /// <summary>The quick bar reads the host's keys: a chord the host keeps for itself is drawn as one that does not fire.</summary>
    [Test]
    public async Task TheQuickBarReadsTheHostsKeys()
    {
        var keeps = WindowHost.Keys with
        {
            Reserved = [.. WindowHost.Keys.Reserved, new ReservedChord(new HostChord(ConsoleKey.F1, ConsoleModifiers.Control, "Ctrl+F1", "Ctrl+F1"), "a test")],
        };

        using var driver = new AvaloniaConsoleDriver(120, 34);
        await using var window = App(driver, DemoScene.Build(), keys: keeps);
        using var plain = new AvaloniaConsoleDriver(120, 34);
        await using var terminalKeys = App(plain, DemoScene.Build(), keys: HostKeys.Terminal);

        var kept = window.QuickBarEntries(DemoScene.ActiveSessionKey).Items.Single(i => i.Key == "⌃F1");
        var free = terminalKeys.QuickBarEntries(DemoScene.ActiveSessionKey).Items.Single(i => i.Key == "⌃F1");
        await Assert.That(kept.Fires).IsFalse();
        await Assert.That(free.Fires).IsTrue();
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static SharpMUTermApp App(
        AvaloniaConsoleDriver driver,
        AppConfiguration config,
        INativeSettings? settings = null,
        INativePalette? palette = null,
        Action<AppConfiguration>? save = null,
        HostKeys? keys = null) =>
        new(config, WindowHost.Create(driver, clipboard: null) with
        {
            Capabilities = WindowOptions.Capabilities,
            Audio = null,
            Presence = null,
            Announcer = null,
            RunsInline = true,
            NativeSettings = settings,
            NativePalette = palette,
            Keys = keys ?? WindowHost.Keys,
        }, save: save);

    private sealed class FakeSettings : INativeSettings
    {
        public SettingsForm? Form { get; private set; }

        public void Show(SettingsForm form) => Form = form;

        public void Close() => Form = null;
    }

    private sealed class FakePalette : INativePalette
    {
        public PaletteSession? Session { get; private set; }

        public void Show(PaletteSession session) => Session = session;

        public void Close() => Session = null;
    }
}
