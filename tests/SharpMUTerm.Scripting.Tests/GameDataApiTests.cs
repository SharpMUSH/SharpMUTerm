using System.Text.Json.Nodes;

namespace SharpMUTerm.Scripting.Tests;

/// <summary><c>gmcp.get</c>, <c>msdp.get</c> and <c>gmcp.send</c>: game data in and out of Lua.</summary>
public class GameDataApiTests
{
    [Test]
    public async Task GmcpGetReturnsTablesAndScalars()
    {
        var world = new FakeScriptWorld();
        world.Data["Char.Vitals"] = JsonNode.Parse("""{"hp":"450","flags":[true,false],"lvl":12}""");
        using var host = new ScriptHost(world);

        host.Execute("""
            local v = gmcp.get('Char.Vitals')
            world.send(v.hp .. ' ' .. tostring(v.flags[1]) .. ' ' .. (v.lvl + 1))
            world.send(tostring(gmcp.get('Nope')))
            """);

        await Assert.That(world.Sent).IsEquivalentTo(new[] { "450 true 13", "nil" });
    }

    [Test]
    public async Task MsdpGetReadsTheMsdpPrefix()
    {
        var world = new FakeScriptWorld();
        world.Data["MSDP.HEALTH"] = JsonValue.Create("90");
        using var host = new ScriptHost(world);

        host.Execute("world.send(msdp.get('HEALTH'))");

        await Assert.That(world.Sent).IsEquivalentTo(new[] { "90" });
    }

    [Test]
    public async Task GmcpSendSerialisesATableAndPassesAStringThrough()
    {
        var world = new FakeScriptWorld();
        using var host = new ScriptHost(world);

        host.Execute("""
            gmcp.send('Char.Skills.Get', { group = 'combat' })
            gmcp.send('Char.Items.Inv', '')
            gmcp.send('Comm.List', { 'a', 'b' })
            gmcp.send('Raw', '{"x":1}')
            """);

        await Assert.That(world.SentGmcp).IsEquivalentTo(new[]
        {
            ("Char.Skills.Get", """{"group":"combat"}"""),
            ("Char.Items.Inv", ""),
            ("Comm.List", """["a","b"]"""),
            ("Raw", """{"x":1}"""),
        });
    }

    [Test]
    public async Task ATableThatContainsItselfIsRefusedNotLooped()
    {
        var world = new FakeScriptWorld();
        using var host = new ScriptHost(world);

        await Assert.That(() => host.Execute("local t = {} t.me = t gmcp.send('X', t)")).Throws<ScriptException>();
        await Assert.That(world.SentGmcp).IsEmpty();
    }
}
