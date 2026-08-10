using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The bar marking where the reader was when they left the terminal: where it is drawn, and what makes
/// it go away again.
/// <para>
/// The rule that <em>recognises</em> a return lives in <see cref="TerminalFocusWatcherTests"/>, because
/// it is a rule about time and needs no client around it. These are about what the client does with one.
/// Both halves are exercised here at least once, through the real global-shortcut registration, so the
/// seam between them is not left to inspection.
/// </para>
/// </summary>
/// <remarks>
/// Serialised for the reason every file that renders a frame is: rendering redirects the process-global
/// <c>Console.Out</c>, and the harness redirects <c>Console.In</c>.
/// </remarks>
[NotInParallel]
public class AwayDividerTests
{
    private const int Width = 120;
    private const int Height = 32;

    /// <summary>The window the demo scene's own output lands in.</summary>
    private const string Main = "main";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    [Test]
    public async Task AReturnDrawsTheBarWhereTheReaderLeft()
    {
        var (app, session, _) = Bound();
        session.PrintSystem("*** before you left");
        app.SimulateKey(Key(ConsoleKey.End)); // the reader is here; this is the boundary

        session.PrintSystem("*** while you were away");
        session.PrintSystem("*** and again");
        app.SimulateReturnFromAway(TimeSpan.FromMinutes(12));

        var index = app.AwayBarIndex(Main);
        await Assert.That(index).IsNotNull();

        var rows = app.PaneLines(Main);
        await Assert.That(rows[index!.Value]).Contains(AwayBarRenderer.Label);
        await Assert.That(rows[index.Value]).Contains("2 lines");
        await Assert.That(rows[index.Value]).Contains("12 min");

        // The boundary is where they left, so what they had already read is above it and what they
        // missed is below. A bar in the wrong place is worse than no bar.
        await Assert.That(rows[index.Value - 1]).Contains("before you left");
        await Assert.That(rows[index.Value + 1]).Contains("while you were away");
    }

    [Test]
    public async Task AWindowThatGainedNothingGetsNoBar()
    {
        var (app, session, _) = Bound();
        session.PrintSystem("*** before you left");
        app.SimulateKey(Key(ConsoleKey.End));

        app.SimulateReturnFromAway(TimeSpan.FromHours(2));

        // Nothing arrived, so there is no boundary. A bar on the newest line of a quiet pane would be
        // furniture that says only "time passed", which the reader already knew.
        await Assert.That(app.AwayBarIndex(Main)).IsNull();
        await Assert.That(app.PaneLines(Main).Any(r => r.Contains(AwayBarRenderer.Label))).IsFalse();
    }

    [Test]
    public async Task ASecondReturnReplacesTheFirstBar()
    {
        var (app, session, _) = Bound();
        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** first absence");
        app.SimulateReturnFromAway(TimeSpan.FromMinutes(5));

        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** second absence");
        app.SimulateReturnFromAway(TimeSpan.FromMinutes(9));

        // Two boundaries in one pane cannot both be where the reader left.
        var bars = app.PaneLines(Main).Count(r => r.Contains(AwayBarRenderer.Label));
        await Assert.That(bars).IsEqualTo(1);

        var index = app.AwayBarIndex(Main);
        await Assert.That(app.PaneLines(Main)[index!.Value]).Contains("9 min");
    }

    /// <summary>
    /// The third consumption conjunct. A shallow absence puts the bar on screen with everything below
    /// it, so "seen" and "at the tail" are both true in the frame it is drawn — and clearing there would
    /// remove it before the reader had looked at it.
    /// </summary>
    [Test]
    public async Task TheBarSurvivesTheFrameItIsDrawnIn()
    {
        var (app, session, _) = Bound();
        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** while you were away");
        app.SimulateReturnFromAway(TimeSpan.FromMinutes(12));
        app.RenderWholeFrame();

        await Assert.That(app.AwayBarIndex(Main)).IsNotNull();
    }

    [Test]
    public async Task TheBarGoesOnceItHasBeenSeenAndTheReaderTouchesSomething()
    {
        var (app, session, _) = Bound();
        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** while you were away");
        app.SimulateReturnFromAway(TimeSpan.FromMinutes(12));
        app.RenderWholeFrame();

        app.SimulateKey(Key(ConsoleKey.End));

        await Assert.That(app.AwayBarIndex(Main)).IsNull();
        await Assert.That(app.PaneLines(Main).Any(r => r.Contains(AwayBarRenderer.Label))).IsFalse();

        // What it marked is still there. The bar goes; the lines it pointed at do not.
        await Assert.That(app.PaneLines(Main).Any(r => r.Contains("while you were away"))).IsTrue();
    }

    /// <summary>
    /// The first consumption conjunct, and the one the obvious rule gets wrong. A bottom-anchored pane
    /// is already "caught up" the instant the reader returns, however much is above the fold — so a bar
    /// they have not scrolled up to must survive their typing.
    /// </summary>
    [Test]
    public async Task ADeepAbsenceKeepsItsBarUntilTheReaderGoesAndLooks()
    {
        var (app, session, _) = Bound();
        app.SimulateKey(Key(ConsoleKey.End));
        for (var i = 0; i < 200; i++)
        {
            session.PrintSystem($"*** while you were away {i}");
        }

        app.SimulateReturnFromAway(TimeSpan.FromHours(2));
        app.RenderWholeFrame();

        app.SimulateKey(Key(ConsoleKey.End));
        await Assert.That(app.AwayBarIndex(Main)).IsNotNull();

        // Now go and find it, then come back to the bottom: that is the gap being crossed.
        for (var i = 0; i < 40; i++)
        {
            app.SimulateKey(Key(ConsoleKey.PageUp));
        }

        app.RenderWholeFrame();
        app.SimulateKey(Key(ConsoleKey.End, ctrl: true));
        app.RenderWholeFrame();
        app.SimulateKey(Key(ConsoleKey.End));

        await Assert.That(app.AwayBarIndex(Main)).IsNull();
    }

