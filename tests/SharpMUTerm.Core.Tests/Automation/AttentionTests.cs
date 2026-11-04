using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Tests.Session;

namespace SharpMUTerm.Core.Tests.Automation;

/// <summary>Which lines ask for the player's attention: an alert trigger, or a world's name-and-pages rule.</summary>
public class AttentionTests
{
    [Test]
    [Arguments("Rivane pages: are you around?")]
    [Arguments("Rivane pages you, \"hello\"")]
    [Arguments("From afar, Rivane waves.")]
    [Arguments("Rivane tells you 'meet me at the gate'")]
    [Arguments("Rivane whispers: psst")]
    [Arguments("Rivane whispers to you, \"psst\"")]
    public async Task PagesTellsAndWhispersAreRecognised(string line) =>
        await Assert.That(AttentionMatcher.Classify(line, "Corvid")).IsEqualTo(AttentionReason.Page);

    [Test]
    public async Task TheNameCountsAsAWholeWordAnywhereButTheStart()
    {
        await Assert.That(AttentionMatcher.Classify("Rivane says, \"corvid, look!\"", "Corvid")).IsEqualTo(AttentionReason.Name);
        await Assert.That(AttentionMatcher.Classify("Rivane hands a map to Corvid.", "Corvid")).IsEqualTo(AttentionReason.Name);

        // The player's own pose, as a MUSH shows it back; a longer word containing the name; their own page.
        await Assert.That(AttentionMatcher.Classify("Corvid waves.", "Corvid")).IsNull();
        await Assert.That(AttentionMatcher.Classify("The Corvidae nest is empty.", "Corvid")).IsNull();
        await Assert.That(AttentionMatcher.Classify("You paged Rivane with 'hi'.", "Corvid")).IsNull();
        await Assert.That(AttentionMatcher.Classify("A Z appears.", "Z")).IsNull();
    }

    [Test]
    public async Task TheThrottleLetsOneAlertThroughPerInterval()
    {
        var time = new Mapping.ManualTimeProvider();
        var throttle = new AttentionThrottle(time);

        await Assert.That(throttle.TryTake()).IsTrue();
        await Assert.That(throttle.TryTake()).IsFalse();
        time.Advance(AttentionThrottle.Interval);
        await Assert.That(throttle.TryTake()).IsTrue();
    }

    private static async Task<(FakeTelnetSession Telnet, List<AttentionEventArgs> Raised)> Open(bool alertOnName, Trigger? trigger = null)
    {
        var telnet = new FakeTelnetSession();
        var world = new WorldDefinition { Name = "Aetherfall", Host = "h", Port = 1, AlertOnName = alertOnName };
        var set = new TriggerSet { Name = "s" };
        if (trigger is not null)
        {
            set.Triggers.Add(trigger);
        }

        var session = new WorldSession(world, new CharacterDefinition { Name = "Corvid" }, [set], _ => telnet);
        var raised = new List<AttentionEventArgs>();
        session.AttentionRequested += (_, e) => raised.Add(e);
        await session.ConnectAsync();
        return (telnet, raised);
    }

    [Test]
    public async Task AWorldThatOptsInIsToldOfAMentionAndAPage()
    {
        var (telnet, raised) = await Open(alertOnName: true);

        telnet.EmitLine("Rivane pages: hi");
        telnet.EmitLine("Rivane nods to Corvid.");
        telnet.EmitLine("The fountain bubbles.");

        await Assert.That(raised.Select(e => e.Reason)).IsEquivalentTo([AttentionReason.Page, AttentionReason.Name]);
        await Assert.That(raised[0].Text).IsEqualTo("Rivane pages: hi");
    }

    [Test]
    public async Task AWorldThatDoesNotOptInIsToldOnlyOfAnAlertTrigger()
    {
        var trigger = new Trigger { Name = "attack", Pattern = "attacks you", Actions = { Alert = true, Gag = true } };
        var (telnet, raised) = await Open(alertOnName: false, trigger);

        telnet.EmitLine("Rivane pages: hi");
        telnet.EmitLine("A rat attacks you!");

        await Assert.That(raised.Select(e => e.Reason)).IsEquivalentTo([AttentionReason.Trigger]);
    }

    [Test]
    public async Task TheAlertFlagIsCopiedWithTheRule()
    {
        var trigger = new Trigger { Name = "t", Pattern = "x", Actions = { Alert = true } };
        await Assert.That(trigger.Clone().Actions.Alert).IsTrue();
        await Assert.That(new WorldDefinition { AutoReconnect = true, CommandSeparator = ";", AlertOnName = true }.Clone() is
            { AutoReconnect: true, CommandSeparator: ";", AlertOnName: true }).IsTrue();
    }
}
