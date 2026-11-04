using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// F5's world fields and F9's character fields, declared with scoped bindings, and the theme field: a scoped field reads
/// and writes the world or character it is aimed at and no other, resets to a fresh world's or character's default,
/// refuses what the cell screen refuses, and is found by the search; the theme field keeps a customised theme when
/// another is tried.
/// </summary>
public class ScopedSettingsTests
{
    private static AppConfiguration TwoWorlds()
    {
        var config = new AppConfiguration();
        config.Worlds.Clear();
        config.Worlds.Add(new WorldDefinition { Name = "Aetherfall", Host = "aether.example", Characters = { new CharacterDefinition { Name = "Corvid" } } });
        config.Worlds.Add(new WorldDefinition { Name = "Brightwater", Host = "bright.example", Characters = { new CharacterDefinition { Name = "Wren" } } });
        return config;
    }

    private static SettingsField World(string id) => BuiltInSettings.World.Fields.Single(f => f.Id == id);

    private static SettingsField Character(string id) => BuiltInSettings.Character.Fields.Single(f => f.Id == id);

    /// <summary>
    /// The note F5 draws beside <c>join wraps</c> reaches the window through the form, for the world being edited only, in
    /// the cells' own sentence, and goes once the setting is taken.
    /// </summary>
    [Test]
    public async Task AWorldNoteIsShownForItsWorldUntilTheFieldIsChanged()
    {
        var config = TwoWorlds();
        var advice = WorldFieldValues.JoinWrapsAdvice(78, 60);
        var form = new SettingsForm(BuiltInSettings.All, config, change => change(), _ => { }, "worlds", isDark: true)
        {
            WorldNotes = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [WorldFieldValues.JoinWrapsFieldId] = new Dictionary<string, string> { ["Aetherfall"] = advice },
            },
        };
        var join = World(WorldFieldValues.JoinWrapsFieldId);

        await Assert.That(advice).IsEqualTo("recommended · wraps at 78, pane is 60");
        await Assert.That(form.Note(join)).IsEqualTo(advice);
        await Assert.That(form.Note(World("emoji"))).IsNull();

        form.Choose(form.Subjects(SettingScope.World)[1]);
        await Assert.That(form.Note(join)).IsNull().Because("Brightwater's pane is not ragged");

