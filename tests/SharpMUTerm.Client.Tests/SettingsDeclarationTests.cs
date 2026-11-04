using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// The declared settings: every built-in field is bound to the configuration, a fresh configuration is at every
/// default, a change is seen and a reset undoes it, and the search finds a field by label, group, help or
/// keyword and a screen by its title.
/// </summary>
public class SettingsDeclarationTests
{
    private static IEnumerable<SettingsField> Fields => BuiltInSettings.All.SelectMany(s => s.Fields);

    [Test]
    public async Task EveryDeclaredFieldIsBoundAndGrouped()
    {
        foreach (var field in Fields)
        {
            await Assert.That(field.Binding).IsNotNull();
            await Assert.That(field.Group).IsNotNull();
        }
    }

    [Test]
    public async Task AFreshConfigurationIsAtEveryDefault()
    {
        var config = new AppConfiguration();
        foreach (var field in Fields)
        {
            await Assert.That(field.Binding!.IsDefault(config)).IsTrue();
        }
    }

    [Test]
    public async Task AChangeIsSeenAndAResetUndoesIt()
    {
        var config = new AppConfiguration();
        config.Text.TabWidth = 8;
        config.Text.StatusMeters.RemoveAt(0);
        var tab = BuiltInSettings.Text.Fields.Single(f => f.Id == "tab-width").Binding!;
        var meters = BuiltInSettings.Text.Fields.Single(f => f.Id == BuiltInSettings.MetersField).Binding!;

        await Assert.That(tab.IsDefault(config)).IsFalse();
        await Assert.That(meters.IsDefault(config)).IsFalse();

        tab.Reset(config);
        meters.Reset(config);

        await Assert.That(config.Text.TabWidth).IsEqualTo(TextSettings.DefaultTabWidth);
        await Assert.That(config.Text.StatusMeters.Count).IsEqualTo(3);
    }

    /// <summary>A reset list is a fresh one, never a default instance two configurations would share.</summary>
    [Test]
    public async Task ResetListsAreNotShared()
    {
        var binding = (SettingBinding<List<StatusMeter>>)BuiltInSettings.Text.Fields
            .Single(f => f.Id == BuiltInSettings.MetersField).Binding!;
        var a = new AppConfiguration();
        var b = new AppConfiguration();
        binding.Reset(a);
        binding.Reset(b);

        await Assert.That(ReferenceEquals(a.Text.StatusMeters, b.Text.StatusMeters)).IsFalse();
    }

    [Test]
    public async Task TheSearchFindsLabelsKeywordsAndTitles()
    {
        var byLabel = SettingsSearch.Find(BuiltInSettings.All, "METER");
        await Assert.That(byLabel.Select(m => m.Field?.Id)).Contains(BuiltInSettings.MetersField);
        var meters = byLabel.First(m => m.Field?.Id == BuiltInSettings.MetersField);
        await Assert.That(meters.Start).IsEqualTo(0);
        await Assert.That(meters.Length).IsEqualTo(5);

        var byKeyword = SettingsSearch.Find(BuiltInSettings.All, "opacity");
        await Assert.That(byKeyword.Single().Field!.Id).IsEqualTo("transparency");
        await Assert.That(byKeyword.Single().Start).IsEqualTo(-1);

        var byTitle = SettingsSearch.Find(BuiltInSettings.All, "alias");
        await Assert.That(byTitle.Single().Field).IsNull();
        await Assert.That(byTitle.Single().Section.Id).IsEqualTo("aliases");

        await Assert.That(SettingsSearch.Find(BuiltInSettings.All, "  ")).IsEmpty();
    }

    [Test]
    public async Task MatchesAreCountedBySection()
    {
        var counts = SettingsSearch.CountBySection(SettingsSearch.Find(BuiltInSettings.All, "volume"));

        await Assert.That(counts["text"]).IsEqualTo(3);
        await Assert.That(counts.ContainsKey("input")).IsFalse();
    }

    /// <summary>Every settings screen F2–F10 is a section with its key, so the search can list and open all of them.</summary>
    [Test]
    public async Task EveryScreenIsASectionWithItsKey()
    {
        var registry = new ContributionRegistry();
        BuiltInSettings.Register(registry.SettingsSections);

        await Assert.That(registry.SettingsSections.Ordered.Select(s => s.Key!))
            .IsEquivalentTo(new[] { "F5", "F2", "F3", "F4", "F6", "F7", "F8", "F9", "F10" });
    }
}
