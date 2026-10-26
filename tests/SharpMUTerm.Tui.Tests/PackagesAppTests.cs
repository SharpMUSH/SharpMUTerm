using System.IO.Compression;
using System.Text;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Packages;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>Packages in the app: the review before an install, the Packages screen, and what each key writes.</summary>
[NotInParallel]
public class PackagesAppTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private const string Manifest = """
        {"id":"aether.basics","name":"Aether basics","version":"1.0.0","games":["aetherfall.example:4000"]}
        """;

    private const string Set = """
        {"name":"Basics","triggers":[{"name":"hp","pattern":"^HP low","actions":{"sendResponse":"quaff"}}]}
        """;

    private static byte[] Archive()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in new[] { ("package.json", Manifest), ("sets/basics.json", Set), ("scripts/basics.lua", "x = 1") })
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return buffer.ToArray();
    }

    private sealed class Rig : IAsyncDisposable
    {
        public required SharpMUTermApp App { get; init; }

        public required AppConfiguration Config { get; init; }

        public required string Root { get; init; }

        public int Saves { get; set; }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }

    private static Rig Open()
    {
        Console.SetIn(TextReader.Null);
        var root = Directory.CreateTempSubdirectory("sharpmuterm-pkgapp-").FullName;
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Aetherfall",
            Host = "aetherfall.example",
            Port = 4000,
            Characters = { new CharacterDefinition { Name = "Corvid", Logging = new LoggingSettings() } },
        });

        Rig? rig = null;
        var app = new SharpMUTermApp(
            config,
            ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null),
            save: _ => rig!.Saves++,
            packages: new PackageStore(Path.Combine(root, "packages")));
        rig = new Rig { App = app, Config = config, Root = root };
        return rig;
    }

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char ch = '\0') => new(ch, key, false, false, false);

    [Test]
    public async Task TheReviewSaysWhatTheArchiveAddsAndInstallsNothingUntilEnter()
    {
        await using var rig = Open();

        rig.App.ReviewPackage(Archive());

        await Assert.That(rig.App.PackageView.Mode).IsEqualTo("review");
        var text = string.Join("\n", rig.App.PackageView.Lines.Select(MarkupText.Plain));
        await Assert.That(text).Contains("1 of them sends commands as you");
        await Assert.That(text).Contains("basics.lua");
        await Assert.That(text).Contains("Corvid (Aetherfall)");
        await Assert.That(rig.App.Packages.Installed).IsEmpty();
        await Assert.That(Directory.Exists(Path.Combine(rig.Root, "packages"))).IsFalse();

        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(rig.App.PackageView.IsOpen).IsFalse();
        await Assert.That(rig.App.Packages.Installed.Single().Id).IsEqualTo("aether.basics");
        await Assert.That(File.Exists(Path.Combine(rig.Root, "packages", "aether.basics", "package.json"))).IsTrue();
        await Assert.That(rig.Config.ResolveTriggerSets(rig.Config.Worlds[0].Characters[0], rig.Config.Worlds[0]).Select(s => s.Name))
            .Contains("Basics");
    }

    [Test]
    public async Task EscapeOnTheReviewInstallsNothing()
    {
        await using var rig = Open();

        rig.App.ReviewPackage(Archive());
        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Escape, '\u001b'));

        await Assert.That(rig.App.PackageView.IsOpen).IsFalse();
        await Assert.That(rig.App.Packages.Installed).IsEmpty();
        await Assert.That(Directory.Exists(Path.Combine(rig.Root, "packages"))).IsFalse();
    }

    [Test]
    public async Task ALinkIsFetchedThenReviewed()
    {
        await using var rig = Open();
        Uri? asked = null;
        rig.App.PackageFetch = (uri, _) => { asked = uri; return Task.FromResult<byte[]?>(Archive()); };

        Type(rig.App, "/package install https://packages.example/basics.zip");
        for (var i = 0; i < 200 && !rig.App.PackageView.IsOpen; i++)
        {
            await Task.Delay(10);
        }

        await Assert.That(asked!.Host).IsEqualTo("packages.example");
        await Assert.That(rig.App.PackageView.Mode).IsEqualTo("review");
    }

    [Test]
    public async Task APlainHttpLinkIsRefused()
    {
        await using var rig = Open();
        rig.App.PackageFetch = (_, _) => throw new InvalidOperationException("must not fetch");

        Type(rig.App, "/package install http://packages.example/basics.zip");

        await Assert.That(rig.App.PackageView.IsOpen).IsFalse();
        await Assert.That(MarkupText.Plain(rig.App.StatusMarkup)).Contains("https only");
    }

    [Test]
    public async Task SpaceSwitchesAPackageOffAndItsSetsStopApplying()
    {
        await using var rig = Open();
        rig.App.ReviewPackage(Archive());
        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Enter, '\r'));
        var character = rig.Config.Worlds[0].Characters[0];

        rig.App.SimulateKey(Key(ConsoleKey.F10));
        await Assert.That(rig.App.PackageView.Mode).IsEqualTo("list");
        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Spacebar, ' '));

        await Assert.That(rig.Config.DisabledPackages).Contains("aether.basics");
        await Assert.That(rig.Config.ResolveTriggerSets(character, rig.Config.Worlds[0]).Any(s => s.Name == "Basics")).IsFalse();
        await Assert.That(MarkupText.Plain(rig.App.PackageView.Lines[2])).Contains("[ ]");
    }

    [Test]
    public async Task CopyMakesTheSetsThePlayersOwn()
    {
        await using var rig = Open();
        rig.App.ReviewPackage(Archive());
        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Enter, '\r'));

        rig.App.ShowPackages();
        rig.App.PackageView.SimulateKey(Key(ConsoleKey.C, 'c'));

        var copy = rig.Config.TriggerSets.Single(s => s.Name == "Basics");
        await Assert.That(copy.Triggers.Single().Name).IsEqualTo("hp");
        await Assert.That(rig.App.PackageView.IsOpen).IsTrue();
    }

    [Test]
    public async Task RemoveAsksOnceThenRemoves()
    {
        await using var rig = Open();
        rig.App.ReviewPackage(Archive());
        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Enter, '\r'));
        rig.App.ShowPackages();

        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Delete));
        await Assert.That(rig.App.Packages.Installed.Count).IsEqualTo(1);
        await Assert.That(string.Join("\n", rig.App.PackageView.Lines.Select(MarkupText.Plain))).Contains("Del again removes it");

        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Delete));

        await Assert.That(rig.App.Packages.Installed).IsEmpty();
        await Assert.That(Directory.Exists(Path.Combine(rig.Root, "packages", "aether.basics"))).IsFalse();
    }

    [Test]
    public async Task AnyOtherKeyKeepsAPackageAfterTheRemovePrompt()
    {
        await using var rig = Open();
        rig.App.ReviewPackage(Archive());
        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Enter, '\r'));
        rig.App.ShowPackages();

        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Delete));
        rig.App.PackageView.SimulateKey(Key(ConsoleKey.DownArrow));
        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Delete));

        await Assert.That(rig.App.Packages.Installed.Count).IsEqualTo(1);
    }

    [Test]
    public async Task NotAPackageIsSaidSo()
    {
        await using var rig = Open();

        rig.App.ReviewPackage([1, 2, 3]);

        await Assert.That(rig.App.PackageView.IsOpen).IsFalse();
        await Assert.That(MarkupText.Plain(rig.App.StatusMarkup)).Contains("not installed");
    }

    [Test]
    public async Task ThePackagesAreOnTheCommandSurface()
    {
        await using var rig = Open();

        var ids = rig.App.BuildCatalog().Select(i => i.Id).ToList();

        await Assert.That(ids).Contains("term:packages");
        await Assert.That(ids).Contains("term:package-install");
    }

    [Test]
    public async Task AnAppWithNoStoreWritesNothing()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        await using var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));

        app.ReviewPackage(Archive());
        app.PackageView.SimulateKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(app.Packages.Installed.Count).IsEqualTo(1);
        await Assert.That(app.Packages.HasRoot).IsFalse();
    }
}
