using System.IO.Compression;
using System.Text;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Packages;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// F10 declared: the packages as a list section with its fields and actions, the one binding it writes
/// (<c>DisabledPackages</c>, replaced), the review model both hosts draw, the browse model, and the manager the window
/// reads — which fetches nothing on its own and hands every gesture to the client.
/// </summary>
public class PackageSettingsTests
{
    private static PackageContents Contents(string id, string name, string version, string games, string set, bool script)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entries = new List<(string, string)>
            {
                ("package.json", $$"""{"id":"{{id}}","name":"{{name}}","version":"{{version}}","games":[{{games}}]}"""),
                ("sets/main.json", set),
            };
            if (script)
            {
                entries.Add(("scripts/main.lua", "x = 1"));
            }

            foreach (var (entry, text) in entries)
            {
                using var stream = zip.CreateEntry(entry).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return PackageArchive.Read(buffer.ToArray()).Contents!;
    }

    private const string SendingSet = """
        {"name":"Basics","triggers":[{"name":"hp","pattern":"^HP low","actions":{"sendResponse":"quaff"}}]}
        """;

    private static (AppConfiguration Config, PackageStore Store) Installed()
    {
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Aetherfall",
            Host = "aetherfall.example",
            Port = 4000,
            Characters = { new CharacterDefinition { Name = "Corvid" } },
        });
        var store = new PackageStore();
        store.Install(Contents("aether.basics", "Aether basics", "1.0.0", "\"aetherfall.example:4000\"", SendingSet, script: true));
        config.Packages = store.Installed;
        return (config, store);
    }

    private sealed class Calls : IPackageHost
    {
        public List<string> Log { get; } = [];

        public void SetEnabled(string id, bool on) => Log.Add($"enable {id} {on}");

        public void Remove(string id) => Log.Add($"remove {id}");

        public void CopySets(string id) => Log.Add($"copy {id}");

        public void Browse() => Log.Add("browse");

        public void Install(PackageIndexEntry entry) => Log.Add($"install {entry.Id}");

        public void Accept(PackageReviewModel review) => Log.Add($"accept {review.Name}");

        public void Cancel(PackageReviewModel review) => Log.Add($"cancel {review.Name}");
    }

    [Test]
    public async Task F10IsDeclaredAsAListOfPackagesWithItsFieldsAndActions()
    {
        var section = BuiltInSettings.Packages;
        var packages = section.Packages!;

        await Assert.That(BuiltInSettings.All).Contains(section);
        await Assert.That(SettingsForm.IsDeclared(section)).IsTrue();
        await Assert.That(section.Key).IsEqualTo("F10");
        await Assert.That(section.Scope).IsEqualTo(SettingScope.Item);
        await Assert.That(packages.Fields.Select(f => f.Id)).IsEquivalentTo(
            new[] { "enabled", "name", "version", "games", "author", "description", "sets", "scripts", "media", "update", "characters" });
        await Assert.That(packages.Actions.Select(a => a.Id)).IsEquivalentTo(
            new[] { PackageCollection.Toggle, PackageCollection.CopySets, PackageCollection.Remove, PackageCollection.Browse });
        await Assert.That(packages.Fields.All(f => f.Group is not null)).IsTrue();
    }

    [Test]
    public async Task TheSearchFindsAPackagesFields()
    {
        var matches = SettingsSearch.Find(BuiltInSettings.All, "lua");

        await Assert.That(matches.Any(m => m.Section.Id == "packages" && m.Field?.Id == "scripts")).IsTrue();
    }

    [Test]
    public async Task TheFieldsSayWhatThePackageIsAndAdds()
    {
        var (config, _) = Installed();
        var item = PackageCollection.Items(config, null).Single();
        var packages = BuiltInSettings.Packages.Packages!;
        string Read(string id) => ((InfoField)packages.Field(id)).Read(new SettingTarget(config, Item: item));

        await Assert.That(Read("version")).IsEqualTo("1.0.0");
        await Assert.That(Read("games")).IsEqualTo("aetherfall.example:4000");
        await Assert.That(Read("sets")).IsEqualTo("Basics · 1 rule");
        await Assert.That(Read("scripts")).IsEqualTo("main.lua");
        await Assert.That(Read("media")).IsEqualTo("none");
        await Assert.That(Read("characters")).IsEqualTo("Corvid (Aetherfall)");
        await Assert.That(Read("update")).Contains("not checked");
    }

    [Test]
    public async Task AnIndexWithANewerVersionIsAnUpdate()
    {
        var (config, _) = Installed();
        var (index, _) = PackageIndex.Parse($$"""
            {"packages":[{"id":"aether.basics","name":"Aether basics","version":"1.1.0","games":["*"],
              "url":"https://p.example/a.zip","sha256":"{{new string('a', 64)}}"}]}
            """);

        var item = PackageCollection.Items(config, index).Single();

        await Assert.That(item.Update).IsEqualTo("1.1.0");
        await Assert.That(((InfoField)BuiltInSettings.Packages.Packages!.Field("update")).Read(new SettingTarget(config, Item: item)))
            .IsEqualTo("1.1.0 in the index");
    }

    [Test]
    public async Task SwitchingAPackageOffReplacesTheListAndItsSetsStopApplying()
    {
        var (config, _) = Installed();
        var character = config.Worlds[0].Characters[0];
        var item = PackageCollection.Items(config, null).Single();
        var enabled = (SettingBinding<bool>)BuiltInSettings.Packages.Packages!.Field("enabled").Binding!;
        var before = config.DisabledPackages;
        await Assert.That(config.ResolveTriggerSets(character, config.Worlds[0]).Select(s => s.Name)).Contains("Basics");

        enabled.Set(new SettingTarget(config, Item: item), false);

        await Assert.That(ReferenceEquals(before, config.DisabledPackages)).IsFalse().Because("lists are replaced, never edited");
        await Assert.That(config.DisabledPackages).Contains("aether.basics");
        await Assert.That(config.ResolveTriggerSets(character, config.Worlds[0]).Select(s => s.Name)).DoesNotContain("Basics");
        await Assert.That(PackageCollection.SetEnabled(config, "aether.basics", false)).IsFalse().Because("already off");
    }

    [Test]
    public async Task TheReviewLeadsWithWhatCanActAsThePlayer()
    {
        var contents = Contents("aether.basics", "Aether basics", "1.0.0", "\"*\"", SendingSet, script: true);

        var review = PackageReviewModel.From(contents, ["Corvid (Aetherfall)"], existing: null);

        await Assert.That(review.Title).IsEqualTo("Install package");
        await Assert.That(review.Acts).IsTrue();
        await Assert.That(review.Sections.Select(s => s.Heading)).IsEquivalentTo(
            new[] { PackageReviewModel.ActsHeading, PackageReviewModel.AddsHeading, PackageReviewModel.WhoHeading });
        var acts = review.Sections[0].Rows;
        await Assert.That(acts[0].Value).IsEqualTo("1 rule sends commands as you");
        await Assert.That(acts[0].Tone).IsEqualTo(PackageReviewTone.Warning);
        await Assert.That(acts[1].Value).Contains("main.lua");
        await Assert.That(acts[2].Tone).IsEqualTo(PackageReviewTone.Warning);
    }

    [Test]
    public async Task AQuietPackageSaysNothingActsAsThePlayerAndAnUpdateNamesWhatItReplaces()
    {
        var (config, store) = Installed();
        var contents = Contents("aether.basics", "Aether basics", "2.0.0", "\"*\"", """{"name":"Quiet","triggers":[{"name":"x","pattern":"^x"}]}""", script: false);

        var review = PackageReviewModel.From(contents, [], store.Find("aether.basics"));

        await Assert.That(review.Acts).IsFalse();
        await Assert.That(review.Title).IsEqualTo("Update package");
        await Assert.That(review.Replaces).IsEqualTo("1.0.0");
        await Assert.That(review.Sections[0].Rows[0].Tone).IsEqualTo(PackageReviewTone.Quiet);
        await Assert.That(review.Sections[2].Rows[1].Value).IsEqualTo("none of yours, yet");
        _ = config;
    }

    [Test]
    public async Task TheBrowseListPutsTheWorldsOwnFirstAndSaysWhatIsInstalled()
    {
        var (config, store) = Installed();
        var hash = new string('a', 64);
        var (index, _) = PackageIndex.Parse($$"""
            {"packages":[
              {"id":"any.thing","name":"Any thing","version":"1.0.0","games":["*"],"url":"https://p.example/1.zip","sha256":"{{hash}}"},
              {"id":"aether.basics","name":"Aether basics","version":"1.1.0","games":["aetherfall.example"],"url":"https://p.example/2.zip","sha256":"{{hash}}"},
              {"id":"bad","name":"Bad","version":"1","games":["*"],"url":"http://x","sha256":"x"}]}
            """);

        var model = PackageBrowseModel.From(index!, store.Installed, config.Worlds[0]);

        await Assert.That(model.World).IsEqualTo("Aetherfall");
        await Assert.That(model.Dropped).IsEqualTo(1);
        await Assert.That(model.Rows.Select(r => r.Entry.Id)).IsEquivalentTo(new[] { "aether.basics", "any.thing" });
        await Assert.That(model.Rows[0].ForWorld).IsTrue();
        await Assert.That(model.Rows[0].State).IsEqualTo(PackageBrowseState.Update);
        await Assert.That(model.Rows[0].StateText).IsEqualTo("installed 1.0.0, update");
        await Assert.That(model.Rows[1].ForWorld).IsFalse().Because("a * package is not the world's own");
    }

    [Test]
    public async Task TheManagerFetchesNothingByItselfAndHandsEveryGestureToTheClient()
    {
        var (config, _) = Installed();
        var calls = new Calls();
        var manager = new PackageManager(config, BuiltInSettings.Packages.Packages!, calls);
        var item = manager.Items.Single();

        await Assert.That(calls.Log).IsEmpty();
        await Assert.That(manager.Index).IsNull();
        manager.Accept(); // nothing to accept
        await Assert.That(calls.Log).IsEmpty();

        manager.SetEnabled(item, false);
        manager.CopySets(item);
        manager.Remove(item);
        manager.Browse();
        var review = PackageReviewModel.From(Contents("x", "X", "1.0.0", "\"*\"", SendingSet, false), [], null);
        manager.PublishReview(review);
        manager.Accept();
        manager.Cancel();

        await Assert.That(calls.Log).IsEquivalentTo(new[]
        {
            "enable aether.basics False", "copy aether.basics", "remove aether.basics", "browse", "accept X", "cancel X",
        });
    }
}
