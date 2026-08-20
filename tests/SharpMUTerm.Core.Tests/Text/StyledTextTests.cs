using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Text;

public class StyledTextTests
{
    [Test]
    public async Task Restyle_RecoloursSubrange_AndSplitsSpans()
    {
        var line = StyledLine.FromText("hello world", TextStyle.Default);
        var restyled = StyledText.Restyle(line, 6, 5, s => s.WithForeground(TerminalColor.FromIndex(2)));

        await Assert.That(restyled.Text).IsEqualTo("hello world");
        var worldSpan = restyled.Spans.First(s => s.Text.Contains("world"));
        await Assert.That(worldSpan.Style.Foreground).IsEqualTo(TerminalColor.FromIndex(2));
        var helloSpan = restyled.Spans.First(s => s.Text.Contains("hello"));
        await Assert.That(helloSpan.Style.Foreground).IsEqualTo(TerminalColor.Default);
    }

    [Test]
    public async Task Restyle_ClampsRangePastEnd()
    {
        var line = StyledLine.FromText("abc", TextStyle.Default);
        var restyled = StyledText.Restyle(line, 1, 100, s => s.AddAttribute(TextAttributes.Bold));
        await Assert.That(restyled.Text).IsEqualTo("abc");
        await Assert.That(restyled.Spans.Last().Style.HasAttribute(TextAttributes.Bold)).IsTrue();
    }

    [Test]
    public async Task Restyle_EmptyRange_ReturnsSameText()
    {
        var line = StyledLine.FromText("abc", TextStyle.Default);
        var restyled = StyledText.Restyle(line, 1, 0, s => s.AddAttribute(TextAttributes.Bold));
        await Assert.That(restyled.Text).IsEqualTo("abc");
    }

    [Test]
    public async Task Coalesce_MergesAdjacentEqualStyles()
    {
        var styles = new[] { TextStyle.Default, TextStyle.Default, TextStyle.Default };
        var line = StyledText.Coalesce("abc", styles);
        await Assert.That(line.Spans).HasSingleItem();
    }

    [Test]
    public async Task StripColour_ResetsBothColoursAndMergesTheSpansThatBecameEqual()
    {
        var line = new StyledLine(new[]
        {
            new StyledSpan("red", new TextStyle(TerminalColor.FromIndex(1), TerminalColor.Default, TextAttributes.None)),
            new StyledSpan("blue", new TextStyle(TerminalColor.FromIndex(4), TerminalColor.FromIndex(7), TextAttributes.None)),
        });

        var stripped = StyledText.StripColour(line);

        await Assert.That(stripped.Text).IsEqualTo("redblue");
        await Assert.That(stripped.Spans).HasSingleItem();
        await Assert.That(stripped.Spans[0].Style.Foreground).IsEqualTo(TerminalColor.Default);
        await Assert.That(stripped.Spans[0].Style.Background).IsEqualTo(TerminalColor.Default);
    }

    /// <summary>
    /// Attributes and interactions survive: bold is how a server marks emphasis once its palette is
    /// gone, and a stripped MXP link is still a link.
    /// </summary>
    [Test]
    public async Task StripColour_KeepsAttributesAndInteractions()
    {
        var style = new TextStyle(TerminalColor.FromIndex(2), TerminalColor.Default, TextAttributes.Bold);
        var line = new StyledLine(new[]
        {
            new StyledSpan("north", style, SpanInteraction.Command("north")),
        });

        var stripped = StyledText.StripColour(line);

        await Assert.That(stripped.Spans[0].Style.HasAttribute(TextAttributes.Bold)).IsTrue();
        await Assert.That(stripped.Spans[0].Interaction!.Target).IsEqualTo("north");
    }

    /// <summary>Two spans that differ only by their link stay two spans, or the link would spread.</summary>
    [Test]
    public async Task StripColour_DoesNotMergeAcrossDifferentInteractions()
    {
        var style = new TextStyle(TerminalColor.FromIndex(2), TerminalColor.Default, TextAttributes.None);
        var line = new StyledLine(new[]
        {
            new StyledSpan("north", style, SpanInteraction.Command("north")),
            new StyledSpan(" east", style, SpanInteraction.Command("east")),
        });

        var stripped = StyledText.StripColour(line);

        await Assert.That(stripped.Spans.Count).IsEqualTo(2);
    }

