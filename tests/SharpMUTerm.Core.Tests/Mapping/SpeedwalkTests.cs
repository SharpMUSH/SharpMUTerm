using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Core.Tests.Mapping;

public class SpeedwalkTests
{
    /// <summary>What a game reports on arriving: the room and the exits it already has, so the map is left as it is.</summary>
    private static void Arrive(MapRecorder recorder, long id) =>
        recorder.Apply(new RoomObservation(
            id,
            recorder.Map.Room(id)?.Name ?? "Somewhere",
            (recorder.Map.Room(id)?.Exits ?? new Dictionary<string, long>()).Select(e => new ObservedExit(e.Key, e.Value)).ToList()));

    /// <summary>Hall (1) — north — Gallery (2) — east — Tower (3).</summary>
    private static (MapRecorder Recorder, List<string> Sent, Speedwalk Walk, ManualTimeProvider Clock) Walker(
        string? failure = null)
    {
        var map = new WorldMap();
        map.Add(new MapRoom { Id = 1, Name = "Hall", Exits = { ["north"] = 2 } });
        map.Add(new MapRoom { Id = 2, Name = "Gallery", Y = -1, Exits = { ["south"] = 1, ["east"] = 3 } });
        map.Add(new MapRoom { Id = 3, Name = "Tower", X = 1, Y = -1, Exits = { ["west"] = 2 } });
        var recorder = new MapRecorder(map);
        Arrive(recorder, 1);
        var sent = new List<string>();
        var clock = new ManualTimeProvider();
        var walk = new Speedwalk(recorder, c => { sent.Add(c); return Task.CompletedTask; }, failure, clock);
        return (recorder, sent, walk, clock);
    }

    [Test]
    public async Task EachStepWaitsForTheRoomBefore()
    {
        var (recorder, sent, walk, _) = Walker();
        string? last = "unset";
        walk.Changed += (_, reason) => last = reason;

        walk.Start(PathFinder.Find(recorder.Map, 1, 3)!);
        await Assert.That(sent).IsEquivalentTo(new[] { "n" });

        Arrive(recorder, 2);
        await Assert.That(sent).IsEquivalentTo(new[] { "n", "e" });
        await Assert.That(walk.Remaining).IsEqualTo(1);

        Arrive(recorder, 3);
        await Assert.That(walk.IsWalking).IsFalse();
        await Assert.That(last).IsEqualTo("arrived");
    }

    [Test]
    public async Task ALookInTheSameRoomDoesNotStopIt()
    {
        var (recorder, sent, walk, _) = Walker();
        walk.Start(PathFinder.Find(recorder.Map, 1, 3)!);

        Arrive(recorder, 1);

        await Assert.That(walk.IsWalking).IsTrue();
        await Assert.That(sent.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ArrivingOffThePathStops()
    {
        var (recorder, _, walk, _) = Walker();
        recorder.Map.Add(new MapRoom { Id = 9, Name = "Pit" });
        walk.Start(PathFinder.Find(recorder.Map, 1, 3)!);

        Arrive(recorder, 9);

        await Assert.That(walk.IsWalking).IsFalse();
    }

    [Test]
    public async Task TheFailurePatternStopsAtOnce()
    {
        var (recorder, _, walk, _) = Walker("can't go that way");
        string? last = null;
        walk.Changed += (_, reason) => last = reason;
        walk.Start(PathFinder.Find(recorder.Map, 1, 3)!);

        walk.Observe("You can't go that way.");

        await Assert.That(walk.IsWalking).IsFalse();
        await Assert.That(last).IsEqualTo("stopped: You can't go that way.");
    }

    [Test]
    public async Task AStepThatNeverArrivesTimesOut()
    {
        var (recorder, _, walk, clock) = Walker();
        walk.Start(PathFinder.Find(recorder.Map, 1, 3)!);

        clock.Advance(Speedwalk.StepTimeout - TimeSpan.FromSeconds(1));
        await Assert.That(walk.IsWalking).IsTrue();

        clock.Advance(TimeSpan.FromSeconds(2));
        await Assert.That(walk.IsWalking).IsFalse();
    }

    [Test]
    public async Task AnEmptyPathIsAlreadyThere()
    {
        var (_, sent, walk, _) = Walker();
        string? last = null;
        walk.Changed += (_, reason) => last = reason;

        walk.Start(Array.Empty<PathStep>());

        await Assert.That(sent).IsEmpty();
        await Assert.That(last).IsEqualTo("already there");
    }
}
