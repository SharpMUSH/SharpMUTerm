using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Telnet;

/// <summary>NEW-ENVIRON as MNES: answered when a server asks, with the client's identity and nothing about the machine.</summary>
public class MnesTests
{
    private const byte Iac = 255, Sb = 250, Se = 240, Do = 253, Will = 251, NewEnviron = 39;
    private const byte Is = 0, Send = 1, Var = 0, Val = 1;

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    private static byte[] Variable(string name, string value) =>
        [Var, .. Encoding.ASCII.GetBytes(name), Val, .. Encoding.ASCII.GetBytes(value)];

    [Test]
    public async Task AServersSendIsAnsweredWithTheClientsIdentity()
    {
        var transport = new ScriptedTransport { Greeting = [Iac, Do, NewEnviron, Iac, Sb, NewEnviron, Send, Iac, Se] };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        await session.ConnectAsync();

        await Until(() => Contains(transport.Sent, [Iac, Sb, NewEnviron, Is]));
        await Until(() => Contains(transport.Sent, [Iac, Se]));
        var sent = transport.Sent;

        await Assert.That(Contains(sent, [Iac, Will, NewEnviron])).IsTrue();
        await Assert.That(Contains(sent, Variable("CLIENT_NAME", "SharpMUTerm"))).IsTrue();
        await Assert.That(Contains(sent, Variable("TERMINAL_TYPE", "XTERM"))).IsTrue();
        await Assert.That(Contains(sent, Variable("MTTS", "2845"))).IsTrue();
        await Assert.That(Contains(sent, Encoding.ASCII.GetBytes("IPADDRESS"))).IsFalse();
        await Assert.That(Contains(sent, Encoding.ASCII.GetBytes("USER"))).IsFalse();
    }

    [Test]
    public async Task NothingIsOfferedToAServerThatDoesNotAsk()
    {
        var transport = new ScriptedTransport { Greeting = Encoding.ASCII.GetBytes("Hello.\r\n") };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        await session.ConnectAsync();
        await Task.Delay(100);

        await Assert.That(Contains(transport.Sent, [Iac, Will, NewEnviron])).IsFalse();
    }

    [Test]
    public async Task TheMttsOverNewEnvironIsTheOneTtypeEndsOn()
    {
        var identity = TelnetSessionOptions.MnesIdentity(null);
        await Assert.That(TelnetSessionOptions.SharpMUTermTerminalTypes).Contains("MTTS " + identity["MTTS"]);
        await Assert.That(int.Parse(identity["MTTS"]) & 512).IsEqualTo(512);
        await Assert.That(identity.ContainsKey("CHARSET")).IsFalse();
    }
}
