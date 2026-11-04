using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Workspaces;
using TUnit.Assertions.Enums;

namespace SharpMUTerm.Client.Tests;

public class FuzzyMatchTests
{
    [Test]
    public async Task AQueryMatchesWhenItIsASubsequence_IgnoringCase()
    {
        await Assert.That(FuzzyMatch.Match("SPLIT", "Split right")).IsNotNull();
        await Assert.That(FuzzyMatch.Match("lgg", "Start logging")).IsNotNull();
        await Assert.That(FuzzyMatch.Match("zzz", "Start logging")).IsNull();
    }

    [Test]
    public async Task WordsAreMatchedInOrder_EachAfterTheLast()
    {
        var match = FuzzyMatch.Match("trig sel", "Make a trigger from the selection");
        await Assert.That(match).IsNotNull();
        await Assert.That(match!.Positions).IsEquivalentTo(new[] { 7, 8, 9, 10, 24, 25, 26 }, CollectionOrdering.Matching);
        await Assert.That(FuzzyMatch.Match("sel trig", "Make a trigger from the selection")).IsNull();
    }

    [Test]
    public async Task AWholePrefixBeatsAScatteredMatch()
    {
        var prefix = FuzzyMatch.Match("lay", "Layouts")!;
        var scattered = FuzzyMatch.Match("lay", "Clear window, say")!;
        await Assert.That(prefix.Score).IsGreaterThan(scattered.Score);
    }

    [Test]
    public async Task WordStartsBeatTheMiddleOfAWord()
    {
        // f…p at two word starts, against an f and a p buried in words.
        var starts = FuzzyMatch.Match("fp", "Freeze pane")!;
        var buried = FuzzyMatch.Match("fp", "Self-help")!;
        await Assert.That(starts.Score).IsGreaterThan(buried.Score);
    }

    [Test]
    public async Task TheBestAlignmentIsFound_NotTheLeftmost()
    {
        // The leftmost s is in "Show"; the best is the word start of "scrollback".
        var match = FuzzyMatch.Match("sc", "Show scrollback")!;
        await Assert.That(match.Positions).IsEquivalentTo(new[] { 5, 6 }, CollectionOrdering.Matching);
    }
}

public class CommandPaletteModelTests
{
    private static readonly CommandItem[] Items =
    {
        new(CommandGroup.Layout, "Split right", "layout:split-right", "⌃B |") { Key = "⌃B |" },
        new(CommandGroup.Layout, "Split down", "layout:split-down", "⌃B -") { Key = "⌃B -" },
        new(CommandGroup.Layout, "Layouts", "layout:layouts", "⌥M") { Key = "⌥M" },
        new(CommandGroup.Terminal, "Start logging", "term:log-on"),
        new(CommandGroup.Terminal, "Clear window", "term:clear"),
        new(CommandGroup.Terminal, "Make a trigger from the selection", "term:trigger-from-selection", "opens F2")
            { Detail = "opens F2" },
        new(CommandGroup.Terminal, "Copy the selection", "term:copy", "⌃C · drag") { Key = "⌃C", Detail = "drag" },
        new(CommandGroup.GoTo, "Go to #public", "win:2", "⌥2 · Corvid · 3 unread") { Key = "⌥2", Detail = "Corvid · 3 unread" },
    };

    private static IEnumerable<string> Ids(PaletteView view) => view.Entries.Select(e => e.Item.Id);

    private static IEnumerable<string> Headers(PaletteView view) => view.Rows.OfType<PaletteHeader>().Select(h => h.Label);

    [Test]
    public async Task AnEmptyQueryListsEveryGroupInItsFixedOrder()
    {
        var view = CommandPaletteModel.Build("", Items);
        await Assert.That(Headers(view)).IsEquivalentTo(new[] { "GO TO", "TERMINAL", "LAYOUT" }, CollectionOrdering.Matching);
        await Assert.That(view.Entries.Count).IsEqualTo(Items.Length);
        await Assert.That(view.Total).IsEqualTo(Items.Length);
    }

    [Test]
    public async Task LayFindsLayoutsFirst()
    {
        var view = CommandPaletteModel.Build("lay", Items);
        await Assert.That(view.Entries[0].Item.Id).IsEqualTo("layout:layouts");
        await Assert.That(view.Rows[0]).IsEqualTo(new PaletteHeader("LAYOUT"));
    }

