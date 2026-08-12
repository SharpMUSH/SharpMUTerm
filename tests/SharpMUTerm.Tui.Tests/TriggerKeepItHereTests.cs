using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// "Highlight text and send it to the pane where we found it" — the reported gap, end to end.
/// <para>
/// Two things it turned out to need, and one it did not. It did <b>not</b> need a highlight rule to
/// carry a route: there is one line and one set of destinations, so a rule that only recolours already
/// reaches every pane the line was going to. What it needed was a way to <em>say</em> that — the F2
/// route field spelt "delivers nowhere of its own" as <c>main</c>, which reads as a destination — and a
/// <c>main</c> that really is one, so a shared trigger set can name each character's own window without
/// knowing that character's name.
/// </para>
/// </summary>
/// <remarks>
/// Serialised with the other end-to-end suites: constructing the app and rendering a frame both touch
/// the process-global console streams.
/// </remarks>
[NotInParallel]
public class TriggerKeepItHereTests
{
    private const int Width = 160;
    private const int Height = 40;

    private const string Ann = "Convergence.Ann";
    private const string Bob = "Convergence.Bob";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>The gold a highlight rule paints with — comfortably over the legibility floor, so what
    /// reaches the pane is the colour that was picked rather than a lift of it.</summary>
    private const string Gold = "#ffd700";

    [Test]
    public async Task AHighlightRuleReachesTheCapturePaneAnotherRuleSentTheLineTo()
    {
        // The user's own framing: the second rule "should still follow the original route, as long as it
        // does not change where it routes to". The capture rule owns the destination; the highlight rule
        // adds no route and recolours the line that goes there.
        var app = await Two(Config(
            Capture("^<Chat>", "Chat"),
            Highlight("Ann")));

        Receive(app, AnnWire, "<Chat> Ann says hello\n");

        // The highlight splits the line into spans, so the assertion is on the fragments and the colour
        // rather than on the sentence — a pane holds markup, and the whole point of the rule is that the
        // sentence is no longer one run.
        var chat = string.Join("\n", app.PaneLines(Workspace.SpawnWindowId(Ann, "Chat")));
        await Assert.That(chat).Contains($"[{Gold}]Ann[/]");
        await Assert.That(chat).Contains("says hello");
    }

    [Test]
    public async Task AHighlightRuleDoesNotOpenAPaneOrCopyTheLineAnywhere()
    {
        // The half that would be easy to break by "fixing" the above: a rule with no route must add no
        // destination at all, or every highlight rule would sprout a pane.
        var app = await Two(Config(Highlight("Ann")));

        Receive(app, AnnWire, "Ann waves at you\n");

        await Assert.That(app.WindowIds().Any(id => id.StartsWith(Workspace.SpawnPrefix, StringComparison.Ordinal)))
            .IsFalse();

        var main = string.Join("\n", app.PaneLines(MainWindowOf(app, Ann)));
        await Assert.That(main).Contains($"[{Gold}]Ann[/]");
        await Assert.That(main).Contains("waves at you");
    }

    [Test]
    public async Task RoutingToMainKeepsAGaggedLineInTheOwnersOwnWindow()
    {
        // `route: main` is a destination and a gag suppresses only the *default* delivery, so the line
        // stays. Before, `main` was the label on a null route, and gag deleted the line outright.
        var app = await Two(Config(new Trigger
        {
            Name = "Public",
            Pattern = "^<Public> (.+)$",
            Actions = new TriggerActions { SpawnTarget = TriggerActions.MainWindow, Gag = true },
        }));

        Receive(app, AnnWire, "<Public> Ann says hello\n");

        await Assert.That(string.Join("\n", app.PaneLines(MainWindowOf(app, Ann)))).Contains("hello");
    }

    [Test]
    public async Task MainIsEachCharactersOwnWindowAndNotWhicheverOneMatchedFirst()
    {
        // The reason `main` earns a reserved word rather than being spelt as the window's title: one
        // trigger set is shared by every character that lists it, and a title can only name one of them.
        // Both characters run this rule and each keeps their own line.
        var app = await Two(Config(new Trigger
        {
            Name = "Public",
            Pattern = "^<Public> (.+)$",
            Actions = new TriggerActions { SpawnTarget = TriggerActions.MainWindow, Gag = true },
        }));

        Receive(app, AnnWire, "<Public> Ann says first\n");
        Receive(app, BobWire, "<Public> Bob says second\n");

        var annPane = string.Join("\n", app.PaneLines(MainWindowOf(app, Ann)));
        var bobPane = string.Join("\n", app.PaneLines(MainWindowOf(app, Bob)));

        await Assert.That(annPane).Contains("first");
        await Assert.That(annPane).DoesNotContain("second");
        await Assert.That(bobPane).Contains("second");
        await Assert.That(bobPane).DoesNotContain("first");
    }

    [Test]
    public async Task TwoRulesNamingOnePaneDeliverOneLineToIt()
    {
        // They delivered two, and the pane showed the line twice.
        var app = await Two(Config(
            Capture("^<Chat>", "Chat"),
            new Trigger
            {
                Name = "Mention",
                Pattern = "Ann",
                Actions = new TriggerActions { SpawnTarget = "Chat" },
            }));

        Receive(app, AnnWire, "<Chat> Ann says hello\n");

        var lines = app.PaneLines(Workspace.SpawnWindowId(Ann, "Chat"));
        await Assert.That(lines.Count(l => l.Contains("Ann says hello", StringComparison.Ordinal))).IsEqualTo(1);
    }

    // ---- Harness ------------------------------------------------------------------------------

    private RecordingTelnetSession AnnWire { get; set; } = new();

    private RecordingTelnetSession BobWire { get; set; } = new();

    private static string MainWindowOf(SharpMUTermApp app, string sessionKey) =>
        app.WindowIds().Single(id =>
            app.WindowOwnerOf(id) == sessionKey && !id.StartsWith(Workspace.SpawnPrefix, StringComparison.Ordinal));

    private static Trigger Capture(string pattern, string target) => new()
    {
        Name = target,
        Pattern = pattern,
        Actions = new TriggerActions { SpawnTarget = target, Gag = true },
    };

    private static Trigger Highlight(string pattern) => new()
    {
        Name = pattern,
        Pattern = pattern,
        Actions = new TriggerActions { HighlightForeground = TerminalColor.FromRgb(0xff, 0xd7, 0x00) },
    };

    private static AppConfiguration Config(params Trigger[] triggers)
    {
        var config = new AppConfiguration();
        var set = new TriggerSet { Name = "Comms" };
        foreach (var trigger in triggers)
        {
            set.Triggers.Add(trigger);
        }

        config.TriggerSets.Add(set);
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
