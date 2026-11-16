using System.Net.WebSockets;
using TelnetNegotiationCore.Connections;
using TelnetNegotiationCore.WebSockets;

namespace SharpMUTerm.Core.Transport;

/// <summary>
/// A world reached over a WebSocket (<see cref="ConnectionOptions.WebSocketPath"/>). TelnetNegotiationCore connects the
/// socket, offering <c>telnet.mudstandards.org</c> and <c>telnet</c>, then <c>gmcp.mudstandards.org</c> and
/// <c>terminal.mudstandards.org</c>, and its own transport runs the session over whichever the server picked
/// (<see cref="ForTelnet"/>). Evennia offers no telnet and picks <c>gmcp</c>: text and GMCP only, with no NAWS, TTYPE,
/// CHARSET, echo or MXP, and the library drops every telnet byte this client writes before it reaches the server.
/// </summary>
public sealed class WebSocketTransport(ConnectionOptions options) : ITransport
{
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultPing = TimeSpan.FromSeconds(30);

    private readonly ConnectionOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private ClientWebSocket? _socket;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    public string? RemoteDescription { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_socket is not null)
        {
            throw new InvalidOperationException("Transport is already connected.");
        }

        if (!WebSocketAddress.IsPath(_options.WebSocketPath))
        {
            throw new InvalidOperationException("The world's WebSocket path is not one this client dials.");
        }

        var address = new Uri(WebSocketAddress.Format(_options.Host, _options.Port, _options.UseTls, _options.WebSocketPath!));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ConnectTimeout);
        _socket = await TelnetWebSocketClient.ConnectAsync(
            address,
            socket =>
            {
                // A quiet connection behind a proxy is kept open, and one that stops answering is noticed.
                socket.KeepAliveInterval = PingInterval(_options.KeepaliveInterval);
                socket.KeepAliveTimeout = TimeSpan.FromSeconds(30);
                if (_options.AllowInvalidCertificates)
                {
                    socket.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                }
            },
            timeout.Token).ConfigureAwait(false);
        RemoteDescription = address.ToString();
    }

    /// <summary>
    /// 30 seconds, or the world's keepalive when that is shorter: a ping is the only keepalive a gmcp or terminal
    /// socket carries.
    /// </summary>
    public static TimeSpan PingInterval(TimeSpan? keepalive) =>
        keepalive is { } wanted && wanted < DefaultPing ? wanted : DefaultPing;

    public ITelnetTransport ForTelnet(int receiveBufferSize) =>
        TelnetTransport.WebSocketClient(_socket ?? throw new InvalidOperationException("Transport is not connected."));

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        var socket = _socket ?? throw new InvalidOperationException("Transport is not connected.");
        await socket.SendAsync(data, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var socket = _socket ?? throw new InvalidOperationException("Transport is not connected.");
        try
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            return result.MessageType == WebSocketMessageType.Close ? 0 : result.Count;
        }
        catch (WebSocketException)
        {
            return 0;
        }
    }

    public async Task CloseAsync()
    {
        if (_socket is not { } socket)
        {
            return;
        }

        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var wait = new CancellationTokenSource(CloseWait);
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, wait.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort close.
        }
        finally
        {
            socket.Abort();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        _socket?.Dispose();
        _socket = null;
    }
}
