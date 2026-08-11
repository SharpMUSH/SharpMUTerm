using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Text;

/// <summary>
/// The plain-text URL detector: what it finds, what it deliberately does not, and the one invariant
/// everything else rests on — it never changes a character of the line, only which spans cover them.
/// </summary>
public class UrlDetectorTests
{
    private static StyledLine Plain(string text) => StyledLine.FromText(text, TextStyle.Default);

    /// <summary>The targets of every interactive span, deduplicated in order — one entry per link.</summary>
    private static List<string> Targets(StyledLine line)
    {
        var targets = new List<string>();
        foreach (var span in line.Spans)
        {
            if (span.Interaction is { Kind: InteractionKind.Hyperlink } link &&
                (targets.Count == 0 || targets[^1] != link.Target))
            {
                targets.Add(link.Target);
            }
        }

        return targets;
    }

    /// <summary>The visible text of the characters carrying an interaction.</summary>
    private static string LinkedText(StyledLine line) =>
        string.Concat(line.Spans.Where(s => s.IsInteractive).Select(s => s.Text));

    [Test]
    public async Task APlainUrlBecomesOneLink()
    {
        var line = UrlDetector.ApplyToLine(Plain("see https://example.com/page for details"));

        await Assert.That(Targets(line)).IsEquivalentTo(new[] { "https://example.com/page" });
        await Assert.That(LinkedText(line)).IsEqualTo("https://example.com/page");
    }

    [Test]
    public async Task TheTextIsUntouchedAndOnlyTheSpanBoundariesMove()
    {
        const string text = "http://a.example/x and http://b.example/y";
        var line = UrlDetector.ApplyToLine(Plain(text));

        await Assert.That(line.Text).IsEqualTo(text);
        await Assert.That(line.Spans.Count).IsGreaterThan(1);
    }

    [Test]
    public async Task BothSchemesAreFoundAndTheCaseDoesNotMatter()
    {
        var line = UrlDetector.ApplyToLine(Plain("HTTP://a.example/x then HttpS://b.example/y"));

        await Assert.That(Targets(line)).IsEquivalentTo(new[] { "HTTP://a.example/x", "HttpS://b.example/y" });
    }

    /// <summary>
    /// The reason this works on the line rather than on each span: a server may change colour part-way
    /// through a URL, and two half-links to two truncated targets is the bug this whole type exists to
    /// avoid — the same shape as the wrap it is fixing.
    /// </summary>
    [Test]
    public async Task AUrlStraddlingAColourChangeIsOneLinkInTwoSpans()
    {
        var red = TextStyle.Default.WithForeground(TerminalColor.FromRgb(0xff, 0x00, 0x00));
        var line = UrlDetector.ApplyToLine(new StyledLine(new[]
        {
            new StyledSpan("https://example.com/", TextStyle.Default),
            new StyledSpan("deep/page", red),
        }));

        await Assert.That(Targets(line)).IsEquivalentTo(new[] { "https://example.com/deep/page" });
        await Assert.That(LinkedText(line)).IsEqualTo("https://example.com/deep/page");

        // Two spans, because the colours differ and must survive; one target, because it is one link.
        var interactive = line.Spans.Where(s => s.IsInteractive).ToList();
        await Assert.That(interactive.Count).IsEqualTo(2);
        await Assert.That(interactive[0].Style).IsNotEqualTo(interactive[1].Style);
    }

    [Test]
    public async Task ASentenceFinalStopIsNotPartOfTheUrl()
    {
        var line = UrlDetector.ApplyToLine(Plain("read https://example.com/page."));

        await Assert.That(Targets(line)).IsEquivalentTo(new[] { "https://example.com/page" });
    }

    [Test]
    public async Task AnUnbalancedCloserIsGivenBackButABalancedOneIsKept()
    {
        var wrapped = UrlDetector.ApplyToLine(Plain("(see https://example.com/map)"));
        await Assert.That(Targets(wrapped)).IsEquivalentTo(new[] { "https://example.com/map" });

        var balanced = UrlDetector.ApplyToLine(Plain("https://example.com/Foo_(disambiguation)"));
        await Assert.That(Targets(balanced)).IsEquivalentTo(new[] { "https://example.com/Foo_(disambiguation)" });
    }

