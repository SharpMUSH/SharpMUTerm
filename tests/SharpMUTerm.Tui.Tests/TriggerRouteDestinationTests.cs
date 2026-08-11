using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The two reported defects, from the shell's side and over a live connection: <b>a trigger could only
/// route to a spawn window</b>, and <b>a highlight colour did not survive the same rule's rewrite</b>.
/// <para>
/// Both are asserted end to end rather than on the engine alone, because both have a second half here.
/// A destination is only a destination if the shell appends to it (<c>OnSpawnLine</c>), and a highlight
/// is only a highlight if the markup a pane is fed carries the colour — a pane holds Spectre markup and
/// nothing else, so a <c>StyledLine</c> that was right on the way in proves nothing about the frame.
/// </para>
/// </summary>
/// <remarks>
/// Serialised with the other end-to-end suites: constructing the app and rendering a frame both touch
/// the process-global console streams.
/// </remarks>
[NotInParallel]
public class TriggerRouteDestinationTests
{
    private const int Width = 160;
    private const int Height = 40;

    private const string Ann = "Convergence.Ann";
    private const string Bob = "Convergence.Bob";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    // ---- Routing somewhere that is not a fresh spawn window -----------------------------------

    /// <summary>
    /// The headline. Ann's rule routes to <c>Bob</c> — a window that already exists, of a kind no rule
    /// could reach — and the line lands in it. Before the fix routing went through
    /// <c>Workspace.RouteSpawn</c>, which can only ever answer with a spawn id it owns, so this opened a
    /// fourth pane called <c>Bob</c> beside Bob's own and left his empty.
    /// </summary>
    [Test]
    public async Task ARuleRoutesIntoAnotherCharactersWindowThatAlreadyExists()
    {
        var app = await Two(RouteTo("Bob"));

        Receive(app, AnnWire, "<Public> Ann says, \"hello\"\n");

        await Assert.That(string.Join("\n", app.PaneLines(MainWindowOf(app, Bob)))).Contains("hello");
        await Assert.That(app.WindowIds()).DoesNotContain(Workspace.SpawnWindowId(Ann, "Bob"));
    }

    /// <summary>
    /// And into a character's own main window, which the F2 route list has always been able to name and a
    /// rule could not reach: <c>main</c> there means <em>do not route</em>, so it only ever described a
    /// line the rule did not also gag. A gagging rule aimed at the main window used to delete the line.
    /// </summary>
    [Test]
    public async Task AGaggingRuleCanStillPutItsLineInItsOwnMainWindow()
    {
        var app = await Two(RouteTo("Ann"));

        Receive(app, AnnWire, "<Public> Ann says, \"hello\"\n");

        await Assert.That(string.Join("\n", app.PaneLines(MainWindowOf(app, Ann)))).Contains("hello");
    }

    /// <summary>
    /// A target nothing answers to is still a capture pane, created and owned by the matching session —
    /// the path every capture rule that ships takes, and the one that must not have moved.
    /// </summary>
    [Test]
    public async Task AnUnknownTargetStillOpensACapturePane()
    {
        var app = await Two(RouteTo("Chat"));

        Receive(app, AnnWire, "<Public> Ann says, \"hello\"\n");

        var id = Workspace.SpawnWindowId(Ann, "Chat");
        await Assert.That(app.WindowIds()).Contains(id);
        await Assert.That(app.WindowOwnerOf(id)).IsEqualTo(Ann);
        await Assert.That(string.Join("\n", app.PaneLines(id))).Contains("hello");
    }

    /// <summary>
    /// The per-session guarantee is untouched, and this is the fixture it was bought with: two characters
    /// running one capture rule still get a pane each. Resolution admits another character's <em>main</em>
    /// window and never their capture panes, so a shared channel name cannot collapse the two back into
    /// one and file the second character's channel under the first.
    /// </summary>
    [Test]
    public async Task TwoCharactersCapturingOneNameStillGetAPaneEach()
    {
        var app = await Two(RouteTo("Public"));

        Receive(app, AnnWire, "<Public> Ann says, \"first\"\n");
        Receive(app, BobWire, "<Public> Bob says, \"second\"\n");

        var ann = string.Join("\n", app.PaneLines(Workspace.SpawnWindowId(Ann, "Public")));
        var bob = string.Join("\n", app.PaneLines(Workspace.SpawnWindowId(Bob, "Public")));

        await Assert.That(ann).Contains("first");
        await Assert.That(ann).DoesNotContain("second");
        await Assert.That(bob).Contains("second");
        await Assert.That(bob).DoesNotContain("first");
    }

