using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// F7's <c>background transparency</c>, on the two kinds of terminal it has to serve. Kitty can be told
/// a level per colour (<see cref="KittyTransparentColours"/>), so there every plane stays painted and the
/// test reads what was named to it. Anywhere else a translucent terminal makes translucent exactly the
/// cells written on its <em>default</em> background (<c>SGR 49</c>), so the property under test is which
/// cells the frame leaves on the default — read off the painted cells, where
/// <see cref="FrameGrid.Cell.Background"/> is null.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global console.</remarks>
[NotInParallel]
public class TransparentBackgroundTests
{
    private const int Width = 120;
    private const int Height = 34;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly TerminalCapabilities Kitty =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false,
            supportsTransparentColours: true);

    private static SharpMUTermApp App(AppConfiguration config, TerminalCapabilities? capabilities = null)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(config, capabilities ?? Headless, new HeadlessConsoleDriver(Width, Height));
    }

    private static AppConfiguration Transparent(int level = 40)
    {
        var config = DemoScene.Build();
        config.Text.BackgroundTransparency = level;
        return config;
    }

    private static Rgb Rgb(SharpConsoleUI.Color colour) => new(colour.R, colour.G, colour.B);

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
        await Assert.That(new TextSettings().BackgroundTransparency).IsEqualTo(0);

        var app = App(DemoScene.Build());
        var frame = app.RenderSnapshot("split");

        foreach (var (paneId, rect) in app.PaneOutputRects())
        {
            await Assert.That(DefaultCells(frame, rect)).IsEqualTo(0).Because(paneId);
        }
    }

    /// <summary>
    /// On a terminal that cannot be told a level, an untinted pane's whole output area is the terminal's own background — focused or not, since
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

        app.Configuration.Text.BackgroundTransparency = 70;
        app.SaveConfiguration();
        var on = app.RenderWholeFrame();
        var rect = app.PaneOutputRects()[pane];

        await Assert.That(DefaultCells(on, rect)).IsEqualTo(rect.Width * rect.Height);

        app.Configuration.Text.BackgroundTransparency = 0;
        app.SaveConfiguration();
        var off = app.RenderWholeFrame();

        await Assert.That(DefaultCells(off, rect)).IsEqualTo(0);
        await Assert.That(FrameGrid.Cells(off, Width, Height).Values.Any(c => c.Background is null)).IsFalse();
    }

    /// <summary>
    /// <b>On kitty nothing is handed to the default background.</b> Every plane stays painted, so the
    /// frame is the opaque one cell for cell, and the level is carried by naming the planes instead.
    /// </summary>
    [Test]
    public async Task OnKittyEveryCellStaysPaintedAndThePlanesAreNamed()
    {
        var app = App(Transparent(), Kitty);
        var frame = app.RenderSnapshot("split");

        await Assert.That(FrameGrid.Cells(frame, Width, Height).Values.Any(c => c.Background is null)).IsFalse();
        await Assert.That(app.TransparentColours.Count).IsGreaterThan(0);
        await Assert.That(app.LastColourControl).IsEqualTo(KittyTransparentColours.Sequence(app.TransparentColours, 40));
    }

    /// <summary>
    /// <b>The reported defect: the other planes were left opaque.</b> On kitty the named set covers both
    /// panes' bodies (one of them tinted), the backdrop, the command line, the header — every
    /// background a frame of this scene is mostly made of.
    /// </summary>
    [Test]
    public async Task OnKittyTheHeaderTheCommandLineAndATintedPaneAreTransparentToo()
    {
        var config = Transparent();
        config.Worlds[0].Characters[0].Tint = PaneTint.Slate;
        var app = App(config, Kitty);
        var frame = app.RenderSnapshot("split");

        var named = app.TransparentColours;
        foreach (var (paneId, colour) in app.PaneSurfaceColors)
        {
            await Assert.That(named).Contains(Rgb(colour)).Because(paneId);
        }

        await Assert.That(named).Contains(WorkspacePalette.Backdrop(config.Theme));
        await Assert.That(named).Contains(config.Theme.StatusBackground);

        // And they are what the frame is made of: count the cells painted in a named colour.
        var cells = FrameGrid.Cells(frame, Width, Height).Values;
        var covered = cells.Count(c => c.Background is { } bg && named.Contains(bg));
        await Assert.That(covered).IsGreaterThan(cells.Count * 9 / 10);
    }

    /// <summary>Kitty holds seven colours, and the client never names an eighth.</summary>
    [Test]
    public async Task NoMoreThanKittysSevenSlotsAreUsed()
    {
        var config = Transparent();
        config.Worlds[0].Characters[0].Tint = PaneTint.Slate;
        var app = App(config, Kitty);
        app.RenderSnapshot("tint-tabs");

        await Assert.That(app.TransparentColours.Count).IsLessThanOrEqualTo(KittyTransparentColours.Slots);
        await Assert.That(app.TransparentColours.Distinct().Count()).IsEqualTo(app.TransparentColours.Count);
    }

    /// <summary>
    /// <b>The level reaches kitty when it is committed, and zero hands the slots back.</b> A frame that
    /// changed nothing writes nothing, so the escape is not repeated sixty times a second.
    /// </summary>
    [Test]
    public async Task ChangingTheLevelOnKittyRenamesThePlanesAndZeroResetsThem()
    {
        var app = App(Transparent(25), Kitty);
        app.RenderSnapshot();
        var first = app.LastColourControl;
        await Assert.That(first).Contains("@0.75");

        app.RenderWholeFrame();
        await Assert.That(ReferenceEquals(first, app.LastColourControl)).IsTrue();

        app.Configuration.Text.BackgroundTransparency = 60;
        app.SaveConfiguration();
        app.RenderWholeFrame();
        await Assert.That(app.LastColourControl).Contains("@0.4");

        app.Configuration.Text.BackgroundTransparency = 0;
        app.SaveConfiguration();
        app.RenderWholeFrame();
        await Assert.That(app.LastColourControl).IsEqualTo(KittyTransparentColours.Reset);
        await Assert.That(app.TransparentColours.Count).IsEqualTo(0);
    }

    /// <summary>Off, nothing is ever written to kitty — not even a reset.</summary>
    [Test]
    public async Task OffOnKittyWritesNoColourControl()
    {
        var app = App(DemoScene.Build(), Kitty);
        app.RenderSnapshot("split");

        await Assert.That(app.LastColourControl).IsNull();
    }

    /// <summary>The escape kitty's color-control documentation spells, slot for slot.</summary>
    [Test]
    public async Task TheSequenceSetsTheNamedSlotsAndResetsTheRest()
    {
        var sequence = KittyTransparentColours.Sequence([new Rgb(0x12, 0x12, 0x14), new Rgb(0x26, 0x26, 0x2b)], 35);

        await Assert.That(sequence).IsEqualTo(
            "\u001b]21;transparent_background_color1=#121214@0.65;transparent_background_color2=#26262b@0.65"
            + ";transparent_background_color3;transparent_background_color4;transparent_background_color5"
            + ";transparent_background_color6;transparent_background_color7\u001b\\");
        await Assert.That(KittyTransparentColours.Sequence([new Rgb(0, 0, 0)], 100)).Contains("#000000@0;");
    }
}