    [Test]
    public async Task AQuotedUrlDoesNotSwallowTheQuote()
    {
        var line = UrlDetector.ApplyToLine(Plain("the site \"https://example.com/x\" is up"));

        await Assert.That(Targets(line)).IsEquivalentTo(new[] { "https://example.com/x" });
    }

    /// <summary>
    /// A server's own markup says what its text does. This may not replace it, and may not wrap it in a
    /// second link — including the case that matters most, a <c>&lt;SEND&gt;</c> whose command happens to
    /// contain a URL.
    /// </summary>
    [Test]
    public async Task TextTheServerAlreadyMarkedUpIsLeftAlone()
    {
        var line = UrlDetector.ApplyToLine(new StyledLine(new[]
        {
            new StyledSpan("go ", TextStyle.Default),
            new StyledSpan("https://example.com/x", TextStyle.Default, SpanInteraction.Command("look")),
        }));

        await Assert.That(Targets(line)).IsEmpty();
        await Assert.That(line.Spans.Single(s => s.IsInteractive).Interaction!.Target).IsEqualTo("look");
    }

    [Test]
    public async Task NoSchemeMeansNoLink()
    {
        var line = UrlDetector.ApplyToLine(Plain("visit www.example.com or example.com/page or me@example.com"));

        await Assert.That(Targets(line)).IsEmpty();
    }

    /// <summary>
    /// The gate is the whole point of naming two schemes rather than accepting "something:". A detector
    /// that emitted these would let a world choose which application the desktop launches.
    /// </summary>
    [Test]
    [Arguments("file:///etc/passwd")]
    [Arguments("javascript:alert(1)")]
    [Arguments("ms-msdt:/id")]
    [Arguments("mailto:someone@example.com")]
    public async Task NoOtherSchemeIsEverDetected(string target)
    {
        var line = UrlDetector.ApplyToLine(Plain($"click {target} now"));

        await Assert.That(Targets(line)).IsEmpty();
    }

    [Test]
    public async Task ASchemeWithNothingAfterItIsNotAUrl()
    {
        await Assert.That(Targets(UrlDetector.ApplyToLine(Plain("the https:// prefix")))).IsEmpty();
    }

    /// <summary>
    /// A redirector carries a second scheme inside its query. That is one link — the outer one — and the
    /// boundary rule is what stops a second, half-length link being found inside it.
    /// </summary>
    [Test]
    public async Task ASchemeInsideAUrlDoesNotStartASecondLink()
    {
        var line = UrlDetector.ApplyToLine(Plain("https://r.example/?to=https://example.com/x"));

        await Assert.That(Targets(line)).IsEquivalentTo(new[] { "https://r.example/?to=https://example.com/x" });
    }

    [Test]
    public async Task AUrlPastTheCapIsNotLinkedAtAll()
    {
        var huge = "https://example.com/" + new string('a', UrlDetector.MaxTargetLength);

        await Assert.That(Targets(UrlDetector.ApplyToLine(Plain(huge)))).IsEmpty();
    }

    [Test]
    public async Task ALineWithNoUrlIsReturnedUnchanged()
    {
        var line = Plain("nothing to see here");

        await Assert.That(UrlDetector.ApplyToLine(line)).IsSameReferenceAs(line);
    }

    [Test]
    public async Task TheHighlightRuleColourSurvivesDetection()
    {
        var rule = TerminalColor.FromRgb(0x00, 0xf5, 0xb7);
        var line = UrlDetector.ApplyToLine(new StyledLine(
            new[] { new StyledSpan("see https://example.com/x", TextStyle.Default) },
            rule));

        await Assert.That(line.RuleColor).IsEqualTo(rule);
    }

    [Test]
    public async Task FindReportsTheRangeItMatched()
    {
        var line = Plain("go https://example.com/x now");
        var match = UrlDetector.Find(line).Single();

        await Assert.That(line.Text[match.Start..match.End]).IsEqualTo(match.Url);
        await Assert.That(match.Url).IsEqualTo("https://example.com/x");
    }
}
