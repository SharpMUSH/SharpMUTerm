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
    /// An escape between the arming and the tag disarms too — it is "something other than a
    /// '&lt;'". One case per exit from the escape state machine, because the disarm is a property of
    /// <em>how the sequence ended</em> and each exit had to be told separately: miss one and the
    /// exploit above comes back with that escape in front of it.
    /// <para>
    /// Written <c>\u001b</c> rather than <c>\x1b</c> deliberately: <c>\x</c> is greedy over hex
    /// digits, so <c>"\x1bc"</c> is the single character U+01BC and not ESC followed by 'c'.
    /// </para>
    /// </summary>
    [Test]
    [Arguments("\u001bc")] // RIS — ProcessEscape's two-byte default arm
    [Arguments("\u001b7")] // save cursor — same arm
    [Arguments("\u001bM")] // reverse index — same arm
    [Arguments("\u001b(B")] // designate charset — the intermediate arm
    [Arguments("\u001b[0m")] // SGR — a CSI final byte that is not 'z'
    [Arguments("\u001b[2J")] // erase — a CSI final byte that is discarded
    [Arguments("\u001b[1\u0001z")] // a control byte mid-sequence — the malformed-abort arm
    [Arguments("\u001b]0;title\u0007")] // OSC, BEL-terminated
    [Arguments("\u001b]0;title\u001b\\")] // OSC, ST-terminated
    public async Task TempSecure_IsSpentByAnyEscapeThatIsNotALineTag(string escape)
    {
        var parser = new MxpParser();

        var line = parser.Feed(TempSecure + escape + "<SEND HREF=\"@shutdown\">click</SEND>\n")[0];

        await Assert.That(line.Text).Contains("<SEND HREF=\"@shutdown\">");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    /// <summary>
    /// And a <em>line</em> tag between the arming and the tag disarms as well. Only <c>ESC[4z</c>
    /// leaves an arming standing, so the model is uniform with every other kind of sequence. Not
    /// player-reachable — anyone who can emit <c>ESC[0z</c> can emit <c>ESC[1z</c> and skip the
    /// mechanism — but a rule with one exception is easier to keep right than a rule with two.
    /// </summary>
    /// <remarks>
    /// Every case leaves the line in Open mode — <c>ESC[2z</c> and <c>ESC[6z</c> are left out on
    /// purpose, because a locked or secured line refuses or honours the SEND for its <em>own</em>
    /// reason and the arming would go untested. Nothing follows the line tag but the tag itself, so
    /// the only thing that can have disarmed is the line tag under test.
    /// </remarks>
    [Test]
    [Arguments("\u001b[0z")] // OPEN
    [Arguments("\u001b[5z")] // LOCK OPEN
    [Arguments("\u001b[99z")] // a number with no meaning
    [Arguments("\u001b[z")] // no number at all
    public async Task TempSecure_IsSpentByAnyLineTagButItself(string lineTag)
    {
        var parser = new MxpParser();

        var line = parser.Feed(TempSecure + lineTag + "<SEND HREF=\"@shutdown\">click</SEND>\n")[0];

        await Assert.That(line.Text).Contains("<SEND HREF=\"@shutdown\">");
        await Assert.That(line.Spans.Any(s => s.IsInteractive)).IsFalse();
    }

    /// <summary>A repeated <c>ESC[4z</c> does not disarm itself — the exception is exactly one tag wide.</summary>
    [Test]
    public async Task TempSecure_SurvivesARepeatOfItself()
    {
        var parser = new MxpParser();

        var line = parser.Feed(TempSecure + TempSecure + "<SEND HREF=\"look\">a</SEND>\n")[0];

        await Assert.That(line.Text).IsEqualTo("a");
        await Assert.That(line.Spans[0].IsInteractive).IsTrue();
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
    /// <see cref="MxpParser.Reset"/> clears all three mode fields, and it takes <em>two</em> lines to
    /// say so. Line one catches <c>_lineMode</c> and <c>_tempSecure</c>; only line two catches
    /// <c>_defaultMode</c>, because <c>_lineMode</c> is re-read from it in <c>CompleteLine</c> and
    /// nowhere else — so a Reset that forgot the default alone would still refuse the first SEND.
    /// </summary>
    [Test]
    public async Task Reset_ClearsEveryModeField()
    {
        var parser = new MxpParser();
        parser.Feed(LockSecure + "a\n"); // default and current mode both Secure
        parser.Feed(TempSecure); // and an arming pending

        parser.Reset();

        var lines = parser.Feed(
            "<SEND HREF=\"@shutdown\">one</SEND>\n<SEND HREF=\"@shutdown\">two</SEND>\n");

        await Assert.That(lines[0].Text).Contains("<SEND HREF=\"@shutdown\">");
        await Assert.That(lines[0].Spans.Any(s => s.IsInteractive)).IsFalse();
        await Assert.That(lines[1].Text).Contains("<SEND HREF=\"@shutdown\">");
        await Assert.That(lines[1].Spans.Any(s => s.IsInteractive)).IsFalse();
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
