using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Protocols;

public class MxpParserTests
{
    /// <summary>
    /// The <c>ESC[1z</c> SECURE line tag. Every test below that exercises a *secure* element —
    /// SEND, A, BR, and the unsupported tags that are consumed rather than rendered — has to say so,
    /// because on an open line the parser now (correctly) echoes such a tag as literal text instead
    /// of honouring it. That refusal is <see cref="MxpLineModeTests"/>'s subject; these tests are
    /// about what the element does once the server has secured it.
    /// </summary>
    private const string Secure = "\x1b[1z";

    private static StyledLine ParseSingleLine(string input)
    {
        var parser = new MxpParser();
        var lines = parser.Feed(input);
        if (lines.Count != 1)
        {
            throw new InvalidOperationException($"Expected 1 line, got {lines.Count}.");
        }

        return lines[0];
    }

    [Test]
    public async Task PlainText_ProducesSingleDefaultSpan()
    {
        var line = ParseSingleLine("hello world\n");
        await Assert.That(line.Spans).HasSingleItem();
        await Assert.That(line.Spans[0].Text).IsEqualTo("hello world");
        await Assert.That(line.Spans[0].Style).IsEqualTo(TextStyle.Default);
        await Assert.That(line.Spans[0].IsInteractive).IsFalse();
    }

    [Test]
    public async Task Feed_SplitsOnNewlines()
    {
        var parser = new MxpParser();
        var lines = parser.Feed("one\ntwo\nthree\n");
        await Assert.That(lines).Count().IsEqualTo(3);
        await Assert.That(lines[0].Text).IsEqualTo("one");
        await Assert.That(lines[1].Text).IsEqualTo("two");
        await Assert.That(lines[2].Text).IsEqualTo("three");
    }

    [Test]
    public async Task Flush_ReturnsPartialPromptLine()
    {
        var parser = new MxpParser();
        var lines = parser.Feed("Enter password:");
        await Assert.That(lines).Count().IsEqualTo(0);

        var prompt = parser.Flush();
        await Assert.That(prompt).IsNotNull();
        await Assert.That(prompt!.Text).IsEqualTo("Enter password:");
    }

    [Test]
    public async Task Flush_ReturnsNullWhenNothingBuffered()
    {
        var parser = new MxpParser();
        parser.Feed("done\n");
        await Assert.That(parser.Flush()).IsNull();
    }

    [Test]
    public async Task CarriageReturn_IsDropped()
    {
        var line = ParseSingleLine("prompt\r\n");
        await Assert.That(line.Text).IsEqualTo("prompt");
    }

    [Test]
    public async Task BlankLine_IsEmittedAsEmpty()
    {
        var parser = new MxpParser();
        var lines = parser.Feed("a\n\nb\n");
        await Assert.That(lines).Count().IsEqualTo(3);
        await Assert.That(lines[1].IsEmpty).IsTrue();
    }

    [Test]
    public async Task Bold_TogglesBoldAttribute()
    {
        var line = ParseSingleLine("<B>bold</B> plain\n");
        await Assert.That(line.Spans).Count().IsEqualTo(2);
        await Assert.That(line.Spans[0].Text).IsEqualTo("bold");
        await Assert.That(line.Spans[0].Style.HasAttribute(TextAttributes.Bold)).IsTrue();
        await Assert.That(line.Spans[1].Text).IsEqualTo(" plain");
        await Assert.That(line.Spans[1].Style.HasAttribute(TextAttributes.Bold)).IsFalse();
    }

    [Test]
    public async Task BoldAlias_StrongProducesBold()
    {
        var line = ParseSingleLine("<STRONG>x</STRONG>\n");
        await Assert.That(line.Spans[0].Style.HasAttribute(TextAttributes.Bold)).IsTrue();
    }

    [Test]
    public async Task Italic_And_Underline_And_Strikeout()
    {
        var line = ParseSingleLine("<I>i</I><U>u</U><S>s</S>\n");
        await Assert.That(line.Spans[0].Style.HasAttribute(TextAttributes.Italic)).IsTrue();
        await Assert.That(line.Spans[1].Style.HasAttribute(TextAttributes.Underline)).IsTrue();
        await Assert.That(line.Spans[2].Style.HasAttribute(TextAttributes.Strikethrough)).IsTrue();
    }

