using SharpMUTerm.Core.Protocols;

namespace SharpMUTerm.Core.Tests.Protocols;

/// <summary>
/// The open/secure classification on its own. This table <em>is</em> the security boundary — every
/// tag the parser honours on an unsecured line is a tag this returned true for — and the parser only
/// ever drives eight of its entries, because <see cref="MxpParser.Canonical"/> folds the alternative
/// spellings away before the gate sees them and nothing implements <c>H</c> or <c>FONT</c>'s
/// behaviour. So <c>STRONG</c>, <c>EM</c>, <c>STRIKEOUT</c>, <c>HIGH</c> and mixed case reach it from
/// nowhere else, and a typo in any of them would turn an open tag secure with nothing to notice.
/// </summary>
public class MxpTagCategoryTests
{
    /// <summary>
    /// The allow-list, spelt out a second time rather than read back off the type — a test that asked
    /// the table what was in it would agree with any edit to the table, including a deletion.
    /// Verbatim from the spec: "Only the tags described in this section are OPEN tags."
    /// </summary>
    [Test]
    [Arguments("B")]
    [Arguments("BOLD")]
    [Arguments("STRONG")]
    [Arguments("I")]
    [Arguments("ITALIC")]
    [Arguments("EM")]
    [Arguments("U")]
    [Arguments("UNDERLINE")]
    [Arguments("S")]
    [Arguments("STRIKEOUT")]
    [Arguments("C")]
    [Arguments("COLOR")]
    [Arguments("H")]
    [Arguments("HIGH")]
    [Arguments("FONT")]
    public async Task EveryOpenTagIsOpen(string name) =>
        await Assert.That(MxpTagCategory.IsOpen(name)).IsTrue();

    /// <summary>
    /// A server may spell a tag however it likes, so the classification is case-insensitive. The
    /// parser upper-cases before the gate, so this is the only place the lower and mixed forms are
    /// ever asked for.
    /// </summary>
    [Test]
    [Arguments("b")]
    [Arguments("bold")]
    [Arguments("Strong")]
    [Arguments("iTaLiC")]
    [Arguments("em")]
    [Arguments("underline")]
    [Arguments("StRiKeOuT")]
    [Arguments("color")]
    [Arguments("high")]
    [Arguments("font")]
    public async Task CaseDoesNotChangeTheAnswer(string name) =>
        await Assert.That(MxpTagCategory.IsOpen(name)).IsTrue();

    /// <summary>
    /// "All other MXP tags are SECURE tags." The interesting members are the two that carry a
    /// command (<c>SEND</c>, <c>A</c>), the two the parser consumes rather than renders
    /// (<c>VAR</c>, <c>IMG</c>), the element definition a server uses to invent tags, and a name
    /// nobody has defined at all — because this is an allow-list, an unknown tag must come back
    /// secure rather than fall through some default.
    /// </summary>
    [Test]
    [Arguments("SEND")]
    [Arguments("send")]
    [Arguments("A")]
    [Arguments("a")]
    [Arguments("VAR")]
    [Arguments("IMG")]
    [Arguments("BR")]
    [Arguments("P")]
    [Arguments("H1")]
    [Arguments("EXPIRE")]
    [Arguments("VERSION")]
    [Arguments("!ELEMENT")]
    [Arguments("NOSUCHTAG")]
    [Arguments("")]
    public async Task EverythingElseIsSecure(string name) =>
        await Assert.That(MxpTagCategory.IsOpen(name)).IsFalse();

    /// <summary>
    /// A near-miss is not a match. The lookup is exact, so no prefix, suffix or padded spelling of
    /// an open tag can be smuggled past it — <c>SENDB</c> must not ride in on <c>B</c>.
    /// </summary>
    [Test]
    [Arguments("BB")]
    [Arguments("B ")]
    [Arguments(" B")]
    [Arguments("SENDB")]
    [Arguments("BSEND")]
    [Arguments("COLOUR")]
    [Arguments("/B")]
    public async Task ANearMissIsNotAnOpenTag(string name) =>
        await Assert.That(MxpTagCategory.IsOpen(name)).IsFalse();
}
