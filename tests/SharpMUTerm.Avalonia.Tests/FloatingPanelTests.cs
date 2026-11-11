using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;
using SharpMUTerm.Tui.Panels;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// Panels popped out into windows of their own (<see cref="FloatingPanelPresenter"/>, the client's
/// <c>SharpMUTermApp.Floating.cs</c>): the pane a panel leaves goes back into the layout and costs no cell, the
/// floating window runs the same client methods as the docked view, a remembered place is clamped to the screens there
/// are, the window never takes the keyboard and hands keys to the command line, and a host that cannot float keeps the
/// panel in its region.
/// </summary>
public class FloatingPanelTests
{
    private static readonly SKColor Back = new(0x10, 0x20, 0x30);

    [Test]
    public async Task OneFloatingWindowZoomKeyIsAppliedOnce()
    {
        var zoom = await Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, Back, 16, null);
            var panel = Floating(PanelIds.Vitals, new FloatingBounds(0, 0, 400, 300), _ => { }, () => { });
            var window = new FloatingPanelWindow(panel, view);
            window.Show();
            try
            {
                window.KeyPressQwerty(global::Avalonia.Input.PhysicalKey.Equal, global::Avalonia.Input.RawInputModifiers.Control);
                return view.ZoomPercent;
            }
            finally { window.Close(); }
        }, CancellationToken.None);
        await Assert.That(zoom).IsEqualTo(110);
    }

    // ── The client's half ────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task PoppingOutTakesThePanelOutOfThePanesAndDockingPutsItBack()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = DesktopLayerTests.InlineWindowApp(driver);
        app.RenderSnapshot("layout-combat");
        var vitals = Panel(driver, PanelIds.Vitals).WindowId;
        var before = Rects(app);
        await Assert.That(Panel(driver, PanelIds.Vitals).PopOut is not null).IsTrue().Because("the window can float vitals, so its view offers it");

        await Assert.That(app.PopOutPanel(vitals)).IsTrue();
        app.RenderWholeFrame();
        await Assert.That(app.IsFloating(vitals)).IsTrue();
        await Assert.That(Tabs(app)).DoesNotContain(vitals).Because("no pane holds a floating panel");
        await Assert.That(driver.Floating.Panels.Select(p => p.Model.WindowId)).IsEquivalentTo(new[] { vitals });
        await Assert.That(driver.Floating.Panels[0].Model.Title).IsEqualTo("Vitals");
        await Assert.That(driver.Pointer.Panels.Any(p => p.WindowId == vitals)).IsFalse().Because("nothing is drawn over a pane it left");
        await Assert.That(app.PopOutPanel(vitals)).IsFalse().Because("it is already out");

        // ⌃P offers the way back, and not the way out, while it floats.
        var catalog = app.BuildCatalog().Select(i => i.Id).ToArray();
        await Assert.That(catalog).Contains(PanelFloatCommands.Dock(vitals));
        await Assert.That(catalog).DoesNotContain(PanelFloatCommands.PopOut(vitals));

        await Assert.That(app.DispatchCommand(PanelFloatCommands.Dock(vitals))).IsTrue();
        app.RenderWholeFrame();
        await Assert.That(app.IsFloating(vitals)).IsFalse();
        await Assert.That(driver.Floating.Panels).IsEmpty();
        await Assert.That(Rects(app)).IsEquivalentTo(before).Because("docking back puts the panel exactly where the layout places it");
        await Assert.That(app.BuildCatalog().Select(i => i.Id)).Contains(PanelFloatCommands.PopOut(vitals));
    }

    [Test]
    public async Task APoppedOutPanelLeavesThePanesALayoutWithoutItWouldHave()
    {
        using var floatingDriver = new AvaloniaConsoleDriver(140, 36);
        await using var floated = DesktopLayerTests.InlineWindowApp(floatingDriver);
        floated.RenderSnapshot("layout-combat");
        var vitals = Panel(floatingDriver, PanelIds.Vitals).WindowId;
        await Assert.That(floated.PopOutPanel(vitals)).IsTrue();
        floated.RenderWholeFrame();

        // The same scene, with the vitals window closed and Combat applied less its vitals placement.
        using var plainDriver = new AvaloniaConsoleDriver(140, 36);
        await using var plain = DesktopLayerTests.InlineWindowApp(plainDriver);
        plain.RenderSnapshot("layout-combat");
        plain.SimulateWindowChange(vitals);
        await Assert.That(plain.DispatchCommand("layout:close")).IsTrue();
        var without = BuiltInLayouts.Combat with
        {
            Placements = BuiltInLayouts.Combat.Placements.Where(p => p.PanelId != PanelIds.Vitals).ToList(),
        };
        await Assert.That(plain.ApplyLayout(DemoScene.ActiveSessionKey, without)).IsTrue();
        plain.RenderWholeFrame();

        await Assert.That(Rects(floated)).IsEquivalentTo(Rects(plain)).Because("no cell is spent on a panel that floats");

        // NAWS is the session window's pane rectangle (ReportPaneSizes): the same in both.
        var output = Rects(plain).Keys.Single(k => !k.StartsWith("panel:", StringComparison.Ordinal) && !k.StartsWith("map:", StringComparison.Ordinal) && Rects(plain)[k].Width > 60);
        await Assert.That(Rects(floated)[output]).IsEqualTo(Rects(plain)[output]);
    }

    [Test]
    public async Task TheFloatingPanelRunsTheSameClientMethodsAsTheDockedOne()
    {
        // The map: a room walked to from the floating window and the same room's cell link in a docked map.
        using var aDriver = new AvaloniaConsoleDriver(140, 36);
        await using var a = DesktopLayerTests.InlineWindowApp(aDriver);
        a.RenderSnapshot("layout-combat");
        var map = Panel(aDriver, PanelIds.Map);
        var room = map.Map!.Rooms.First(r => !r.Current && r.Id != map.Map.AnchorId).Id;
        await Assert.That(a.PopOutPanel(map.WindowId)).IsTrue();
        var floatingMap = aDriver.Floating.Panels.Single().Content;
        await Assert.That(floatingMap.WalkTo).IsSameReferenceAs(map.WalkTo).Because("one walk callback per window, docked or floating");
        floatingMap.WalkTo!(room);

        using var bDriver = new AvaloniaConsoleDriver(140, 36);
        await using var b = DesktopLayerTests.InlineWindowApp(bDriver);
        b.RenderSnapshot("layout-combat");
        b.SimulateMapClick(map.WindowId, MapRenderer.LinkPrefix + room);
        await Assert.That(a.WalkState).IsNotEqualTo(string.Empty);
        await Assert.That(a.WalkState).IsEqualTo(b.WalkState);

        // The quick bar: its first key, from the floating window and from the docked cells.
        var quick = Panel(aDriver, PanelIds.QuickBar);
        await Assert.That(a.PopOutPanel(quick.WindowId)).IsTrue();
        var floatingQuick = aDriver.Floating.Panels.Single(p => p.Model.PanelId == PanelIds.QuickBar).Content;
        await Assert.That(floatingQuick.QuickBar).IsEqualTo(quick.QuickBar);
        floatingQuick.Pick!(0);
        b.SimulatePanelClick(quick.WindowId, QuickBarRenderer.LinkPrefix + "0");
        // The demo has no session, so both end in the same refusal, on the panel's own character.
        await Assert.That(a.LastNotice).IsNotNull();
        await Assert.That(a.LastNotice).IsEqualTo(b.LastNotice);
        await Assert.That(a.LastQuickMacro).IsEqualTo(b.LastQuickMacro);
    }

    [Test]
    public async Task APopOutIsOfferedOnThePanesMenuAndRemembersWhereItWasMoved()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = DesktopLayerTests.InlineWindowApp(driver);
        app.RenderSnapshot("layout-combat");
        var map = Panel(driver, PanelIds.Map);
        app.SimulateWindowChange(map.WindowId);
        var at = new System.Drawing.Point(map.Rect.X + (map.Rect.Width / 2), map.Rect.Y + (map.Rect.Height / 2));
        var entry = app.PointerMenu(at).SingleOrDefault(i => i.Label.StartsWith("Pop out", StringComparison.Ordinal));
        await Assert.That(entry).IsNotNull().Because("the pane's menu offers the pop out");
        entry!.Run();
        await Assert.That(app.IsFloating(map.WindowId)).IsTrue();

        // Where the player puts it is remembered on the window (and so the workspace), and by "save changes".
        var moved = new FloatingBounds(1200, 90, 420, 400);
        driver.Floating.Panels.Single().Moved(moved);
        await Assert.That(driver.Floating.Panels.Single().Model.Bounds).IsEqualTo(moved);
        var saved = app.CaptureSession().Windows.Single(w => w.Id == map.WindowId);
        await Assert.That(saved.Floating).IsEqualTo(moved);
        await Assert.That(app.DispatchCommand(LayoutCommands.SaveAsNew)).IsTrue();
        var layout = app.Layouts.All.Single(l => !l.IsBuiltIn);
        await Assert.That(layout.PlacementOf(PanelIds.Map)!.Floating).IsEqualTo(moved);

        // Docked and popped out again, it goes back where it was.
        await Assert.That(app.DockPanel(map.WindowId)).IsTrue();
        await Assert.That(app.PopOutPanel(map.WindowId)).IsTrue();
        await Assert.That(driver.Floating.Panels.Single().Model.Bounds).IsEqualTo(moved);

        // Applying the saved layout floats it at its saved place; applying Combat docks it.
        await Assert.That(app.DispatchCommand(LayoutCommands.Apply(BuiltInLayouts.CombatId))).IsTrue();
        await Assert.That(app.IsFloating(map.WindowId)).IsFalse();
        await Assert.That(app.DispatchCommand(LayoutCommands.Apply(layout.Id))).IsTrue();
        await Assert.That(app.IsFloating(map.WindowId)).IsTrue();
        await Assert.That(driver.Floating.Panels.Single().Model.Bounds).IsEqualTo(moved);
    }

    // ── A host that cannot float ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task TheTerminalKeepsAFloatingPanelInItsRegionAndOffersNoPopOut()
    {
        // A workspace saved by the window with vitals floating, resumed by the terminal.
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var window = DesktopLayerTests.InlineWindowApp(driver);
        window.RenderSnapshot("layout-combat");
        var vitals = Panel(driver, PanelIds.Vitals).WindowId;
        await Assert.That(window.PopOutPanel(vitals)).IsTrue();
        var state = window.CaptureSession();
        await Assert.That(state.Windows.Single(w => w.Id == vitals).Floating).IsNotNull();

        var config = DemoScene.Build();
        config.LastSession = state;
        using var headless = new HeadlessConsoleDriver(140, 36);
        await using var terminal = new SharpMUTermApp(config, ClientHost.Headless(headless, WindowOptions.Capabilities));
        terminal.RenderWholeFrame();

        await Assert.That(terminal.CanFloatPanels).IsFalse();
        await Assert.That(Tabs(terminal)).Contains(vitals).Because("the terminal draws every panel in its pane");
        await Assert.That(terminal.IsFloating(vitals)).IsFalse();
        await Assert.That(terminal.CaptureSession().Windows.Single(w => w.Id == vitals).Floating).IsNotNull()
            .Because("the flag is kept for the next host that can float");
        await Assert.That(terminal.BuildCatalog().Any(i => i.Id.StartsWith(PanelFloatCommands.PopOutPrefix, StringComparison.Ordinal)
            || i.Id.StartsWith(PanelFloatCommands.DockPrefix, StringComparison.Ordinal))).IsFalse();
        await Assert.That(terminal.DispatchCommand(PanelFloatCommands.PopOut(vitals))).IsFalse();

        // A layout that floats a panel is folded the same as one that does not, in the terminal.
        using var other = new HeadlessConsoleDriver(140, 36);
        await using var plain = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(other, WindowOptions.Capabilities));
        plain.RenderSnapshot("layout-combat");
        var floats = BuiltInLayouts.Combat with
        {
            Placements = BuiltInLayouts.Combat.Placements
                .Select(p => p.PanelId == PanelIds.Vitals ? p with { Floating = new FloatingBounds(10, 10, 300, 200) } : p).ToList(),
        };
        var rects = Rects(plain);
        await Assert.That(plain.ApplyLayout(DemoScene.ActiveSessionKey, floats)).IsTrue();
        plain.RenderWholeFrame();
        await Assert.That(Rects(plain)).IsEquivalentTo(rects);

        // And the window's own workspace, resumed by the window, floats it again and keeps it out of the panes.
        var again = DemoScene.Build();
        again.LastSession = state;
        using var windowDriver = new AvaloniaConsoleDriver(140, 36);
        await using var resumed = new SharpMUTermApp(again, DesktopLayerTests.InlineHost(windowDriver));
        resumed.RenderWholeFrame();
        await Assert.That(resumed.IsFloating(vitals)).IsTrue();
        await Assert.That(Tabs(resumed)).DoesNotContain(vitals);
    }

    // ── The window's half ────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ARememberedPlaceIsClampedToTheScreensThereAreNow()
    {
        var (position, reported, activated) = await Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(40, 10);
            var view = new TerminalView(driver, SKColors.White, Back, 16, null);
            var presenter = driver.Floating;
            presenter.Screens = () => [new FloatingBounds(0, 0, 1920, 1080)];
            presenter.Attach(owner: null, view);
            var reported = new List<FloatingBounds>();

            // Left on a second monitor, to the right of this one, that is no longer plugged in.
            presenter.Show([Floating(PanelIds.Vitals, new FloatingBounds(2500, 300, 400, 200), reported.Add, () => { })]);
            DesktopLayerTests.Drain();
            var window = presenter.Windows.Values.Single();
            var result = (window.Position, reported.ToList(), window.ShowActivated);
            presenter.Detach();
            driver.Dispose();
            return result;
        }, CancellationToken.None);

        await Assert.That(position).IsEqualTo(new PixelPoint(1520, 300));
        await Assert.That(reported.Last()).IsEqualTo(new FloatingBounds(1520, 300, 400, 200)).Because("the clamped place is the one remembered");
        await Assert.That(activated).IsFalse().Because("a floating window never takes the keyboard on opening");
    }

    [Test]
    public async Task ClosingTheWindowDocksThePanelAndTheClientClosingItDocksNothing()
    {
        var (docksOnClose, docksOnClientClose, closed) = await Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(40, 10);
            var view = new TerminalView(driver, SKColors.White, Back, 16, null);
            var presenter = driver.Floating;
            presenter.Screens = () => [new FloatingBounds(0, 0, 1920, 1080)];
            presenter.Attach(owner: null, view);
            var docks = 0;
            var map = Floating(PanelIds.Map, new FloatingBounds(100, 100, 400, 400), _ => { }, () => docks++);
            var vitals = Floating(PanelIds.Vitals, new FloatingBounds(600, 100, 300, 200), _ => { }, () => docks++);
            presenter.Show([map, vitals]);
            DesktopLayerTests.Drain();

            // The player closes the map's window.
            presenter.Windows[map.Model.WindowId].Close();
            DesktopLayerTests.Drain();
            var onClose = docks;

            // The client docks vitals: its window closes without docking it a second time.
            presenter.Show([]);
            DesktopLayerTests.Drain();
            var result = (onClose, docks - onClose, presenter.Windows.Count);
            presenter.Detach();
            driver.Dispose();
            return result;
        }, CancellationToken.None);

        await Assert.That(docksOnClose).IsEqualTo(1);
        await Assert.That(docksOnClientClose).IsEqualTo(0);
        await Assert.That(closed).IsEqualTo(0);
    }

    [Test]
    public async Task ThePopOutButtonTheFloatingViewAndTheKeysGoThroughTheClient()
    {
        var result = await Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(140, 36);
            var view = new TerminalView(driver, SKColors.White, Back, 16, null);
            var layer = new DesktopLayer(view, HostChrome.HeaderAndRail);
            var root = new Panel();
            root.Children.Add(view);
            root.Children.Add(layer);
            var owner = new Window { Content = root, Width = 1300, Height = 760 };
            owner.Show();
            view.Focus();
            driver.Floating.Screens = () => [new FloatingBounds(0, 0, 3840, 2160)];
            driver.Floating.Attach(owner, view);

            var app = DesktopLayerTests.InlineWindowApp(driver);
            driver.KeyPressed += (_, key) => app.SimulateKey(key);
            try
            {
                app.RenderSnapshot("layout-combat");
                DesktopLayerTests.Drain();
                var vitals = driver.Pointer.Panels.First(p => p.PanelId == PanelIds.Vitals).WindowId;
                var hadButton = layer.PopOuts.ContainsKey(vitals);

                layer.PopOuts[vitals].PerformClick();
                DesktopLayerTests.Drain();
                app.RenderWholeFrame();
                DesktopLayerTests.Drain();
                var floating = driver.Floating.Windows.GetValueOrDefault(vitals);
                var content = floating?.PanelView.GetType().Name;
                var buttonGone = !layer.PopOuts.ContainsKey(vitals);
                var focusKept = view.IsFocused;

                // Typing while the floating window is in front lands on the command line.
                floating!.KeyTextInput("n");
                DesktopLayerTests.Drain();
                var typed = app.PrimaryInputText;

                // Closing it docks the panel back.
                floating!.Close();
                DesktopLayerTests.Drain();
                app.RenderWholeFrame();
                DesktopLayerTests.Drain();
                return (hadButton, content, buttonGone, focusKept, typed, app.IsFloating(vitals), layer.PopOuts.ContainsKey(vitals), driver.Floating.Windows.Count);
            }
            finally
            {
                driver.Floating.Detach();
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                owner.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

        await Assert.That(result.hadButton).IsTrue().Because("a docked vitals strip carries the pop-out button");
        await Assert.That(result.content).IsEqualTo(nameof(VitalsGauges)).Because("the floating window draws the panel's own native view");
        await Assert.That(result.buttonGone).IsTrue();
        await Assert.That(result.focusKept).IsTrue().Because("the command line keeps the keyboard");
        await Assert.That(result.typed).EndsWith("n");
        await Assert.That(result.Item6).IsFalse().Because("closing the floating window docks the panel");
        await Assert.That(result.Item7).IsTrue().Because("docked, it offers the pop out again");
        await Assert.That(result.Item8).IsEqualTo(0);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static PanelOverlay Panel(AvaloniaConsoleDriver driver, string id) =>
        driver.Pointer.Panels.First(p => p.PanelId == id);

    /// <summary>Each pane's output rectangle by the window in front of it, so two clients' pane ids need not agree.</summary>
    private static Dictionary<string, PaneRect> Rects(SharpMUTermApp app)
    {
        var windows = app.PaneWindows();
        return app.PaneOutputRects().ToDictionary(r => windows.GetValueOrDefault(r.Key) ?? r.Key, r => r.Value, StringComparer.Ordinal);
    }

    private static List<string> Tabs(SharpMUTermApp app) => Flatten(app.CaptureSession().Root).SelectMany(p => p.Tabs).ToList();

    private static List<LayoutNodeState> Flatten(LayoutNodeState node) =>
        node.Children is { Count: > 0 } children ? children.SelectMany(Flatten).ToList() : [node];

    private static FloatingPanel Floating(string panelId, FloatingBounds bounds, Action<FloatingBounds> moved, Action dock)
    {
        var windowId = panelId == PanelIds.Map ? "map:demo" : $"panel:{panelId}:demo";
        var vitals = new VitalsModel([new VitalsMeter("hp", "412", "500", true, 412 / 500.0, false)], null);
        var content = new PanelOverlay(panelId, windowId, default, new PanelInk("#3ccf6a", "#ff5555", "#333338"), panelId == PanelIds.Vitals ? vitals : null);
        return new FloatingPanel(
            new FloatingPanelModel(windowId, panelId, panelId, "Corvid", bounds), content, "#202024", "#e0e0e0", moved, dock);
    }

    private static HeadlessUnitTestSession Session => TerminalViewTests.Session;
}
