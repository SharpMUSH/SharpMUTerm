using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Text;

/// <summary>
/// ⌃F's older-history walk (⌥H): newest first, across the ring and the file-backed spill, in ranged reads, skipping what
/// the pane holds, stopping at the cap, on cancellation and on a pattern that does not parse.
/// </summary>
public class ScrollbackSearchTests
{
    private static ScrollbackBuffer Buffer(int lines, int capacity = 10_000, IScrollbackSpill? spill = null, Func<int, string>? text = null)
    {
        var buffer = new ScrollbackBuffer(capacity, spill);
        for (var i = 0; i < lines; i++)
        {
            buffer.Append(StyledLine.FromText(text?.Invoke(i) ?? $"line {i}", TextStyle.Default));
        }

        return buffer;
    }

    [Test]
    public async Task HitsAreListedNewestFirstWithTheirAbsoluteIndex()
    {
        var buffer = Buffer(100, text: i => i % 30 == 0 ? $"goblin {i}" : $"street {i}");

        var result = ScrollbackSearch.Run(buffer, "GOBLIN", false, null, null, CancellationToken.None, chunk: 7);

        await Assert.That(result!.Done).IsTrue();
        await Assert.That(result.Found.Select(m => m.Index).ToArray()).IsEquivalentTo(new long[] { 90, 60, 30, 0 });
        await Assert.That(result.Found[0].Line.Text).IsEqualTo("goblin 90");
        await Assert.That(result.Found[0].MatchStart).IsEqualTo(0);
        await Assert.That(result.Found[0].MatchLength).IsEqualTo(6);
        await Assert.That(result.Searched).IsEqualTo(100);
    }

    [Test]
    public async Task LinesThePaneHoldsAreSkipped()
    {
        var buffer = Buffer(10, text: i => $"goblin {i}");
        var held = buffer.GetRange(5, 5).ToHashSet(ReferenceEqualityComparer.Instance);

        var result = ScrollbackSearch.Run(buffer, "goblin", false, line => held.Contains(line), null, CancellationToken.None);

        await Assert.That(result!.Found.Select(m => m.Index).ToArray()).IsEquivalentTo(new long[] { 4, 3, 2, 1, 0 });
    }

    [Test]
    public async Task ProgressIsReportedAfterEveryChunk()
    {
        var buffer = Buffer(25, text: i => $"goblin {i}");
        var reports = new List<ScrollbackSearchProgress>();

        ScrollbackSearch.Run(buffer, "goblin", false, null, reports.Add, CancellationToken.None, chunk: 10);

        await Assert.That(reports.Count).IsEqualTo(3);
        await Assert.That(reports[0].Found.Count).IsEqualTo(10);
        await Assert.That(reports[0].Done).IsFalse();
        await Assert.That(reports[^1].Done).IsTrue();
    }

    [Test]
    public async Task ACancelledWalkReportsNothingFurther()
    {
        var buffer = Buffer(50, text: i => $"goblin {i}");
        using var cancel = new CancellationTokenSource();
        var reports = 0;

        var result = ScrollbackSearch.Run(buffer, "goblin", false, null, _ => { reports++; cancel.Cancel(); }, cancel.Token, chunk: 10);

        await Assert.That(result).IsNull();
        await Assert.That(reports).IsEqualTo(1);
    }

    [Test]
    public async Task TheWalkStopsAtTheCapAndSaysSo()
    {
        var buffer = Buffer(ScrollbackSearch.MaxHits + 200, text: i => $"goblin {i}");

        var result = ScrollbackSearch.Run(buffer, "goblin", false, null, null, CancellationToken.None, chunk: 64);

        await Assert.That(result!.Capped).IsTrue();
        await Assert.That(result.Found.Count).IsEqualTo(ScrollbackSearch.MaxHits);
    }

    [Test]
    public async Task APatternThatDoesNotParseIsAnErrorNotAThrow()
    {
        var buffer = Buffer(10);

        var result = ScrollbackSearch.Run(buffer, "gob(lin", true, null, null, CancellationToken.None);

        await Assert.That(result!.Error).IsNotNull();
        await Assert.That(result.Found.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TheSpillBehindTheRingIsSearchedToo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"smuterm-deep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var spill = new FileScrollbackSpill(new ScrollbackSpillOptions { Directory = root });
            var buffer = Buffer(3_000, capacity: 200, spill: spill, text: i => i == 17 ? "the old goblin" : $"street {i}");

            var result = ScrollbackSearch.Run(buffer, "goblin", false, null, null, CancellationToken.None, chunk: 500);

            // Line 17 left the 200-line ring long ago: only the spill holds it.
            await Assert.That(buffer.OldestIndex).IsLessThanOrEqualTo(17);
            await Assert.That(result!.Found.Select(m => m.Index).ToArray()).IsEquivalentTo(new long[] { 17 });
            await Assert.That(result.Searched).IsEqualTo(buffer.AvailableLines);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