    /// <summary>
    /// A window this session does not own is not relabelled by routing into it. <c>OwnerLabel</c> prefixes
    /// a tab as <c>Owner: Name</c> to tie a capture pane scattered into another pane back to its
    /// character; stamping it on a destination somebody else owns would rename their pane after whoever
    /// last routed a line into it.
    /// </summary>
    [Test]
    public async Task RoutingIntoAWindowDoesNotRelabelItAfterTheRoutingCharacter()
    {
        var app = await Two(RouteTo("Bob"));

        Receive(app, AnnWire, "<Public> Ann says, \"hello\"\n");

        await Assert.That(app.WindowOwnerLabelOf(MainWindowOf(app, Bob))).IsNull();
    }

    // ---- The highlight, in the markup a pane is actually fed ------------------------------------

    /// <summary>
    /// A rule that rewrites and highlights: the pane's markup carries the colour. On the unfixed build
    /// the rewrite ran after the highlight and replaced the line with an unstyled one, so the pane was
    /// fed plain text while the F2 screen went on badging that rule <c>H</c> and painting its swatch.
    /// </summary>
    [Test]
    public async Task ARewrittenLineReachesThePaneWearingItsHighlight()
    {
        var app = await Two(Configuration(new TriggerActions
        {
            HighlightForeground = TerminalColor.FromRgb(0xff, 0xd7, 0x00),
            Rewrite = "» $1",
        }));

        Receive(app, AnnWire, "<Public> Ann says, \"hello\"\n");

        var line = app.PaneLines(MainWindowOf(app, Ann)).Last();
        await Assert.That(line).Contains("» Ann says");
        await Assert.That(line).Contains("#ffd700");
    }

    // ---- Harness ------------------------------------------------------------------------------

    private RecordingTelnetSession AnnWire { get; set; } = new();

    private RecordingTelnetSession BobWire { get; set; } = new();

    /// <summary>The window a character's own output goes to — found by its owner rather than assumed.</summary>
    private static string MainWindowOf(SharpMUTermApp app, string sessionKey) =>
        app.WindowIds().Single(id =>
            app.WindowOwnerOf(id) == sessionKey && !id.StartsWith(Workspace.SpawnPrefix, StringComparison.Ordinal));

    private static AppConfiguration RouteTo(string target) =>
        Configuration(new TriggerActions { SpawnTarget = target, Gag = true });

    private static AppConfiguration Configuration(TriggerActions actions)
    {
        var config = new AppConfiguration();
        config.TriggerSets.Add(new TriggerSet
        {
            Name = "Comms",
            Triggers =
            {
                new Trigger { Name = "Public", Pattern = "^<Public> (.+)$", Actions = actions },
            },
        });

        config.Worlds.Add(new WorldDefinition
        {
            Name = "Convergence",
            Host = "convergence.example.org",
            Port = 4201,
            Characters =
            {
                new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings(), TriggerSets = { "Comms" } },
                new CharacterDefinition { Name = "Bob", Logging = new LoggingSettings(), TriggerSets = { "Comms" } },
            },
        });

        return config;
    }

    /// <summary>
    /// Both characters open and connected. Two, because every destination this is about is a window
    /// somebody else has — and because a session that was never connected never runs its receive path,
    /// which would make any routing assertion true whatever the code does.
    /// </summary>
    private async Task<SharpMUTermApp> Two(AppConfiguration config)
    {
        Console.SetIn(TextReader.Null);
        AnnWire = new RecordingTelnetSession();
        BobWire = new RecordingTelnetSession();

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
        await Open(app, Ann, AnnWire);
        await Open(app, Bob, BobWire);
        app.RenderNextFrame();
        return app;
    }

    private static async Task Open(SharpMUTermApp app, string sessionKey, RecordingTelnetSession wire)
    {
        app.TelnetFactory = _ => wire;
        if (!app.DispatchCommand(CommandIds.Character(sessionKey)))
        {
            throw new InvalidOperationException($"the app would not switch to {sessionKey}");
        }

        await app.FindSession(sessionKey)!.ConnectAsync();
    }

    private static void Receive(SharpMUTermApp app, RecordingTelnetSession wire, string text)
    {
        wire.Receive(text);
        app.RenderNextFrame();
    }
}