    [Test]
    public async Task NestedFormatting_CombinesAttributes()
    {
        var line = ParseSingleLine("<B><I>x</I></B>\n");
        await Assert.That(line.Spans).HasSingleItem();
        await Assert.That(line.Spans[0].Style.HasAttribute(TextAttributes.Bold)).IsTrue();
        await Assert.That(line.Spans[0].Style.HasAttribute(TextAttributes.Italic)).IsTrue();
    }

    [Test]
    public async Task NestedFormatting_CloseRevertsInnerOnly()
    {
        var line = ParseSingleLine("<B>a<I>b</I>c</B>\n");
        await Assert.That(line.Spans).Count().IsEqualTo(3);
        await Assert.That(line.Spans[0].Style.HasAttribute(TextAttributes.Italic)).IsFalse();
        await Assert.That(line.Spans[1].Style.HasAttribute(TextAttributes.Italic)).IsTrue();
        await Assert.That(line.Spans[2].Style.HasAttribute(TextAttributes.Bold)).IsTrue();
        await Assert.That(line.Spans[2].Style.HasAttribute(TextAttributes.Italic)).IsFalse();
    }

    [Test]
    public async Task UnbalancedCloser_DoesNotThrow()
    {
        var line = ParseSingleLine("plain</B> more\n");
        await Assert.That(line.Text).IsEqualTo("plain more");
    }

    [Test]
    public async Task StrayOpenBoldWithoutClose_AppliesToRestOfLine()
    {
        var line = ParseSingleLine("<B>rest of line\n");
        await Assert.That(line.Spans[0].Style.HasAttribute(TextAttributes.Bold)).IsTrue();
    }

    [Test]
    public async Task Color_ForeNamed_SetsForeground()
    {
        WebColors.TryParse("red", out var red);
        var line = ParseSingleLine("<COLOR FORE=red>hot</COLOR>\n");
        await Assert.That(line.Spans[0].Text).IsEqualTo("hot");
        await Assert.That(line.Spans[0].Style.Foreground).IsEqualTo(red);
    }

    [Test]
    public async Task ColorShortForm_C_WithForeAndBack()
    {
        WebColors.TryParse("white", out var white);
        WebColors.TryParse("blue", out var blue);
        var line = ParseSingleLine("<C FORE=white BACK=blue>x</C>\n");
        await Assert.That(line.Spans[0].Style.Foreground).IsEqualTo(white);
        await Assert.That(line.Spans[0].Style.Background).IsEqualTo(blue);
    }

    [Test]
    public async Task ColorPositional_FirstIsForeground()
    {
        WebColors.TryParse("green", out var green);
        var line = ParseSingleLine("<COLOR green>go</COLOR>\n");
        await Assert.That(line.Spans[0].Style.Foreground).IsEqualTo(green);
    }

    [Test]
    public async Task ColorClose_RevertsForeground()
    {
        var line = ParseSingleLine("<COLOR FORE=red>red</COLOR>back\n");
        await Assert.That(line.Spans).Count().IsEqualTo(2);
        await Assert.That(line.Spans[1].Text).IsEqualTo("back");
        await Assert.That(line.Spans[1].Style.Foreground).IsEqualTo(TerminalColor.Default);
    }

    [Test]
    public async Task Font_ColorAttribute_SetsForegroundHex()
    {
        WebColors.TryParse("#00ff00", out var lime);
        var line = ParseSingleLine("<FONT COLOR=#00ff00>x</FONT>\n");
        await Assert.That(line.Spans[0].Style.Foreground).IsEqualTo(lime);
    }

    [Test]
    public async Task Color_UnknownName_IsIgnored()
    {
        var line = ParseSingleLine("<COLOR FORE=notacolour>x</COLOR>\n");
        await Assert.That(line.Spans[0].Style.Foreground).IsEqualTo(TerminalColor.Default);
    }

    [Test]
    public async Task Send_WithHref_ProducesSendCommand()
    {
        var line = ParseSingleLine(Secure + "<SEND HREF=\"look\">here</SEND>\n");
        await Assert.That(line.Spans).HasSingleItem();
        var span = line.Spans[0];
        await Assert.That(span.Text).IsEqualTo("here");
        await Assert.That(span.IsInteractive).IsTrue();
        await Assert.That(span.Interaction!.Kind).IsEqualTo(InteractionKind.SendCommand);
        await Assert.That(span.Interaction!.Target).IsEqualTo("look");
    }

