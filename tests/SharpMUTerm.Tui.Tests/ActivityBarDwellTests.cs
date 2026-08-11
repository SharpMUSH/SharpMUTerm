using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The bar does not retire the instant it has been read past. Two conditions were not enough: on a
/// shallow absence the pane is already at its live tail, so the next keystroke took the bar away a
/// second or two after it appeared. The third is a floor in <em>time</em>, because that is the unit the
/// complaint was in — a raised input count would be an hour on a quiet character and three seconds on a
/// busy one.
/// </summary>
/// <remarks>
/// Serialised for the reason every file that renders a frame is: rendering redirects the process-global
/// <c>Console.Out</c>, and the harness redirects <c>Console.In</c>.
/// </remarks>
[NotInParallel]
public class ActivityBarDwellTests
{
    private const int Width = 120;
    private const int Height = 32;
    private const string Main = "main";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static (SharpMUTermApp App, WorldSession Session, ManualTimeProvider Time) Bound(int seconds)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        config.Text.ActivityBarSeconds = seconds;

        var time = new ManualTimeProvider();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height), time);
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        app.RenderSnapshot();
        return (app, session, time);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    /// <summary>
    /// Leaves a bar in the main window that has been read past on both of the original counts: the pane
    /// is at its live tail (a shallow absence never takes it off) and an input has landed since.
    /// </summary>
    private static void DrawAndRead(SharpMUTermApp app, WorldSession session)
    {
        session.PrintSystem("*** before you left");
        app.SimulateKey(Key(ConsoleKey.End));
        session.PrintSystem("*** while you were away");
        app.SimulateReturnFromAway(TimeSpan.FromMinutes(2));
        app.SimulateKey(Key(ConsoleKey.End));
    }

    [Test]
    public async Task TheBarSurvivesBeingReadPastUntilTheFloorHasElapsed()
    {
        var (app, session, time) = Bound(30);
        DrawAndRead(app, session);

        await Assert.That(app.AwayBarIndex(Main)).IsNotNull();

        time.Advance(TimeSpan.FromSeconds(29));
        app.SimulateKey(Key(ConsoleKey.End));
        await Assert.That(app.AwayBarIndex(Main)).IsNotNull();

        time.Advance(TimeSpan.FromSeconds(2));
        app.SimulateKey(Key(ConsoleKey.End));
        await Assert.That(app.AwayBarIndex(Main)).IsNull();
    }

    /// <summary>
    /// Zero is a real answer, and it is exactly the behaviour the floor replaced — so a reader who
    /// disagrees with the default can have the old client back rather than a compromise.
    /// </summary>
    [Test]
    public async Task AFloorOfZeroRetiresTheBarAsSoonAsItHasBeenReadPast()
    {
        var (app, session, _) = Bound(0);
        DrawAndRead(app, session);

        await Assert.That(app.AwayBarIndex(Main)).IsNull();
    }

    /// <summary>
    /// The floor is a floor and not a clock the bar goes by on its own: with the other two conditions
    /// unmet — here, the pane taken off its live tail — waiting does not retire it.
    /// </summary>
    [Test]
    public async Task TimePassingIsNotEnoughOnItsOwn()
    {
        var (app, session, time) = Bound(30);

        // Enough output to have a scrollback at all. Without it PageUp clamps at offset zero, which *is*
        // the bottom, so auto-scroll re-attaches and the pane never leaves its live tail — the test would
        // then be asserting the opposite of what it says.
        for (var i = 1; i <= 80; i++)
        {
            session.PrintSystem($"*** line {i}");
        }

        app.RenderSnapshot();
        DrawAndRead(app, session);

        app.SimulateScrollKey(Key(ConsoleKey.PageUp));
        time.Advance(TimeSpan.FromMinutes(5));
        app.SimulateKey(Key(ConsoleKey.End));

        await Assert.That(app.AwayBarIndex(Main)).IsNotNull();
    }
}
