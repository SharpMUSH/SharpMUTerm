using SharpMUTerm.Client.Commands;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Mapping;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Looking at another level or area of the map and back, protecting hand edits from the server's next report, and
/// finding a room — through the cell map's keys and links, ⌃P and <c>/map</c>, over a live GMCP session.
/// </summary>
[NotInParallel]
public class MapViewTests
{
    private const string Ann = "Convergence.Ann";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static string MapId => SharpMUTermApp.MapWindowId(Ann);

    private const string Hall = """{"num":1,"name":"Hall","area":"Keep","coords":"0,0,0","exits":{"n":2,"u":4}}""";

    /// <summary>A keep of three rooms on level 0 and a loft on level 1, and a yard next door; Ann stands in the hall.</summary>
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
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(160, 40));
        app.TelnetFactory = _ => wire;
        app.DispatchCommand(CommandIds.Character(Ann));
        await app.FindSession(Ann)!.ConnectAsync();
        wire.ReceiveGmcp("Room.Info", """{"num":5,"name":"Garden","area":"Yard","coords":"2,-1,0","exits":{"w":3}}""");
        wire.ReceiveGmcp("Room.Info", """{"num":4,"name":"Loft","area":"Keep","coords":"0,0,1","exits":{"d":1}}""");
        wire.ReceiveGmcp("Room.Info", """{"num":3,"name":"Tower","area":"Keep","coords":"1,-1,0","exits":{"w":2,"e":5}}""");
        wire.ReceiveGmcp("Room.Info", """{"num":2,"name":"Gallery","area":"Keep","coords":"0,-1,0","exits":{"s":1,"e":3}}""");
        wire.ReceiveGmcp("Room.Info", Hall);
        app.DispatchCommand("term:map");
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

    private static WorldMap MapOf(SharpMUTermApp app) => app.SessionMapFor(app.FindSession(Ann)!)!.Map;

    private static string Title(SharpMUTermApp app) => MarkupText.Plain(app.MapMarkup(MapId)[0]);

    [Test]
    public async Task PaletteStepsALevelUpAndBackAndTheTitleSaysWhereItIs()
    {
        var (app, _) = await Open();
        await using var _app = app;

        var ids = app.BuildCatalog().Select(i => i.Id).ToList();
        await Assert.That(ids).Contains("map:level-up");
        await Assert.That(ids).Contains("map:next-area");
        await Assert.That(ids).DoesNotContain("map:here").Because("the map already shows where Ann is");
        await Assert.That(Title(app)).Contains("Hall · Keep · z 0");
        await Assert.That(app.MapMarkup(MapId)[0]).Contains(MapRenderer.LookPrefix + "up");

        app.DispatchCommand("map:level-up");

        await Assert.That(app.MapLookOf(MapId)).IsEqualTo(new MapViewpoint(MapOf(app).Room(1)!.AreaId, 1));
        await Assert.That(Title(app)).StartsWith("Viewing · Keep · z 1");
        await Assert.That(Title(app)).Contains("⌂ here");
        await Assert.That(app.BuildCatalog().Select(i => i.Id)).Contains("map:here");

        app.DispatchCommand("map:here");
        await Assert.That(app.MapLookOf(MapId)).IsNull();
        await Assert.That(Title(app)).StartsWith("Hall · Keep · z 0");
    }

    [Test]
    public async Task TheTitlesArrowsAndTheLegendsAreasAreLinksTheMapAnswers()
    {
        var (app, _) = await Open();
        await using var _app = app;

        app.SimulateMapClick(MapId, MapRenderer.LookPrefix + "up");
        await Assert.That(app.MapLookOf(MapId)!.Level).IsEqualTo(1);
        app.SimulateMapClick(MapId, MapRenderer.LookPrefix + "up");
        await Assert.That(app.MapLookOf(MapId)!.Level).IsEqualTo(1).Because("there is no level 2: refused, not moved");
        app.SimulateMapClick(MapId, MapRenderer.LookPrefix + "here");
        await Assert.That(app.MapLookOf(MapId)).IsNull();

        var legend = app.MapMarkup(MapId)[^1];
        await Assert.That(legend).Contains(MapRenderer.LookPrefix + "area:1").Because("Yard is reached by an exit and is a link");
        app.SimulateMapClick(MapId, MapRenderer.LookPrefix + "area:1");
        var yard = MapOf(app).Areas.Values.Single(a => a.Name == "Yard").Id;
        await Assert.That(app.MapLookOf(MapId)).IsEqualTo(new MapViewpoint(yard, 0));
        await Assert.That(Title(app)).StartsWith("Viewing · Yard · z 0");
    }

    [Test]
    public async Task AClickOnAViewedLevelStillWalksToTheRealRoom()
    {
        var (app, wire) = await Open();
        await using var _app = app;
        app.DispatchCommand("map:level-up");
        var before = wire.Lines.Count;

        app.SimulateMapClick(MapId, MapRenderer.LinkPrefix + "4");

        await Assert.That(wire.Lines.Skip(before)).IsEquivalentTo(new[] { "u" });
    }

    [Test]
    public async Task MapCommandsLookByLevelAndAreaAndCanFindARoom()
    {
        var (app, _) = await Open();
        await using var _app = app;

        Type(app, "/map view yard");
        await Assert.That(app.MapLookOf(MapId)!.AreaId).IsEqualTo(MapOf(app).Areas.Values.Single(a => a.Name == "Yard").Id);
        Type(app, "/map here");
        await Assert.That(app.MapLookOf(MapId)).IsNull();
        Type(app, "/map level up");
        await Assert.That(app.MapLookOf(MapId)!.Level).IsEqualTo(1);
        Type(app, "/map level down");
        await Assert.That(app.MapLookOf(MapId)).IsNull().Because("level 0 of the keep is where Ann is");

        Type(app, "/map find loft");
        await Assert.That(app.MapLookOf(MapId)!.Level).IsEqualTo(1).Because("the map shows the room found");
    }

    /// <summary>
    /// A find is a picker, not lines in the output: ⌃P opens listing a <c>Walk to …</c> entry per room found, the client's
    /// own commands, and ⏎ on one walks there. Nothing lands in a pane, where a link would be the server text handler's.
    /// </summary>
    [Test]
    public async Task AFindOpensThePaletteOnTheRoomsFoundAndAnEntryWalksThere()
    {
        var (app, wire) = await Open();
        await using var _app = app;
        var before = app.WindowIds().Sum(w => app.PaneLines(w).Count);

        Type(app, "/map find loft");

        await Assert.That(app.MenuIsOpen).IsTrue();
        var listed = app.Palette.View.Entries.Select(e => e.Item.Id).ToList();
        await Assert.That(listed).IsEquivalentTo(new[] { BuiltInCommands.MapWalk(4) });
        await Assert.That(app.Palette.View.Entries[0].Item.Title).IsEqualTo("Walk to Loft");
        await Assert.That(app.WindowIds().Sum(w => app.PaneLines(w).Count)).IsEqualTo(before).Because("no *** lines in the output");

        var sent = wire.Lines.Count;
        await Assert.That(app.DispatchCommand(BuiltInCommands.MapWalk(4))).IsTrue();
        await Assert.That(wire.Lines.Skip(sent)).IsEquivalentTo(new[] { "u" }).Because("the walk's first step went out");

        // An id the last find did not list is refused, not walked.
        await Assert.That(app.DispatchCommand(BuiltInCommands.MapWalk(1))).IsTrue();
        await Assert.That(app.StatusMarkup).Contains("not in the last /map find");
    }

    [Test]
    public async Task InEditModePageKeysStepLevelsAndTheSelectionComesWith()
    {
        var (app, _) = await Open();
        await using var _app = app;
        app.DispatchCommand("map:edit");
        await Assert.That(app.MapSelection(MapId)).IsEqualTo(1L);

        await Assert.That(app.SimulateMapKey(Key(ConsoleKey.PageUp))).IsTrue();
        await Assert.That(app.MapLookOf(MapId)!.Level).IsEqualTo(1);
        await Assert.That(app.MapSelection(MapId)).IsEqualTo(4L).Because("the keys edit a room that is on screen");

        await Assert.That(app.SimulateMapKey(Key(ConsoleKey.H, 'h'))).IsTrue();
        await Assert.That(app.MapLookOf(MapId)).IsNull();
        await Assert.That(app.MapSelection(MapId)).IsEqualTo(1L);

        await Assert.That(app.SimulateMapKey(Key(ConsoleKey.V, 'v'))).IsTrue();
        await Assert.That(app.MapSelection(MapId)).IsEqualTo(5L).Because("Yard has one room");
    }

    [Test]
    public async Task AHandRenameSurvivesTheServersNextReportUntilItIsReset()
    {
        var (app, wire) = await Open();
        await using var _app = app;

        Type(app, "/map rename 1 Great Hall");
        wire.ReceiveGmcp("Room.Info", Hall);
        await Assert.That(MapOf(app).Room(1)!.Name).IsEqualTo("Great Hall");
        await Assert.That(app.MapDigest()).Contains("|name]");

        app.DispatchCommand("map:edit");
        await Assert.That(string.Join("\n", app.MapMarkup(MapId).Select(MarkupText.Plain))).Contains("mine  name");
        await Assert.That(app.SimulateMapKey(Key(ConsoleKey.R, 'R', shift: true))).IsTrue();

        await Assert.That(MapOf(app).Room(1)!.Name).IsEqualTo("Hall").Because("the server's last report is put back at once");
        await Assert.That(MapOf(app).Room(1)!.HandEdited).IsEmpty();
        app.DispatchCommand("map:undo");
        await Assert.That(MapOf(app).Room(1)!.Name).IsEqualTo("Great Hall");
    }

    [Test]
    public async Task FindRoomPutsTheCommandOnTheCommandLine()
    {
        var (app, _) = await Open();
        await using var _app = app;

        await Assert.That(app.DispatchCommand("map:find")).IsTrue();

        await Assert.That(app.ArmedInputText).IsEqualTo("/map find ");
    }
}
