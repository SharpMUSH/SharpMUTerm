using System.IO.Compression;
using System.Text;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Core.Tests.Packages;

public class PackageTests
{
    internal const string Manifest = """
        {"id":"aether.basics","name":"Aether basics","version":"1.0.0","games":["aetherfall.example:4000"]}
        """;

    internal const string Set = """
        {"name":"Basics","triggers":[{"name":"hp","pattern":"^HP low","actions":{"sendResponse":"quaff"}}],
         "aliases":[{"name":"k","pattern":"^k (.+)$","substitution":"kill $1"}],"scriptFiles":["scripts/basics.lua"]}
        """;

    internal static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    internal static (string, byte[]) Text(string name, string text) => (name, Encoding.UTF8.GetBytes(text));

    internal static byte[] Basics(string version = "1.0.0", string set = Set) => Zip(
        Text("package.json", Manifest.Replace("1.0.0", version)),
        Text("sets/basics.json", set),
        Text("scripts/basics.lua", "function hello() end"),
        ("media/bell.wav", [1, 2, 3]),
        Text("README.md", "hi"));

    private static string TempRoot() => Directory.CreateTempSubdirectory("sharpmuterm-pkg-").FullName;

    [Test]
    public async Task AnArchiveReadsIntoItsParts()
    {
        var (contents, problem) = PackageArchive.Read(Basics());

        await Assert.That(problem).IsNull();
        await Assert.That(contents!.Manifest.Id).IsEqualTo("aether.basics");
        await Assert.That(contents.Sets.Single().Name).IsEqualTo("Basics");
        await Assert.That(contents.Scripts.Keys).IsEquivalentTo(new[] { "basics.lua" });
        await Assert.That(contents.Media.Keys).IsEquivalentTo(new[] { "bell.wav" });
        await Assert.That(contents.Ignored).IsEquivalentTo(new[] { "README.md" });

        var review = contents.Review();
        await Assert.That(review.Triggers).IsEqualTo(1);
        await Assert.That(review.Aliases).IsEqualTo(1);
        await Assert.That(review.Sending).IsEqualTo(2);
    }

    [Test]
    [Arguments("../evil.lua")]
    [Arguments("scripts/../../evil.lua")]
    [Arguments("/etc/evil.lua")]
    [Arguments("scripts\\evil.lua")]
    [Arguments("C:/evil.lua")]
    [Arguments("scripts/./evil.lua")]
    [Arguments("sets/deep/evil.json")]
    public async Task AnEntryOutsideTheFoldersIsNeverRead(string name)
    {
        var (contents, _) = PackageArchive.Read(Zip(Text("package.json", Manifest), Text(name, "x")));

        await Assert.That(contents!.Scripts).IsEmpty();
        await Assert.That(contents.Sets).IsEmpty();
        await Assert.That(contents.Ignored).Contains(name);
    }

    [Test]
    public async Task AFileLargerThanItsCapIsRefusedWhileDecompressing()
    {
        var bomb = new byte[(int)PackageArchive.MaxTextBytes + 1];
        var archive = Zip(Text("package.json", Manifest), ("scripts/bomb.lua", bomb));

        await Assert.That(archive.Length).IsLessThan(100_000); // zeros compress: the header's sizes are not trusted
        var (contents, problem) = PackageArchive.Read(archive);

        await Assert.That(contents).IsNull();
        await Assert.That(problem).Contains("larger than");
    }

    [Test]
    [Arguments("""{"id":"../x","name":"X","version":"1.0.0","games":["*"]}""", "id")]
    [Arguments("""{"id":"x","name":"X","version":"one","games":["*"]}""", "version")]
    [Arguments("""{"id":"x","name":"X","version":"1.0.0","games":[]}""", "games")]
    [Arguments("""{"id":"x","name":"X","version":"1.0.0","games":["a b"]}""", "game address")]
    [Arguments("""not json""", "parse")]
    public async Task ABadManifestStopsEverything(string manifest, string says)
    {
        var (contents, problem) = PackageArchive.Read(Zip(Text("package.json", manifest), Text("sets/a.json", Set)));

        await Assert.That(contents).IsNull();
        await Assert.That(problem).Contains(says);
    }

    [Test]
    public async Task NoManifestIsNoPackage()
    {
        var (_, problem) = PackageArchive.Read(Zip(Text("sets/a.json", Set)));

        await Assert.That(problem).Contains("package.json");
    }

