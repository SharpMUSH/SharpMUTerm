using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>⇥ on the command line: completes from what the bar's own connection has seen, and cycles.</summary>
[NotInParallel]
public class CompletionTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly ConsoleKeyInfo Tab = new('\t', ConsoleKey.Tab, false, false, false);

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
    }

    private static async Task<(SharpMUTermApp App, RecordingTelnetSession Telnet)> Connected()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Shire", Host = "shire.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        await app.BindWorldWithoutConnecting(world).ConnectAsync();
        app.RenderNextFrame();
        return (app, telnet);
    }

    [Test]
    public async Task TabCompletesTheLastWordAndCyclesBackToWhatWasTyped()
    {
        var (app, telnet) = await Connected();
        telnet.Receive("Gandalf and the gatekeeper are here.");
        Type(app, "say hi ga");

        app.SimulateKey(Tab);
        await Assert.That(app.ArmedInputText).IsEqualTo("say hi gatekeeper");
        app.SimulateKey(Tab);
        await Assert.That(app.ArmedInputText).IsEqualTo("say hi Gandalf");
        app.SimulateKey(Tab);
        await Assert.That(app.ArmedInputText).IsEqualTo("say hi ga");
        await Assert.That(telnet.Lines).IsEmpty();
    }

    [Test]
    public async Task TypingEndsTheCycle()
    {
        var (app, telnet) = await Connected();
        telnet.Receive("Gandalf and the gatekeeper are here.");
        Type(app, "ga");
        app.SimulateKey(Tab);
        Type(app, " and gan");

        app.SimulateKey(Tab);

        await Assert.That(app.ArmedInputText).IsEqualTo("gatekeeper and Gandalf");
    }

    [Test]
    public async Task NothingToCompleteLeavesTheTextAlone()
    {
        var (app, telnet) = await Connected();
        telnet.Receive("Nobody is here.");
        Type(app, "say zz");

        app.SimulateKey(Tab);

        await Assert.That(app.ArmedInputText).IsEqualTo("say zz");
    }
}
