using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Protocols;

/// <summary>MXP <c>&lt;SEND&gt;</c> menus and <c>&amp;text;</c>, fed a line and a flush the way a session does.</summary>
public class MxpSendMenuTests
{
    private const string Secure = "\x1b[1z";

    private static StyledLine Line(string text)
    {
        var parser = new MxpParser();
        parser.Feed(text);
        return parser.Flush() ?? StyledLine.Empty;
    }

    private static SpanInteraction Clickable(StyledLine line) => line.Spans.Single(s => s.IsInteractive).Interaction!;

    [Test]
    public async Task SeveralCommandsMakeAMenuWhoseFirstIsTheClick()
    {
        var send = Clickable(Line(Secure + "<SEND \"buy sword|sell sword|look sword\">sword</SEND>"));

        await Assert.That(send.Target).IsEqualTo("buy sword");
        await Assert.That(send.Choices!.Select(c => c.Command)).IsEquivalentTo(
            new[] { "buy sword", "sell sword", "look sword" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(send.Choices!.Select(c => c.Label)).IsEquivalentTo(
            new[] { "buy sword", "sell sword", "look sword" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AHintWithOneMorePartIsTheTooltipThenTheLabels()
    {
        var send = Clickable(Line(Secure + "<SEND href=\"buy sword|sell sword\" hint=\"The sword|Buy|Sell\">sword</SEND>"));

        await Assert.That(send.Hint).IsEqualTo("The sword");
        await Assert.That(send.Choices!.Select(c => c.Label)).IsEquivalentTo(
            new[] { "Buy", "Sell" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task AHintWithAsManyPartsIsTheLabelsAlone()
    {
        var send = Clickable(Line(Secure + "<SEND href=\"n|s\" hint=\"North|South\">go</SEND>"));

        await Assert.That(send.Hint).IsNull();
        await Assert.That(send.Choices!.Select(c => c.Label)).IsEquivalentTo(
            new[] { "North", "South" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task OneCommandIsNoMenuAndKeepsItsHint()
    {
        var send = Clickable(Line(Secure + "<SEND href=\"look\" hint=\"Look|around\">here</SEND>"));

        await Assert.That(send.Choices).IsNull();
        await Assert.That(send.Hint).IsEqualTo("Look|around");
    }

    [Test]
    public async Task TextIsFilledInWithTheElementsOwnText()
    {
        var send = Clickable(Line(Secure + "<SEND \"look &text;|get &TEXT;\">a lantern</SEND>"));

        await Assert.That(send.Target).IsEqualTo("look a lantern");
        await Assert.That(send.Choices!.Last().Command).IsEqualTo("get a lantern");
    }

    [Test]
    public async Task AMenuIsCappedAtTwentyCommands()
    {
        var href = string.Join('|', Enumerable.Range(0, 30).Select(i => $"c{i}"));
        var send = Clickable(Line(Secure + $"<SEND \"{href}\">x</SEND>"));

        await Assert.That(send.Choices!.Count).IsEqualTo(MxpParser.MaxSendChoices);
    }

    [Test]
    public async Task OnAnOpenLineAMenuIsTextLikeAnyOtherSend()
    {
        var line = Line("<SEND \"a|b\">x</SEND>");

        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
        await Assert.That(line.Text).Contains("<SEND \"a|b\">");
    }
}
