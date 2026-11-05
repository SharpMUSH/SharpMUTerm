using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Telnet.Mssp;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// The window's Worlds &amp; Characters page is drawn from <see cref="SettingsForm"/>'s world operations: each one is a
/// single commit through the funnel, replaces the list it changes rather than editing it (the window reads from another
/// thread), and leaves the form choosing what the page should show next.
/// </summary>
public class WorldEditingTests
{
    private static AppConfiguration TwoWorlds()
    {
        var config = new AppConfiguration();
        config.Worlds.Clear();
        config.Worlds.Add(new WorldDefinition { Name = "Aetherfall", Host = "aether.example", Port = 4201, Characters = { new CharacterDefinition { Name = "Corvid" } } });
        config.Worlds.Add(new WorldDefinition { Name = "Brightwater", Host = "bright.example", Characters = { new CharacterDefinition { Name = "Wren" } } });
        return config;
    }

    private static (SettingsForm Form, List<string?> Commits) Form(AppConfiguration config, List<string>? connected = null, List<string>? connects = null)
    {
        var commits = new List<string?>();
        var form = new SettingsForm(BuiltInSettings.All, config, change => change(), _ => { }, "worlds", isDark: true)
        {
            CommitAs = (change, what) =>
            {
                change();
                commits.Add(what);
            },
            ConnectedKeys = connected is null ? null : () => connected,
            Connect = connects is null ? null : connects.Add,
        };
        return (form, commits);
    }

    [Test]
    public async Task ANewNameNeverTakesOneInUse()
    {
        await Assert.That(WorldEditing.Available(["New World"], "New World")).IsEqualTo("New World 2");
        await Assert.That(WorldEditing.Available(["new world", "New World 2"], "New World")).IsEqualTo("New World 3");
        await Assert.That(WorldEditing.Available(["Other"], "New World")).IsEqualTo("New World");
    }

    [Test]
    public async Task AddingAWorldReplacesTheListInOneCommitAndChoosesIt()
    {
        var config = TwoWorlds();
        var before = config.Worlds;
        var (form, commits) = Form(config);

        var added = form.AddWorld();

        await Assert.That(config.Worlds).IsNotSameReferenceAs(before);
        await Assert.That(config.Worlds.Select(w => w.Name)).IsEquivalentTo(new[] { "Aetherfall", "Brightwater", "New World" });
        await Assert.That(form.ChosenWorld).IsSameReferenceAs(added);
        await Assert.That(commits).IsEquivalentTo(new string?[] { "adding a world" });
    }

    [Test]
    public async Task DeletingTheChosenWorldMovesTheChoiceToItsNeighbour()
    {
        var config = TwoWorlds();
        var (form, commits) = Form(config);
        form.Choose(config.Worlds[0], null);

        await Assert.That(form.DeleteWorld(config.Worlds[0])).IsTrue();

        await Assert.That(config.Worlds.Select(w => w.Name)).IsEquivalentTo(new[] { "Brightwater" });
        await Assert.That(form.ChosenWorld!.Name).IsEqualTo("Brightwater");
        await Assert.That(commits.Single()).IsEqualTo("deleting " + WorldEditing.Deletion(new WorldDefinition { Name = "Aetherfall", Characters = { new CharacterDefinition { Name = "Corvid" } } }));
    }

