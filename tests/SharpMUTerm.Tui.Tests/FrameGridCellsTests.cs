using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Direct coverage of <see cref="FrameGrid.Cells"/> — the walker every contrast assertion goes
/// through, driven with hand-written frames rather than rendered ones.
/// <para>
/// It is worth its own file because this parser's failure mode is silence. A walker that mistracks
/// colour, or decodes nothing at all, does not throw: it hands back cells nobody painted, or no cells,
/// and an audit over either passes. So the sequences it must get right are asserted here, on frames
/// small enough to read, instead of being inferred from a suite staying green.
/// </para>
/// <para>
/// <b>None of the reset forms below appear in a frame this driver emits</b> — it always writes an
/// explicit <c>0;38;2;…;48;2;…</c>. They are covered because they are legal, because this walker is
/// shared, and because the cost of finding out otherwise is a suite that has been reading frames wrong
/// for a while without anything going red.
/// </para>
/// </summary>
public class FrameGridCellsTests
{
    private const int Width = 20;
    private const int Height = 3;

    private static string At(int row, int column) => $"[{row};{column}H";

    private static string Fg(int r, int g, int b) => $"[38;2;{r};{g};{b}m";

    private static string Bg(int r, int g, int b) => $"[48;2;{r};{g};{b}m";

    private static FrameGrid.Cell Cell(string frame, int row, int column) =>
        FrameGrid.Cells(frame, Width, Height)[(row, column)];

    [Test]
    public async Task AGlyphCarriesTheColoursInForceWhenItWasWritten()
    {
        var frame = At(1, 1) + Fg(1, 2, 3) + Bg(4, 5, 6) + "x";

        var cell = Cell(frame, 0, 0);

        await Assert.That(cell.Glyph).IsEqualTo('x');
        await Assert.That(cell.Foreground).IsEqualTo(new Rgb(1, 2, 3));
        await Assert.That(cell.Background).IsEqualTo(new Rgb(4, 5, 6));
    }

    [Test]
    public async Task AnEmptySgrIsAReset()
    {
        // ECMA-48: `CSI m` is `CSI 0 m`. Splitting an empty parameter string yields no codes, so a loop
        // over them runs no body — and the colours from the previous span stay standing, which makes
        // every glyph after it read as painted in colours it is not wearing.
        var frame = At(1, 1) + Fg(1, 2, 3) + Bg(4, 5, 6) + "a" + "[m" + "b";

        await Assert.That(Cell(frame, 0, 1).Glyph).IsEqualTo('b');
        await Assert.That(Cell(frame, 0, 1).Foreground).IsNull();
        await Assert.That(Cell(frame, 0, 1).Background).IsNull();
    }

    [Test]
    public async Task ThirtyNineClearsTheForegroundAndLeavesTheBackground()
    {
        var frame = At(1, 1) + Fg(1, 2, 3) + Bg(4, 5, 6) + "a" + "[39m" + "b";

        var cell = Cell(frame, 0, 1);

        await Assert.That(cell.Foreground).IsNull();
        await Assert.That(cell.Background).IsEqualTo(new Rgb(4, 5, 6));
    }

    [Test]
    public async Task FortyNineClearsTheBackgroundAndLeavesTheForeground()
    {
        var frame = At(1, 1) + Fg(1, 2, 3) + Bg(4, 5, 6) + "a" + "[49m" + "b";

        var cell = Cell(frame, 0, 1);

        await Assert.That(cell.Foreground).IsEqualTo(new Rgb(1, 2, 3));
        await Assert.That(cell.Background).IsNull();
    }

    [Test]
    public async Task AFortyNineInsideATruecolorTripleIsNotAReset()
    {
        // `38;2;49;5;6` is a *foreground* whose red channel is 49. Backgrounds tested for the reset code
        // with `parameters.Contains("49")`, which reads that as "return the background to default" and
        // clears a background the sequence never mentioned. It survived unnoticed because the branch
        // below it re-set the background whenever the same sequence also carried a `48;2;` — so only a
        // foreground-only span could show it, and this driver does not emit one.
        var frame = At(1, 1) + Bg(4, 5, 6) + "a" + "[38;2;49;5;6m" + "b";

        await Assert.That(FrameGrid.Backgrounds(frame)[(0, 1)]).IsEqualTo("48;2;4;5;6");
    }

    [Test]
    public async Task BackgroundsHonoursAStandaloneFortyNine()
    {
        // The other half of the same line: a real 49 must still clear the background.
        var frame = At(1, 1) + Bg(4, 5, 6) + "a" + "[49m" + "b";

        await Assert.That(FrameGrid.Backgrounds(frame)[(0, 1)]).IsNull();
    }

    [Test]
    public async Task ACellPaintedTwiceReportsTheLastColourWrittenToIt()
    {
        // The property that makes this a *grid* and not a stream, and the whole reason the contrast
        // audit goes through it: what is audited is what is left on screen, not everything the driver
        // wrote on the way there.
        var frame = At(1, 1) + Fg(1, 2, 3) + Bg(4, 5, 6) + "a"
            + At(1, 1) + Fg(7, 8, 9) + Bg(10, 11, 12) + "z";

        var cells = FrameGrid.Cells(frame, Width, Height);

        await Assert.That(cells[(0, 0)].Glyph).IsEqualTo('z');
        await Assert.That(cells[(0, 0)].Foreground).IsEqualTo(new Rgb(7, 8, 9));
        await Assert.That(cells.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AGlyphPastTheEdgeIsDroppedRatherThanThrowing()
    {
        var frame = At(1, 1) + Fg(1, 2, 3) + Bg(4, 5, 6) + new string('x', Width + 10);

        var cells = FrameGrid.Cells(frame, Width, Height);

        await Assert.That(cells.Count).IsEqualTo(Width);
    }

    [Test]
    public async Task AnUndecodedIndexedColourLeavesTheColourAloneRatherThanCorruptingIt()
    {
        // 38;5;n is legal and this driver never writes it. What matters is that meeting one degrades —
        // the code and its arguments fall through as unknowns — rather than being read as a truecolor
        // triple and painting a cell in a colour nothing chose.
        var frame = At(1, 1) + Fg(1, 2, 3) + Bg(4, 5, 6) + "a" + "[38;5;200m" + "b";

        await Assert.That(Cell(frame, 0, 1).Foreground).IsEqualTo(new Rgb(1, 2, 3));
    }
}
