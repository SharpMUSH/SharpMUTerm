using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests;

/// <summary>
/// What ⌃F matches. The rules are few and each one is a decision the surface above it depends on, so
/// they are pinned here rather than inferred from the surface's behaviour.
/// </summary>
public class OutputSearchTests
{
    private static readonly string[] Lines =
    {
        "The goblin snarls at you.",
        "<OOC> Ana: Goblin room is bugged",
        "You hit the goblin for 12 damage.",
        "A town guard stands watch by the northern gate.",
    };

    [Test]
    public async Task APlainQueryFindsEveryLineHoldingIt()
    {
        var result = OutputSearch.Match(Lines, "goblin", regex: false);

        await Assert.That(result.Error).IsNull();
        await Assert.That(result.Matches.Select(m => m.LineIndex)).IsEquivalentTo(new[] { 0, 1, 2 });
    }

    /// <summary>
    /// Case-insensitive, which is what <c>HistorySearch</c> does — two search surfaces in one client
    /// disagreeing about case is a bug report waiting to happen.
    /// </summary>
    [Test]
    public async Task CaseIsIgnoredInBothModes()
    {
        await Assert.That(OutputSearch.Match(Lines, "GOBLIN", regex: false).Matches.Count).IsEqualTo(3);
        await Assert.That(OutputSearch.Match(Lines, "GOBLIN", regex: true).Matches.Count).IsEqualTo(3);
    }

    /// <summary>
    /// The way back for a reader who wants case to matter: an inline option, which is a documented .NET
    /// feature rather than one this client invented. It is why there is no third toggle on the surface.
    /// </summary>
    [Test]
    public async Task RegexModeHonoursAnInlineCaseOption()
    {
        var result = OutputSearch.Match(Lines, "(?-i)Goblin", regex: true);

        await Assert.That(result.Matches.Select(m => m.LineIndex)).IsEquivalentTo(new[] { 1 });
    }

    /// <summary>
    /// Plain mode is plain: a query full of metacharacters is text, not a pattern. Anything else and a
    /// reader searching for <c>$5.00</c> or <c>(OOC)</c> gets a silent misfire or an error they did not
    /// ask for.
    /// </summary>
    [Test]
    public async Task PlainModeTreatsMetacharactersLiterally()
    {
        var lines = new[] { "abc", "a.c" };

        var result = OutputSearch.Match(lines, "a.c", regex: false);

        await Assert.That(result.Matches.Select(m => m.LineIndex)).IsEquivalentTo(new[] { 1 });
    }

    [Test]
    public async Task RegexModeMatchesAsAPattern()
    {
        var result = OutputSearch.Match(Lines, @"\d+ damage", regex: true);

        await Assert.That(result.Matches.Select(m => m.LineIndex)).IsEquivalentTo(new[] { 2 });
    }

    /// <summary>
    /// An empty query matches nothing, which is where this parts company with <c>HistorySearch</c>: there
    /// an empty query is the opening chronological list, and a command history is short. A pane buffer is
    /// thousands of lines, and "everything, oldest first" is not a result set anybody asked for.
    /// </summary>
    [Test]
    public async Task AnEmptyQueryMatchesNothingAndIsNotAnError()
    {
        var result = OutputSearch.Match(Lines, string.Empty, regex: false);

        await Assert.That(result.Matches).IsEmpty();
        await Assert.That(result.Error).IsNull();
    }

    /// <summary>
    /// An invalid pattern is a state, not an exception. A regex is typed one character at a time, so
    /// most of the time a regex query is being typed it is invalid — throwing, or listing stale results,
    /// are both worse than saying so.
    /// </summary>
    [Test]
    public async Task AnInvalidPatternIsReportedRatherThanThrown()
    {
        var result = OutputSearch.Match(Lines, "goblin(", regex: true);

        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Matches).IsEmpty();
    }

    /// <summary>
    /// The same characters in plain mode are a query, not a pattern, so they cannot be invalid.
    /// </summary>
    [Test]
    public async Task ThatSameQueryIsFineInPlainMode()
    {
        await Assert.That(OutputSearch.Match(new[] { "goblin(x)" }, "goblin(", regex: false).Error).IsNull();
    }

    /// <summary>
    /// This runs on the UI thread, on every keystroke, over every line of every window. A pattern that
    /// backtracks catastrophically must come back as an error rather than wedge the client.
    /// </summary>
    [Test]
    public async Task ARunawayPatternTimesOutIntoAnError()
    {
        var lines = new[] { new string('a', 4000) + "b" };

        var result = OutputSearch.Match(lines, "(a+)+$", regex: true);

        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Matches).IsEmpty();
    }

    [Test]
    public async Task AnOverLongQueryIsRefused()
    {
        var result = OutputSearch.Match(Lines, new string('x', OutputSearch.MaxQueryLength + 1), regex: false);

        await Assert.That(result.Error).IsNotNull();
        await Assert.That(result.Matches).IsEmpty();
    }

    /// <summary>
    /// One match per line, the first, and its offsets — the result is a list of <em>lines to go to</em>,
    /// and the offsets are there so a row can show why it is listed.
    /// </summary>
    [Test]
    public async Task AMatchCarriesTheLineAndWhereTheQueryLandedInIt()
    {
        var result = OutputSearch.Match(new[] { "You hit the goblin, and the goblin falls." }, "goblin", regex: false);

        var match = result.Matches.Single();
        await Assert.That(match.Text).IsEqualTo("You hit the goblin, and the goblin falls.");
        await Assert.That(match.MatchStart).IsEqualTo(12);
        await Assert.That(match.MatchLength).IsEqualTo(6);
    }

    /// <summary>
    /// Oldest first, the buffer's own order. The rows are a transcript rather than a ranking, and the
    /// reader is looking for a place in it.
    /// </summary>
    [Test]
    public async Task MatchesKeepTheBuffersOwnOrder()
    {
        var result = OutputSearch.Match(Lines, "the", regex: false);

        await Assert.That(result.Matches.Select(m => m.LineIndex).ToArray())
            .IsEquivalentTo(result.Matches.Select(m => m.LineIndex).OrderBy(i => i).ToArray());
    }

    /// <summary>A zero-width regex match must not produce a row claiming to mark nothing.</summary>
    [Test]
    public async Task AZeroWidthPatternMatchesNoLines()
    {
        var result = OutputSearch.Match(Lines, "x*", regex: true);

        await Assert.That(result.Matches).IsEmpty();
    }
}
