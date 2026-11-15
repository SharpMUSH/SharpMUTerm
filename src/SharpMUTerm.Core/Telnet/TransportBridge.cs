using SharpMUTerm.Core.Transport;

namespace SharpMUTerm.Core.Telnet;

/// <summary>
/// Hands one of our <see cref="ITransport"/>s to TelnetNegotiationCore as the transport its read loop runs on
/// (<c>TelnetInterpreterBuilder.StartAsync</c>). The read loop is the library's, so its start hook
/// (<c>OnStarting</c>) applies; the socket, TLS and the test fakes stay ours.
/// </summary>
internal sealed class TransportBridge(ITransport transport, int receiveBufferSize)
    : TelnetNegotiationCore.Connections.ITelnetTransport
{
    public async Task ReadAsync(Func<ReadOnlyMemory<byte>, ValueTask> onData, CancellationToken cancellationToken)
    {
        var buffer = new byte[receiveBufferSize];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await transport.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return; // the peer closed
            }

            await onData(buffer.AsMemory(0, read)).ConfigureAwait(false);
        }
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
        // Not the token: the library passes its start token, which a disconnect cancels before the socket closes,
        // and a write already under way should finish as it did when this loop was ours.
        transport.SendAsync(data, CancellationToken.None);

    public async ValueTask CloseAsync(CancellationToken cancellationToken) =>
        await transport.CloseAsync().ConfigureAwait(false);
}
