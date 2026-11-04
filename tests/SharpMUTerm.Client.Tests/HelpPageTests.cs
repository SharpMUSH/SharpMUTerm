using SharpMUTerm.Client.Help;
using SharpMUTerm.Client.Settings;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// A help page as both hosts draw it (<see cref="HelpPage"/>): headings read off a paragraph's opening label, keys found
/// by their glyphs, the map's key table kept as rows. And the highlight-names explanation both hosts show where the field
/// is edited.
/// </summary>
public class HelpPageTests
{
    private static HelpTopicLookup Topics => new(HelpTopics.For("⌥⏎"));

    private sealed record HelpTopicLookup(IReadOnlyList<Modals.HelpTopic> All)
    {
        public Modals.HelpTopic this[string id] => All.Single(t => t.Id == id);
    }

    [Test]
    public async Task AParagraphOpeningWithALabelIsABlockHeadedByIt()
    {
        var blocks = HelpPage.Blocks(Topics["reading"]);

        await Assert.That(blocks.Select(b => b.Heading ?? string.Empty)).IsEquivalentTo(new[] { "Scroll", "Search", "Freeze", "Ragged lines", "Copy" });
        await Assert.That(blocks[^1].Text).StartsWith("Drag across output").Because("the sentence starts the block, so it is capitalised");
        await Assert.That(blocks[0].Text).Contains("the live tail below").Because("a paragraph's source lines are joined");
    }

    [Test]
    public async Task LinesInsideOneParagraphThatOpenWithALabelAreBlocksOfTheirOwn()
    {
        var headings = HelpPage.Blocks(Topics["keys"]).Select(b => b.Heading).Where(h => h is not null).ToList();

        await Assert.That(headings).Contains("Windows");
        await Assert.That(headings).Contains("Characters");
        await Assert.That(headings).Contains("Connection");
    }

    [Test]
    public async Task KeysAreSpansOfTheirOwn()
    {
        var keys = HelpPage.Spans("⌃Z/⌃Y undo, ⌥⇧ and an arrow, ⇧↑/⇧↓ a line, PgUp/PgDn, ⏎ goes, F5 ▸ join, Esc closes, ⌃B | splits")
            .Where(s => s.IsKey).Select(s => s.Text).ToList();

        await Assert.That(keys).IsEquivalentTo(new[] { "⌃Z", "⌃Y", "⌥⇧", "⇧↑", "⇧↓", "PgUp", "PgDn", "⏎", "F5", "Esc", "⌃B" });
        await Assert.That(string.Concat(HelpPage.Spans("a ⌃F b").Select(s => s.Text))).IsEqualTo("a ⌃F b").Because("nothing is lost");
    }

    [Test]
    public async Task TheMapsKeyTableStaysAsRows()
    {
        var rows = HelpPage.Blocks(Topics[HelpTopics.MapTopic]).Where(b => b.Preformatted).ToList();

        await Assert.That(rows.Count).IsGreaterThan(1).Because("each row of the table is a row, not one joined paragraph");
        await Assert.That(rows.All(r => r.Heading is null)).IsTrue();
    }

    [Test]
    public async Task TheHighlightNamesHelpSaysTheSyntaxAndEveryColour()
    {
        var help = BuiltInSettings.World.Fields.Single(f => f.Id == "highlight-names").Help!;

        await Assert.That(help).Contains(HighlightColours.NameSyntax);
        foreach (var colour in HighlightColours.NameColours)
        {
            await Assert.That(help).Contains(colour);
        }

        await Assert.That(HighlightColours.NameColours).DoesNotContain(HighlightColours.None);
    }
}
