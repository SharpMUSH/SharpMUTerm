using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Telnet;

/// <summary>
/// The Pueblo handshake from the client's side, as PennMUSH frames it: the server's hello is answered with
/// <c>PUEBLOCLIENT</c>, the server's start sequence switches the connection, and neither line reaches the
/// output. Pueblo is text at a login prompt, so nothing is said to a server that did not say hello first.
/// </summary>
public class PuebloHandshakeTests
{
    private const string Hello = "This world is Pueblo 1.10 Enhanced.\r\n";
    private const string Start = "</xch_mudtext><img xch_mode=purehtml><xch_page clear=text>\n";

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    [Test]
    public async Task TheHelloIsAnsweredAndTheStartSwitchesTheConnection()
    {
        var transport = new ScriptedTransport { Greeting = Ascii(Hello + "Welcome\r\n") };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        var lines = new List<string>();
        var enabled = 0;
        session.OutputReceived += (_, e) => { lock (lines) lines.Add(e.Text); };
        session.PuebloEnabled += (_, _) => Interlocked.Increment(ref enabled);
        await session.ConnectAsync();

        await Assert.That(await transport.WaitForSentAsync(Ascii("PUEBLOCLIENT 2.50\r\n"))).IsTrue();

        transport.SendToClient(Ascii(Start + "<b>bold</b>\r\n"));
        for (var i = 0; i < 100 && Volatile.Read(ref enabled) == 0; i++)
        {
            await Task.Delay(10);
        }

        await Assert.That(Volatile.Read(ref enabled)).IsEqualTo(1);
        await Task.Delay(50);
        string[] seen;
        lock (lines)
        {
            seen = [.. lines];
        }

        await Assert.That(seen).Contains("Welcome");
        await Assert.That(seen).Contains("<b>bold</b>");
        await Assert.That(seen.Any(l => l.Contains("Pueblo", StringComparison.Ordinal) || l.Contains("xch_", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ASilentServerHearsNothing()
    {
        var transport = new ScriptedTransport { Greeting = Ascii("Welcome\r\n") };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        await session.ConnectAsync();
        await Task.Delay(150);

        await Assert.That(Encoding.ASCII.GetString(transport.Sent)).DoesNotContain("PUEBLOCLIENT");
    }

    [Test]
    public async Task AWorldThatSaysNoDoesNotAnswer()
    {
        var transport = new ScriptedTransport { Greeting = Ascii(Hello) };
        await using var session = new TelnetSession(
            transport, NullLogger.Instance, new TelnetSessionOptions { AnnouncePueblo = false });
        await session.ConnectAsync();
        await Task.Delay(150);

        await Assert.That(Encoding.ASCII.GetString(transport.Sent)).DoesNotContain("PUEBLOCLIENT");
    }
}
