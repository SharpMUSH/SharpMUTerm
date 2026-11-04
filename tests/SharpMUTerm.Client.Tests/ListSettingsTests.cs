using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// The list declaration kind: F2, F3, F4 and F6 are sections over trigger sets whose item fields are ordinary
/// descriptors bound to the item itself. Every change goes through the form's one commit, a package's set refuses
/// every edit and can be copied to edit, and the search finds item fields.
/// </summary>
public class ListSettingsTests
{
    private static (AppConfiguration Config, TriggerSet Own, TriggerSet Package) Configuration()
    {
        var config = new AppConfiguration();
        config.TriggerSets.Clear();
        var own = new TriggerSet
        {
            Name = "public",
            Triggers =
            {
                new Trigger { Name = "first", Pattern = "a" },
                new Trigger { Name = "second", Pattern = "b" },
                new Trigger { Name = "third", Pattern = "c" },
            },
        };
        config.TriggerSets.Add(own);
        var package = new TriggerSet { Name = "combat", Triggers = { new Trigger { Name = "theirs", Pattern = "x" } } };
        config.Packages =
        [
            new InstalledPackage(new PackageManifest { Id = "pack", Name = "Pack", Version = "1.0.0" }, [package]),
        ];
        return (config, own, package);
    }

    private static SettingsForm Form(AppConfiguration config, Action count, object? item = null, string section = "triggers") =>
        new(BuiltInSettings.All, config, change => { change(); count(); }, _ => { }, section, isDark: true, item: item);

    [Test]
    public async Task TheListScreensAreDeclaredWithACollection()
    {
        foreach (var id in new[] { "triggers", "aliases", "keypad", "timers" })
        {
            var section = BuiltInSettings.All.Single(s => s.Id == id);
            await Assert.That(section.Collection).IsNotNull();
            await Assert.That(section.Scope).IsEqualTo(SettingScope.Item);
            await Assert.That(SettingsForm.IsDeclared(section)).IsTrue();
            await Assert.That(section.Collection!.Fields.All(f => f.Binding?.Scope == SettingScope.Item)).IsTrue();
        }

        var triggers = BuiltInSettings.Triggers.Collection!.Fields.Select(f => f.Id).ToList();
        await Assert.That(triggers).IsEquivalentTo(
            new[] { "enabled", "name", "match", "pattern", "set", "route", "fg", "bg", "attributes", "gag", "rewrite", "respond", "script", "stop", "case" });
    }

    [Test]
    public async Task AnItemFieldEditsTheItemAndCommitsOnce()
    {
        var (config, own, _) = Configuration();
        var commits = 0;
        var second = own.Triggers[1];
        var form = Form(config, () => commits++, second);
        var collection = BuiltInSettings.Triggers.Collection!;
        var gag = (BoolField)collection.Field("gag");

        await Assert.That(form.Set((SettingBinding<bool>)gag.Binding!, true)).IsTrue();
        await Assert.That(second.Actions.Gag).IsTrue();
        await Assert.That(own.Triggers[0].Actions.Gag).IsFalse();
        await Assert.That(commits).IsEqualTo(1);

        var pattern = (TextField)collection.Field("pattern");
        await Assert.That(form.SetText(pattern, "you gain *")).IsNull();
        await Assert.That(second.Pattern).IsEqualTo("you gain *");
        await Assert.That(commits).IsEqualTo(2);
    }

    [Test]
    public async Task APatternThatIsNotARegexIsRefusedForARegexRule()
    {
        var (config, own, _) = Configuration();
        var commits = 0;
        var form = Form(config, () => commits++, own.Triggers[0]);
        var pattern = (TextField)BuiltInSettings.Triggers.Collection!.Field("pattern");

        await Assert.That(form.SetText(pattern, "(")).IsNotNull();
        await Assert.That(own.Triggers[0].Pattern).IsEqualTo("a");
        await Assert.That(commits).IsEqualTo(0);
    }

