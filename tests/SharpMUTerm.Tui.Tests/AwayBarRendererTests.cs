using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

public class AwayBarRendererTests
{
    [Test]
    public async Task Bar_CarriesTheLabelInTheAccentAndBothFiguresDim()
    {
        var bar = AwayBarRenderer.Bar(37, TimeSpan.FromMinutes(12), "#c678dd");

        await Assert.That(bar).Contains($"[#c678dd]{Glyphs.Away} {AwayBarRenderer.Label}[/]");

        // Both figures, because a returning reader asks two questions: how much is in front of me, and
        // how far behind am I. A bar carrying one of them answers half.
        await Assert.That(bar).Contains("37 lines");
        await Assert.That(bar).Contains("12 min");
        await Assert.That(bar).Contains("[dim]");
        await Assert.That(bar).Contains("─");
    }

    [Test]
    public async Task Bar_CountsOneLineInTheSingular()
    {
        var bar = AwayBarRenderer.Bar(1, TimeSpan.FromMinutes(3), "#c678dd");

        await Assert.That(bar).Contains("1 line ");
        await Assert.That(bar).DoesNotContain("1 lines");
    }

    [Test]
    public void Bar_RejectsAnEmptyAccent()
    {
        Assert.Throws<ArgumentException>(() => AwayBarRenderer.Bar(4, TimeSpan.FromMinutes(2), string.Empty));
    }

    /// <summary>
    /// The coarsest unit that still decides something. The anchor is the last input event rather than
    /// the moment of departure — focus-out is not observable — so a sub-minute gap must not be dressed
    /// up as "0 min", which would claim a precision the figure does not have.
    /// </summary>
    [Test]
    [Arguments(0, "a moment")]
    [Arguments(59, "a moment")]
    [Arguments(60, "1 min")]
    [Arguments(12 * 60, "12 min")]
    [Arguments(59 * 60, "59 min")]
    [Arguments(60 * 60, "1 h")]
    [Arguments((2 * 60 + 14) * 60, "2 h 14 min")]
    [Arguments(24 * 60 * 60, "1 day")]
    [Arguments(3 * 24 * 60 * 60, "3 days")]
    public async Task Duration_ReadsInTheCoarsestUsefulUnit(int seconds, string expected)
    {
        await Assert.That(AwayBarRenderer.Duration(TimeSpan.FromSeconds(seconds))).IsEqualTo(expected);
    }

    /// <summary>
    /// A negative span is reachable: the anchor is the last input event, and a clock that steps
    /// backwards (an NTP correction, a suspend) can put it after the moment the return arrives. It must
    /// read as "a moment" rather than produce a negative count of minutes.
    /// </summary>
    [Test]
    public async Task Duration_TreatsATimeGoingBackwardsAsAMoment()
    {
        await Assert.That(AwayBarRenderer.Duration(TimeSpan.FromMinutes(-5))).IsEqualTo("a moment");
    }
}
