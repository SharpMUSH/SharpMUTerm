using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// Undo of committed settings edits: a step is the difference one edit made, written back onto the configuration as it
/// is now and per section, into the live objects rather than over them, and refused out loud when there is none.
/// </summary>
public class SettingsHistoryTests
{
    private static AppConfiguration Configuration()
    {
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Aetherfall", Host = "aether.example", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Corvid", Password = "secret" });
        config.Worlds.Add(world);
        config.TriggerSets.Clear();
        config.TriggerSets.Add(new TriggerSet
        {
            Name = "public",
            Triggers = { new Trigger { Name = "a", Pattern = "a" }, new Trigger { Name = "b", Pattern = "b" }, new Trigger { Name = "c", Pattern = "c" } },
        });
        return config;
    }

    private static void Edit(SettingsHistory history, string section, string what, Action change)
    {
        var before = history.Capture();
        change();
        history.Record(section, before, what);
    }

    [Test]
    public async Task AnUndoPutsTheValueBackAndARedoMakesItAgain()
    {
        var config = Configuration();
        var history = new SettingsHistory(config);
        var world = config.Worlds[0];
        Edit(history, "worlds", "port", () => world.Port = 4201);

        var undone = history.Undo("worlds");
        await Assert.That(undone.Done).IsTrue();
        await Assert.That(undone.Note).IsEqualTo("Undid port.");
        await Assert.That(world.Port).IsEqualTo(4000);
        await Assert.That(config.Worlds[0]).IsSameReferenceAs(world).Because("a session holds this world; it stays that object");

        var redone = history.Redo("worlds");
        await Assert.That(redone.Note).IsEqualTo("Redid port.");
        await Assert.That(world.Port).IsEqualTo(4201);
    }

    [Test]
    public async Task NothingToUndoIsSaidAndChangesNothing()
    {
        var config = Configuration();
        var history = new SettingsHistory(config, id => id == "options" ? "Text & display" : null);
        var result = history.Undo("options");
        await Assert.That(result.Done).IsFalse();
        await Assert.That(result.Note).IsEqualTo("Nothing to undo in Text & display.");
        await Assert.That(history.Redo("options").Note).IsEqualTo("Nothing to redo in Text & display.");
    }

    [Test]
    public async Task AChangeThatChangedNothingIsNotRecorded()
    {
        var config = Configuration();
        var history = new SettingsHistory(config);
        var before = history.Capture();
        await Assert.That(history.Record("options", before, "nothing")).IsFalse();
        await Assert.That(history.CanUndo("options")).IsFalse();
    }

    [Test]
    public async Task EachSectionUndoesOnlyItsOwnEdits()
    {
        var config = Configuration();
        var history = new SettingsHistory(config);
        Edit(history, "options", "tab width", () => config.Text.TabWidth = 8);
        Edit(history, "worlds", "host", () => config.Worlds[0].Host = "elsewhere.example");

        // F7's edit is undone and F5's, made after it, is left alone.
        history.Undo("options");
        await Assert.That(config.Text.TabWidth).IsEqualTo(new TextSettings().TabWidth);
        await Assert.That(config.Worlds[0].Host).IsEqualTo("elsewhere.example");
        await Assert.That(history.CanUndo("options")).IsFalse();
        await Assert.That(history.CanUndo("worlds")).IsTrue();
    }

    [Test]
    public async Task ANewEditForgetsWhatCouldBeRedone()
    {
        var config = Configuration();
        var history = new SettingsHistory(config);
        Edit(history, "worlds", "port", () => config.Worlds[0].Port = 1);
        history.Undo("worlds");
        Edit(history, "worlds", "port", () => config.Worlds[0].Port = 2);
        await Assert.That(history.CanRedo("worlds")).IsFalse();
    }

    [Test]
    public async Task AHistoryKeepsItsDepthAndNoMore()
    {
        var config = Configuration();
        var history = new SettingsHistory(config);
        for (var i = 1; i <= SettingsHistory.Depth + 10; i++)
        {
            var port = 5000 + i;
            Edit(history, "worlds", "port", () => config.Worlds[0].Port = port);
        }

        var undone = 0;
        while (history.Undo("worlds").Done)
        {
            undone++;
        }

        await Assert.That(undone).IsEqualTo(SettingsHistory.Depth);
        await Assert.That(config.Worlds[0].Port).IsEqualTo(5010);
    }

    [Test]
    public async Task ADeletedItemComesBackAndTheOthersKeepTheirInstances()
    {
        var config = Configuration();
        var history = new SettingsHistory(config);
        var set = config.TriggerSets[0];
        var (a, c) = (set.Triggers[0], set.Triggers[2]);
        var list = set.Triggers;
        Edit(history, "triggers", "deleting a trigger", () => set.Triggers = set.Triggers.Where(t => t.Name != "b").ToList());

        history.Undo("triggers");
        await Assert.That(set.Triggers.Select(t => t.Name)).IsEquivalentTo(new[] { "a", "b", "c" });
        await Assert.That(set.Triggers[0]).IsSameReferenceAs(a);
        await Assert.That(set.Triggers[2]).IsSameReferenceAs(c);
        await Assert.That(set.Triggers).IsNotSameReferenceAs(list).Because("a list is replaced, never edited in place");
    }

    [Test]
    public async Task AMoveIsUndoneWithEveryItemStillItself()
    {
        var config = Configuration();
        var history = new SettingsHistory(config);
        var set = config.TriggerSets[0];
        var (a, b) = (set.Triggers[0], set.Triggers[1]);
        Edit(history, "triggers", "moving it", () => set.Triggers = [b, a, set.Triggers[2]]);

        history.Undo("triggers");
        await Assert.That(set.Triggers[0]).IsSameReferenceAs(a);
        await Assert.That(set.Triggers[1]).IsSameReferenceAs(b);
        await Assert.That(a.Name).IsEqualTo("a").Because("a reorder swaps instances, not their fields");
    }

    [Test]
    public async Task APasswordEditUndoesThoughTheDocumentNeverCarriesIt()
    {
        var config = Configuration();
        var history = new SettingsHistory(config);
        var character = config.Worlds[0].Characters[0];
        Edit(history, "characters", "password", () => character.Password = "changed");

        history.Undo("characters");
        await Assert.That(character.Password).IsEqualTo("secret");
    }

    [Test]
    public async Task AnUndoAfterTheListWasResizedPutsTheItemBackWhole()
    {
        var config = Configuration();
        var history = new SettingsHistory(config);
        var set = config.TriggerSets[0];
        Edit(history, "triggers", "pattern", () => set.Triggers[2].Pattern = "changed");

        // Made since, on another screen: the edited item is gone. Undo puts the list back as the step knew it.
        set.Triggers = [set.Triggers[0]];
        history.Undo("triggers");
        await Assert.That(set.Triggers.Select(t => t.Pattern)).IsEquivalentTo(new[] { "a", "b", "c" });
    }

    [Test]
    public async Task TheFormNamesEachChangeAndAsksTheClientToUndo()
    {
        var config = Configuration();
        var named = new List<string?>();
        var asked = new List<(string, bool)>();
        var form = new SettingsForm(BuiltInSettings.All, config, change => change(), _ => { }, "worlds", isDark: true)
        {
            CommitAs = (change, what) =>
            {
                change();
                named.Add(what);
            },
            UndoRedo = (section, redo) => asked.Add((section, redo)),
        };

        var port = BuiltInSettings.World.Fields.OfType<SharpMUTerm.Client.Contributions.NumberField>().Single(f => f.Id == "port");
        form.SetNumber(port, 4201);
        await Assert.That(named).IsEquivalentTo(new string?[] { "port" });
        await Assert.That(form.Undo("worlds")).IsTrue();
        await Assert.That(form.Redo("worlds")).IsTrue();
        await Assert.That(asked).IsEquivalentTo(new[] { ("worlds", false), ("worlds", true) });

        var plain = new SettingsForm(BuiltInSettings.All, config, change => change(), _ => { }, "worlds", isDark: true);
        await Assert.That(plain.Undo("worlds")).IsFalse().Because("a client that keeps no history is not asked");
    }
}
