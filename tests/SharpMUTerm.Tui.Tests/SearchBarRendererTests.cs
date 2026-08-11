using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The bar drawn above the line ⌃F sent you to. Fourth of the boundary bars, and held to their rule:
/// mark the boundary, never restyle the content.
/// </summary>
public class SearchBarRendererTests
{
    [Test]
    public async Task ItCarriesTheQueryTheOrdinalAndTheChordThatGoesToTheNextHit()
    {
        var bar = SearchBarRenderer.Bar("goblin", 12, 38, "#c678dd");

        await Assert.That(bar).Contains($"[#c678dd]{Glyphs.Search} goblin (12 of 38)[/]");
        await Assert.That(bar).Contains(SearchBarRenderer.NextChord);
        await Assert.That(bar).Contains("[dim]");
        await Assert.That(bar).Contains("─");
    }

    /// <summary>
    /// The ordinal is what makes ⌥G legible: without it the bar moves and nothing says whether you are
    /// getting closer to the end of the results or going round in circles.
    /// </summary>
    [Test]
    public async Task TheOrdinalSaysWhereInTheResultsThisIs()
    {
        await Assert.That(SearchBarRenderer.Bar("key", 1, 1, "#c678dd")).Contains("(1 of 1)");
    }

    /// <summary>
    /// The query is the reader's own text going into markup. A search for <c>[public]</c> must appear on
    /// the bar rather than be eaten as a tag — the rule every renderer here follows for text it did not
    /// write.
    /// </summary>
    [Test]
    public async Task TheQueryIsEscapedRatherThanParsedAsMarkup()
    {
        var bar = SearchBarRenderer.Bar("[public]", 1, 2, "#c678dd");

        await Assert.That(bar).Contains("[[public]]");
    }

    [Test]
    public void ItRejectsAnEmptyAccent()
    {
        Assert.Throws<ArgumentException>(() => SearchBarRenderer.Bar("goblin", 1, 1, string.Empty));
    }

    /// <summary>
    /// The chord it names is the only one there is. ⌥⇧G would be the obvious partner and cannot arrive —
    /// kitty writes it as a CSI-u sequence this parser drops — so the bar must not offer it.
    /// </summary>
    [Test]
    public async Task ItNamesNoChordThatCannotArrive()
    {
        await Assert.That(SearchBarRenderer.Bar("goblin", 1, 3, "#c678dd")).DoesNotContain("⇧");
    }
}
