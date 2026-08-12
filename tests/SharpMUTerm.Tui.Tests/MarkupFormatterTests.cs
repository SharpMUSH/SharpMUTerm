using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

public class MarkupFormatterTests
{
    private static readonly MarkupFormatter Formatter = new(ThemeLibrary.Dark());

    [Test]
    public async Task PlainText_IsWrappedInAResolvedForegroundColour()
    {
        var line = StyledLine.FromText("hello", TextStyle.Default);

        var markup = Formatter.ToMarkup(line);

        // Default resolves to the theme foreground, so text is coloured but carries no background.
        await Assert.That(markup).Contains("hello");
        await Assert.That(markup).StartsWith("[#");
        await Assert.That(markup).EndsWith("[/]");
        await Assert.That(markup).DoesNotContain(" on #");
    }

    [Test]
    public async Task RuleColor_PrependsLeftRuleGlyph()
    {
        var line = new StyledLine(
            new[] { new StyledSpan("channel", TextStyle.Default) },
            TerminalColor.FromRgb(0x00, 0xf5, 0xb7));

        var markup = Formatter.ToMarkup(line);

        // A 2-col left rule in the trigger colour precedes the content.
        await Assert.That(markup).StartsWith("[#00f5b7]▌[/] ");
        await Assert.That(markup).Contains("channel");
    }

    [Test]
    public async Task Timestamp_PrependsADimGutterAheadOfContent()
    {
        var line = StyledLine.FromText("hello", TextStyle.Default);

        var markup = Formatter.ToMarkup(line, "09:24");

        await Assert.That(markup).StartsWith("[dim]09:24[/] ");
        await Assert.That(markup).Contains("hello");
    }

    [Test]
    public async Task Timestamp_PrecedesTheTriggerLeftRule()
    {
        var line = new StyledLine(
            new[] { new StyledSpan("channel", TextStyle.Default) },
            TerminalColor.FromRgb(0x00, 0xf5, 0xb7));

        var markup = Formatter.ToMarkup(line, "09:24");

        // Timestamp gutter first, then the coloured left rule.
        await Assert.That(markup).StartsWith("[dim]09:24[/] [#00f5b7]▌[/] ");
    }

    [Test]
    public async Task NullOrEmptyTimestamp_AddsNoGutter()
    {
        var line = StyledLine.FromText("hello", TextStyle.Default);

        await Assert.That(Formatter.ToMarkup(line, null)).StartsWith("[#");
        await Assert.That(Formatter.ToMarkup(line, "")).StartsWith("[#");
    }

    [Test]
    public async Task BoldItalic_EmitsAttributeTokens()
    {
        // Gold rather than red: pure #ff0000 measures 2.998:1 against this theme's reading plane, a
        // hair under the floor, so it comes out lifted by a single step — correct, and a distraction in
        // a test about attribute tokens. TheLegibilityFloor* tests below are where that is the subject.
        var style = new TextStyle(
            TerminalColor.FromRgb(0xff, 0xd7, 0x00),
            TerminalColor.Default,
            TextAttributes.Bold | TextAttributes.Italic);
        var line = StyledLine.FromText("x", style);

        var markup = Formatter.ToMarkup(line);

        await Assert.That(markup).Contains("bold");
        await Assert.That(markup).Contains("italic");
        await Assert.That(markup).Contains("#ffd700");
    }

    [Test]
    public async Task TheLegibilityFloorLiftsAColourThatCannotBeReadOnThePane()
    {
        // ANSI 4 on the default dark theme is #000080 against a #36363d focused pane: 1.34:1, which is
        // most of what "unreadable colours against our backgrounds" was about. MU* servers send it
        // constantly, because they are written for black terminals and this one is not black.
        var line = StyledLine.FromText("x", new TextStyle(
            TerminalColor.FromIndex(4), TerminalColor.Default, TextAttributes.None));

        var markup = new MarkupFormatter(ThemeLibrary.Dark()).ToMarkup(line);

        await Assert.That(markup).DoesNotContain("#000080");
        await Assert.That(Contrast.Ratio(
                Parse(markup), WorkspacePalette.ReadingPlane(ThemeLibrary.Dark())))
            .IsGreaterThanOrEqualTo(Contrast.Floor);
    }

