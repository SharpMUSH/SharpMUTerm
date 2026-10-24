using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <b>History recall belongs to the character, not to the client.</b> The request: "history-recall from
/// the input window should be bound to each character connected, it should not be global." A global list
/// puts the line just sent to Ann under ↑ in Bob's window, one ⏎ away from Bob — the misdelivery
/// <c>SendTarget</c> exists to prevent, arriving by recall rather than by routing. Each test drives real
/// keys through two connected characters in a split, and moves between them the way a user does.
/// </summary>
/// <remarks>Serialised: constructing the app touches the process-global console streams.</remarks>
[NotInParallel]
public class PerCharacterHistoryTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private const string Ann = "Alfa.Ann";
    private const string Bob = "Bravo.Bob";

    private static ConsoleKeyInfo Key(char c) => new(c, ConsoleKey.NoName, false, false, false);

    private static ConsoleKeyInfo Up => new('\0', ConsoleKey.UpArrow, false, false, false);

    private static ConsoleKeyInfo AltKey(ConsoleKey key) =>
        new(char.ToLowerInvariant(key.ToString()[0]), key, false, true, false);

    private static void Send(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(Key(c));
        }

        app.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    [Test]
    public async Task UpRecallsOnlyWhatWasSentToTheFocusedCharacter()
    {
        var app = await BuildScene();

        Send(app, "say to Ann");
        app.SimulateKey(AltKey(ConsoleKey.J)); // to Bob
        await Assert.That(app.ActiveSessionKey).IsEqualTo(Bob);

        app.SimulateKey(Up);
        await Assert.That(app.ArmedInputText)
            .IsEqualTo(string.Empty)
            .Because("Bob has sent nothing, so there is nothing of his to recall");

        Send(app, "say to Bob");
        app.SimulateKey(Up);
        await Assert.That(app.ArmedInputText).IsEqualTo("say to Bob");
        app.SimulateKey(Up);
        await Assert.That(app.ArmedInputText)
            .IsEqualTo("say to Bob")
            .Because("the oldest of Bob's lines is the last stop, and Ann's line is not behind it");
    }

    [Test]
    public async Task ACharactersHistoryIsStillThereWhenYouComeBack()
    {
        var app = await BuildScene();

        Send(app, "look");
        Send(app, "say hello");
        app.SimulateKey(AltKey(ConsoleKey.J)); // to Bob
        Send(app, "score");
        app.SimulateKey(AltKey(ConsoleKey.K)); // back to Ann
        await Assert.That(app.ActiveSessionKey).IsEqualTo(Ann);

        app.SimulateKey(Up);
        await Assert.That(app.ArmedInputText).IsEqualTo("say hello");
        app.SimulateKey(Up);
        await Assert.That(app.ArmedInputText).IsEqualTo("look");

        await Assert.That(app.HistoryEntries(InputBar.Primary)).IsEquivalentTo(new[] { "look", "say hello" });
    }

    /// <summary>The ⌃R surface lists the focused character's lines and says whose they are.</summary>
    [Test]
    public async Task TheHistorySearchShowsTheFocusedCharactersLinesAndNamesThem()
    {
        var app = await BuildScene();

        Send(app, "say to Ann");
        app.SimulateKey(AltKey(ConsoleKey.J)); // to Bob
        Send(app, "say to Bob");

        app.SimulateKey(new ConsoleKeyInfo('\x12', ConsoleKey.R, false, false, true));
        await Assert.That(app.HistorySearchOpen).IsTrue();

        var surface = string.Join("\n", app.HistorySearchLines);
        await Assert.That(surface).Contains("Bob's command line");
        await Assert.That(surface).DoesNotContain("say to Ann");
    }

    /// <summary>Two characters, each connected, each main window in a pane of its own; Ann focused.</summary>
    private static async Task<SharpMUTermApp> BuildScene()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        foreach (var (world, character) in new[] { ("Alfa", "Ann"), ("Bravo", "Bob") })
        {
            var definition = new WorldDefinition
            {
                Name = world,
                Host = $"{world.ToLowerInvariant()}.example.org",
                Port = 4000,
            };
            definition.Characters.Add(new CharacterDefinition { Name = character, Logging = new LoggingSettings() });
            config.Worlds.Add(definition);
        }

        config.LastSession = new WorkspaceState
        {
            Windows =
            {
                new WorkspaceWindowState
                {
                    Id = "main", Title = "Ann", Kind = WindowKind.Main, SessionKey = Ann, Sequence = 1,
                },
                new WorkspaceWindowState
                {
                    Id = "char:Bravo.Bob", Title = "Bob", Kind = WindowKind.Main, SessionKey = Bob, Sequence = 2,
                },
            },
            Root = new LayoutNodeState
            {
                Type = "split",
                Direction = SplitDirection.Row,
                Children =
                {
                    new LayoutNodeState { Type = "pane", Id = "p1", Tabs = { "main" }, ActiveIndex = 0, Sequence = 1 },
                    new LayoutNodeState
                    {
                        Type = "pane", Id = "p2", Tabs = { "char:Bravo.Bob" }, ActiveIndex = 0, Sequence = 2,
                    },
                },
            },
            FocusedPaneId = "p1",
        };

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(140, 36));
        app.TelnetFactory = _ => new RecordingTelnetSession();

        foreach (var key in new[] { Ann, Bob })
        {
            if (!app.DispatchCommand(CommandIds.Character(key)))
            {
                throw new InvalidOperationException($"the app would not switch to {key}");
            }

            await app.FindSession(key)!.ConnectAsync();
        }

        app.DispatchCommand(CommandIds.Character(Ann));
        app.RenderNextFrame();
        if (app.ActiveSessionKey != Ann || app.ArmedInputText.Length != 0)
        {
            throw new InvalidOperationException($"{app.ActiveSessionKey} is active holding '{app.ArmedInputText}'");
        }

        return app;
    }
}
