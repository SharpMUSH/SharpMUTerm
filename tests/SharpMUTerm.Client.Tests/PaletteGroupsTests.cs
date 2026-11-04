using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// ⌃P's groups: OUTPUT used to hold forty-odd entries (help, packages, every map and panel command, the composer); the
/// catalog is now grouped by what an entry is about, and a new configuration is pointed at where to start.
/// </summary>
public class PaletteGroupsTests
{
    private static IReadOnlyList<CommandItem> Catalog(CommandContext? state = null)
    {
        var registry = new ContributionRegistry();
        BuiltInCommands.Register(registry, _ => true);
        return registry.Commands.Catalog(new CommandEnvironment(
            state ?? new CommandContext(),
            new Workspace(),
            Array.Empty<CharacterRef>(),
            null,
            Array.Empty<SettingsEntry>(),
            HostKeyGlyphs.Terminal));
    }

    private static CommandGroup GroupOf(IReadOnlyList<CommandItem> catalog, string id) => catalog.Single(i => i.Id == id).Group;

    [Test]
    public async Task EachEntryIsListedUnderWhatItIsAbout()
    {
        var catalog = Catalog();
        await Assert.That(GroupOf(catalog, BuiltInCommands.HelpCommand)).IsEqualTo(CommandGroup.Help);
        await Assert.That(GroupOf(catalog, BuiltInCommands.ImportCommand)).IsEqualTo(CommandGroup.Help);
        await Assert.That(GroupOf(catalog, "term:packages")).IsEqualTo(CommandGroup.Help);
        await Assert.That(GroupOf(catalog, "term:map")).IsEqualTo(CommandGroup.Map);
        await Assert.That(GroupOf(catalog, "map:edit")).IsEqualTo(CommandGroup.Map);
        await Assert.That(GroupOf(catalog, "panel:vitals")).IsEqualTo(CommandGroup.Panels);
        await Assert.That(GroupOf(catalog, "term:compose")).IsEqualTo(CommandGroup.Compose);
        await Assert.That(GroupOf(catalog, "term:history")).IsEqualTo(CommandGroup.Compose);
        await Assert.That(GroupOf(catalog, BuiltInCommands.SceneStartCommand)).IsEqualTo(CommandGroup.Terminal);

        // OUTPUT is about output now: a group a reader can scan.
        await Assert.That(catalog.Count(i => i.Group == CommandGroup.Terminal)).IsLessThanOrEqualTo(16);
    }

    [Test]
    public async Task TheGroupsAreListedInTheirFixedOrderWithHelpFirstInItsOwn()
    {
        var view = CommandPaletteModel.Build(string.Empty, Catalog());
        var headers = view.Rows.OfType<PaletteHeader>().Select(h => h.Label).ToList();
        await Assert.That(headers).IsEquivalentTo(
            new[] { "CONNECTION", "OUTPUT", "WRITING", "MAP", "PANELS", "WINDOWS & PANES", "HELP & SETUP" },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var help = view.Entries.First(e => e.Item.Group == CommandGroup.Help);
        await Assert.That(help.Item.Id).IsEqualTo(BuiltInCommands.HelpCommand);
    }

    [Test]
    public async Task ANewConfigurationIsSuggestedHelpWelcomeAndImport()
    {
        var suggested = CommandPaletteModel.Suggested(new CommandContext(NewConfiguration: true));
        await Assert.That(suggested.Take(3)).IsEquivalentTo(
            new[] { BuiltInCommands.HelpCommand, BuiltInCommands.WelcomeCommand, BuiltInCommands.ImportCommand },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(CommandPaletteModel.Suggested(new CommandContext())).DoesNotContain(BuiltInCommands.HelpCommand);

        var view = CommandPaletteModel.Build(string.Empty, Catalog(), suggested: suggested);
        await Assert.That(view.Rows[0]).IsEqualTo(new PaletteHeader(CommandPaletteModel.SuggestedLabel));
        await Assert.That(view.Entries[0].Item.Id).IsEqualTo(BuiltInCommands.HelpCommand);
    }

    [Test]
    public async Task TheRoomsAMapFindListedAreWalkToEntriesThatItsQueryAloneLists()
    {
        var found = new[] { new FoundRoom(12, "The Grand Plaza", "Aetherfall", 0), new FoundRoom(40, "Plaza Steps", null, 1) };
        var catalog = Catalog(new CommandContext(MapFound: found));
        var walks = catalog.Where(i => i.Id.StartsWith(BuiltInCommands.MapWalkPrefix, StringComparison.Ordinal)).ToList();
        await Assert.That(walks.Select(w => w.Id)).IsEquivalentTo(
            new[] { BuiltInCommands.MapWalk(12), BuiltInCommands.MapWalk(40) }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(walks[0].Title).IsEqualTo("Walk to The Grand Plaza");
        await Assert.That(walks[0].Detail).IsEqualTo("Aetherfall · level 0 · #12");
        await Assert.That(walks.All(w => w.Group == CommandGroup.Map)).IsTrue();

        // The palette opens with the find's query typed in, and lists the rooms and nothing else.
        var view = CommandPaletteModel.Build(BuiltInCommands.MapWalkQuery, catalog);
        await Assert.That(view.Entries.Select(e => e.Item.Id)).IsEquivalentTo(
            walks.Select(w => w.Id), TUnit.Assertions.Enums.CollectionOrdering.Matching);

        // Nothing found, nothing offered.
        await Assert.That(Catalog().Any(i => i.Id.StartsWith(BuiltInCommands.MapWalkPrefix, StringComparison.Ordinal))).IsFalse();
    }
}
