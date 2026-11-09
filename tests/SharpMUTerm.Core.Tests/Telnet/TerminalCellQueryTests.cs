using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Telnet;

public class TerminalCellQueryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AnswersBeforeAnyNewlineWithHeightThenWidthAndNoTerminator(bool fragmented)
    {
        var transport = new ScriptedTransport { Fragmented = fragmented };
        await using var session = new TelnetSession(transport, NullLogger.Instance)
        {
            TerminalCellSizeProvider = () => new(10, 24),
        };
        await session.ConnectAsync();
        var offset = transport.Sent.Length;
        transport.SendToClient("\u001b[16t"u8.ToArray());
        await Until(() => transport.Sent.Length > offset);
        await Assert.That(Encoding.ASCII.GetString(transport.Sent[offset..])).IsEqualTo("\u001b[6;24;10t");
        await session.SendLineAsync("look");
        await Until(() => Encoding.ASCII.GetString(transport.Sent).Contains("look\r\n"));
        await Assert.That(Encoding.ASCII.GetString(transport.Sent[offset..])).IsEqualTo("\u001b[6;24;10tlook\r\n");
    }

    [Test]
    [Arguments("\u001b]title\r\0\u001b[16t\a")]
    [Arguments("\u001bPdata\r\0\u001b[16t\u001b\\")]
    [Arguments("\u001b^data\u001b[16t\u001b\\")]
    [Arguments("\u001b]title\u001b[16t\a")]
    [Arguments("\u001bPdata\u001b[16t\u001b\\")]
    [Arguments("\u001b_Gpayload\u001b[16t\u001b\\")]
    [Arguments("\u001bXdata\u001b[16t\u001b\\")]
    [Arguments("\u001b[?16t\u001b[16;1t\u001b[016t\u001b[16 t")]
    public async Task NeverAnswersControlStringContentsOrDifferentCommands(string input)
    {
        var transport = new ScriptedTransport { Fragmented = true };
        await using var session = new TelnetSession(transport, NullLogger.Instance)
        {
            TerminalCellSizeProvider = () => new(10, 24),
        };
        await session.ConnectAsync();
        var offset = transport.Sent.Length;
        await DeliverLine(session, transport, input + "done");
        await Assert.That(transport.Sent.Length).IsEqualTo(offset);
        transport.SendToClient("\u001b[16t"u8.ToArray());
        await Until(() => transport.Sent.Length > offset);
        await Assert.That(Encoding.ASCII.GetString(transport.Sent[offset..])).IsEqualTo("\u001b[6;24;10t");
    }

    [Test]
    public async Task OnlyRefreshesAPeerThatAskedAndOnlyForChangedMeasuredPixels()
    {
        TerminalCellSize? size = new(10, 24);
        var transport = new ScriptedTransport();
        await using var session = new TelnetSession(transport, NullLogger.Instance)
        {
            TerminalCellSizeProvider = () => size,
        };
        await session.ConnectAsync();
        var offset = transport.Sent.Length;
        await session.RefreshTerminalCellSizeAsync();
        await Assert.That(transport.Sent.Length).IsEqualTo(offset);
        transport.SendToClient("\u001b[16t"u8.ToArray());
        await Until(() => transport.Sent.Length > offset);
        offset = transport.Sent.Length;
        await session.RefreshTerminalCellSizeAsync();
        await Assert.That(transport.Sent.Length).IsEqualTo(offset);
        size = new(16, 40);
        await session.RefreshTerminalCellSizeAsync();
        await Assert.That(Encoding.ASCII.GetString(transport.Sent[offset..])).IsEqualTo("\u001b[6;40;16t");
        offset = transport.Sent.Length;
        size = null;
        await session.RefreshTerminalCellSizeAsync();
        size = new(0, 24);
        await session.RefreshTerminalCellSizeAsync();
        size = new(10, 65536);
        await session.RefreshTerminalCellSizeAsync();
        await Assert.That(transport.Sent.Length).IsEqualTo(offset);
    }

    [Test]
    public async Task UnknownMetricsDoNotInventAReplyAndCanBecomeAvailableLater()
    {
        TerminalCellSize? size = null;
        var transport = new ScriptedTransport();
        await using var session = new TelnetSession(transport, NullLogger.Instance) { TerminalCellSizeProvider = () => size };
        await session.ConnectAsync();
        var offset = transport.Sent.Length;
        await DeliverLine(session, transport, "\u001b[16tdone");
        await Assert.That(transport.Sent.Length).IsEqualTo(offset);
        size = new(10, 24);
        await session.RefreshTerminalCellSizeAsync();
        await Assert.That(Encoding.ASCII.GetString(transport.Sent[offset..])).IsEqualTo("\u001b[6;24;10t");
    }

    [Test]
    public async Task ObservingQueriesPreservesTheOriginalApplicationBytes()
    {
        var transport = new ScriptedTransport { Fragmented = true };
        await using var session = new TelnetSession(transport, NullLogger.Instance,
            new TelnetSessionOptions { EncodingOverride = Encoding.Latin1 })
        {
            TerminalCellSizeProvider = () => new(10, 24),
        };
        await session.ConnectAsync();
        const string input = "caf\u00e9\u001b[31m red\u001b[0m\u001b[16t";
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, e) => completion.TrySetResult(e.Text);
        transport.SendToClient(Encoding.Latin1.GetBytes(input + "\r\n"));
        var output = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(output).IsEqualTo(input);
    }

    [Test]
    public async Task QueriesInsideTelnetSubnegotiationDoNotReachTheObserver()
    {
        var transport = new ScriptedTransport();
        await using var session = new TelnetSession(transport, NullLogger.Instance) { TerminalCellSizeProvider = () => new(10, 24) };
        await session.ConnectAsync();
        var offset = transport.Sent.Length;
        transport.SendToClient([255, 250, 200, 27, 91, 49, 54, 116, 255, 240]);
        await DeliverLine(session, transport, "done");
        await Assert.That(transport.Sent.Length).IsEqualTo(offset);
    }

    [Test]
    public async Task ALineBoundaryRecoversFromAnUnterminatedControlString()
    {
        var transport = new ScriptedTransport { Fragmented = true };
        await using var session = new TelnetSession(transport, NullLogger.Instance) { TerminalCellSizeProvider = () => new(10, 24) };
        await session.ConnectAsync();
        await DeliverLine(session, transport, "\u001b]unterminated");
        var offset = transport.Sent.Length;
        transport.SendToClient("\u001b[16t"u8.ToArray());
        await Until(() => transport.Sent.Length > offset);
        await Assert.That(Encoding.ASCII.GetString(transport.Sent[offset..])).IsEqualTo("\u001b[6;24;10t");
        await session.DisconnectAsync();
        offset = transport.Sent.Length;
        await session.RefreshTerminalCellSizeAsync();
        await Assert.That(transport.Sent.Length).IsEqualTo(offset);
    }

    [Test]
    public async Task AGoAheadPromptBoundaryRecoversAnUnfinishedControlString()
    {
        var transport = new ScriptedTransport { Fragmented = true };
        await using var session = new TelnetSession(transport, NullLogger.Instance) { TerminalCellSizeProvider = () => new(10, 24) };
        var prompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, e) => { if (e.IsPrompt) prompt.TrySetResult(); };
        await session.ConnectAsync();
        transport.SendToClient([.. "\u001b]unfinished"u8.ToArray(), 255, 249]);
        await prompt.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var offset = transport.Sent.Length;
        transport.SendToClient("\u001b[16t"u8.ToArray());
        await Until(() => transport.Sent.Length > offset);
        await Assert.That(Encoding.ASCII.GetString(transport.Sent[offset..])).IsEqualTo("\u001b[6;24;10t");
    }

    private static async Task DeliverLine(TelnetSession session, ScriptedTransport transport, string text)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnOutput(object? sender, TelnetOutputEventArgs e) => completion.TrySetResult();
        session.OutputReceived += OnOutput;
        try
        {
            transport.SendToClient(Encoding.ASCII.GetBytes(text + "\r\n"));
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            session.OutputReceived -= OnOutput;
        }
    }

    private static async Task Until(Func<bool> predicate)
    {
        for (var i = 0; i < 250 && !predicate(); i++)
        {
            await Task.Delay(10);
        }

        await Assert.That(predicate()).IsTrue();
    }
}
