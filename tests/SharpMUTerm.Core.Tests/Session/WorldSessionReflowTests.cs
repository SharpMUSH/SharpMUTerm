using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Session;

/// <summary>F5 ▸ join wraps, through a real session: ingest-time, triggers see the joined line, nothing out of order.</summary>
public class WorldSessionReflowTests
{
    private static readonly string[] Paragraph =
    [
        "The market square is crowded with stalls selling everything from bolts of",
        "dyed cloth to barrels of salted fish, and the noise of haggling rises above",
        "the clatter of carts as travelling merchants call out their wares.",
    ];

    private const string Joined =
        "The market square is crowded with stalls selling everything from bolts of " +
        "dyed cloth to barrels of salted fish, and the noise of haggling rises above " +
        "the clatter of carts as travelling merchants call out their wares.";

    private static (WorldSession Session, FakeTelnetSession Telnet, List<StyledLine> Printed) Create(
        bool join, TriggerSet? set = null)
    {
        var telnet = new FakeTelnetSession();
        var world = new WorldDefinition { Name = "T", Host = "h", Port = 1, JoinWrappedLines = join };
        var session = new WorldSession(world, triggerSets: set is null ? null : new[] { set }, sessionFactory: _ => telnet)
        {
            ReflowHold = TimeSpan.FromHours(1), // released only by what the tests send
        };
        var printed = new List<StyledLine>();
        session.LinePrinted += (_, l) => { lock (printed) { printed.Add(l); } };

        // The connection's own notes are not lines any more (ConnectionNoted); recorded in order with the lines, as
        // "*** CLOSED: …", so a test can still say a note came after a line.
        session.ConnectionNoted += (_, n) => { lock (printed) { printed.Add(StyledLine.FromText("*** " + n, TextStyle.Default)); } };
        return (session, telnet, printed);
    }

    /// <summary>Lines a server filled to 76 columns, so the session learns the wrap column.</summary>
    private static void Warm(FakeTelnetSession telnet)
    {
        for (var i = 0; i < ServerWrapJoiner.MinimumSamples; i++)
        {
            telnet.EmitLine(new string('=', 75) + ".");
        }
    }

    // The hold timer prints on its own thread, so the list is read and written under its lock.
    private static List<string> Output(List<StyledLine> printed)
    {
        lock (printed)
        {
            return printed.Select(l => l.Text).Where(t => !t.StartsWith("***", StringComparison.Ordinal) && !t.StartsWith("===", StringComparison.Ordinal)).ToList();
        }
    }

    [Test]
    public async Task AWorldThatJoinsShowsTheParagraphAsOneLine()
    {
        var (session, telnet, printed) = Create(join: true);
        await session.ConnectAsync();
        Warm(telnet);

        foreach (var row in Paragraph)
        {
            telnet.EmitLine(row);
        }

        await Assert.That(Output(printed)).IsEquivalentTo(new[] { Joined });
    }

    [Test]
    public async Task AWorldThatDoesNotShowsTheRowsAsSent()
    {
        var (session, telnet, printed) = Create(join: false);
        await session.ConnectAsync();
        Warm(telnet);

        foreach (var row in Paragraph)
        {
            telnet.EmitLine(row);
        }

        await Assert.That(Output(printed)).IsEquivalentTo(Paragraph);
    }

    [Test]
    public async Task ATriggerMatchesAcrossTheServersBreak()
    {
        var set = new TriggerSet();
        set.Triggers.Add(new Trigger { Pattern = "bolts of dyed cloth", Actions = new TriggerActions { SpawnTarget = "Shop" } });
        var (session, telnet, _) = Create(join: true, set);
        var spawned = new List<string>();
        session.SpawnLine += (_, e) => spawned.Add(e.Line.Text);
        await session.ConnectAsync();
        Warm(telnet);

        foreach (var row in Paragraph)
        {
            telnet.EmitLine(row);
        }

        await Assert.That(spawned).IsEquivalentTo(new[] { Joined });
    }

    [Test]
    public async Task APromptReleasesAHeldLineFirst()
    {
        var (session, telnet, printed) = Create(join: true);
        await session.ConnectAsync();
        Warm(telnet);

        telnet.EmitLine(Paragraph[0]);
        await Assert.That(Output(printed)).IsEmpty();

        telnet.EmitPrompt("HP:100>");

        await Assert.That(Output(printed)).IsEquivalentTo(new[] { Paragraph[0], "HP:100>" });
    }

    [Test]
    public async Task AHeldLineIsShownWhenTheStreamGoesQuiet()
    {
        var (session, telnet, printed) = Create(join: true);
        session.ReflowHold = TimeSpan.FromMilliseconds(20);
        await session.ConnectAsync();
        Warm(telnet);

        telnet.EmitLine(Paragraph[0]);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Output(printed).Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        await Assert.That(Output(printed)).IsEquivalentTo(new[] { Paragraph[0] });
    }

    [Test]
    public async Task ADisconnectReleasesAHeldLineBeforeItsOwnMessage()
    {
        var (session, telnet, printed) = Create(join: true);
        await session.ConnectAsync();
        Warm(telnet);

        telnet.EmitLine(Paragraph[0]);
        await telnet.DisconnectAsync();

        var texts = printed.Select(l => l.Text).ToList();
        var held = texts.IndexOf(Paragraph[0]);
        await Assert.That(held).IsGreaterThanOrEqualTo(0);
        await Assert.That(texts.FindIndex(t => t.StartsWith("*** " + ConnectionNote.Closed, StringComparison.Ordinal))).IsGreaterThan(held);
    }
}
