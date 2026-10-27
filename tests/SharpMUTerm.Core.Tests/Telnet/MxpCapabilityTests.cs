using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Telnet;

/// <summary>
/// A server's <c>&lt;SUPPORT&gt;</c> and <c>&lt;VERSION&gt;</c> are answered. Since TelnetNegotiationCore 4.3
/// the library takes both lines out of the stream once MXP mode has started and answers neither by itself, so
/// <see cref="MxpParser"/> never sees them. Unanswered, SharpMUSH recorded that this client renders none of the
/// tags it asked about and sent a picture's address as text instead of an <c>&lt;IMAGE&gt;</c>.
/// </summary>
public class MxpCapabilityTests
{
    private const byte Iac = 255, Will = 251, Do = 253, Sb = 250, Se = 240, Mxp = 91;

    private static async Task<ScriptedTransport> InMxpModeAsync(TelnetSession session, ScriptedTransport transport)
    {
        await session.ConnectAsync();
        await Assert.That(await transport.WaitForSentAsync([Iac, Do, Mxp])).IsTrue();
        transport.SendToClient([Iac, Sb, Mxp, Iac, Se]);
        return transport;
    }

    private static byte[] Line(string text) => Encoding.UTF8.GetBytes(text + "\r\n");

    [Test]
    public async Task ASupportRequestIsAnsweredWithWhatTheParserRenders()
    {
        var transport = new ScriptedTransport { Greeting = [Iac, Will, Mxp] };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        await InMxpModeAsync(session, transport);

        transport.SendToClient(Line("\x1b[1z<SUPPORT image send h1>"));

        await Assert.That(await WaitForTextAsync(transport, "<SUPPORTS")).IsTrue();
        var reply = Reply(transport, "<SUPPORTS");
        await Assert.That(reply).StartsWith("\x1b[1z<SUPPORTS");
        await Assert.That(reply).Contains("+image");
        await Assert.That(reply).Contains("+send");
        await Assert.That(reply).Contains("-h1");
    }

    [Test]
    public async Task ABareSupportRequestIsAnsweredWithEveryTag()
    {
        var transport = new ScriptedTransport { Greeting = [Iac, Will, Mxp] };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        await InMxpModeAsync(session, transport);

        transport.SendToClient(Line("\x1b[1z<SUPPORT>"));

        await Assert.That(await WaitForTextAsync(transport, "<SUPPORTS")).IsTrue();
        var reply = Reply(transport, "<SUPPORTS");
        foreach (var tag in MxpParser.SupportedTags)
        {
            await Assert.That(reply).Contains("+" + tag);
        }
    }

    [Test]
    public async Task AVersionRequestIsAnswered()
    {
        var transport = new ScriptedTransport { Greeting = [Iac, Will, Mxp] };
        await using var session = new TelnetSession(transport, NullLogger.Instance);
        await InMxpModeAsync(session, transport);

        transport.SendToClient(Line("\x1b[1z<VERSION>"));

        await Assert.That(await WaitForTextAsync(transport, "<VERSION")).IsTrue();
        await Assert.That(Reply(transport, "<VERSION")).Contains("CLIENT=SharpMUTerm");
    }

    [Test]
    [Arguments(new[] { "image", "SEND" }, new[] { "image", "SEND" }, new string[0])]
    [Arguments(new[] { "send.expire", "h1" }, new string[0], new[] { "send.expire", "h1" })]
    [Arguments(new[] { "\"image\"" }, new[] { "image" }, new string[0])]
    [Arguments(new[] { "color.fore", "color.size" }, new[] { "color.fore" }, new[] { "color.size" })]
    [Arguments(new[] { "color.*" }, new[] { "color.fore", "color.back" }, new string[0])]
    [Arguments(new[] { "h1.*", "b.*" }, new string[0], new[] { "h1.*" })]
    public async Task AnAnswerNamesWhatWasAskedAbout(string[] asked, string[] supported, string[] unsupported)
    {
        var (yes, no) = MxpParser.AnswerSupport(asked);
        await Assert.That(yes).IsEquivalentTo(supported);
        await Assert.That(no).IsEquivalentTo(unsupported);
    }

    private static async Task<bool> WaitForTextAsync(ScriptedTransport transport, string text)
    {
        for (var i = 0; i < 200; i++)
        {
            if (Encoding.UTF8.GetString(transport.Sent).Contains(text, StringComparison.Ordinal))
            {
                return true;
            }

            await Task.Delay(10);
        }

        return false;
    }

    /// <summary>The reply line that holds <paramref name="start"/>, from the secure-line prefix in front of it.</summary>
    private static string Reply(ScriptedTransport transport, string start)
    {
        var sent = Encoding.UTF8.GetString(transport.Sent);
        var at = sent.IndexOf(start, StringComparison.Ordinal);
        var prefix = sent.LastIndexOf("\x1b[", at, StringComparison.Ordinal);
        var end = sent.IndexOf('\n', at);
        return sent[(prefix < 0 ? at : prefix)..(end < 0 ? sent.Length : end)].TrimEnd('\r');
    }
}
