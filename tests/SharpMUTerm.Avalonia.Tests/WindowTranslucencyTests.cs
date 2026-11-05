using Avalonia.Controls;
using SharpConsoleUI;
using SharpConsoleUI.Layout;
using SkiaSharp;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// F7's background transparency in the window: the client says the level (<see cref="WindowTranslucency"/>), and the
/// window draws its planes at it, read off the painted pixels. Text stays opaque, and a window the desktop could not make
/// translucent stays opaque, since a translucent plane there would show black rather than the desktop.
/// </summary>
public class WindowTranslucencyTests
{
    private static readonly SKColor Back = new(0x20, 0x24, 0x30);

    [Test]
    public async Task ThePlanesAlphaIsTheRestOfTheLevelAndOpaqueWhereTheDesktopGaveNone()
    {
        await Assert.That(WindowTranslucency.PlaneAlpha(0, WindowTransparencyLevel.Transparent)).IsEqualTo((byte)255);
        await Assert.That(WindowTranslucency.PlaneAlpha(40, WindowTransparencyLevel.Transparent)).IsEqualTo((byte)153);
        await Assert.That(WindowTranslucency.PlaneAlpha(100, WindowTransparencyLevel.Transparent)).IsEqualTo((byte)0);
        await Assert.That(WindowTranslucency.PlaneAlpha(40, WindowTransparencyLevel.None)).IsEqualTo((byte)255);
    }

    [Test]
    public async Task TheLevelIsHeldToItsRangeAndRaisedWhenSaid()
    {
        var translucency = new WindowTranslucency();
        var said = new List<int>();
        translucency.Changed += said.Add;
        translucency.Show(140);
        translucency.Show(-3);
        await Assert.That(said).IsEquivalentTo(new[] { 100, 0 });
        await Assert.That(translucency.Percent).IsEqualTo(0);
    }

    [Test]
    public async Task EveryBackgroundIsDrawnOnceAtTheAlphaAndTheTextStaysOpaque()
    {
        using var driver = new AvaloniaConsoleDriver(4, 2);
        var plane = new Color(0x40, 0x10, 0x10);
        var buffer = new CharacterBuffer(4, 2, Color.Default);
        buffer.SetCell(1, 0, new Cell(new System.Text.Rune('█'), Color.White, plane));
        buffer.SetCell(2, 0, new Cell(new System.Text.Rune(' '), Color.White, plane));
        driver.WriteBufferRegion(0, 0, buffer, 0, 0, 4, Color.Default);
        driver.Flush();

        using var fonts = new CellFonts(16);
        using var surface = SKSurface.Create(new SKImageInfo(4 * fonts.CellWidth, (2 * fonts.CellHeight) + 5, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        surface.Canvas.Clear(SKColors.Transparent);
        new CellRenderer(fonts, SKColors.White, Back).Render(surface.Canvas, driver.Frame, driver.Pictures, caretShown: false, alpha: 153);
        using var pixels = SKBitmap.FromImage(surface.Snapshot());

        SKColor Centre(int col, int row) =>
            pixels.GetPixel((col * fonts.CellWidth) + (fonts.CellWidth / 2), (row * fonts.CellHeight) + (fonts.CellHeight / 2));

        // The default background and a plane alike, at the alpha and in their own colour: once, not over each other.
        await Assert.That(Centre(0, 0).Alpha).IsEqualTo((byte)153);
        await Assert.That(Centre(3, 1).Alpha).IsEqualTo((byte)153);
        await Assert.That(Centre(2, 0).Alpha).IsEqualTo((byte)153);
        await Assert.That(Centre(2, 0).Red).IsEqualTo((byte)0x40);

        // The pixels under the last row too, which no row reaches.
        await Assert.That(pixels.GetPixel(2, (2 * fonts.CellHeight) + 3).Alpha).IsEqualTo((byte)153);

        // A full block is ink, and ink is opaque.
        await Assert.That(Centre(1, 0)).IsEqualTo(SKColors.White);
    }

    [Test]
    public async Task AnOpaqueFrameIsDrawnAsItAlwaysWas()
    {
        using var driver = new AvaloniaConsoleDriver(2, 1);
        driver.Flush();
        using var fonts = new CellFonts(16);
        using var surface = SKSurface.Create(new SKImageInfo(2 * fonts.CellWidth, fonts.CellHeight));
        new CellRenderer(fonts, SKColors.White, Back).Render(surface.Canvas, driver.Frame, driver.Pictures, caretShown: false);
        using var pixels = SKBitmap.FromImage(surface.Snapshot());
        await Assert.That(pixels.GetPixel(1, 1)).IsEqualTo(Back);
    }
}
