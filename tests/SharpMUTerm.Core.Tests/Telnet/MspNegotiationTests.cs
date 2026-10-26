using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Telnet;

/// <summary>
/// MSP (option 90) from the client's side: agreed only by a session that plays sound, and reported through
/// <see cref="ITelnetSession.MspChanged"/> both ways, because a <c>!!SOUND</c> line is a request only while the
/// option is agreed.
/// </summary>
public class MspNegotiationTests
{
    private const byte Iac = 255, Will = 251, Wont = 252, Do = 253, Dont = 254, Msp = 90;

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        return condition();
    }

    [Test]
    public async Task ASessionThatPlaysSoundAgreesAndReportsBothWays()
    {
        var transport = new ScriptedTransport { Greeting = [Iac, Will, Msp] };
        await using var session = new TelnetSession(
            transport, NullLogger.Instance, new TelnetSessionOptions { PlaysMsp = true });
        var changes = new List<bool>();
        session.MspChanged += (_, active) => { lock (changes) changes.Add(active); };
        await session.ConnectAsync();

        await Assert.That(await transport.WaitForSentAsync([Iac, Do, Msp])).IsTrue();
        await Assert.That(await WaitForAsync(() => { lock (changes) return changes.Count == 1; })).IsTrue();

        transport.SendToClient([Iac, Wont, Msp]);
        await Assert.That(await transport.WaitForSentAsync([Iac, Dont, Msp])).IsTrue();
        await Assert.That(await WaitForAsync(() => { lock (changes) return changes.Count == 2; })).IsTrue();

        bool[] seen;
        lock (changes)
        {
            seen = [.. changes];
        }

        await Assert.That(seen).IsEquivalentTo(new[] { true, false });
    }

    [Test]
    public async Task ASessionWithNothingToPlayRefusesAndSaysNothing()
    {
        var transport = new ScriptedTransport { Greeting = [Iac, Will, Msp] };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        var raised = 0;
        session.MspChanged += (_, _) => Interlocked.Increment(ref raised);
        await session.ConnectAsync();

        await Assert.That(await transport.WaitForSentAsync([Iac, Dont, Msp])).IsTrue();
        await Task.Delay(50);
        await Assert.That(Volatile.Read(ref raised)).IsEqualTo(0);
        await Assert.That(Contains(transport.Sent, [Iac, Do, Msp])).IsFalse();
    }

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;
}