    [Test]
    public async Task Send_WithoutHref_UsesEnclosedTextAsCommand()
    {
        var line = ParseSingleLine(Secure + "<SEND>north</SEND>\n");
        var span = line.Spans[0];
        await Assert.That(span.Text).IsEqualTo("north");
        await Assert.That(span.Interaction!.Kind).IsEqualTo(InteractionKind.SendCommand);
        await Assert.That(span.Interaction!.Target).IsEqualTo("north");
    }

    [Test]
    public async Task Send_CapturesHint()
    {
        var line = ParseSingleLine(Secure + "<SEND HREF=\"look\" HINT=\"examine\">x</SEND>\n");
        await Assert.That(line.Spans[0].Interaction!.Hint).IsEqualTo("examine");
    }

    [Test]
    public async Task Send_MultiCommandHref_UsesFirstAsPrimary()
    {
        var line = ParseSingleLine(Secure + "<SEND HREF=\"a|b|c\">x</SEND>\n");
        await Assert.That(line.Spans[0].Interaction!.Target).IsEqualTo("a");
    }

    [Test]
    public async Task Send_PromptFlag_SetsPromptOnly()
    {
        var line = ParseSingleLine(Secure + "<SEND HREF=\"cast spell\" PROMPT>x</SEND>\n");
        await Assert.That(line.Spans[0].Interaction!.PromptOnly).IsTrue();
    }

    [Test]
    public async Task Send_BareWithoutClose_ClosesAtEndOfLine()
    {
        var parser = new MxpParser();
        // Only the first line is secured: the mode reverts at the newline, which is exactly the
        // boundary the interaction must not survive.
        var lines = parser.Feed(Secure + "<SEND HREF=\"go\">walk\nnext line\n");
        await Assert.That(lines).Count().IsEqualTo(2);
        await Assert.That(lines[0].Spans[0].Interaction!.Target).IsEqualTo("go");
        await Assert.That(lines[1].Spans[0].IsInteractive).IsFalse();
    }

    [Test]
    public async Task Anchor_WithHref_ProducesHyperlink()
    {
        var line = ParseSingleLine(Secure + "<A HREF=\"https://example.com\">site</A>\n");
        var span = line.Spans[0];
        await Assert.That(span.Text).IsEqualTo("site");
        await Assert.That(span.Interaction!.Kind).IsEqualTo(InteractionKind.Hyperlink);
        await Assert.That(span.Interaction!.Target).IsEqualTo("https://example.com");
    }

    [Test]
    public async Task Entities_AreDecoded()
    {
        var line = ParseSingleLine("&lt;&gt;&amp;&nbsp;&#65;\n");
        await Assert.That(line.Text).IsEqualTo("<>& A");
    }

    [Test]
    public async Task Entities_QuoteAndApos()
    {
        var line = ParseSingleLine("&quot;&apos;\n");
        await Assert.That(line.Text).IsEqualTo("\"'");
    }

    [Test]
    public async Task Entity_HexNumeric_IsDecoded()
    {
        var line = ParseSingleLine("&#x41;&#x42;\n");
        await Assert.That(line.Text).IsEqualTo("AB");
    }

    [Test]
    public async Task Entity_Unknown_IsEmittedLiterally()
    {
        var line = ParseSingleLine("&unknown;\n");
        await Assert.That(line.Text).IsEqualTo("&unknown;");
    }

    [Test]
    public async Task StrayAmpersand_IsEmittedLiterally()
    {
        var line = ParseSingleLine("Tom & Jerry\n");
        await Assert.That(line.Text).IsEqualTo("Tom & Jerry");
    }

    [Test]
    public async Task UnknownTag_IsConsumedNotLeaked()
    {
        var line = ParseSingleLine(Secure + "a<VAR NAME=hp>b\n");
        await Assert.That(line.Text).IsEqualTo("ab");
    }

    [Test]
    public async Task UnsupportedTags_AreStripped()
    {
        var line = ParseSingleLine(Secure + "<H1>Title</H1><P>text<IMG SRC=\"x.png\">end\n");
        await Assert.That(line.Text).IsEqualTo("Titletextend");
    }

