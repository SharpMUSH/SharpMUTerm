using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// What the ⌃F surface means by a keystroke, and what it says. The client wiring is
/// <see cref="SearchEndToEndTests"/>' job; this is the part that needs no terminal.
/// </summary>
public class SearchPromptTests
{
    private static ConsoleKeyInfo Key(char c) => new(c, ConsoleKey.NoName, false, false, false);

    private static ConsoleKeyInfo Bare(ConsoleKey key) => new('\0', key, false, false, false);

    private static ConsoleKeyInfo Alt(ConsoleKey key) => new('\0', key, false, true, false);

    private static ConsoleKeyInfo Ctrl(ConsoleKey key) => new('\0', key, false, false, true);

    private static SearchDecision Interpret(
        ConsoleKeyInfo key, string query = "gob", int selected = 0, int count = 3,
        bool regex = false, bool all = false) =>
        SearchPrompt.Interpret(key, query, selected, count, regex, all);

    private static readonly SearchRow[] Rows =
    {
        new("main", "main", 12, "The goblin snarls at you.", 4, 6),
        new("spawn:chat", "Chat", 3, "<OOC> Ana: goblin room is bugged", 11, 6),
    };

    [Test]
    public async Task TypingBuildsTheQueryAndPointsAtTheFirstRow()
    {
        var decision = Interpret(Key('x'), query: "gob", selected: 2);

        await Assert.That(decision.Action).IsEqualTo(SearchAction.Redraw);
        await Assert.That(decision.Query).IsEqualTo("gobx");
        await Assert.That(decision.Selected).IsEqualTo(0);
    }

    [Test]
    public async Task BackspaceWidensTheQuery()
    {
        var decision = Interpret(Bare(ConsoleKey.Backspace), query: "gob");

        await Assert.That(decision.Action).IsEqualTo(SearchAction.Redraw);
        await Assert.That(decision.Query).IsEqualTo("go");
    }

    [Test]
    public async Task BackspaceOnAnEmptyQueryDoesNothing()
    {
        await Assert.That(Interpret(Bare(ConsoleKey.Backspace), query: string.Empty).Action)
            .IsEqualTo(SearchAction.None);
    }

    [Test]
    public async Task TheArrowsWalkTheRowsAndWrap()
    {
        await Assert.That(Interpret(Bare(ConsoleKey.DownArrow), selected: 2, count: 3).Selected).IsEqualTo(0);
        await Assert.That(Interpret(Bare(ConsoleKey.UpArrow), selected: 0, count: 3).Selected).IsEqualTo(2);
    }

    [Test]
    public async Task EnterGoes()
    {
        await Assert.That(Interpret(Bare(ConsoleKey.Enter)).Action).IsEqualTo(SearchAction.Go);
    }

    /// <summary>
    /// With nothing listed there is nowhere to go, so ⏎ leaves the surface up: the query is what needs
    /// fixing, and Esc is the advertised way out.
    /// </summary>
    [Test]
    public async Task EnterWithNothingListedLeavesTheSurfaceUp()
    {
        await Assert.That(Interpret(Bare(ConsoleKey.Enter), count: 0).Action).IsEqualTo(SearchAction.None);
    }

    [Test]
    public async Task EscapeAndCtrlFBothClose()
    {
        await Assert.That(Interpret(Bare(ConsoleKey.Escape)).Action).IsEqualTo(SearchAction.Cancel);
        await Assert.That(Interpret(Ctrl(ConsoleKey.F)).Action).IsEqualTo(SearchAction.Cancel);
    }

    /// <summary>
    /// Both toggles re-search from the top: each changes what the list *is*, and a pointer kept at row
    /// 12 of a different result set points at nothing the reader chose.
    /// </summary>
    [Test]
    public async Task AltEAndAltAFlipTheirTogglesAndResetThePointer()
    {
        var regex = Interpret(Alt(ConsoleKey.E), selected: 2, regex: false);
        await Assert.That(regex.Action).IsEqualTo(SearchAction.Redraw);
        await Assert.That(regex.Regex).IsTrue();
        await Assert.That(regex.Selected).IsEqualTo(0);

        var all = Interpret(Alt(ConsoleKey.A), selected: 2, all: false);
        await Assert.That(all.AllWindows).IsTrue();
        await Assert.That(all.Selected).IsEqualTo(0);

        // And back again — they are toggles, not switches that only turn on.
        await Assert.That(Interpret(Alt(ConsoleKey.E), regex: true).Regex).IsFalse();
        await Assert.That(Interpret(Alt(ConsoleKey.A), all: true).AllWindows).IsFalse();
    }

    /// <summary>
    /// Anything unrecognised is swallowed. A modal over the workspace that let keys through would be
    /// typing into the command line it is covering.
    /// </summary>
    [Test]
    public async Task UnrecognisedKeysAreSwallowed()
    {
        await Assert.That(Interpret(Ctrl(ConsoleKey.K)).Action).IsEqualTo(SearchAction.None);
        await Assert.That(Interpret(Alt(ConsoleKey.Z)).Action).IsEqualTo(SearchAction.None);
        await Assert.That(Interpret(Bare(ConsoleKey.Tab)).Action).IsEqualTo(SearchAction.None);
        await Assert.That(Interpret(Bare(ConsoleKey.F5)).Action).IsEqualTo(SearchAction.None);
    }

