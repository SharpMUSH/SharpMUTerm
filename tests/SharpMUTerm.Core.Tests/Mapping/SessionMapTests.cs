using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Core.Tests.Mapping;

public class SessionMapTests
{
    private static SessionMap Map(List<string> sent)
    {
        var map = new WorldMap();
        map.Add(new MapRoom { Id = 1, Name = "Hall", Exits = { ["north"] = 2, ["east"] = 5 } });
        map.Add(new MapRoom { Id = 2, Name = "Square", Y = -1, Exits = { ["south"] = 1 } });
        map.Add(new MapRoom { Id = 5, Name = "Corridor", X = 1, Exits = { ["west"] = 1, ["east"] = 6 } });
        map.Add(new MapRoom { Id = 6, Name = "Corridor", X = 2, Exits = { ["west"] = 5, ["east"] = 7 } });
        map.Add(new MapRoom { Id = 7, Name = "Square", X = 3, Exits = { ["west"] = 6 } });
        var recorder = new MapRecorder(map);
        recorder.Apply(new RoomObservation(1, "Hall", [new ObservedExit("north", 2), new ObservedExit("east", 5)]));
        return new SessionMap(recorder, new Speedwalk(recorder, c => { sent.Add(c); return Task.CompletedTask; }));
    }

    [Test]
    public async Task ANameFindsTheNearestRoomCalledThat()
    {
        using var map = Map(new List<string>());

        await Assert.That(map.Find("square")!.Id).IsEqualTo(2L);
        await Assert.That(map.Find("7")!.Id).IsEqualTo(7L);
        await Assert.That(map.Find("Nowhere")).IsNull();
    }

    [Test]
    public async Task WalkToStartsTheWalkOrSaysWhy()
    {
        var sent = new List<string>();
        using var map = Map(sent);

        await Assert.That(map.WalkTo("Nowhere")).IsEqualTo("no room on the map is called 'Nowhere'");
        await Assert.That(map.WalkTo("7")).IsNull();
        await Assert.That(sent).IsEquivalentTo(new[] { "e" });
        await Assert.That(map.Walk.Destination).IsEqualTo(7L);
    }

    [Test]
    public async Task ALockedRoomIsWalkedAround()
    {
        using var map = Map(new List<string>());
        map.SetLocked(5, true);

        await Assert.That(map.Map.Room(5)!.Locked).IsTrue();
        await Assert.That(map.WalkTo("7")).IsEqualTo("the map knows no way there");
        await Assert.That(map.Recorder.IsDirty).IsTrue();
    }

    [Test]
    public async Task CurrentNamesTheRoomAndArea()
    {
        using var map = Map(new List<string>());
        map.Map.Room(1)!.AreaId = map.Map.AreaNamed("Keep").Id;

        await Assert.That(map.Current).IsEqualTo((1L, "Hall", (string?)"Keep"));
    }
}
