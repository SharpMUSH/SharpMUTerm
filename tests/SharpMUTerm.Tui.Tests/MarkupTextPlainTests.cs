using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <see cref="MarkupText.Plain"/> — the text a markup line actually puts on the screen, and what ⌃F
/// searches. Its offsets are the ones the surface marks a row's match with, so they have to be in the
/// same coordinate system as the width every renderer measures with.
/// </summary>
public class MarkupTextPlainTests
{
    [Test]
    public async Task TagsAreRemovedAndTheTextIsLeftAlone()
    {
        await Assert.That(MarkupText.Plain("[bold #ff0000]The goblin[/] snarls."))
            .IsEqualTo("The goblin snarls.");
    }

    /// <summary>
    /// The point of searching this rather than the markup: a world may change colour mid-word, and a
    /// query for the word has to find it. Same defect as a URL split by a colour change, one layer down.
    /// </summary>
    [Test]
    public async Task AColourChangeInsideAWordDoesNotSplitIt()
    {
        await Assert.That(MarkupText.Plain("gob[#00ff00]lin[/]")).IsEqualTo("goblin");
    }

    /// <summary>And the other half: a tag's own text is not searchable.</summary>
    [Test]
    public async Task ATagsContentsAreNotPartOfTheText()
    {
        await Assert.That(MarkupText.Plain("[#ff0000]red[/]")).DoesNotContain("ff0000");
    }

    [Test]
    public async Task EscapedBracketsComeBackAsTheOneCharacterTheyStandFor()
    {
        await Assert.That(MarkupText.Plain("[[OOC]] Ana: hello")).IsEqualTo("[OOC] Ana: hello");
    }

    [Test]
    public async Task ALinkSpanKeepsItsVisibleTextAndLosesItsTarget()
    {
        await Assert.That(MarkupText.Plain("see [link=https://example.com/map]the map[/] here"))
            .IsEqualTo("see the map here");
    }

    /// <summary>
    /// The invariant that keeps a match's offsets meaningful: this and <see cref="MarkupText.VisibleLength"/>
    /// must agree on every input, or a row would mark a run at a column the renderer measures differently.
    /// </summary>
    [Test]
    [Arguments("plain text")]
    [Arguments("[bold]bold[/] and [dim]dim[/]")]
    [Arguments("[[escaped]] and [#00ff00]coloured[/]")]
    [Arguments("[link=https://example.com]a link[/]")]
    [Arguments("")]
    [Arguments("[dim]12:04[/] [bold]0001[/] · the courier's road")]
    public async Task ItAgreesWithVisibleLength(string markup)
    {
        await Assert.That(MarkupText.Plain(markup).Length).IsEqualTo(MarkupText.VisibleLength(markup));
    }
}
