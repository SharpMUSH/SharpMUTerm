using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A MUSH's edit grab on the command line: the text lands in the bar of the window it came from, nothing is
/// sent, and a line being typed is kept in history rather than lost.
/// </summary>
[NotInParallel]
public class EditGrabTests
{
    private const string Prefix = "FugueEdit >";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo Key(char c) => new(c, ConsoleKey.NoName, false, false, false);

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(Key(c));
        }
    }

    private sealed record Two(SharpMUTermApp App, RecordingTelnetSession Ann, RecordingTelnetSession Bob);

    private static async Task<Two> TwoWorlds()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var ares = new WorldDefinition { Name = "Ares", Host = "ares.example.org", Port = 4201, GrabPrefix = Prefix };
        ares.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        var other = new WorldDefinition { Name = "Other", Host = "other.example.org", Port = 4201, GrabPrefix = Prefix };
        other.Characters.Add(new CharacterDefinition { Name = "Bob", Logging = new LoggingSettings() });
        config.Worlds.Add(ares);
        config.Worlds.Add(other);

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        var ann = new RecordingTelnetSession();
        var bob = new RecordingTelnetSession();
        app.TelnetFactory = options => options.Host.StartsWith("ares", StringComparison.Ordinal) ? ann : bob;
        foreach (var key in new[] { "Other.Bob", "Ares.Ann" })
        {
            await Assert.That(app.DispatchCommand(Core.Commands.CommandIds.Character(key))).IsTrue();
            await app.FindSession(key)!.ConnectAsync();
        }

        app.RenderNextFrame();
        return new Two(app, ann, bob);
    }

    [Test]
    public async Task AGrabFillsTheCommandLineAndSendsNothing()
    {
        var two = await TwoWorlds();

        two.Ann.Receive($"{Prefix} &desc me=A tall figure in grey.");

        await Assert.That(two.App.ArmedInputText).IsEqualTo("&desc me=A tall figure in grey.");
        await Assert.That(two.Ann.Lines).IsEmpty();
    }

    [Test]
    public async Task ALineBeingTypedIsKeptInHistory()
    {
        var two = await TwoWorlds();
        Type(two.App, "say half a thought");

        two.Ann.Receive($"{Prefix} &desc me=x");
        await Assert.That(two.App.ArmedInputText).IsEqualTo("&desc me=x");

        two.App.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, true, false));
        await Assert.That(two.App.ArmedInputText).IsEqualTo("say half a thought");
    }

    [Test]
    public async Task AGrabForAWindowNotInFrontWaitsForIt()
    {
        var two = await TwoWorlds();
        Type(two.App, "for Ann");

        two.Bob.Receive($"{Prefix} &desc me=Bob's");

        // Ann's bar is untouched: a grab never lands on another character's command line.
        await Assert.That(two.App.ArmedInputText).IsEqualTo("for Ann");
        await Assert.That(two.App.DispatchCommand(Core.Commands.CommandIds.Character("Other.Bob"))).IsTrue();
        await Assert.That(two.App.ArmedInputText).IsEqualTo("&desc me=Bob's");
        await Assert.That(two.Bob.Lines).IsEmpty();
    }
}
