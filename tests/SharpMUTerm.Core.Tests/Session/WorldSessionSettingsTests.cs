using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Core.Tests.Session;

/// <summary>
/// The world and character settings F5 gained: the on-disconnect commands, the <c>ansi</c> content choice, and
/// emoji switches that reach the next connection.
/// </summary>
public class WorldSessionSettingsTests
{
    private static WorldDefinition World() => new() { Name = "T", Host = "h", Port = 1 };

    [Test]
    public async Task DisconnectingSendsTheOnDisconnectCommandsFirst()
    {
        var telnet = new FakeTelnetSession();
        var character = new CharacterDefinition { Name = "Ann", OnDisconnect = "say bye; QUIT" };
        var session = new WorldSession(World(), character, sessionFactory: _ => telnet);
        await session.ConnectAsync();

        await session.DisconnectAsync();

        await Assert.That(telnet.SentLines).IsEquivalentTo(new[] { "say bye", "QUIT" });
        await Assert.That(session.IsConnected).IsFalse();
    }

    [Test]
    public async Task QuittingTheClientSendsThemToo()
    {
        var telnet = new FakeTelnetSession();
        var character = new CharacterDefinition { Name = "Ann", OnDisconnect = "QUIT" };
        var session = new WorldSession(World(), character, sessionFactory: _ => telnet);
        await session.ConnectAsync();

        await session.DisposeAsync();

        await Assert.That(telnet.SentLines).IsEquivalentTo(new[] { "QUIT" });
    }

    [Test]
    public async Task AConnectionTheServerDroppedSendsNothing()
    {
        var telnet = new FakeTelnetSession();
        var character = new CharacterDefinition { Name = "Ann", OnDisconnect = "QUIT" };
        var session = new WorldSession(World(), character, sessionFactory: _ => telnet);
        await session.ConnectAsync();
        await telnet.DisconnectAsync(); // the server's close, as the session sees it

        await session.DisconnectAsync();
        await session.DisposeAsync();

        await Assert.That(telnet.SentLines).IsEmpty();
    }

    [Test]
    public async Task AnAnsiOnlyWorldIsNotSwitchedToMxp()
    {
        var world = World();
        world.ContentFormat = ContentFormat.AnsiOnly;
        var telnet = new FakeTelnetSession();
        var session = new WorldSession(world, sessionFactory: _ => telnet);
        await session.ConnectAsync();

        telnet.RaiseMxpEnabled();
        telnet.EmitLine("<B>after</B>");

        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text == "<B>after</B>")).IsTrue();
    }

    [Test]
    public async Task EmojiSwitchesChangedBetweenConnectionsApplyToTheNextOne()
    {
        var world = World();
        var telnet = new FakeTelnetSession();
        var session = new WorldSession(world, sessionFactory: _ => telnet);
        await session.ConnectAsync();
        telnet.EmitLine("one :fire:");

        await session.DisconnectAsync();
        world.Emoji.Enabled = true;
        await session.ConnectAsync();
        telnet.EmitLine("two :fire:");

        var lines = session.Scrollback.Snapshot();
        await Assert.That(lines.Any(l => l.Text == "one :fire:")).IsTrue();
        await Assert.That(lines.Any(l => l.Text.StartsWith("two", StringComparison.Ordinal) && l.Text.Contains("🔥", StringComparison.Ordinal))).IsTrue();
    }
    [Test]
    public async Task PuebloNegotiationSwitchesAnAutomaticWorldsParser()
    {
        var telnet = new FakeTelnetSession();
        var session = new WorldSession(World(), sessionFactory: _ => telnet);
        await session.ConnectAsync();

        telnet.EmitLine("<b>before</b>");
        telnet.RaisePuebloEnabled();
        telnet.EmitLine("<b>after</b>");

        var lines = session.Scrollback.Snapshot();
        await Assert.That(lines.Any(l => l.Text == "<b>before</b>")).IsTrue();
        await Assert.That(lines.Any(l => l.Text == "after")).IsTrue();
    }

    /// <summary>
    /// The first markup a connection negotiates keeps it: MXP after Pueblo leaves the Pueblo parser in place.
    /// <c>SEND</c> tells them apart — a fresh MXP parser refuses it on an open line and echoes it, Pueblo
    /// strips it.
    /// </summary>
    [Test]
    public async Task MxpAfterPuebloDoesNotSwitchAgain()
    {
        var telnet = new FakeTelnetSession();
        var session = new WorldSession(World(), sessionFactory: _ => telnet);
        await session.ConnectAsync();

        telnet.RaisePuebloEnabled();
        telnet.RaiseMxpEnabled();
        telnet.EmitLine("<SEND>after</SEND>");

        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text == "after")).IsTrue();
    }

    [Test]
    public async Task AnExplicitMxpWorldIgnoresPueblo()
    {
        var world = World();
        world.ContentFormat = ContentFormat.Mxp;
        var telnet = new FakeTelnetSession();
        var session = new WorldSession(world, sessionFactory: _ => telnet);
        await session.ConnectAsync();

        telnet.RaisePuebloEnabled();
        telnet.EmitLine("<SEND>after</SEND>");

        await Assert.That(session.Scrollback.Snapshot().Any(l => l.Text == "after")).IsFalse();
    }
}
