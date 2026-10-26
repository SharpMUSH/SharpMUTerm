using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Mapping;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>A connected session's GMCP rooms reach its map, and the map is kept in the store the app was given.</summary>
[NotInParallel]
public class MapRecordingTests
{
    private const string Ann = "Convergence.Ann";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    [Test]
    public async Task GmcpRoomsBuildTheSessionsMap()
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
        var store = new MapStore();
        await using var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(160, 32), maps: store);
        app.TelnetFactory = _ => wire;
        await Assert.That(app.DispatchCommand(CommandIds.Character(Ann))).IsTrue();
        var session = app.FindSession(Ann)!;
        await session.ConnectAsync();

        wire.ReceiveGmcp("Room.Info", """{"num":1,"name":"Hall","exits":{"n":2}}""");
        await session.SendUserInputAsync("n");
        wire.ReceiveGmcp("Room.Info", """{"num":2,"name":"Gallery","exits":{"s":1}}""");

        var recorder = app.MapFor(session)!;
        await Assert.That(recorder.CurrentRoom!.Name).IsEqualTo("Gallery");
        await Assert.That(store.For("convergence.example", 4201).Rooms.Count).IsEqualTo(2);
        await Assert.That(PathFinder.Find(recorder.Map, 2, 1)!.Single().Command).IsEqualTo("s");
    }
}
