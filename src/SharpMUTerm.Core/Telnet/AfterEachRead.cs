namespace SharpMUTerm.Core.Telnet;

/// <summary>
/// Runs <paramref name="afterRead"/> once the interpreter has taken each read, on the read loop, before the next read.
/// </summary>
/// <remarks>
/// This is the batch boundary <see cref="TelnetSession.ReceiveBatchEnded"/> reports, and it is taken here rather than
/// from the library's <c>OnInputProcessed</c> hook on purpose. That hook waits for the interpreter's queue by polling
/// with <c>Task.Delay(1)</c> after every read: about a millisecond a read on Linux and the timer's 15.6 ms on Windows,
/// so a server writing a byte at a time (or a busy one) is delayed by that per read, and the fragmented telnet tests
/// ran out of time on Windows. The read loop this replaced called the same callbacks straight after
/// <c>InterpretByteArrayAsync</c> with no wait, and this keeps that.
/// </remarks>
internal sealed class AfterEachRead(TelnetNegotiationCore.Connections.ITelnetTransport inner, Action afterRead)
    : TelnetNegotiationCore.Connections.ITelnetTransport
{
    public Task ReadAsync(Func<ReadOnlyMemory<byte>, ValueTask> onData, CancellationToken cancellationToken) =>
        inner.ReadAsync(
            async data =>
            {
                await onData(data).ConfigureAwait(false);
                afterRead();
            },
            cancellationToken);

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken) =>
        inner.WriteAsync(data, cancellationToken);

    public ValueTask CloseAsync(CancellationToken cancellationToken) => inner.CloseAsync(cancellationToken);
}