        form.Choose(form.Subjects(SettingScope.World)[0]);
        config.Worlds[0].JoinWrappedLines = true;
        await Assert.That(form.Note(join)).IsNull().Because("a recommendation that has been taken has nothing left to say");
    }

    [Test]
    public async Task TheWorldAndCharacterSectionsAreScoped()
    {
        await Assert.That(BuiltInSettings.World.Scope).IsEqualTo(SettingScope.World);
        await Assert.That(BuiltInSettings.Character.Scope).IsEqualTo(SettingScope.Character);
        await Assert.That(BuiltInSettings.Text.Scope).IsEqualTo(SettingScope.App);
        await Assert.That(BuiltInSettings.World.Fields.All(f => f.Binding!.Scope == SettingScope.World)).IsTrue();
        await Assert.That(BuiltInSettings.Character.Fields.All(f => f.Binding!.Scope == SettingScope.Character)).IsTrue();
    }

    [Test]
    public async Task AWorldFieldWritesTheWorldItIsAimedAtAndResetsToAFreshWorldsDefault()
    {
        var config = TwoWorlds();
        var second = config.Worlds[1];
        var grab = (SettingBinding<string?>)World("edit-grab").Binding!;
        var target = new SettingTarget(config, second);

        grab.Set(target, "FugueEdit > ");

        await Assert.That(second.GrabPrefix).IsEqualTo("FugueEdit > ");
        await Assert.That(config.Worlds[0].GrabPrefix).IsNull();
        await Assert.That(grab.IsDefault(target)).IsFalse();
        await Assert.That(grab.IsDefault(new SettingTarget(config, config.Worlds[0]))).IsTrue();

        grab.Reset(target);
        await Assert.That(second.GrabPrefix).IsEqualTo(new WorldDefinition().GrabPrefix);
    }

    [Test]
    public async Task AScopedFieldWithNoSubjectSaysSoAndChangesNothing()
    {
        var config = new AppConfiguration();
        var grab = World("edit-grab").Binding!;

        await Assert.That(grab.IsDefault(config)).IsTrue();
        await Assert.That(grab.Text(config)).IsEqualTo("per world");
        await Assert.That(Character("tint").Binding!.Text(config)).IsEqualTo("per character");
    }

    [Test]
    public async Task IdentityFieldsAreNotReset()
    {
        foreach (var id in new[] { "name", "host", "port" })
        {
            await Assert.That(World(id).Binding!.Resettable).IsFalse();
        }

        await Assert.That(Character("name").Binding!.Resettable).IsFalse();
        await Assert.That(Character("password").Binding!.Resettable).IsFalse();
        await Assert.That(World("edit-grab").Binding!.Resettable).IsTrue();
    }

    [Test]
    public async Task TheFormEditsTheChosenSubjectThroughOneCommitEach()
    {
        var config = TwoWorlds();
        var commits = 0;
        var form = new SettingsForm(BuiltInSettings.All, config, change => { change(); commits++; }, _ => { }, "worlds", isDark: true);

        await Assert.That(form.Subject(SettingScope.World)!.World).IsSameReferenceAs(config.Worlds[0]);
        form.Choose(form.Subjects(SettingScope.World)[1]);
        await Assert.That(form.Subject(SettingScope.Character)!.Character!.Name).IsEqualTo("Wren");

        var grab = (TextField)World("edit-grab");
        await Assert.That(form.SetText(grab, "  Edit> ")).IsNull();
        await Assert.That(commits).IsEqualTo(1);
        await Assert.That(config.Worlds[1].GrabPrefix).IsEqualTo("Edit>");
        await Assert.That(config.Worlds[0].GrabPrefix).IsNull();

        form.Reset(grab);
        await Assert.That(commits).IsEqualTo(2);
        await Assert.That(config.Worlds[1].GrabPrefix).IsNull();

        var tint = (SettingBinding<string>)Character("tint").Binding!;
        form.Set(tint, "Moss");
        await Assert.That(config.Worlds[1].Characters[0].Tint).IsEqualTo(PaneTint.Moss);
        await Assert.That(config.Worlds[0].Characters[0].Tint).IsEqualTo(PaneTint.None);
    }

    [Test]
    public async Task TheFormRefusesWhatTheCellScreenRefuses()
    {
        var config = TwoWorlds();
        var commits = 0;
        var form = new SettingsForm(BuiltInSettings.All, config, change => { change(); commits++; }, _ => { }, "worlds", isDark: true);

        await Assert.That(form.SetText((TextField)World("host"), "   ")).IsNotNull();
        await Assert.That(form.SetText((TextField)World("walk-fail"), "(unclosed")).IsNotNull();
        await Assert.That(commits).IsEqualTo(0);
        await Assert.That(config.Worlds[0].Host).IsEqualTo("aether.example");
    }

    [Test]
    public async Task AConnectLineLeftUnsetShowsTheDefaultTemplate()
    {
        var config = TwoWorlds();
        var form = new SettingsForm(BuiltInSettings.All, config, change => change(), _ => { }, "logging", isDark: true);
        var connect = (TextField)Character("connect");

        await Assert.That(form.Text(connect)).IsEqualTo(connect.Fallback!);
    }

    [Test]
    public async Task TheSearchFindsWorldAndCharacterFields()
    {
        var grab = SettingsSearch.Find(BuiltInSettings.All, "edit grab");
        await Assert.That(grab.Any(m => m.Section.Id == BuiltInSettings.World.Id && m.Field?.Id == "edit-grab")).IsTrue();

        var tint = SettingsSearch.Find(BuiltInSettings.All, "pane colour");
        await Assert.That(tint.Any(m => m.Section.Id == BuiltInSettings.Character.Id && m.Field?.Id == "tint")).IsTrue();

        var theme = SettingsSearch.Find(BuiltInSettings.All, "solarized");
        await Assert.That(theme.Any(m => m.Field?.Id == BuiltInSettings.ThemeFieldId)).IsTrue();
    }

    [Test]
    public async Task PickingAThemeSetsItsNameAndColours()
    {
        var config = new AppConfiguration();
        ThemeChoices.Apply(config, "Light");

        await Assert.That(config.ThemeName).IsEqualTo("Light");
        await Assert.That(config.Theme.Name).IsEqualTo("Light");
        await Assert.That(ThemeChoices.Current(config)).IsEqualTo("Light");
        await Assert.That(ThemeChoices.Names(config)).Contains("Solarized Dark");
    }

    [Test]
    public async Task TryingAnotherThemeKeepsTheCustomisedOne()
    {
        var config = new AppConfiguration();
        var mine = config.Theme.Clone();
        mine.Name = "Midnight";
        config.Theme = mine;
        config.ThemeName = "Midnight";
        await Assert.That(ThemeChoices.Current(config)).IsEqualTo("Midnight");

        ThemeChoices.Apply(config, "Dark");

        await Assert.That(ThemeChoices.Current(config)).IsEqualTo("Dark");
        await Assert.That(config.CustomThemes.Select(t => t.Name)).Contains("Midnight");
        await Assert.That(ThemeChoices.Names(config)).Contains("Midnight");

        ThemeChoices.Apply(config, "Midnight");
        await Assert.That(ThemeChoices.Current(config)).IsEqualTo("Midnight");
    }

    [Test]
    public async Task ANameThePickerDoesNotListChangesNothing()
    {
        var config = new AppConfiguration();
        ThemeChoices.Apply(config, "No Such Theme");
        await Assert.That(config.ThemeName).IsEqualTo("Dark");
    }
}