    [Test]
    public async Task Br_ProducesLineBreak()
    {
        var parser = new MxpParser();
        // BR is not one of the spec's open tags, so it needs securing like any other.
        var lines = parser.Feed(Secure + "first<BR>second\n");
        await Assert.That(lines).Count().IsEqualTo(2);
        await Assert.That(lines[0].Text).IsEqualTo("first");
        await Assert.That(lines[1].Text).IsEqualTo("second");
    }

    [Test]
    public async Task StrayLessThan_IsEmittedLiterally()
    {
        var line = ParseSingleLine("5 < 3 is false\n");
        await Assert.That(line.Text).IsEqualTo("5 < 3 is false");
    }

    [Test]
    public async Task TagSplitAcrossFeeds_IsReassembled()
    {
        var parser = new MxpParser();
        var first = parser.Feed("<COL");
        await Assert.That(first).Count().IsEqualTo(0);
        var second = parser.Feed("OR FORE=red>hot</COLOR>\n");
        WebColors.TryParse("red", out var red);
        await Assert.That(second).HasSingleItem();
        await Assert.That(second[0].Spans[0].Text).IsEqualTo("hot");
        await Assert.That(second[0].Spans[0].Style.Foreground).IsEqualTo(red);
    }

    [Test]
    public async Task EntitySplitAcrossFeeds_IsReassembled()
    {
        var parser = new MxpParser();
        parser.Feed("x&a");
        var lines = parser.Feed("mp;y\n");
        await Assert.That(lines[0].Text).IsEqualTo("x&y");
    }

    [Test]
    public async Task StyleState_PersistsAcrossFeeds()
    {
        var parser = new MxpParser();
        parser.Feed("<B>bold ");
        await Assert.That(parser.CurrentStyle.HasAttribute(TextAttributes.Bold)).IsTrue();
        var lines = parser.Feed("still bold</B>\n");
        await Assert.That(lines[0].Spans[0].Style.HasAttribute(TextAttributes.Bold)).IsTrue();
    }

    [Test]
    public async Task SelfClosingBr_IsHandled()
    {
        var parser = new MxpParser();
        var lines = parser.Feed(Secure + "a<BR/>b\n");
        await Assert.That(lines).Count().IsEqualTo(2);
        await Assert.That(lines[0].Text).IsEqualTo("a");
    }

    /// <summary>
    /// Was <c>EscapeByte_IsPassedThrough</c>, asserting that <c>"a\x1bb\n"</c> produced the literal
    /// text <c>"a\x1bb"</c> — i.e. that an ESC byte, and whatever followed it, fell straight into the
    /// span text unparsed. Two things were wrong with it. That pass-through was the bug this task
    /// fixes: an MXP world's ANSI (or any other escape) rendered as garbage instead of being decoded
    /// or discarded. And separately, C#'s <c>\x</c> escape is greedy over up to four hex digits, so
    /// <c>"\x1bb"</c> was never ESC followed by <c>'b'</c> at all — it compiled to the single
    /// character U+01BB, which the old test's "pass everything through" assertion could not have told
    /// apart from a real ESC anyway. <c>\u001b</c> is exactly four digits and does not swallow what
    /// follows it, which is used below and is now this file's ESC spelling. Fixed, "\u001bb" reads as
    /// the two-byte escape "ESC b", which AnsiParser also consumes and ignores, so only "a" survives.
    /// </summary>
    [Test]
    public async Task EscapeByte_TwoByteEscapeIsConsumedAndDiscarded()
    {
        var line = ParseSingleLine("a\u001bb\n");
        await Assert.That(line.Text).IsEqualTo("a");
    }

    /// <summary>
    /// The spec permits ANSI inside MXP — "ANSI and VT100 codes can still be used as normal" — and
    /// nothing upstream of this parser strips it: WorldSession picks MxpParser *or* AnsiParser, never
    /// both. Before this, an SGR sequence was appended to the line as literal text and its colour was
    /// lost, so an MXP world rendered "<ESC>[0;33mYellow" in place of yellow text.
    /// </summary>
    [Test]
    public async Task Ansi_SgrSetsTheStyleAndLeavesNoEscapeInTheText()
    {
        var parser = new MxpParser();

        var line = parser.Feed("\x1b[33mYellow\x1b[0m plain\n")[0];

        await Assert.That(line.Text).IsEqualTo("Yellow plain");
        await Assert.That(line.Spans[0].Style.Foreground).IsEqualTo(TerminalColor.FromIndex(3));
        await Assert.That(line.Spans[^1].Style.Foreground).IsEqualTo(TerminalColor.Default);
    }

