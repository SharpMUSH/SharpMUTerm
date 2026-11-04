using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;
using SkiaSharp;
using Color = SharpConsoleUI.Color;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window's native views over the cells (<see cref="DesktopLayer"/>): the header's Layouts button, whose
/// presses are the window's and whose menu is the client's commands, and the native vitals gauges, drawn from the
/// same model as the cell strip under them and taking no input of their own.
/// </summary>
public class DesktopLayerTests
{
    private static readonly SKColor Back = new(0x10, 0x20, 0x30);

    private static readonly VitalsModel Vitals = new(
        [
            new VitalsMeter("hp", "412", "500", true, 412 / 500.0, false),
            new VitalsMeter("mp", "23", "120", true, 23 / 120.0, true),
        ],
        new VitalsTarget("a rotting ghoul", 0.4));

    // ── The client's half: what it tells a host ──────────────────────────────────────────────────

    [Test]
    public async Task TheClientPublishesTheVitalsPaneWithTheModelItsCellStripIsDrawnFrom()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = InlineWindowApp(driver);
        app.RenderSnapshot("layout-combat");

        var panel = driver.Pointer.Panels.SingleOrDefault(p => p.PanelId == PanelIds.Vitals);
        await Assert.That(panel).IsNotNull();
        var pane = app.PaneOutputRects().Single(r => app.PaneWindows().GetValueOrDefault(r.Key) == panel!.WindowId);
        await Assert.That(panel!.Rect).IsEqualTo(pane.Value).Because("the overlay goes exactly where the strip's cells are");
        await Assert.That(panel.Vitals).IsEqualTo(app.ReadVitals(DemoScene.ActiveSessionKey));
        await Assert.That(panel.Vitals!.Meters.Count).IsGreaterThan(0);
        await Assert.That(driver.Pointer.Actions.Select(a => a.Id)).Contains(HeaderAction.LayoutsId);
    }

    [Test]
    public async Task PublishingPanelsMovesNoPaneRectangle()
    {
        using var headless = new HeadlessConsoleDriver(140, 36);
        await using var terminal = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, WindowOptions.Capabilities));
        terminal.RenderSnapshot("layout-combat");

        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var window = InlineWindowApp(driver);
        window.RenderSnapshot("layout-combat");

        await Assert.That(driver.Pointer.Panels.Count).IsGreaterThan(0);
        await Assert.That(window.PaneOutputRects().OrderBy(r => r.Key).ToArray())
            .IsEquivalentTo(terminal.PaneOutputRects().OrderBy(r => r.Key).ToArray());
    }

    [Test]
    public async Task TheLayoutsMenuIsTheLayoutLibraryWithTheOneInUseChecked()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = InlineWindowApp(driver);
        app.RenderSnapshot("layout-combat");

        var items = app.LayoutsMenuItems();
        var labels = items.Where(i => !i.IsSeparator).Select(i => i.Label).ToArray();
        await Assert.That(labels).Contains("Roleplay");
        await Assert.That(labels).Contains("Combat");
        await Assert.That(labels).Contains("Save changes");
        await Assert.That(labels).Contains("Save as new");
        await Assert.That(labels).Contains("Use for Aetherfall");
        await Assert.That(labels).Contains("Use for Corvid only");
        await Assert.That(labels).Contains("Manage layouts…");
        await Assert.That(items.Single(i => i.Checked).Label).IsEqualTo("Combat");

        // Choosing an entry runs the command, through the registry, that the ⌥M list's ⏎ runs.
        items.Single(i => i.Label == "Roleplay").Run();
        await Assert.That(app.LiveLayoutId).IsEqualTo(BuiltInLayouts.RoleplayId);
        await Assert.That(app.LayoutsMenuItems().Single(i => i.Checked).Label).IsEqualTo("Roleplay");
    }

    [Test]
    public async Task EveryLayoutCommandResolvesThroughTheRegistry()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = InlineWindowApp(driver);
        app.RenderSnapshot("layout-combat");

        foreach (var id in new[]
                 {
                     LayoutCommands.Apply(BuiltInLayouts.CombatId), LayoutCommands.SaveChanges, LayoutCommands.SaveAsNew,
                     LayoutCommands.UseForWorld, LayoutCommands.UseForCharacter,
                 })
        {
            await Assert.That(app.Contributions.Commands.Resolve(id)).IsNotNull().Because(id);
        }

        await Assert.That(app.DispatchCommand(LayoutCommands.SaveAsNew)).IsTrue();
        await Assert.That(app.Layouts.All.Count(l => !l.IsBuiltIn)).IsEqualTo(1);
        await Assert.That(app.DispatchCommand(LayoutCommands.UseForCharacter)).IsTrue();
    }

    // ── The window's half: what it draws, and where the pointer goes ─────────────────────────────

    [Test]
    public async Task ThePressOnTheLayoutsButtonNeverReachesTheClientAndOpensTheClientsMenu()
    {
        var (visible, clientSaw, asked, entries) = await Run((window, view, layer, driver) =>
        {
            var asked = 0;
            Offer(driver, show =>
            {
                asked++;
                show([new PointerMenuItem("Combat", null, () => { }, Checked: true), PointerMenuItem.Separator, new PointerMenuItem("Manage layouts…", "⌥M", () => { })]);
            });
            var clientSaw = new List<List<MouseFlags>>();
            driver.MouseEvent += (_, flags, _) => clientSaw.Add(flags);

            window.UpdateLayout();
            var button = layer.Button(HeaderAction.LayoutsId)!;
            var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(centre);
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            window.MouseDown(centre, MouseButton.Right);
            window.MouseUp(centre, MouseButton.Right);
            Drain();
            var entries = layer.OpenedMenu?.Items.Select(i => i is MenuItem m ? $"{m.IsChecked}" : "-").ToArray() ?? [];
            return (button.IsVisible, clientSaw, asked, entries);
        });

        await Assert.That(visible).IsTrue();
        await Assert.That(clientSaw).IsEmpty().Because("every press on the button is the window's");
        await Assert.That(asked).IsEqualTo(1).Because("only the left click opens the menu");
        await Assert.That(entries).IsEquivalentTo(new[] { "True", "-", "False" });
    }

    [Test]
    public async Task TheButtonCoversOnlyBlankHeaderCellsAndStandsDownOverText()
    {
        var (blank, overText) = await Run((window, view, layer, driver) =>
        {
            Offer(driver, _ => { });
            var blank = layer.Button(HeaderAction.LayoutsId)!.IsVisible;
            driver.SetNarrowCell(driver.Frame.Width - 2, 0, 'x', Color.Default, Color.Default);
            driver.Flush();
            Drain();
            return (blank, layer.Button(HeaderAction.LayoutsId)!.IsVisible);
        });

        await Assert.That(blank).IsTrue();
        await Assert.That(overText).IsFalse();
    }

    [Test]
    public async Task VitalsGaugesAreDrawnOverTheirPaneAndThePointerStillReachesTheCells()
    {
        var (gaugeRect, paneRect, bars, barColour, cellsUnchanged, clientSaw) = await Run((window, view, layer, driver) =>
        {
            var before = driver.Frame;
            Publish(driver, new PaneRect(2, 2, 36, 6));
            Drain();
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame()!;
            var gauges = layer.Gauges.Values.Single();
            var cell = view.SizeFor(1, 1);
            var paneRect = new Rect(2 * cell.Width, 2 * cell.Height, 36 * cell.Width, 6 * cell.Height);
            var gaugeRect = new Rect(Canvas.GetLeft(gauges), Canvas.GetTop(gauges), gauges.Bounds.Width, gauges.Bounds.Height);
            var bars = gauges.DrawnBars.ToArray();
            var fill = bars[0].Fill;
            var inLayer = gauges.TranslatePoint(new Point(fill.X + 3, fill.Center.Y), window)!.Value;
            var barColour = Pixel(frame, inLayer.X, inLayer.Y);

            var clientSaw = new List<System.Drawing.Point>();
            driver.MouseEvent += (_, _, at) => clientSaw.Add(at);
            var press = new Point((5 * cell.Width) + 2, (4 * cell.Height) + 2);
            window.MouseDown(press, MouseButton.Left);
            window.MouseUp(press, MouseButton.Left);
            return (gaugeRect, paneRect, bars, barColour, ReferenceEquals(before, driver.Frame), clientSaw);
        });

        await Assert.That(gaugeRect).IsEqualTo(paneRect);
        await Assert.That(bars.Length).IsEqualTo(3).Because("health, mana and the target");
        await Assert.That(bars[1].Low).IsTrue();
        await Assert.That(barColour.G).IsGreaterThan(barColour.R).Because("health's bar wears the bar ink, a green");
        await Assert.That(cellsUnchanged).IsTrue().Because("the gauges draw over the frame, never into it");
        await Assert.That(clientSaw.Distinct().ToArray()).IsEquivalentTo(new[] { new System.Drawing.Point(5, 4) });
    }

    [Test]
    public async Task AShortPaneLaysTheMetersSideBySideAndATallOneStacksThem()
    {
        var (row, stacked) = await Run((window, view, layer, driver) =>
        {
            Publish(driver, new PaneRect(0, 2, 40, 1));
            Drain();
            window.UpdateLayout();
            var row = layer.Gauges.Values.Single().Stacked;
            Publish(driver, new PaneRect(0, 2, 40, 9));
            Drain();
            window.UpdateLayout();
            return (row, layer.Gauges.Values.Single().Stacked);
        }, rows: 12);

        await Assert.That(row).IsFalse();
        await Assert.That(stacked).IsTrue();
    }

    [Test]
    public async Task AGaugeGoesWhenItsPanelLeavesTheScreen()
    {
        var (shown, after) = await Run((window, view, layer, driver) =>
        {
            Publish(driver, new PaneRect(0, 2, 30, 3));
            Drain();
            var shown = layer.Gauges.Count;
            ((IPointerPresenter)driver.Pointer).Panels([]);
            Drain();
            return (shown, layer.Gauges.Count);
        });

        await Assert.That(shown).IsEqualTo(1);
        await Assert.That(after).IsEqualTo(0);
    }

    [Test]
    public async Task TheClientPublishesTheMapAroundTheCharacterAndAWalkGoesThroughItsSpeedwalk()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = InlineWindowApp(driver);
        app.RenderSnapshot("layout-combat");

        var map = driver.Pointer.Panels.SingleOrDefault(p => p.PanelId == PanelIds.Map);
        await Assert.That(map).IsNotNull();
        await Assert.That(map!.WindowId).IsEqualTo(SharpMUTermApp.MapWindowId(DemoScene.ActiveSessionKey));
        await Assert.That(map.Map!.Current).IsNotNull();
        await Assert.That(map.Map.Exits.Count).IsGreaterThan(0);

        var target = map.Map.Rooms.First(r => !r.Current);
        map.WalkTo!(target.Id);
        app.RenderNextFrame();
        var after = driver.Pointer.Panels.Single(p => p.PanelId == PanelIds.Map);
        await Assert.That(after.Map!.Walking).IsNotNull().Because("the click started the client's own speedwalk");
        await Assert.That(after.Map.Rooms.Single(r => r.Id == target.Id).Ahead).IsTrue();
        await Assert.That(ReferenceEquals(after.WalkTo, map.WalkTo)).IsTrue().Because("one walker per window, so an overlay compares equal");
    }

    [Test]
    public async Task OnTheNativeMapAClickWalksADragPansTheWheelZoomsAndARightClickReachesTheClient()
    {
        var (walked, pan, zoom, clientSaw) = await Run((window, view, layer, driver) =>
        {
            var walked = new List<long>();
            var model = new MapViewModel(
                "Town",
                0,
                [
                    new MapViewRoom(1, "Plaza", 0, 0, Current: true, Ahead: false, Locked: false),
                    new MapViewRoom(2, "North Road", 0, -1, Current: false, Ahead: false, Locked: false),
                ],
                [new MapViewExit(1, 2)],
                null);
            ((IPointerPresenter)driver.Pointer).Panels(
                [new PanelOverlay(PanelIds.Map, "map:demo", new PaneRect(0, 1, 40, 9), new PanelInk("#33ccff", "#ffaa33", "#555555"), null, model, walked.Add)]);
            Drain();
            window.UpdateLayout();
            var canvas = layer.Maps.Values.Single();
            Point At(MapViewRoom room) => canvas.TranslatePoint(canvas.CentreOf(room), window)!.Value;

            var clientSaw = new List<List<MouseFlags>>();
            driver.MouseEvent += (_, flags, _) => clientSaw.Add(flags);

            // A click on the room north of the character, then a click on the character's own room (nothing to walk).
            window.MouseDown(At(model.Rooms[1]), MouseButton.Left);
            window.MouseUp(At(model.Rooms[1]), MouseButton.Left);
            window.MouseDown(At(model.Rooms[0]), MouseButton.Left);
            window.MouseUp(At(model.Rooms[0]), MouseButton.Left);

            // A drag that starts on a room pans and walks nowhere.
            var from = At(model.Rooms[1]);
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from + new Vector(30, 12));
            window.MouseUp(from + new Vector(30, 12), MouseButton.Left);
            var pan = canvas.Pan;

            window.MouseWheel(At(model.Rooms[0]), new Vector(0, 2));
            var zoom = canvas.Zoom;

            window.MouseDown(At(model.Rooms[0]), MouseButton.Right);
            window.MouseUp(At(model.Rooms[0]), MouseButton.Right);
            return (walked, pan, zoom, clientSaw);
        });

        await Assert.That(walked).IsEquivalentTo(new long[] { 2 });
        await Assert.That(pan).IsEqualTo(new Vector(30, 12));
        await Assert.That(zoom).IsGreaterThan(1.0);
        await Assert.That(clientSaw.Count).IsEqualTo(2).Because("only the right click is handed on, pressed and released");
        await Assert.That(clientSaw[0]).Contains(MouseFlags.Button3Pressed);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    internal static SharpMUTermApp InlineWindowApp(AvaloniaConsoleDriver driver) =>
        new(DemoScene.Build(), WindowHost.Create(driver, clipboard: null) with
        {
            Capabilities = WindowOptions.Capabilities,
            Audio = null,
            Presence = null,
            Announcer = null,
            RunsInline = true,
        });

    private static void Offer(AvaloniaConsoleDriver driver, Action<Action<IReadOnlyList<PointerMenuItem>>> menu)
    {
        ((IPointerPresenter)driver.Pointer).HeaderActions([new HeaderAction(HeaderAction.LayoutsId, "Layouts", "⌥M", menu)]);
        Drain();
    }

    private static void Publish(AvaloniaConsoleDriver driver, PaneRect rect) =>
        ((IPointerPresenter)driver.Pointer).Panels(
            [new PanelOverlay(PanelIds.Vitals, "panel:vitals:demo", rect, new PanelInk("#3ccf6a", "#ff5555", "#333338"), Vitals)]);

    internal static void Drain() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    internal static Task<T> Run<T>(
        Func<Window, TerminalView, DesktopLayer, AvaloniaConsoleDriver, T> body, int columns = 40, int rows = 10) =>
        Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(columns, rows);
            var view = new TerminalView(driver, SKColors.White, Back, 16, null);
            var layer = new DesktopLayer(view);
            var root = new Panel();
            root.Children.Add(view);
            root.Children.Add(layer);
            var window = new Window { Content = root, WindowDecorations = WindowDecorations.BorderOnly };
            var size = view.SizeFor(columns, rows);
            window.Width = size.Width;
            window.Height = size.Height;
            window.Show();
            view.Focus();
            driver.Flush();
            Drain();
            try
            {
                return body(window, view, layer, driver);
            }
            finally
            {
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

    private static global::Avalonia.Media.Color Pixel(global::Avalonia.Media.Imaging.WriteableBitmap bitmap, double x, double y)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        var i = ((int)y * buffer.RowBytes) + ((int)x * 4);
        return buffer.Format == global::Avalonia.Platform.PixelFormat.Rgba8888
            ? global::Avalonia.Media.Color.FromRgb(bytes[i], bytes[i + 1], bytes[i + 2])
            : global::Avalonia.Media.Color.FromRgb(bytes[i + 2], bytes[i + 1], bytes[i]);
    }

    private static HeadlessUnitTestSession Session => TerminalViewTests.Session;
}
