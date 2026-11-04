using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Below <see cref="SharpMUTermApp.NarrowColumns"/> the rail folds itself and an empty command line is one row, so a
/// narrow terminal's room goes to the output. Pinned on the pane's width and on what the world is told over NAWS at
/// the three sizes the audit measured, on the player's own fold winning until the width next crosses the line, and on
/// the window's rail folding by the same rule through <see cref="SidebarModel.Collapsed"/>.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class NarrowLayoutTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class WindowPresenter : IPointerPresenter
    {
        public int? Columns { get; set; }

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

        public int? WindowColumns => Columns;
    }

    private static SharpMUTermApp App(int width, int height, HostChrome? chrome = null, IPointerPresenter? pointer = null, string? view = null)
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.Headless(new HeadlessConsoleDriver(width, height), Caps) with { Chrome = chrome ?? HostChrome.None, Pointer = pointer };
        var app = new SharpMUTermApp(DemoScene.Build(), host);
        app.RenderSnapshot(view);
        app.RenderNextFrame();
        return app;
    }

    private static async Task<RecordingTelnetSession> ConnectMainAsync(SharpMUTermApp app)
    {
        var telnet = new RecordingTelnetSession();
        var session = new WorldSession(new WorldDefinition { Name = "W", Host = "h", Port = 1 }, sessionFactory: _ => telnet);
        await session.ConnectAsync();
        app.AttachSession(session, SharpMUTermApp.MainWindowId);
        app.RenderNextFrame();
        return telnet;
    }

    /// <summary>
    /// At 80×24 the rail is its strip and the command line one row: the pane is 72 columns where it was 60, and the
    /// output is two rows taller. The world is told the pane it is really drawn in.
    /// </summary>
    [Test]
    public async Task At80By24TheRailFoldsAndTheCommandLineIsOneRow()
    {
        await using var app = App(80, 24);
        await Assert.That(app.RailCollapsed).IsTrue();
        await Assert.That(app.LaidOutRows.Primary).IsEqualTo(1);

        var telnet = await ConnectMainAsync(app);
        var pane = app.PaneOutputRects().Values.Single();
        await Assert.That(pane.Width).IsGreaterThanOrEqualTo(72).Because("the folded strip and its divider cost 8 columns, the open rail 20");
        await Assert.That(pane.X + pane.Width).IsEqualTo(80);
        await Assert.That(telnet.Sizes[^1]).IsEqualTo((pane.Width, pane.Height));
    }

    /// <summary>At 100×30 the layout is the full one: the open rail leaves the pane the 80 columns a server wraps at.</summary>
    [Test]
    public async Task At100By30TheRailIsOpenAndTheCommandLineKeepsItsRows()
    {
        await using var app = App(100, 30);
        await Assert.That(app.RailCollapsed).IsFalse();
        await Assert.That(app.LaidOutRows.Primary).IsEqualTo(new InputSettings().Rows);

        var telnet = await ConnectMainAsync(app);
        var pane = app.PaneOutputRects().Values.Single();
        await Assert.That(pane.Width).IsGreaterThanOrEqualTo(78).Because("100 is chosen so the open rail still leaves a server's wrap width");
        await Assert.That(telnet.Sizes[^1]).IsEqualTo((pane.Width, pane.Height));
    }

    [Test]
    public async Task At120By32NothingFolds()
    {
        await using var app = App(120, 32);
        await Assert.That(app.RailCollapsed).IsFalse();
        await Assert.That(app.LaidOutRows.Primary).IsEqualTo(new InputSettings().Rows);

        var telnet = await ConnectMainAsync(app);
        var pane = app.PaneOutputRects().Values.Single();
        await Assert.That(telnet.Sizes[^1]).IsEqualTo((pane.Width, pane.Height));
    }

    /// <summary>A short but wide terminal keeps its rail and gives the command line's spare rows to the output.</summary>
    [Test]
    public async Task AShortWideTerminalKeepsItsRailAndStartsTheCommandLineAtOneRow()
    {
        await using var app = App(140, 24);
        await Assert.That(app.RailCollapsed).IsFalse();
        await Assert.That(app.LaidOutRows.Primary).IsEqualTo(1);
    }

    /// <summary>The one-row command line still grows as it wraps, as it always has.</summary>
    [Test]
    public async Task TheOneRowCommandLineGrowsAsItWraps()
    {
        await using var app = App(80, 24);
        var rows = app.LaidOutRows;
        foreach (var ch in new string('x', 200))
        {
            app.SimulateKey(new ConsoleKeyInfo(ch, ConsoleKey.X, false, false, false));
        }

        app.RenderNextFrame();
        app.RenderNextFrame();
        await Assert.That(app.LaidOutRows.Primary).IsGreaterThan(1);
        await Assert.That(app.LaidOutRows.Workspace).IsLessThan(rows.Workspace);
    }

    /// <summary>
    /// A newline typed into the one-row bar grows it to two and both rows are on screen: the editor had scrolled to
    /// keep the caret on a row the viewport did not have yet, and the bar takes that scroll back when it grows.
    /// </summary>
    [Test]
    public async Task ANewlineInTheOneRowBarShowsBothRows()
    {
        await using var app = App(80, 24);
        app.SimulateKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));
        app.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        app.RenderNextFrame();
        var frame = app.RenderWholeFrame();

        await Assert.That(app.LaidOutRows.Primary).IsEqualTo(2);
        var rows = FrameGrid.Decode(frame, 80, 24);
        await Assert.That(rows.Count(r => r.Contains("say hello there", StringComparison.Ordinal))).IsEqualTo(1);
    }

    /// <summary>
    /// A built-in layout owning the panes re-folds when the width crosses the line: Combat's vitals card in the side
    /// column becomes a one-row strip across the top below 100 columns, and comes back when the window widens.
    /// </summary>
    [Test]
    public async Task CrossingTheLineFoldsABuiltInIntoItsNarrowArrangementAndBack()
    {
        var window = new WindowPresenter { Columns = 120 };
        await using var app = App(120, 32, HostChrome.None, window, "layout-combat");
        await Assert.That(app.PaneOutputRects()["panel:vitals"].Height).IsGreaterThan(1);

        window.Columns = 90;
        app.SyncNarrowLayout();
        app.RenderNextFrame();
        await Assert.That(app.PaneOutputRects()["panel:vitals"].Height).IsEqualTo(1);

        window.Columns = 120;
        app.SyncNarrowLayout();
        app.RenderNextFrame();
        await Assert.That(app.PaneOutputRects()["panel:vitals"].Height).IsGreaterThan(1);
    }

    /// <summary>
    /// ⌃B b (or ⌃P, or the window's fold button) wins over the automatic fold until the width crosses the line again:
    /// a resize that stays on one side changes nothing the player chose.
    /// </summary>
    [Test]
    public async Task APlayersFoldWinsUntilTheWidthCrossesTheLine()
    {
        var window = new WindowPresenter { Columns = 120 };
        await using var app = App(120, 32, HostChrome.HeaderAndRail, window);
        await Assert.That(app.RailCollapsed).IsFalse();

        // Folded by hand on a wide window, and kept through a resize that stays wide.
        await Assert.That(app.DispatchCommand("layout:toggle-rail")).IsTrue();
        window.Columns = 110;
        await Assert.That(app.SyncNarrowLayout()).IsFalse();
        await Assert.That(app.RailCollapsed).IsTrue();

        // Narrowed: already folded, so nothing to do — and opened by hand, it stays open while it stays narrow.
        window.Columns = 90;
        app.SyncNarrowLayout();
        await Assert.That(app.RailCollapsed).IsTrue();
        await Assert.That(app.DispatchCommand("layout:toggle-rail")).IsTrue();
        window.Columns = 85;
        await Assert.That(app.SyncNarrowLayout()).IsFalse();
        await Assert.That(app.RailCollapsed).IsFalse();

        // Widened past the line and narrowed again: each crossing is the automatic fold's.
        window.Columns = 120;
        app.SyncNarrowLayout();
        await Assert.That(app.RailCollapsed).IsFalse();
        app.DispatchCommand("layout:toggle-rail");
        window.Columns = 95;
        app.SyncNarrowLayout();
        await Assert.That(app.RailCollapsed).IsTrue();
        window.Columns = 125;
        await Assert.That(app.SyncNarrowLayout()).IsTrue();
        await Assert.That(app.RailCollapsed).IsFalse();
    }

    /// <summary>
    /// In a window that draws its own sidebar the fold is decided on the whole window's width, not the cells'; and the
    /// host is told through the sidebar's model, so the rail it draws is the one that folds.
    /// </summary>
    [Test]
    public async Task TheWindowsOwnSidebarFoldsByTheWindowsWidthNotTheCells()
    {
        // 90 columns of cells in a window 120 wide: the sidebar is open, and the cells are not what is measured.
        var wide = new WindowPresenter { Columns = 120 };
        await using (var app = App(90, 30, HostChrome.HeaderAndRail, wide))
        {
            await Assert.That(app.RailCollapsed).IsFalse();
            await Assert.That(wide.Published.Last().Sidebar.Collapsed).IsFalse();
        }

        // 90 columns of cells in a window 95 wide: narrow, so the window's sidebar folds and is told so.
        var narrow = new WindowPresenter { Columns = 95 };
        await using var folded = App(90, 30, HostChrome.HeaderAndRail, narrow);
        await Assert.That(folded.RailCollapsed).IsTrue();
        await Assert.That(narrow.Published.Last().Sidebar.Collapsed).IsTrue();
        await Assert.That(folded.BuildSidebar().Collapsed).IsTrue();
    }
}
