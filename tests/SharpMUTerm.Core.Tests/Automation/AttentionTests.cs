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

    private static async Task<(FakeTelnetSession Telnet, List<AttentionEventArgs> Raised)> Open(
        bool alertOnName, Trigger? trigger = null, bool alertOnPage = false)
    {
        var telnet = new FakeTelnetSession();
        var world = new WorldDefinition { Name = "Aetherfall", Host = "h", Port = 1, AlertOnName = alertOnName, AlertOnPage = alertOnPage };
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
    public async Task AWorldWithPageAlertsIsToldOfAPageAndNotOfAMention()
    {
        var (telnet, raised) = await Open(alertOnName: false, alertOnPage: true);

        telnet.EmitLine("Rivane nods to Corvid.");
        telnet.EmitLine("Rivane tells you 'hi'");

        await Assert.That(raised.Select(e => e.Reason)).IsEquivalentTo([AttentionReason.Page]);
    }

    [Test]
    public async Task ANewWorldAlertsOnPagesAndNotOnTheName()
    {
        var world = new WorldDefinition();

        await Assert.That(world.AlertOnPage).IsTrue();
        await Assert.That(world.AlertOnName).IsFalse();
        await Assert.That(world.Alerts(AttentionReason.Page)).IsTrue();
        await Assert.That(world.Alerts(AttentionReason.Name)).IsFalse();
        await Assert.That(world.Alerts(AttentionReason.Trigger)).IsTrue();

        // The name rule covers pages, as it did before pages had a field of their own.
        world.AlertOnPage = false;
        world.AlertOnName = true;
        await Assert.That(world.Alerts(AttentionReason.Page)).IsTrue();
    }

    [Test]
    public async Task AWorldSavedBeforePageAlertsKeepsWhatItHad()
    {
        const string json = """
            { "version": 5, "worlds": [
              { "name": "Old", "host": "h", "port": 1 },
              { "name": "Named", "host": "h", "port": 2, "alertOnName": true },
              { "name": "New", "host": "h", "port": 3, "alertOnPage": true } ] }
            """;

        var config = ConfigurationStore.Deserialize(json);

        await Assert.That(config.Worlds.Select(w => w.AlertOnPage)).IsEquivalentTo([false, false, true]);
        await Assert.That(config.Worlds[1].Alerts(AttentionReason.Page)).IsTrue();

        // A world made now and saved comes back with page alerts on.
        var saved = new AppConfiguration();
        saved.Worlds.Add(new WorldDefinition { Name = "Fresh", Host = "h", Port = 4 });
        var reloaded = ConfigurationStore.Deserialize(ConfigurationStore.Serialize(saved));
        await Assert.That(reloaded.Worlds[0].AlertOnPage).IsTrue();
    }

    [Test]
    public async Task TheAlertFlagIsCopiedWithTheRule()
    {
        var trigger = new Trigger { Name = "t", Pattern = "x", Actions = { Alert = true } };
        await Assert.That(trigger.Clone().Actions.Alert).IsTrue();
        await Assert.That(new WorldDefinition { AutoReconnect = true, CommandSeparator = ";", AlertOnName = true, AlertOnPage = false }.Clone() is
            { AutoReconnect: true, CommandSeparator: ";", AlertOnName: true, AlertOnPage: false }).IsTrue();
    }
}
