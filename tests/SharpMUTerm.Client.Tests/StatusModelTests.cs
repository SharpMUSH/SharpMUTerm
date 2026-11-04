using SharpMUTerm.Client.Chrome;

namespace SharpMUTerm.Client.Tests;

/// <summary>The status row as data: two clusters split at the renderer's padding, and runs a click can hand back.</summary>
public class StatusModelTests
{
    private const string Ink = "#cccccc";

    [Test]
    public async Task TheRowSplitsAtItsWidestGapAndDropsTheGap()
    {
        var model = StatusModel.From(
            [new("Corvid · connected  ⌥F freezes", Ink, false), new("        ", Ink, false), new("UTF-8 · ⌃P palette ", Ink, true)],
            "#101010");

        await Assert.That(model.Left.Select(r => r.Text)).IsEquivalentTo(new[] { "Corvid · connected  ⌥F freezes" });
        await Assert.That(model.Right.Select(r => r.Text)).IsEquivalentTo(new[] { "UTF-8 · ⌃P palette" });
        await Assert.That(model.Right.Single().Bold).IsTrue();
        await Assert.That(model.Text).IsEqualTo("Corvid · connected  ⌥F freezes   UTF-8 · ⌃P palette");
    }

    [Test]
    public async Task ARowWithNoWideGapReadsFromTheLeft()
    {
        var model = StatusModel.From([new("  a notice, said once  ", Ink, false)], "#000000");
        await Assert.That(model.Right).IsEmpty();
        await Assert.That(model.Text).IsEqualTo("a notice, said once");
    }

    [Test]
    public async Task MarkSetsTheTargetOnTheLastOccurrenceAndSplitsRuns()
    {
        var runs = StatusModel.Mark([new("⌥R Reconnect · ⌥R Reconnect", Ink, false)], "⌥R Reconnect", "world:reconnect");

        await Assert.That(runs.Select(r => r.Text)).IsEquivalentTo(new[] { "⌥R Reconnect · ", "⌥R Reconnect" });
        await Assert.That(runs[1].Target).IsEqualTo("world:reconnect");
        await Assert.That(StatusModel.From(runs, "#000000").Targets()).IsEquivalentTo(new[] { "world:reconnect" });

        var unchanged = StatusModel.Mark(runs, "not there", "x");
        await Assert.That(unchanged).IsSameReferenceAs(runs);
    }

    [Test]
    public async Task TwoRowsThatSayTheSameAreEqual()
    {
        StatusModel Make() => StatusModel.From([new("left", Ink, false), new("    right", Ink, false)], "#000000");
        await Assert.That(Make()).IsEqualTo(Make());
        await Assert.That(Make()).IsNotEqualTo(StatusModel.Empty);
    }
}
