using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>The cell map's edit mode: ⌃P ▸ Edit map, the keys it names, and the /map verbs behind the ones that need words.</summary>
[NotInParallel]
public class MapEditModeTests
{
    private const string Ann = "Convergence.Ann";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static string MapId => SharpMUTermApp.MapWindowId(Ann);

    private static async Task<(SharpMUTermApp App, RecordingTelnetSession Wire)> Open()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Convergence",
            Host = "convergence.example",
            Port = 4201,
            Characters = { new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() } },
        });
        var wire = new RecordingTelnetSession();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(160, 32));
        app.TelnetFactory = _ => wire;
        app.DispatchCommand(CommandIds.Character(Ann));
        await app.FindSession(Ann)!.ConnectAsync();
        wire.ReceiveGmcp("Room.Info", """{"num":1,"name":"Hall","area":"Keep","exits":{"n":2}}""");
        wire.ReceiveGmcp("Room.Info", """{"num":2,"name":"Gallery","area":"Keep","exits":{"s":1,"e":3}}""");
        wire.ReceiveGmcp("Room.Info", """{"num":3,"name":"Tower","area":"Keep","exits":{"w":2}}""");
        wire.ReceiveGmcp("Room.Info", """{"num":1,"name":"Hall","area":"Keep","exits":{"n":2}}""");
        return (app, wire);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0', bool shift = false) => new(c, key, shift, false, false);

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.SimulateKey(Key(ConsoleKey.Enter, '\r'));
    }

    private static Core.Mapping.WorldMap MapOf(SharpMUTermApp app) => app.SessionMapFor(app.FindSession(Ann)!)!.Map;

    [Test]
    public async Task EditMapSelectsTheRoomYouAreInAndTakesTheKeys()
    {
        var (app, _) = await Open();
        await using var _app = app;

        await Assert.That(app.DispatchCommand("map:edit")).IsTrue();

        await Assert.That(app.MapSelection(MapId)).IsEqualTo(1L);
        await Assert.That(app.MapKeysActive).IsTrue();
        var plain = app.MapMarkup(MapId).Select(MarkupText.Plain).ToList();
        await Assert.That(plain.Any(l => l.Contains("#1 Hall"))).IsTrue();
        await Assert.That(plain.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"Esc\s+done"))).IsTrue().Because(string.Join("\n", plain));
    }

    [Test]
    public async Task ArrowsSelectShiftArrowsMoveAndLAndUAnswer()
    {
        var (app, wire) = await Open();
        await using var _app = app;
        app.DispatchCommand("map:edit");
        var before = wire.Lines.Count;

        app.SimulateKey(Key(ConsoleKey.UpArrow));
        await Assert.That(app.MapSelection(MapId)).IsEqualTo(2L);
        var x = MapOf(app).Room(2)!.X;
        app.SimulateKey(Key(ConsoleKey.LeftArrow, shift: true));
        await Assert.That(MapOf(app).Room(2)!.X).IsEqualTo(x - 1);
        app.SimulateKey(Key(ConsoleKey.L, 'l'));
        await Assert.That(MapOf(app).Room(2)!.Locked).IsTrue();
        app.SimulateKey(Key(ConsoleKey.U, 'u'));
        await Assert.That(MapOf(app).Room(2)!.Locked).IsFalse();
        await Assert.That(app.SessionMapFor(app.FindSession(Ann)!)!.Recorder.IsDirty).IsTrue();
        await Assert.That(wire.Lines.Count).IsEqualTo(before).Because("editing sends nothing to the game");
    }

    [Test]
    public async Task AKeyThatNeedsWordsPutsTheCommandOnTheCommandLineAndEnterAppliesIt()
    {
        var (app, _) = await Open();
        await using var _app = app;
        app.DispatchCommand("map:edit");
        app.SimulateKey(Key(ConsoleKey.UpArrow));

        app.SimulateKey(Key(ConsoleKey.R, 'r'));
        await Assert.That(app.MapKeysActive).IsFalse();
        await Assert.That(app.ArmedInputText).IsEqualTo("/map rename 2 Gallery");
        foreach (var _ in "Gallery")
        {
            app.SimulateKey(Key(ConsoleKey.Backspace, '\b'));
        }

        Type(app, "Long Gallery");

        await Assert.That(MapOf(app).Room(2)!.Name).IsEqualTo("Long Gallery");
        await Assert.That(app.MapKeysActive).IsTrue().Because("the edit hands the keys back to the map");
    }

    [Test]
    public async Task EveryKeyTheFooterNamesIsAnswered()
    {
        var (app, _) = await Open();
        await using var _app = app;
        var keys = new Dictionary<string, ConsoleKeyInfo[]>
        {
            ["←↑→↓"] = [Key(ConsoleKey.UpArrow)],
            ["⇧←↑→↓"] = [Key(ConsoleKey.RightArrow, shift: true)],
            ["r"] = [Key(ConsoleKey.R, 'r')], ["a"] = [Key(ConsoleKey.A, 'a')], ["l"] = [Key(ConsoleKey.L, 'l')],
            ["w"] = [Key(ConsoleKey.W, 'w')], ["x"] = [Key(ConsoleKey.X, 'x')], ["-"] = [Key(ConsoleKey.OemMinus, '-')],
            ["n"] = [Key(ConsoleKey.N, 'n')], ["s"] = [Key(ConsoleKey.S, 's')], ["Del"] = [Key(ConsoleKey.Delete)],
            ["PgUp PgDn"] = [Key(ConsoleKey.PageDown)], ["v"] = [Key(ConsoleKey.V, 'v')], ["h"] = [Key(ConsoleKey.H, 'h')],
            ["R"] = [Key(ConsoleKey.R, 'R', shift: true)],
            ["u"] = [Key(ConsoleKey.U, 'u')], ["⏎"] = [Key(ConsoleKey.Enter, '\r')], ["Esc"] = [Key(ConsoleKey.Escape, '\u001b')],
        };

        foreach (var (key, _) in MapEditing.Keys)
        {
            await Assert.That(keys.ContainsKey(key)).IsTrue().Because($"the test knows {key}");
            app.DispatchCommand("map:edit");
            if (!app.MapKeysActive)
            {
                app.DispatchCommand("map:edit");
            }

            app.SimulateKey(Key(ConsoleKey.UpArrow)); // off the room you are in, so Del and ⏎ have something to do
            await Assert.That(app.SimulateMapKey(keys[key][0])).IsTrue().Because($"{key} is on the footer");
            while (app.ArmedInputText.Length > 0)
            {
                app.SimulateKey(Key(ConsoleKey.Backspace, '\b'));
            }

            if (app.MapSelection(MapId) is not null)
            {
                app.SelectMapRoom(MapId, null);
            }
        }
    }

    [Test]
    public async Task AClickSelectsInEditModeAndWalksOutsideIt()
    {
        var (app, wire) = await Open();
        await using var _app = app;
        app.DispatchCommand("term:map");
        app.DispatchCommand("map:edit");
        var before = wire.Lines.Count;

        app.SimulateMapClick(MapId, "mux:map:3");
        await Assert.That(app.MapSelection(MapId)).IsEqualTo(3L);
        await Assert.That(wire.Lines.Count).IsEqualTo(before);

        app.SimulateKey(Key(ConsoleKey.Escape, '\u001b'));
        await Assert.That(app.MapSelection(MapId)).IsNull();
        app.SimulateMapClick(MapId, "mux:map:3");
        await Assert.That(wire.Lines.Skip(before)).IsEquivalentTo(new[] { "n" });
    }

    [Test]
    public async Task DeletingTheRoomYouAreInIsRefusedAndAnyOtherIsUndoable()
    {
        var (app, _) = await Open();
        await using var _app = app;

        Type(app, "/map delete 1");
        await Assert.That(MapOf(app).Room(1)).IsNotNull();
        Type(app, "/map delete 3");
        await Assert.That(MapOf(app).Room(3)).IsNull();
        await Assert.That(app.BuildCatalog().Select(i => i.Id)).Contains("map:undo");
        await Assert.That(app.DispatchCommand("map:undo")).IsTrue();
        await Assert.That(MapOf(app).Room(3)!.Name).IsEqualTo("Tower");
    }
}
