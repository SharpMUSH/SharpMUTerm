using SharpMUTerm.Core.Commands;

namespace SharpMUTerm.Core.Tests.Commands;

/// <summary>
/// What the composer sends: one command, line breaks written the way a MUSH stores them, and the two
/// escaping modes the window's own toggle chooses between.
/// </summary>
public class ComposeMessageTests
{
    [Test]
    public async Task LinesBecomeOneCommandJoinedByLineBreaks()
    {
        var line = ComposeMessage.Build("+bbpost 12=Title\nfirst\nsecond", ComposeEscaping.AsTyped);

        await Assert.That(line).IsEqualTo("+bbpost 12=Title%rfirst%rsecond");
    }

    /// <summary>
    /// A blank row in the middle is a paragraph break and survives as one; blank rows at the ends are
    /// where the caret was left and are not part of the post.
    /// </summary>
    [Test]
    public async Task InteriorBlankLinesSurviveAndTrailingOnesDoNot()
    {
        var line = ComposeMessage.Build("\n\nTitle\n\nbody\n\n\n", ComposeEscaping.AsTyped);

        await Assert.That(line).IsEqualTo("Title%r%rbody");
    }

    [Test]
    [Arguments("a\r\nb")]
    [Arguments("a\nb")]
    [Arguments("a\rb")]
    public async Task EveryLineEndingBreaksInTheSamePlace(string body) =>
        await Assert.That(ComposeMessage.Build(body, ComposeEscaping.AsTyped)).IsEqualTo("a%rb");

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("\n\n\n")]
    public async Task AnEmptyBufferIsNothingToSend(string? body) =>
        await Assert.That(ComposeMessage.Build(body, ComposeEscaping.AsTyped)).IsNull();

    [Test]
    public async Task AsTypedChangesNothingButTheBreaks()
    {
        const string body = "100% sure [ok] {x}; done\nand \\ too";

        await Assert.That(ComposeMessage.Build(body, ComposeEscaping.AsTyped))
            .IsEqualTo("100% sure [ok] {x}; done%rand \\ too");
    }

    [Test]
    public async Task LiteralProtectsEveryMetacharacter()
    {
        var line = ComposeMessage.Build("100% sure [ok] {x}; done", ComposeEscaping.Literal);

        await Assert.That(line).IsEqualTo("100%% sure \\[ok\\] \\{x\\}\\; done");
    }

    /// <summary>
    /// The ordering that makes literal mode work at all: the escaping runs per line, before the breaks
    /// are joined in, so the <c>%r</c> this writes is never itself escaped. The other way round produces
    /// <c>%%r</c> — the characters "%r" posted into the body instead of a line break, on every line of
    /// every literal post.
    /// </summary>
    [Test]
    public async Task LiteralEscapesTheBodyAndNotTheBreaksItWrites()
    {
        var line = ComposeMessage.Build("50% here\n50% there", ComposeEscaping.Literal);

        await Assert.That(line).IsEqualTo("50%% here%r50%% there");
        await Assert.That(line).DoesNotContain("%%r");
    }

    /// <summary>
    /// A backslash the writer typed is escaped too. Left alone it would be read as the escape itself and
    /// would eat the character after it — so <c>a\b</c> posts as <c>ab</c>, which is a character quietly
    /// missing from somebody's post.
    /// </summary>
    [Test]
    public async Task LiteralEscapesABackslashSoItDoesNotEatWhatFollowsIt()
    {
        await Assert.That(ComposeMessage.Build("a\\b", ComposeEscaping.Literal)).IsEqualTo("a\\\\b");
    }

    [Test]
    public async Task LiteralLeavesOrdinaryProseAlone()
    {
        const string body = "The caravan reached the pass at dusk.";

        await Assert.That(ComposeMessage.Build(body, ComposeEscaping.Literal)).IsEqualTo(body);
    }
}