    /// <summary>
    /// The bar is the client's own chrome, so it goes into the line buffer directly rather than through
    /// the append seam. A reader who was away and is now reading has enough to do without the badge
    /// counting the client's own furniture as something else they missed.
    /// </summary>
    [Test]
    public async Task TheBarIsNotCountedAsUnread()
    {
        var (app, session, _) = Bound();
        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** while you were away");

        var before = app.UnreadOf(Main);
        app.SimulateReturnFromAway(TimeSpan.FromMinutes(12));

        await Assert.That(app.UnreadOf(Main)).IsEqualTo(before);
    }

    /// <summary>
    /// A bar trimmed off the top of the buffer is gone, and the mark has to go with it — a mark left
    /// pointing at row zero would have the next removal take a line of the game's output instead.
    /// </summary>
    [Test]
    public async Task ABarTrimmedOffTheTopOfTheBufferIsForgotten()
    {
        var config = Quiet();
        config.ScrollbackLines = 40;
        var (app, session, _) = Bound(config);

        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** while you were away");
        app.SimulateReturnFromAway(TimeSpan.FromMinutes(12));
        await Assert.That(app.AwayBarIndex(Main)).IsNotNull();

        for (var i = 0; i < 80; i++)
        {
            session.PrintSystem($"*** and life went on {i}");
        }

        // The buffer is the assertion and the control is not: MarkupControl accumulates every line it
        // was ever appended and is only pruned by a re-feed, so its text still holds rows the buffer has
        // dropped. That is true of the game's own trimmed output as much as of this bar.
        await Assert.That(app.AwayBarIndex(Main)).IsNull();
    }

    /// <summary>
    /// The whole feature, end to end, through the Tab the terminal's focus report actually arrives as —
    /// the real global-shortcut registration, not <c>SimulateReturnFromAway</c>. This is the only test
    /// that crosses the seam between recognising a return and drawing one.
    /// </summary>
    [Test]
    public async Task ATabAfterAQuietGapDrawsTheBar()
    {
        var (app, session, time) = Bound(focusReporting: true);
        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** while you were away");

        time.Advance(TimeSpan.FromMinutes(12));
        app.SimulateKey(Key(ConsoleKey.Tab, '\t'));

        await Assert.That(app.AwayBarIndex(Main)).IsNotNull();
    }

    /// <summary>
    /// And the other half, which is the one that has to hold every single day: a Tab pressed by a reader
    /// who is sitting right there is a Tab. It must not be consumed, and it must not mark anything.
    /// <para>
    /// This also pins the harness itself. <c>SimulateKey</c> used to run a global shortcut and discard
    /// its result, swallowing the key either way — invisible while every claim returned true, and wrong
    /// the moment one declined.
    /// </para>
    /// </summary>
    [Test]
    public async Task ATabFromAReaderWhoIsSittingThereIsATab()
    {
        var (app, session, _) = Bound(focusReporting: true);
        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** a line");

        app.SimulateKey(Key(ConsoleKey.Tab, '\t'));

        await Assert.That(app.AwayBarIndex(Main)).IsNull();
    }

    [Test]
    public async Task AnAppWithNoFocusReportingClaimsNoTabAtAll()
    {
        var (app, session, time) = Bound();
        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** while you were away");

        time.Advance(TimeSpan.FromHours(3));
        app.SimulateKey(Key(ConsoleKey.Tab, '\t'));

        // Headless is not a terminal that can report focus, so nothing here is a return and Tab is
        // nobody's but the command line's.
        await Assert.That(app.AwayBarIndex(Main)).IsNull();
    }

    private static (SharpMUTermApp App, WorldSession Session, ManualTimeProvider Time) Bound(
        bool focusReporting = false) => Bound(Quiet(), focusReporting);

    /// <summary>
    /// The demo configuration with the scrollback spill off. These tests print more than a session's
    /// in-memory ring holds, and a spilling session writes segment files into the developer's own cache
    /// directory — which <c>UserDirectoryGuard</c> fails the run for, correctly.
    /// </summary>
    private static Core.Configuration.AppConfiguration Quiet()
    {
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        return config;
    }

    private static (SharpMUTermApp App, WorldSession Session, ManualTimeProvider Time) Bound(
        Core.Configuration.AppConfiguration config,
        bool focusReporting = false)
    {
        Console.SetIn(TextReader.Null);
        var time = new ManualTimeProvider();
        var app = new SharpMUTermApp(
            config,
            Headless,
            new HeadlessConsoleDriver(Width, Height),
            time,
            focusReporting: focusReporting);
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        return (app, session, time);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char character = '\0', bool ctrl = false) =>
        new(character, key, false, false, ctrl);

    private static ConsoleKeyInfo Key(ConsoleKey key, bool ctrl) => Key(key, '\0', ctrl);
}