    /// <summary>
    /// The honesty rule the settings screens and the composer are held to: every key the footer names
    /// does something. Pressed here rather than read, so a hint that outlived its binding fails.
    /// </summary>
    [Test]
    public async Task EveryKeyTheFooterNamesDoesSomething()
    {
        var named = new (string Name, ConsoleKeyInfo Key)[]
        {
            ("↑", Bare(ConsoleKey.UpArrow)),
            ("↓", Bare(ConsoleKey.DownArrow)),
            ("⏎", Bare(ConsoleKey.Enter)),
            ("⌥E", Alt(ConsoleKey.E)),
            ("⌥A", Alt(ConsoleKey.A)),
            ("Esc", Bare(ConsoleKey.Escape)),
            ("⌃F", Ctrl(ConsoleKey.F)),
            ("type", Key('g')),
        };

        foreach (var (name, key) in named)
        {
            await Assert.That(Interpret(key).Action).IsNotEqualTo(SearchAction.None).Because($"{name} is advertised");
        }

        // And the footer names each of them, so the two halves cannot drift apart.
        foreach (var fragment in new[] { "↑↓", "⏎", "⌥E", "⌥A", "Esc", "⌃F", "type to search" })
        {
            await Assert.That(SearchPrompt.Hints).Contains(fragment);
        }
    }

    /// <summary>
    /// The surface says which way both toggles are set. Neither is visible in the results, and both
    /// change what a query finds.
    /// </summary>
    [Test]
    public async Task TheHeaderSaysWhichWayTheTogglesAreSet()
    {
        var text = string.Join('\n', SearchPrompt.Render(Rows, "gob", null, false, false, "main", 4812, 0));
        var pattern = string.Join('\n', SearchPrompt.Render(Rows, "gob", null, true, true, "main", 4812, 0));

        await Assert.That(text).Contains("text");
        await Assert.That(text).Contains("main");
        await Assert.That(pattern).Contains("regex");
        await Assert.That(pattern).Contains("every window");
    }

    /// <summary>
    /// The bound is on the frame. ⌃F searches the lines the client is holding, not a session's whole
    /// history, and a reader who cannot find something from an hour ago should be able to see why.
    /// </summary>
    [Test]
    public async Task TheHeaderStatesHowMuchWasSearched()
    {
        var lines = string.Join('\n', SearchPrompt.Render(Rows, "gob", null, false, false, "main", 4812, 0));

        await Assert.That(lines).Contains("2 found");
        await Assert.That(lines).Contains("4,812 lines held");
    }

    [Test]
    public async Task AnInvalidPatternIsSaidRatherThanShownAsNoMatches()
    {
        var lines = string.Join(
            '\n', SearchPrompt.Render(Array.Empty<SearchRow>(), "gob(", "unterminated group", true, false, "main", 10, -1));

        await Assert.That(lines).Contains("unterminated group");
        await Assert.That(lines).DoesNotContain("no matches");
    }

    [Test]
    public async Task AnEmptyQuerySaysWhatToDoRatherThanNothing()
    {
        var lines = string.Join(
            '\n', SearchPrompt.Render(Array.Empty<SearchRow>(), string.Empty, null, false, false, "main", 10, -1));

        await Assert.That(lines).Contains("type to search main");
    }

    [Test]
    public async Task NoMatchesNamesTheKeyThatWidensTheQuery()
    {
        var lines = string.Join(
            '\n', SearchPrompt.Render(Array.Empty<SearchRow>(), "zzz", null, false, false, "main", 10, -1));

        await Assert.That(lines).Contains("⌫ widens");
    }

    /// <summary>
    /// The window column is drawn only when there is more than one window in the results — with one
    /// window it would be the same word on every row, saying nothing.
    /// </summary>
    [Test]
    public async Task TheWindowColumnAppearsOnlyWhenEveryWindowIsSearched()
    {
        var one = string.Join('\n', SearchPrompt.Render(Rows, "gob", null, false, false, "main", 10, -1));
        var all = string.Join('\n', SearchPrompt.Render(Rows, "gob", null, false, true, "main", 10, -1));

        await Assert.That(one).DoesNotContain("Chat");
        await Assert.That(all).Contains("Chat");
    }

    /// <summary>A row shows why it is listed: the matched run is marked in the accent.</summary>
    [Test]
    public async Task AnUnselectedRowMarksWhereTheQueryLanded()
    {
        var lines = SearchPrompt.Render(Rows, "goblin", null, false, false, "main", 10, selected: 1);

        await Assert.That(lines.Any(l => l.Contains("[bold ") && l.Contains("goblin"))).IsTrue();
    }

    [Test]
    public async Task ScrollKeepsThePointedAtRowInsideTheListArea()
    {
        await Assert.That(SearchPrompt.Scroll(0, 9, 40, 5)).IsEqualTo(5);
        await Assert.That(SearchPrompt.Scroll(5, 5, 40, 5)).IsEqualTo(5);
        await Assert.That(SearchPrompt.Scroll(20, 0, 40, 5)).IsEqualTo(0);
    }
}