    [Test]
    public async Task TheLegibilityFloorLeavesAColourThatAlreadyReadsAlone()
    {
        // Byte-identical, not merely close: the floor must not restyle text that was already fine, which
        // is most of what any game sends.
        var line = StyledLine.FromText("x", new TextStyle(
            TerminalColor.FromRgb(0xff, 0xd7, 0x00), TerminalColor.Default, TextAttributes.None));

        await Assert.That(new MarkupFormatter(ThemeLibrary.Dark()).ToMarkup(line)).Contains("#ffd700");
    }

    [Test]
    public async Task AHighlightIsMeasuredAgainstItsOwnBackgroundAndNotThePane()
    {
        // A span carrying a background is painted on *that*, so the pane it happens to be in says
        // nothing about whether it can be read. Dark blue on white is 14.3:1 and must survive untouched;
        // measured against the pane instead it would be lifted to something unreadable on its own band.
        var line = StyledLine.FromText("x", new TextStyle(
            TerminalColor.FromRgb(0x00, 0x00, 0x80),
            TerminalColor.FromRgb(0xff, 0xff, 0xff),
            TextAttributes.None));

        var markup = new MarkupFormatter(ThemeLibrary.Dark()).ToMarkup(line);

        await Assert.That(markup).Contains("#000080 on #ffffff");
    }

    [Test]
    public async Task TheFloorCanBeSwitchedOffAndThenTheBytesAreExactlyWhatTheyWere()
    {
        var line = StyledLine.FromText("x", new TextStyle(
            TerminalColor.FromIndex(4), TerminalColor.Default, TextAttributes.None));

        var markup = new MarkupFormatter(ThemeLibrary.Dark(), new TextSettings { KeepTextLegible = false })
            .ToMarkup(line);

        await Assert.That(markup).Contains("#000080");
    }

