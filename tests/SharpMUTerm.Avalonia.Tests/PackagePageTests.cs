using System.IO.Compression;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SharpMUTerm.Client.Packages;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Packages;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// F10 in the settings window (<see cref="PackagePageView"/>): the declared fields — the same the cell screen draws —
/// every gesture handed to the client and none performed here, the index fetched only from the Browse tab, and the
/// review drawn from the model the cell review draws, with Install the only way to consent.
/// </summary>
public class PackagePageTests
{
    private static PackageContents Contents()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in new[]
            {
                ("package.json", """{"id":"aether.basics","name":"Aether basics","version":"1.0.0","author":"builders","description":"Warnings.","games":["aetherfall.example:4000"]}"""),
                ("sets/basics.json", """{"name":"Basics","triggers":[{"name":"hp","pattern":"^HP low","actions":{"sendResponse":"quaff"}}]}"""),
                ("scripts/basics.lua", "x = 1"),
            })
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return PackageArchive.Read(buffer.ToArray()).Contents!;
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

    private sealed class Harness
    {
        public Harness()
        {
            Config.Worlds.Add(new WorldDefinition
            {
                Name = "Aetherfall",
                Host = "aetherfall.example",
                Port = 4000,
                Characters = { new CharacterDefinition { Name = "Corvid" } },
            });
            var store = new PackageStore();
            store.Install(Contents());
            Config.Packages = store.Installed;
            Manager = new PackageManager(Config, BuiltInSettings.Packages.Packages!, Calls);
        }

        public AppConfiguration Config { get; } = new();

        public Calls Calls { get; } = new();

        public PackageManager Manager { get; }

        public SettingsForm Form() => new(
            BuiltInSettings.All,
            Config,
            change => change(),
            _ => { },
            "packages",
            isDark: true)
        {
            Packages = Manager,
        };
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [Test]
    public async Task ThePageDrawsTheDeclaredFieldsTheCellScreenDraws()
    {
        var harness = new Harness();
        var (shown, values) = await Run(harness, window => (window.ShownFields.ToArray(), window.PackagePage!.Values));
        var cells = new List<string>();
        PackageScreen.List(
            BuiltInSettings.Packages.Packages!, harness.Config, PackageCollection.Items(harness.Config, null), 0, false, 100, cells);

        await Assert.That(shown).IsEquivalentTo(cells, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(shown).IsEquivalentTo(BuiltInSettings.Packages.Packages!.Fields.Select(f => f.Id), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(values["scripts"]).IsEqualTo("basics.lua");
        await Assert.That(values["characters"]).IsEqualTo("Corvid (Aetherfall)");
    }

    [Test]
    public async Task TheSwitchCopyAndRemoveAreHandedToTheClient()
    {
        var harness = new Harness();
        var (afterOneRemove, log) = await Run(harness, window =>
        {
            var page = window.PackagePage!;
            page.Rows.Single().Switch.IsChecked = false;
            Dispatcher.UIThread.RunJobs();
            Click(page.CopyButton!);
            Click(page.RemoveButton!);
            var asked = harness.Calls.Log.Count(l => l.StartsWith("remove", StringComparison.Ordinal));
            Click(page.RemoveButton!);
            return (asked, harness.Calls.Log.ToArray());
        });

        await Assert.That(afterOneRemove).IsEqualTo(0).Because("the first press asks, as Del does on the cell screen");
        await Assert.That(log).IsEquivalentTo(new[] { "enable aether.basics False", "copy aether.basics", "remove aether.basics" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(harness.Config.DisabledPackages).IsEmpty().Because("the page writes nothing itself");
    }

    [Test]
    public async Task TheIndexIsFetchedOnlyFromTheBrowseTab()
    {
        var harness = new Harness();
        var (before, fetch, after) = await Run(harness, window =>
        {
            var page = window.PackagePage!;
            var opened = harness.Calls.Log.ToArray();
            page.BrowseTab.IsSelected = true;
            Dispatcher.UIThread.RunJobs();
            var button = page.FetchButton is not null;
            Click(page.FetchButton!);
            return (opened, button, harness.Calls.Log.ToArray());
        });

        await Assert.That(before).IsEmpty();
        await Assert.That(fetch).IsTrue();
        await Assert.That(after).IsEquivalentTo(new[] { "browse" });
    }

    [Test]
    public async Task TheBrowseListComesForwardWhenTheIndexIsAskedForAndInstallIsHandedToTheClient()
    {
        var harness = new Harness();
        var (browsing, rows, log) = await Run(harness, window =>
        {
            var page = window.PackagePage!;
            var (index, _) = PackageIndex.Parse($$"""
                {"packages":[{"id":"aether.basics","name":"Aether basics","version":"1.1.0","games":["aetherfall.example"],
                  "url":"https://p.example/a.zip","sha256":"{{new string('a', 64)}}"}]}
                """);
            harness.Manager.PublishFetching();
            harness.Manager.PublishIndex(index!, PackageBrowseModel.From(index!, harness.Config.Packages, harness.Config.Worlds[0]));
            page.Refresh();
            Dispatcher.UIThread.RunJobs();
            Click(page.InstallButton!);
            return (page.Browsing, harness.Manager.Items.Single().Update, harness.Calls.Log.ToArray());
        });

        await Assert.That(browsing).IsTrue();
        await Assert.That(rows).IsEqualTo("1.1.0");
        await Assert.That(log).IsEquivalentTo(new[] { "install aether.basics" });
    }

    [Test]
    public async Task TheReviewIsTheModelsRowsAndInstallIsTheConsent()
    {
        var harness = new Harness();
        var model = PackageReviewModel.From(Contents(), ["Corvid (Aetherfall)"], harness.Config.Packages.Single());
        var (lines, shown, log, gone) = await Run(harness, window =>
        {
            var page = window.PackagePage!;
            harness.Manager.PublishReview(model);
            page.Refresh();
            var drawn = page.ReviewLines.ToArray();
            var on = page.ShownReview;
            Click(page.AcceptButton!);
            var calls = harness.Calls.Log.ToArray();
            harness.Manager.PublishReview(null);
            page.Refresh();
            return (drawn, on, calls, page.ShownReview is null);
        });

        var expected = model.Sections.SelectMany(s => new[] { s.Heading }.Concat(s.Rows.Select(r => r.Label.Length == 0 ? r.Value : $"{r.Label}: {r.Value}")));
        await Assert.That(lines).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(lines[0]).IsEqualTo(PackageReviewModel.ActsHeading);
        await Assert.That(ReferenceEquals(shown, model)).IsTrue();
        await Assert.That(log).IsEquivalentTo(new[] { "accept Aether basics" });
        await Assert.That(gone).IsTrue();
    }

    private static Task<T> Run<T>(Harness harness, Func<SettingsWindow, T> body) =>
        TerminalViewTests.Session.Dispatch(() =>
        {
            var window = new SettingsWindow(harness.Form());
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                return body(window);
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
}