    [Test]
    public async Task AnInstallSurvivesARestartAndItsScriptsResolveInsideIt()
    {
        var root = TempRoot();
        try
        {
            var store = new PackageStore(root);
            await Assert.That(store.Install(PackageArchive.Read(Basics()).Contents!)).IsNull();

            var again = new PackageStore(root);
            again.Load();
            var installed = again.Find("aether.basics")!;

            await Assert.That(installed.Sets.Single().ScriptFiles).IsEquivalentTo(new[] { "package:aether.basics/basics.lua" });
            await Assert.That(again.ResolveScript("package:aether.basics/basics.lua"))
                .IsEqualTo(Path.Combine(root, "aether.basics", "scripts", "basics.lua"));
            await Assert.That(again.ResolveScript("package:aether.basics/../../config.json")).IsNull();
            await Assert.That(again.ResolveScript("package:other/basics.lua")).IsNull();
            await Assert.That(again.MediaFolders.Single()).IsEqualTo(Path.Combine(root, "aether.basics", "media"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AnUpdateReplacesThePackagesSetsWholesale()
    {
        var root = TempRoot();
        try
        {
            var store = new PackageStore(root);
            store.Install(PackageArchive.Read(Basics()).Contents!);
            store.Install(PackageArchive.Read(Basics("1.1.0", """{"name":"Renamed"}""")).Contents!);
            store.Load();

            await Assert.That(store.Find("aether.basics")!.Manifest.Version).IsEqualTo("1.1.0");
            await Assert.That(store.Find("aether.basics")!.Sets.Select(s => s.Name)).IsEquivalentTo(new[] { "Renamed" });
            await Assert.That(Directory.GetDirectories(root).Select(d => Path.GetFileName(d)!)).IsEquivalentTo(new[] { "aether.basics" });
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task RemovingDeletesTheFolder()
    {
        var root = TempRoot();
        try
        {
            var store = new PackageStore(root);
            store.Install(PackageArchive.Read(Basics()).Contents!);

            await Assert.That(store.Remove("aether.basics")).IsNull();
            await Assert.That(Directory.Exists(Path.Combine(root, "aether.basics"))).IsFalse();
            await Assert.That(store.Installed).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AStoreWithNoRootWritesNothing()
    {
        var store = new PackageStore();
        store.Install(PackageArchive.Read(Basics()).Contents!);

        await Assert.That(store.Installed.Count).IsEqualTo(1);
        await Assert.That(store.ResolveScript("package:aether.basics/basics.lua")).IsNull();
        await Assert.That(store.MediaFolders).IsEmpty();
    }

    [Test]
    public async Task APackagesSetsApplyOnlyToItsGamesAndOnlyWhileOn()
    {
        var store = new PackageStore();
        store.Install(PackageArchive.Read(Basics()).Contents!);
        var config = new AppConfiguration { Packages = store.Installed };
        var ann = new CharacterDefinition { Name = "Ann" };
        var aether = new WorldDefinition { Host = "Aetherfall.example", Port = 4000 };
        var other = new WorldDefinition { Host = "aetherfall.example", Port = 4001 };

        await Assert.That(config.ResolveTriggerSets(ann, aether).Select(s => s.Name)).IsEquivalentTo(new[] { "Basics" });
        await Assert.That(config.ResolveTriggerSets(ann, other)).IsEmpty();
        await Assert.That(config.ResolveTriggerSets(ann)).IsEmpty();

        config.DisabledPackages.Add("aether.basics");
        await Assert.That(config.ResolveTriggerSets(ann, aether)).IsEmpty();
    }

    [Test]
    [Arguments("*", "any.host", 1, true)]
    [Arguments("*.example.org", "mud.example.org", 23, true)]
    [Arguments("*.example.org", "example.org.evil", 23, false)]
    [Arguments("mud.example.org", "MUD.example.org", 4000, true)]
    [Arguments("mud.example.org:23", "mud.example.org", 4000, false)]
    public async Task GamePatterns(string game, string host, int port, bool matches)
    {
        await Assert.That(PackageManifest.Matches(game, host, port)).IsEqualTo(matches);
    }

    [Test]
    public async Task PackageSetsAreNeverSaved()
    {
        var store = new PackageStore();
        store.Install(PackageArchive.Read(Basics()).Contents!);
        var json = ConfigurationStore.Serialize(new AppConfiguration { Packages = store.Installed });

        await Assert.That(json).DoesNotContain("Basics");
    }
}
