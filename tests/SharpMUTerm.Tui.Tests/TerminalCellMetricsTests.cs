using SharpMUTerm.Core.Telnet;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

public class TerminalCellMetricsTests
{
    [Test]
    public async Task DerivesPhysicalPixelsFromTheTerminalExtent()
    {
        await Assert.That(TerminalCellMetrics.FromWindowSize(120, 40, 1200, 960)).IsEqualTo(new TerminalCellSize(10, 24));
        await Assert.That(TerminalCellMetrics.FromWindowSize(120, 40, 1440, 1200)).IsEqualTo(new TerminalCellSize(12, 30));
    }

    [Test]
    [Arguments(0, 40, 1200, 960)]
    [Arguments(120, 0, 1200, 960)]
    [Arguments(120, 40, 0, 960)]
    [Arguments(120, 40, 1200, 0)]
    [Arguments(120, 40, 1201, 960)]
    public async Task NeverInventsMetricsForMissingOrInexactDimensions(int columns, int rows, int width, int height)
        => await Assert.That(TerminalCellMetrics.FromWindowSize(columns, rows, width, height)).IsNull();

    [Test]
    public async Task PollsPixelOnlyChangesAndStopsOnDisposal()
    {
        TerminalCellSize? size = new(10, 24);
        using var metrics = new TerminalCellMetrics(() => Volatile.Read(ref size));
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        metrics.Changed += () =>
        {
            if (metrics.Current == new TerminalCellSize(12, 30)) { changed.TrySetResult(); }
        };
        metrics.Start();
        Volatile.Write(ref size, new(12, 30));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        metrics.Dispose();
        Volatile.Write(ref size, new(16, 40));
        await Task.Delay(600);
        await Assert.That(metrics.Current).IsEqualTo(new TerminalCellSize(12, 30));
    }

    [Test]
    public async Task PublishesPixelOnlyChangesAndForgetsUnavailableMetrics()
    {
        TerminalCellSize? size = new(10, 24);
        using var metrics = new TerminalCellMetrics(() => size);
        var changes = 0;
        metrics.Changed += () => changes++;
        metrics.Sample();
        metrics.Sample();
        size = new(12, 30);
        metrics.Sample();
        await Assert.That(metrics.Current).IsEqualTo(size);
        size = null;
        metrics.Sample();
        await Assert.That(metrics.Current).IsNull();
        await Assert.That(changes).IsEqualTo(3);
        metrics.Dispose();
        size = new(10, 24);
        metrics.Sample();
        metrics.Start();
        await Assert.That(changes).IsEqualTo(3);
    }
}
