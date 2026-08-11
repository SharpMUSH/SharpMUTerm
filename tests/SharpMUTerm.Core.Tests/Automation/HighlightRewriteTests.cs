using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Automation;

/// <summary>
/// The reported defect: <b>highlight colours don't seem to actually work.</b> They work on their own —
/// <see cref="TriggerEngineTests.Highlight_RecoloursMatchedRegion"/> has always passed — and they were
/// destroyed by the rule's <em>own</em> rewrite. <c>TriggerEngine.Process</c> applied the highlight to
/// the matched region and then, four lines later, replaced the whole line with
/// <c>StyledLine.FromText(text, TextStyle.Default)</c>, which is a line with no colour, no attributes
/// and no left rule on it.
/// <para>
/// That combination is not exotic; it is what a channel rule looks like. Route the line to a capture
/// pane, tidy it up (<c>» $1</c>) and colour it — which is exactly the shape of the demo
/// configuration's own headline rule (<c>DemoScene</c>'s <c>public</c>: teal, bold, and
/// <c>Rewrite = "» $1"</c>). The F2 screen badges such a rule <c>H</c> and paints both swatches, so the
/// client promised a highlight it then threw away, and the only way to get one was to discover that
/// deleting the rewrite brought it back.
/// </para>
/// <para>
/// The fix is an ordering one: the rewrite runs <em>first</em>, and the highlight is then applied to
/// the whole rewritten line. It cannot be applied to the match's own offsets, because after a rewrite
/// those address a string that no longer exists — the rewritten text is the rule's product in its
/// entirety, so colouring all of it is the only reading that means anything.
/// </para>
/// </summary>
public class HighlightRewriteTests
{
    private static readonly TerminalColor Gold = TerminalColor.FromRgb(0xff, 0xd7, 0x00);

    private static StyledLine Line(string text) => StyledLine.FromText(text, TextStyle.Default);

    private static TriggerResult Run(TriggerActions actions, string pattern, string text)
    {
        var engine = new TriggerEngine();
        engine.Add(new Trigger { Pattern = pattern, Actions = actions });
        return engine.Process(Line(text));
    }

    /// <summary>The headline: a rule that rewrites and highlights does both.</summary>
    [Test]
    public async Task ARewrittenLineStillWearsItsRulesHighlight()
    {
        var result = Run(
            new TriggerActions { HighlightForeground = Gold, Rewrite = "» $1" },
            @"^\[public\] (.+)$",
            "[public] hello there");

        await Assert.That(result.Line.Text).IsEqualTo("» hello there");
        await Assert.That(result.Line.Spans.All(s => s.Style.Foreground == Gold)).IsTrue();
    }

    /// <summary>
    /// The whole rewritten line, not a fragment of it. The match's offsets described the line the
    /// rewrite replaced, so re-using them would colour an arbitrary prefix of the new text — which is
    /// the same defect wearing a different mask, and harder to spot.
    /// </summary>
    [Test]
    public async Task TheHighlightCoversTheWholeRewrittenLine()
    {
        // The rewrite is far longer than the region that matched, so a highlight still keyed to
        // match.Index/Length would leave the tail of the new text unstyled.
        var result = Run(
            new TriggerActions { HighlightBackground = Gold, Rewrite = "$1 — and a great deal more text besides" },
            @"^\[(\w+)\]",
            "[public] hello there");

        await Assert.That(result.Line.Spans.All(s => s.Style.Background == Gold)).IsTrue();
    }

    /// <summary>Attributes are part of the same promise, and were lost with the colours.</summary>
    [Test]
    public async Task ARewrittenLineKeepsTheAttributesItsRuleAdded()
    {
        var result = Run(
            new TriggerActions { AddAttributes = TextAttributes.Bold, Rewrite = "» $1" },
            @"^\[public\] (.+)$",
            "[public] hello there");

        await Assert.That(result.Line.Spans.All(s => s.Style.HasAttribute(TextAttributes.Bold))).IsTrue();
    }

    /// <summary>
    /// And the left rule, which is the marker the output pane draws to say a trigger touched this line
    /// at all. It went with the colours, so a rewritten line was indistinguishable from an untouched one.
    /// </summary>
    [Test]
    public async Task ARewrittenLineKeepsItsLeftRule()
    {
        var result = Run(
            new TriggerActions { HighlightForeground = Gold, Rewrite = "» $1" },
            @"^\[public\] (.+)$",
            "[public] hello there");

        await Assert.That(result.Line.RuleColor).IsEqualTo(Gold);
    }

    /// <summary>
    /// A rule that only rewrites still produces unstyled text. Reordering the two actions must not smuggle
    /// a style onto a line whose rule asked for none — the rewritten text is deliberately the default
    /// style, so that a rewrite is a way to <em>drop</em> a server's colour as well as to reword it.
    /// </summary>
    [Test]
    public async Task ARewriteWithNoHighlightIsStillPlain()
    {
        var result = Run(new TriggerActions { Rewrite = "» $1" }, @"^\[public\] (.+)$", "[public] hello there");

        await Assert.That(result.Line.Text).IsEqualTo("» hello there");
        await Assert.That(result.Line.RuleColor).IsNull();
        await Assert.That(result.Line.Spans.All(s => s.Style.Equals(TextStyle.Default))).IsTrue();
    }

    /// <summary>
    /// Without a rewrite nothing moves: the highlight still covers the matched region and only that. This
    /// is the property the reordering could most easily have broken, and it is the behaviour every rule
    /// that does not rewrite depends on.
    /// </summary>
    [Test]
    public async Task WithoutARewriteTheHighlightStillCoversOnlyTheMatch()
    {
        var result = Run(new TriggerActions { HighlightForeground = Gold }, "gold", "you find gold today");

        var gold = result.Line.Spans.Single(s => s.Text == "gold");
        await Assert.That(gold.Style.Foreground).IsEqualTo(Gold);
        await Assert.That(result.Line.Spans.Where(s => s.Text != "gold").All(s =>
            s.Style.Foreground == TerminalColor.Default)).IsTrue();
    }

    /// <summary>
    /// A <em>later</em> rule's rewrite still replaces an earlier rule's highlighted text, and that is
    /// correct rather than the same bug one rule over: the characters the first rule coloured are gone.
    /// Pinned so the ordering fix is not later "generalised" into carrying styles across rules, where it
    /// would be re-colouring text the first rule never saw.
    /// </summary>
    [Test]
    public async Task ALaterRulesRewriteStillReplacesAnEarlierRulesHighlight()
    {
        var engine = new TriggerEngine();
        engine.Add(new Trigger { Pattern = "gold", Actions = new TriggerActions { HighlightForeground = Gold } });
        engine.Add(new Trigger { Pattern = "^you find (.+)$", Actions = new TriggerActions { Rewrite = "found: $1" } });

        var result = engine.Process(Line("you find gold today"));

        await Assert.That(result.Line.Text).IsEqualTo("found: gold today");
        await Assert.That(result.Line.Spans.All(s => s.Style.Foreground == TerminalColor.Default)).IsTrue();
    }
}
