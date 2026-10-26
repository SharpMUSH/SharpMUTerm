using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Core.Tests.Session;

/// <summary>A MUSH's edit grab: the prefixed line is taken out, raised, and never shown, triggered on or sent.</summary>
public class EditGrabTests
{
    private static (WorldSession Session, FakeTelnetSession Telnet, List<string> Grabs) Create(string? prefix, TriggerSet? set = null)
    {
        var telnet = new FakeTelnetSession();
        var world = new WorldDefinition { Name = "Ares", Host = "ares.example", Port = 4201, GrabPrefix = prefix };
        var session = new WorldSession(world, triggerSets: set is null ? null : new[] { set }, sessionFactory: _ => telnet);
        var grabs = new List<string>();
        session.EditGrabbed += (_, text) => grabs.Add(text);
        return (session, telnet, grabs);
    }

    [Test]
    public async Task APrefixedLineIsGrabbedAndNotShown()
    {
        var (session, telnet, grabs) = Create("FugueEdit >");
        await session.ConnectAsync();

        telnet.EmitLine("FugueEdit > &desc me=A tall figure in grey.");
        telnet.EmitLine("You see nothing special.");

        await Assert.That(grabs).IsEquivalentTo(new[] { "&desc me=A tall figure in grey." });
        var shown = session.Scrollback.Snapshot().Select(l => l.Text).ToArray();
        await Assert.That(shown.Any(t => t.Contains("FugueEdit", StringComparison.Ordinal))).IsFalse();
        await Assert.That(shown).Contains("You see nothing special.");
    }

    [Test]
    public async Task NoPrefixMeansNoGrab()
    {
        var (session, telnet, grabs) = Create(null);
        await session.ConnectAsync();

        telnet.EmitLine("FugueEdit > &desc me=x");

        await Assert.That(grabs).IsEmpty();
        await Assert.That(session.Scrollback.Snapshot().Select(l => l.Text)).Contains("FugueEdit > &desc me=x");
    }

    [Test]
    public async Task AGrabIsNeitherTriggeredOnNorSent()
    {
        var set = new TriggerSet();
        set.Triggers.Add(new Trigger { Pattern = "desc", Actions = new TriggerActions { SendResponse = "say caught" } });
        var (session, telnet, grabs) = Create("FugueEdit >", set);
        await session.ConnectAsync();

        telnet.EmitLine("FugueEdit > &desc me=x");

        await Assert.That(grabs).Count().IsEqualTo(1);
        await Assert.That(telnet.SentLines).DoesNotContain("say caught");
    }

    [Test]
    public async Task TheMatchIsExactAndAtTheStart()
    {
        var (session, telnet, grabs) = Create("FugueEdit >");
        await session.ConnectAsync();

        telnet.EmitLine("fugueedit > lower case");
        telnet.EmitLine("Bob pages: FugueEdit > not at the start");

        await Assert.That(grabs).IsEmpty();
    }
}
