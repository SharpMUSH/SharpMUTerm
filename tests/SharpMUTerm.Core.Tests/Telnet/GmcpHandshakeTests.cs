using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Telnet;

/// <summary>
/// Once a server's GMCP is agreed the client introduces itself — <c>Core.Hello</c>, then
/// <c>Core.Supports.Set</c> — and not before. Servers in the IRE tradition send nothing until the second
/// arrives, and this client used to send neither.
/// </summary>
public class GmcpHandshakeTests
{
    private const byte Iac = 255;
    private const byte Will = 251;
    private const byte Sb = 250;
    private const byte Se = 240;
    private const byte Gmcp = 201;
    private const byte Msdp = 69;

    [Test]
    public async Task AnAgreedGmcpGetsHelloThenSupportsSet()
    {
        var transport = new ScriptedTransport { Greeting = [Iac, Will, Gmcp] };
        await using var session = new TelnetSession(
            transport, NullLogger.Instance, new TelnetSessionOptions { GmcpModules = ["IRE.Rift 1"] });
        await session.ConnectAsync();

        var messages = await Eventually(() => GmcpMessages(transport.Sent), m => m.Count >= 2);

        await Assert.That(messages.Count).IsEqualTo(2);
        await Assert.That(messages[0]).StartsWith("Core.Hello ");
        await Assert.That(messages[0]).Contains("\"SharpMUTerm\"");
        await Assert.That(messages[1]).StartsWith("Core.Supports.Set ");
        await Assert.That(messages[1]).Contains("\"Char 1\"");
        await Assert.That(messages[1]).Contains("\"Room 1\"");
        await Assert.That(messages[1]).Contains("\"IRE.Rift 1\"");
    }

    /// <summary>The handshake is an answer to the server's offer, never a question of the client's own.</summary>
    [Test]
    public async Task NoOfferMeansNoHandshake()
    {
        var transport = new ScriptedTransport();
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        await session.ConnectAsync();
        await Task.Delay(150);

        await Assert.That(GmcpMessages(transport.Sent)).IsEmpty();
    }

    [Test]
    public async Task TheHandshakeCanBeSwitchedOff()
    {
        var transport = new ScriptedTransport { Greeting = [Iac, Will, Gmcp] };
        await using var session = new TelnetSession(
            transport, NullLogger.Instance, new TelnetSessionOptions { IntroduceOverGmcp = false });
        await session.ConnectAsync();
        await Task.Delay(150);

        await Assert.That(GmcpMessages(transport.Sent)).IsEmpty();
    }

    [Test]
    public async Task AgreeingMsdpIsReported()
    {
        var transport = new ScriptedTransport { Greeting = [Iac, Will, Msdp] };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        var raised = 0;
        session.MsdpEnabled += (_, _) => Interlocked.Increment(ref raised);
        await session.ConnectAsync();

        await Eventually(() => Volatile.Read(ref raised), n => n > 0);

        await Assert.That(Volatile.Read(ref raised)).IsEqualTo(1);
    }

    [Test]
    [Arguments("Char 1", "Char", 1)]
    [Arguments("IRE.Rift", "IRE.Rift", 1)]
    [Arguments("  Comm.Channel   2 ", "Comm.Channel", 2)]
    public async Task AModuleParses(string spec, string name, int version)
    {
        var module = GmcpHandshake.Parse(spec);

        await Assert.That(module).IsNotNull();
        await Assert.That(module!.Value.Name).IsEqualTo(name);
        await Assert.That(module.Value.Version).IsEqualTo(version);
    }

    [Test]
    [Arguments("")]
    [Arguments("Char one")]
    [Arguments("Char 0")]
    [Arguments("Char 1 2")]
    [Arguments(".Char 1")]
    [Arguments("Char\"; 1")]
    public async Task ANonsenseModuleIsDropped(string spec) =>
        await Assert.That(GmcpHandshake.Parse(spec)).IsNull();

    /// <summary>A world's own entry for a default module replaces its version rather than listing it twice.</summary>
    [Test]
    public async Task AWorldsModuleOverridesADefaultsVersion()
    {
        var modules = GmcpHandshake.Modules(["room 2", "IRE.Rift 1"]);

        await Assert.That(modules.Count(m => m.Name.Equals("Room", StringComparison.OrdinalIgnoreCase))).IsEqualTo(1);
        await Assert.That(modules.Single(m => m.Name.Equals("room", StringComparison.OrdinalIgnoreCase)).Version).IsEqualTo(2);
        await Assert.That(modules[^1].Name).IsEqualTo("IRE.Rift");
    }

    /// <summary>Every <c>IAC SB GMCP … IAC SE</c> payload in a byte stream, as text.</summary>
    private static List<string> GmcpMessages(byte[] wire)
    {
        var messages = new List<string>();
        for (var i = 0; i + 2 < wire.Length; i++)
        {
            if (wire[i] != Iac || wire[i + 1] != Sb || wire[i + 2] != Gmcp)
            {
                continue;
            }

            var start = i + 3;
            var end = start;
            while (end + 1 < wire.Length && !(wire[end] == Iac && wire[end + 1] == Se))
            {
                end++;
            }

            messages.Add(Encoding.UTF8.GetString(wire, start, end - start));
            i = end;
        }

        return messages;
    }

    private static async Task<T> Eventually<T>(Func<T> read, Func<T, bool> done)
    {
        var value = read();
        for (var i = 0; i < 100 && !done(value); i++)
        {
            await Task.Delay(20);
            value = read();
        }

        return value;
    }
}
