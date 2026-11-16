using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
/// The handshake runs through a handler of this transport's own, so the socket is dialled with no delay, the
/// certificate is checked by the same rule <see cref="TcpTransport"/> uses, and the endpoint reached is the one
/// <see cref="RemoteDescription"/> names (through a proxy, that is the proxy).
/// </summary>
public sealed class WebSocketTransport(ConnectionOptions options) : ITransport
{
    private static readonly TimeSpan CloseWait = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultPing = TimeSpan.FromSeconds(30);

    private readonly ConnectionOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private ClientWebSocket? _socket;
    private HttpMessageInvoker? _invoker;

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
        EndPoint? reached = null;
        var handler = new SocketsHttpHandler
        {
            // A redirect is a second address nobody configured.
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, token).ConfigureAwait(false);
                    // A dual-mode socket reports an IPv4 peer as ::ffff:a.b.c.d; say it as the address it is.
                    reached = socket.RemoteEndPoint is IPEndPoint { Address.IsIPv4MappedToIPv6: true } mapped
                        ? new IPEndPoint(mapped.Address.MapToIPv4(), mapped.Port)
                        : socket.RemoteEndPoint;
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None || _options.AllowInvalidCertificates;
        _invoker = new HttpMessageInvoker(handler);
        _socket = await TelnetWebSocketClient.ConnectAsync(
            address,
            _invoker,
            socket =>
            {
                // A quiet connection behind a proxy is kept open, and one that stops answering is noticed.
                socket.KeepAliveInterval = PingInterval(_options.KeepaliveInterval);
                socket.KeepAliveTimeout = TimeSpan.FromSeconds(30);
            },
            bridges: null,
            timeout.Token).ConfigureAwait(false);
        RemoteDescription = reached is null ? address.ToString() : $"{address} ({reached})";
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
        _invoker?.Dispose();
        _invoker = null;
    }
}
