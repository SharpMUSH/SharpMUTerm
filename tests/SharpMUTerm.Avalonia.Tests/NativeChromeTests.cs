using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window's native header and sidebar (<see cref="NativeHeader"/>, <see cref="NativeSidebar"/>): drawn beside the
/// cells at a desktop app's size, with no cell under either — the view is moved out of their way, so the grid the
/// driver holds is what is left of the window; a click is handed back as the model's own target and never reaches the
/// client as a press on a cell; a press on the cells still lands on the cell under the pointer; empty header space
/// still moves the window; and no pane is laid out differently from a terminal told the same chrome.
/// </summary>
public class NativeChromeTests
{
    private static readonly SKColor Back = new(0x10, 0x20, 0x30);

    private static readonly TerminalColor Teal = TerminalColor.FromRgb(0x00, 0xf5, 0xb7);

    private const int Columns = 100, Rows = 30;

    private static SidebarModel Sidebar(bool collapsed = false) => new(
        [
            new RailWorld("Aetherfall", "aether.example", 4000, Teal,
            [
                new RailCharacter("Corvid", "Aetherfall.Corvid", Connected: true, Active: true, Unread: 3,
                [
                    new RailWindow("main", "session:1", "⌥1", 0, true, false),
                    new RailWindow("Chat", "spawn:2", "⌥2", 3, false, false),
                ]),
                new RailCharacter("Wren", "Aetherfall.Wren", Connected: false, Active: false, Unread: 0, [], Chord: "⌥J"),
            ]),
        ],
        "session:1",
        collapsed);

    private static HeaderModel Header(bool prefix = false) => new(
        false,
        new HeaderWorld("Aetherfall", Teal),
        new HeaderCharacter("Corvid", "Aetherfall.Corvid", CharacterPresence.Online, "aether.example", 4000, true, CommandIds.Character("Aetherfall.Corvid")),
        1, 2, true, LogFormat.None, prefix)
    {
        Prefix = prefix
            ? new HeaderPrefix([new HeaderPrefixKey("|", "split this pane side by side", null), new HeaderPrefixKey("z", "zoom", "needs a second pane")], "Esc cancels")
            : null,
    };

    private static ChromeOverlay Overlay(Action<string> activate, bool interactive = true, bool collapsed = false, bool prefix = false) => new(
        HostChrome.HeaderAndRail,
        interactive,
        Header(prefix),
        Sidebar(collapsed),
        new ChromePalette("#9d7cff", "#00f5b7", "#00f5b7", "#ffd700", "#00f5b7", "#ffaf00", "#202028", "#18181d", "#e0e0e0"),
        activate);

