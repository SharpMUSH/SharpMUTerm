using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Scripting.Tests;

/// <summary><c>store.*</c>: values a character's scripts keep between runs, bounded, and on disk only when given a folder.</summary>
public class ScriptStoreTests
{
    [Test]
    public async Task AValueSetInOneHostIsReadByTheNextFromTheSameFile()
    {
        var root = Directory.CreateTempSubdirectory("sharpmuterm-store-").FullName;
        try
        {
            var path = Path.Combine(root, "Ann.json");
            using (var host = new ScriptHost(new FakeScriptWorld(), new ScriptStore(path)))
            {
                host.Execute("store.set('kills', 3) store.set('seen', { 'Bob', 'Cy' }) store.set('gone', true) store.set('gone', nil)");
            }

            var world = new FakeScriptWorld();
            using var next = new ScriptHost(world, new ScriptStore(path));
            next.Execute("""
                world.send(store.get('kills') + 1)
                world.send(store.get('seen')[2])
                world.send(tostring(store.get('gone')))
                world.send(table.concat(store.keys(), ','))
                """);

            await Assert.That(world.Sent).IsEquivalentTo(new[] { "4", "Cy", "nil", "kills,seen" });
            if (!OperatingSystem.IsWindows())
            {
                await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AHostWithNoStoreKeepsValuesInMemoryAndWritesNothing()
    {
        var world = new FakeScriptWorld();
        using var host = new ScriptHost(world);

        host.Execute("store.set('x', 'y') world.send(store.get('x'))");

        await Assert.That(world.Sent).IsEquivalentTo(new[] { "y" });
        await Assert.That(new ScriptStore(null).FilePath).IsNull();
    }

    [Test]
    public async Task ASetPastABoundIsRefusedAndChangesNothing()
    {
        var store = new ScriptStore(null);
        await Assert.That(store.Set(new string('k', ScriptStore.MaxKeyLength + 1), System.Text.Json.Nodes.JsonValue.Create(1))).IsNotNull();
        await Assert.That(store.Set("big", System.Text.Json.Nodes.JsonValue.Create(new string('x', ScriptStore.MaxBytes)))).IsNotNull();
        await Assert.That(store.Keys).IsEmpty();

        using var host = new ScriptHost(new FakeScriptWorld(), store);
        await Assert.That(() => host.Execute("store.set('f', function() end)")).Throws<ScriptException>();
        await Assert.That(() => host.Execute("store.set('big', string.rep('x', 300000))")).Throws<ScriptException>();
        await Assert.That(store.Keys).IsEmpty();
    }

    [Test]
    public async Task AFileThatDoesNotParseIsMovedAsideRatherThanOverwritten()
    {
        var root = Directory.CreateTempSubdirectory("sharpmuterm-store-").FullName;
        try
        {
            var path = Path.Combine(root, "Ann.json");
            File.WriteAllText(path, "{ not json");
            var reports = new List<string>();

            var store = new ScriptStore(path, reports.Add);
            store.Set("a", System.Text.Json.Nodes.JsonValue.Create(1));

            await Assert.That(File.ReadAllText(path + ".bad")).IsEqualTo("{ not json");
            await Assert.That(reports.Count).IsEqualTo(1);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task TwoCharactersNeverShareAFileAndNoneNamesAPath()
    {
        var plain = ScriptStore.FileNameFor("Aetherfall.Corvid");
        var slash = ScriptStore.FileNameFor("A/B.C");
        var under = ScriptStore.FileNameFor("A_B.C");

        await Assert.That(plain).IsEqualTo("Aetherfall.Corvid.json");
        await Assert.That(slash).IsNotEqualTo(under);
        await Assert.That(slash).DoesNotContain("/");
        await Assert.That(ScriptStore.FileNameFor("..")).DoesNotContain("..");
    }

    [Test]
    public async Task TheStoreOutlivesAReloadOfTheSessionsScripts()
    {
        var scripts = Directory.CreateTempSubdirectory("sharpmuterm-scripts-").FullName;
        var data = Directory.CreateTempSubdirectory("sharpmuterm-store-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(scripts, "main.lua"), "store.set('loads', (store.get('loads') or 0) + 1)");
            var set = new TriggerSet { Name = "s", ScriptFiles = { "main.lua" } };
            var session = new WorldSession(
                new WorldDefinition { Name = "T", Host = "h", Port = 1 },
                new CharacterDefinition { Name = "Ann" },
                triggerSets: new[] { set },
                sessionFactory: _ => new FakeTelnetSession());
            using var sessionScripts = new SessionScripts(session, scripts, _ => { }, storeRoot: data);

            sessionScripts.Reload();
            sessionScripts.Reload();

            await Assert.That(sessionScripts.Store.Get("loads")!.GetValue<long>()).IsEqualTo(2);
            await Assert.That(File.Exists(Path.Combine(data, "T.Ann.json"))).IsTrue();
        }
        finally
        {
            Directory.Delete(scripts, recursive: true);
            Directory.Delete(data, recursive: true);
        }
    }
}