    /// <summary>The first <c>#rrggbb</c> in a markup string, as a colour.</summary>
    private static Rgb Parse(string markup)
    {
        var at = markup.IndexOf('#', StringComparison.Ordinal);
        var hex = markup.Substring(at + 1, 6);
        return new Rgb(
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..], 16));
    }

    [Test]
    public async Task Background_IsEmittedOnlyWhenSet()
    {
        var withBg = new TextStyle(TerminalColor.Default, TerminalColor.FromRgb(16, 32, 48), TextAttributes.None);
        var markup = Formatter.ToMarkup(StyledLine.FromText("x", withBg));

        await Assert.That(markup).Contains("on #102030");
    }

    [Test]
    public async Task SendCommandInteraction_BecomesAnEscapedSendLink()
    {
        var span = new StyledSpan("go north", TextStyle.Default, SpanInteraction.Command("go north"));
        var line = new StyledLine(new[] { span });

        var markup = Formatter.ToMarkup(line);

        await Assert.That(markup).Contains("[link=mux:send:go%20north]");
        await Assert.That(markup).EndsWith("[/][/]"); // closes style then link
    }

    [Test]
    public async Task PromptOnlyInteraction_UsesThePromptScheme()
    {
        var span = new StyledSpan("look", TextStyle.Default, SpanInteraction.Command("look", promptOnly: true));
        var markup = Formatter.ToMarkup(new StyledLine(new[] { span }));

        await Assert.That(markup).Contains("[link=mux:prompt:look]");
    }

    /// <summary>
    /// A hyperlink gets a scheme of its own, like the other two kinds. It used to be emitted as the raw
    /// <c>href</c>, which is what let a world write <c>&lt;A HREF="mux:send:@shutdown"&gt;</c> and have the
    /// click handler send it — see <c>LinkSchemeSecurityTests</c> and <see cref="LinkPayload"/>.
    /// </summary>
    [Test]
    public async Task Hyperlink_UsesTheWebScheme()
    {
        var span = new StyledSpan("site", TextStyle.Default, SpanInteraction.Link("https://example.org"));
        var markup = Formatter.ToMarkup(new StyledLine(new[] { span }));

        await Assert.That(markup).Contains("[link=mux:web:https://example.org]");
    }

    [Test]
    public async Task LiteralBrackets_AreEscaped()
    {
        var markup = Formatter.ToMarkup(StyledLine.FromText("[chat] hi", TextStyle.Default));

        await Assert.That(markup).Contains("[[chat]] hi");
    }

    [Test]
    public async Task EmptyLine_ProducesEmptyMarkup()
    {
        await Assert.That(Formatter.ToMarkup(StyledLine.Empty)).IsEqualTo(string.Empty);
    }

    // ---- F7 preferences that are decisions about markup ----

    private static StyledLine Blinking() => new(new[]
    {
        new StyledSpan("alert", new TextStyle(TerminalColor.Default, TerminalColor.Default, TextAttributes.Blink)),
    });

    /// <summary>
    /// SGR 5 is parsed but dropped by default: a blinking line is the one rendition a server can
    /// impose that the reader cannot stop looking at. F7's <c>allow blink</c> is what lets it through.
    /// </summary>
    [Test]
    public async Task AllowBlink_Off_DropsTheBlinkAttribute()
    {
        var formatter = new MarkupFormatter(ThemeLibrary.Dark(), new TextSettings { AllowBlink = false });

        await Assert.That(formatter.ToMarkup(Blinking())).DoesNotContain("blink");
    }

    [Test]
    public async Task AllowBlink_On_EmitsTheBlinkToken()
    {
        var formatter = new MarkupFormatter(ThemeLibrary.Dark(), new TextSettings { AllowBlink = true });

        await Assert.That(formatter.ToMarkup(Blinking())).Contains("blink");
    }

    /// <summary>The setting is read per span, so flipping it changes the very next line rendered.</summary>
    [Test]
    public async Task AllowBlink_FlippingIt_ChangesTheNextLine()
    {
        var text = new TextSettings { AllowBlink = false };
        var formatter = new MarkupFormatter(ThemeLibrary.Dark(), text);

        var before = formatter.ToMarkup(Blinking());
        text.AllowBlink = true;
        var after = formatter.ToMarkup(Blinking());

        await Assert.That(before).DoesNotContain("blink");
        await Assert.That(after).Contains("blink");
    }

    private static StyledLine LinkLine() => new(new[]
    {
        new StyledSpan("site", TextStyle.Default, SpanInteraction.Link("https://example.org")),
    });

    [Test]
    public async Task UnderlineHyperlinks_On_UnderlinesAClickableSpan()
    {
        var formatter = new MarkupFormatter(ThemeLibrary.Dark(), new TextSettings { UnderlineHyperlinks = true });

        await Assert.That(formatter.ToMarkup(LinkLine())).Contains("underline");
    }

    [Test]
    public async Task UnderlineHyperlinks_Off_LeavesAnUnstyledLinkUnstyled()
    {
        var formatter = new MarkupFormatter(ThemeLibrary.Dark(), new TextSettings { UnderlineHyperlinks = false });
        var markup = formatter.ToMarkup(LinkLine());

        await Assert.That(markup).DoesNotContain("underline");
        await Assert.That(markup).Contains("[link=mux:web:https://example.org]"); // still clickable, just unstyled
    }

    /// <summary>It underlines links, not everything — plain text is untouched either way.</summary>
    [Test]
    public async Task UnderlineHyperlinks_DoesNotTouchPlainText()
    {
        var formatter = new MarkupFormatter(ThemeLibrary.Dark(), new TextSettings { UnderlineHyperlinks = true });

        await Assert.That(formatter.ToMarkup(StyledLine.FromText("hello", TextStyle.Default)))
            .DoesNotContain("underline");
    }

    /// <summary>
    /// A link the server already underlined gets one token, not two: the preference is folded into the
    /// span's own attributes rather than emitted alongside them.
    /// </summary>
    [Test]
    public async Task UnderlineHyperlinks_OnAnAlreadyUnderlinedLink_EmitsOneToken()
    {
        var formatter = new MarkupFormatter(ThemeLibrary.Dark(), new TextSettings { UnderlineHyperlinks = true });
        var line = new StyledLine(new[]
        {
            new StyledSpan(
                "site",
                new TextStyle(TerminalColor.Default, TerminalColor.Default, TextAttributes.Underline),
                SpanInteraction.Link("https://example.org")),
        });

        var markup = formatter.ToMarkup(line);

        await Assert.That(markup.Split("underline").Length - 1).IsEqualTo(1);
    }
}