    [Test]
    public async Task TrigSelFindsTheTriggerBuilder_WithItsCharactersMarked()
    {
        var view = CommandPaletteModel.Build("trig sel", Items);
        var first = view.Entries[0];
        await Assert.That(first.Item.Id).IsEqualTo("term:trigger-from-selection");
        await Assert.That(first.TitleMatches)
            .IsEquivalentTo(new[] { new TextRange(7, 4), new TextRange(24, 3) }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task TheBestGroupComesFirst_EvenWhenItIsNotTheFirstGroup()
    {
        // "log" is a title prefix only in TERMINAL; GO TO's entry does not match at all.
        var view = CommandPaletteModel.Build("log", Items);
        await Assert.That(view.Entries[0].Item.Id).IsEqualTo("term:log-on");
        await Assert.That(Headers(view).First()).IsEqualTo("TERMINAL");
    }

    [Test]
    public async Task ATitleMatchListsAboveADetailMatch()
    {
        // "corvid" is only in a detail; "c" … also matches titles, which must come first.
        var detailOnly = CommandPaletteModel.Build("corvid", Items);
        await Assert.That(Ids(detailOnly)).IsEquivalentTo(new[] { "win:2" });
        await Assert.That(detailOnly.Entries[0].Field).IsEqualTo(PaletteMatchField.Other);
        await Assert.That(detailOnly.Entries[0].TitleMatches).IsEmpty();
    }

    [Test]
    public async Task EqualScoresKeepCatalogOrder()
    {
        var view = CommandPaletteModel.Build("split", Items);
        await Assert.That(Ids(view).Take(2))
            .IsEquivalentTo(new[] { "layout:split-right", "layout:split-down" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task RecentComesFirst_NewestFirst_AndIsNotListedTwice()
    {
        var recent = new RecentCommands();
        recent.Record("term:clear");
        recent.Record("layout:split-down");
        recent.Record("gone:away"); // no longer offered: skipped

        var view = CommandPaletteModel.Build("", Items, recent.Ids);
        await Assert.That(view.Rows[0]).IsEqualTo(new PaletteHeader("RECENT"));
        await Assert.That(Ids(view).Take(2))
            .IsEquivalentTo(new[] { "layout:split-down", "term:clear" }, CollectionOrdering.Matching);
        await Assert.That(Ids(view).Count(id => id == "term:clear")).IsEqualTo(1);
        await Assert.That(view.Entries.Count).IsEqualTo(Items.Length);
    }

    [Test]
    public async Task RecentIsNotAGroupOnceSomethingIsTyped()
    {
        var view = CommandPaletteModel.Build("clear", Items, new[] { "term:clear" });
        await Assert.That(Headers(view)).DoesNotContain("RECENT");
    }

    [Test]
    public async Task TheStateSuggestsWhatItMakesLikely_AfterRecent()
    {
        var suggested = CommandPaletteModel.Suggested(new CommandContext(OutputSelected: true));
        await Assert.That(suggested).Contains("term:copy");
        await Assert.That(suggested).Contains("term:trigger-from-selection");

        var view = CommandPaletteModel.Build("", Items, new[] { "term:clear" }, suggested);
        await Assert.That(Headers(view).Take(2)).IsEquivalentTo(new[] { "RECENT", "SUGGESTED" }, CollectionOrdering.Matching);
        await Assert.That(Ids(view).Skip(1).Take(2))
            .IsEquivalentTo(new[] { "term:copy", "term:trigger-from-selection" }, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AnIdleClientSuggestsNothing()
    {
        await Assert.That(CommandPaletteModel.Suggested(new CommandContext())).IsEmpty();
    }

    [Test]
    public async Task KeysAreSpeltInOneVocabulary_AndTheKeyWidthIsTheWidest()
    {
        var items = new[]
        {
            new CommandItem(CommandGroup.Terminal, "Newline", "term:newline", "Alt+⏎") { Key = "Alt+⏎" },
            new CommandItem(CommandGroup.Layout, "Split right", "layout:split-right", "⌃B |") { Key = "⌃B |" },
        };
        var view = CommandPaletteModel.Build("", items);
        await Assert.That(view.Entries.Single(e => e.Item.Id == "term:newline").Key).IsEqualTo("⌥⏎");
        await Assert.That(view.KeyWidth).IsEqualTo(4);
    }

    [Test]
    public async Task TheRealCatalogCarriesKeysApartFromDetails()
    {
        var catalog = BuiltInCommands.Catalog(new Workspace(), Array.Empty<CharacterRef>(), null, new CommandContext());
        var view = CommandPaletteModel.Build("", catalog);

        var copy = view.Entries.Single(e => e.Item.Id == "term:copy");
        await Assert.That(copy.Key).IsEqualTo("⌃C");
        await Assert.That(copy.Detail).DoesNotContain("⌃C");
        var input = view.Entries.Single(e => e.Item.Id == "term:input2-on");
        await Assert.That(input.Key).IsEqualTo("⌃B i");
        await Assert.That(input.Detail).IsEqualTo("this window");
    }
}

public class KeyGlyphsTests
{
    [Test]
    [Arguments("Alt+⏎", "⌥⏎")]
    [Arguments("Ctrl+⏎", "⌃⏎")]
    [Arguments("Ctrl+Enter", "⌃⏎")]
    [Arguments("Shift+Tab", "⇧⇥")]
    [Arguments("⌃B |", "⌃B |")]
    [Arguments("F4", "F4")]
    [Arguments("PgUp", "PgUp")]
    public async Task ChordsAreSpeltInSymbols(string chord, string expected)
    {
        await Assert.That(KeyGlyphs.Normalize(chord)).IsEqualTo(expected);
    }
}

public class RecentCommandsTests
{
    [Test]
    public async Task RunningAgainMovesToTheFront_AndTheOldestFallsOff()
    {
        var recent = new RecentCommands(capacity: 3);
        foreach (var id in new[] { "a", "b", "c", "a", "d" })
        {
            recent.Record(id);
        }

        await Assert.That(recent.Ids).IsEquivalentTo(new[] { "d", "a", "c" }, CollectionOrdering.Matching);
    }
}
