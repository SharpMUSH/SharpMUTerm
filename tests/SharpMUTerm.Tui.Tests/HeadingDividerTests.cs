using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;
using SharpMUTerm.Tui.Panels;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A docked panel's heading band is the boundary above its pane, as a tab strip is: Roleplay's composer stacked over
/// who-here has no divider row between them, the HERE band's empty run is the divider (hover, drag, double-click), and
/// the band's text is not. The window, whose native panels lie over that row, keeps its divider row.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class HeadingDividerTests
{
    private const int Width = 120;
    private const int Height = 34;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App(HostChrome? chrome = null, string theme = "Dark")
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ThemeName = theme;
        config.Theme = ThemeLibrary.Get(theme);
        var host = ClientHost.Headless(new HeadlessConsoleDriver(Width, Height), Headless) with { Chrome = chrome ?? HostChrome.None };
        var app = new SharpMUTermApp(config, host);
        app.RenderSnapshot("layout-roleplay");
        app.RenderNextFrame();
        return app;
    }

    /// <summary>The side column's two panes, composer over who-here, by their whole rectangles.</summary>
    private static (PaneRect Composer, PaneRect Here) Column(SharpMUTermApp app)
    {
        var rects = app.PaneSnapshot().Rects.Values.Where(r => r.X > Width / 2).OrderBy(r => r.Y).ToArray();
        return (rects[0], rects[1]);
    }

    /// <summary>What the pointer finds along the top row of <paramref name="pane"/> (not the divider column beside it).</summary>
    private static IEnumerable<(Point Cell, PointerTarget Target)> Row(SharpMUTermApp app, PaneRect pane) =>
        Enumerable.Range(pane.X, pane.Width).Select(x => (new Point(x, pane.Y), app.TargetAt(new Point(x, pane.Y))));

    [Test]
    public async Task NoDividerRowIsDrawnAboveAPanelsHeadingBand()
    {
        var app = App();
        var (composer, here) = Column(app);

        await Assert.That(here.Y).IsEqualTo(composer.Y + composer.Height).Because("the HERE band is the boundary");
        var text = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
        await Assert.That(text[here.Y].Substring(here.X, here.Width)).Contains("HERE");
    }

    [Test]
    public async Task TheBandsEmptyRunIsTheDividerAndItsWordsAreNot()
    {
        var app = App();
        var (_, here) = Column(app);
        var text = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height)[here.Y];

        var divider = Row(app, here).Where(hit => hit.Target.Kind == PointerKind.Divider).ToArray();
        await Assert.That(divider).IsNotEmpty();
        await Assert.That(divider.All(hit => hit.Target.Shape == PointerShape.ResizeRows)).IsTrue();
        await Assert.That(divider[0].Target.Tooltip).Contains("above and below");
        await Assert.That(divider.All(hit => text[hit.Cell.X] == ' ')).IsTrue().Because("only blank band cells are the handle");
        await Assert.That(divider.Min(hit => hit.Cell.X)).IsGreaterThan(text.IndexOf("HERE", StringComparison.Ordinal))
            .Because("the run past the heading's words");
        await Assert.That(divider.Max(hit => hit.Cell.X)).IsEqualTo(here.X + here.Width - 1);
    }

    [Test]
    public async Task HoveringTheBandLightsOnlyItsEmptyRunAndMovesNoPane()
    {
        var app = App();
        var before = app.PaneOutputRects();
        var (_, here) = Column(app);
        var cell = Row(app, here).First(hit => hit.Target.Kind == PointerKind.Divider).Cell;
        var resting = FrameGrid.Backgrounds(app.RenderWholeFrame());

        app.SimulatePointer(cell, MouseFlags.ReportMousePosition);
        var lit = FrameGrid.Backgrounds(app.RenderWholeFrame());

        await Assert.That(app.DividerLit).IsTrue();
        var changed = lit.Keys.Where(k => lit[k] != resting.GetValueOrDefault(k)).ToArray();
        await Assert.That(changed).IsNotEmpty();
        await Assert.That(changed.All(k => k.Row == here.Y && k.Column >= app.TargetAt(cell).Area.X)).IsTrue();
        await Assert.That(app.PaneOutputRects()).IsEquivalentTo(before);

        // Lit, the run is still found (it is read off the paint, where it now wears the hover colour).
        await Assert.That(app.TargetAt(cell).Kind).IsEqualTo(PointerKind.Divider);
    }

    [Test]
    public async Task DraggingTheBandResizesTheStack()
    {
        var app = App();
        var (composer, here) = Column(app);
        var cell = Row(app, here).First(hit => hit.Target.Kind == PointerKind.Divider).Cell;

        app.SimulatePointer(cell, MouseFlags.Button1Pressed);
        app.SimulatePointer(cell with { Y = cell.Y + 3 }, MouseFlags.Button1Pressed, MouseFlags.Button1Dragged, MouseFlags.ReportMousePosition);
        app.SimulatePointer(cell with { Y = cell.Y + 3 }, MouseFlags.Button1Released);
        app.RenderNextFrame();

        var (composerAfter, hereAfter) = Column(app);
        await Assert.That(composerAfter.Height).IsEqualTo(composer.Height + 3);
        await Assert.That(hereAfter.Y).IsEqualTo(here.Y + 3).Because("the band follows the pointer");
    }

    /// <summary>
    /// The window draws who-here and the composer natively over their whole panes, heading row included, and those views
    /// take the pointer, so the band's run could not be pressed there: the window keeps its divider row.
    /// </summary>
    [Test]
    public async Task TheWindowKeepsItsDividerRowUnderTheNativePanels()
    {
        var app = App(HostChrome.Desktop);
        var (composer, here) = Column(app);

        await Assert.That(here.Y).IsEqualTo(composer.Y + composer.Height + 1);
    }

    /// <summary>The band does not wait for the roster: in a stack it is the boundary, and must not come and go.</summary>
    [Test]
    public async Task WhoHereDrawsItsBandBeforeTheGameHasSaidWhoIsThere()
    {
        var ink = new WhoHereRenderer.Ink("#00f5b7", HeadingBand.For(ThemeLibrary.Get("Dark"), "#00f5b7"));
        var lines = WhoHereRenderer.Render(WhoHereModel.Unknown, 30, 6, ink);

        await Assert.That(FrameGrid.Visible(lines[0])).Contains("HERE");
        await Assert.That(FrameGrid.Visible(lines[1])).IsEqualTo(WhoHereRenderer.UnknownText);
    }
}
