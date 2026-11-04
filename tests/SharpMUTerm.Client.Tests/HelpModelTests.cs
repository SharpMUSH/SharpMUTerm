using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Client.Help;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// In-app help (<see cref="HelpModel"/>), the one table of slash commands it and <c>--help</c> read
/// (<see cref="SlashCommands"/>), the dictionary offer (<see cref="DictionaryOfferModel"/>), and the ⌃P suggestion to
/// join a world's server wraps.
/// </summary>
public class HelpModelTests
{
    private static HelpModel Model() => HelpModel.Build(
        BuiltInCommands.Catalog(new Workspace(), Array.Empty<CharacterRef>(), null, new CommandContext()),
        "⌥⏎",
        [("⌃Q", "quits")]);

    [Test]
    public async Task HelpListsHowTosThenSlashCommandsThenKeys()
    {
        var model = Model();
        var sections = model.Entries.Select(e => e.Section).Distinct().ToList();

        await Assert.That(sections[0]).IsEqualTo(HelpModel.TopicsSection);
        await Assert.That(sections[1]).IsEqualTo(HelpModel.CommandsSection);
        await Assert.That(sections.Skip(2).All(s => s.StartsWith("KEYS", StringComparison.Ordinal))).IsTrue();
        await Assert.That(model.Entries.Count(e => e.Kind == HelpKind.Command)).IsEqualTo(SlashCommands.All.Count);
        await Assert.That(model.Topics.Select(t => t.Id)).Contains("getting-started");
        await Assert.That(model.Entries.Any(e => e.Section == HelpModel.HostSection && e.Key == "⌥⏎")).IsTrue()
            .Because("the host's newline is named as this host spells it");
    }

    [Test]
    public async Task EveryKeyRowIsAKeyTheRegistryOffers()
    {
        var model = Model();
        var catalog = BuiltInCommands.Catalog(new Workspace(), Array.Empty<CharacterRef>(), null, new CommandContext());
        foreach (var entry in model.Entries.Where(e => e.Kind == HelpKind.Key && e.Target.Length > 0))
        {
            var item = catalog.Single(i => i.Id == entry.Target);
            await Assert.That(entry.Key).IsEqualTo(KeyGlyphs.Normalize(item.Key!)).Because(entry.Title);
        }
    }

    [Test]
    public async Task TheSearchNarrowsByEveryWord_AndReadsAHowTosPage()
    {
        var model = Model();
        var connect = model.View("connect host");
        await Assert.That(connect).IsNotEmpty();
        await Assert.That(connect.Any(e => e.Title.StartsWith("/connect", StringComparison.Ordinal))).IsTrue();

        // "wraps" is said on the reading page, not in its title: the page is still found.
        await Assert.That(model.View("join wraps").Any(e => e.Kind == HelpKind.Topic && e.Target == "reading")).IsTrue();
        await Assert.That(model.View("zzqx")).IsEmpty();
        await Assert.That(model.View(string.Empty).Count).IsEqualTo(model.Entries.Count);
    }

    [Test]
    public async Task ChoosingAHowToKeepsHelpOpen_AndACommandEndsIt()
    {
        var model = Model();
        var chosen = new List<int>();
        var closed = 0;
        var session = new HelpSession(model, isDark: true, chosen.Add, () => closed++);
        var topic = model.Entries.ToList().FindIndex(e => e.Kind == HelpKind.Topic);
        var command = model.Entries.ToList().FindIndex(e => e.Kind == HelpKind.Command);

        await Assert.That(session.Choose(topic)).IsFalse();
        await Assert.That(session.IsEnded).IsFalse();
        await Assert.That(session.Choose(command)).IsTrue();
        await Assert.That(chosen).IsEquivalentTo(new[] { command });
        await Assert.That(session.IsEnded).IsTrue();
    }

    [Test]
    public async Task TheSlashTableNamesEachCommandOnce_WithAUsageThatStartsWithIt()
    {
        await Assert.That(SlashCommands.Names.Distinct().Count()).IsEqualTo(SlashCommands.Names.Count);
        foreach (var command in SlashCommands.All)
        {
            await Assert.That(command.Usage.StartsWith(command.Name, StringComparison.Ordinal)).IsTrue().Because(command.Usage);
            await Assert.That(command.Summary).IsNotEmpty();
        }
    }

    [Test]
    public async Task TheDictionaryOfferSaysWhereItFetchesFromAndWhereItSaves()
    {
        var model = new DictionaryOfferModel("English (US)", "en_US", "LibreOffice", "MIT-like", "raw.githubusercontent.com", "about 0.6 MB", "/home/a/dictionaries", false);
        var text = string.Join("\n", model.Lines);
        await Assert.That(text).Contains("raw.githubusercontent.com");
        await Assert.That(text).Contains("/home/a/dictionaries");

        var none = model with { Folder = null };
        await Assert.That(string.Join("\n", none.Lines)).Contains("nothing can be saved");
    }

    [Test]
    public async Task F1sKeyRowOffersADictionaryWhenThereIsNone()
    {
        var keys = ComposerModel.KeysFor(ComposerPlace.FullScreen, false, false, true, armed: true);
        await Assert.That(keys.Any(k => k.Key == "⌥W" && k.Label == ComposerModel.GetDictionary)).IsTrue();
        var with = ComposerModel.KeysFor(ComposerPlace.FullScreen, false, true, true, armed: true);
        await Assert.That(with.Any(k => k.Key == "⌥W" && k.Label == "add word")).IsTrue();
    }

    [Test]
    public async Task ARaggedWorldIsSuggested_AndItsEntryNamesTheWorld()
    {
        var context = new CommandContext(RaggedWraps: [new RaggedWrap("Aetherfall", 78, 60)]);
        var suggested = CommandPaletteModel.Suggested(context);
        await Assert.That(suggested).Contains(BuiltInCommands.JoinWraps("Aetherfall"));

        var catalog = BuiltInCommands.Catalog(new Workspace(), Array.Empty<CharacterRef>(), null, context);
        var item = catalog.Single(i => i.Id == BuiltInCommands.JoinWraps("Aetherfall"));
        await Assert.That(item.Title).IsEqualTo("Join server wraps for Aetherfall");
        await Assert.That(CommandPaletteModel.Suggested(new CommandContext())).DoesNotContain(BuiltInCommands.JoinWraps("Aetherfall"));
    }
}
