using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Core.Tests.Mapping;

public class MapRecorderTests
{
    private static RoomObservation Room(long? id, string name, params (string Key, long? To)[] exits) =>
        new(id, name, exits.Select(e => new ObservedExit(e.Key, e.To)).ToList());

    [Test]
    public async Task ServerRoomsAreAddedLinkedAndPlacedByTheWayWalked()
    {
        var recorder = new MapRecorder(new WorldMap());

        recorder.Apply(Room(1, "Hall", ("north", 2)));
        recorder.NoteCommand("n");
        recorder.Apply(Room(2, "Gallery", ("south", 1), ("up", 3)));
        recorder.NoteCommand("say hello"); // not a move
        recorder.NoteCommand("u");
        recorder.Apply(Room(3, "Attic"));

        var map = recorder.Map;
        await Assert.That(recorder.CurrentRoomId).IsEqualTo(3L);
        await Assert.That((map.Room(1)!.X, map.Room(1)!.Y)).IsEqualTo((0, 0));
        await Assert.That((map.Room(2)!.X, map.Room(2)!.Y, map.Room(2)!.Z)).IsEqualTo((0, -1, 0));
        await Assert.That(map.Room(3)!.Z).IsEqualTo(1);
        await Assert.That(map.Room(2)!.Exits["up"]).IsEqualTo(3L);
        await Assert.That(recorder.IsDirty).IsTrue();
    }

    [Test]
    public async Task ServerCoordinatesWin()
    {
        var recorder = new MapRecorder(new WorldMap());
        recorder.Apply(Room(1, "Hall") with { Coordinates = (10, 20, 0) });
        recorder.NoteCommand("e");
        recorder.Apply(Room(2, "Yard") with { Coordinates = (40, 2, 0) });

        await Assert.That((recorder.Map.Room(2)!.X, recorder.Map.Room(2)!.Y)).IsEqualTo((40, 2));
    }

    /// <summary>A room walked into a cell another room holds is pushed on along the same direction.</summary>
    [Test]
    public async Task ACollisionIsNudgedAlongTheDirection()
    {
        var map = new WorldMap();
        map.Add(new MapRoom { Id = 50, X = 1, Y = 0 });
        var recorder = new MapRecorder(map);

        recorder.Apply(Room(1, "Hall"));
        recorder.NoteCommand("east");
        recorder.Apply(Room(2, "Annex"));

        await Assert.That((map.Room(2)!.X, map.Room(2)!.Y)).IsEqualTo((2, 0));
    }

    [Test]
    public async Task ASpecialExitIsLearnedAndPlacedBesideTheRoomBefore()
    {
        var recorder = new MapRecorder(new WorldMap());
        recorder.Apply(Room(1, "Hall", ("enter portal", null)));
        recorder.NoteCommand("enter portal");
        recorder.Apply(Room(9, "Elsewhere"));

        var hall = recorder.Map.Room(1)!;
        await Assert.That(hall.SpecialExits["enter portal"]).IsEqualTo(9L);
        await Assert.That(hall.Stubs).IsEmpty();
        var elsewhere = recorder.Map.Room(9)!;
        await Assert.That(Math.Max(Math.Abs(elsewhere.X), Math.Abs(elsewhere.Y))).IsEqualTo(1);
    }

    /// <summary>
    /// The text source gives no numbers. Walking out and back finds the first room again through the link the
    /// walk made, rather than adding a second copy of it; a <c>look</c> finds the same room by name.
    /// </summary>
    [Test]
    public async Task TextRoomsAreRecognisedByTheWayBack()
    {
        var recorder = new MapRecorder(new WorldMap());

        recorder.Apply(Room(null, "Square", ("north", null)));
        recorder.NoteCommand("n");
        recorder.Apply(Room(null, "Market", ("south", null)));
        recorder.NoteCommand("s");
        recorder.Apply(Room(null, "Square", ("north", null)));
        recorder.Apply(Room(null, "Square", ("north", null))); // look

        var map = recorder.Map;
        await Assert.That(map.Rooms.Count).IsEqualTo(2);
        await Assert.That(recorder.CurrentRoom!.Name).IsEqualTo("Square");
        await Assert.That(map.Room(1)!.Exits["north"]).IsEqualTo(2L);
        await Assert.That(map.Room(2)!.Exits["south"]).IsEqualTo(1L);
        await Assert.That(PathFinder.Find(map, 1, 2)!.Single().Command).IsEqualTo("n");
    }

    [Test]
    public async Task AnExitsUpdateChangesTheCurrentRoomWithoutMoving()
    {
        var recorder = new MapRecorder(new WorldMap());
        recorder.Apply(Room(1, "Hall", ("north", 2)));

        recorder.Apply(Room(null, string.Empty, ("east", 3)) with { ExitsOnly = true });

        await Assert.That(recorder.CurrentRoomId).IsEqualTo(1L);
        await Assert.That(recorder.Map.Room(1)!.Exits.Keys).IsEquivalentTo(new[] { "east" });
    }
}
