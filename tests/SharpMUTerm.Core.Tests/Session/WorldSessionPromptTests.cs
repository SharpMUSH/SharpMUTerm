using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Core.Tests.Session;

public class WorldSessionPromptTests
{
    private static (WorldSession session, FakeTelnetSession telnet) Create(
        WorldDefinition world,
        TriggerSet? set = null)
    {
        var telnet = new FakeTelnetSession();
        var sets = set is null ? null : new[] { set };
        var session = new WorldSession(world, triggerSets: sets, sessionFactory: _ => telnet);
        return (session, telnet);
    }

    private static WorldDefinition World() => new() { Name = "T", Host = "h", Port = 1, LocalEcho = true };

    [Test]
    public async Task APromptIsAddedToTheSessionsScrollbackAsWellAsCurrentPrompt()
    {
        var (session, telnet) = Create(World());
        await session.ConnectAsync();

        telnet.EmitPrompt("HP:100>");

        await Assert.That(session.CurrentPrompt).IsNotNull();
        await Assert.That(session.CurrentPrompt!.Text).IsEqualTo("HP:100>");
        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text == "HP:100>")).IsTrue();
    }

    [Test]
    public async Task APromptRunsThroughTheTriggersLikeAnyOtherLine()
    {
        var set = new TriggerSet();
        set.Triggers.Add(new Trigger { Pattern = @"^HP:", Actions = new TriggerActions { SpawnTarget = "Vitals" } });
        var (session, telnet) = Create(World(), set);
        SpawnLineEventArgs? spawned = null;
        session.SpawnLine += (_, e) => spawned = e;
        await session.ConnectAsync();

        telnet.EmitPrompt("HP:100>");

        await Assert.That(spawned).IsNotNull();
        await Assert.That(spawned!.Target).IsEqualTo("Vitals");
        await Assert.That(spawned!.Line.Text).IsEqualTo("HP:100>");
    }
}