    [Test]
    public async Task TheHeaderAndTheSidebarSitBesideTheCellsAndNoCellIsUnderThem()
    {
        var (header, sidebar, view, grid, cell, windowSize) = await Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Chrome(Overlay(_ => { }));
            Drain();
            window.UpdateLayout();
            Drain();
            return (Bounds(layer.Header, window), Bounds(layer.Sidebar, window), Bounds(view, window),
                (driver.ScreenSize.Width, driver.ScreenSize.Height), view.CellExtent, window.ClientSize);
        });

        await Assert.That(header).IsEqualTo(new Rect(0, 0, windowSize.Width, NativeLayout.HeaderHeight));
        await Assert.That(sidebar).IsEqualTo(new Rect(0, NativeLayout.HeaderHeight, NativeLayout.SidebarWidth + NativeLayout.SidebarGutter, windowSize.Height - NativeLayout.HeaderHeight));
        await Assert.That(view.Position).IsEqualTo(new Point(NativeLayout.SidebarWidth + NativeLayout.SidebarGutter, NativeLayout.HeaderHeight));
        await Assert.That(header.Height).IsGreaterThan(cell.Height * 1.5).Because("a desktop header, not a squeezed cell row");

        // The grid is the view's, and the view is what the chrome leaves: the client's cells start past both.
        await Assert.That(grid).IsEqualTo(((int)(view.Width / cell.Width), (int)(view.Height / cell.Height)));
        await Assert.That(grid.Width).IsEqualTo(Columns).Because("the window was sized to hold the requested grid beside its chrome");
        await Assert.That(grid.Height).IsEqualTo(Rows);
    }

    [Test]
    public async Task AFoldedRailIsASlimStripAndTheGridWidensIntoTheRoomItGaveBack()
    {
        var (open, folded, openColumns, foldedColumns, rows, openWindow, foldedWindow) = await Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Chrome(Overlay(_ => { }));
            Drain();
            window.UpdateLayout();
            Drain();
            var open = layer.Sidebar.Bounds.Width;
            var openColumns = driver.ScreenSize.Width;
            var openWindow = ((IPointerPresenter)driver.Pointer).WindowColumns;
            ((IPointerPresenter)driver.Pointer).Chrome(Overlay(_ => { }, collapsed: true));
            Drain();
            window.UpdateLayout();
            Drain();
            return (open, layer.Sidebar.Bounds.Width, openColumns, driver.ScreenSize.Width, layer.Sidebar.Rows.Select(r => r.Kind).ToArray(),
                openWindow, ((IPointerPresenter)driver.Pointer).WindowColumns);
        });

        // The client's narrow layout reads the window's whole width, which the fold does not change: measured on the
        // cells, the fold that widened them would read as the window growing past the line and undo itself.
        await Assert.That(openWindow).IsNotNull();
        await Assert.That(openWindow!.Value).IsGreaterThan(openColumns);
        await Assert.That(foldedWindow).IsEqualTo(openWindow);

        await Assert.That(open).IsEqualTo(NativeLayout.SidebarWidth + NativeLayout.SidebarGutter);
        await Assert.That(folded).IsEqualTo(NativeLayout.SidebarSlim + NativeLayout.SidebarGutter);
        await Assert.That(foldedColumns).IsGreaterThan(openColumns);
        await Assert.That(rows).IsEquivalentTo(new[] { RailRowKind.World, RailRowKind.Character, RailRowKind.Character })
            .Because("folded, a world is its mark and each character its disc; windows are the open sidebar's");
    }

    [Test]
    public async Task UnderAModalTheChromeStaysAndTakesNoClickAndTheArmedPrefixIsInTheHeader()
    {
        var (visible, activated, prefixKeys) = await Run((window, view, layer, driver) =>
        {
            var activated = new List<string>();
            ((IPointerPresenter)driver.Pointer).Chrome(Overlay(activated.Add, interactive: false));
            Drain();
            window.UpdateLayout();
            Drain();
            var visible = (layer.Header.IsVisible, layer.Sidebar.IsVisible, layer.Header.Inert);
            Click(window, layer.Sidebar, layer.Sidebar.Rows.Single(r => r.Character?.Name == "Wren").Area);

            ((IPointerPresenter)driver.Pointer).Chrome(Overlay(activated.Add, prefix: true));
            Drain();
            window.UpdateLayout();
            Drain();
            return (visible, activated, layer.Header.PrefixKeys.Select(k => (k.Key.Keys, k.Key.Available)).ToArray());
        });

        await Assert.That(visible).IsEqualTo((true, true, true));
        await Assert.That(activated).IsEmpty().Because("a modal owns the screen; the chrome beside it is inert");
        await Assert.That(prefixKeys).IsEquivalentTo(new[] { ("|", true), ("z", false) });
    }

    [Test]
    public async Task AClickOnTheSidebarOrTheHeaderHandsBackTheModelsTargetAndNeverReachesTheClient()
    {
        var (activated, clientSaw, rows, focused) = await Run((window, view, layer, driver) =>
        {
            var activated = new List<string>();
            ((IPointerPresenter)driver.Pointer).Chrome(Overlay(activated.Add));
            Drain();
            window.UpdateLayout();
            Drain();
            var clientSaw = new List<List<MouseFlags>>();
            driver.MouseEvent += (_, flags, _) => clientSaw.Add(flags);

            var chat = layer.Sidebar.Rows.Single(r => r.Window?.Id == "spawn:2");
            Click(window, layer.Sidebar, chat.Area);
            var wren = layer.Sidebar.Rows.Single(r => r.Character?.Name == "Wren");
            Click(window, layer.Sidebar, wren.Area);
            Click(window, layer.Sidebar, layer.Sidebar.Hits.Single(h => h.Target == SidebarModel.AddGameTarget).Area);
            Click(window, layer.Sidebar, layer.Sidebar.Hits.Single(h => h.Target == SidebarModel.CollapseTarget).Area);
            Click(window, layer.Header, layer.Header.Hits.Last(h => h.Target == HeaderModel.PaletteTarget).Area);
            Click(window, layer.Header, layer.Header.Hits.Single(h => h.Target.StartsWith(CommandIds.CharacterPrefix, StringComparison.Ordinal)).Area);
            window.MouseWheel(new Point(40, window.ClientSize.Height / 2), new Vector(0, 1));
            Drain();
            return (activated, clientSaw, layer.Sidebar.Rows.Select(r => r.Kind).ToArray(), view.IsFocused);
        });

        await Assert.That(activated).IsEquivalentTo(new[]
        {
            CommandIds.Window("spawn:2"), CommandIds.Character("Aetherfall.Wren"), SidebarModel.AddGameTarget,
            SidebarModel.CollapseTarget, HeaderModel.PaletteTarget, CommandIds.Character("Aetherfall.Corvid"),
        });
        await Assert.That(clientSaw.Count).IsEqualTo(0).Because("every press, release and wheel over native chrome is the window's");
        await Assert.That(focused).IsTrue().Because("the keyboard stays with the client");
        await Assert.That(rows).IsEquivalentTo(new[] { RailRowKind.World, RailRowKind.Character, RailRowKind.Window, RailRowKind.Window, RailRowKind.Character });
    }

    [Test]
    public async Task APressOnTheCellsLandsOnTheCellUnderThePointerPastTheChrome()
    {
        var landed = await Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Chrome(Overlay(_ => { }));
            Drain();
            window.UpdateLayout();
            Drain();
            var cells = new List<System.Drawing.Point>();
            driver.MouseEvent += (_, _, point) => cells.Add(point);

            // The middle of cell (5, 3), in window coordinates: past the sidebar and under the header.
            var cell = view.CellExtent;
            var at = view.TranslatePoint(new Point((5.5 * cell.Width), (3.5 * cell.Height)), window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            Drain();
            return cells;
        });

        await Assert.That(landed.Count).IsGreaterThan(0);
        await Assert.That(landed.Distinct()).IsEquivalentTo(new[] { new System.Drawing.Point(5, 3) });
    }

    [Test]
    public async Task APressOnEmptyHeaderSpaceMovesTheWindowAndNeverReachesTheClient()
    {
        var (gestures, clientSaw, activated, cellRowMoves) = await Run((window, view, layer, driver) =>
        {
            var activated = new List<string>();
            ((IPointerPresenter)driver.Pointer).Chrome(Overlay(activated.Add));
            Drain();
            window.UpdateLayout();
            Drain();
            var clientSaw = new List<List<MouseFlags>>();
            driver.MouseEvent += (_, flags, _) => clientSaw.Add(flags);

            // Between the brand and the search box: a gap the header leaves on purpose.
            var brand = layer.Header.Hits[0].Area;
            var gap = layer.Header.TranslatePoint(new Point(brand.Right + 10, layer.Header.Bounds.Height / 2), window)!.Value;
            window.MouseDown(gap, MouseButton.Left);
            window.MouseUp(gap, MouseButton.Left);
            Drain();
            var gestures = view.WindowGestures;
            var fromHeader = clientSaw.Count;

            // The cells' first row is a pane's tab strip here, never a handle: a blank cell there is the client's.
            var blank = view.TranslatePoint(new Point(view.Bounds.Width - 40, view.CellExtent.Height / 2), window)!.Value;
            window.MouseDown(blank, MouseButton.Left);
            window.MouseUp(blank, MouseButton.Left);
            Drain();
            return (gestures, (fromHeader, clientSaw.Count), activated, view.WindowGestures - gestures);
        });

        await Assert.That(gestures).IsEqualTo(1);
        await Assert.That(clientSaw.Item1).IsEqualTo(0).Because("a press on the header is the window's");
        await Assert.That(clientSaw.Item2).IsGreaterThan(0).Because("a press on the cells' first row is the client's");
        await Assert.That(activated.Count).IsEqualTo(0);
        await Assert.That(cellRowMoves).IsEqualTo(0);
    }

    [Test]
    [Arguments("connections")]
    [Arguments("layout-roleplay")]
    [Arguments("layout-combat")]
    public async Task TheLiveClientDrawsNoChromeCellAndLaysThePanesOutAsATerminalToldTheSame(string view)
    {
        using var headless = new HeadlessConsoleDriver(120, 32);
        await using var terminal = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, WindowOptions.Capabilities) with { Chrome = HostChrome.HeaderAndRail });
        terminal.RenderSnapshot(view);

        using var driver = new AvaloniaConsoleDriver(120, 32);
        await using var window = new SharpMUTermApp(DemoScene.Build(), WindowHost.Create(driver, clipboard: null) with
        {
            Capabilities = WindowOptions.Capabilities,
            Audio = null,
            Presence = null,
            Announcer = null,
            RunsInline = true,
        });
        window.RenderSnapshot(view);
        window.RenderNextFrame();

        var chrome = driver.Pointer.Chrome;
        await Assert.That(chrome).IsNotNull();
        await Assert.That(chrome!.Drawn).IsEqualTo(HostChrome.HeaderAndRail);
        await Assert.That(chrome.Sidebar).IsEqualTo(window.BuildSidebar());
        await Assert.That(window.LaidOutRows.Header).IsEqualTo(0);
        await Assert.That(Enumerable.Range(0, 32).Any(y => driver.Frame.RowText(y).Contains("muterm", StringComparison.Ordinal))).IsFalse()
            .Because("the cell header is not drawn when the window draws its own");
        var rects = window.PaneOutputRects();
        await Assert.That(rects.Values.Min(r => r.X)).IsEqualTo(0).Because("no rail column comes before the panes");
        await Assert.That(rects.OrderBy(r => r.Key).ToArray()).IsEquivalentTo(terminal.PaneOutputRects().OrderBy(r => r.Key).ToArray());
    }

    private static void Click(Window window, Control on, Rect area)
    {
        var centre = on.TranslatePoint(area.Center, window)!.Value;
        window.MouseMove(centre);
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        Drain();
    }

    private static Rect Bounds(Control control, Window window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static void Drain() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static Task<T> Run<T>(Func<Window, TerminalView, DesktopLayer, AvaloniaConsoleDriver, T> body) =>
        Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(Columns, Rows);
            var view = new TerminalView(driver, SKColors.White, Back, 16, null);
            var layer = new DesktopLayer(view, HostChrome.HeaderAndRail);
            var root = new Panel();
            root.Children.Add(view);
            root.Children.Add(layer);
            var window = new Window { Content = root, WindowDecorations = WindowDecorations.BorderOnly };
            var cells = view.SizeFor(Columns, Rows);
            window.Width = cells.Width + NativeLayout.SidebarWidth + NativeLayout.SidebarGutter;
            window.Height = cells.Height + NativeLayout.HeaderHeight;
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

    private static HeadlessUnitTestSession Session => TerminalViewTests.Session;
}
