using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A host that draws the header or the rail itself (<see cref="ClientHost.Chrome"/>) gets no cell for it: no header
/// row, no rail column, and every count the client makes of its own rows and columns — the layout, the input-height
/// veto, the pane rectangles and so what each world is told over NAWS — is made over the cells that are left.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class HostChromeLayoutTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class ChromePresenter : IPointerPresenter
    {
        public List<ChromeOverlay> Published { get; } = [];

        public void Hover(PointerTarget target)
        {
        }

        public void ShowMenu(Point cell, IReadOnlyList<PointerMenuItem> items)
        {
        }

        public bool DrawsMenus => true;

        public void Scrollbars(IReadOnlyList<PaneScrollbar> bars)
        {
        }

        public void Chrome(ChromeOverlay chrome) => Published.Add(chrome);
    }

    private static SharpMUTermApp App(HostChrome chrome, out string frame, string? view = null, int width = Width, int height = Height, IPointerPresenter? pointer = null)
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.Headless(new HeadlessConsoleDriver(width, height), Caps) with { Chrome = chrome, Pointer = pointer };
        var app = new SharpMUTermApp(DemoScene.Build(), host);
        frame = app.RenderSnapshot(view);
        app.RenderNextFrame();
        return app;
    }

    private static async Task<(WorldSession Session, RecordingTelnetSession Telnet)> ConnectedAsync(string name)
    {
        var telnet = new RecordingTelnetSession();
        var session = new WorldSession(new WorldDefinition { Name = name, Host = "h", Port = 1 }, sessionFactory: _ => telnet);
        await session.ConnectAsync();
        return (session, telnet);
    }

    [Test]
    public async Task EveryHostTheRepositoryBuildsSaysWhichChromeItDraws()
    {
        await Assert.That(ClientHost.Headless(new HeadlessConsoleDriver(40, 10), Caps).Chrome).IsEqualTo(HostChrome.None);
        await Assert.That(ClientHost.Snapshot(40, 10, Caps).Chrome).IsEqualTo(HostChrome.None);
        await Assert.That(ClientHost.ForHarness(Caps, new HeadlessConsoleDriver(40, 10), null, null, null).Chrome).IsEqualTo(HostChrome.None);
        await Assert.That(HostChrome.None.Any).IsFalse();
        await Assert.That(HostChrome.HeaderAndRail.Any).IsTrue();
    }

    [Test]
    public async Task AHostThatDrawsTheHeaderGetsNoHeaderRowAndTheWorkspaceTakesIt()
    {
        await using var cells = App(HostChrome.None, out var cellFrame);
        await using var native = App(HostChrome.HeaderAndRail with { Rail = false }, out var nativeFrame);

        await Assert.That(cells.LaidOutRows.Header).IsEqualTo(1);
        await Assert.That(native.LaidOutRows.Header).IsEqualTo(0);
        await Assert.That(native.LaidOutRows.Workspace).IsEqualTo(cells.LaidOutRows.Workspace + 1);

        // The ribbon's brand is on the terminal's first row and on no row of the window's cells.
        await Assert.That(FrameGrid.Decode(cellFrame, Width, Height)[0]).Contains("MUTerm");
        await Assert.That(FrameGrid.Decode(nativeFrame, Width, Height).Any(row => row.Contains("MUTerm", StringComparison.Ordinal) || row.Contains("muterm", StringComparison.Ordinal))).IsFalse();

        // The bands still fill the grid exactly: nothing promised to a header that is not there.
        var rows = native.LaidOutRows;
        await Assert.That(rows.Header + rows.Workspace + rows.Primary + rows.Second + rows.Status).IsEqualTo(Height);
    }

    [Test]
    public async Task AHostThatDrawsTheRailGetsNoRailColumnAndThePanesStartAtTheEdge()
    {
        await using var cells = App(HostChrome.None, out _);
        await using var native = App(HostChrome.HeaderAndRail with { Header = false }, out var nativeFrame);

        var cellPane = cells.PaneOutputRects().Values.Single();
        var nativePane = native.PaneOutputRects().Values.Single();
        await Assert.That(cellPane.X).IsGreaterThan(0).Because("the terminal's rail, divider and spacer come first");
        await Assert.That(nativePane.X).IsEqualTo(0);
        await Assert.That(nativePane.Width).IsEqualTo(Width);
        await Assert.That(nativePane.Width).IsEqualTo(cellPane.Width + cellPane.X);
        await Assert.That(nativePane.Height).IsEqualTo(cellPane.Height);

        // No rail row in the cells: the world's heading the terminal's rail opens with is nowhere in the first columns.
        var world = DemoScene.Build().Worlds[0].Name;
        await Assert.That(FrameGrid.Decode(nativeFrame, Width, Height).Skip(1).Any(row => row.TrimStart().StartsWith(world, StringComparison.Ordinal)))
            .IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("split")]
    [Arguments("layout-combat")]
    [Arguments("layout-roleplay")]
    public async Task ThePanesTileTheWholeGridWhenTheHostDrawsBoth(string? view)
    {
        await using var app = App(HostChrome.HeaderAndRail, out _, view);
        var rects = app.PaneSnapshot().Rects.Values.ToList();
        await Assert.That(rects.Min(r => r.X)).IsEqualTo(0);
        await Assert.That(rects.Min(r => r.Y)).IsEqualTo(0);
        await Assert.That(rects.Max(r => r.X + r.Width)).IsEqualTo(Width);
        await Assert.That(app.LaidOutRows.Header).IsEqualTo(0);
    }

    [Test]
    public async Task EachWorldIsToldItsPanesRealSizeWhenTheHostDrawsTheChrome()
    {
        await using var app = App(HostChrome.HeaderAndRail, out _, "split");
        var left = await ConnectedAsync("Left");
        var right = await ConnectedAsync("Right");
        app.AttachSession(left.Session, "main");
        app.AttachSession(right.Session, DemoScene.ChatWindowId);
        app.RenderNextFrame();

        var surface = app.PaneSnapshot();
        PaneRect Output(string windowId) => app.PaneOutputRects()[surface.Rects.Keys.Single(id => surface.ActiveWindow(id) == windowId)];
        var l = Output("main");
        var r = Output(DemoScene.ChatWindowId);

        await Assert.That(left.Telnet.Sizes[^1]).IsEqualTo((l.Width, l.Height));
        await Assert.That(right.Telnet.Sizes[^1]).IsEqualTo((r.Width, r.Height));

        // The two panes and the divider between them are the whole width: no rail column was taken off either.
        await Assert.That(l.X).IsEqualTo(0);
        await Assert.That(l.Width + r.Width + 1).IsEqualTo(Width);
    }

    [Test]
    [Arguments(6)]
    [Arguments(8)]
    [Arguments(12)]
    public async Task TheInputVetoCountsNoHeaderRowAndTheBandsFitASmallGrid(int height)
    {
        await using var app = App(HostChrome.HeaderAndRail, out _, height: height, width: 80);
        var rows = app.LaidOutRows;
        await Assert.That(rows.Header).IsEqualTo(0);
        await Assert.That(rows.Header + rows.Workspace + rows.Primary + rows.Second + rows.Status).IsEqualTo(height);
        await Assert.That(rows.Status).IsGreaterThan(0).Because("the status line is never arranged off the grid");
    }

    // ── What the host is told ─────────────────────────────────────────────────────────────────────

    [Test]
    public async Task OnlyAHostThatDrawsChromeIsToldAboutIt()
    {
        var cells = new ChromePresenter();
        await using (App(HostChrome.None, out _, pointer: cells))
        {
            await Assert.That(cells.Published).IsEmpty();
        }

        var native = new ChromePresenter();
        await using var app = App(HostChrome.HeaderAndRail, out _, "characters", pointer: native);
        var chrome = native.Published.Last();
        await Assert.That(chrome.Drawn).IsEqualTo(HostChrome.HeaderAndRail);
        await Assert.That(chrome.Interactive).IsTrue();
        await Assert.That(chrome.Sidebar).IsEqualTo(app.BuildSidebar());
        await Assert.That(chrome.HeaderModel).IsEqualTo(app.BuildHeaderModel());
        await Assert.That(chrome.Palette.HeaderPlane).StartsWith("#");
        await Assert.That(chrome.Palette.RailPlane).StartsWith("#");
    }

    [Test]
    public async Task TheChromeStaysUnderAModalAndTakesNoClick()
    {
        var pointer = new ChromePresenter();

        // The connections scene leaves the quit prompt open over the main window.
        await using var app = App(HostChrome.HeaderAndRail, out _, "connections", pointer: pointer);
        var chrome = pointer.Published.Last();
        await Assert.That(chrome.Interactive).IsFalse();

        var before = app.ActiveWindowId();
        var other = chrome.Sidebar.Worlds.SelectMany(w => w.Characters).SelectMany(c => c.Windows).First(w => w.Id != before);
        await Assert.That(app.ActivateChromeTarget(SidebarModel.TargetOf(other))).IsFalse();
        await Assert.That(app.ActiveWindowId()).IsEqualTo(before);
    }

    [Test]
    public async Task TheArmedPrefixIsInTheHeaderModelWithEveryKeyThePanelLists()
    {
        var pointer = new ChromePresenter();
        await using var app = App(HostChrome.HeaderAndRail, out _, "prefix", pointer: pointer);
        var prefix = app.BuildHeaderModel().Prefix;
        await Assert.That(prefix).IsNotNull();
        await Assert.That(prefix!.Keys.Select(k => k.Keys)).IsEquivalentTo(PrefixPanel.Entries(app.PrefixFactsSnapshot).Select(e => e.Keys));
        await Assert.That(prefix.Exit).Contains("Esc");
        await Assert.That(pointer.Published.Last().HeaderModel.Prefix).IsEqualTo(prefix);

        await using var idle = App(HostChrome.HeaderAndRail, out _);
        await Assert.That(idle.BuildHeaderModel().Prefix).IsNull();
    }

    [Test]
    public async Task TheSidebarsFoldButtonFoldsTheRailAndSaysSo()
    {
        var pointer = new ChromePresenter();
        await using var app = App(HostChrome.HeaderAndRail, out _, pointer: pointer);
        var panes = app.PaneOutputRects();
        await Assert.That(pointer.Published.Last().Offers(SidebarModel.CollapseTarget)).IsTrue();

        await Assert.That(app.ActivateChromeTarget(SidebarModel.CollapseTarget)).IsTrue();
        app.RenderNextFrame();
        await Assert.That(pointer.Published.Last().Sidebar.Collapsed).IsTrue();
        await Assert.That(app.PaneOutputRects()).IsEquivalentTo(panes).Because("the cells hold no rail to fold; only the host's sidebar narrows");

        await Assert.That(app.ActivateChromeTarget(SidebarModel.CollapseTarget)).IsTrue();
        app.RenderNextFrame();
        await Assert.That(pointer.Published.Last().Sidebar.Collapsed).IsFalse();
    }

    [Test]
    public async Task AHostDrawingOnlyTheHeaderIsOfferedNoRailTarget()
    {
        var pointer = new ChromePresenter();
        await using var app = App(HostChrome.HeaderAndRail with { Rail = false }, out _, "characters", pointer: pointer);
        var chrome = pointer.Published.Last();
        var window = chrome.Sidebar.Worlds.SelectMany(w => w.Characters).SelectMany(c => c.Windows).First();
        await Assert.That(chrome.Offers(SidebarModel.TargetOf(window))).IsFalse();
        await Assert.That(chrome.Offers(SidebarModel.CollapseTarget)).IsFalse();
        await Assert.That(chrome.Offers(HeaderModel.PaletteTarget)).IsTrue();
    }
}