    /// <summary>A non-SGR CSI is consumed and discarded, exactly as AnsiParser does with it.</summary>
    [Test]
    public async Task Ansi_NonSgrCsiIsDiscarded()
    {
        var parser = new MxpParser();

        var line = parser.Feed("a\x1b[2Kb\n")[0];

        await Assert.That(line.Text).IsEqualTo("ab");
    }

    /// <summary>ANSI and MXP compose: the tag applies on top of the SGR colour.</summary>
    [Test]
    public async Task Ansi_AndMxpTagsCompose()
    {
        var parser = new MxpParser();

        var line = parser.Feed("\x1b[33m<B>both</B>\n")[0];

        await Assert.That(line.Spans[0].Style.Foreground).IsEqualTo(TerminalColor.FromIndex(3));
        await Assert.That(line.Spans[0].Style.Attributes.HasFlag(TextAttributes.Bold)).IsTrue();
    }

    [Test]
    public async Task Send_WithFormattingInside_KeepsInteractionOnAllSpans()
    {
        var line = ParseSingleLine(Secure + "<SEND HREF=\"go\"><B>walk</B> now</SEND>\n");
        await Assert.That(line.Spans).Count().IsEqualTo(2);
        await Assert.That(line.Spans[0].Interaction!.Target).IsEqualTo("go");
        await Assert.That(line.Spans[0].Style.HasAttribute(TextAttributes.Bold)).IsTrue();
        await Assert.That(line.Spans[1].Interaction!.Target).IsEqualTo("go");
    }

    [Test]
    public async Task Reset_ClearsStyleAndStack()
    {
        var parser = new MxpParser();
        parser.Feed("<B>bold");
        parser.Reset();
        await Assert.That(parser.CurrentStyle).IsEqualTo(TextStyle.Default);
        await Assert.That(parser.HasPendingContent).IsFalse();
        var lines = parser.Feed("plain\n");
        await Assert.That(lines[0].Spans[0].Style.HasAttribute(TextAttributes.Bold)).IsFalse();
    }

    [Test]
    public async Task HasPendingContent_TrueWithBufferedText()
    {
        var parser = new MxpParser();
        parser.Feed("partial");
        await Assert.That(parser.HasPendingContent).IsTrue();
    }

    /// <summary>
    /// OSC (ESC ]) sets a terminal property (window title, most commonly) and is terminated by
    /// either BEL or ST (ESC backslash). Before this fix, only CSI (ESC [) was recognised; an OSC
    /// payload fell through the generic two-byte-escape branch a character at a time and leaked
    /// into the line as literal text. Mirrors AnsiParser.ProcessOsc's BEL arm exactly.
    /// </summary>
    [Test]
    public async Task Osc_BelTerminatedSequenceIsDiscarded()
    {
        var line = ParseSingleLine("a\x1b]0;title\u0007b\n");
        await Assert.That(line.Text).IsEqualTo("ab");
    }

    /// <summary>The ST form of an OSC terminator (ESC backslash) rather than BEL.</summary>
    [Test]
    public async Task Osc_StTerminatedSequenceIsDiscarded()
    {
        var line = ParseSingleLine("a\x1b]0;title\x1b\\b\n");
        await Assert.That(line.Text).IsEqualTo("ab");
    }

    /// <summary>
    /// A two-byte-intermediate escape (ESC ( B, "select G0 as US-ASCII") is a three-byte unit:
    /// ESC, the intermediate, then a trailing byte that ends it. Before this fix the intermediate
    /// byte sent the parser straight back to Text, leaving the trailing byte to print as if it
    /// were ordinary output.
    /// </summary>
    [Test]
    public async Task Escape_ThreeByteIntermediateIsConsumedAndDiscarded()
    {
        var line = ParseSingleLine("a\x1b(Bb\n");
        await Assert.That(line.Text).IsEqualTo("ab");
    }
}
