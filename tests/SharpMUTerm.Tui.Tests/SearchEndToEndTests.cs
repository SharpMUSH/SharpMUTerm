using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The ⌃F surface driven through the chord the app actually registers. <see cref="SearchPromptTests"/>
/// pins what the surface means and says; these pin that the client is wired to it — that the chord opens
/// it, that ⌥A widens what it looks at, that ⏎ takes the reader to the window the line is really in, and
/// that nothing it does reaches the wire.
/// </summary>
/// <remarks>
/// Serialised for the reason every file that renders a frame is: rendering redirects the process-global
/// <c>Console.Out</c>, and the harness redirects <c>Console.In</c>.
/// </remarks>
[NotInParallel]
public class SearchEndToEndTests
{
    private const int Width = 140;
    private const int Height = 40;
    private const string Main = "main";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static (SharpMUTermApp App, WorldSession Session) Bound()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        app.RenderSnapshot();
        return (app, session);
    }

    private static ConsoleKeyInfo Ctrl(ConsoleKey key) => new('\0', key, false, false, true);

    private static ConsoleKeyInfo Alt(ConsoleKey key) => new('\0', key, false, true, false);

    private static ConsoleKeyInfo Bare(ConsoleKey key) => new('\0', key, false, false, false);

    [Test]
    public async Task CtrlFOpensTheSurfaceAndCtrlFAgainClosesIt()
    {
        var (app, _) = Bound();

        app.SimulateKey(Ctrl(ConsoleKey.F));
        await Assert.That(app.SearchIsOpen).IsTrue();

        app.SimulateKey(Ctrl(ConsoleKey.F));
        await Assert.That(app.SearchIsOpen).IsFalse();
    }

    [Test]
    public async Task TypingListsTheLinesHoldingTheQuery()
    {
        var (app, session) = Bound();
        session.PrintSystem("*** The goblin snarls at you.");
        session.PrintSystem("*** A town guard stands watch.");
        session.PrintSystem("*** You hit the goblin.");

        app.SimulateKey(Ctrl(ConsoleKey.F));
        app.SimulateSearchTyping("goblin");

        await Assert.That(app.SearchRows.Count).IsEqualTo(2);
        await Assert.That(app.SearchRows.All(r => r.WindowId == Main)).IsTrue();
    }

    /// <summary>
    /// The scope toggle, and the reason it exists: the hit that matters is usually in a pane you are not
    /// looking at. Narrow first — the focused window — and ⌥A widens.
    /// </summary>
    [Test]
    public async Task AltAWidensFromTheFocusedWindowToEveryWindow()
    {
        var (app, session) = Bound();
        session.PrintSystem("*** the goblin snarls at you");
        app.SimulateWindowChange(DemoScene.ChatWindowId);
        app.SimulateWindowChange(Main);

        app.SimulateKey(Ctrl(ConsoleKey.F));
        app.SimulateSearchTyping("the");
        var focused = app.SearchRows.Count;

        app.SimulateSearchKey(Alt(ConsoleKey.A));

        await Assert.That(app.SearchRows.Count).IsGreaterThan(focused);
        await Assert.That(app.SearchRows.Any(r => r.WindowId != Main)).IsTrue();
    }

    /// <summary>
    /// ⌥E is the difference between a query and a pattern, and the frame says which way it is set — this
    /// is the behaviour behind that label.
    /// </summary>
    [Test]
    public async Task AltESwitchesTheQueryToAPattern()
    {
        var (app, session) = Bound();
        session.PrintSystem("*** You hit the goblin for 12 damage.");
        session.PrintSystem("*** You hit the goblin for no damage.");

        app.SimulateKey(Ctrl(ConsoleKey.F));
        app.SimulateSearchTyping(@"\d+ damage");
        await Assert.That(app.SearchRows).IsEmpty();

        app.SimulateSearchKey(Alt(ConsoleKey.E));

        await Assert.That(app.SearchRows.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The headline: ⏎ on a hit in a window the reader is <em>not</em> looking at takes them to that
    /// window, not merely to that line. Activation is the app's one path, so the pane, the tab and the
    /// session all move together.
    /// </summary>
    [Test]
    public async Task EnterGoesToTheWindowTheLineIsActuallyIn()
    {
        var (app, session) = Bound();
        session.PrintSystem("*** the vault key is behind the bar");
        app.SimulateWindowChange(DemoScene.ChatWindowId);

        app.SimulateKey(Ctrl(ConsoleKey.F));
        app.SimulateSearchKey(Alt(ConsoleKey.A));
        app.SimulateSearchTyping("vault key");
        await Assert.That(app.SearchRows.Count).IsEqualTo(1);

        app.SimulateSearchKey(Bare(ConsoleKey.Enter));

        await Assert.That(app.SearchIsOpen).IsFalse();
        await Assert.That(app.ActiveWindowId()).IsEqualTo(Main);
    }

    /// <summary>
    /// And it marks where it took them: a bar directly above the line, saying which hit this is out of
    /// how many and which key goes to the next.
    /// </summary>
    [Test]
    public async Task TheBarSitsDirectlyAboveTheLineItSentYouTo()
    {
        var (app, session) = Bound();
        session.PrintSystem("*** the vault key is behind the bar");

        app.SimulateKey(Ctrl(ConsoleKey.F));
        app.SimulateSearchTyping("vault key");
        app.SimulateSearchKey(Bare(ConsoleKey.Enter));

        var index = app.SearchBarIndex(Main);
        await Assert.That(index).IsNotNull();

        var rows = app.PaneLines(Main);
        await Assert.That(rows[index!.Value]).Contains("(1 of 1)");
        await Assert.That(rows[index.Value]).Contains(SearchBarRenderer.NextChord);
        await Assert.That(rows[index.Value + 1]).Contains("vault key");
    }

    /// <summary>⌥G walks to the next hit and wraps, without the surface being reopened.</summary>
    [Test]
    public async Task AltGWalksToTheNextHitAndWraps()
    {
        var (app, session) = Bound();
        session.PrintSystem("*** the goblin snarls");
        session.PrintSystem("*** a quiet line");
        session.PrintSystem("*** the goblin falls");

        app.SimulateKey(Ctrl(ConsoleKey.F));
        app.SimulateSearchTyping("goblin");
        app.SimulateSearchKey(Bare(ConsoleKey.Enter));
        var first = app.SearchBarIndex(Main)!.Value;

        app.SimulateKey(Alt(ConsoleKey.G));
        var second = app.SearchBarIndex(Main)!.Value;
        await Assert.That(second).IsGreaterThan(first);
        await Assert.That(app.PaneLines(Main)[second + 1]).Contains("goblin falls");

        // And round again, rather than stopping at the end with nothing said.
        app.SimulateKey(Alt(ConsoleKey.G));
        await Assert.That(app.PaneLines(Main)[app.SearchBarIndex(Main)!.Value + 1]).Contains("goblin snarls");
    }

    [Test]
    public async Task AltGWithNothingSearchedForYetRefusesOutLoud()
    {
        var (app, _) = Bound();

        app.SimulateKey(Alt(ConsoleKey.G));

        await Assert.That(app.Messages.Entries.Any(m => m.Text.Contains("nothing has been searched for"))).IsTrue();
    }

    /// <summary>One bar, client-wide: a second landing moves it rather than leaving a trail.</summary>
    [Test]
    public async Task ASecondLandingMovesTheBarRatherThanAddingOne()
    {
        var (app, session) = Bound();
        session.PrintSystem("*** the goblin snarls");
        session.PrintSystem("*** the goblin falls");

        app.SimulateKey(Ctrl(ConsoleKey.F));
        app.SimulateSearchTyping("goblin");
        app.SimulateSearchKey(Bare(ConsoleKey.Enter));
        app.SimulateKey(Alt(ConsoleKey.G));

        var bars = app.PaneLines(Main).Count(l => l.Contains(Glyphs.Search));
        await Assert.That(bars).IsEqualTo(1);
    }

    /// <summary>
    /// The surface is modal chrome and sends nothing. A connected recording transport is the point: with
    /// an unconnected session every "nothing reached the wire" assertion passes whatever the surface did.
    /// </summary>
    [Test]
    public async Task NothingTheSurfaceDoesReachesTheWire()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        await session.ConnectAsync();
        app.RenderSnapshot();
        session.PrintSystem("*** the goblin snarls");
        var before = telnet.Lines.Count;

        app.SimulateKey(Ctrl(ConsoleKey.F));
        app.SimulateSearchTyping("goblin");
        app.SimulateSearchKey(Bare(ConsoleKey.Enter));

        await Assert.That(telnet.Lines.Count).IsEqualTo(before);
    }

    /// <summary>
    /// It refuses over the composer and says which surface is in the way — the composer's own guard is the
    /// other half, and together they make the pair mutually exclusive rather than one-sided.
    /// </summary>
    [Test]
    public async Task ItRefusesOverTheComposerAndSaysSo()
    {
        var (app, _) = Bound();
        app.SimulateKey(Bare(ConsoleKey.F1));

        app.SimulateKey(Ctrl(ConsoleKey.F));

        await Assert.That(app.SearchIsOpen).IsFalse();
        await Assert.That(app.Messages.Entries.Any(m => m.Text.Contains("composer"))).IsTrue();
    }
}
