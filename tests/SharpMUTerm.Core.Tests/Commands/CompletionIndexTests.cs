using SharpMUTerm.Core.Commands;

namespace SharpMUTerm.Core.Tests.Commands;

public class CompletionIndexTests
{
    // Newest first, and a later word on a line is newer than an earlier one.
    [Test]
    public async Task WordsAreOfferedNewestFirstWithoutRegardToCase()
    {
        var index = new CompletionIndex();
        index.ObserveLine("The gatehouse looms.");
        index.ObserveLine("Gandalf arrives from the gate.");

        await Assert.That(index.Candidates("ga")).IsEquivalentTo(
            new[] { "gate", "Gandalf", "gatehouse" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task NamesComeBeforeWords()
    {
        var index = new CompletionIndex();
        index.ObserveLine("A galleon sails past.");
        index.ObserveGmcp("Room.Players", """[{"name":"Galadriel","fullname":"Galadriel the Fair"}]""");
        index.ObserveGmcp("Comm.Channel.Text", """{"channel":"ooc","talker":"Gimli","text":"hi"}""");

        await Assert.That(index.Candidates("g")).IsEquivalentTo(
            new[] { "Gimli", "Galadriel", "galleon" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ShortWordsTheTypedPrefixAndMalformedGmcpAreLeftOut()
    {
        var index = new CompletionIndex();
        index.ObserveLine("go to it, gondolier");
        index.ObserveGmcp("Room.Players", "{not json");
        index.ObserveGmcp("Char.Vitals", """{"name":"Gollum"}""");

        await Assert.That(index.Candidates("go")).IsEquivalentTo(new[] { "gondolier" });
        await Assert.That(index.Candidates("gondolier")).IsEmpty();
        await Assert.That(index.Candidates(string.Empty)).IsEmpty();
    }

    [Test]
    public async Task TheOldestWordGoesPastTheCap()
    {
        var index = new CompletionIndex();
        index.ObserveLine("zephyr");
        for (var i = 0; i < CompletionIndex.MaxWords; i++)
        {
            index.ObserveLine($"word{i:D5}x");
        }

        await Assert.That(index.Candidates("zep")).IsEmpty();
        await Assert.That(index.Candidates("word00001")).IsNotEmpty();
    }
}
