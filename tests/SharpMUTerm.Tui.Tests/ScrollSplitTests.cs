using System.Text.RegularExpressions;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Split scrollback (F7 ▸ SCROLLBACK): a pane scrolled back off its live tail keeps the newest rows in view
/// under a band, inside its own rectangle. Asserted on the painted frame, not on the state that drives it.
/// </summary>
[NotInParallel]
public class ScrollSplitTests
{
    private const string Main = "main";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App(bool split = true)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.Text.SplitScrollback = split;
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        app.RenderSnapshot("scrollback");
        return app;
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, bool ctrl = false, bool alt = false) =>
        new('\0', key, shift: false, alt: alt, control: ctrl);

    private static string Plain(string ansi) => Regex.Replace(ansi, @"\x1b\[[0-9;?]*[A-Za-z]", string.Empty);

    private static string Settled(SharpMUTermApp app)
    {
        app.RenderWholeFrame();
        return Plain(app.RenderWholeFrame());
    }

    private static string Line(int number) => $"line {number:0000}";

    /// <summary>
    /// One wheel notch, delivered the way a live client gets it: to our driver handler and to the framework's own
    /// window dispatch, both of which see every mouse frame once <c>Run()</c> has started.
    /// </summary>
    private static void Wheel(SharpMUTermApp app, HeadlessConsoleDriver driver, bool up, System.Drawing.Point at)
    {
        var flags = new List<MouseFlags> { up ? MouseFlags.WheeledUp : MouseFlags.WheeledDown };
        driver.SimulateMouseEvent(flags, at);
        app.SimulateFrameworkMouse(flags, at);
        Settled(app);
    }

    private static (SharpMUTermApp App, HeadlessConsoleDriver Driver, PaneRect Pane) WheelApp()
    {
        Console.SetIn(TextReader.Null);
        var driver = new HeadlessConsoleDriver(120, 32);
        var app = new SharpMUTermApp(DemoScene.Build(), Headless, driver);
        app.RenderSnapshot("scrollback");
        Settled(app);
        var paneId = app.PaneWindows().First(p => p.Value == Main).Key;
        return (app, driver, app.PaneOutputRects()[paneId]);
    }

    /// <summary>
    /// The reported defect: a wheel over the live tail scrolled the history by three rows and the tail by one, both
    /// at once. Anywhere in a split pane the wheel reads back through the history, and the tail stays on the
    /// newest rows.
    /// </summary>
    [Test]
    public async Task TheWheelOverTheLiveTailScrollsTheHistoryAndNotTheTail()
    {
        var (app, driver, pane) = WheelApp();
        var history = new System.Drawing.Point(pane.X + 5, pane.Y + 2);
        var tail = new System.Drawing.Point(pane.X + 5, pane.Y + pane.Height - 2);

        Wheel(app, driver, up: true, history);
        await Assert.That(app.IsScrollSplit(Main)).IsTrue();
        var opened = app.ScrollbackOf(Main)!.Value.Offset;
        var tailAt = app.ScrollTailOf(Main)!.Value;

        Wheel(app, driver, up: true, tail);
        await Assert.That(app.ScrollbackOf(Main)!.Value.Offset).IsEqualTo(opened - 3);
        await Assert.That(app.ScrollTailOf(Main)!.Value.Offset).IsEqualTo(tailAt.Offset);
        await Assert.That(app.ScrollTailOf(Main)!.Value.AutoScroll).IsTrue();

        Wheel(app, driver, up: false, tail);
        await Assert.That(app.ScrollbackOf(Main)!.Value.Offset).IsEqualTo(opened);
        await Assert.That(app.ScrollTailOf(Main)!.Value.Offset).IsEqualTo(tailAt.Offset);
    }

    /// <summary>A notch is three rows wherever the pointer is, never three plus the framework's own one.</summary>
    [Test]
    public async Task AWheelNotchOverTheHistoryMovesItThreeRows()
    {
        var (app, driver, pane) = WheelApp();
        var history = new System.Drawing.Point(pane.X + 5, pane.Y + 2);

        Wheel(app, driver, up: true, history);
        var opened = app.ScrollbackOf(Main)!.Value.Offset;
        Wheel(app, driver, up: true, history);

        await Assert.That(app.ScrollbackOf(Main)!.Value.Offset).IsEqualTo(opened - 3);
    }

    [Test]
    public async Task ScrollingBackSplitsThePaneAndKeepsTheNewestLineInView()
    {
        var app = App();
        var before = app.PaneOutputRects().ToDictionary(p => p.Key, p => p.Value);

        app.SimulateKey(Key(ConsoleKey.PageUp));
        app.SimulateKey(Key(ConsoleKey.PageUp));
        var frame = Settled(app);

        await Assert.That(app.IsScrollSplit(Main)).IsTrue();
        await Assert.That(frame).Contains("scrolled back");
        await Assert.That(frame).Contains("End returns");
        await Assert.That(frame).Contains(Line(SharpMUTermApp.ScrollbackSceneLines));

        // Inside the pane: no rectangle moved, so no server is told a new size.
        await Assert.That(app.PaneOutputRects()).IsEquivalentTo(before);
    }

    [Test]
    public async Task OutputArrivingWhileSplitLandsInTheTailAndLeavesTheHistoryWhereItWas()
    {
        var app = App();
        app.SimulateKey(Key(ConsoleKey.PageUp));
        app.SimulateKey(Key(ConsoleKey.PageUp));
        Settled(app);
        var offset = app.ScrollbackOf(Main)!.Value.Offset;

        app.LoadLongScene(Main, 3, first: SharpMUTermApp.ScrollbackSceneLines + 1);
        var frame = Settled(app);

        await Assert.That(frame).Contains(Line(SharpMUTermApp.ScrollbackSceneLines + 3));
        await Assert.That(app.ScrollbackOf(Main)!.Value.Offset).IsEqualTo(offset);
        await Assert.That(app.IsScrollSplit(Main)).IsTrue();
    }

    [Test]
    public async Task ASplitPaneIsNotScrolledBackForTheUnreadBadge()
    {
        var app = App();
        app.SimulateKey(Key(ConsoleKey.PageUp));
        Settled(app);

        await Assert.That(app.IsScrollSplit(Main)).IsTrue();
        await Assert.That(app.IsWindowScrolledBack(Main)).IsFalse();
    }

    [Test]
    public async Task ALineThatLandsInTheLiveTailIsNotMissed()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        for (var i = 1; i <= 80; i++)
        {
            session.PrintSystem($"*** line {i}");
        }

        app.RenderSnapshot();
        app.SimulateScrollKey(Key(ConsoleKey.PageUp));
        Settled(app);
        await Assert.That(app.IsScrollSplit(Main)).IsTrue();

        session.PrintSystem("*** arrived while you were reading back");
        app.SimulateScrollKey(Key(ConsoleKey.End, ctrl: true));

        await Assert.That(app.UnreadOf(Main)).IsEqualTo(0);
        await Assert.That(app.AwayBarIndex(Main)).IsNull();
    }

    [Test]
    public async Task CtrlEndReturnsAndClosesTheSplit()
    {
        var app = App();
        app.SimulateKey(Key(ConsoleKey.PageUp));
        Settled(app);

        app.SimulateKey(Key(ConsoleKey.End, ctrl: true));
        var frame = Settled(app);

        await Assert.That(app.IsScrollSplit(Main)).IsFalse();
        await Assert.That(frame).DoesNotContain("scrolled back");
        await Assert.That(app.ScrollbackOf(Main)!.Value.AutoScroll).IsTrue();
    }

    [Test]
    public async Task BareEndReturnsWhenTheCaretIsAlreadyAtTheEnd()
    {
        var app = App();
        app.SimulateKey(Key(ConsoleKey.PageUp));
        Settled(app);

        app.SimulateKey(Key(ConsoleKey.End));
        Settled(app);

        await Assert.That(app.IsScrollSplit(Main)).IsFalse();
    }

    [Test]
    public async Task ScrollingTheHistoryBackDownToItsEndClosesTheSplit()
    {
        var app = App();
        app.SimulateKey(Key(ConsoleKey.PageUp));
        Settled(app);

        for (var i = 0; i < 10 && app.IsScrollSplit(Main); i++)
        {
            app.SimulateKey(Key(ConsoleKey.PageDown));
            Settled(app);
        }

        await Assert.That(app.IsScrollSplit(Main)).IsFalse();
    }

    [Test]
    public async Task WithTheSettingOffThePaneScrollsAsAWhole()
    {
        var app = App(split: false);
        app.SimulateKey(Key(ConsoleKey.PageUp));
        var frame = Settled(app);

        await Assert.That(app.IsScrollSplit(Main)).IsFalse();
        await Assert.That(frame).DoesNotContain("scrolled back");
        await Assert.That(frame).DoesNotContain(Line(SharpMUTermApp.ScrollbackSceneLines));
    }

    [Test]
    public async Task FreezingASplitPaneHandsItToFreeze()
    {
        var app = App();
        app.SimulateKey(Key(ConsoleKey.PageUp));
        Settled(app);

        app.SimulateKey(Key(ConsoleKey.F, alt: true));
        var frame = Settled(app);

        await Assert.That(app.IsScrollSplit(Main)).IsFalse();
        await Assert.That(frame).Contains("FROZEN");
        await Assert.That(frame).DoesNotContain("scrolled back");
    }

    [Test]
    public async Task TheHistoryIsDrawnOnAPlaneOfItsOwnAndTheTailOnThePanes()
    {
        var app = App();
        var unsplit = app.RenderWholeFrame();
        app.SimulateKey(Key(ConsoleKey.PageUp));
        app.SimulateKey(Key(ConsoleKey.PageUp));
        app.RenderWholeFrame();
        var frame = app.RenderWholeFrame();

        var rows = FrameGrid.Decode(frame, 120, 32);
        var cells = FrameGrid.Cells(frame, 120, 32);
        var before = FrameGrid.Cells(unsplit, 120, 32);
        var newest = rows.ToList().FindIndex(r => r.Contains(Line(SharpMUTermApp.ScrollbackSceneLines), StringComparison.Ordinal));
        var band = rows.ToList().FindIndex(r => r.Contains("scrolled back", StringComparison.Ordinal));
        await Assert.That(band).IsGreaterThan(0);
        await Assert.That(newest).IsGreaterThan(band);

        var column = rows[newest].IndexOf("line", StringComparison.Ordinal);
        var history = cells[(band - 1, column)].Background;
        var tail = cells[(newest, column)].Background;
        await Assert.That(history).IsNotEqualTo(tail);
        await Assert.That(tail).IsEqualTo(before[(newest, column)].Background).Because("the live tail stays on the pane's own plane");
    }

    [Test]
    [Arguments(30, 1, 15)]
    [Arguments(20, 1, 10)]
    [Arguments(10, 1, 5)]
    [Arguments(7, 1, 3)]
    [Arguments(6, 1, 0)]
    [Arguments(30, 2, 14)]
    [Arguments(8, 2, 3)]
    [Arguments(7, 2, 0)]
    public async Task TheLiveTailIsHalfWhatTheBandLeavesAndAtLeastThreeRows(int pane, int band, int tail)
    {
        await Assert.That(SharpMUTermApp.TailRowsFor(pane, band)).IsEqualTo(tail);
    }

    [Test]
    [Arguments(120)]
    [Arguments(60)]
    [Arguments(30)]
    public async Task TheBandIsExactlyAsWideAsThePaneWhateverTheCount(int width)
    {
        foreach (var rows in new[] { 1, 42, 99, 4127 })
        {
            var markup = ScrollSplitBarRenderer.Bar(rows, width, "#e5c07b");
            await Assert.That(MarkupStripped(markup).Length).IsEqualTo(width);
        }
    }

    private static string MarkupStripped(string markup) =>
        Regex.Replace(markup, @"\[(?:/|dim|#[0-9a-f]{6})\]", string.Empty).Replace("[[", "[").Replace("]]", "]");
}
