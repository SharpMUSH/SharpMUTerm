using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Core.Tests.Mapping;

public class MapStoreTests
{
    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "sharpmuterm-maps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Test]
    public async Task AMapRoundTrips()
    {
        var directory = TempDirectory();
        try
        {
            var store = new MapStore(directory);
            var map = store.For("Mud.Example.ORG.", 4000);
            var area = map.AreaNamed("Town");
            map.Add(new MapRoom
            {
                Id = 7,
                Name = "Square",
                AreaId = area.Id,
                X = 1,
                Y = -2,
                Z = 3,
                Environment = "city",
                Exits = { ["north"] = 8 },
                SpecialExits = { ["enter fountain"] = 9 },
                Weight = 4,
                ExitWeights = { ["north"] = 2 },
                LockedExits = { "enter fountain" },
                Locked = true,
                Symbol = "$",
                Color = "#ff8800",
                UserData = { ["note"] = "bank" },
            });
            store.Save("mud.example.org", 4000);

            await Assert.That(File.Exists(Path.Combine(directory, "mud.example.org_4000.json"))).IsTrue();

            var loaded = new MapStore(directory).For("mud.example.org", 4000);
            var room = loaded.Room(7)!;
            await Assert.That(loaded.Areas[area.Id].Name).IsEqualTo("Town");
            await Assert.That((room.Name, room.AreaId, room.X, room.Y, room.Z)).IsEqualTo(("Square", (int?)area.Id, 1, -2, 3));
            await Assert.That(room.Exits["north"]).IsEqualTo(8L);
            await Assert.That(room.SpecialExits["enter fountain"]).IsEqualTo(9L);
            await Assert.That((room.Weight, room.ExitWeights["north"], room.Locked)).IsEqualTo((4, 2, true));
            await Assert.That(room.LockedExits).Contains("enter fountain");
            await Assert.That((room.Symbol, room.Color, room.UserData["note"])).IsEqualTo(("$", "#ff8800", "bank"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task AStoreWithNoDirectoryWritesNothing()
    {
        var store = new MapStore();
        store.For("h", 1).Add(new MapRoom { Id = 1 });
        store.Save("h", 1);

        await Assert.That(store.IsPersistent).IsFalse();
        await Assert.That(store.For("h", 1).Room(1)).IsNotNull();
    }

    /// <summary>An older client never destroys a newer client's map: it loads empty, says why, and does not write.</summary>
    [Test]
    public async Task ANewerFileIsLeftAlone()
    {
        var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, MapStore.FileNameFor(MapStore.Key("h", 1)));
            var newer = """{"version":99,"rooms":[{"id":1,"name":"Future"}]}""";
            File.WriteAllText(path, newer);

            var store = new MapStore(directory);
            var map = store.For("h", 1);
            map.Add(new MapRoom { Id = 2 });
            store.Save("h", 1);

            await Assert.That(map.Rooms.Count).IsEqualTo(1);
            await Assert.That(store.ProblemFor("h", 1)).Contains("newer");
            await Assert.That(File.ReadAllText(path)).IsEqualTo(newer);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task AnUnversionedFileIsTheFirstLayoutAndBadExitsAreDropped()
    {
        var map = MapStore.Deserialize("""{"rooms":[{"id":1,"name":"Old","weight":0,"exits":{"N":2,"sideways":3}}]}""");

        var room = map.Room(1)!;
        await Assert.That(room.Exits.Keys).IsEquivalentTo(new[] { "north" });
        await Assert.That(room.Weight).IsEqualTo(1);
    }

    [Test]
    public async Task AnUnreadableFileIsMovedAsideAndMappingStartsAgain()
    {
        var directory = TempDirectory();
        try
        {
            var path = Path.Combine(directory, MapStore.FileNameFor(MapStore.Key("h", 1)));
            File.WriteAllText(path, "not json");

            var store = new MapStore(directory);
            store.For("h", 1).Add(new MapRoom { Id = 5 });
            store.Save("h", 1);

            await Assert.That(File.ReadAllText(path + MapStore.AsideSuffix)).IsEqualTo("not json");
            await Assert.That(new MapStore(directory).For("h", 1).Room(5)).IsNotNull();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    [Arguments("../../etc/passwd", 23)]
    [Arguments("..", 1)]
    [Arguments("a/b\\c", 2)]
    public async Task AHostCannotNameAPathOutsideTheFolder(string host, int port)
    {
        var name = MapStore.FileNameFor(MapStore.Key(host, port));

        await Assert.That(name).DoesNotContain("/");
        await Assert.That(name).DoesNotContain("\\");
        await Assert.That(name.StartsWith('.')).IsFalse();
        await Assert.That(Path.GetFileName(name)).IsEqualTo(name);
    }
}
