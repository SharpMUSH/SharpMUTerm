using SharpMUTerm.Client.Chrome;

namespace SharpMUTerm.Client.Tests;

/// <summary>The status row as data: segments by role, laid out to the row's width, and runs a click can hand back.</summary>
public class StatusModelTests
{
    private const string Dim = "#888888";

    private static StatusSegment Seg(StatusRole role, params StatusRun[] runs) => new(role, runs);

    private static StatusModel Row(int width) => new(
        [Seg(StatusRole.Identity, new("Corvid", null, true), new(" ", null, false), new("connected", Dim, false))],
        [Seg(StatusRole.Encoding, new StatusRun("utf-8", Dim, false)), Seg(StatusRole.Palette, new StatusRun("⌃P palette", Dim, false, HeaderModel.PaletteTarget))],
        "#101010",
        "#e0e0e0")
    {
        Width = width,
    };

    [Test]
    public async Task TheClustersReadWithASeparatorBetweenSegments()
    {
        var model = Row(60);
        await Assert.That(model.Text).IsEqualTo("Corvid connected   utf-8   ⌃P palette");
        await Assert.That(model.Right.Select(r => r.Text)).IsEquivalentTo(new[] { "utf-8", StatusModel.Separator, "⌃P palette" });
        await Assert.That(StatusModel.WidthOf(model.RightSegments)).IsEqualTo(18);
    }

    [Test]
    public async Task TheRowPushesTheRightClusterToItsWidth()
    {
        var row = Row(60).Row();
        await Assert.That(string.Concat(row.Select(r => r.Text)).Length).IsEqualTo(60);
        await Assert.That(row[^1].Target).IsEqualTo(HeaderModel.PaletteTarget);

        // Narrower than both clusters: the gap is never less than its minimum.
        var tight = string.Concat(Row(10).Row().Select(r => r.Text));
        await Assert.That(tight).Contains("connected" + new string(' ', StatusModel.MinGap) + "utf-8");
    }

    [Test]
    public async Task FitCutsTheRowEndingInTheInkItCutInto()
    {
        var fit = Row(60).Fit(12);
        await Assert.That(string.Concat(fit.Select(r => r.Text))).IsEqualTo("Corvid conn…");
        await Assert.That(fit[^1].Ink).IsEqualTo(Dim);
        await Assert.That(Row(60).Fit(60)).IsEquivalentTo(Row(60).Row());
    }

    [Test]
    public async Task TargetsAreWhatTheClientPutOnRuns()
    {
        await Assert.That(Row(60).Targets()).IsEquivalentTo(new[] { HeaderModel.PaletteTarget });
    }

    [Test]
    public async Task TwoRowsThatSayTheSameAreEqual()
    {
        await Assert.That(Row(60)).IsEqualTo(Row(60));
        await Assert.That(Row(60)).IsNotEqualTo(Row(61));
        await Assert.That(Row(60)).IsNotEqualTo(StatusModel.Empty);
    }
}
