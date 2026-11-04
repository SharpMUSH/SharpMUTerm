using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Core.Tests.Mapping;

/// <summary>
/// A hand edit survives the server's next report of the room: the fields a player changed are recorded per room, kept
/// with the map, left alone by the recorder, and given back to the server only on "reset to server".
/// </summary>
public class HandEditTests
{
    private static RoomObservation Room(long? id, string name, params (string Key, long? To)[] exits) =>
        new(id, name, exits.Select(e => new ObservedExit(e.Key, e.To)).ToList());

    private static RoomObservation Hall => Room(1, "Hall", ("north", 2)) with { Area = "Keep", Coordinates = (0, 0, 0) };

    private static RoomObservation Gallery => Room(2, "Gallery", ("south", 1)) with { Area = "Keep", Coordinates = (0, -1, 0) };

    private static MapRecorder Recorded()
    {
        var recorder = new MapRecorder(new WorldMap());
        recorder.Apply(Hall);
        recorder.NoteCommand("n");
        recorder.Apply(Gallery);
        return recorder;
    }

    /// <summary>Back to the hall, as a GMCP game reports it: everything about the room again.</summary>
    private static void Revisit(MapRecorder recorder)
    {
        recorder.NoteCommand("s");
        recorder.Apply(Hall);
    }

    [Test]
    public async Task EachEditMarksTheFieldItChangedAndNothingElse()
    {
        var map = Recorded().Map;

        await Assert.That(map.Room(1)!.HandEdited).IsEmpty().Because("recording is not editing");
        map.Rename(1, "Great Hall");
        map.SetArea(1, "Tower");
        await Assert.That(map.Room(1)!.HandEdited.Order()).IsEquivalentTo(new[] { MapFields.Area, MapFields.Name });
        map.SetLocked(1, true);
        map.SetNote(1, "cold");
        map.SetWeight(1, 4);
        await Assert.That(map.Room(1)!.HandEdited.Count).IsEqualTo(2).Because("locks, notes and weights were never the server's");
        map.SetExit(1, "ne", 2);
        map.Move(1, 1, 0, 0);
        await Assert.That(map.Room(1)!.HandEdited.Order()).IsEquivalentTo(new[] { MapFields.Area, MapFields.Exits, MapFields.Name, MapFields.Position });
        await Assert.That(map.Rename(1, "Great Hall")).IsFalse();
    }

    [Test]
    public async Task TheServersNextReportLeavesHandEditedFieldsAlone()
    {
        var recorder = Recorded();
        var map = recorder.Map;
        map.Rename(1, "Great Hall");
        map.SetArea(1, "Tower");
        map.SetExit(1, "climb rope", 2);
        map.Move(1, 3, 2, 0);

        Revisit(recorder);

        var hall = map.Room(1)!;
        await Assert.That(hall.Name).IsEqualTo("Great Hall");
        await Assert.That(map.Areas[hall.AreaId!.Value].Name).IsEqualTo("Tower");
        await Assert.That(hall.SpecialExits.ContainsKey("climb rope")).IsTrue();
        await Assert.That((hall.X, hall.Y, hall.Z)).IsEqualTo((3, 2, 0));
        await Assert.That(recorder.CurrentRoomId).IsEqualTo(1L).Because("the character still moved");
    }

    [Test]
    public async Task AFieldNobodyEditedStillFollowsTheServer()
    {
        var recorder = Recorded();
        recorder.Map.Rename(1, "Great Hall");

        recorder.NoteCommand("s");
        recorder.Apply(Room(1, "Hall", ("north", 2), ("east", 3)) with { Area = "Keep", Coordinates = (5, 5, 0) });

        var hall = recorder.Map.Room(1)!;
        await Assert.That(hall.Name).IsEqualTo("Great Hall");
        await Assert.That((hall.X, hall.Y)).IsEqualTo((5, 5));
        await Assert.That(hall.Exits.ContainsKey("east")).IsTrue();
    }

    [Test]
    public async Task AnExitsOnlyUpdateLeavesHandEditedExitsAlone()
    {
        var recorder = Recorded();
        recorder.Map.RemoveExit(2, "south");

        recorder.Apply(Room(2, string.Empty, ("south", 1), ("west", 9)) with { ExitsOnly = true });

        await Assert.That(recorder.Map.Room(2)!.Exits).IsEmpty();
    }

