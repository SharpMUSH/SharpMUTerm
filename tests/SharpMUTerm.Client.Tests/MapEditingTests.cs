using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Client.Tests;

/// <summary>The one action model both hosts' map edits go through, and what the map view says about rooms.</summary>
public class MapEditingTests
{
    private static MapRecorder Recorded()
    {
        var recorder = new MapRecorder(new WorldMap());
        recorder.Apply(new RoomObservation(1, "Hall", [new ObservedExit("north", 2), new ObservedExit("east", 3)]) with { Area = "Keep" });
        recorder.NoteCommand("n");
        recorder.Apply(new RoomObservation(2, "Gallery", [new ObservedExit("south", 1)]) with { Area = "Keep" });
        recorder.NoteCommand("s");
        recorder.Apply(new RoomObservation(1, "Hall", [new ObservedExit("north", 2), new ObservedExit("east", 3)]) with { Area = "Keep" });
        return recorder;
    }

    private static long? Resolve(MapRecorder recorder, string? name) =>
        name is null ? recorder.CurrentRoomId : recorder.Map.Rooms.Values.FirstOrDefault(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Id;

    [Test]
    public async Task EveryCommandVerbParsesIntoItsEdit()
    {
        var recorder = Recorded();
        (MapEdit? Edit, string? Problem) Parse(string text) => MapEditing.Parse(text, n => Resolve(recorder, n));

        await Assert.That(Parse("rename 2 Long Gallery").Edit).IsEqualTo(MapEdit.Rename(2, "Long Gallery"));
        await Assert.That(Parse("area 2").Edit).IsEqualTo(MapEdit.Area(2, string.Empty));
        await Assert.That(Parse("weight 2 5").Edit).IsEqualTo(MapEdit.Weight(2, 5));
        await Assert.That(Parse("exit 2 climb rope 1").Edit).IsEqualTo(MapEdit.AddExit(2, "climb rope", 1));
        await Assert.That(Parse("exit 2 down next").Edit).IsEqualTo(MapEdit.LinkNext(2, "down"));
        await Assert.That(Parse("unexit 2 south").Edit).IsEqualTo(MapEdit.RemoveExit(2, "south"));
        await Assert.That(Parse("move 2 1 -1").Edit).IsEqualTo(MapEdit.Move(2, 1, -1));
        await Assert.That(Parse("delete 2").Edit).IsEqualTo(MapEdit.Delete(2));
        await Assert.That(Parse("note 2 a draught").Edit).IsEqualTo(MapEdit.Note(2, "a draught"));
        await Assert.That(Parse("symbol 2 $").Edit).IsEqualTo(MapEdit.Symbol(2, "$"));
        await Assert.That(Parse("lock gallery").Edit).IsEqualTo(MapEdit.Lock(2, true));
        await Assert.That(Parse("unlock").Edit).IsEqualTo(MapEdit.Lock(1, false));
        await Assert.That(Parse("undo").Edit).IsEqualTo(MapEdit.Undo);
        await Assert.That(Parse("rename hall Foo").Problem!).Contains("a room is its number");
        await Assert.That(Parse("weight 2 heavy").Edit).IsNull();
        await Assert.That(Parse("paint 2").Problem!).StartsWith("use /map");
    }

    [Test]
    public async Task ApplyAnswersInASentenceAndRefusesWhatItCannotDo()
    {
        var recorder = Recorded();

        await Assert.That(MapEditing.Apply(recorder, MapEdit.Rename(2, "Long Gallery"))).IsEqualTo(new MapEditResult(true, "renamed Gallery to Long Gallery"));
        await Assert.That(MapEditing.Apply(recorder, MapEdit.Rename(2, "Long Gallery")).Done).IsFalse();
        await Assert.That(MapEditing.Apply(recorder, MapEdit.Weight(2, 0)).Done).IsFalse();
        await Assert.That(MapEditing.Apply(recorder, MapEdit.AddExit(2, "up", 99)).Done).IsFalse();
        await Assert.That(MapEditing.Apply(recorder, MapEdit.Symbol(2, "ab")).Done).IsFalse();
        await Assert.That(MapEditing.Apply(recorder, MapEdit.Delete(1)).Message).Contains("walk out of it first");
        await Assert.That(MapEditing.Apply(recorder, MapEdit.Delete(42)).Done).IsFalse();

        await Assert.That(MapEditing.Apply(recorder, MapEdit.Delete(2)).Done).IsTrue();
        await Assert.That(recorder.Map.Room(1)!.Stubs).Contains("north");
        await Assert.That(MapEditing.Apply(recorder, MapEdit.Undo).Message).IsEqualTo("undid: delete Long Gallery");
        await Assert.That(recorder.Map.Room(2)!.Name).IsEqualTo("Long Gallery");
        await Assert.That(MapEditing.Apply(recorder, MapEdit.Undo).Done).IsTrue();
        await Assert.That(recorder.Map.Room(2)!.Name).IsEqualTo("Gallery");
        await Assert.That(MapEditing.Apply(recorder, MapEdit.Undo)).IsEqualTo(new MapEditResult(false, "nothing to undo"));
    }

    [Test]
    public async Task TheViewNamesTheExitsOfTheRoomYouAreInAndTheRoomBeingEdited()
    {
        var recorder = Recorded();
        recorder.Map.SetNote(2, "draughty");

        var plain = MapViewModel.From(recorder.Map, 1, [], null);
        await Assert.That(plain.Editing).IsFalse();
        await Assert.That(plain.Nearby).Contains(new MapViewExitName("n", 2, "Gallery"));
        await Assert.That(plain.Nearby.Any(e => e.Exit == "e" && e.Name is null)).IsTrue();

        var editing = MapViewModel.From(recorder.Map, 1, [], null, selected: 2, pending: (2, "down"), undo: "rename");
        await Assert.That(editing.Editing).IsTrue();
        await Assert.That(editing.Selected!.Note).IsEqualTo("draughty");
        await Assert.That(editing.Rooms.Single(r => r.Id == 2).Selected).IsTrue();
        await Assert.That(editing.Rooms.Single(r => r.Id == 2).Noted).IsTrue();
        await Assert.That(editing.Pending).IsNotNull();
        await Assert.That(editing).IsEqualTo(MapViewModel.From(recorder.Map, 1, [], null, selected: 2, pending: (2, "down"), undo: "rename"));
        await Assert.That(editing).IsNotEqualTo(MapViewModel.From(recorder.Map, 1, [], null, selected: 1, pending: (2, "down"), undo: "rename"));
    }

    [Test]
    public async Task AnArrowSelectsTheNearestRoomThatWay()
    {
        var map = new WorldMap();
        map.Add(new MapRoom { Id = 1, Name = "A", X = 0, Y = 0 });
        map.Add(new MapRoom { Id = 2, Name = "B", X = 2, Y = 0 });
        map.Add(new MapRoom { Id = 3, Name = "C", X = 1, Y = 1 });
        map.Add(new MapRoom { Id = 4, Name = "D", X = 0, Y = -3 });
        var view = MapViewModel.From(map, 1, [], null);

        await Assert.That(MapEditing.Neighbour(view, 1, 1, 0)).IsEqualTo(2L);
        await Assert.That(MapEditing.Neighbour(view, 1, 0, 1)).IsEqualTo(3L);
        await Assert.That(MapEditing.Neighbour(view, 1, 0, -1)).IsEqualTo(4L);
        await Assert.That(MapEditing.Neighbour(view, 1, -1, 0)).IsNull();
    }

    [Test]
    public async Task TheRoomMenuOffersOnlyWhatCanBeDoneToThatRoom()
    {
        var here = new MapViewRoom(1, "Hall", 0, 0, true, false, false);
        var there = here with { Id = 2, Current = false, Locked = true };

        var mine = MapEditing.Menu(here, null).Entries;
        await Assert.That(mine.First().Enabled).IsFalse().Because("you cannot walk to the room you are in");
        await Assert.That(mine.Single(e => e.Label == "Delete room").Enabled).IsFalse();
        await Assert.That(mine.Any(e => e.Label.StartsWith("Undo", StringComparison.Ordinal))).IsFalse();

        var theirs = MapEditing.Menu(there, "rename Hall").Entries;
        await Assert.That(theirs.Any(e => e.Label == "Unlock")).IsTrue();
        await Assert.That(theirs.Last().Command).IsEqualTo(MapEditing.UndoVerb);
        foreach (var entry in theirs.Where(e => e.Command is not null))
        {
            var needsWords = MapEditing.TextVerbs.Contains(entry.Command!) || entry.Command == MapEditing.WalkVerb;
            await Assert.That(needsWords || MapEditing.EditFor(entry.Command!, 2) is not null).IsTrue().Because(entry.Label);
        }
    }
}
