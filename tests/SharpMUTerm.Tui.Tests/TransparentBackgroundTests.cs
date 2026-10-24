using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// F7's <c>transparent background</c>, as the frame says it. A translucent terminal makes translucent
/// exactly the cells written with the terminal's <em>default</em> background (<c>SGR 49</c>) and draws
/// every explicit colour opaque, so the property under test is which cells the frame leaves on the default
/// — read off the painted cells, where <see cref="FrameGrid.Cell.Background"/> is null.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global console.</remarks>
[NotInParallel]
public class TransparentBackgroundTests
{
    private const int Width = 120;
    private const int Height = 34;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App(AppConfiguration config)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
    }

    private static AppConfiguration Transparent()
    {
        var config = DemoScene.Build();
        config.Text.TransparentBackground = true;
        return config;
    }

    /// <summary>How many cells of a rectangle are left on the terminal's own background.</summary>
    private static int DefaultCells(string frame, PaneRect rect)
    {
        var cells = FrameGrid.Cells(frame, Width, Height);
        var count = 0;
        for (var y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (var x = rect.X; x < rect.X + rect.Width; x++)
            {
                if (cells.TryGetValue((y, x), out var cell) && cell.Background is null)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// <b>Off by default, and off paints every pane cell.</b> Whether transparency looks right is the
    /// terminal's configuration, which this client cannot see, so a client that has not asked for it keeps
    /// the theme's planes and leaves nothing to the terminal.
    /// </summary>
    [Test]
    public async Task ItIsOffByDefaultAndEveryPaneCellIsPainted()
    {
        await Assert.That(new TextSettings().TransparentBackground).IsFalse();

        var app = App(DemoScene.Build());
        var frame = app.RenderSnapshot("split");

        foreach (var (paneId, rect) in app.PaneOutputRects())
        {
            await Assert.That(DefaultCells(frame, rect)).IsEqualTo(0).Because(paneId);
        }
    }

    /// <summary>
    /// On, an untinted pane's whole output area is the terminal's own background — focused or not, since
    /// the pane you are reading is the one that is always focused, and a client with one pane would
    /// otherwise show the setting doing nothing.
    /// </summary>
    [Test]
    public async Task AnUntintedPaneIsTheTerminalsOwnBackgroundFocusedOrNot()
    {
        var app = App(Transparent());
        var frame = app.RenderSnapshot("split");
        var rects = app.PaneOutputRects();

        await Assert.That(rects.Count).IsEqualTo(2);
        foreach (var (paneId, rect) in rects)
        {
            await Assert.That(DefaultCells(frame, rect)).IsEqualTo(rect.Width * rect.Height).Because(paneId);
            await Assert.That(app.PaneSurfaceColors[paneId]).IsEqualTo(SharpConsoleUI.Color.Transparent);
        }
    }

    /// <summary>
    /// <b>A tinted pane stays painted.</b> Its colour is the only thing on the plane that says whose pane it
    /// is, and that is a fact; the untinted plane only ever said "this is a pane", which the tab strip and
    /// the hairline say as well.
    /// </summary>
    [Test]
    public async Task ATintedPaneKeepsItsColour()
    {
        var config = Transparent();
        config.Worlds[0].Characters[0].Tint = PaneTint.Slate;
        var app = App(config);

        var frame = app.RenderSnapshot();
        var pane = app.PaneIdOf("main")!;
        var plane = WorkspacePalette.Focus(WorkspacePalette.Tint(config.Theme, PaneTint.Slate));
        var colour = new SharpConsoleUI.Color(plane.R, plane.G, plane.B, 255);

        await Assert.That(app.PaneSurfaceColors[pane]).IsEqualTo(colour);
        await Assert.That(DefaultCells(frame, app.PaneOutputRects()[pane])).IsEqualTo(0);
    }

    /// <summary>
    /// The focused pane still says so. Its body has given up the lift, so the selected tab chip — painted the
    /// pane's plane, which is lifted for focus — has to keep it, or nothing on the pane would differ between
    /// focused and not.
    /// </summary>
    [Test]
    public async Task TheFocusedPanesTabChipKeepsTheFocusLift()
    {
        var config = Transparent();
        var app = App(config);

        var frame = app.RenderSnapshot("split");
        var focused = app.FocusedPaneId!;
        var rect = app.PaneOutputRects()[focused];
        var lift = WorkspacePalette.Focus(config.Theme);
        var strip = new PaneRect(rect.X, rect.Y - 1, rect.Width, 1);

        await Assert.That(FrameGrid.CellsPaintedIn(frame, strip, new SharpConsoleUI.Color(lift.R, lift.G, lift.B, 255)))
            .IsGreaterThan(0);
    }

    /// <summary>
    /// <b>Committing the setting applies it now.</b> F7 commits through <c>SaveConfiguration</c>, the funnel
    /// every settings screen uses, and the panes and the backdrop follow it without a restart — and follow it
    /// back.
    /// </summary>
    [Test]
    public async Task TurningItOnAndOffAppliesWithoutARestart()
    {
        var app = App(DemoScene.Build());
        app.RenderSnapshot();
        var pane = app.PaneIdOf("main")!;

        app.Configuration.Text.TransparentBackground = true;
        app.SaveConfiguration();
        var on = app.RenderWholeFrame();
        var rect = app.PaneOutputRects()[pane];

        await Assert.That(DefaultCells(on, rect)).IsEqualTo(rect.Width * rect.Height);

        app.Configuration.Text.TransparentBackground = false;
        app.SaveConfiguration();
        var off = app.RenderWholeFrame();

        await Assert.That(DefaultCells(off, rect)).IsEqualTo(0);
        await Assert.That(FrameGrid.Cells(off, Width, Height).Values.Any(c => c.Background is null)).IsFalse();
    }
}
