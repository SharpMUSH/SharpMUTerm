using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Transport;

namespace SharpMUTerm.Core.Tests.Transport;

/// <summary>
/// A world with a WebSocket path is dialled over a WebSocket, end to end: the real session factory, the real
/// <see cref="WebSocketTransport"/>, and a loopback server that speaks telnet in binary frames.
/// </summary>
public class WebSocketWorldTests
{
    [Test]
    public async Task AWorldWithAPathReadsAndWritesTelnetInsideAWebSocket()
    {
        await using var server = await WebSocketServer.StartAsync();
        var world = new WorldDefinition { Name = "Lanternport", Host = "127.0.0.1", Port = server.Port, WebSocketPath = "/telnet" };
        await using var session = new WorldSession(world, null);
        var lines = new List<string>();
        session.LinePrinted += (_, line) =>
        {
            lock (lines)
            {
                lines.Add(line.Text);
            }
        };

        await session.ConnectAsync();
        await server.SendAsync("Welcome to Lanternport.\r\n");
        await Until(() => { lock (lines) { return lines.Contains("Welcome to Lanternport."); } });

        await session.SendCommandAsync("look");
        await Until(() => Encoding.ASCII.GetString(server.Received).Contains("look\r\n", StringComparison.Ordinal));

        await Assert.That(server.Path).IsEqualTo("/telnet");
        await Assert.That(server.SubProtocol).IsEqualTo("telnet.mudstandards.org");
    }

    [Test]
    public async Task AnEvenniaGameIsReachedOverGmcpWithNoTelnetByteSent()
    {
        // Evennia offers no telnet subprotocol: it picks gmcp, sends text in BINARY frames and GMCP in TEXT frames.
        await using var server = await WebSocketServer.StartAsync(pick: "gmcp.mudstandards.org");
        var world = new WorldDefinition { Name = "Synapse", Host = "127.0.0.1", Port = server.Port, WebSocketPath = "/ws" };
        await using var session = new WorldSession(world, null);
        var lines = new List<string>();
        session.LinePrinted += (_, line) =>
        {
            lock (lines)
            {
                lines.Add(line.Text);
            }
        };

        await session.ConnectAsync();
        // Evennia sends each message with no line ending; each is still a line of its own.
        await server.SendAsync("Welcome, runner.\u001b[0m");
        await server.SendTextAsync("Char.Vitals {\"hp\":10,\"maxhp\":12}");
        await server.SendAsync("Type your name.\u001b[0m");
        await server.SendAsync("Name: ");
        await Until(() => { lock (lines) { return lines.Contains("Welcome, runner.") && lines.Contains("Type your name."); } });
        await Until(() => session.GameData.GetText("Char.Vitals.hp") == "10");

        // The client says hello once the server's choice reads as WILL GMCP, and never as a telnet byte.
        await Until(() => server.Text.Any(t => t.StartsWith("Core.Hello ", StringComparison.Ordinal)));
        await session.SendCommandAsync("look");
        await Until(() => Encoding.UTF8.GetString(server.Received).Contains("look\r\n", StringComparison.Ordinal));

        await Assert.That(server.SubProtocol).IsEqualTo("gmcp.mudstandards.org");
        await Assert.That(server.Received).DoesNotContain((byte)0xFF);
        await Assert.That(Encoding.UTF8.GetString(server.Received)).IsEqualTo("look\r\n");
    }

    [Test]
    public async Task TheTransportSaysWhichEndpointTheHandshakeReached()
    {
        await using var server = await WebSocketServer.StartAsync();
        await using var transport = new WebSocketTransport(new ConnectionOptions { Host = "127.0.0.1", Port = server.Port, WebSocketPath = "/ws" });

        await transport.ConnectAsync();

        await Assert.That(transport.IsConnected).IsTrue();
        await Assert.That(transport.RemoteDescription).IsEqualTo($"ws://127.0.0.1:{server.Port}/ws (127.0.0.1:{server.Port})");
    }

