using System.Text;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Session;

/// <summary>An in-memory <see cref="ITelnetSession"/> for driving <c>WorldSession</c> tests.</summary>
internal sealed class FakeTelnetSession : ITelnetSession
{
    public bool IsConnected { get; private set; }

    public List<string> SentLines { get; } = new();

    /// <summary>What this fake reports as in force; a test that cares sets it and raises the event.</summary>
    public SessionEncoding CurrentEncoding { get; set; } =
        new(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), EncodingSource.Assumed);

    public event EventHandler<TelnetOutputEventArgs>? OutputReceived;
    public event EventHandler<GmcpMessageEventArgs>? GmcpReceived;
    public event EventHandler<MsdpMessageEventArgs>? MsdpReceived;
#pragma warning disable CS0067 // Required by the interface; not exercised by current tests.
    public event EventHandler<MsspReceivedEventArgs>? MsspReceived;
#pragma warning restore CS0067
    public event EventHandler<SessionEncodingEventArgs>? EncodingChanged;
    public event EventHandler<SessionDisconnectedEventArgs>? Disconnected;
    public event EventHandler? MxpEnabled;
    public event EventHandler? PuebloEnabled;
    public event EventHandler? MsdpEnabled;
    public event EventHandler<bool>? MspChanged;
    public event EventHandler? ReceiveBatchEnded;

    public List<(string Variable, string Value)> SentMsdp { get; } = new();

    public List<(string Package, string Json)> SentGmcp { get; } = new();

    /// <summary>When set, the next connect fails with it.</summary>
    public Exception? ConnectFault { get; set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (ConnectFault is { } fault)
        {
            return Task.FromException(fault);
        }

        IsConnected = true;
        return Task.CompletedTask;
    }

    /// <summary>The server goes away: cleanly (an orderly close) with no error, or failed with one.</summary>
    public void Drop(Exception? error = null)
    {
        IsConnected = false;
        Disconnected?.Invoke(this, new SessionDisconnectedEventArgs(error));
    }

    public ValueTask SendLineAsync(string text, CancellationToken cancellationToken = default)
    {
        SentLines.Add(text);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask SendGmcpAsync(string package, string json, CancellationToken cancellationToken = default)
    {
        SentGmcp.Add((package, json));
        return ValueTask.CompletedTask;
    }

    public ValueTask SendMsdpAsync(string variable, string value, CancellationToken cancellationToken = default)
    {
        SentMsdp.Add((variable, value));
        return ValueTask.CompletedTask;
    }

    public ValueTask SetWindowSizeAsync(int width, int height) => ValueTask.CompletedTask;

    public Task DisconnectAsync()
    {
        if (IsConnected)
        {
            IsConnected = false;
            Disconnected?.Invoke(this, new SessionDisconnectedEventArgs(null));
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ---- test drivers ----

    /// <summary>A line, as one read of its own unless <paramref name="endRead"/> is false (more of the same read follows).</summary>
    public void EmitLine(string text, bool endRead = true)
    {
        OutputReceived?.Invoke(this, new TelnetOutputEventArgs(text, isPrompt: false));
        if (endRead)
        {
            EndRead();
        }
    }

    public void EmitPrompt(string text)
    {
        OutputReceived?.Invoke(this, new TelnetOutputEventArgs(text, isPrompt: true));
        EndRead();
    }

    /// <summary>The end of one read from the socket: everything it carried has been raised.</summary>
    public void EndRead() => ReceiveBatchEnded?.Invoke(this, EventArgs.Empty);

    public void EmitGmcp(string package, string json) =>
        GmcpReceived?.Invoke(this, new GmcpMessageEventArgs(package, json));

    public void EmitEncoding(SessionEncoding encoding)
    {
        CurrentEncoding = encoding;
        EncodingChanged?.Invoke(this, new SessionEncodingEventArgs(encoding));
    }

    public void RaiseMxpEnabled() => MxpEnabled?.Invoke(this, EventArgs.Empty);

    public void RaisePuebloEnabled() => PuebloEnabled?.Invoke(this, EventArgs.Empty);

    public void RaiseMsp(bool active) => MspChanged?.Invoke(this, active);

    public void RaiseMsdpEnabled() => MsdpEnabled?.Invoke(this, EventArgs.Empty);

    public void EmitMsdp(string json) => MsdpReceived?.Invoke(this, new MsdpMessageEventArgs(json));
}
