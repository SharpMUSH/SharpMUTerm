using System.Text;
using SharpConsoleUI;
using SharpConsoleUI.Layout;
using SharpMUTerm.Graphics;
using SkiaSharp;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The point of the driver: a picture sent the Kitty way — pixels to <c>IGraphicsProtocol</c>, placeholder cells
/// to the grid — comes out as the picture, at the cells the placeholders occupy. Read off the painted pixels.
/// </summary>
public class PictureRenderingTests
{
    private static readonly SKColor Back = new(0x20, 0x20, 0x20);

    [Test]
    public async Task APlaceholderNamesItsPictureRowAndColumn()
    {
        var cell = Placeholder(0xC00001, row: 2, column: 5);
        await Assert.That(CellRenderer.Placeholder(cell)).IsEqualTo(((uint)0xC00001, 2, 5));
    }

    [Test]
    public async Task AnOrdinaryCellIsNoPlaceholder()
    {
        var cell = GridCell.Blank with { Rune = new Rune('x') };
        await Assert.That(CellRenderer.Placeholder(cell)).IsNull();
    }

    [Test]
    public async Task APictureIsPaintedAtItsCellsAndOnlyThere()
    {
        // A 2×1-cell picture: left half red, right half blue, sent as raw RGB the way the client sends one.
        using var driver = new AvaloniaConsoleDriver(6, 3);
        const int pw = 16, ph = 16;
        var rgb = new byte[pw * ph * 3];
        for (var y = 0; y < ph; y++)
        {
            for (var x = 0; x < pw; x++)
            {
                var i = ((y * pw) + x) * 3;
                (rgb[i], rgb[i + 1], rgb[i + 2]) = x < pw / 2 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)0, (byte)255);
            }
        }

        driver.TransmitRawRgb(0xC00000, rgb, pw, ph, columns: 2, rows: 1);

        // The placeholder row goes in through the framework's own buffer type, as a frame would.
        var buffer = new CharacterBuffer(6, 3, Color.Default);
        buffer.SetCell(2, 1, ToCell(Placeholder(0xC00000, 0, 0)));
        buffer.SetCell(3, 1, ToCell(Placeholder(0xC00000, 0, 1)));
        driver.WriteBufferRegion(0, 1, buffer, 0, 1, 6, Color.Default);
        driver.Flush();

        using var fonts = new CellFonts(16);
        using var surface = SKSurface.Create(new SKImageInfo(6 * fonts.CellWidth, 3 * fonts.CellHeight));
        new CellRenderer(fonts, SKColors.White, Back).Render(surface.Canvas, driver.Frame, driver.Pictures, caretShown: false);
        using var pixels = SKBitmap.FromImage(surface.Snapshot());

        SKColor Centre(int col, int row) =>
            pixels.GetPixel((col * fonts.CellWidth) + (fonts.CellWidth / 2), (row * fonts.CellHeight) + (fonts.CellHeight / 2));

        await Assert.That(Centre(2, 1)).IsEqualTo(new SKColor(255, 0, 0));
        await Assert.That(Centre(3, 1)).IsEqualTo(new SKColor(0, 0, 255));
        await Assert.That(Centre(1, 1)).IsEqualTo(Back);
        await Assert.That(Centre(4, 1)).IsEqualTo(Back);
        await Assert.That(Centre(2, 0)).IsEqualTo(Back);
    }

    [Test]
    public async Task APlaceholderForAPictureNeverSentDrawsNothing()
    {
        using var driver = new AvaloniaConsoleDriver(4, 2);
        var buffer = new CharacterBuffer(4, 2, Color.Default);
        buffer.SetCell(1, 0, ToCell(Placeholder(0xC00009, 0, 0)));
        driver.WriteBufferRegion(0, 0, buffer, 0, 0, 4, Color.Default);
        driver.Flush();

        using var fonts = new CellFonts(16);
        using var surface = SKSurface.Create(new SKImageInfo(4 * fonts.CellWidth, 2 * fonts.CellHeight));
        new CellRenderer(fonts, SKColors.White, Back).Render(surface.Canvas, driver.Frame, driver.Pictures, caretShown: false);
        using var pixels = SKBitmap.FromImage(surface.Snapshot());
        await Assert.That(pixels.GetPixel(fonts.CellWidth + (fonts.CellWidth / 2), fonts.CellHeight / 2)).IsEqualTo(Back);
    }

    [Test]
    public async Task AHalfBlockFillsExactlyTheTopHalfOfItsCell()
    {
        using var driver = new AvaloniaConsoleDriver(1, 1);
        driver.SetNarrowCell(0, 0, '▀', new Color(0, 255, 0), new Color(0, 0, 0));
        driver.Flush();

        using var fonts = new CellFonts(16);
        using var surface = SKSurface.Create(new SKImageInfo(fonts.CellWidth, fonts.CellHeight));
        new CellRenderer(fonts, SKColors.White, Back).Render(surface.Canvas, driver.Frame, driver.Pictures, caretShown: false);
        using var pixels = SKBitmap.FromImage(surface.Snapshot());

        // Flush with both edges — a glyph from the font would stop short of the cell's top.
        await Assert.That(pixels.GetPixel(fonts.CellWidth / 2, 0)).IsEqualTo(new SKColor(0, 255, 0));
        await Assert.That(pixels.GetPixel(fonts.CellWidth / 2, (fonts.CellHeight / 2) - 1)).IsEqualTo(new SKColor(0, 255, 0));
        await Assert.That(pixels.GetPixel(fonts.CellWidth / 2, fonts.CellHeight - 1)).IsEqualTo(new SKColor(0, 0, 0));
    }

    [Test]
    public async Task AFrameIsPublishedOnlyByAFlush()
    {
        using var driver = new AvaloniaConsoleDriver(3, 1);
        driver.SetNarrowCell(0, 0, 'a', Color.Default, Color.Default);
        await Assert.That(driver.Frame.RowText(0)).IsEqualTo("   ");
        driver.Flush();
        await Assert.That(driver.Frame.RowText(0)).IsEqualTo("a  ");
    }

    [Test]
    public async Task OverwritingHalfAWideCharacterBlanksTheOtherHalf()
    {
        var grid = new CellGrid(4, 1);
        var buffer = new CharacterBuffer(4, 1);
        buffer.SetCell(0, 0, new Cell(new Rune('漢'), Color.Default, Color.Default));
        buffer.SetCell(1, 0, new Cell(new Rune(' '), Color.Default, Color.Default) { IsWideContinuation = true });
        grid.CopyFrom(0, 0, buffer, 0, 0, 4, Color.Default);
        grid.Set(1, 0, GridCell.Blank with { Rune = new Rune('x') });
        await Assert.That(grid.Snapshot(default).RowText(0)).IsEqualTo(" x  ");
    }

    [Test]
    public async Task AResizeIsReportedToTheFrameworkOnce()
    {
        using var driver = new AvaloniaConsoleDriver(10, 5);
        var sizes = new List<SharpConsoleUI.Helpers.Size>();
        driver.ScreenResized += (_, size) => sizes.Add(size);
        driver.Resize(12, 6);
        driver.Resize(12, 6);
        await Assert.That(sizes.Count).IsEqualTo(1);
        await Assert.That(driver.ScreenSize.Width).IsEqualTo(12);
        await Assert.That(driver.ScreenSize.Height).IsEqualTo(6);
    }

    private static GridCell Placeholder(uint id, int row, int column) =>
        new(
            new Rune(KittyGraphicsProtocol.PlaceholderCodePoint),
            char.ConvertFromUtf32(KittyGraphicsProtocol.RowColumnDiacritics[row]) + char.ConvertFromUtf32(KittyGraphicsProtocol.RowColumnDiacritics[column]),
            new Color((byte)(id >> 16), (byte)(id >> 8), (byte)id),
            Color.Default,
            TextDecoration.None,
            false);

    private static Cell ToCell(GridCell cell) =>
        new(cell.Rune, cell.Foreground, cell.Background) { Combiners = cell.Combiners };
}
