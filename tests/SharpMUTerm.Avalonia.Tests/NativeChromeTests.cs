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
/// The window's native header and sidebar (<see cref="NativeHeader"/>, <see cref="NativeSidebar"/>): drawn exactly
/// over the cells the client says hold the header and the rail, from the models those cells are drawn from; a click
/// is handed back as the model's own target and never reaches the client as a press on a cell; empty header space
/// still moves the window; and no pane moves.
/// </summary>
public class NativeChromeTests
{
    private static readonly SKColor Back = new(0x10, 0x20, 0x30);

    private static readonly TerminalColor Teal = TerminalColor.FromRgb(0x00, 0xf5, 0xb7);

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
        1, 2, true, LogFormat.None, prefix);

    private static ChromeOverlay Overlay(Action<string> activate, bool shown = true, bool collapsed = false, bool prefix = false) => new(
        shown,
        new PaneRect(0, 0, 100, 1),
        Header(prefix),
        new PaneRect(0, 1, 22, 28),
        Sidebar(collapsed),
        new ChromePalette("#9d7cff", "#00f5b7", "#00f5b7", "#ffd700", "#00f5b7"),
        activate);

    [Test]
    public async Task TheHeaderAndTheSidebarCoverExactlyTheCellsTheClientNamed()
    {
        var (header, sidebar, cell) = await Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Chrome(Overlay(_ => { }));
            Drain();
            window.UpdateLayout();
            return (Bounds(layer.Header, window), Bounds(layer.Sidebar, window), view.CellExtent);
        });

        await Assert.That(header).IsEqualTo(new Rect(0, 0, 100 * cell.Width, cell.Height));
        await Assert.That(sidebar).IsEqualTo(new Rect(0, cell.Height, 22 * cell.Width, 28 * cell.Height));
    }

    [Test]
    public async Task NothingNativeIsDrawnUnderAnotherWindowOverACollapsedRailOrOverTheArmedPrefix()
    {
        var states = await Run((window, view, layer, driver) =>
        {
            var seen = new List<(bool Header, bool Sidebar)>();
            foreach (var overlay in new[]
                     {
                         Overlay(_ => { }), Overlay(_ => { }, shown: false), Overlay(_ => { }, collapsed: true), Overlay(_ => { }, prefix: true),
                     })
            {
                ((IPointerPresenter)driver.Pointer).Chrome(overlay);
                Drain();
                seen.Add((layer.Header.IsVisible, layer.Sidebar.IsVisible));
            }

            return seen;
        });

        await Assert.That(states[0]).IsEqualTo((true, true));
        await Assert.That(states[1]).IsEqualTo((false, false));
        await Assert.That(states[2]).IsEqualTo((true, false));
        await Assert.That(states[3]).IsEqualTo((false, true));
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
            var clientSaw = new List<List<MouseFlags>>();
            driver.MouseEvent += (_, flags, _) => clientSaw.Add(flags);

            void Click(Control on, Rect area)
            {
                var centre = on.TranslatePoint(area.Center, window)!.Value;
                window.MouseMove(centre);
                window.MouseDown(centre, MouseButton.Left);
                window.MouseUp(centre, MouseButton.Left);
                Drain();
            }

            var chat = layer.Sidebar.Rows.Single(r => r.Window?.Id == "spawn:2");
            Click(layer.Sidebar, chat.Area);
            var wren = layer.Sidebar.Rows.Single(r => r.Character?.Name == "Wren");
            Click(layer.Sidebar, wren.Area);
            Click(layer.Sidebar, layer.Sidebar.Hits.Single(h => h.Target == SidebarModel.AddGameTarget).Area);
            Click(layer.Header, layer.Header.Hits.Last(h => h.Target == HeaderModel.PaletteTarget).Area);
            Click(layer.Header, layer.Header.Hits.Single(h => h.Target.StartsWith(CommandIds.CharacterPrefix, StringComparison.Ordinal)).Area);
            window.MouseWheel(window.Bounds.Center.WithX(10), new Vector(0, 1));
            Drain();
            return (activated, clientSaw, layer.Sidebar.Rows.Select(r => r.Kind).ToArray(), view.IsFocused);
        }, columns: 100, rows: 30);

        await Assert.That(activated).IsEquivalentTo(new[]
        {
            CommandIds.Window("spawn:2"), CommandIds.Character("Aetherfall.Wren"), SidebarModel.AddGameTarget,
            HeaderModel.PaletteTarget, CommandIds.Character("Aetherfall.Corvid"),
        });
        await Assert.That(clientSaw.Count).IsEqualTo(0).Because("every press, release and wheel over native chrome is the window's");
        await Assert.That(focused).IsTrue().Because("the keyboard stays with the client");
        await Assert.That(rows).IsEquivalentTo(new[] { RailRowKind.World, RailRowKind.Character, RailRowKind.Window, RailRowKind.Window, RailRowKind.Character });
    }

    [Test]
    public async Task APressOnEmptyHeaderSpaceMovesTheWindowAndNeverReachesTheClient()
    {
        var (gestures, clientSaw, activated) = await Run((window, view, layer, driver) =>
        {
            var activated = new List<string>();
            ((IPointerPresenter)driver.Pointer).Chrome(Overlay(activated.Add));
            Drain();
            window.UpdateLayout();
            var clientSaw = new List<List<MouseFlags>>();
            driver.MouseEvent += (_, flags, _) => clientSaw.Add(flags);

            // Between the brand and the search box: a gap the header leaves on purpose.
            var brand = layer.Header.Hits[0].Area;
            var gap = layer.Header.TranslatePoint(new Point(brand.Right + 8, layer.Header.Bounds.Height / 2), window)!.Value;
            window.MouseDown(gap, MouseButton.Left);
            window.MouseUp(gap, MouseButton.Left);
            Drain();
            return (view.WindowGestures, clientSaw, activated);
        }, columns: 100, rows: 30);

        await Assert.That(gestures).IsEqualTo(1);
        await Assert.That(clientSaw.Count).IsEqualTo(0);
        await Assert.That(activated.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TheLiveClientPublishesItsOwnRectanglesAndNoPaneMovesAgainstTheTerminal()
    {
        using var headless = new HeadlessConsoleDriver(120, 32);
        await using var terminal = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, WindowOptions.Capabilities));
        terminal.RenderSnapshot("connections");

        using var driver = new AvaloniaConsoleDriver(120, 32);
        await using var window = new SharpMUTermApp(DemoScene.Build(), WindowHost.Create(driver, clipboard: null) with
        {
            Capabilities = WindowOptions.Capabilities,
            Audio = null,
            Presence = null,
            Announcer = null,
            RunsInline = true,
        });
        window.RenderSnapshot("connections");
        window.RenderNextFrame();

        var chrome = driver.Pointer.Chrome;
        await Assert.That(chrome).IsNotNull();
        await Assert.That(chrome!.Header).IsEqualTo(new PaneRect(0, 0, 120, 1));
        await Assert.That(chrome.Rail.Width).IsEqualTo(window.RailColumnWidth);
        await Assert.That(chrome.Sidebar).IsEqualTo(window.BuildSidebar());
        await Assert.That(window.PaneOutputRects().OrderBy(r => r.Key).ToArray())
            .IsEquivalentTo(terminal.PaneOutputRects().OrderBy(r => r.Key).ToArray());
    }

    private static Rect Bounds(Control control, Window window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static void Drain() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static Task<T> Run<T>(
        Func<Window, TerminalView, DesktopLayer, AvaloniaConsoleDriver, T> body, int columns = 100, int rows = 30) =>
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

    private static HeadlessUnitTestSession Session => TerminalViewTests.Session;
}
