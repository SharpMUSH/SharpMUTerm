using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// What a trigger set is and who uses it, said one way on every surface, and the set operations the window's list pages
/// run: each one commit, each replacing the lists it changes.
/// </summary>
public class TriggerSetEditingTests
{
    private static AppConfiguration Config()
    {
        var config = new AppConfiguration();
        config.Worlds.Clear();
        config.TriggerSets.Clear();
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Aetherfall",
            Host = "aether.example",
            Characters =
            {
                new CharacterDefinition { Name = "Corvid", TriggerSets = ["Comms", "Trade"] },
                new CharacterDefinition { Name = "Rookery", TriggerSets = ["Comms"] },
                new CharacterDefinition { Name = "Thistle" },
                new CharacterDefinition { Name = "Ann", TriggerSets = ["comms"] },
            },
        });
        config.TriggerSets.Add(new TriggerSet { Name = "Comms", Triggers = { new Trigger { Name = "a", Pattern = "a" }, new Trigger { Name = "b", Pattern = "b" } } });
        config.TriggerSets.Add(new TriggerSet { Name = "Trade" });
        config.TriggerSets.Add(new TriggerSet { Name = "Combat", Aliases = { new Alias { Name = "k", Pattern = "k" } } });
        return config;
    }

    private static (SettingsForm Form, List<string?> Commits) Form(AppConfiguration config, CharacterDefinition? character)
    {
        var commits = new List<string?>();
        var form = new SettingsForm(
            BuiltInSettings.All, config, change => change(), _ => { }, "triggers", isDark: true, character: character)
        {
            CommitAs = (change, what) =>
            {
                change();
                commits.Add(what);
            },
        };
        return (form, commits);
    }

    [Test]
    public async Task UsageNamesTheCharactersThatUseASetMatchedAsTheResolverMatches()
    {
        var config = Config();

        await Assert.That(TriggerSetEditing.Usage(config.Worlds, "Comms")).IsEqualTo("used by Corvid and 2 more");
        await Assert.That(TriggerSetEditing.Usage(config.Worlds, "Trade")).IsEqualTo("used by Corvid");
        await Assert.That(TriggerSetEditing.Usage(config.Worlds, "Combat")).IsEqualTo(TriggerSetEditing.Unused);
        await Assert.That(TriggerSetEditing.IsUsed(config.Worlds, "Combat")).IsFalse();
    }

    [Test]
    public async Task HoldsCountsEveryKindAndSaysEmpty()
    {
        var config = Config();

        await Assert.That(TriggerSetEditing.Holds(config.TriggerSets[0])).IsEqualTo("2 triggers");
        await Assert.That(TriggerSetEditing.Holds(config.TriggerSets[1])).IsEqualTo("empty");
        await Assert.That(TriggerSetEditing.Holds(config.TriggerSets[2])).IsEqualTo("1 alias");
    }

    [Test]
    public async Task ANewSetIsNamedApartAndUsedByTheCharacterInFrontInOneCommit()
    {
        var config = Config();
        var thistle = config.Worlds[0].Characters[2];
        var sets = config.TriggerSets;
        var (form, commits) = Form(config, thistle);

        var made = form.NewSet();
        var again = form.NewSet();

        await Assert.That(config.TriggerSets).IsNotSameReferenceAs(sets);
        await Assert.That(made.Name).IsEqualTo(TriggerSetEditing.NewSetName);
        await Assert.That(again.Name).IsEqualTo(TriggerSetEditing.NewSetName + " 2");
        await Assert.That(thistle.TriggerSets).IsEquivalentTo(new[] { "New Set", "New Set 2" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(commits.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RenamingASetRewritesEveryReferenceInPlaceAndRefusesATakenName()
    {
        var config = Config();
        var corvid = config.Worlds[0].Characters[0];
        var (form, _) = Form(config, corvid);

        await Assert.That(form.RenameSet(config.TriggerSets[0], "trade")).IsNotNull();
        await Assert.That(form.RenameSet(config.TriggerSets[0], "  ")).IsEqualTo(TriggerSetEditing.BlankName);
        await Assert.That(form.RenameSet(config.TriggerSets[0], "Channels")).IsNull();

        await Assert.That(config.TriggerSets[0].Name).IsEqualTo("Channels");
        await Assert.That(corvid.TriggerSets).IsEquivalentTo(new[] { "Channels", "Trade" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(config.Worlds[0].Characters[3].TriggerSets.Single()).IsEqualTo("Channels");
    }

    [Test]
    public async Task DeletingASetTakesEveryCharactersUseOfItAndSaysWhatGoes()
    {
        var config = Config();
        var (form, _) = Form(config, null);
        var comms = config.TriggerSets[0];

        await Assert.That(form.SetDeletion(comms)).IsEqualTo("trigger set Comms and its 2 rules, 3 characters using it");
        await Assert.That(form.DeleteSet(comms)).IsTrue();

        await Assert.That(config.TriggerSets.Select(s => s.Name)).IsEquivalentTo(new[] { "Trade", "Combat" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(config.Worlds[0].Characters.SelectMany(c => c.TriggerSets)).IsEquivalentTo(new[] { "Trade" });
    }

    [Test]
    public async Task UsingASetAppendsItSoItLosesAClashAndUnusingRemovesIt()
    {
        var config = Config();
        var rookery = config.Worlds[0].Characters[1];

        TriggerSetEditing.Use(rookery, "Combat", on: true);
        await Assert.That(rookery.TriggerSets).IsEquivalentTo(new[] { "Comms", "Combat" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        TriggerSetEditing.Use(rookery, "COMMS", on: false);
        await Assert.That(rookery.TriggerSets).IsEquivalentTo(new[] { "Combat" });
    }
}
