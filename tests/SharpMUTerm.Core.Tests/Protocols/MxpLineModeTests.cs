using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Protocols;

/// <summary>
/// MXP's line-security model. The spec's rationale is the whole point of this file: "players on a
/// MUD can exploit this power and cause problems… you would not want to allow them to… execute
/// script commands on the client of other users." Without it, a SEND tag typed by another player
/// into a public channel becomes a clickable command in this client.
/// </summary>
public class MxpLineModeTests
{
    private const string Open = "\x1b[0z";
    private const string Secure = "\x1b[1z";
    private const string Locked = "\x1b[2z";
    private const string Reset = "\x1b[3z";
    private const string TempSecure = "\x1b[4z";
    private const string LockOpen = "\x1b[5z";
    private const string LockSecure = "\x1b[6z";
    private const string LockLocked = "\x1b[7z";

    /// <summary>The attack the mode system exists to stop.</summary>
    [Test]
    public async Task SecureTagOnAnOpenLine_IsShownAsTextAndIsNotClickable()
    {
        var parser = new MxpParser();

        var line = parser.Feed("Rivane says, '<SEND href=\"@shutdown\">click</SEND>'\n")[0];

        await Assert.That(line.Text).Contains("<SEND href=\"@shutdown\">");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    [Test]
    public async Task OpenTagOnAnOpenLine_IsHonoured()
    {
        var parser = new MxpParser();

        var line = parser.Feed("plain <B>bold</B>\n")[0];

        await Assert.That(line.Text).IsEqualTo("plain bold");
        await Assert.That(line.Spans[^1].Style.Attributes.HasFlag(TextAttributes.Bold)).IsTrue();
    }

    [Test]
    public async Task SecureTagOnASecureLine_IsHonoured()
    {
        var parser = new MxpParser();

        var line = parser.Feed(Secure + "<SEND href=\"look\">look</SEND>\n")[0];

        await Assert.That(line.Text).IsEqualTo("look");
        await Assert.That(line.Spans[0].IsInteractive).IsTrue();
    }

    [Test]
    public async Task LockedLine_ParsesNothingAtAll()
    {
        var parser = new MxpParser();

        var line = parser.Feed(Locked + "<B>not bold</B> &amp; not an entity\n")[0];

        await Assert.That(line.Text).IsEqualTo("<B>not bold</B> &amp; not an entity");
    }

    /// <summary>Spec: OPEN, SECURE and LOCKED all revert "when a newline is received".</summary>
    [Test]
    public async Task ModeRevertsToTheDefaultAtTheNextNewline()
    {
        var parser = new MxpParser();

        var lines = parser.Feed(Secure + "<SEND href=\"look\">a</SEND>\n<SEND href=\"look\">b</SEND>\n");

        await Assert.That(lines[0].Spans[0].IsInteractive).IsTrue();
        await Assert.That(lines[1].Text).Contains("<SEND");
    }

    /// <summary>Spec: LOCK SECURE makes "Secure mode … the new default mode".</summary>
    [Test]
    public async Task LockSecure_SurvivesTheNewline()
    {
        var parser = new MxpParser();

        var lines = parser.Feed(LockSecure + "<SEND href=\"look\">a</SEND>\n<SEND href=\"look\">b</SEND>\n");

        await Assert.That(lines[0].Spans[0].IsInteractive).IsTrue();
        await Assert.That(lines[1].Spans[0].IsInteractive).IsTrue();
    }

    /// <summary>Spec: TEMP SECURE sets "secure mode for the next tag only".</summary>
    [Test]
    public async Task TempSecure_CoversExactlyOneTag()
    {
        var parser = new MxpParser();

        var line = parser.Feed(TempSecure + "<SEND href=\"look\">a</SEND><SEND href=\"x\">b</SEND>\n")[0];

        await Assert.That(line.Spans[0].IsInteractive).IsTrue();
        await Assert.That(line.Text).Contains("<SEND href=\"x\">");
    }

    /// <summary>Spec: RESET closes open tags, returns to Open, and resets the style.</summary>
    [Test]
    public async Task Reset_ReturnsToOpenAndClearsStyle()
    {
        var parser = new MxpParser();

        var line = parser.Feed(LockSecure + "<B>bold" + Reset + "after<SEND href=\"x\">c</SEND>\n")[0];

        await Assert.That(line.Spans[^1].Style.Attributes.HasFlag(TextAttributes.Bold)).IsFalse();
        await Assert.That(line.Text).Contains("<SEND href=\"x\">");
    }

    /// <summary>
    /// The real prompt from tdome.nukefire.org, byte for byte: the server secures each of its own
    /// tags and locks the content between them so a player-supplied name cannot inject one.
    /// </summary>
    [Test]
    public async Task NukeFirePrompt_ParsesIntoTwoClickableAnswers()
    {
        var parser = new MxpParser();

        parser.Feed(
            "at right, Pemberton ("
            + Secure + "<send>" + LockLocked + "Y" + Secure + "</send>" + LockLocked + "/"
            + Secure + "<send>" + LockLocked + "N" + Secure + "</send>" + LockLocked + ")?");
        var line = parser.Flush()!;

        await Assert.That(line.Text).IsEqualTo("at right, Pemberton (Y/N)?");
        await Assert.That(line.Spans.Count(s => s.IsInteractive)).IsEqualTo(2);
    }

    [Test]
    public async Task LockOpen_MakesOpenTheDefaultAgain()
    {
        var parser = new MxpParser();

        var lines = parser.Feed(LockSecure + "a\n" + LockOpen + "<SEND href=\"x\">b</SEND>\n");

        await Assert.That(lines[1].Text).Contains("<SEND");
    }

    [Test]
    public async Task ClosingTagTakesTheCategoryOfItsElement()
    {
        var parser = new MxpParser();

        var line = parser.Feed("</SEND> and </B>\n")[0];

        await Assert.That(line.Text).IsEqualTo("</SEND> and ");
    }

    /// <summary>
    /// The refused tag is echoed byte for byte. Anything the round trip normalises — case, the
    /// spacing between attributes, the quoting style — is a character a player could use to smuggle
    /// something past a reader who is being shown "what the other player typed".
    /// </summary>
    [Test]
    public async Task ARefusedTagIsEchoedByteForByte()
    {
        var parser = new MxpParser();
        const string tag = "<SeNd   hReF = '@shutdown'\tHINT=\"a  b\"   >";

        var line = parser.Feed("Rivane says, '" + tag + "click</sEnD  >'\n")[0];

        await Assert.That(line.Text).IsEqualTo("Rivane says, '" + tag + "click</sEnD  >'");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    /// <summary>
    /// An unknown or unparseable line-tag number is ignored rather than guessed at: a mode this
    /// client invents is a mode the server did not ask for.
    /// </summary>
    [Test]
    public async Task AnUnknownLineTagNumberLeavesTheModeAlone()
    {
        var parser = new MxpParser();

        // 99 has no meaning, ESC[z has no number, ESC[-1z and ESC[ 1 z are spellings the spec does
        // not have — none of them may move the mode, in either direction.
        var line = parser.Feed(Secure + "\x1b[99z\x1b[z\x1b[-1z\x1b[ 1 z<SEND href=\"look\">a</SEND>\n")[0];

        await Assert.That(line.Text).IsEqualTo("a");
        await Assert.That(line.Spans[0].IsInteractive).IsTrue();
    }

    /// <summary>
    /// Spec: TEMP SECURE "must be immediately followed by a '&lt;' character to start a tag." The
    /// arming may not survive the server's own prose, or the first tag a <em>player</em> wrote into
    /// that prose spends it — which is this exploit, on a line the server never secured.
    /// </summary>
    [Test]
    public async Task TempSecure_IsSpentByInterveningText_NotByAPlayersTag()
    {
        var parser = new MxpParser();

        var line = parser.Feed(TempSecure + "Rivane says, '<SEND HREF=\"@shutdown\">click</SEND>'\n")[0];

        await Assert.That(line.Text).IsEqualTo("Rivane says, '<SEND HREF=\"@shutdown\">click</SEND>'");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    /// <summary>
    /// The same arming must not survive a locked stretch either: on a locked line no character can
    /// start a tag, so every one of them disarms, and the tag after the unlock is the player's.
    /// </summary>
    [Test]
    public async Task TempSecure_IsSpentByLockedText_NotBySurvivingTheUnlock()
    {
        var parser = new MxpParser();

        var line = parser.Feed(
            LockLocked + "Rivane says, " + TempSecure + "hello"
            + Open + "<SEND HREF=\"@shutdown\">click</SEND>\n")[0];

        await Assert.That(line.Text).Contains("<SEND HREF=\"@shutdown\">");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    /// <summary>
    /// A non-line-tag escape between the arming and the tag disarms too — it is "something other
    /// than a '&lt;'". <c>ESC[0m</c> is the one a server is most likely to emit there.
    /// </summary>
    [Test]
    public async Task TempSecure_IsSpentByAnEscapeThatIsNotALineTag()
    {
        var parser = new MxpParser();

        var line = parser.Feed(TempSecure + "\x1b[0m<SEND HREF=\"@shutdown\">click</SEND>\n")[0];

        await Assert.That(line.Text).Contains("<SEND HREF=\"@shutdown\">");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    /// <summary>
    /// A refused close would leave the frame open, and a deferred <c>&lt;send&gt;</c> — one with no
    /// HREF, whose command is its enclosed text — absorbs every span to the end of the line. The
    /// player's speech would become the tail of the command the server's own tag runs.
    /// </summary>
    [Test]
    public async Task ARefusedCloseCannotFeedPlayerTextIntoADeferredSendsCommand()
    {
        var parser = new MxpParser();

        var line = parser.Feed(TempSecure + "<send>Y</send> Rivane says, 'hi'\n")[0];

        await Assert.That(line.Text).IsEqualTo("Y Rivane says, 'hi'");

        // The server's own one-tag send survives, with exactly the command it wrote.
        var commands = line.Spans.Where(s => s.IsInteractive).Select(s => s.Interaction!.Target).ToList();
        await Assert.That(commands).IsEquivalentTo(new[] { "Y" });
        await Assert.That(commands.Any(c => c.Contains("Rivane"))).IsFalse();
        await Assert.That(commands.Any(c => c.Contains("send"))).IsFalse();
    }

    /// <summary>
    /// The same shape without TEMP SECURE: the server secures its opener and drops to Open for the
    /// content, which is a real pattern (the NukeFire prompt is this with LOCKED for the content).
    /// </summary>
    [Test]
    public async Task ACloseIsHonouredOnAnOpenLineWhenItsElementIsStillOpen()
    {
        var parser = new MxpParser();

        var line = parser.Feed(Secure + "<send>" + Open + "Y</send> Rivane says, 'hi'\n")[0];

        await Assert.That(line.Text).IsEqualTo("Y Rivane says, 'hi'");
        await Assert.That(line.Spans.Where(s => s.IsInteractive).Select(s => s.Interaction!.Target))
            .IsEquivalentTo(new[] { "Y" });
    }

    /// <summary>A close matching nothing open is still refused, and still echoed byte for byte.</summary>
    [Test]
    public async Task ACloseMatchingNothingOpenIsStillRefused()
    {
        var parser = new MxpParser();

        var line = parser.Feed("Rivane says, '</SEND>'\n")[0];

        await Assert.That(line.Text).IsEqualTo("Rivane says, '</SEND>'");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    /// <summary>A pending TEMP SECURE dies with the line rather than arming the next line's first tag.</summary>
    [Test]
    public async Task TempSecure_DoesNotSurviveTheNewline()
    {
        var parser = new MxpParser();

        var lines = parser.Feed(TempSecure + "\n<SEND HREF=\"@shutdown\">click</SEND>\n");

        await Assert.That(lines[1].Text).Contains("<SEND HREF=\"@shutdown\">");
        await Assert.That(lines[1].Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    /// <summary>
    /// <see cref="MxpParser.Reset"/> clears all three mode fields. Each alone would let a SEND
    /// through here: the default and the current mode are both Secure when Reset is called, and the
    /// arming is pending.
    /// </summary>
    [Test]
    public async Task Reset_ClearsEveryModeField()
    {
        var parser = new MxpParser();
        parser.Feed(LockSecure + "a\n");
        parser.Feed(TempSecure);

        parser.Reset();

        var line = parser.Feed("<SEND HREF=\"@shutdown\">click</SEND>\n")[0];

        await Assert.That(line.Text).Contains("<SEND HREF=\"@shutdown\">");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    /// <summary>Open mode is the default, so <c>ESC[0z</c> is a no-op on a fresh parser.</summary>
    [Test]
    public async Task OpenLineTag_RefusesASecureTag()
    {
        var parser = new MxpParser();

        var line = parser.Feed(LockSecure + Open + "<SEND href=\"x\">b</SEND>\n")[0];

        await Assert.That(line.Text).Contains("<SEND href=\"x\">");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }
}
