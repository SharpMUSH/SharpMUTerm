using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Telnet;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Session;

public class WorldSessionTests
{
    private static (WorldSession session, FakeTelnetSession telnet) Create(
        WorldDefinition world,
        TriggerSet? set = null)
    {
        var telnet = new FakeTelnetSession();
        var sets = set is null ? null : new[] { set };
        var session = new WorldSession(world, triggerSets: sets, sessionFactory: _ => telnet);
        return (session, telnet);
    }

    private static WorldDefinition World() => new() { Name = "T", Host = "h", Port = 1, LocalEcho = true };

    /// <summary>
    /// A session whose factory hands out a <em>fresh</em> fake per connect, the way a real one does,
    /// and a <c>dial</c> that connects and returns the fake now on the wire. Reusing one fake across a
    /// reconnect is not the shipping shape: <c>ConnectAsync</c> disposes the old
    /// <see cref="ITelnetSession"/> and subscribes to the new one, so with a single instance every
    /// handler is attached twice and each emitted line is parsed twice.
    /// </summary>
    private static (WorldSession session, Func<Task<FakeTelnetSession>> dial) CreateReconnectable(WorldDefinition world)
    {
        FakeTelnetSession? current = null;
        var session = new WorldSession(world, sessionFactory: _ => current = new FakeTelnetSession());
        return (session, async () =>
        {
            await session.ConnectAsync();
            return current!;
        }
        );
    }

