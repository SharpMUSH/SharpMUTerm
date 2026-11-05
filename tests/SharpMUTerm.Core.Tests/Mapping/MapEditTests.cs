using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Core.Tests.Mapping;

/// <summary>Edits by hand: the map's own operations, the recorder's undo, and an exit waiting for the next room.</summary>
public class MapEditTests
{
    private static RoomObservation Room(long? id, string name, params (string Key, long? To)[] exits) =>
        new(id, name, exits.Select(e => new ObservedExit(e.Key, e.To)).ToList());

    private static MapRecorder Recorded()
    {
        var recorder = new MapRecorder(new WorldMap());
        recorder.Apply(Room(1, "Hall", ("north", 2)) with { Area = "Keep" });
        recorder.NoteCommand("n");
        recorder.Apply(Room(2, "Gallery", ("south", 1)) with { Area = "Keep" });
        recorder.MarkSaved();
        return recorder;
    }

    [Test]
    public async Task EachEditChangesOnlyWhatItSaysAndRefusesNonsense()
    {
        var map = Recorded().Map;

        await Assert.That(map.Rename(1, "  Great Hall ")).IsTrue();
        await Assert.That(map.Room(1)!.Name).IsEqualTo("Great Hall");
        await Assert.That(map.Rename(1, "   ")).IsFalse();
        await Assert.That(map.SetArea(1, "Tower")).IsTrue();
        await Assert.That(map.Areas[map.Room(1)!.AreaId!.Value].Name).IsEqualTo("Tower");
        await Assert.That(map.SetArea(1, "")).IsTrue();
        await Assert.That(map.Room(1)!.AreaId).IsNull();
        await Assert.That(map.SetWeight(1, 0)).IsFalse();
        await Assert.That(map.SetWeight(1, 7)).IsTrue();
        await Assert.That(map.SetExit(1, "ne", 2)).IsTrue();
        await Assert.That(map.Room(1)!.Exits["northeast"]).IsEqualTo(2L);
        await Assert.That(map.SetExit(1, "climb rope", 2)).IsTrue();
        await Assert.That(map.Room(1)!.SpecialExits["climb rope"]).IsEqualTo(2L);
        await Assert.That(map.SetExit(1, "e", 99)).IsFalse().Because("no room 99");
        await Assert.That(map.RemoveExit(1, "northeast")).IsTrue();
        await Assert.That(map.Move(1, 1, 0, 1)).IsTrue();
        await Assert.That((map.Room(1)!.X, map.Room(1)!.Z)).IsEqualTo((1, 1));
        await Assert.That(map.SetSymbol(1, "$")).IsTrue();
        await Assert.That(map.SetSymbol(1, "[")).IsFalse().Because("a bracket would be markup");
        await Assert.That(map.SetNote(1, "a note\u0007")).IsTrue();
        await Assert.That(map.Room(1)!.Note).IsEqualTo("a note");
    }

    [Test]
    public async Task DeletingARoomTurnsTheExitsThatLedThereBackIntoStubs()
    {
        var map = Recorded().Map;

        await Assert.That(map.Remove(2)).IsTrue();
        await Assert.That(map.Room(2)).IsNull();
        await Assert.That(map.Room(1)!.Exits.ContainsKey("north")).IsFalse();
        await Assert.That(map.Room(1)!.Stubs).Contains("north");
    }

    [Test]
    public async Task AnEditIsKeptForUndoMarksTheMapDirtyAndUndoPutsEveryTouchedRoomBack()
    {
        var recorder = Recorded();
        var changes = 0;
        recorder.Changed += (_, _) => changes++;

        await Assert.That(recorder.Edit("delete Gallery", [2, 1], m => m.Remove(2))).IsTrue();
        await Assert.That(recorder.IsDirty).IsTrue();
        await Assert.That(recorder.UndoLabel).IsEqualTo("delete Gallery");

        await Assert.That(recorder.Undo()).IsEqualTo("delete Gallery");
        await Assert.That(recorder.Map.Room(2)!.Name).IsEqualTo("Gallery");
        await Assert.That(recorder.Map.Room(1)!.Exits["north"]).IsEqualTo(2L);
        await Assert.That(recorder.Map.Room(1)!.Stubs).DoesNotContain("north");
        await Assert.That(recorder.Undo()).IsNull();
        await Assert.That(changes).IsEqualTo(2);
    }

    [Test]
    public async Task AnEditThatChangesNothingIsNotKept()
    {
        var recorder = Recorded();

        await Assert.That(recorder.Edit("rename", [1], m => m.Rename(1, "Hall"))).IsFalse();
        await Assert.That(recorder.UndoCount).IsEqualTo(0);
        await Assert.That(recorder.IsDirty).IsFalse();
    }

    [Test]
    public async Task UndoKeepsTheLastFewEdits()
    {
        var recorder = Recorded();
        for (var i = 0; i < MapRecorder.UndoDepth + 5; i++)
        {
            recorder.Edit($"weight {i}", [1], m => m.SetWeight(1, i + 2));
        }

        await Assert.That(recorder.UndoCount).IsEqualTo(MapRecorder.UndoDepth);
    }

    [Test]
    public async Task UndoingANewAreaTakesTheAreaAwayToo()
    {
        var recorder = Recorded();
        var areas = recorder.Map.Areas.Count;

        recorder.Edit("area", [1], m => m.SetArea(1, "Crypt"));
        await Assert.That(recorder.Map.Areas.Count).IsEqualTo(areas + 1);
        recorder.Undo();

        await Assert.That(recorder.Map.Areas.Count).IsEqualTo(areas);
        await Assert.That(recorder.Map.Areas[recorder.Map.Room(1)!.AreaId!.Value].Name).IsEqualTo("Keep");
    }

    /// <summary>Mudlet's "the room I go to next": the exit is linked to the next room walked into, and that is undoable too.</summary>
    [Test]
    public async Task AnExitWaitsForTheNextRoomWalkedInto()
    {
        var recorder = Recorded();

        await Assert.That(recorder.LinkNext(1, "climb")).IsTrue();
        await Assert.That(recorder.PendingLink).IsEqualTo((1L, "climb"));
        recorder.Apply(Room(2, "Gallery", ("south", 1))); // the next room reported, which is not the one the exit leaves
        await Assert.That(recorder.Map.Room(1)!.SpecialExits["climb"]).IsEqualTo(2L);
        await Assert.That(recorder.PendingLink).IsNull();

        recorder.Undo();
        await Assert.That(recorder.Map.Room(1)!.SpecialExits.ContainsKey("climb")).IsFalse();
        await Assert.That(recorder.PendingLink).IsEqualTo((1L, "climb"));
    }

    [Test]
    public async Task AWaitingExitIsNotLinkedToItsOwnRoom()
    {
        var recorder = Recorded();
        recorder.LinkNext(2, "down");

        recorder.Apply(Room(2, "Gallery", ("south", 1)));

        await Assert.That(recorder.PendingLink).IsEqualTo((2L, "down"));
        await Assert.That(recorder.Map.Room(2)!.Exits.ContainsKey("down")).IsFalse();
    }

    [Test]
    public async Task ANoteIsSavedWithTheMap()
    {
        var map = Recorded().Map;
        map.SetNote(1, "the fountain hides a key");

        var read = MapStore.Deserialize(MapStore.Serialize(map, "x:1"));

        await Assert.That(read.Room(1)!.Note).IsEqualTo("the fountain hides a key");
    }
}