    [Test]
    public async Task ResetToServerPutsBackWhatTheServerLastSaidAndCanBeUndone()
    {
        var recorder = Recorded();
        var map = recorder.Map;
        recorder.Edit("rename", [1], m => m.Rename(1, "Great Hall"));
        recorder.Edit("move", [1], m => m.Move(1, 2, 0, 0));

        await Assert.That(recorder.ResetToServer(1, "reset Hall")).IsTrue();
        await Assert.That(map.Room(1)!.Name).IsEqualTo("Hall");
        await Assert.That(map.Room(1)!.X).IsEqualTo(0);
        await Assert.That(map.Room(1)!.HandEdited).IsEmpty();
        await Assert.That(recorder.ResetToServer(1, "again")).IsFalse().Because("nothing is edited by hand now");

        await Assert.That(recorder.Undo()).IsEqualTo("reset Hall");
        await Assert.That(map.Room(1)!.Name).IsEqualTo("Great Hall");
        await Assert.That(map.Room(1)!.HandEdited).Contains(MapFields.Name);
    }

    [Test]
    public async Task ResetWithNoReportThisRunLetsTheNextReportSetTheFields()
    {
        var recorder = new MapRecorder(MapStore.Deserialize(MapStore.Serialize(Recorded().Map, "x:1")));
        recorder.Map.Rename(1, "Great Hall");

        await Assert.That(recorder.HasServerReport(1)).IsFalse();
        await Assert.That(recorder.ResetToServer(1, "reset")).IsTrue();
        await Assert.That(recorder.Map.Room(1)!.Name).IsEqualTo("Great Hall").Because("nothing to put back yet");

        recorder.Apply(Hall);
        await Assert.That(recorder.Map.Room(1)!.Name).IsEqualTo("Hall");
    }

    [Test]
    public async Task UndoingAnEditTakesItsProtectionWithIt()
    {
        var recorder = Recorded();
        recorder.Edit("rename", [1], m => m.Rename(1, "Great Hall"));
        recorder.Undo();

        await Assert.That(recorder.Map.Room(1)!.HandEdited).IsEmpty();
    }

    [Test]
    public async Task HandEditsAreKeptWithTheMapInVersionTwo()
    {
        var map = Recorded().Map;
        map.Rename(1, "Great Hall");
        map.Move(2, 0, -1, 0);

        var text = MapStore.Serialize(map, "example.org:4000");
        var back = MapStore.Deserialize(text);

        await Assert.That(text).Contains("\"version\": 2");
        await Assert.That(back.Room(1)!.HandEdited).IsEquivalentTo(new[] { MapFields.Name });
        await Assert.That(back.Room(2)!.HandEdited).IsEquivalentTo(new[] { MapFields.Position });
        await Assert.That(back.Room(1)!.HandEdited.Contains("NAME")).IsFalse().Because("the comparer is ordinal again");
    }

    [Test]
    public async Task AVersionOneFileReadsWithNothingEditedByHandAndIsWrittenBackAsVersionTwo()
    {
        const string v1 = """
            {"version":1,"endpoint":"example.org:4000","areas":[{"id":1,"name":"Keep"}],
             "rooms":[{"id":1,"name":"Hall","areaId":1,"x":0,"y":0,"z":0,"exits":{"north":2}},
                      {"id":2,"name":"Gallery","areaId":1,"x":0,"y":-1,"z":0,"exits":{"south":1}}]}
            """;

        var map = MapStore.Deserialize(v1);

        await Assert.That(map.Rooms.Count).IsEqualTo(2);
        await Assert.That(map.Rooms.Values.All(r => r.HandEdited.Count == 0)).IsTrue();
        await Assert.That(MapStore.Serialize(map, "example.org:4000")).Contains("\"version\": 2");

        var recorder = new MapRecorder(map);
        recorder.Apply(Room(1, "Hall of Echoes", ("north", 2)) with { Area = "Keep" });
        await Assert.That(map.Room(1)!.Name).IsEqualTo("Hall of Echoes").Because("an old map has no hand edits to protect");
    }

    [Test]
    public async Task AFileFromALaterClientWithUnknownFieldsInHandEditedKeepsOnlyTheKnownOnes()
    {
        const string text = """{"version":2,"rooms":[{"id":1,"name":"Hall","handEdited":["name","colour"]}]}""";

        var map = MapStore.Deserialize(text);

        await Assert.That(map.Room(1)!.HandEdited).IsEquivalentTo(new[] { MapFields.Name });
    }
}
