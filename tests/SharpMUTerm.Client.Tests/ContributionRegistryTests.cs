using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Client.Tests;

public class ContributionRegistryTests
{
    private static ICommandContext Context(CommandContext? state = null, HostKeyGlyphs? keys = null) =>
        new CommandEnvironment(
            state ?? new CommandContext(),
            new Workspace(),
            Array.Empty<CharacterRef>(),
            null,
            Array.Empty<SettingsEntry>(),
            keys ?? HostKeyGlyphs.Terminal);

    private static SingleCommand Command(string id, string title = "T", ContributionOwner? owner = null) => new()
    {
        Id = id,
        Title = title,
        Category = CommandGroup.Terminal,
        Owner = owner ?? ContributionOwner.BuiltIn,
        Run = _ => true,
    };

    [Test]
    public async Task ADuplicateIdIsRefusedWithinAKind()
    {
        var registry = new ContributionRegistry();
        registry.Commands.Add(Command("x:one"));

        await Assert.That(() => registry.Commands.Add(Command("x:one", owner: ContributionOwner.Package("p"))))
            .Throws<ArgumentException>();
        await Assert.That(registry.Commands.Count).IsEqualTo(1);

        // Ids are per kind: a panel may share a command's id.
        registry.Panels.Add(new PanelContribution { Id = "x:one", Title = "One" });
        await Assert.That(registry.Panels.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ACommandUnderAFamilysPrefixIsRefused_AndOverlappingFamiliesToo()
    {
        var registry = new ContributionRegistry();
        registry.Commands.Add(new CommandFamily
        {
            Id = "fam", IdPrefix = "win:", Generate = _ => Array.Empty<CommandItem>(), Run = (_, _) => true,
        });

        await Assert.That(() => registry.Commands.Add(Command("win:chat"))).Throws<ArgumentException>();
        await Assert.That(() => registry.Commands.Add(new CommandFamily
        {
            Id = "fam2", IdPrefix = "win:x", Generate = _ => Array.Empty<CommandItem>(), Run = (_, _) => true,
        })).Throws<ArgumentException>();
    }

    [Test]
    public async Task TheCatalogIsInRegistrationOrder_AndHonoursAvailability()
    {
        var registry = new ContributionRegistry();
        registry.Commands.Add(Command("b:second", "Second"));
        registry.Commands.Add(new SingleCommand
        {
            Id = "a:hidden", Title = "Hidden", Category = CommandGroup.Terminal,
            IsAvailable = c => c.State.LoggingOn, Run = _ => true,
        });
        registry.Commands.Add(Command("a:first", "First"));

        var quiet = registry.Commands.Catalog(Context()).Select(c => c.Id).ToList();
        await Assert.That(quiet).IsEquivalentTo(new[] { "b:second", "a:first" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var logging = registry.Commands.Catalog(Context(new CommandContext(LoggingOn: true))).Select(c => c.Id).ToList();
        await Assert.That(logging).IsEquivalentTo(new[] { "b:second", "a:hidden", "a:first" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AHostKeyHintIsSpeltTheWayTheHostSpellsIt()
    {
        var registry = new ContributionRegistry();
        registry.Commands.Add(new SingleCommand
        {
            Id = "term:nl", Title = "Newline", Category = CommandGroup.Terminal,
            Key = KeyHint.ForHost(HostKey.Newline), Detail = "in the command line", Run = _ => true,
        });

        var terminal = registry.Commands.Catalog(Context()).Single();
        var window = registry.Commands.Catalog(Context(keys: new HostKeyGlyphs("Ctrl+⏎"))).Single();
        await Assert.That(terminal.Subtitle).IsEqualTo("Alt+⏎ · in the command line");
        await Assert.That(window.Subtitle).IsEqualTo("Ctrl+⏎ · in the command line");
    }

    [Test]
    public async Task DispatchFindsTheExactCommandFirst_ThenTheFamily_AndIgnoresAvailability()
    {
        var ran = new List<string>();
        var registry = new ContributionRegistry();
        registry.Commands.Add(new SingleCommand
        {
            Id = "term:log-on", Title = "Start logging", Category = CommandGroup.Terminal,
            IsAvailable = _ => false, Run = _ => { ran.Add("log"); return true; },
        });
        registry.Commands.Add(new CommandFamily
        {
            Id = "goto:windows", IdPrefix = "win:",
            Generate = _ => Array.Empty<CommandItem>(),
            Run = (id, _) => { ran.Add(id); return true; },
        });

        await Assert.That(registry.Commands.TryExecute("term:log-on", Context(), out var logHandled)).IsTrue();
        await Assert.That(registry.Commands.TryExecute("win:chat", Context(), out var winHandled)).IsTrue();
        await Assert.That(registry.Commands.TryExecute("nope", Context(), out var nopeHandled)).IsFalse();
        await Assert.That(logHandled && winHandled && !nopeHandled).IsTrue();
        await Assert.That(ran).IsEquivalentTo(new[] { "log", "win:chat" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        // A family's own id names the family, not a command.
        await Assert.That(registry.Commands.Resolve("goto:windows")).IsNull();
    }

    [Test]
    public async Task RemovingAPackageTakesEveryKindItRegistered_AndLeavesTheRestInOrder()
    {
        var package = ContributionOwner.Package("bells");
        var registry = new ContributionRegistry();
        registry.Commands.Add(Command("a:one"));
        registry.Commands.Add(Command("bells:ring", owner: package));
        registry.Commands.Add(Command("a:two"));
        registry.StatusCells.Add(new StatusCellContribution { Id = "bells", Owner = package, ReservedWidth = 3 });
        registry.Panels.Add(new PanelContribution { Id = "bells", Owner = package, Title = "Bells" });

        await Assert.That(registry.RemoveOwnedBy(package)).IsEqualTo(3);
        await Assert.That(registry.Commands.Select(c => c.Id).ToList())
            .IsEquivalentTo(new[] { "a:one", "a:two" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(registry.StatusCells.Count).IsEqualTo(0);

        // Its ids are free again.
        registry.Commands.Add(Command("bells:ring", owner: package));
        await Assert.That(registry.Commands.Contains("bells:ring")).IsTrue();
    }

    [Test]
    public async Task SectionsReadInOrder_CellsDropHighestFirst_TiesByRegistration()
    {
        var registry = new ContributionRegistry();
        registry.SettingsSections.Add(new SettingsSectionContribution { Id = "late", Title = "Late", Order = 20 });
        registry.SettingsSections.Add(new SettingsSectionContribution
        {
            Id = "early", Title = "Early", Order = 10,
            Fields = new SettingsField[]
            {
                new BoolField("on", "enabled"),
                new ChoiceField("mode", "mode", new[] { "a", "b" }),
                new NumberField("n", "count", 0, 10),
                new TextField("name", "name", MaxLength: 20),
                new ColourField("tint", "tint", new[] { "teal" }),
                new ListField("rules", "rules", new SettingsField[] { new TextField("pattern", "pattern") }),
            },
        });
        registry.SettingsSections.Add(new SettingsSectionContribution { Id = "tie", Title = "Tie", Order = 10 });

        await Assert.That(registry.SettingsSections.Ordered.Select(s => s.Id).ToList())
            .IsEquivalentTo(new[] { "early", "tie", "late" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        registry.StatusCells.Add(new StatusCellContribution { Id = "room", DropOrder = 1, ReservedWidth = 12 });
        registry.StatusCells.Add(new StatusCellContribution { Id = "meters", DropOrder = 5, ReservedWidth = 10 });
        registry.StatusCells.Add(new StatusCellContribution { Id = "mute", DropOrder = 1, ReservedWidth = 7 });
        await Assert.That(registry.StatusCells.InDropOrder.Select(s => s.Id).ToList())
            .IsEquivalentTo(new[] { "meters", "mute", "room" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

        await Assert.That(() => registry.SettingsSections.Add(new SettingsSectionContribution
        {
            Id = "dupe", Title = "Dupe",
            Fields = new SettingsField[] { new BoolField("x", "x"), new BoolField("x", "y") },
        })).Throws<ArgumentException>();
    }

    /// <summary>
    /// The built-in catalog is registered as contributions, so its ids are all addressable through the
    /// registry and none is refused for clashing with another.
    /// </summary>
    [Test]
    public async Task EveryBuiltInEntryResolvesToTheContributionThatListedIt()
    {
        var ran = new List<string>();
        var registry = new ContributionRegistry();
        BuiltInCommands.Register(registry, id => { ran.Add(id); return true; });

        var ws = new Workspace(mainWindowId: "main", mainTitle: "Main", sessionKey: "A.One");
        ws.RouteSpawn("Chat", "A.One");
        ws.Layout.SplitFocused(SplitDirection.Row);
        var characters = new CharacterRef[]
        {
            new("A", "One", "A.One", true, true, true),
            new("A", "Two", "A.Two", true, true),
        };
        var context = new CommandEnvironment(
            new CommandContext(ScriptsOn: true, Walking: true, Sound: true, Announced: false),
            ws,
            characters,
            "A.One",
            new SettingsEntry[] { new("Aliases", "screen:aliases", "F3") },
            HostKeyGlyphs.Terminal);

        var catalog = registry.Commands.Catalog(context);
        foreach (var item in catalog)
        {
            await Assert.That(registry.Commands.TryExecute(item.Id, context, out var handled)).IsTrue();
            await Assert.That(handled).IsTrue();
        }

        await Assert.That(ran).IsEquivalentTo(catalog.Select(c => c.Id).ToList(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(catalog.Select(c => c.Group).Distinct().Count()).IsEqualTo(Enum.GetValues<CommandGroup>().Length);
    }
}
