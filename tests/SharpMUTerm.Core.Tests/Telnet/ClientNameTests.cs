using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Telnet;

/// <summary>TTYPE/MTTS: the first answer names this client, then the terminal, then the MTTS claim.</summary>
public class ClientNameTests
{
    private const byte Iac = 255, Sb = 250, Se = 240, Do = 253, Will = 251, Ttype = 24, Is = 0, Send = 1;

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;

    private static byte[] Answer(string name) => [Iac, Sb, Ttype, Is, .. Encoding.ASCII.GetBytes(name), Iac, Se];

    [Test]
    public async Task TheFirstTerminalTypeIsTheClientsName()
    {
        byte[] ask = [Iac, Sb, Ttype, Send, Iac, Se];
        var transport = new ScriptedTransport { Greeting = [Iac, Do, Ttype, .. ask, .. ask, .. ask] };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        await session.ConnectAsync();

        await Until(() => Contains(transport.Sent, Answer("MTTS 2845")));
        var sent = transport.Sent;

        await Assert.That(Contains(sent, [Iac, Will, Ttype])).IsTrue();
        await Assert.That(Contains(sent, Answer("SHARPMUTERM"))).IsTrue();
        await Assert.That(Contains(sent, Answer("XTERM"))).IsTrue();
        await Assert.That(Contains(sent, Answer("MTTS 2845"))).IsTrue();
        await Assert.That(Contains(sent, Answer("UNKNOWN"))).IsFalse();
    }

    [Test]
    public async Task TheVersionIsTheOneMinVerStamped()
    {
        var version = GmcpHandshake.ClientVersion;
        var stamped = typeof(GmcpHandshake).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;

        await Assert.That(version).IsNotEqualTo("0.0.0");
        await Assert.That(version).DoesNotContain("+");
        await Assert.That(stamped.StartsWith(version, StringComparison.Ordinal)).IsTrue();
        await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+")).IsTrue();
    }
}
