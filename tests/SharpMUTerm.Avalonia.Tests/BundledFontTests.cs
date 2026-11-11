using SharpMUTerm.Tui;
using SkiaSharp;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window ships its own text face and Nerd Font symbols, so a machine with no Nerd Font installed still
/// draws the chrome's icons rather than boxes.
/// </summary>
public class BundledFontTests
{
    [Test]
    public async Task ImageSizingUsesTheViewsMeasuredCellsAfterScaling()
    {
        var measurements = await TerminalViewTests.Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 16, null, WindowOptions.DefaultLineHeight);
            var first = ((IPointerPresenter)driver.Pointer).ImageCellSize;
            var initial = new ImageCellSize(view.CellExtent.Width, view.CellExtent.Height);
            view.PrepareScale(1.5);
            return (first, initial, current: ((IPointerPresenter)driver.Pointer).ImageCellSize,
                expected: new ImageCellSize(view.CellExtent.Width, view.CellExtent.Height));
        }, CancellationToken.None);
        await Assert.That(measurements.first).IsEqualTo(measurements.initial);
        await Assert.That(measurements.current).IsEqualTo(measurements.expected);
    }

    [Test]
    public async Task TheBundledFacesAreInTheBuild()
    {
        await Assert.That(CellFonts.HasBundledFaces).IsTrue();
    }

    /// <summary>A Nerd Font icon from the Private Use Area the header draws: some face can always draw it.</summary>
    [Test]
    [Arguments(0xF0E0)]
    [Arguments(0xE0B0)]
    [Arguments(0xF0F3)]
    public async Task AChromeIconAlwaysHasAFaceThatDrawsIt(int codePoint)
    {
        using var fonts = new CellFonts(16);

        await Assert.That(fonts.For(codePoint, bold: false, italic: false).Typeface.ContainsGlyph(codePoint)).IsTrue();
    }

    [Test]
    public async Task TextHasAMonospaceFaceWithAWholePixelCell()
    {
        using var fonts = new CellFonts(16);

        await Assert.That(fonts.CellWidth).IsGreaterThan(0);
        await Assert.That(fonts.For('M', bold: true, italic: false).Typeface.ContainsGlyph('M')).IsTrue();
    }
}
