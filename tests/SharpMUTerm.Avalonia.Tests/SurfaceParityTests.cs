using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
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

        // Notices: each toast the window was handed is the model the status row was drawn from, and the last is the
        // one the row carries now.
        {
            using var driver = new AvaloniaConsoleDriver(140, 36);
            await using var app = App(driver, DemoScene.Build());
            app.RenderSnapshot("notices");
            var said = driver.Pointer.Notices;
            await Assert.That(said.Count).IsEqualTo(4);
            await Assert.That(said.Count(n => n.Notice.Source is not null)).IsEqualTo(1).Because("one is a source label, not a key");
            await Assert.That((object)said[^1].Notice).IsEqualTo(app.DrawnModel(SurfaceCatalog.Notice)).Because("notice");
            await Assert.That(app.NoticeMarkup(said[^1].Notice)).IsEqualTo(app.LastNotice);
            await Assert.That(said[0].Notice.Action).IsNotNull().Because("the first offers its action");
            covered.Add(SurfaceCatalog.Notice);
        }

        // F1: the window's editor is handed the model the cell window draws for the same draft, in each mode and for a
        // MOO edit. Two clients, one with the window's editor and one without, opened on the same scene.
        foreach (var view in new[] { "compose", "compose-literal", "compose-preview", "moo-edit", "script-edit" })
        {
            using var nativeDriver = new AvaloniaConsoleDriver(140, 36);
            var composer = new FakeComposer();
            await using var native = App(nativeDriver, DemoScene.Build(), composer: composer);
            native.RenderSnapshot(view);

            using var cellDriver = new AvaloniaConsoleDriver(140, 36);
            await using var cells = App(cellDriver, DemoScene.Build());
            cells.RenderSnapshot(view);

            await Assert.That(composer.Session).IsNotNull().Because($"{view}: F1 opened in the window's editor");
            await Assert.That(native.Composer.IsOpen).IsFalse().Because($"{view}: and not in cells under it");
            await Assert.That(cells.DrawnModel(SurfaceCatalog.Compose)).IsEqualTo((object)composer.Session!.Model).Because(view);
            if (view == "script-edit")
            {
                await Assert.That(composer.Session.Model.Edit!.ErrorLine).IsEqualTo(14).Because("both hosts mark the line the error named");
            }
        }

        covered.Add(SurfaceCatalog.Compose);

        // The docked composer: the panel's model is the one its cells drew in that frame, and the one a client with no
        // native editor draws for the same scene.
        {
            using var nativeDriver = new AvaloniaConsoleDriver(140, 36);
            await using var native = App(nativeDriver, DemoScene.Build(), composer: new FakeComposer());
            native.RenderSnapshot("panel-composer");
            var panel = nativeDriver.Pointer.Panels.Single(p => p.PanelId == PanelIds.Composer);

            using var cellDriver = new AvaloniaConsoleDriver(140, 36);
            await using var cells = App(cellDriver, DemoScene.Build());
            cells.RenderSnapshot("panel-composer");

            await Assert.That(panel.Composer).IsNotNull();
            await Assert.That(panel.Composer!.Body).IsNotEmpty().Because("the scene types a draft");
            await Assert.That(native.DrawnModel(PanelIds.Composer, panel.WindowId)).IsEqualTo((object)panel.Composer).Because("panel-composer");
            await Assert.That(cells.DrawnModel(PanelIds.Composer, panel.WindowId)).IsEqualTo((object)panel.Composer).Because("panel-composer: the cell client");
            covered.Add(PanelIds.Composer);
        }

        // The modals: the session the window was handed carries the model the cell modal drew for the same scene — the
        // quit prompt's facts (connections and a draft), ⌃F's hits in each of its toggles, ⌃R's list, ⌥M's rows and the
        // welcome's menu and filled-in form.
        foreach (var (surface, view) in new[]
        {
            (SurfaceCatalog.Quit, "quit"),
            (SurfaceCatalog.Search, "search"),
            (SurfaceCatalog.Search, "search-regex"),
            (SurfaceCatalog.Search, "search-all"),
            (SurfaceCatalog.History, "history-search"),
            (SurfaceCatalog.Layouts, "layouts"),
            (SurfaceCatalog.Welcome, "welcome"),
            (SurfaceCatalog.Welcome, "welcome-connect"),
            (SurfaceCatalog.Help, "help"),
            (SurfaceCatalog.Dictionary, "dictionary-offer"),
            (SurfaceCatalog.Import, "import-review"),
            (SurfaceCatalog.ScriptConsole, "script-console"),
            (SurfaceCatalog.Search, "search-older"),
            (SurfaceCatalog.HistoryView, "search-history"),
        })
        {
            using var nativeDriver = new AvaloniaConsoleDriver(140, 36);
            var modals = new FakeModals();
            await using var native = App(nativeDriver, DemoScene.Build(), modals: modals);
            native.RenderSnapshot(view);

            using var cellDriver = new AvaloniaConsoleDriver(140, 36);
            await using var cells = App(cellDriver, DemoScene.Build());
            cells.RenderSnapshot(view);

            var session = modals.Open(surface);
            await Assert.That(session).IsNotNull().Because($"{view}: opened in the window's controls");
            var drawn = cells.DrawnModel(surface);
            await Assert.That(drawn).IsNotNull().Because($"{view}: the cell modal is open in the cell client");
            await Assert.That(drawn).IsEqualTo(ModelOf(session!)).Because(view);
            covered.Add(surface);
        }

        // ⌃F's ⌥H offer: the model both hosts draw offers older history while the pane cap trimmed what was searched.
        {
            using var nativeDriver = new AvaloniaConsoleDriver(140, 36);
            var modals = new FakeModals();
            await using var native = App(nativeDriver, DemoScene.Build(), modals: modals);
            native.RenderSnapshot("search");
            var offered = ((SearchSession)modals.Open(SurfaceCatalog.Search)!).Model;
            await Assert.That(offered.OffersOlder).IsTrue();
            await Assert.That(offered.HistoryNote).StartsWith(SearchModel.OlderKey);
        }

        // ⌃R filtered: the view hands its query to the native box, and the native list's rows for it are the cell list's.
        {
            using var nativeDriver = new AvaloniaConsoleDriver(140, 36);
            var modals = new FakeModals();
            await using var native = App(nativeDriver, DemoScene.Build(), modals: modals);
            native.RenderSnapshot("history-search-filter");

            using var cellDriver = new AvaloniaConsoleDriver(140, 36);
            await using var cells = App(cellDriver, DemoScene.Build());
            cells.RenderSnapshot("history-search-filter");

            var history = (HistorySession)modals.Open(SurfaceCatalog.History)!;
            await Assert.That(history.InitialQuery).IsEqualTo("north").Because("the filter view types its query into the native box");
            var rows = history.Model.View(history.InitialQuery).Select(m => m.Text).ToArray();
            await Assert.That(rows).IsNotEmpty();
            await Assert.That(rows).IsEquivalentTo(cells.HistorySearchRows, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }

        // A floating panel: the window it is drawn in is handed the model the client recorded for it, and the panel
        // inside is the model the docked strip's cells were drawn from.
        {
            using var driver = new AvaloniaConsoleDriver(140, 36);
            await using var app = App(driver, DemoScene.Build());
            app.RenderSnapshot("layout-combat");
            var vitals = Panel(driver, PanelIds.Vitals);
            await Assert.That(app.DispatchCommand(PanelFloatCommands.PopOut(vitals.WindowId))).IsTrue();
            var floating = driver.Floating.Panels.Single();
            await Assert.That(app.DrawnModel(SurfaceCatalog.FloatingPanel, vitals.WindowId)).IsEqualTo((object)floating.Model).Because("floating-panel");
            await Assert.That(floating.Content.Vitals).IsEqualTo(app.ReadVitals(DemoScene.ActiveSessionKey)).Because("floating-panel: the panel inside");
            covered.Add(SurfaceCatalog.FloatingPanel);
        }

        // The tab overflow chip: the menu the window opens is the list the cells recorded for that strip in the same frame.
        {
            using var driver = new AvaloniaConsoleDriver(56, 20);
            await using var app = App(driver, DemoScene.Build());
            app.RenderSnapshot("tabs-many");
            var (paneId, recorded) = app.TabOverflowMenus.Single();
            await Assert.That(app.OpenTabOverflow(paneId)).IsTrue();
            await Assert.That(driver.Pointer.LastModelMenu).IsNotNull().Because("tab-overflow: the window opens a native menu");
            await Assert.That((object)driver.Pointer.LastModelMenu!.Value.Menu).IsEqualTo(app.DrawnModel(SurfaceCatalog.TabOverflow, paneId)).Because("tab-overflow");
            await Assert.That(recorded.Entries).IsNotEmpty();
            covered.Add(SurfaceCatalog.TabOverflow);
        }

        var drawnNatively = SurfaceCatalog.All.Where(s => s.On(SurfaceHosts.Window).HasFlag(SurfaceDrawing.Native)).Select(s => s.Id);
        await Assert.That(covered).IsEquivalentTo(drawnNatively).Because("every surface the window draws natively has its model compared here");
    }

    /// <summary>
    /// <c>search-landed</c> goes through the window's native ⌃F: the query is searched there, and ⏎ on the second row
    /// ends in the cell ⏎'s method, so the native search has closed and the bar sits where the cell landing puts it.
    /// </summary>
    [Test]
    public async Task TheLandedSearchViewLandsThroughTheNativeSearchWhereTheCellsLand()
    {
        using var nativeDriver = new AvaloniaConsoleDriver(140, 36);
        var modals = new FakeModals();
        await using var native = App(nativeDriver, DemoScene.Build(), modals: modals);
        native.RenderSnapshot("search-landed");

        using var cellDriver = new AvaloniaConsoleDriver(140, 36);
        await using var cells = App(cellDriver, DemoScene.Build());
        cells.RenderSnapshot("search-landed");

        await Assert.That(modals.Shown.Any(s => s.Surface == SurfaceCatalog.Search)).IsTrue().Because("the search ran in the window's controls");
        await Assert.That(modals.Open(SurfaceCatalog.Search)).IsNull().Because("⏎ closed it");
        await Assert.That(native.SearchIsOpen).IsFalse().Because("no cell search was opened either");
        var bar = native.SearchBarIndex(native.ActiveWindowId());
        await Assert.That(bar).IsNotNull();
        await Assert.That(bar).IsEqualTo(cells.SearchBarIndex(cells.ActiveWindowId()));
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

        // Sidebar: a window row's menu, Close this window — the row is brought forward, then ⌃W's command runs.
        await Same("sidebar: row menu", "layout-combat",
            (app, _) =>
            {
                var target = OtherWindow(app);
                var menu = app.SidebarRowMenu(SidebarModel.KeyOf(RailRowKind.Window, target));
                app.RunSidebarMenuEntry(menu.Entries.Single(e => e.Command == "layout:close").Command!);
            },
            (app, _) =>
            {
                app.DispatchRailTarget(OtherWindow(app));
                app.DispatchCommand("layout:close");
            });
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
        await Same("map: pop out", "layout-combat",
            (app, d) => Panel(d, PanelIds.Map).PopOut!(),
            (app, d) => app.DispatchCommand(PanelFloatCommands.PopOut(Panel(d, PanelIds.Map).WindowId)));

        // Map editing: a room selected and edited on the canvas, and the same with the cell map's keys.
        await Same("map: select", "layout-combat",
            (app, d) => Panel(d, PanelIds.Map).SelectRoom!(OtherRoom(Panel(d, PanelIds.Map).Map!)),
            (app, d) => CellSelect(app, d));
        await Same("map: rename", "layout-combat",
            (app, d) => NativeEdit(d, room => MapEdit.Rename(room, "Spice Yard")),
            (app, d) =>
            {
                CellSelect(app, d);
                MapKeys(app, MapKey(ConsoleKey.R, 'r'));
                while (app.PrimaryInputText.Count(c => c == ' ') > 3 || !app.PrimaryInputText.EndsWith(' '))
                {
                    MapKeys(app, MapKey(ConsoleKey.Backspace, '\b'));
                }

                TypeLine(app, "Spice Yard");
            });
        await Same("map: lock", "layout-combat",
            (app, d) => NativeEdit(d, room => MapEdit.Lock(room, true)),
            (app, d) => { CellSelect(app, d); MapKeys(app, MapKey(ConsoleKey.L, 'l')); });
        await Same("map: move", "layout-combat",
            (app, d) => NativeEdit(d, room => MapEdit.Move(room, 1, 0)),
            (app, d) => { CellSelect(app, d); MapKeys(app, new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, true, false, false)); });
        await Same("map: delete", "layout-combat",
            (app, d) => NativeEdit(d, MapEdit.Delete),
            (app, d) => { CellSelect(app, d); MapKeys(app, MapKey(ConsoleKey.Delete, '\0')); });
        await Same("map: undo", "layout-combat",
            (app, d) =>
            {
                NativeEdit(d, room => MapEdit.Lock(room, true));
                Panel(d, PanelIds.Map).EditMap!(MapEdit.Undo);
            },
            (app, d) => { CellSelect(app, d); MapKeys(app, MapKey(ConsoleKey.L, 'l'), MapKey(ConsoleKey.U, 'u')); });
        await Same("map: exit to the next room", "layout-combat",
            (app, d) => NativeEdit(d, room => MapEdit.LinkNext(room, "climb")),
            (app, d) =>
            {
                CellSelect(app, d);
                MapKeys(app, MapKey(ConsoleKey.X, 'x'));
                TypeLine(app, "climb next");
            });

        // Another level or area, and back: the canvas's buttons and menu, and the cell title's and legend's links and keys.
        await Same("map: look up a level", "layout-combat",
            (app, d) => Panel(d, PanelIds.Map).LookMap!(MapLook.Up),
            (app, d) => app.SimulateMapClick(Panel(d, PanelIds.Map).WindowId, MapRenderer.LookPrefix + "up"));
        await Same("map: look at another area", "layout-combat",
            (app, d) => Panel(d, PanelIds.Map).LookMap!(MapLook.ToArea(Panel(d, PanelIds.Map).Map!.Areas.First(a => !a.Drawn).Name)),
            (app, d) =>
            {
                var map = Panel(d, PanelIds.Map).Map!;
                var index = map.Areas.ToList().FindIndex(a => !a.Drawn);
                app.SimulateMapClick(Panel(d, PanelIds.Map).WindowId, MapRenderer.LookPrefix + "area:" + index);
            });
        await Same("map: back to where I am", "layout-combat",
            (app, d) =>
            {
                Panel(d, PanelIds.Map).LookMap!(MapLook.Up);
                Panel(d, PanelIds.Map).LookMap!(MapLook.Here);
            },
            (app, d) =>
            {
                app.SimulateMapClick(Panel(d, PanelIds.Map).WindowId, MapRenderer.LookPrefix + "up");
                app.SimulateMapClick(Panel(d, PanelIds.Map).WindowId, MapRenderer.LookPrefix + "here");
            });
        await Same("map: next area while editing", "layout-combat",
            (app, d) =>
            {
                Panel(d, PanelIds.Map).SelectRoom!(OtherRoom(Panel(d, PanelIds.Map).Map!));
                Panel(d, PanelIds.Map).LookMap!(MapLook.NextArea);
            },
            (app, d) => { CellSelect(app, d); MapKeys(app, MapKey(ConsoleKey.V, 'v')); });

        // A hand edit given back to the server: the inspector's Reset button, and R on the cell map.
        await Same("map: reset to server", "layout-combat",
            (app, d) =>
            {
                NativeEdit(d, room => MapEdit.Rename(room, "Spice Yard"));
                Panel(d, PanelIds.Map).EditMap!(MapEdit.ResetToServer(OtherRoom(Panel(d, PanelIds.Map).Map!)));
            },
            (app, d) =>
            {
                CellSelect(app, d);
                MapKeys(app, MapKey(ConsoleKey.R, 'r'));
                while (app.PrimaryInputText.Count(c => c == ' ') > 3 || !app.PrimaryInputText.EndsWith(' '))
                {
                    MapKeys(app, MapKey(ConsoleKey.Backspace, '\b'));
                }

                TypeLine(app, "Spice Yard");
                MapKeys(app, new ConsoleKeyInfo('R', ConsoleKey.R, true, false, false));
            });
        probed.Add(PanelIds.Map);

        // Vitals: the pop-out button, and ⌃P's entry for the same window.
        await Same("vitals: pop out", "layout-combat",
            (app, d) => Panel(d, PanelIds.Vitals).PopOut!(),
            (app, d) => app.DispatchCommand(PanelFloatCommands.PopOut(Panel(d, PanelIds.Vitals).WindowId)));
        probed.Add(PanelIds.Vitals);

        // A floating panel: its window's Dock (and closing it), and ⌃P's dock entry.
        await Same("floating-panel: dock", "layout-combat",
            (app, d) =>
            {
                Panel(d, PanelIds.Vitals).PopOut!();
                d.Floating.Panels.Single().Dock();
            },
            (app, d) =>
            {
                var id = Panel(d, PanelIds.Vitals).WindowId;
                app.DispatchCommand(PanelFloatCommands.PopOut(id));
                app.DispatchCommand(PanelFloatCommands.Dock(id));
            });
        probed.Add(SurfaceCatalog.FloatingPanel);

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

        // Notice: a click on a toast runs its action; in cells the same command is the ⌃P entry, run by the registry.
        var openF5 = new SharpMUTerm.Client.Chrome.NoticeAction("screen:worlds", "Open F5");
        await Same("notice: action", "layout-combat",
            (app, d) =>
            {
                app.SimulateNotice("a set is missing", SharpMUTerm.Core.Diagnostics.MessageSeverity.Warning, "⌃P", openF5);
                var toast = d.Pointer.Notices[^1];
                toast.Run(toast.Notice.Action!.Command);
            },
            (app, _) =>
            {
                app.SimulateNotice("a set is missing", SharpMUTerm.Core.Diagnostics.MessageSeverity.Warning, "⌃P", openF5);
                app.DispatchCommand(openF5.Command);
            });
        probed.Add(SurfaceCatalog.Notice);

        // F1: ⌃S in the window's editor and ⌃S in the cell window send the same command for the same draft — as typed and
        // literal, one line and several paragraphs. Esc keeps the draft, and its mode, in both.
        foreach (var (draft, literal) in new[]
        {
            ("+pose waves\nand sits", false),
            ("say 100% [sure]; {x}", true),
            ("+bbpost 1=Title\n\nFirst paragraph.\n\nSecond, 50% [done].\n", false),
            ("+bbpost 1=Title\n\nFirst paragraph.\n\nSecond, 50% [done].\n", true),
        })
        {
            var native = await ComposeOnTheWire(native: true, draft, literal, send: true);
            var cells = await ComposeOnTheWire(native: false, draft, literal, send: true);
            await Assert.That(native.Sent).IsNotEmpty();
            await Assert.That(native.Sent).IsEquivalentTo(cells.Sent).Because($"compose: send '{draft}' literal={literal}");
            await Assert.That(native.Outcome).IsEqualTo(cells.Outcome);
        }

        {
            var native = await ComposeOnTheWire(native: true, "half a post", literal: true, send: false);
            var cells = await ComposeOnTheWire(native: false, "half a post", literal: true, send: false);
            await Assert.That(native.Sent).IsEmpty();
            await Assert.That(native.Outcome).IsEqualTo(cells.Outcome).Because("compose: close keeps the draft");
            await Assert.That(native.Outcome).Contains("draft=half a post literal");
        }

        probed.Add(SurfaceCatalog.Compose);

        // The docked composer: ⌃S from the window's editor and from the cells send one command to its character, and
        // Esc releases it to the command line in both.
        {
            var native = await DockedOnTheWire(native: true);
            var cells = await DockedOnTheWire(native: false);
            await Assert.That(native.Sent).IsEquivalentTo(new[] { "+pose waves%rand sits" });
            await Assert.That(native.Sent).IsEquivalentTo(cells.Sent).Because("composer: send");
            await Assert.That(native.Outcome).IsEqualTo(cells.Outcome).Because("composer: release");
            probed.Add(PanelIds.Composer);
        }

        // The quit prompt: Y quits and N stays, from the window's buttons and from the cell prompt's keys.
        await SameModal("quit: yes", "quit",
            (app, m) => ((QuitSession)m.Open(SurfaceCatalog.Quit)!).Answer(true),
            app => app.SimulateQuitKey(new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false)));
        await SameModal("quit: no", "quit",
            (app, m) => ((QuitSession)m.Open(SurfaceCatalog.Quit)!).Answer(false),
            app => app.SimulateQuitKey(new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false)));
        probed.Add(SurfaceCatalog.Quit);

        // ⌃F: ⏎ on the second hit lands on the same line; ⌥A widens and ⌥E reads a pattern, then ⏎ on the last hit.
        await SameModal("search: go", "search",
            (app, m) => ((SearchSession)m.Open(SurfaceCatalog.Search)!).Go(1),
            app =>
            {
                app.SimulateSearchKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
                app.SimulateSearchKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            });
        await SameModal("search: every window", "search",
            (app, m) =>
            {
                var search = (SearchSession)m.Open(SurfaceCatalog.Search)!;
                search.Search(search.Model.Query, regex: false, allWindows: true);
                search.Go(search.Model.Rows.Count - 1);
            },
            app =>
            {
                app.SimulateSearchKey(new ConsoleKeyInfo('\0', ConsoleKey.A, false, true, false));
                app.SimulateSearchKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
                app.SimulateSearchKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            });
        await SameModal("search: regex", "search",
            (app, m) =>
            {
                var search = (SearchSession)m.Open(SurfaceCatalog.Search)!;
                search.Search("gob+lin c", regex: true, allWindows: false);
                search.Go(0);
            },
            app =>
            {
                app.SimulateSearchKey(new ConsoleKeyInfo('\0', ConsoleKey.E, false, true, false));
                foreach (var _ in "goblin")
                {
                    app.SimulateSearchKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
                }

                app.SimulateSearchTyping("gob+lin c");
                app.SimulateSearchKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            });
        probed.Add(SurfaceCatalog.Search);

        // ⌃R: ⏎ on the first row for "north" places the same text on the command line, and sends nothing.
        await SameModal("history: insert", "history-search",
            (app, m) =>
            {
                var history = (HistorySession)m.Open(SurfaceCatalog.History)!;
                history.Insert(history.Model.View("north")[0].Index);
            },
            app =>
            {
                app.SimulateHistorySearchTyping("north");
                app.SimulateHistorySearchKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            });
        probed.Add(SurfaceCatalog.History);

        // ⌥M: apply Roleplay, save as new, rename the copy and delete it — each through the one verb method, so the
        // status, the library and the panes agree.
        await SameModal("layouts: apply", "layouts",
            (app, m) =>
            {
                var layouts = (LayoutsSession)m.Open(SurfaceCatalog.Layouts)!;
                layouts.Run(LayoutVerb.Apply, BuiltInLayouts.RoleplayId);
            },
            app => LayoutsKeysTo(app, BuiltInLayouts.RoleplayId, new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
        await SameModal("layouts: save as new", "layouts",
            (app, m) =>
            {
                var layouts = (LayoutsSession)m.Open(SurfaceCatalog.Layouts)!;
                layouts.Run(LayoutVerb.SaveAsNew, BuiltInLayouts.CombatId);
            },
            app => LayoutsKeysTo(app, BuiltInLayouts.CombatId, new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false)));
        await SameModal("layouts: rename", "layouts",
            (app, m) =>
            {
                var layouts = (LayoutsSession)m.Open(SurfaceCatalog.Layouts)!;
                layouts.Run(LayoutVerb.SaveAsNew, BuiltInLayouts.CombatId);
                layouts.Run(LayoutVerb.Rename, layouts.Model.Current!.Id, "Raid night");
            },
            app =>
            {
                LayoutsKeysTo(app, BuiltInLayouts.CombatId, new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false));
                app.SimulateLayoutsKey(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));
                for (var i = 0; i < 40; i++)
                {
                    app.SimulateLayoutsKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
                }

                foreach (var c in "Raid night")
                {
                    app.SimulateLayoutsKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
                }

                app.SimulateLayoutsKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            });
        await SameModal("layouts: delete", "layouts",
            (app, m) =>
            {
                var layouts = (LayoutsSession)m.Open(SurfaceCatalog.Layouts)!;
                layouts.Run(LayoutVerb.SaveAsNew, BuiltInLayouts.CombatId);
                layouts.Run(LayoutVerb.Delete, layouts.Model.Current!.Id);
            },
            app =>
            {
                LayoutsKeysTo(app, BuiltInLayouts.CombatId, new ConsoleKeyInfo('n', ConsoleKey.N, false, false, false));
                app.SimulateLayoutsKey(new ConsoleKeyInfo('d', ConsoleKey.D, false, false, false));
                app.SimulateLayoutsKey(new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false));
            });
        probed.Add(SurfaceCatalog.Layouts);

        // The welcome: the form filled in, then Connect in the window and ⏎ in the cells — through /connect's path, to
        // the same world and character, connected.
        await SameModal("welcome: connect", "welcome-connect",
            (app, m) =>
            {
                var welcome = (WelcomeSession)m.Open(SurfaceCatalog.Welcome)!;
                var form = welcome.Model;
                welcome.Connect(new WelcomeForm(form.Host, form.Port, form.Tls, form.Character, form.PasswordLater));
            },
            app => app.SimulateWelcomeKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
        await SameModal("welcome: layout and browse", "welcome",
            (app, m) =>
            {
                var welcome = (WelcomeSession)m.Open(SurfaceCatalog.Welcome)!;
                welcome.SetLayout(WelcomeLayout.Combat);
                welcome.Choose(WelcomeChoice.Settings);
            },
            app =>
            {
                app.SimulateWelcomeKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
                app.SimulateWelcomeKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
                app.SimulateWelcomeKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
                app.SimulateWelcomeKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            });
        probed.Add(SurfaceCatalog.Welcome);

        // Help: a slash command placed on the command line (sent nowhere), and a key's command run — each the entry the
        // other host's ⏎ picks, through RunHelpEntry.
        await SameModal("help: place a slash command", "help",
            (app, m) =>
            {
                var help = (HelpSession)m.Open(SurfaceCatalog.Help)!;
                help.Choose(HelpEntryIndex(help.Model, e => e.Kind == HelpKind.Command && e.Title.StartsWith("/connect", StringComparison.Ordinal)));
            },
            app => HelpKeysTo(app, e => e.Kind == HelpKind.Command && e.Title.StartsWith("/connect", StringComparison.Ordinal)));
        await SameModal("help: run a key", "help",
            (app, m) =>
            {
                var help = (HelpSession)m.Open(SurfaceCatalog.Help)!;
                help.Choose(HelpEntryIndex(help.Model, e => e.Kind == HelpKind.Key && e.Target == "layout:toggle-rail"));
            },
            app => HelpKeysTo(app, e => e.Kind == HelpKind.Key && e.Target == "layout:toggle-rail"));
        probed.Add(SurfaceCatalog.Help);

        // The dictionary offer: Download in a client that keeps no dictionaries folder refuses out loud in both.
        await SameModal("dictionary: download", "dictionary-offer",
            (app, m) => ((DictionaryOfferSession)m.Open(SurfaceCatalog.Dictionary)!).Accept(),
            app => app.SimulateHelpKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
        probed.Add(SurfaceCatalog.Dictionary);

        // The import review: Import writes the same world, character and set as the cells' Enter; with the passwords
        // switched on first (the window's check box, the cells' p), the password is stored on the character in both.
        await SameModal("import: accept", "import-review",
            (app, m) => ((ImportReviewSession)m.Open(SurfaceCatalog.Import)!).Accept(),
            app => app.SimulateImportKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
        await SameModal("import: passwords, then accept", "import-review",
            (app, m) =>
            {
                var review = (ImportReviewSession)m.Open(SurfaceCatalog.Import)!;
                review.TogglePasswords();
                review.Accept();
            },
            app =>
            {
                app.SimulateImportKey(new ConsoleKeyInfo('p', ConsoleKey.P, false, false, false));
                app.SimulateImportKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            });
        probed.Add(SurfaceCatalog.Import);
        // The script console: a line run at the prompt, a store key deleted, and the scripts reloaded — each through the
        // session in the window and through the cell console's keys, ending in RunConsoleLine, DeleteStoreKey and
        // ReloadConsoleScripts. The scene leaves "store.keys()" typed at the cell prompt.
        await SameModal("script console: run", "script-console",
            (app, m) => ((ScriptConsoleSession)m.Open(SurfaceCatalog.ScriptConsole)!).Run("store.keys()"),
            app => app.SimulateConsoleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)));
        await SameModal("script console: delete key", "script-console",
            (app, m) => ((ScriptConsoleSession)m.Open(SurfaceCatalog.ScriptConsole)!).Delete("kills"),
            app =>
            {
                app.SimulateConsoleKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));
                app.SimulateConsoleKey(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));
            });
        await SameModal("script console: reload", "script-console",
            (app, m) => ((ScriptConsoleSession)m.Open(SurfaceCatalog.ScriptConsole)!).Reload(),
            app => app.SimulateConsoleKey(new ConsoleKeyInfo('\f', ConsoleKey.L, false, false, true)));
        await SameModal("script console: edit a script", "script-console",
            (app, m) => ((ScriptConsoleSession)m.Open(SurfaceCatalog.ScriptConsole)!).EditScript(),
            app => app.SimulateConsoleKey(new ConsoleKeyInfo('\u0005', ConsoleKey.E, false, false, true)));
        probed.Add(SurfaceCatalog.ScriptConsole);

        // ⌃F's older history: ⌥H then ⏎ on the first older hit opens the history view on the same page in both; and the
        // history view's one move, a page down, reads the same page in both.
        await SameModal("search: older history", "search",
            (app, m) =>
            {
                var search = (SearchSession)m.Open(SurfaceCatalog.Search)!;
                search.Search(search.Model.Query, false, false, true);
                search.Go(search.Model.HeldRows);
            },
            app =>
            {
                app.SimulateSearchKey(new ConsoleKeyInfo('\0', ConsoleKey.H, false, true, false));
                for (var i = 0; i < app.SearchModelFor("goblin", false, false, true).HeldRows; i++)
                {
                    app.SimulateSearchKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
                }

                app.SimulateSearchKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            });
        await SameModal("history view: scroll", "search-history",
            (app, m) => ((ScrollbackViewSession)m.Open(SurfaceCatalog.HistoryView)!).Key(ConsoleKey.PageDown),
            app => app.SimulateHelpKey(new ConsoleKeyInfo('\0', ConsoleKey.PageDown, false, false, false)));
        probed.Add(SurfaceCatalog.HistoryView);

        var acting = SurfaceCatalog.All.Where(s => s.HasNative && s.Actions.Count > 0).Select(s => s.Id);
        // The tab overflow chip: an entry chosen from the window's menu and the same digit in the cell list bring the same
        // hidden tab forward.
        {
            using var nativeDriver = new AvaloniaConsoleDriver(56, 20);
            await using var a = App(nativeDriver, DemoScene.Build());
            a.RenderSnapshot("tabs-many");
            var before = Outcome(a);
            var (pane, _) = a.TabOverflowMenus.Single();
            a.OpenTabOverflow(pane);
            var (menu, dispatch) = nativeDriver.Pointer.LastModelMenu!.Value;
            dispatch(menu.Entries[0].Command!);

            using var cellDriver = new AvaloniaConsoleDriver(56, 20);
            await using var b = App(cellDriver, DemoScene.Build());
            b.RenderSnapshot("tabs-many");
            b.OpenTabOverflow(b.TabOverflowMenus.Single().Key, cells: true);
            b.SendMenu.SimulateKey(new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false));

            await Assert.That(Outcome(a)).IsNotEqualTo(before).Because("tab-overflow: the native choice did something");
            await Assert.That(Outcome(a)).IsEqualTo(Outcome(b)).Because("tab-overflow");
            await Assert.That(a.ActiveWindowId()).IsEqualTo(b.ActiveWindowId());
        }

        probed.Add(SurfaceCatalog.TabOverflow);

        await Assert.That(probed).IsEquivalentTo(acting).Because("every native surface with actions has a probe here");
    }

    /// <summary>
    /// A connected client writes <paramref name="draft"/> in F1 — the window's editor through its session, or the cell
    /// window through its keys — and sends it with ⌃S or closes it with Esc. What reached the wire, and the state left.
    /// </summary>
    private static async Task<(IReadOnlyList<string> Sent, string Outcome)> ComposeOnTheWire(bool native, string draft, bool literal, bool send)
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        var composer = native ? new FakeComposer() : null;
        var (app, telnet) = await Connected(driver, composer);
        await using var _ = app;
        app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
        if (composer is not null)
        {
            var session = composer.Session!;
            session.Edited(draft);
            if (literal)
            {
                session.ToggleEscaping();
            }

            if (send)
            {
                session.Send(draft);
            }
            else
            {
                session.Close(draft);
            }
        }
        else
        {
            app.Composer.SimulateTyping(draft);
            if (literal)
            {
                app.Composer.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.L, false, true, false));
            }

            app.Composer.SimulateKey(send
                ? new ConsoleKeyInfo('\x13', ConsoleKey.S, false, false, true)
                : new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        }

        // Where the draft went: reopened, F1 says what it holds and in which mode.
        app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
        var held = composer is not null
            ? $"{composer.Session!.Model.Body} {(composer.Session.Model.Escaping == ComposeEscaping.Literal ? "literal" : "as typed")}"
            : $"{app.Composer.Body} {(app.Composer.HeaderMarkup.Contains("literal", StringComparison.Ordinal) ? "literal" : "as typed")}";
        return (telnet.Lines.ToList(), Outcome(app) + " | draft=" + held);
    }

    /// <summary>
    /// A connected client docks its composer, types into it — the window's editor through its session, or the cells
    /// through the composer's keys — sends with ⌃S, types again and releases with Esc.
    /// </summary>
    private static async Task<(IReadOnlyList<string> Sent, string Outcome)> DockedOnTheWire(bool native)
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        var (app, telnet) = await Connected(driver, native ? new FakeComposer() : null);
        await using var _ = app;
        app.DispatchCommand("panel:composer");
        app.RenderNextFrame();
        var windowId = TerminalPanels.WindowId(PanelIds.Composer, "Quiet.Ann");
        var session = native ? driver.Pointer.Panels.Single(p => p.PanelId == PanelIds.Composer).ComposerSession! : null;
        if (session is not null)
        {
            session.Edited("+pose waves\nand sits");
            session.Send("+pose waves\nand sits");
            session.Edited("next");
            session.Release();
        }
        else
        {
            app.SimulateComposerTyping("+pose waves\nand sits");
            app.SimulateKey(new ConsoleKeyInfo('\x13', ConsoleKey.S, false, false, true));
            app.SimulateComposerTyping("next");
            app.SimulateKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));
        }

        return (telnet.Lines.ToList(), $"{Outcome(app)} | armed={app.ComposerArmed} | docked={app.DockedComposerIn(windowId)!.Body}");
    }

    private static async Task<(SharpMUTermApp App, SharpMUTerm.Tui.Tests.RecordingTelnetSession Telnet)> Connected(
        AvaloniaConsoleDriver driver, INativeComposer? composer)
    {
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);
        var app = App(driver, config, composer: composer);
        var telnet = new SharpMUTerm.Tui.Tests.RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        await app.BindWorldWithoutConnecting(world).ConnectAsync();
        app.RenderNextFrame();
        return (app, telnet);
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

    /// <summary>
    /// A modal probe: two clients on the same view, one handed native modals and one drawing them in cells; the
    /// native gesture on one, the cell keys on the other, and the client's state after each the same.
    /// </summary>
    private static async Task SameModal(
        string probe, string view, Action<SharpMUTermApp, FakeModals> native, Action<SharpMUTermApp> cells)
    {
        using var nativeDriver = new AvaloniaConsoleDriver(140, 36);
        var modals = new FakeModals();
        await using var a = App(nativeDriver, DemoScene.Build(), modals: modals);
        a.TelnetFactory = _ => new SharpMUTerm.Tui.Tests.RecordingTelnetSession();
        a.RenderSnapshot(view);
        var before = Outcome(a);
        native(a, modals);

        using var cellDriver = new AvaloniaConsoleDriver(140, 36);
        await using var b = App(cellDriver, DemoScene.Build());
        b.TelnetFactory = _ => new SharpMUTerm.Tui.Tests.RecordingTelnetSession();
        b.RenderSnapshot(view);
        cells(b);

        await Assert.That(Outcome(a)).IsNotEqualTo(before).Because($"{probe}: the native action did something");
        await Assert.That(Outcome(a)).IsEqualTo(Outcome(b)).Because(probe);
    }

    /// <summary>Moves the cell ⌥M list's pointer to <paramref name="layoutId"/> with ↑↓, then presses <paramref name="key"/>.</summary>
    private static void LayoutsKeysTo(SharpMUTermApp app, string layoutId, ConsoleKeyInfo key)
    {
        var model = app.LayoutsModelFor(DemoScene.ActiveSessionKey);
        var target = model.Rows.ToList().FindIndex(r => r.Id == layoutId);
        var step = target > model.Selected
            ? new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false)
            : new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false);
        for (var i = 0; i < Math.Abs(target - model.Selected); i++)
        {
            app.SimulateLayoutsKey(step);
        }

        app.SimulateLayoutsKey(key);
    }

    private static int HelpEntryIndex(HelpModel model, Func<HelpEntry, bool> match)
    {
        for (var i = 0; i < model.Entries.Count; i++)
        {
            if (match(model.Entries[i]))
            {
                return i;
            }
        }

        throw new InvalidOperationException("no such help entry");
    }

    /// <summary>Moves the cell help's pointer down to the entry <paramref name="match"/> picks, then presses ⏎.</summary>
    private static void HelpKeysTo(SharpMUTermApp app, Func<HelpEntry, bool> match)
    {
        var target = HelpEntryIndex(app.CellHelp!.Model, match);
        for (var i = 0; i < 500 && app.CellHelp!.SelectedEntry != target; i++)
        {
            app.SimulateHelpKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        }

        app.SimulateHelpKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    private static object ModelOf(ModalSession session) => session switch
    {
        QuitSession quit => quit.Model,
        SearchSession search => search.Model,
        HistorySession history => history.Model,
        LayoutsSession layouts => layouts.Model,
        WelcomeSession welcome => welcome.Model,
        HelpSession help => help.Model,
        DictionaryOfferSession offer => offer.Model,
        ImportReviewSession import => import.Model,
        ScriptConsoleSession console => console.Model,
        ScrollbackViewSession history => history.Model,
        _ => throw new ArgumentException(session.GetType().Name),
    };

    /// <summary>Everything a surface's action can change, read off the client.</summary>
    private static string Outcome(SharpMUTermApp app) => string.Join(
        " | ",
        "quit=" + app.QuitPromptOpen,
        "exit=" + app.ExitRequested,
        "search=" + app.SearchIsOpen + "@" + app.SearchBarIndex(app.ActiveWindowId()),
        "history=" + app.HistorySearchOpen,
        "layouts=" + app.LayoutsOpen + " " + Layouts(app),
        "worlds=" + string.Join(",", app.Configuration.Worlds.Select(w => $"{w.Name}:{w.Host}:{w.Port}:{w.UseTls}:{w.Layout}:{string.Join("+", w.Characters.Select(c => c.Name))}")),
        "sessions=" + string.Join(",", app.Configuration.Worlds.SelectMany(w => w.Characters.Select(c => $"{w.Name}.{c.Name}")).Select(k => k + ":" + app.FindSession(k)?.State)),
        "input=" + app.PrimaryInputText,
        "window=" + app.ActiveWindowId(),
        "panes=" + app.PaneIds.Count,
        "menu=" + app.MenuIsOpen,
        "screen=" + app.OpenSettingsKey,
        "layout=" + app.LiveLayoutId,
        "welcome=" + app.WelcomeIsOpen,
        "help=" + app.HelpIsOpen + "/" + app.DictionaryOfferOpen,
        "import=" + app.ImportReviewOpen + " " + string.Join(",", app.Configuration.TriggerSets.Select(s => s.Name + ":" + s.Triggers.Count))
            + " " + string.Join(",", app.Configuration.Worlds.SelectMany(w => w.Characters).Select(c => c.Name + (c.Password is null ? string.Empty : "*"))),
        "console=" + app.ScriptConsoleIsOpen + " " + ConsoleState(app),
        "older=" + (app.HistoryViewModel is { } page ? $"{page.Window}:{page.Top}:{page.Marked}" : "shut"),
        "quick=" + app.LastQuickMacro,
        "walk=" + app.WalkState,
        "map=" + app.MapDigest(),
        "rail=" + (app.BuildChromeOverlay().Sidebar.Collapsed ? "folded" : "open"),
        "floating=" + string.Join(",", app.FloatingPublished.Select(f => f.Model.WindowId)),
        "notice=" + app.LastNotice,
        "recent=" + string.Join(",", app.RecentCommandIds));

    private static string ConsoleState(SharpMUTermApp app)
    {
        var model = app.ScriptConsoleModelNow();
        return $"[{string.Join(";", model.Lines.Select(l => l.Kind + ":" + l.Text))}] [{string.Join(",", model.Store.Select(e => e.Key))}]";
    }

    private static string Layouts(SharpMUTermApp app)
    {
        var model = app.LayoutsModelFor(DemoScene.ActiveSessionKey);
        return $"{model.Status} [{string.Join(",", model.Rows.Select(r => r.Name + (r.InUse ? "*" : string.Empty) + (r.WorldDefault ? "^" : string.Empty)))}]";
    }

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

    private static ConsoleKeyInfo MapKey(ConsoleKey key, char c) => new(c, key, false, false, false);

    /// <summary>The cell way to select a room: Edit map from the palette, then a click on the room's cell link while editing.</summary>
    private static void CellSelect(SharpMUTermApp app, AvaloniaConsoleDriver driver)
    {
        var room = OtherRoom(Panel(driver, PanelIds.Map).Map!);
        app.DispatchCommand("map:edit");
        app.SimulateMapClick(Panel(driver, PanelIds.Map).WindowId, MapRenderer.LinkPrefix + room);
    }

    /// <summary>The native way: the room selected on the canvas, then an edit from its menu or inspector.</summary>
    private static void NativeEdit(AvaloniaConsoleDriver driver, Func<long, MapEdit> edit)
    {
        var room = OtherRoom(Panel(driver, PanelIds.Map).Map!);
        Panel(driver, PanelIds.Map).SelectRoom!(room);
        Panel(driver, PanelIds.Map).EditMap!(edit(room));
    }

    private static void MapKeys(SharpMUTermApp app, params ConsoleKeyInfo[] keys)
    {
        foreach (var key in keys)
        {
            app.SimulateKey(key);
        }
    }

    private static void TypeLine(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

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
        await Assert.That(hotkeys).Contains("⌃3");
        await Assert.That(hotkeys).DoesNotContain("▲ ⌃3");

        // The numpad is a key the window delivers: F4 marks no numpad binding dead, and Num7 reaches its macro.
        await Assert.That(MacroKeys.Verdict(WindowHost.Keys, "Num7").Fires).IsTrue();
        await Assert.That(hotkeys).DoesNotContain("▲ Num7");
        var numpad = KeyMapper.FromKeyDown(Key.NumPad7, KeyModifiers.None);
        await Assert.That(numpad).IsNotNull();
        await Assert.That(numpad!.Value.Key).IsEqualTo(ConsoleKey.NumPad7);
        await Assert.That(numpad.Value.KeyChar).IsEqualTo('7');
        var walk = config.TriggerSets.SelectMany(s => s.Macros).First(m => m.Key == "Num7").Command;
        await Assert.That(app.SimulateKey(numpad.Value)).IsEqualTo(walk);
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
        HostKeys? keys = null,
        INativeComposer? composer = null,
        INativeModals? modals = null) =>
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
            NativeComposer = composer,
            NativeModals = modals,
        }, save: save);

    /// <summary>A window editor that only records the session it was handed, so the client's side can be driven.</summary>
    private sealed class FakeComposer : INativeComposer
    {
        public ComposerSession? Session { get; private set; }

        public bool Available => true;

        public void Open(ComposerSession session, PanelInk ink) => Session = session;

        public void Close() => Session = null;
    }

    private sealed class FakeSettings : INativeSettings
    {
        public SettingsForm? Form { get; private set; }

        public void Show(SettingsForm form) => Form = form;

        public void Close() => Form = null;
    }

    /// <summary>Native modals that only record the sessions they were handed, so the client's side can be driven.</summary>
    private sealed class FakeModals : INativeModals
    {
        public List<ModalSession> Shown { get; } = [];

        public List<ModalSession> Closed { get; } = [];

        public bool Available => true;

        /// <summary>The session of <paramref name="surface"/> shown last and not closed since, or null.</summary>
        public ModalSession? Open(string surface) =>
            Shown.LastOrDefault(s => s.Surface == surface && !Closed.Contains(s) && !s.IsEnded);

        public void Show(ModalSession session) => Shown.Add(session);

        public void Close(ModalSession session) => Closed.Add(session);
    }

    private sealed class FakePalette : INativePalette
    {
        public PaletteSession? Session { get; private set; }

        public void Show(PaletteSession session) => Session = session;

        public void Close() => Session = null;
    }
}
