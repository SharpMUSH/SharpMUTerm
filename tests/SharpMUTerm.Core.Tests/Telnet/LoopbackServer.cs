using System.Net;
using System.Net.Sockets;

namespace SharpMUTerm.Core.Tests.Telnet;

/// <summary>
/// A socket on loopback that accepts one connection and records every byte written to it, saying
/// nothing back. It exists for the one thing <see cref="ScriptedTransport"/> cannot do: let a
/// <see cref="Core.Session.WorldSession"/> build its <em>own</em> transport and telnet session, so a
/// test reads what a server would have received rather than what an injected double was handed.
/// <para>
/// Silent on purpose. The server that exposed the unsolicited-<c>DO</c> bug negotiated nothing at all,
/// which is the commonest MU* server there is and the case a scripted greeting cannot stand in for.
/// </para>
/// </summary>
internal sealed class LoopbackServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Lock _gate = new();
    private readonly List<byte> _received = [];
    private readonly CancellationTokenSource _cts = new();

    public LoopbackServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptAsync);
    }

    /// <summary>The ephemeral port the listener was given.</summary>
    public int Port { get; }

    /// <summary>Everything the client has written, in order.</summary>
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

    private async Task AcceptAsync()
    {
        try
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            var stream = client.GetStream();
            var buffer = new byte[4096];
            while (!_cts.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, _cts.Token);
                if (read <= 0)
                {
                    return;
                }

                lock (_gate)
                {
                    _received.AddRange(buffer.AsSpan(0, read));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The test finished first, which is the ordinary way this ends.
        }
        catch (Exception)
        {
            // A socket torn down under the reader is likewise the test being over.
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _cts.Dispose();
    }
}