    [Test]
    public async Task AWorldKeepaliveShorterThanTheDefaultPingSetsThePing()
    {
        var world = new WorldDefinition { Name = "W", Host = "example.org", Port = 443, WebSocketPath = "/ws", KeepaliveSeconds = 10 };

        await Assert.That(WebSocketTransport.PingInterval(world.ToConnectionOptions().KeepaliveInterval)).IsEqualTo(TimeSpan.FromSeconds(10));
        await Assert.That(WebSocketTransport.PingInterval(TimeSpan.FromMinutes(5))).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(WebSocketTransport.PingInterval(null)).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task AServerThatClosesTheSocketEndsTheConnection()
    {
        await using var server = await WebSocketServer.StartAsync();
        var world = new WorldDefinition { Name = "Lanternport", Host = "127.0.0.1", Port = server.Port, WebSocketPath = "/" };
        await using var session = new WorldSession(world, null);
        var states = new List<ConnectionState>();
        session.StateChanged += (_, e) =>
        {
            lock (states)
            {
                states.Add(e.State);
            }
        };

        await session.ConnectAsync();
        await server.CloseAsync();

        await Until(() => { lock (states) { return states.Contains(ConnectionState.Disconnected); } });
    }

    [Test]
    [Arguments("ws://example.org", "example.org", 80, false, "/")]
    [Arguments("wss://example.org", "example.org", 443, true, "/")]
    [Arguments("WSS://example.org:8443/mud?v=1", "example.org", 8443, true, "/mud?v=1")]
    [Arguments("ws://[2001:db8::1]:4000/", "2001:db8::1", 4000, false, "/")]
    public async Task AnAddressReadsAsItsHostPortTlsAndPath(string text, string host, int port, bool tls, string path)
    {
        var parsed = WebSocketAddress.TryParse(text, out var refusal);

        await Assert.That(refusal).IsNull();
        await Assert.That(parsed).IsEqualTo((host, port, tls, path));
    }

    [Test]
    [Arguments("ws://user:pw@example.org/")]
    [Arguments("ws://example.org/#top")]
    [Arguments("ws://example.org/a b")]
    [Arguments("ws://example.org/café")]
    [Arguments("ws://example.org:99999/")]
    [Arguments("http://example.org/")]
    [Arguments("ws:///")]
    public async Task AnAddressThatCarriesMoreThanAPlaceToDialIsRefused(string text)
    {
        var parsed = WebSocketAddress.TryParse(text, out var refusal);

        await Assert.That(parsed).IsNull();
        await Assert.That(refusal).IsNotNull();
    }

    [Test]
    public async Task APathStartsWithASlashAndHoldsOnlyAddressCharacters()
    {
        await Assert.That(WebSocketAddress.IsPath("/")).IsTrue();
        await Assert.That(WebSocketAddress.IsPath("/ws/telnet?game=1")).IsTrue();
        await Assert.That(WebSocketAddress.IsPath("ws")).IsFalse();
        await Assert.That(WebSocketAddress.IsPath("/a\r\nHost: x")).IsFalse();
        await Assert.That(WebSocketAddress.IsPath("/" + new string('a', WebSocketAddress.MaxPathLength))).IsFalse();
        await Assert.That(WebSocketAddress.Format("2001:db8::1", 443, true, "/m")).IsEqualTo("wss://[2001:db8::1]:443/m");
    }

    [Test]
    public async Task AWorldWithNoPathIsTelnetAndOneWithAPathSaysItsAddress()
    {
        var telnet = new WorldDefinition { Name = "W", Host = "example.org", Port = 4000 };
        var socket = new WorldDefinition { Name = "W", Host = "example.org", Port = 443, UseTls = true, WebSocketPath = "/ws" };

        await Assert.That(telnet.ToConnectionOptions().WebSocketPath).IsNull();
        await Assert.That(socket.ToConnectionOptions().ToString()).IsEqualTo("wss://example.org:443/ws");
        await Assert.That(socket.Clone().WebSocketPath).IsEqualTo("/ws");
    }

    private static async Task Until(Func<bool> predicate)
    {
        for (var i = 0; i < 200 && !predicate(); i++)
        {
            await Task.Delay(20);
        }

        await Assert.That(predicate()).IsTrue();
    }

    /// <summary>One WebSocket connection on loopback, recording what the client sends.</summary>
    private sealed class WebSocketServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly TaskCompletionSource<WebSocket> _socket = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Lock _gate = new();
        private readonly List<byte> _received = [];
        private readonly List<string> _text = [];
        private readonly string? _pick;
        private readonly CancellationTokenSource _cts = new();

        private WebSocketServer(int port, string? pick)
        {
            Port = port;
            _pick = pick;
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        public int Port { get; }

        public string? Path { get; private set; }

        public string? SubProtocol { get; private set; }

        public byte[] Received
        {
            get
            {
                lock (_gate)
                {
                    return [.. _received];
                }
            }
        }

        /// <summary>Text frames the client sent.</summary>
        public string[] Text
        {
            get
            {
                lock (_gate)
                {
                    return [.. _text];
                }
            }
        }

        /// <param name="pick">The subprotocol to pick when the client offers it; the first offered otherwise.</param>
        public static Task<WebSocketServer> StartAsync(string? pick = null)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var server = new WebSocketServer(port, pick);
            server._listener.Start();
            _ = Task.Run(server.AcceptAsync);
            return Task.FromResult(server);
        }

        public async Task SendAsync(string text)
        {
            var socket = await _socket.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await socket.SendAsync(Encoding.ASCII.GetBytes(text), WebSocketMessageType.Binary, true, CancellationToken.None);
        }

        public async Task SendTextAsync(string text)
        {
            var socket = await _socket.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public async Task CloseAsync()
        {
            var socket = await _socket.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }

        private async Task AcceptAsync()
        {
            try
            {
                var context = await _listener.GetContextAsync();
                Path = context.Request.Url?.PathAndQuery;
                var offered = context.Request.Headers["Sec-WebSocket-Protocol"] ?? string.Empty;
                var names = offered.Split(',', StringSplitOptions.TrimEntries);
                SubProtocol = _pick is not null && names.Contains(_pick) ? _pick : names.FirstOrDefault();
                var accepted = await context.AcceptWebSocketAsync(SubProtocol);
                _socket.SetResult(accepted.WebSocket);
                var buffer = new byte[4096];
                var message = new List<byte>();
                while (!_cts.IsCancellationRequested)
                {
                    var result = await accepted.WebSocket.ReceiveAsync(buffer, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    lock (_gate)
                    {
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            message.AddRange(buffer.AsSpan(0, result.Count));
                            if (result.EndOfMessage)
                            {
                                _text.Add(Encoding.UTF8.GetString([.. message]));
                                message.Clear();
                            }
                        }
                        else
                        {
                            _received.AddRange(buffer.AsSpan(0, result.Count));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // The test is over, or the client went away.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            if (_socket.Task.IsCompletedSuccessfully)
            {
                _socket.Task.Result.Abort();
            }

            _listener.Close();
            _cts.Dispose();
        }
    }
}