    [Test]
    public async Task AddDuplicateDeleteAndMoveEditTheSet()
    {
        var (config, own, _) = Configuration();
        var commits = 0;
        var form = Form(config, () => commits++);
        var section = BuiltInSettings.Triggers;

        var added = form.Add(section, own) as Trigger;
        await Assert.That(added).IsNotNull();
        await Assert.That(own.Triggers[^1]).IsSameReferenceAs(added!);
        await Assert.That(form.Item).IsSameReferenceAs(added);

        var copy = form.Duplicate(section, own.Triggers[0]) as Trigger;
        await Assert.That(own.Triggers[1]).IsSameReferenceAs(copy!);
        await Assert.That(copy!.Name).IsEqualTo("first copy");

        var third = own.Triggers.Single(t => t.Name == "third");
        await Assert.That(form.Move(section, third, -1)).IsTrue();
        await Assert.That(own.Triggers.Select(t => t.Name).ToList())
            .IsEquivalentTo(new[] { "first", "first copy", "third", "second", added!.Name }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(form.Move(section, own.Triggers[0], -1)).IsFalse();

        await Assert.That(form.Delete(section, copy)).IsTrue();
        await Assert.That(own.Triggers.Contains(copy)).IsFalse();
        await Assert.That(commits).IsEqualTo(4);
    }

    [Test]
    public async Task APackagesSetRefusesEveryEditAndCanBeCopiedToEdit()
    {
        var (config, _, package) = Configuration();
        var commits = 0;
        var theirs = package.Triggers[0];
        var form = Form(config, () => commits++, theirs);
        var section = BuiltInSettings.Triggers;
        var collection = section.Collection!;

        var groups = form.Groups(section);
        await Assert.That(groups.Single(g => ReferenceEquals(g.Set, package)).ReadOnly).IsTrue();
        await Assert.That(form.IsReadOnly(theirs)).IsTrue();
        await Assert.That(form.Set((SettingBinding<bool>)collection.Field("gag").Binding!, true)).IsFalse();
        await Assert.That(form.SetText((TextField)collection.Field("pattern"), "y")).IsEqualTo(SettingsForm.ReadOnlyRefusal);
        await Assert.That(form.Add(section, package)).IsNull();
        await Assert.That(form.Duplicate(section, theirs)).IsNull();
        await Assert.That(form.Delete(section, theirs)).IsFalse();
        await Assert.That(form.SetEnabled(section, theirs, false)).IsFalse();
        await Assert.That(theirs.Actions.Gag).IsFalse();
        await Assert.That(theirs.Pattern).IsEqualTo("x");
        await Assert.That(commits).IsEqualTo(0);

        await Assert.That(form.CopyToEdit(package)).IsTrue();
        var copied = config.TriggerSets.Single(s => s.Name.StartsWith("combat", StringComparison.Ordinal));
        await Assert.That(copied.Triggers.Single().Name).IsEqualTo("theirs");
        await Assert.That(copied.Triggers.Single()).IsNotSameReferenceAs(theirs);
        await Assert.That(commits).IsEqualTo(1);
    }

    [Test]
    public async Task MovingARuleToAnotherSetMovesIt()
    {
        var (config, own, _) = Configuration();
        var other = new TriggerSet { Name = "other" };
        config.TriggerSets.Add(other);
        var commits = 0;
        var rule = own.Triggers[0];
        var form = Form(config, () => commits++, rule);
        var set = (ChoiceField)BuiltInSettings.Triggers.Collection!.Field("set");

        await Assert.That(form.Choices(set)).Contains("other");
        await Assert.That(form.Set((SettingBinding<string>)set.Binding!, "other")).IsTrue();
        await Assert.That(other.Triggers.Single()).IsSameReferenceAs(rule);
        await Assert.That(own.Triggers.Contains(rule)).IsFalse();
    }

    [Test]
    public async Task AKeyBindingRefusesAChordTheHostCannotDeliverOrAnotherHolds()
    {
        var config = new AppConfiguration();
        config.TriggerSets.Clear();
        var set = new TriggerSet
        {
            Name = "keys",
            Macros = { new Macro { Name = "north", Key = "F12", Command = "n" }, new Macro { Name = "south", Key = "F11", Command = "s" } },
        };
        config.TriggerSets.Add(set);
        var context = new SettingsContext([], [], key => key == "Ctrl+H" ? "arrives as Backspace" : null, ["F10", "F11", "F12"]);
        var form = new SettingsForm(BuiltInSettings.All, config, change => change(), _ => { }, "keypad", isDark: true, context: context, item: set.Macros[1]);
        var key = (KeyField)BuiltInSettings.Keypad.Collection!.Field("key");

        await Assert.That(form.SetKey(key, "Ctrl+H")).IsEqualTo("arrives as Backspace");
        await Assert.That(form.SetKey(key, "F12")).Contains("north");
        await Assert.That(form.SetKey(key, "F10")).IsNull();
        await Assert.That(set.Macros[1].Key).IsEqualTo("F10");
    }

    [Test]
    public async Task TheLiveTestRunsTheSelectedRuleOverTheContextsLines()
    {
        var config = new AppConfiguration();
        config.TriggerSets.Clear();
        var rule = new Trigger { Name = "xp", Pattern = "You gain * experience", MatchType = TriggerMatchType.Wildcard };
        config.TriggerSets.Add(new TriggerSet { Name = "s", Triggers = { rule } });
        var context = new SettingsContext([], ["You gain 40 experience points.", "A guard nods.", "You gain 7 experience points."], _ => null, []);
        var form = new SettingsForm(BuiltInSettings.All, config, change => change(), _ => { }, "triggers", isDark: true, context: context, item: rule);

        var result = form.Preview();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.Matched).IsEqualTo(2);
        await Assert.That(form.Preview("dragon")!.Value.Matched).IsEqualTo(0);
    }
}
