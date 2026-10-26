using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Presence;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>Discord presence follows the window in front, only for a world that allows it, and is cleared otherwise.</summary>
[NotInParallel]
public class PresenceTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private const string Info = """{"applicationid":"123456789012345678"}""";
    private const string Status = """{"details":"In the Grand Plaza","state":"Level 12","partysize":2,"partymax":4}""";

    private sealed class FakePresence : IPresence
    {
        public List<GamePresence?> Shown { get; } = [];

        public int Opened { get; set; }

        public void Show(GamePresence? presence) => Shown.Add(presence);

        public void Dispose()
        {
        }
    }

    private static async Task<(SharpMUTermApp App, FakePresence Presence, Dictionary<string, RecordingTelnetSession> Wires)> Open(bool allow)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var game = new WorldDefinition { Name = "Game", Host = "game.example", Port = 23, DiscordPresence = allow };
        game.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new() });
        var other = new WorldDefinition { Name = "Other", Host = "other.example", Port = 23 };
        other.Characters.Add(new CharacterDefinition { Name = "Bob", Logging = new() });
        config.Worlds.Add(game);
        config.Worlds.Add(other);

        var presence = new FakePresence();
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null) with
        {
            Presence = () => { presence.Opened++; return presence; },
        };
        var app = new SharpMUTermApp(config, host);
        var wires = new Dictionary<string, RecordingTelnetSession>();
        app.TelnetFactory = options => wires[options.Host] = new RecordingTelnetSession();
        foreach (var key in new[] { "Other.Bob", "Game.Ann" })
        {
            app.DispatchCommand(CommandIds.Character(key));
            await app.FindSession(key)!.ConnectAsync();
        }

        return (app, presence, wires);
    }

    [Test]
    public async Task TheGameInFrontIsShownAndSwitchingAwayClearsIt()
    {
        var (app, presence, wires) = await Open(allow: true);
        await using var _ = app;

        wires["game.example"].ReceiveGmcp("External.Discord.Info", Info);
        wires["game.example"].ReceiveGmcp("External.Discord.Status", Status);
        app.RenderNextFrame();

        await Assert.That(app.ShownPresence?.Details).IsEqualTo("In the Grand Plaza");
        await Assert.That(app.ShownPresence?.PartyMax).IsEqualTo(4);
        await Assert.That(presence.Opened).IsEqualTo(1);

        app.DispatchCommand(CommandIds.Character("Other.Bob"));
        app.RenderNextFrame();
        await Assert.That(app.ShownPresence).IsNull();
        await Assert.That(presence.Shown[^1]).IsNull();

        app.DispatchCommand(CommandIds.Character("Game.Ann"));
        await app.FindSession("Game.Ann")!.DisconnectAsync();
        app.RenderNextFrame();
        await Assert.That(app.ShownPresence).IsNull();
    }

    [Test]
    public async Task AWorldThatHasNotOptedInNeverReachesDiscord()
    {
        var (app, presence, wires) = await Open(allow: false);
        await using var _ = app;

        wires["game.example"].ReceiveGmcp("External.Discord.Info", Info);
        wires["game.example"].ReceiveGmcp("External.Discord.Status", Status);
        app.RenderNextFrame();

        await Assert.That(presence.Opened).IsEqualTo(0);
        await Assert.That(app.ShownPresence).IsNull();
    }
}
