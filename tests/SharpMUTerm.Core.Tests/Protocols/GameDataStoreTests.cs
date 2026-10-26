using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Tests.Session;

namespace SharpMUTerm.Core.Tests.Protocols;

/// <summary>
/// The session's out-of-band game data. Before it the client kept nine fixed <c>Char.*</c> keys, drew none
/// of them, and dropped MSDP on the floor.
/// </summary>
public class GameDataStoreTests
{
    [Test]
    public async Task APathReadsAFieldOfTheLongestMatchingPackage()
    {
        var store = new GameDataStore();
        store.SetGmcp("Char", """{"name":"Ann"}""");
        store.SetGmcp("Char.Vitals", """{"hp":"450","maxhp":"500"}""");

        await Assert.That(store.GetText("Char.Vitals.hp")).IsEqualTo("450");
        await Assert.That(store.GetNumber("Char.Vitals.maxhp")).IsEqualTo(500);
        await Assert.That(store.GetText("Char.name")).IsEqualTo("Ann");
        await Assert.That(store.GetText("Char.Vitals.mp")).IsNull();
    }

    /// <summary>IRE spells it <c>Char.Vitals</c>, Aardwolf <c>char.vitals</c>; one meter reads both.</summary>
    [Test]
    public async Task NamesAndFieldsAreCaseInsensitive()
    {
        var store = new GameDataStore();
        store.SetGmcp("char.vitals", """{"HP":12}""");

        await Assert.That(store.GetNumber("Char.Vitals.hp")).IsEqualTo(12);
    }

    /// <summary><c>Char.Status</c> sends only what changed; replacing the package would forget the rest.</summary>
    [Test]
    public async Task AnObjectIsMergedIntoWhatIsHeld()
    {
        var store = new GameDataStore();
        store.SetGmcp("Char.Status", """{"level":"10","race":"Elf"}""");
        store.SetGmcp("Char.Status", """{"level":"11"}""");

        await Assert.That(store.GetText("Char.Status.level")).IsEqualTo("11");
        await Assert.That(store.GetText("Char.Status.race")).IsEqualTo("Elf");
    }

    [Test]
    public async Task ArraysAreIndexedAndScalarsReplace()
    {
        var store = new GameDataStore();
        store.SetGmcp("Room.Players", """[{"name":"Bob"},{"name":"Cy"}]""");
        store.SetGmcp("Char.Count", "3");
        store.SetGmcp("Char.Count", "4");

        await Assert.That(store.GetText("Room.Players.1.name")).IsEqualTo("Cy");
        await Assert.That(store.GetText("Char.Count")).IsEqualTo("4");
    }

    [Test]
    public async Task MsdpVariablesAreHeldUnderTheirOwnNames()
    {
        var store = new GameDataStore();
        store.SetMsdp("""{"HEALTH":"90","HEALTH_MAX":"100"}""");

        await Assert.That(store.GetNumber("MSDP.HEALTH")).IsEqualTo(90);
        await Assert.That(store.GetNumber("msdp.health_max")).IsEqualTo(100);
        await Assert.That(store.Count).IsEqualTo(2);
    }

    [Test]
    public async Task APayloadOverTheCapIsRefused()
    {
        var store = new GameDataStore();
        var big = "\"" + new string('x', GameDataStore.MaxPayloadBytes) + "\"";

        await Assert.That(store.SetGmcp("Big", big)).IsFalse();
        await Assert.That(store.Count).IsEqualTo(0);
    }

    [Test]
    public async Task NewPackagesStopAtTheCapAndHeldOnesStillUpdate()
    {
        var store = new GameDataStore();
        for (var i = 0; i < GameDataStore.MaxPackages; i++)
        {
            store.SetGmcp($"P{i}", "1");
        }

        await Assert.That(store.SetGmcp("OneTooMany", "1")).IsFalse();
        await Assert.That(store.SetGmcp("P0", "2")).IsTrue();
        await Assert.That(store.GetText("P0")).IsEqualTo("2");
    }

    [Test]
    public async Task MalformedJsonIsRefusedAndAnEmptyBodyIsHeld()
    {
        var store = new GameDataStore();

        await Assert.That(store.SetGmcp("Bad", "{nope")).IsFalse();
        await Assert.That(store.SetGmcp("Core.Ping", "")).IsTrue();
        await Assert.That(store.Entries.Select(e => e.Package)).IsEquivalentTo(new[] { "Core.Ping" });
    }

    [Test]
    public async Task TheSessionStoresWhatArrivesAndClearsOnConnect()
    {
        var telnet = new FakeTelnetSession();
        var session = new WorldSession(new WorldDefinition { Name = "T", Host = "h", Port = 1 }, sessionFactory: _ => telnet);
        await session.ConnectAsync();

        telnet.EmitGmcp("Char.Vitals", """{"hp":5}""");
        telnet.EmitMsdp("""{"MANA":"7"}""");
        await Assert.That(session.GameData.GetNumber("Char.Vitals.hp")).IsEqualTo(5);
        await Assert.That(session.GameData.GetNumber("MSDP.MANA")).IsEqualTo(7);

        await session.DisconnectAsync();
        await session.ConnectAsync();
        await Assert.That(session.GameData.Count).IsEqualTo(0);
    }

    /// <summary>An MSDP server sends nothing until asked; the meters' variables are asked for once it is agreed.</summary>
    [Test]
    public async Task AgreeingMsdpReportsTheVariablesTheSessionWants()
    {
        var telnet = new FakeTelnetSession();
        var session = new WorldSession(new WorldDefinition { Name = "T", Host = "h", Port = 1 }, sessionFactory: _ => telnet)
        {
            MsdpReports = ["HEALTH", "HEALTH_MAX", "health"],
        };
        await session.ConnectAsync();

        telnet.RaiseMsdpEnabled();

        await Assert.That(telnet.SentMsdp).IsEquivalentTo(new[] { ("REPORT", "HEALTH"), ("REPORT", "HEALTH_MAX") });
    }
}