    [Test]
    public async Task OutputLine_IsAppendedToScrollbackAndRaisesEvent()
    {
        var (session, telnet) = Create(World());
        StyledLine? printed = null;
        session.LinePrinted += (_, l) => printed = l;
        await session.ConnectAsync();

        telnet.EmitLine("You see a troll.");

        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text == "You see a troll.")).IsTrue();
        await Assert.That(printed).IsNotNull();
    }

    [Test]
    public async Task EmptyOutputLine_IsAppendedAsBlankLine()
    {
        var (session, telnet) = Create(World());
        await session.ConnectAsync();
        var before = session.Scrollback.Snapshot().Count;

        telnet.EmitLine(string.Empty);

        var after = session.Scrollback.Snapshot();
        await Assert.That(after.Count).IsEqualTo(before + 1);
        await Assert.That(after[^1].IsEmpty).IsTrue();
    }

    [Test]
    public async Task SameFrame_OutputPreservesAnEmptyMiddleLine()
    {
        var (session, telnet) = Create(World());
        await session.ConnectAsync();
        var before = session.Scrollback.Snapshot().Count;

        telnet.EmitLine("Line one\n\nThis line three, previous line was empty, this is valid output. It came from the same received frame.");

        var after = session.Scrollback.Snapshot();
        await Assert.That(after.Count).IsEqualTo(before + 3);
        await Assert.That(after[before].Text).IsEqualTo("Line one");
        await Assert.That(after[before + 1].IsEmpty).IsTrue();
        await Assert.That(after[before + 2].Text).IsEqualTo("This line three, previous line was empty, this is valid output. It came from the same received frame.");
    }

    [Test]
    public async Task AnsiColor_InOutput_IsParsedIntoStyledSpans()
    {
        var (session, telnet) = Create(World());
        await session.ConnectAsync();

        telnet.EmitLine("\x1b[31mdanger\x1b[0m");

        var line = session.Scrollback.Snapshot().First(l => l.Text == "danger");
        await Assert.That(line.Spans[0].Style.Foreground).IsEqualTo(TerminalColor.FromIndex(1));
    }

    [Test]
    public async Task Trigger_Gag_SuppressesLineFromScrollback()
    {
        var set = new TriggerSet();
        set.Triggers.Add(new Trigger { Pattern = "secret", Actions = new TriggerActions { Gag = true } });
        var (session, telnet) = Create(World(), set);
        await session.ConnectAsync();

        telnet.EmitLine("a secret message");

        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text.Contains("secret message"))).IsFalse();
    }

    [Test]
    public async Task Trigger_Response_IsSentToServer()
    {
        var set = new TriggerSet();
        set.Triggers.Add(new Trigger
        {
            Pattern = @"^(\w+) waves",
            Actions = new TriggerActions { SendResponse = "wave $1" },
        });
        var (session, telnet) = Create(World(), set);
        await session.ConnectAsync();

        telnet.EmitLine("Gandalf waves");

        await Assert.That(telnet.SentLines).Contains("wave Gandalf");
    }

    [Test]
    public async Task Trigger_Spawn_RoutesLineToSpawnEvent()
    {
        var set = new TriggerSet();
        set.Triggers.Add(new Trigger { Pattern = @"\[chat\]", Actions = new TriggerActions { SpawnTarget = "Chat" } });
        var (session, telnet) = Create(World(), set);
        SpawnLineEventArgs? spawned = null;
        session.SpawnLine += (_, e) => spawned = e;
        await session.ConnectAsync();

        telnet.EmitLine("[chat] hello");

        await Assert.That(spawned).IsNotNull();
        await Assert.That(spawned!.Target).IsEqualTo("Chat");
    }

    [Test]
    public async Task Prompt_UpdatesCurrentPrompt_WithoutScrollback()
    {
        var (session, telnet) = Create(World());
        StyledLine? promptEvt = null;
        session.PromptChanged += (_, p) => promptEvt = p;
        await session.ConnectAsync();

        telnet.EmitPrompt("HP:100 >");

        await Assert.That(session.CurrentPrompt).IsNotNull();
        await Assert.That(session.CurrentPrompt!.Text).IsEqualTo("HP:100 >");
        await Assert.That(promptEvt).IsNotNull();
        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text == "HP:100 >")).IsFalse();
    }

    /// <summary>
    /// A prompt is a live connection's question. Left standing after a disconnect it is a dead
    /// server's question sitting above a command line that can no longer answer it.
    /// </summary>
    [Test]
    public async Task Prompt_IsClearedOnDisconnect()
    {
        var (session, telnet) = Create(World());
        var cleared = false;
        await session.ConnectAsync();
        telnet.EmitPrompt("Enter name: ");
        session.PromptChanged += (_, p) => cleared = p is null;

        await session.DisconnectAsync();

        await Assert.That(session.CurrentPrompt).IsNull();
        await Assert.That(cleared).IsTrue();
    }

    /// <summary>
    /// A session that was never prompted reports no change on the way out: an event claiming the
    /// prompt cleared, raised where there was never one, is a repaint nobody asked for.
    /// </summary>
    [Test]
    public async Task Disconnect_WithNoPrompt_RaisesNoPromptChange()
    {
        var (session, _) = Create(World());
        var raised = false;
        await session.ConnectAsync();
        session.PromptChanged += (_, _) => raised = true;

        await session.DisconnectAsync();

        await Assert.That(raised).IsFalse();
    }

    [Test]
    public async Task UserInput_IsEchoedAndSent()
    {
        var (session, telnet) = Create(World());
        await session.ConnectAsync();

        await session.SendUserInputAsync("look");

        await Assert.That(telnet.SentLines).Contains("look");
        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text == "look")).IsTrue();
    }

    [Test]
    public async Task UserInput_AliasIsExpandedBeforeSend()
    {
        var set = new TriggerSet();
        set.Aliases.Add(new Alias { Pattern = "^gt (.+)", Substitution = "grouptell $1" });
        var (session, telnet) = Create(World(), set);
        await session.ConnectAsync();

        await session.SendUserInputAsync("gt hello");

        await Assert.That(telnet.SentLines).Contains("grouptell hello");
        await Assert.That(telnet.SentLines).DoesNotContain("gt hello");
    }

    [Test]
    public async Task Macro_KeyResolvesAndSends()
    {
        var set = new TriggerSet();
        set.Macros.Add(new Macro { Key = "Ctrl+F1", Command = "north" });
        var (session, telnet) = Create(World(), set);
        await session.ConnectAsync();

        var command = await session.HandleKeyAsync("Ctrl+F1");

        await Assert.That(command).IsEqualTo("north");
        await Assert.That(telnet.SentLines).Contains("north");
    }

    [Test]
    public async Task Gmcp_IsReRaised()
    {
        var (session, telnet) = Create(World());
        GmcpMessageEventArgs? gmcp = null;
        session.GmcpReceived += (_, e) => gmcp = e;
        await session.ConnectAsync();

        telnet.EmitGmcp("Char.Vitals", "{\"hp\":50}");

        await Assert.That(gmcp).IsNotNull();
        await Assert.That(gmcp!.Package).IsEqualTo("Char.Vitals");
    }

    /// <summary>
    /// A saved password is the whole configuration a login needs: no second flag, and the on-connect
    /// commands follow it.
    /// </summary>
    [Test]
    public async Task Character_WithASavedPassword_SendsConnectStringAndOnConnect()
    {
        var character = new CharacterDefinition
        {
            Name = "Wizard",
            Password = "swordfish",
            OnConnect = "look; who",
        };
        var telnet = new FakeTelnetSession();
        var session = new WorldSession(World(), character, sessionFactory: _ => telnet);

        await session.ConnectAsync();

        await Assert.That(telnet.SentLines).Contains("connect Wizard swordfish");
        await Assert.That(telnet.SentLines).Contains("look");
        await Assert.That(telnet.SentLines).Contains("who");
        await Assert.That(session.SessionKey).IsEqualTo("T.Wizard");
    }

    [Test]
    public async Task AnonymousSession_KeyIsWorldName_AndSendsNoLoginLine()
    {
        var (session, telnet) = Create(World());
        await session.ConnectAsync();

        await Assert.That(session.SessionKey).IsEqualTo("T");
        await Assert.That(telnet.SentLines).IsEmpty();
    }

    [Test]
    public async Task State_TransitionsToConnected()
    {
        var (session, _) = Create(World());
        var states = new List<ConnectionState>();
        session.StateChanged += (_, e) => states.Add(e.State);
        await session.ConnectAsync();

        await Assert.That(session.State).IsEqualTo(ConnectionState.Connected);
        await Assert.That(states).Contains(ConnectionState.Connecting);
        await Assert.That(states).Contains(ConnectionState.Connected);
    }

    /// <summary>
    /// MXP is a negotiated telnet option, so the client learns it is in force from the wire and not
    /// from a config field a user has to know to set. NukeFire sent IAC WILL MXP, this client
    /// answered DO, and then parsed the stream with AnsiParser anyway — which is why its prompt
    /// showed a literal "<send>Y</send>".
    /// </summary>
    [Test]
    public async Task Mxp_NegotiationSwitchesTheParser()
    {
        var (session, telnet) = Create(World());
        await session.ConnectAsync();

        telnet.EmitLine("<B>before</B>");
        telnet.RaiseMxpEnabled();
        telnet.EmitLine("<B>after</B>");

        var lines = session.Scrollback.Snapshot();
        await Assert.That(lines.Any(l => l.Text == "<B>before</B>")).IsTrue();
        await Assert.That(lines.Any(l => l.Text == "after")).IsTrue();
    }

    /// <summary>
    /// A world explicitly set to Pueblo is a user's decision about a server that speaks a different
    /// markup, and a stray MXP negotiation must not overrule it.
    /// </summary>
    /// <remarks>
    /// This deliberately does not reuse <c>&lt;B&gt;...&lt;/B&gt;</c> from the test above: both
    /// <c>PuebloParser</c> and <c>MxpParser</c> treat B as always-open and strip it to plain "after"
    /// either way, so that content can't tell "still Pueblo" apart from "wrongly swapped to Mxp". SEND
    /// is a secure-only MXP tag (<see cref="MxpTagCategory"/>) that a freshly-negotiated (unsecured)
    /// <c>MxpParser</c> refuses and echoes back literally, brackets included — while
    /// <c>PuebloParser</c> has no such concept and strips it to plain "after" like any other anchor.
    /// Seeing "after" here is therefore proof the session is still parsing with Pueblo's rules.
    /// </remarks>
    [Test]
    public async Task Mxp_NegotiationDoesNotOverrideAnExplicitContentFormat()
    {
        var world = World();
        world.ContentFormat = ContentFormat.Pueblo;
        var (session, telnet) = Create(world);
        await session.ConnectAsync();

        telnet.RaiseMxpEnabled();
        telnet.EmitLine("<SEND>after</SEND>");

        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text == "after")).IsTrue();
    }

    /// <summary>
    /// The Critical, end to end through a real session: a server's own <c>ESC[1z</c> line must not
    /// leave every following line secure. This is the shape the product actually produces and the one
    /// no parser test reached — the telnet layer strips the terminator, so <c>OnOutputReceived</c>
    /// feeds a line with no <c>'\n'</c> in it and then flushes, and a revert that lived only in
    /// <c>CompleteLine</c> never ran. Servers rely on the newline revert the spec promises rather than
    /// closing with <c>ESC[0z</c>, so one secure line was enough to make the rest of the connection
    /// trusted: a <c>&lt;SEND&gt;</c> a player typed into a public channel became a clickable
    /// <c>@shutdown</c>, and a <c>&lt;VERSION&gt;</c> they typed put a reply on the wire.
    /// </summary>
    [Test]
    public async Task Mxp_ASecureLineFromTheServerDoesNotSecureTheLinesAfterIt()
    {
        var (session, telnet) = Create(World());
        await session.ConnectAsync();
        telnet.RaiseMxpEnabled();

        telnet.EmitLine("\x1b[1z<SEND HREF=\"north\">north</SEND>");
        telnet.EmitLine("Rivane says, '<SEND HREF=\"@shutdown\">click me</SEND>'");
        telnet.EmitLine("Rivane says, '<VERSION>'");

        var lines = session.Scrollback.Snapshot();

        // The server's own secure line is honoured, so the test cannot pass by MXP being off.
        await Assert.That(lines.Any(l => l.Spans.Any(sp => sp.Interaction?.Target == "north"))).IsTrue();

        // The player's is refused and echoed, brackets and all.
        var chat = lines.Single(l => l.Text.Contains("click me"));
        await Assert.That(chat.Text).IsEqualTo("Rivane says, '<SEND HREF=\"@shutdown\">click me</SEND>'");
        await Assert.That(chat.Spans.Any(sp => sp.IsInteractive)).IsFalse();

        // And nothing a player typed reaches the wire as a protocol reply.
        await Assert.That(telnet.SentLines.Any(l => l.Contains("<VERSION"))).IsFalse();
    }

    /// <summary>
    /// Parser state is per connection, and a reconnect is the obvious thing a user does when the
    /// client starts behaving oddly. A server that sent <c>ESC[6z</c> used to leave the <em>next</em>
    /// connection starting in secure default, so an unsecured <c>&lt;SEND&gt;</c> was honoured on a
    /// fresh socket that had never been told anything.
    /// </summary>
    [Test]
    public async Task Mxp_TheSecureDefaultDoesNotSurviveAReconnect()
    {
        var (session, dial) = CreateReconnectable(World());

        var first = await dial();
        first.RaiseMxpEnabled();
        first.EmitLine("\x1b[6zthe server locks secure mode on");

        await session.DisconnectAsync();
        var second = await dial();
        second.RaiseMxpEnabled();
        second.EmitLine("Rivane says, '<SEND HREF=\"@shutdown\">click me</SEND>'");

        var chat = session.Scrollback.Snapshot().Single(l => l.Text.Contains("click me"));
        await Assert.That(chat.Spans.Any(sp => sp.IsInteractive)).IsFalse();
    }

    /// <summary>
    /// The negotiated upgrade is parser state too. <c>ContentFormat</c> is what a connection starts
    /// from, and MXP has to be negotiated again to take it off ANSI — otherwise a reconnect to a
    /// server that never offers MXP goes on parsing the stream as MXP for ever.
    /// </summary>
    [Test]
    public async Task Mxp_TheNegotiatedUpgradeDoesNotSurviveAReconnect()
    {
        var (session, dial) = CreateReconnectable(World());

        var first = await dial();
        first.RaiseMxpEnabled();
        first.EmitLine("<B>upgraded</B>");

        await session.DisconnectAsync();
        var second = await dial();
        second.EmitLine("<B>after</B>");

        var lines = session.Scrollback.Snapshot();
        await Assert.That(lines.Any(l => l.Text == "upgraded")).IsTrue();
        await Assert.That(lines.Any(l => l.Text == "<B>after</B>")).IsTrue();
    }
}
