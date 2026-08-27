using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;

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

    [Test]
    public async Task APromptIsFlaggedAsAPromptAndAnOrdinaryLineIsNot()
    {
        var (session, telnet) = Create(World());
        StyledLine? printed = null;
        session.LinePrinted += (_, l) => printed = l;
        await session.ConnectAsync();

        telnet.EmitLine("You see a troll.");
        await Assert.That(printed).IsNotNull();
        await Assert.That(printed!.IsPrompt).IsFalse();

        printed = null;
        telnet.EmitPrompt("HP:100>");

        await Assert.That(printed).IsNotNull();
        await Assert.That(printed!.IsPrompt).IsTrue();
        await Assert.That(session.CurrentPrompt!.IsPrompt).IsTrue();
    }

    [Test]
    public async Task AStyleOnlyPromptUpdatesCurrentPromptButPrintsNoLine()
    {
        var (session, telnet) = Create(World());
        StyledLine? printed = null;
        var printedCount = 0;
        session.LinePrinted += (_, l) =>
        {
            printed = l;
            printedCount++;
        };
        StyledLine? changed = null;
        session.PromptChanged += (_, p) => changed = p;
        await session.ConnectAsync();

        // Reset past the "*** Connecting..."/"*** Connected." system lines ConnectAsync
        // itself prints — those raise LinePrinted too, and would otherwise be mistaken for
        // the bug under test.
        printed = null;
        printedCount = 0;

        // A prompt boundary that carries only an SGR reset and nothing printable — the shape
        // AnsiParser.Flush() reports as "nothing buffered" (returns null), which is the case
        // WorldSession.OnOutputReceived must not paper over with a blank StyledLine.Empty print.
        telnet.EmitPrompt("[0m");

        await Assert.That(printedCount).IsEqualTo(0);
        await Assert.That(printed).IsNull();
        await Assert.That(session.CurrentPrompt).IsNotNull();
        await Assert.That(session.CurrentPrompt!.Text).IsEqualTo(string.Empty);
        await Assert.That(session.CurrentPrompt!.IsPrompt).IsTrue();
        await Assert.That(changed).IsNotNull();
        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text == string.Empty)).IsFalse();
    }
}
