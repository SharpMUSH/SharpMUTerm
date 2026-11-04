using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Telnet.Mssp;

namespace SharpMUTerm.Core.Tests.Layouts;

/// <summary><see cref="LayoutSuggester"/>: vitals suggest Combat; a MUSH-family codebase, or nothing, suggests Roleplay.</summary>
public class LayoutSuggesterTests
{
    [Test]
    public async Task NothingSeenSuggestsRoleplayByDefault()
    {
        var suggestion = LayoutSuggester.Suggest(LayoutSignals.None);

        await Assert.That(suggestion).IsEqualTo(new LayoutSuggestion(BuiltInLayouts.RoleplayId, LayoutSuggestionReason.Default));
    }

    [Test]
    [Arguments("Char.Vitals")]
    [Arguments("char.vitals")]
    [Arguments("MSDP.HEALTH")]
    public async Task VitalsSuggestCombat(string package)
    {
        var suggestion = LayoutSuggester.Suggest(new LayoutSignals(["Room.Info", package]));

        await Assert.That(suggestion).IsEqualTo(new LayoutSuggestion(BuiltInLayouts.CombatId, LayoutSuggestionReason.Vitals));
    }

    [Test]
    public async Task VitalsWinOverAMushCodebase()
    {
        var suggestion = LayoutSuggester.Suggest(new LayoutSignals(["Char.Vitals"], "PennMUSH 1.8.8p0"));

        await Assert.That(suggestion.LayoutId).IsEqualTo(BuiltInLayouts.CombatId);
    }

    [Test]
    [Arguments("PennMUSH 1.8.8p0")]
    [Arguments("TinyMUSH 3.3")]
    [Arguments("TinyMUX 2.13.0.4")]
    [Arguments("RhostMUSH")]
    [Arguments("MUSH")]
    [Arguments("mux2")]
    [Arguments("FuzzBall MUCK 7.0")]
    [Arguments("LambdaMOO 1.8.1")]
    [Arguments("SharpMUSH")]
    public async Task AMushFamilyCodebaseSuggestsRoleplayAndSaysWhy(string codebase)
    {
        var suggestion = LayoutSuggester.Suggest(new LayoutSignals([], codebase));

        await Assert.That(suggestion).IsEqualTo(new LayoutSuggestion(BuiltInLayouts.RoleplayId, LayoutSuggestionReason.RoleplayServer));
    }

    [Test]
    [Arguments("tbaMUD 2023")]
    [Arguments("Evennia 4.0")]
    [Arguments("Smooth Engine")]
    [Arguments("")]
    public async Task AnyOtherCodebaseIsTheDefault(string codebase)
    {
        await Assert.That(LayoutSuggester.Suggest(new LayoutSignals([], codebase)).Reason).IsEqualTo(LayoutSuggestionReason.Default);
    }

    [Test]
    public async Task TheTinyMudFamilyCountsWhenTheCodebaseSaysNothing()
    {
        var suggestion = LayoutSuggester.Suggest(new LayoutSignals([], "Custom", "TinyMUD"));

        await Assert.That(suggestion.Reason).IsEqualTo(LayoutSuggestionReason.RoleplayServer);
    }

    [Test]
    public async Task TheSignalsAreReadOffTheLiveStores()
    {
        var data = new GameDataStore();
        data.SetMsdp("""{"HEALTH":"100"}""");
        var mssp = MsspData.From([
            new KeyValuePair<string, IReadOnlyList<string>>("CODEBASE", ["TinyMUX 2.13"]),
            new KeyValuePair<string, IReadOnlyList<string>>("FAMILY", ["TinyMUD"]),
        ]);

        var signals = LayoutSignals.From(data, mssp);

        await Assert.That(signals.GameDataPackages).Contains("MSDP.HEALTH");
        await Assert.That(signals.MsspCodebase).IsEqualTo("TinyMUX 2.13");
        await Assert.That(signals.MsspFamily).IsEqualTo("TinyMUD");
        await Assert.That(LayoutSuggester.Suggest(signals).LayoutId).IsEqualTo(BuiltInLayouts.CombatId);
        await Assert.That(LayoutSignals.From(null, null).GameDataPackages).IsEmpty();
    }
}
