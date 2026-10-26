using System.Diagnostics;
using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Core.Tests.Mapping;

public class PathFinderTests
{
    /// <summary>A row of rooms 1 → 2 → … → <paramref name="count"/>, each east of the last, with exits both ways.</summary>
    private static WorldMap Corridor(int count)
    {
        var map = new WorldMap();
        for (var id = 1; id <= count; id++)
        {
            var room = map.Add(new MapRoom { Id = id, Name = $"Room {id}", X = id });
            if (id > 1)
            {
                room.Exits["west"] = id - 1;
            }

            if (id < count)
            {
                room.Exits["east"] = id + 1;
            }
        }

        return map;
    }

    [Test]
    public async Task TheShortestWalkIsItsCommands()
    {
        var map = Corridor(4);

        var path = PathFinder.Find(map, 1, 4);

        await Assert.That(path!.Select(s => s.Command)).IsEquivalentTo(new[] { "e", "e", "e" });
        await Assert.That(path!.Select(s => s.To)).IsEquivalentTo(new long[] { 2, 3, 4 });
        await Assert.That(PathFinder.Find(map, 4, 4)).IsEmpty();
    }

    /// <summary>A square: 1 east to 2 south to 4, or 1 south to 3 east to 4. A heavy room 2 sends the walk round by 3.</summary>
    [Test]
    public async Task AHeavyRoomIsWalkedAround()
    {
        var map = new WorldMap();
        map.Add(new MapRoom { Id = 1, Exits = { ["east"] = 2, ["south"] = 3 } });
        map.Add(new MapRoom { Id = 2, Exits = { ["south"] = 4 } });
        map.Add(new MapRoom { Id = 3, Exits = { ["east"] = 4 } });
        map.Add(new MapRoom { Id = 4 });

        map.Room(3)!.Weight = 5;
        await Assert.That(PathFinder.Find(map, 1, 4)!.Select(s => s.Command)).IsEquivalentTo(new[] { "e", "s" });

        map.Room(3)!.Weight = 1;
        map.Room(2)!.Weight = 5;
        await Assert.That(PathFinder.Find(map, 1, 4)!.Select(s => s.Command)).IsEquivalentTo(new[] { "s", "e" });

        // An exit's own weight wins over the room it enters.
        map.Room(1)!.ExitWeights["east"] = 1;
        map.Room(2)!.ExitWeights["south"] = 1;
        map.Room(1)!.ExitWeights["south"] = 3;
        await Assert.That(PathFinder.Find(map, 1, 4)!.Select(s => s.Command)).IsEquivalentTo(new[] { "e", "s" });
    }

    [Test]
    public async Task LockedRoomsAndExitsAreNeverTaken()
    {
        var map = new WorldMap();
        map.Add(new MapRoom { Id = 1, Exits = { ["east"] = 2, ["south"] = 3 } });
        map.Add(new MapRoom { Id = 2, Exits = { ["south"] = 4 } });
        map.Add(new MapRoom { Id = 3, Exits = { ["east"] = 4 } });
        map.Add(new MapRoom { Id = 4 });

        map.Room(2)!.Locked = true;
        await Assert.That(PathFinder.Find(map, 1, 4)!.Select(s => s.Command)).IsEquivalentTo(new[] { "s", "e" });

        map.Room(1)!.LockedExits.Add("south");
        await Assert.That(PathFinder.Find(map, 1, 4)).IsNull();

        map.Room(4)!.Locked = true;
        await Assert.That(PathFinder.Find(map, 3, 4)).IsNull();
    }

    [Test]
    public async Task SpecialExitsAreWalkedByTheirCommand()
    {
        var map = new WorldMap();
        map.Add(new MapRoom { Id = 1, SpecialExits = { ["enter portal"] = 9 } });
        map.Add(new MapRoom { Id = 9 });

        await Assert.That(PathFinder.Find(map, 1, 9)!.Single().Command).IsEqualTo("enter portal");
    }

    [Test]
    public async Task ARoomNoExitReachesIsUnreachable()
    {
        var map = Corridor(3);
        map.Add(new MapRoom { Id = 50 });
        map.Room(3)!.Exits["north"] = 999; // leads to a room the map does not hold

        await Assert.That(PathFinder.Find(map, 1, 50)).IsNull();
        await Assert.That(PathFinder.Find(map, 1, 999)).IsNull();
        await Assert.That(PathFinder.Find(map, 777, 1)).IsNull();
    }

    /// <summary>A 100×100 grid, corner to corner: 198 steps, inside a budget a speedwalk can afford per keystroke.</summary>
    [Test]
    public async Task TenThousandRoomsIsQuick()
    {
        const int side = 100;
        var map = new WorldMap();
        long Id(int x, int y) => (y * side) + x + 1;
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                var room = map.Add(new MapRoom { Id = Id(x, y), X = x, Y = y });
                if (x > 0) room.Exits["west"] = Id(x - 1, y);
                if (x < side - 1) room.Exits["east"] = Id(x + 1, y);
                if (y > 0) room.Exits["north"] = Id(x, y - 1);
                if (y < side - 1) room.Exits["south"] = Id(x, y + 1);
            }
        }

        PathFinder.Find(map, 1, 2); // warm up the JIT, not the map
        var clock = Stopwatch.StartNew();
        var path = PathFinder.Find(map, Id(0, 0), Id(side - 1, side - 1));
        clock.Stop();

        await Assert.That(path!.Count).IsEqualTo((2 * side) - 2);
        await Assert.That(clock.ElapsedMilliseconds).IsLessThan(500);
    }
}
