using SharpMUTerm.Client.Composer;
using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// Wave 18's model facts, each one field both hosts read: the line a Lua error names (<see cref="ScriptErrors"/>), the
/// history view centred on its hit with context either side, and ⌃F's ⌥H offer laddered to the status line's room.
/// </summary>
public class SearchAndScriptModelTests
{
    [Test]
    [Arguments("corvid.lua:(14,22-30): attempt to index a nil value", "corvid.lua", 14)]
    [Arguments("! corvid.lua:(14,22-30): attempt to index a nil value (+1 more in the console)", "corvid.lua", 14)]
    [Arguments("main.lua:3: unexpected symbol", "main.lua", 3)]
    [Arguments("other.lua:(9,1): oops", "corvid.lua", 0)]
    [Arguments("package:pack/combat.lua names combat.lua:(2,4)", "package:pack/combat.lua", 2)]
    [Arguments("saved 12:00:00 · reloaded · 2 files loaded", "corvid.lua", 0)]
    public async Task TheLineAnErrorNamesIsReadForItsOwnFileOnly(string text, string name, int line)
    {
        await Assert.That(ScriptErrors.LineIn(text, name) ?? 0).IsEqualTo(line);
    }

    [Test]
    public async Task TheNewestErrorNamingTheFileWins()
    {
        string[] errors = ["corvid.lua:(3,1): old", "main.lua:(8,1): elsewhere", "corvid.lua:(11,2): newer"];

        await Assert.That(ScriptErrors.LineIn(errors, "corvid.lua")).IsEqualTo(11);
        await Assert.That(ScriptErrors.LineIn(errors, "nothing.lua")).IsNull();
    }

    [Test]
    public async Task TheHistoryViewOpensCentredOnItsHit()
    {
        var top = ScrollbackViewModel.TopFor(1_000, 0, 5_000);

        await Assert.That(1_000 - top).IsEqualTo(ScrollbackViewModel.MaxPageRows / 2).Because("as many lines above the hit as the page allows");
        await Assert.That(ScrollbackViewModel.TopFor(3, 0, 5_000)).IsEqualTo(0).Because("near the oldest line the page starts at it");
        await Assert.That(ScrollbackViewModel.TopFor(4_998, 0, 5_000)).IsEqualTo(5_000 - ScrollbackViewModel.MaxPageRows);
    }

    [Test]
    public async Task TheLinesEitherSideOfTheHitAreItsContext()
    {
        var lines = Enumerable.Range(90, ScrollbackViewModel.MaxPageRows).Select(i => new ScrollbackViewLine(i, $"line {i}", -1, 0)).ToArray();
        var model = new ScrollbackViewModel("Corvid", "goblin", 90, 0, 500, 100, lines);

        await Assert.That(model.Role(lines[10])).IsEqualTo(ScrollbackLineRole.Hit);
        await Assert.That(model.Role(lines[10 - ScrollbackViewModel.ContextRows])).IsEqualTo(ScrollbackLineRole.Context);
        await Assert.That(model.Role(lines[10 + ScrollbackViewModel.ContextRows])).IsEqualTo(ScrollbackLineRole.Context);
        await Assert.That(model.Role(lines[10 + ScrollbackViewModel.ContextRows + 1])).IsEqualTo(ScrollbackLineRole.Page);
        await Assert.That(model.Role(lines[0])).IsEqualTo(ScrollbackLineRole.Page);
    }

    [Test]
    [Arguments(24, 80, 13)]
    [Arguments(32, 120, 20)]
    [Arguments(40, 160, 20)]
    [Arguments(12, 40, ScrollbackViewModel.MinPageRows)]
    public async Task TheHistoryPageIsSizedFromTheScreen(int rows, int columns, int page)
    {
        await Assert.That(ScrollbackViewModel.PageRowsFor(rows, columns)).IsEqualTo(page);
        await Assert.That(ScrollbackViewModel.TwoTitleRows(columns)).IsEqualTo(columns < ScrollbackViewModel.WideColumns);
    }