    [Test]
    public async Task CharactersAreAddedCopiedMovedAndDeletedWithinTheirWorld()
    {
        var config = TwoWorlds();
        var world = config.Worlds[0];
        var (form, commits) = Form(config);

        var added = form.AddCharacter(world);
        var copy = form.DuplicateCharacter(world.Characters[0])!;
        await Assert.That(world.Characters.Select(c => c.Name)).IsEquivalentTo(new[] { "Corvid", "Corvid copy", "New Character" });
        await Assert.That(form.ChosenCharacter).IsSameReferenceAs(copy);

        await Assert.That(form.MoveCharacter(added, -1)).IsTrue();
        await Assert.That(form.MoveCharacter(world.Characters[0], -1)).IsFalse().Because("the first cannot go further up");
        await Assert.That(world.Characters.Select(c => c.Name)).IsEquivalentTo(new[] { "Corvid", "New Character", "Corvid copy" });

        await Assert.That(form.DeleteCharacter(copy)).IsTrue();
        await Assert.That(form.ChosenCharacter).IsNull();
        await Assert.That(form.ChosenWorld).IsSameReferenceAs(world);
        await Assert.That(commits.Count).IsEqualTo(4);
        await Assert.That(config.Worlds[1].Characters.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ASetIsAppendedSoItLosesClashesToTheSetsAlreadyOn()
    {
        var config = TwoWorlds();
        config.TriggerSets.Add(new TriggerSet { Name = "Combat" });
        config.TriggerSets.Add(new TriggerSet { Name = "Chat" });
        var character = config.Worlds[0].Characters[0];
        character.TriggerSets = ["Chat"];
        var (form, _) = Form(config);

        form.Assign(character, config.TriggerSets.Single(s => s.Name == "Combat"), on: true);
        var rows = form.Assignments(character);

        await Assert.That(character.TriggerSets).IsEquivalentTo(new[] { "Chat", "Combat" });
        await Assert.That(rows.Where(r => r.On).Select(r => (r.Name, r.Rank))).IsEquivalentTo(new[] { ("Chat", 1), ("Combat", 2) });

        form.MoveAssignment(character, config.TriggerSets.Single(s => s.Name == "Combat"), -1);
        await Assert.That(character.TriggerSets).IsEquivalentTo(new[] { "Combat", "Chat" });

        form.Assign(character, config.TriggerSets.Single(s => s.Name == "Chat"), on: false);
        await Assert.That(character.TriggerSets).IsEquivalentTo(new[] { "Combat" });
    }

    [Test]
    public async Task TheListSaysWhoIsConnectedAndConnectingAsksTheClient()
    {
        var config = TwoWorlds();
        var connects = new List<string>();
        var (form, commits) = Form(config, connected: ["Aetherfall.Corvid"], connects: connects);

        var list = form.WorldList();
        await Assert.That(list[0].Connected).IsEqualTo(1);
        await Assert.That(list[1].Connected).IsEqualTo(0);
        await Assert.That(list[0].Address).IsEqualTo("aether.example:4201");

        await Assert.That(form.ConnectTo(config.Worlds[1], config.Worlds[1].Characters[0])).IsTrue();
        await Assert.That(connects).IsEquivalentTo(new[] { "Brightwater.Wren" });
        await Assert.That(commits).IsEmpty();
    }

    [Test]
    public async Task F9IsFoldedIntoTheWorldsPage()
    {
        await Assert.That(SettingsForm.PageOf(BuiltInSettings.Character.Id)).IsEqualTo(BuiltInSettings.World.Id);
        await Assert.That(SettingsForm.PageOf(BuiltInSettings.World.Id)).IsEqualTo(BuiltInSettings.World.Id);
        await Assert.That(SettingsForm.PageOf("text")).IsEqualTo("text");
    }

    [Test]
    public async Task ServerInformationHasThreeStates()
    {
        var world = TwoWorlds().Worlds[0];
        var now = DateTimeOffset.FromUnixTimeSeconds(2_000_000_000);

        var never = ServerInfoModel.For(world, null, now);
        await Assert.That(never.State).IsEqualTo(ServerInfoState.NeverConnected);
        await Assert.That(never.Configured.Select(r => r.Label)).IsEquivalentTo(new[] { "world", "address", "transport" });

        var silent = ServerInfoModel.For(world, new MsspObservation("aether.example:4201", now.AddHours(-2), null, null), now);
        await Assert.That(silent.State).IsEqualTo(ServerInfoState.PublishesNothing);
        await Assert.That(silent.LastSeen!).StartsWith("2 hours ago");
    }

    [Test]
    public async Task AValueOffTheWireCannotEndARowEarly()
    {
        await Assert.That(ServerInfoModel.Sanitize("a\nb\u001bc")).IsEqualTo("a b c");
        await Assert.That(ServerInfoModel.Reading("PLAYERS", "-1", DateTimeOffset.UnixEpoch)).IsEqualTo(ServerInfoModel.Unavailable);
    }
}
