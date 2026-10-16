using TelnetNegotiationCore.Protocols;

namespace SharpMUTerm.Core.Telnet;

/// <summary>
/// Retains the latest pane dimensions until the server enables NAWS. A successful request before
/// DO NAWS means queued, not written: TNC deliberately skips premature subnegotiations.
/// </summary>
internal sealed class WindowSizeProtocol : NAWSProtocol
{
    private readonly SemaphoreSlim _reportGate = new(1, 1);
    private (int Width, int Height)? _requested;
    private (int Width, int Height)? _reported;

    public async ValueTask ReportAsync(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, MaxWindowDimension);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, MaxWindowDimension);

        await _reportGate.WaitAsync().ConfigureAwait(false);
        try
        {
            _requested = (width, height);
            await ReportIfReadyAsync().ConfigureAwait(false);
        }
        finally
        {
            _reportGate.Release();
        }
    }

    protected override async ValueTask OnNegotiationChangedAsync(bool isNegotiated)
    {
        await _reportGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A new agreement needs its own initial report, even if the pane has not changed.
            if (!isNegotiated)
            {
                _reported = null;
            }
            else
            {
                await ReportIfReadyAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _reportGate.Release();
        }
    }

    private async ValueTask ReportIfReadyAsync()
    {
        if (!IsNegotiated || !WindowSizeReportingEnabled || _requested is not { } size || _reported == size)
        {
            return;
        }

        // TNC owns the binary framing and IAC escaping. Remember success only after the write,
        // so a failed write can be retried with the same dimensions.
        await SendWindowSizeAsync(size.Width, size.Height).ConfigureAwait(false);
        _reported = size;
    }
}
