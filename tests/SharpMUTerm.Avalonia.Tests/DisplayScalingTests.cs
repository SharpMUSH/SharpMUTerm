using Avalonia;
using Avalonia.Headless;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window at the display scalings Windows runs it at (125%, 150%): a first size that fits the screen, the field
/// reaching past the grid's leftover pixels, and a footer that shortens rather than cuts.
/// </summary>
public class DisplayScalingTests
{
    private static readonly PixelRect FullHd = new(0, 0, 1920, 1032);

    [Test]
    [Arguments(1.25)]
    [Arguments(1.5)]
    public async Task TheDefaultWindowFitsA1080pScreenByWholeCells(double scaling)
    {
        // 120×36 cells of 10×22 around the desktop chrome: 1,230 × 850 logical, taller than 1080p at 150%.
        var cell = new Size(10, 22);
        var chrome = new Size(252, 44 + 28 + 54);
        var wanted = new Size(chrome.Width + (120 * cell.Width), chrome.Height + (36 * cell.Height));
        var room = WindowFit.Room(FullHd, scaling);
        var fitted = WindowFit.InCells(wanted, room, cell, chrome);

        await Assert.That(fitted.Width).IsLessThanOrEqualTo(room.Width);
        await Assert.That(fitted.Height).IsLessThanOrEqualTo(room.Height);
        await Assert.That((fitted.Height - chrome.Height) % cell.Height).IsEqualTo(0).Because("whole rows, no leftover strip");
        await Assert.That((fitted.Width - chrome.Width) % cell.Width).IsEqualTo(0);

        var at = WindowFit.Centre(fitted, FullHd, scaling);
        await Assert.That(at.Y + (fitted.Height * scaling)).IsLessThanOrEqualTo(FullHd.Bottom).Because("the command line opens on the screen");
        await Assert.That(at.X).IsGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task AWindowThatFitsIsLeftAsAskedFor()
    {
        var wanted = new Size(1230, 850);
        var fitted = WindowFit.InCells(wanted, WindowFit.Room(FullHd, 1), new Size(10, 22), new Size(252, 126));
        await Assert.That(fitted).IsEqualTo(wanted);
    }

    [Test]
    public async Task TheSettingsWindowShrinksToTheScreenButNotBelowItsMinimum()
    {
        var room = WindowFit.Room(FullHd, 1.5);
        var fitted = WindowFit.Within(new Size(1120, 760), room, new Size(640, 440));
        await Assert.That(fitted.Height).IsLessThanOrEqualTo(room.Height);
        await Assert.That(fitted.Width).IsEqualTo(1120);

        var tiny = WindowFit.Within(new Size(1120, 760), new Size(300, 200), new Size(640, 440));
        await Assert.That(tiny).IsEqualTo(new Size(640, 440));
    }

    [Test]
    [Arguments(1f)]
    [Arguments(1.25f)]
    [Arguments(1.5f)]
    public async Task AFieldEndingAtTheLastColumnReachesPastTheGridsLeftoverPixels(float scale)
    {
        var cw = (int)MathF.Round(10 * scale);
        var ch = (int)MathF.Round(22 * scale);
        var band = new InputBand(20, 1, InputLayout.HostFieldPad, [new InputPrompt(20, "Corvid ›")], "⏎ sends");
        var grid = TerminalView.FieldRect(band, cw, ch, 80, scale);
        var view = (80 * cw) + (cw * 0.7f);
        var wide = TerminalView.FieldRect(band, cw, ch, 80, scale, view);

        await Assert.That(wide.Left).IsEqualTo(grid.Left);
        await Assert.That(wide.Right - grid.Right).IsEqualTo(MathF.Floor(view) - (80 * cw)).Because("as far in from the window's edge as from the sidebar's");
        await Assert.That(wide.Right % 1).IsEqualTo(0f).Because("a field edge on a whole device pixel");
        await Assert.That(wide.Left % 1).IsEqualTo(0f);

        var column = new InputBand(20, 1, InputLayout.HostFieldPad, [new InputPrompt(20, "Corvid ›")], "⏎ sends") { Left = 0, Columns = 50 };
        var inColumn = TerminalView.FieldRect(column, cw, ch, 80, scale, view);
        await Assert.That(inColumn).IsEqualTo(TerminalView.FieldRect(column, cw, ch, 80, scale)).Because("a band short of the edge stops at its column");
    }

    [Test]
    public async Task TheComposersSummaryShortensToItsWordsWhenTheFooterIsNarrow()
    {
        var (wide, narrow) = await Session.Dispatch(() =>
        {
            var text = new FittedText { Forms = ["2 paragraphs · 28 words · 160 chars", "28 words"] };
            return (text.Pick(10_000), text.Pick(80));
        }, CancellationToken.None);

        await Assert.That(wide).IsEqualTo("2 paragraphs · 28 words · 160 chars");
        await Assert.That(narrow).IsEqualTo("28 words");
    }

    private static HeadlessUnitTestSession Session => TerminalViewTests.Session;
}