    [Test]
    public async Task StripColour_LeavesAnUncolouredLineExactlyAsItWas()
    {
        var line = StyledLine.FromText("plain", TextStyle.Default);

        await Assert.That(StyledText.StripColour(line)).IsSameReferenceAs(line);
    }

    /// <summary>
    /// The trigger left-rule is this client's mark, not the server's colour, so it rides through.
    /// </summary>
    [Test]
    public async Task StripColour_KeepsTheRuleColour()
    {
        var line = new StyledLine(
            new[] { new StyledSpan("hit", new TextStyle(TerminalColor.FromIndex(1), TerminalColor.Default, TextAttributes.None)) },
            TerminalColor.FromIndex(14));

        await Assert.That(StyledText.StripColour(line).RuleColor).IsEqualTo(TerminalColor.FromIndex(14));
    }

    [Test]
    public async Task Truncate_LeavesAShortLineAlone()
    {
        var line = StyledLine.FromText("short", TextStyle.Default);

        await Assert.That(StyledText.Truncate(line, 80)).IsSameReferenceAs(line);
    }

    /// <summary>The ellipsis is inside the budget, not beside it — the caller asked for a width.</summary>
    [Test]
    public async Task Truncate_CountsTheEllipsisWithinTheBudget()
    {
        var line = StyledLine.FromText(new string('x', 50), TextStyle.Default);

        await Assert.That(StyledText.Truncate(line, 10).Text).IsEqualTo("xxxxxxxxx…");
    }

    [Test]
    public async Task Truncate_WithNoEllipsisCutsHard()
    {
        var line = StyledLine.FromText("abcdef", TextStyle.Default);

        await Assert.That(StyledText.Truncate(line, 3, string.Empty).Text).IsEqualTo("abc");
    }

    [Test]
    public async Task Truncate_ToNothingIsEmpty()
    {
        var line = StyledLine.FromText("abcdef", TextStyle.Default);

        await Assert.That(StyledText.Truncate(line, 0).IsEmpty).IsTrue();
    }

    /// <summary>
    /// Cutting the styled line rather than the markup is the point: every surviving span keeps its
    /// own style, and the ellipsis takes the style of the span it ends in rather than painting a
    /// stray default-coloured character onto the end of a coloured run.
    /// </summary>
    [Test]
    public async Task Truncate_KeepsPerSpanStyleAndStylesTheEllipsis()
    {
        var red = new TextStyle(TerminalColor.FromIndex(1), TerminalColor.Default, TextAttributes.None);
        var blue = new TextStyle(TerminalColor.FromIndex(4), TerminalColor.Default, TextAttributes.None);
        var line = new StyledLine(new[] { new StyledSpan("aaa", red), new StyledSpan("bbbbbb", blue) });

        var cut = StyledText.Truncate(line, 6);

        await Assert.That(cut.Text).IsEqualTo("aaabb…");
        await Assert.That(cut.Spans[0].Style).IsEqualTo(red);
        await Assert.That(cut.Spans[^1].Style).IsEqualTo(blue);
    }

    /// <summary>Half of a link is not a link: a clickable ellipsis would send a truncated target.</summary>
    [Test]
    public async Task Truncate_DoesNotMakeTheEllipsisClickable()
    {
        var line = new StyledLine(new[]
        {
            new StyledSpan("click me please", TextStyle.Default, new SpanInteraction(InteractionKind.SendCommand, "look")),
        });

        var cut = StyledText.Truncate(line, 6);

        await Assert.That(cut.Spans[^1].Interaction).IsNull();
    }

    [Test]
    public async Task Truncate_KeepsTheRuleColour()
    {
        var line = new StyledLine(
            new[] { new StyledSpan("a long highlighted line", TextStyle.Default) },
            TerminalColor.FromIndex(14));

        await Assert.That(StyledText.Truncate(line, 5).RuleColor).IsEqualTo(TerminalColor.FromIndex(14));
    }
}
