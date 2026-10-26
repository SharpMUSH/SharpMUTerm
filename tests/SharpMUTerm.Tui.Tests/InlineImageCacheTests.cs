using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A room picture a game re-sends on every <c>look</c> is fetched and decoded once a session — at any pane
/// width, and however many looks land before the first fetch is back.
/// </summary>
public class InlineImageCacheTests
{
    private const string Url = "https://mud.example/room.png";

    private static readonly InlineImageRequest Room = new(Url, "room.png");

    private sealed class Counting
    {
        public int Fetches;

        public Func<string, CancellationToken, Task<byte[]?>> Fetch(Task<byte[]?> bytes) => (_, _) =>
        {
            Interlocked.Increment(ref Fetches);
            return bytes;
        };
    }

    private static async Task<List<IReadOnlyList<string>>> Show(InlineImageHost host, params int[] widths)
    {
        var drawn = new List<IReadOnlyList<string>>();
        foreach (var width in widths)
        {
            host.Load([Room], InlineImagePresentation.HalfBlock, width, () => null, (_, rows) => drawn.Add(rows));
        }

        await host.Pending;
        return drawn;
    }

    [Test]
    public async Task APictureShownAtAnotherWidthIsNotFetchedAgain()
    {
        var count = new Counting();
        using var host = new InlineImageHost(count.Fetch(Task.FromResult<byte[]?>(TestImages.RgbaPng(320, 160))), run => run());

        var first = await Show(host, 40);
        var second = await Show(host, 20);

        await Assert.That(count.Fetches).IsEqualTo(1);
        await Assert.That(first.Single()[0].Count(c => c == '▀')).IsEqualTo(40);
        await Assert.That(second.Single()[0].Count(c => c == '▀')).IsEqualTo(20);
    }

    [Test]
    public async Task LooksThatArriveBeforeTheFetchIsBackShareIt()
    {
        var count = new Counting();
        var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = new InlineImageHost(count.Fetch(gate.Task), run => run());

        var drawn = new List<IReadOnlyList<string>>();
        for (var look = 0; look < 3; look++)
        {
            host.Load([Room], InlineImagePresentation.HalfBlock, 40, () => null, (_, rows) => { lock (drawn) { drawn.Add(rows); } });
        }

        gate.SetResult(TestImages.RgbaPng(64, 32));
        await host.Pending;

        await Assert.That(count.Fetches).IsEqualTo(1);
        await Assert.That(drawn.Count).IsEqualTo(3);
    }

    [Test]
    public async Task AFailedFetchIsTriedAgainNextTime()
    {
        var calls = 0;
        using var host = new InlineImageHost(
            (_, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1 ? null : TestImages.RgbaPng(64, 32)),
            run => run());

        var first = await Show(host, 40);
        var second = await Show(host, 40);

        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(first.Count).IsEqualTo(0);
        await Assert.That(second.Count).IsEqualTo(1);
    }

    [Test]
    public async Task APictureWithinTheBudgetIsKept()
    {
        using var host = new InlineImageHost(
            (_, _) => Task.FromResult<byte[]?>(TestImages.RgbaPng(64, 32)),
            run => run(),
            sourceCacheBytes: 64 * 32 * 4);

        await Show(host, 40);

        await Assert.That(host.SourcesHeld).IsEqualTo(1);
    }

    [Test]
    public async Task APictureLargerThanTheWholeBudgetIsNotKept()
    {
        using var host = new InlineImageHost(
            (_, _) => Task.FromResult<byte[]?>(TestImages.RgbaPng(64, 32)),
            run => run(),
            sourceCacheBytes: 64 * 32 * 4 - 1);

        var drawn = await Show(host, 40);

        await Assert.That(drawn.Count).IsEqualTo(1);
        await Assert.That(host.SourcesHeld).IsEqualTo(0);
    }
}
