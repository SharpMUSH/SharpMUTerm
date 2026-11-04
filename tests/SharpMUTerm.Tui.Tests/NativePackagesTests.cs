using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Packages;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Packages;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// F10 from its declaration in both hosts: the cell screen draws the declared fields, a host with a settings window
/// opens its package page instead, switching and removing save once through the same methods either way, the review
/// is one model with nothing written before it is accepted, the index is fetched only when asked and an archive that is
/// not the one listed never reaches the review; and a settings <c>--view</c> goes to the window when one is attached.
/// </summary>
[NotInParallel]
public class NativePackagesTests
{
    private static readonly TerminalCapabilities Caps =
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
            foreach (var (name, text) in new[] { ("package.json", Manifest), ("sets/basics.json", Set), ("scripts/basics.lua", "x = 1"), ("media/bell.ogg", "OggS") })
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return buffer.ToArray();
    }

    private sealed class Presenter : INativeSettings
    {
        public List<SettingsForm> Shown { get; } = [];

        public void Show(SettingsForm form) => Shown.Add(form);

        public void Close()
        {
        }
    }

    private sealed class Rig : IAsyncDisposable
    {
        public required SharpMUTermApp App { get; init; }

        public required AppConfiguration Config { get; init; }

        public required string Root { get; init; }

        public required Presenter Native { get; init; }

        public int Saves { get; set; }

        public int Fetches;

        public CharacterDefinition Character => Config.Worlds[0].Characters[0];

        public bool Applies => Config.ResolveTriggerSets(Character, Config.Worlds[0]).Any(s => s.Name == "Basics");

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }

    private static Rig Open(bool native, string? sha = null)
    {
        Console.SetIn(TextReader.Null);
        var root = Directory.CreateTempSubdirectory("sharpmuterm-nativepkg-").FullName;
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Aetherfall",
            Host = "aetherfall.example",
            Port = 4000,
            Characters = { new CharacterDefinition { Name = "Corvid", Logging = new LoggingSettings() } },
        });

        var presenter = new Presenter();
        Rig? rig = null;
        var host = ClientHost.ForHarness(Caps, new HeadlessConsoleDriver(120, 32), null, null, null) with
        {
            NativeSettings = native ? presenter : null,
        };
        var app = new SharpMUTermApp(config, host, save: _ => rig!.Saves++, packages: new PackageStore(Path.Combine(root, "packages")));
        rig = new Rig { App = app, Config = config, Root = root, Native = presenter };
        var archive = Archive();
        app.PackageFetch = (uri, _) =>
        {
            Interlocked.Increment(ref rig!.Fetches);
            return Task.FromResult<byte[]?>(uri.AbsolutePath.EndsWith("index.json", StringComparison.Ordinal)
                ? Encoding.UTF8.GetBytes(Index(archive, sha))
                : archive);
        };
        return rig;
    }

    private static string Index(byte[] archive, string? sha) => $$"""
        {"packages":[
          {"id":"aether.basics","name":"Aether basics","version":"1.0.0","games":["aetherfall.example:4000"],
           "url":"https://packages.example/basics.zip",
           "sha256":"{{sha ?? Convert.ToHexStringLower(SHA256.HashData(archive))}}"}]}
        """;

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char ch = '\0') => new(ch, key, false, false, false);

    private static IReadOnlyList<string> Flatten(PackageReviewModel model) =>
        new[] { model.Title, model.Name, model.Version, model.Author ?? string.Empty, model.Replaces ?? string.Empty, model.Description ?? string.Empty }
            .Concat(model.Sections.SelectMany(s => new[] { s.Heading }.Concat(s.Rows.Select(r => $"{r.Label}|{r.Value}|{r.Tone}"))))
            .ToList();

    private static void Install(Rig rig)
    {
        rig.App.ReviewPackage(Archive());
        if (rig.App.PackageView.IsOpen)
        {
            rig.App.PackageView.SimulateKey(Key(ConsoleKey.Enter, '\r'));
        }
        else
        {
            rig.App.AcceptPackage(rig.App.PendingPackageReview!);
        }
    }

    [Test]
    public async Task TheCellScreenDrawsEveryDeclaredField()
    {
        await using var rig = Open(native: false);
        Install(rig);

        var drawn = rig.App.CellPackageFields();

        await Assert.That(drawn).IsEquivalentTo(BuiltInSettings.Packages.Packages!.Fields.Select(f => f.Id), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task F10OpensTheWindowsPackagePageAndNoCellScreen()
    {
        await using var rig = Open(native: true);

        rig.App.SimulateKey(Key(ConsoleKey.F10));

        var form = rig.Native.Shown.Single();
        await Assert.That(form.Initial).IsEqualTo("packages");
        await Assert.That(form.Packages).IsNotNull();
        await Assert.That(rig.App.PackageView.IsOpen).IsFalse();
    }

    [Test]
    public async Task SwitchingOffInTheWindowSavesOnceAndTheSetsStopApplying()
    {
        await using var rig = Open(native: true);
        Install(rig);
        var saves = rig.Saves;
        var manager = rig.App.PackageManager;
        await Assert.That(rig.Applies).IsTrue();

        manager.SetEnabled(manager.Items.Single(), false);

        await Assert.That(rig.Saves - saves).IsEqualTo(1);
        await Assert.That(rig.Applies).IsFalse();

        manager.SetEnabled(manager.Items.Single(), true);
        await Assert.That(rig.Saves - saves).IsEqualTo(2);
        await Assert.That(rig.Applies).IsTrue();
    }

    [Test]
    public async Task SwitchingOffOnTheCellScreenSavesOnceAndTheSetsStopApplying()
    {
        await using var rig = Open(native: false);
        Install(rig);
        rig.App.ShowPackages();
        var saves = rig.Saves;

        rig.App.PackageView.SimulateKey(Key(ConsoleKey.Spacebar, ' '));

        await Assert.That(rig.Saves - saves).IsEqualTo(1);
        await Assert.That(rig.Applies).IsFalse();
    }

    [Test]
    public async Task RemovingInTheWindowDeletesThePackageAndItsFolder()
    {
        await using var rig = Open(native: true);
        Install(rig);
        await Assert.That(Directory.Exists(Path.Combine(rig.Root, "packages", "aether.basics"))).IsTrue();
        var manager = rig.App.PackageManager;
        var changes = 0;
        manager.Changed += () => changes++;

        manager.Remove(manager.Items.Single());

        await Assert.That(rig.App.Packages.Installed).IsEmpty();
        await Assert.That(manager.Items).IsEmpty();
        await Assert.That(Directory.Exists(Path.Combine(rig.Root, "packages", "aether.basics"))).IsFalse();
        await Assert.That(rig.Applies).IsFalse();
        await Assert.That(manager.Status).Contains("Removed Aether basics");
        await Assert.That(changes).IsGreaterThan(0);
    }

    [Test]
    public async Task CopyInTheWindowMakesTheSetsThePlayersOwn()
    {
        await using var rig = Open(native: true);
        Install(rig);

        rig.App.PackageManager.CopySets(rig.App.PackageManager.Items.Single());

        await Assert.That(rig.Config.TriggerSets.Single(s => s.Name == "Basics").Triggers.Single().Name).IsEqualTo("hp");
    }

    [Test]
    public async Task TheReviewIsOneModelInBothHostsAndNothingIsWrittenBeforeItIsAccepted()
    {
        await using var cells = Open(native: false);
        await using var window = Open(native: true);

        cells.App.ReviewPackage(Archive());
        window.App.ReviewPackage(Archive());

        var cellModel = cells.App.PendingPackageReview!;
        var nativeModel = window.App.PackageManager.Review!;
        await Assert.That(Flatten(nativeModel)).IsEquivalentTo(Flatten(cellModel), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(nativeModel.Sections[0].Heading).IsEqualTo(PackageReviewModel.ActsHeading);

        // The cell review draws the model's rows, in its order.
        var text = string.Join("\n", cells.App.PackageView.Lines.Select(MarkupText.Plain));
        var at = -1;
        foreach (var row in cellModel.Sections.SelectMany(s => s.Rows))
        {
            var next = text.IndexOf(row.Value, at + 1, StringComparison.Ordinal);
            await Assert.That(next).IsGreaterThan(at);
            at = next;
        }

        // The window opened on its package page, and nothing is on disk yet.
        await Assert.That(window.Native.Shown.Single().Initial).IsEqualTo("packages");
        await Assert.That(window.App.Packages.Installed).IsEmpty();
        await Assert.That(Directory.Exists(Path.Combine(window.Root, "packages"))).IsFalse();
        await Assert.That(window.Saves).IsEqualTo(0);

        window.App.PackageManager.Accept();

        await Assert.That(window.App.PackageManager.Review).IsNull();
        await Assert.That(window.App.Packages.Installed.Single().Id).IsEqualTo("aether.basics");
        await Assert.That(File.Exists(Path.Combine(window.Root, "packages", "aether.basics", "package.json"))).IsTrue();
        await Assert.That(window.Applies).IsTrue();
    }

    [Test]
    public async Task CancellingTheReviewInTheWindowWritesNothing()
    {
        await using var rig = Open(native: true);
        rig.App.ReviewPackage(Archive());

        rig.App.PackageManager.Cancel();

        await Assert.That(rig.App.PackageManager.Review).IsNull();
        await Assert.That(rig.App.Packages.Installed).IsEmpty();
        await Assert.That(Directory.Exists(Path.Combine(rig.Root, "packages"))).IsFalse();
    }

    [Test]
    public async Task AStaleReviewCannotBeAccepted()
    {
        await using var rig = Open(native: true);
        rig.App.ReviewPackage(Archive());
        var first = rig.App.PendingPackageReview!;
        rig.App.CancelPackage(first);

        await Assert.That(rig.App.AcceptPackage(first)).IsFalse();
        await Assert.That(rig.App.Packages.Installed).IsEmpty();
    }

    [Test]
    public async Task TheIndexIsNeverFetchedAtStartOrWhenThePageOpens()
    {
        await using var rig = Open(native: true);
        Install(rig);

        rig.App.SimulateKey(Key(ConsoleKey.F10));
        await Assert.That(rig.Native.Shown.Last().Initial).IsEqualTo("packages");
        _ = rig.Native.Shown.Last().Packages!.Items;
        await Task.Delay(50);

        await Assert.That(rig.Fetches).IsEqualTo(0);
        await Assert.That(rig.App.PackageManager.Index).IsNull();
        await Assert.That(rig.App.PackageManager.BrowseModel).IsNull();

        rig.App.PackageManager.Browse();
        await Until(() => rig.App.PackageManager.BrowseModel is not null);

        await Assert.That(rig.Fetches).IsEqualTo(1);
        await Assert.That(rig.App.PackageManager.BrowseModel!.Rows.Single().State).IsEqualTo(PackageBrowseState.Installed);
        await Assert.That(rig.App.PackageManager.BrowseRequests).IsEqualTo(1);
    }

    [Test]
    public async Task InstallingFromTheWindowsBrowseListReviewsAVerifiedPackage()
    {
        await using var rig = Open(native: true);
        var manager = rig.App.PackageManager;
        manager.Browse();
        await Until(() => manager.BrowseModel is not null);

        manager.Install(manager.BrowseModel!.Rows.Single());
        await Until(() => manager.Review is not null);

        await Assert.That(manager.Review!.Name).IsEqualTo("Aether basics");
        await Assert.That(rig.App.Packages.Installed).IsEmpty();
    }

    [Test]
    public async Task AnArchiveWhoseHashDiffersNeverReachesTheWindowsReview()
    {
        await using var rig = Open(native: true, sha: new string('c', 64));
        var manager = rig.App.PackageManager;
        manager.Browse();
        await Until(() => manager.BrowseModel is not null);

        manager.Install(manager.BrowseModel!.Rows.Single());
        await Until(() => manager.Status?.Contains("SHA-256", StringComparison.Ordinal) == true);

        await Assert.That(manager.Review).IsNull();
        await Assert.That(rig.App.PendingPackageReview).IsNull();
        await Assert.That(manager.Status).Contains("does not match the index's SHA-256");
        await Assert.That(rig.App.Packages.Installed).IsEmpty();
    }

    [Test]
    public async Task ASettingsViewGoesToTheWindowWhenOneIsAttached()
    {
        Console.SetIn(TextReader.Null);
        var presenter = new Presenter();
        var host = ClientHost.Headless(new HeadlessConsoleDriver(120, 32), Caps) with { NativeSettings = presenter };
        await using var app = new SharpMUTermApp(DemoScene.Build(), host);

        app.RenderSnapshot("keypad");

        await Assert.That(presenter.Shown.Single().Initial).IsEqualTo("keypad");
        await Assert.That(app.SettingsOpenKey).IsNull();
    }

    [Test]
    public async Task ASettingsViewIsTheCellScreenWithNoWindow()
    {
        Console.SetIn(TextReader.Null);
        await using var app = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(new HeadlessConsoleDriver(120, 32), Caps));

        app.RenderSnapshot("keypad");

        await Assert.That(app.SettingsOpenKey).IsEqualTo(ConsoleKey.F4);
    }

    [Test]
    public async Task ThePackagesViewsGoToTheWindowWhenOneIsAttached()
    {
        foreach (var view in new[] { "packages", "package-review", "package-browse" })
        {
            Console.SetIn(TextReader.Null);
            var presenter = new Presenter();
            var host = ClientHost.Headless(new HeadlessConsoleDriver(120, 32), Caps) with { NativeSettings = presenter };
            await using var app = new SharpMUTermApp(DemoScene.Build(), host);

            app.RenderSnapshot(view);

            await Assert.That(presenter.Shown.Last().Initial).IsEqualTo("packages");
            await Assert.That(app.PackageView.IsOpen).IsFalse();
            if (view == "package-review")
            {
                await Assert.That(app.PackageManager.Review).IsNotNull();
            }

            if (view == "package-browse")
            {
                await Assert.That(app.PackageManager.BrowseModel!.Rows.Count).IsEqualTo(4);
            }
        }
    }
}
