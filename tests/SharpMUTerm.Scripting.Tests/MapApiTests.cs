namespace SharpMUTerm.Scripting.Tests;

/// <summary><c>map.current</c>, <c>map.path</c> and <c>map.walk</c>: the recorded map from Lua.</summary>
public class MapApiTests
{
    [Test]
    public async Task CurrentIsATableOrNil()
    {
        var world = new FakeScriptWorld();
        using var host = new ScriptHost(world);

        host.Execute("world.send(tostring(map.current()))");
        world.Room = new ScriptRoom(42, "Hall", "Keep");
        host.Execute("local r = map.current(); world.send(r.id .. ' ' .. r.name .. ' ' .. r.area)");

        await Assert.That(world.Sent).IsEquivalentTo(new[] { "nil", "42 Hall Keep" });
    }

    [Test]
    public async Task PathListsTheCommandsInOrder()
    {
        var world = new FakeScriptWorld();
        world.Paths["Gate"] = ["n", "e", "open door"];
        using var host = new ScriptHost(world);

        host.Execute("world.send(table.concat(map.path('Gate'), ';')); world.send(tostring(map.path('Nowhere')))");

        await Assert.That(world.Sent).IsEquivalentTo(new[] { "n;e;open door", "nil" });
    }

    [Test]
    public async Task WalkSaysWhyItDidNotStart()
    {
        var world = new FakeScriptWorld();
        world.Paths["Gate"] = ["n"];
        using var host = new ScriptHost(world);

        host.Execute("""
            world.send(tostring(map.walk('Gate')))
            local ok, why = map.walk('Nowhere')
            world.send(tostring(ok) .. ' ' .. why)
            """);

        await Assert.That(world.Sent).IsEquivalentTo(new[] { "true", "false the map knows no way there" });
        await Assert.That(world.Walks).IsEquivalentTo(new[] { "Gate" });
    }
}
