using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Mapping;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>The map window: drawn from the connection's map, walked by a click or <c>/walk</c>.</summary>
[NotInParallel]
public class MapWindowTests
{
    private const string Ann = "Convergence.Ann";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static async Task<(SharpMUTermApp App, RecordingTelnetSession Wire)> Open(ManualTimeProvider? clock = null)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Convergence",
            Host = "convergence.example",
            Port = 4201,
            Map = new MapSettings { WalkFailurePattern = "can't go that way" },
            Characters = { new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() } },
        });
        var wire = new RecordingTelnetSession();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(160, 32), time: clock);
        app.TelnetFactory = _ => wire;
        if (!app.DispatchCommand(CommandIds.Character(Ann)))
        {
            throw new InvalidOperationException("the app would not switch to Ann");
        }

        await app.FindSession(Ann)!.ConnectAsync();
        wire.ReceiveGmcp("Room.Info", """{"num":1,"name":"Hall","area":"Keep","exits":{"n":2}}""");
        wire.ReceiveGmcp("Room.Info", """{"num":2,"name":"Gallery","area":"Keep","exits":{"s":1,"e":3}}""");
        wire.ReceiveGmcp("Room.Info", """{"num":3,"name":"Tower","area":"Keep","exits":{"w":2}}""");
        wire.ReceiveGmcp("Room.Info", """{"num":1,"name":"Hall","area":"Keep","exits":{"n":2}}""");
        return (app, wire);
    }

    private static string MapId => SharpMUTermApp.MapWindowId(Ann);

    [Test]
    public async Task ShowMapOpensTheConnectionsMapInFront()
    {
        var (app, _) = await Open();
        await using var _app = app;

        await Assert.That(app.BuildCatalog().Select(i => i.Id)).Contains("term:map");
        await Assert.That(app.DispatchCommand("term:map")).IsTrue();
        app.RenderNextFrame();

        await Assert.That(app.WindowOwnerOf(MapId)).IsEqualTo(Ann);
        await Assert.That(app.ActiveWindowId()).IsEqualTo(MapId);
        var plain = app.MapMarkup(MapId).Select(MarkupText.Plain).ToList();
        await Assert.That(plain[0]).IsEqualTo("Map · Keep · z 0");
        await Assert.That(plain.Any(l => l.Contains("[@]"))).IsTrue();
        await Assert.That(app.MapMarkup(MapId).Count(l => l.Contains("[link=mux:map:"))).IsGreaterThan(0);
    }

    [Test]
    public async Task ClickingARoomWalksThereAStepAtATime()
    {
        var (app, wire) = await Open();
        await using var _app = app;
        app.DispatchCommand("term:map");
        var before = wire.Lines.Count;

        app.SimulateMapClick(MapId, "mux:map:3");
        await Assert.That(wire.Lines.Skip(before)).IsEquivalentTo(new[] { "n" });
        await Assert.That(app.BuildCatalog().Select(i => i.Id)).Contains("term:walk-stop");

        wire.ReceiveGmcp("Room.Info", """{"num":2,"name":"Gallery","area":"Keep","exits":{"s":1,"e":3}}""");
        await Assert.That(wire.Lines.Skip(before)).IsEquivalentTo(new[] { "n", "e" });
        await Assert.That(MarkupText.Plain(app.MapMarkup(MapId)[0])).Contains("walking, 1 to go");

        wire.ReceiveGmcp("Room.Info", """{"num":3,"name":"Tower","area":"Keep","exits":{"w":2}}""");
        await Assert.That(app.SessionMapFor(app.FindSession(Ann)!)!.Walk.IsWalking).IsFalse();
    }

    /// <summary>The map's handler answers its own scheme and nothing else: a send payload is not a room.</summary>
    [Test]
    public async Task AMapClickIsNeverASend()
    {
        var (app, wire) = await Open();
        await using var _app = app;
        app.DispatchCommand("term:map");
        var before = wire.Lines.Count;

        app.SimulateMapClick(MapId, "mux:send:@shutdown");

        await Assert.That(wire.Lines.Count).IsEqualTo(before);
    }

    [Test]
    public async Task WalkByNameAndTheFailureLineStopsIt()
    {
        var (app, wire) = await Open();
        await using var _app = app;
        var before = wire.Lines.Count;

        Type(app, "/walk tower");
        await Assert.That(wire.Lines.Skip(before)).IsEquivalentTo(new[] { "n" });

        wire.Receive("You can't go that way.\r\n");
        await Assert.That(app.SessionMapFor(app.FindSession(Ann)!)!.Walk.IsWalking).IsFalse();
    }

    [Test]
    public async Task ALockedRoomIsNotWalkedThrough()
    {
        var (app, wire) = await Open();
        await using var _app = app;
        var before = wire.Lines.Count;

        Type(app, "/map lock Gallery");
        Type(app, "/walk 3");

        await Assert.That(wire.Lines.Skip(before)).IsEmpty();
        await Assert.That(app.SessionMapFor(app.FindSession(Ann)!)!.Map.Room(2)!.Locked).IsTrue();
    }

    [Test]
    public async Task TheRendererDrawsRoomsLinksAndStubs()
    {
        var map = new WorldMap();
        map.Add(new MapRoom { Id = 1, Name = "Hall", Exits = { ["east"] = 2 }, Stubs = { "south" } });
        map.Add(new MapRoom { Id = 2, Name = "Yard", X = 1, Exits = { ["west"] = 1 } });
        var ink = new MapRenderer.Ink("#ffffff", "#00ff00", "#ffff00", "#ff00ff");

        var lines = MapRenderer.Render(map, 1, [2], 20, 5, ink);
        var plain = lines.Select(MarkupText.Plain).ToList();

        await Assert.That(plain.All(l => l.Length <= 20)).IsTrue();
        await Assert.That(plain).Contains(l => l.Contains("[@]─[ ]"));
        await Assert.That(plain).Contains(l => l.Trim() == "│");
        await Assert.That(lines).Contains(l => l.Contains("[dim]│[/]"));
        await Assert.That(lines).Contains(l => l.Contains("[link=mux:map:2][#ffff00][[ ]][/][/]"));
        await Assert.That(MapRenderer.ParseLink("mux:map:2")).IsEqualTo(2L);
        await Assert.That(MapRenderer.ParseLink("mux:send:2")).IsNull();
    }

    [Test]
    [Arguments("Hall", null)]
    [Arguments("Hall", "Keep")]
    [Arguments("The Exceedingly Long Hall of the Ancient Kings", "The Northern Reaches")]
    public async Task TheStatusRoomCellKeepsItsWidth(string name, string? area)
    {
        await Assert.That(MarkupText.VisibleLength(SharpMUTermApp.RoomCell(name, area))).IsEqualTo(SharpMUTermApp.RoomCellWidth);
    }

    [Test]
    public async Task TheStatusRowSaysWhereYouAre()
    {
        var clock = new ManualTimeProvider();
        var (app, _) = await Open(clock);
        await using var _app = app;
        clock.Advance(SharpMUTermApp.NoticeDuration + TimeSpan.FromSeconds(1)); // the switch's notice holds the row
        app.RenderNextFrame();

        await Assert.That(MarkupText.Plain(app.StatusMarkup)).Contains("⌂ Hall · Keep");
    }

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }
}
