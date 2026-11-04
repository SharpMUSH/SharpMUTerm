using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>The send group: picked on ⌃P, marked on the rail, one line to each connected member once.</summary>
[NotInParallel]
public class SendGroupTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static AppConfiguration Config()
    {
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Mush", Host = "mush.example", Port = 4201 };
        foreach (var name in new[] { "Ann", "Bob", "Cy" })
        {
            world.Characters.Add(new CharacterDefinition { Name = name, Logging = new() });
        }

        config.Worlds.Add(world);
        return config;
    }

    private static async Task<(SharpMUTermApp App, Dictionary<string, RecordingTelnetSession> Wires)> Open(params string[] connect)
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(Config(), Headless, new HeadlessConsoleDriver(120, 32));
        var wires = new Dictionary<string, RecordingTelnetSession>();
        var queue = new Queue<string>();
        app.TelnetFactory = _ =>
        {
            var wire = new RecordingTelnetSession();
            wires[queue.Dequeue()] = wire;
            return wire;
        };

        foreach (var name in new[] { "Ann", "Bob", "Cy" })
        {
            app.DispatchCommand(CommandIds.Character("Mush." + name));
            if (connect.Contains(name))
            {
                queue.Enqueue(name);
                await app.FindSession("Mush." + name)!.ConnectAsync();
            }
        }

        return (app, wires);
    }

    [Test]
    public async Task TheGroupIsOfferedOnceTwoCharactersAreOpenAndEachMemberIsMarkedOnTheRail()
    {
        var (app, _) = await Open("Ann", "Bob");
        await using var _app = app;

        var titles = app.BuildCatalog().Select(i => i.Title).ToArray();
        await Assert.That(titles).Contains("Add Ann to the send group");
        await Assert.That(titles).DoesNotContain("Send to group");

        app.DispatchCommand(CommandIds.GroupToggle("Mush.Ann"));
        titles = app.BuildCatalog().Select(i => i.Title).ToArray();
        await Assert.That(titles).Contains("Remove Ann from the send group");
        await Assert.That(titles).Contains("Send to group");

        var rail = app.RailLines.Select(MarkupText.Plain).ToArray();
        await Assert.That(rail.Single(l => l.Contains("Ann"))).Contains("◆");
        await Assert.That(rail.Single(l => l.Contains("Bob"))).DoesNotContain("◆");
    }

    [Test]
    public async Task ALineGoesOnceToEachConnectedMemberAndNotToTheOthers()
    {
        var (app, wires) = await Open("Ann", "Bob");
        await using var _app = app;
        foreach (var key in new[] { "Mush.Ann", "Mush.Bob", "Mush.Cy" })
        {
            app.DispatchCommand(CommandIds.GroupToggle(key));
        }

        foreach (var c in "pose waves")
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.DispatchCommand(CommandIds.GroupSend);
        await Task.Delay(50);

        await Assert.That(wires["Ann"].Lines).IsEquivalentTo(new[] { "pose waves" });
        await Assert.That(wires["Bob"].Lines).IsEquivalentTo(new[] { "pose waves" });
        await Assert.That(wires.ContainsKey("Cy")).IsFalse();
        await Assert.That(app.ArmedInputText).IsEqualTo(string.Empty);
        await Assert.That(app.Messages.Entries[^1].Text).Contains("not to Cy");
    }

    /// <summary>⌥C, then <c>g</c> on a row: that character joins the group, and the keys stay in the sidebar for the next.</summary>
    [Test]
    public async Task GOnASidebarRowTogglesThatCharacterInTheGroup()
    {
        var (app, _) = await Open("Ann", "Bob");
        await using var _app = app;

        app.SimulateKey(new ConsoleKeyInfo('c', ConsoleKey.C, false, true, false));
        await Assert.That(app.RailKeysActive).IsTrue();
        app.SimulateRailKey(new ConsoleKeyInfo('g', ConsoleKey.G, false, false, false));

        await Assert.That(app.SendGroupKeys.Count).IsEqualTo(1);
        await Assert.That(app.RailKeysActive).IsTrue().Because("g keeps the keys in the sidebar");

        app.SimulateRailKey(new ConsoleKeyInfo('g', ConsoleKey.G, false, false, false));
        await Assert.That(app.SendGroupKeys.Count).IsEqualTo(0).Because("a second g takes the character out again");
    }

    /// <summary>⌥S sends the command line to the group, as ⌃P ▸ Send to group does, and the entry names the chord.</summary>
    [Test]
    public async Task AltSSendsTheCommandLineToTheGroup()
    {
        var (app, wires) = await Open("Ann", "Bob");
        await using var _app = app;
        app.DispatchCommand(CommandIds.GroupToggle("Mush.Ann"));
        app.DispatchCommand(CommandIds.GroupToggle("Mush.Bob"));

        var entry = app.BuildCatalog().Single(i => i.Id == CommandIds.GroupSend);
        await Assert.That(entry.Key).IsEqualTo("⌥S");

        foreach (var c in "wave")
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.SimulateKey(new ConsoleKeyInfo('s', ConsoleKey.S, false, true, false));
        await Task.Delay(50);

        await Assert.That(wires["Ann"].Lines).IsEquivalentTo(new[] { "wave" });
        await Assert.That(wires["Bob"].Lines).IsEquivalentTo(new[] { "wave" });
    }
}
