using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// ⌃N, and the three places it is now readable from. The chord itself is not new — it has cycled the
/// focused pane's tabs for as long as panes have held more than one window — but it was named on F4 and
/// nowhere else, which is the state ⌃L's newline sat in until it was reported missing.
/// <para>
/// The refusal is the part that is genuinely new behaviour. Advertising a key on a surface obliges that
/// key to do something or say why not: every directional pane entry beside it refuses out loud, and this
/// one returned silently on a pane holding one tab.
/// </para>
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class TabCycleTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo CtrlN =>
        new('\0', ConsoleKey.N, shift: false, alt: false, control: true);

    private static SharpMUTermApp Demo()
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(Width, Height));
    }

    /// <summary>
    /// A fresh client: one pane holding one window. Not the demo scene, whose main pane already carries
    /// the Chat capture as a second tab — which is the whole reason the chord had somewhere to go in every
    /// frame anyone had looked at, and the silent refusal went unnoticed.
    /// </summary>
    private static SharpMUTermApp OneTab()
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(
            new SharpMUTerm.Core.Configuration.AppConfiguration(),
            Headless,
            new HeadlessConsoleDriver(Width, Height));
    }

    /// <summary>
    /// An app whose clock a test can move past <see cref="SharpMUTermApp.NoticeDuration"/>. The scenes
    /// that put two tabs in front get there by switching character, and that raises a notice which sits
    /// <em>over</em> the resting row — so a test reading the row's own content has to let it retire
    /// rather than assert against the message that displaced it.
    /// </summary>
    private static (SharpMUTermApp App, ManualTimeProvider Clock) TimedDemo(int width = Width, int height = Height)
    {
        Console.SetIn(TextReader.Null);
        var clock = new ManualTimeProvider();
        return (new SharpMUTermApp(
            DemoScene.Build(), Headless, new HeadlessConsoleDriver(width, height), time: clock), clock);
    }

    /// <summary>
    /// <c>tint-tabs</c> is the one view where the focused pane holds two windows as tabs — every other
    /// scene with two tabs puts them in a pane that does not hold the focus, and the cycle acts on the
    /// focused one.
    /// </summary>
    private static SharpMUTermApp TwoTabsInFront()
    {
        var app = Demo();
        app.RenderSnapshot("tint-tabs");
        return app;
    }

    [Test]
    public async Task CtrlNMovesToTheNextTabOfTheFocusedPane()
    {
        var app = TwoTabsInFront();
        var before = app.ActiveWindowId();

        app.SimulateKey(CtrlN);

        await Assert.That(app.ActiveWindowId()).IsNotEqualTo(before);
    }

    /// <summary>
    /// Wrapping, which is what makes one key enough: pressed round the strip it comes back rather than
    /// stopping at the end. It is also why there is no backward chord to look for. The count is
    /// discovered rather than written down — the demo pane holds however many windows the scene left in
    /// it, and a literal here would be a test asserting on the fixture instead of on the cycle.
    /// </summary>
    [Test]
    public async Task TheCycleVisitsEveryTabAndWrapsBackToWhereItStarted()
    {
        var app = TwoTabsInFront();
        var first = app.ActiveWindowId();

        var visited = new List<string> { first };
        for (var press = 0; press < 10; press++)
        {
            app.SimulateKey(CtrlN);
            if (app.ActiveWindowId() == first)
            {
                break;
            }

            visited.Add(app.ActiveWindowId());
        }

        await Assert.That(visited.Distinct().Count()).IsEqualTo(visited.Count).Because("no tab is visited twice");
        await Assert.That(visited.Count).IsGreaterThan(1);
        await Assert.That(app.ActiveWindowId()).IsEqualTo(first);
    }

    /// <summary>
    /// The new behaviour. A pane holding one tab has nowhere to cycle to, and the chord said nothing at
    /// all — which is exactly what a key that is broken looks like, and is not something the ⌃P surface
    /// may list without an answer.
    /// </summary>
    [Test]
    public async Task CtrlNOnAPaneHoldingOneTabRefusesOutLoud()
    {
        var app = OneTab();
        app.RenderSnapshot();

        app.SimulateKey(CtrlN);

        await Assert.That(app.StatusMarkup).Contains("this pane has one tab");
    }

    /// <summary>The ⌃P entry and the chord are one action, so they must leave the same tab in front.</summary>
    [Test]
    public async Task TheCommandSurfaceEntryDoesWhatTheChordDoes()
    {
        var viaKey = TwoTabsInFront();
        viaKey.SimulateKey(CtrlN);

        var viaEntry = TwoTabsInFront();
        await Assert.That(viaEntry.DispatchCommand("layout:next-tab")).IsTrue();

        await Assert.That(viaEntry.ActiveWindowId()).IsEqualTo(viaKey.ActiveWindowId());
    }

    /// <summary>
    /// The status row names the chord exactly while the focused pane has somewhere to cycle to — the same
    /// contextual rule the pane and second-bar hints beside it follow, and the reason a fresh client's row
    /// is not carrying a key that would only refuse.
    /// </summary>
    [Test]
    public async Task TheStatusRowNamesTheChordOnlyWhileThereAreTabsToCycle()
    {
        var (tabs, clock) = TimedDemo();
        tabs.RenderSnapshot("tint-tabs");
        clock.Advance(SharpMUTermApp.NoticeDuration); // the character switch's notice retires off the row
        await Assert.That(tabs.StatusMarkup).Contains("⌃N tab");

        var solo = OneTab();
        solo.RenderSnapshot();
        await Assert.That(solo.StatusMarkup).DoesNotContain("⌃N tab");
    }

    /// <summary>
    /// The hint is a segment of a <em>sticky</em> row, and a row that overflows wraps and costs every pane
    /// a line of output — which per-pane NAWS then re-announces to every connected server. The tab segment
    /// must therefore give way on a narrow terminal like the resize hint does, and the pane hint must
    /// survive both of them going.
    /// </summary>
    [Test]
    public async Task ANarrowTerminalDropsTheTabHintBeforeTheOneThatSaysWhereYouAre()
    {
        var narrow = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(76, 30));
        narrow.RenderSnapshot("split");

        await Assert.That(narrow.StatusMarkup).Contains("⌃←→↑↓ pane");
        foreach (var row in FrameGrid.Decode(narrow.RenderSnapshot("split"), 76, 30))
        {
            await Assert.That(row.TrimEnd().Length).IsLessThanOrEqualTo(76);
        }
    }
}
