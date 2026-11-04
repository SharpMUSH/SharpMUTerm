using System.Text.RegularExpressions;
using SharpConsoleUI.Drivers;
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
    [Arguments(30, 9)]
    [Arguments(20, 6)]
    [Arguments(10, 3)]
    [Arguments(7, 3)]
    [Arguments(6, 0)]
    public async Task TheLiveTailIsThirtyPercentOfThePaneAndAtLeastThreeRows(int pane, int tail)
    {
        await Assert.That(SharpMUTermApp.TailRowsFor(pane)).IsEqualTo(tail);
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
