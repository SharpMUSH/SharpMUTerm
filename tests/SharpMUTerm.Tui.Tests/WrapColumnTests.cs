using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// F5 ▸ <c>wrap at</c>: a world's output wraps at a fixed column, and NAWS tells the server that column rather than the
/// pane's width. Driven headlessly through real frames and a transport that records every NAWS write.
/// </summary>
[NotInParallel]
public class WrapColumnTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static (SharpMUTermApp App, ManualTimeProvider Clock) App()
    {
        Console.SetIn(TextReader.Null);
        var clock = new ManualTimeProvider();
        return (new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(Width, Height), clock), clock);
    }

    private static async Task<(WorldSession Session, RecordingTelnetSession Telnet)> ConnectedAsync(int wrapColumn)
    {
        var telnet = new RecordingTelnetSession();
        var world = new WorldDefinition { Name = "Wrapped", Host = "h", Port = 1, WrapColumn = wrapColumn };
        var session = new WorldSession(world, sessionFactory: _ => telnet);
        await session.ConnectAsync();
        return (session, telnet);
    }

    private static int PaneWidthOf(SharpMUTermApp app, string windowId)
    {
        var surface = app.PaneSnapshot();
        var paneId = surface.Rects.Keys.Single(id => surface.ActiveWindow(id) == windowId);
        return app.PaneOutputRects()[paneId].Width;
    }

    /// <summary>A column narrower than the pane: the server is told the column, and the output is laid out at it.</summary>
    [Test]
    public async Task AColumnNarrowerThanThePaneIsReportedAndWrappedAt()
    {
        var (app, _) = App();
        app.RenderSnapshot(null);
        var (session, telnet) = await ConnectedAsync(60);
        app.AttachSession(session, "main");
        app.RenderNextFrame();
        app.RenderNextFrame();

        var pane = PaneWidthOf(app, "main");
        await Assert.That(pane).IsGreaterThan(60);
        await Assert.That(telnet.Sizes[^1].Width).IsEqualTo(60);
        await Assert.That(app.BarWidth("main")).IsEqualTo(60);
    }

    /// <summary>A column wider than the pane: the server is still told the column, and the text wraps at the pane.</summary>
    [Test]
    public async Task AColumnWiderThanThePaneIsReportedButTheTextWrapsAtThePane()
    {
        var (app, _) = App();
        app.RenderSnapshot(null);
        var (session, telnet) = await ConnectedAsync(300);
        app.AttachSession(session, "main");
        app.RenderNextFrame();
        app.RenderNextFrame();

        await Assert.That(telnet.Sizes[^1].Width).IsEqualTo(300);
        await Assert.That(app.BarWidth("main")).IsEqualTo(PaneWidthOf(app, "main"));
    }

    /// <summary>Changing the column on a connected world re-tells the server and re-lays the pane; 0 goes back to the pane.</summary>
    [Test]
    public async Task ChangingTheColumnReachesAConnectedWorldAndZeroFollowsThePane()
    {
        var (app, clock) = App();
        app.RenderSnapshot(null);
        var (session, telnet) = await ConnectedAsync(60);
        app.AttachSession(session, "main");
        app.RenderNextFrame();

        session.World.WrapColumn = 72;
        clock.Advance(SharpMUTermApp.WindowSizeReportInterval);
        app.RenderNextFrame();
        app.RenderNextFrame();
        await Assert.That(telnet.Sizes[^1].Width).IsEqualTo(72);
        await Assert.That(app.BarWidth("main")).IsEqualTo(72);

        session.World.WrapColumn = 0;
        clock.Advance(SharpMUTermApp.WindowSizeReportInterval);
        app.RenderNextFrame();
        app.RenderNextFrame();
        var pane = PaneWidthOf(app, "main");
        await Assert.That(telnet.Sizes[^1].Width).IsEqualTo(pane);
        await Assert.That(app.BarWidth("main")).IsEqualTo(pane);
    }

    /// <summary>A column moves no pane rectangle: it is a margin inside the pane, so the layout around it is unchanged.</summary>
    [Test]
    public async Task AColumnMovesNoPaneRectangle()
    {
        var (app, _) = App();
        app.RenderSnapshot(null);
        var before = app.PaneOutputRects().ToDictionary(p => p.Key, p => p.Value);

        var (session, _) = await ConnectedAsync(50);
        app.AttachSession(session, "main");
        app.RenderNextFrame();
        app.RenderNextFrame();

        await Assert.That(app.PaneOutputRects()).IsEquivalentTo(before);
    }
}