    [Test]
    public async Task APageMovesByTheRowsTheModelCarries()
    {
        var lines = Enumerable.Range(90, 13).Select(i => new ScrollbackViewLine(i, $"line {i}", -1, 0)).ToArray();
        var model = new ScrollbackViewModel("Corvid", "goblin", 90, 0, 500, 96, lines, 13);

        await Assert.That(model.Move(ConsoleKey.PageDown)).IsEqualTo(90 + 12);
        await Assert.That(model.Move(ConsoleKey.End)).IsEqualTo(500 - 13);
        await Assert.That(ScrollbackViewModel.TopFor(96, 0, 500, 13)).IsEqualTo(96 - 6);
        await Assert.That(model).IsNotEqualTo(model with { PageRows = 20 }).Because("the page size is part of what both hosts draw");
        await Assert.That(ScrollbackViewModel.KeysLadder.All(k => k.EndsWith("Esc closes", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task OlderHistoryCountsShareOneRowAndOlderRowsSayHowFarBack()
    {
        SearchRow[] rows =
        [
            new("main", "Corvid", 3, "The goblin snarls.", 4, 6),
            new("main", "Corvid", -1, "A goblin leaps.", 2, 6, 1_200, "demo", SearchModel.AgeLabel(4_799)),
        ];
        var model = new SearchModel("goblin", false, false, "Corvid", 39, null, rows, Older: true)
        {
            OlderFound = 4, OlderSearched = 6_000, OlderTotal = 6_000,
        };

        await Assert.That(model.CountText).IsEqualTo("this window 1 of 39 · older 4 of 6,000");
        await Assert.That((model with { HistoryRunning = true, OlderSearched = 1_200 }).CountText).IsEqualTo("this window 1 of 39 · older 4… 1,200 of 6,000 read");
        await Assert.That(model.LabelFor(rows[0])).IsEqualTo("here");
        await Assert.That(model.LabelFor(rows[1])).IsEqualTo("4.8k lines ago");
        await Assert.That((model with { AllWindows = true }).LabelFor(rows[0])).IsEqualTo("Corvid · here");
        await Assert.That(new SearchModel("goblin", false, false, "Corvid", 39, null, [rows[0]]).CountText).IsEqualTo("1 found · 39 lines held")
            .Because("without ⌥H the count is the one it always was");
        await Assert.That(SearchModel.AgeLabel(40)).IsEqualTo("40 lines ago");
        await Assert.That(SearchModel.AgeLabel(1)).IsEqualTo("1 line ago");
    }

    [Test]
    public async Task AConsoleErrorNamingAScriptLineIsALinkAndNothingElseIs()
    {
        ScriptConsoleLine[] lines =
        [
            new(ScriptConsoleKind.Error, "corvid.lua:(14,16-26): attempt to call a nil value"),
            new(ScriptConsoleKind.Error, "stopped after 2,000,000 instructions"),
            new(ScriptConsoleKind.Output, "corvid.lua:(3,1) printed by a script"),
        ];
        var model = new ScriptConsoleModel("Corvid", null, lines, [], [], 0, ["corvid.lua"], 1, "scripts/");

        await Assert.That(model.ErrorAt(0)).IsEqualTo(("corvid.lua", 14));
        await Assert.That(model.ErrorAt(1)).IsNull().Because("an error that names no line");
        await Assert.That(model.ErrorAt(2)).IsNull().Because("only an error row: what a script printed is not a link");
        await Assert.That(model.ErrorAt(9)).IsNull();
        await Assert.That(ScriptErrors.At("! main.lua:3: unexpected symbol")).IsEqualTo(("main.lua", 3));
    }

    private static SearchModel Found(string query = "goblin", long older = 5_930, bool on = false) =>
        new(query, false, false, "Corvid", 70, null, [], Older: on) { OlderLines = older, OlderSource = "Corvid" };

    [Test]
    public async Task OlderHistoryIsOfferedOnlyWhileThePaneCapTrimmedWhatWasSearched()
    {
        await Assert.That(Found().OffersOlder).IsTrue();
        await Assert.That(Found(older: 0).OffersOlder).IsFalse().Because("the panes hold everything: ⌥H has nothing more to find");
        await Assert.That(Found(query: string.Empty).OffersOlder).IsFalse().Because("nothing has been searched yet");
        await Assert.That(Found(on: true).OffersOlder).IsFalse().Because("⌥H is already on");
    }

    [Test]
    public async Task TheOfferIsLadderedToTheRoomAndAlwaysNamesTheChord()
    {
        var model = Found();
        var ladder = model.OlderHintLadder;

        await Assert.That(ladder[0]).IsEqualTo("⌥H searches older history · 5,930 more lines in Corvid");
        await Assert.That(ladder.All(r => r.StartsWith(SearchModel.OlderKey, StringComparison.Ordinal))).IsTrue();
        for (var i = 1; i < ladder.Count; i++)
        {
            await Assert.That(ladder[i].Length).IsLessThan(ladder[i - 1].Length);
        }

        await Assert.That(model.NoteFor(200)).IsEqualTo(ladder[0]);
        await Assert.That(model.NoteFor(40)).IsEqualTo("⌥H searches older history");
        await Assert.That(model.NoteFor(5)).IsEqualTo(ladder[^1]).Because("the shortest rung, never nothing");
    }
}
