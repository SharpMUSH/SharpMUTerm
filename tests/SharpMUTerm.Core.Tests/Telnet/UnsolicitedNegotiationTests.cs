using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Telnet;

/// <summary>
/// The client asks no server to enable an option the server has not offered.
/// <para>
/// <b>This is the shape of a real failure, and the damage was invisible.</b> The session used to write
/// <c>IAC DO MSSP</c> to the transport the moment it connected, ahead of everything, so that a server
/// which supports MSSP but waits to be asked would answer. It is legal telnet — RFC 854 has either
/// party initiating, and requires a response even to a refusal — but a server that does not implement
/// the option has to <em>consume</em> those three bytes to refuse them, and one that does not leaves
/// them in its line buffer, where they are prepended to the next line the client sends. That line is
/// always the auto-login. The server sees <c>\xFF\xFD\x46connect Name password</c>, does not recognise
/// it, redisplays its connect screen, and the login silently never happens — while the transcript shows
/// a welcome screen twice and no reason for it, because the login line is not echoed or logged.
/// Measured against a live server: with the request, the login line was never evaluated; without it,
/// the same line reached the game.
/// </para>
/// <para>
/// The narrower lesson is where the bytes went. <see cref="TelnetSession"/> wrote them straight to the
/// transport, around TelnetNegotiationCore, because an option request must not be IAC-escaped as data.
/// The library would never have sent that <c>DO</c> on its own: its client-side MSSP answers a server's
/// <c>WILL</c> and initiates nothing. Negotiation is the library's to conduct, and a hand-written
/// negotiation byte is a negotiation nothing is keeping state for.
/// </para>
/// </summary>
public class UnsolicitedNegotiationTests
{
    private const byte Iac = 255;
    private const byte Do = 253;

    /// <summary>
    /// A server that offers nothing at all — the case that broke, and the commonest MU* server there
    /// is. Nothing the client writes may ask it to turn anything on.
    /// </summary>
    [Test]
    public async Task ConnectingToASilentServerRequestsNoOption()
    {
        var transport = new ScriptedTransport();
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        await session.ConnectAsync();
        await Task.Delay(100);

        await Assert.That(Requests(transport.Sent)).IsEmpty()
            .Because("a DO the peer has to consume in order to refuse is a DO a broken peer feeds to its parser");
    }

    /// <summary>
    /// And at the seam it cost, over a real socket.
    /// <para>
    /// <b>An injected session factory cannot pin this and it is worth saying why.</b> The request was
    /// configured in <c>WorldSession.DefaultSessionFactory</c> — the arm every world uses and the one a
    /// test that passes its own <c>sessionFactory</c> replaces wholesale. Such a test would have agreed
    /// with the code while every real connection carried the bytes, which is the exact reason this
    /// shipped. So this one takes a loopback listener and lets the session dial it: real
    /// <see cref="Core.Transport.TcpTransport"/>, real factory, and the assertion is on the bytes a
    /// server actually received before the login line.
    /// </para>
    /// </summary>
    [Test]
    public async Task NoOptionRequestPrecedesTheLoginLine()
    {
        using var server = new LoopbackServer();
        var world = new WorldDefinition { Name = "Convergence MUSH", Host = "127.0.0.1", Port = server.Port };
        var character = new CharacterDefinition { Name = "Mannaz", Password = "hunter2" };
        world.Characters.Add(character);

        await using var session = new WorldSession(world, character);
        await session.ConnectAsync();

        const string login = "connect Mannaz hunter2";
        var expected = Encoding.ASCII.GetBytes(login);
        for (var i = 0; i < 50 && IndexOf(server.Received, expected) < 0; i++)
        {
            await Task.Delay(20);
        }

        var wire = server.Received;
        var loginAt = IndexOf(wire, expected);
        await Assert.That(loginAt).IsGreaterThanOrEqualTo(0).Because("the login line has to have been sent at all");
        await Assert.That(Requests(wire[..loginAt])).IsEmpty()
            .Because("three unconsumed bytes in front of the login line are three bytes the server reads as part of it");
    }

    /// <summary>Every option the client asked the peer to enable, in order.</summary>
    private static IReadOnlyList<byte> Requests(byte[] wire)
    {
        var options = new List<byte>();
        for (var i = 0; i + 2 < wire.Length; i++)
        {
            if (wire[i] == Iac && wire[i + 1] == Do)
            {
                options.Add(wire[i + 2]);
            }
        }

        return options;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++)
            {
                match = haystack[i + j] == needle[j];
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
