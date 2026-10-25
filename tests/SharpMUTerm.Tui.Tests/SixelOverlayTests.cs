using Color = SharpConsoleUI.Color;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Imaging;
using SharpConsoleUI.Layout;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The Sixel overlay: a picture's half-block rows found on the frame by their colours, and drawn again as
/// Sixel over them once — and again only when the rows under it change, move, or were covered.
/// </summary>
[NotInParallel]
public class SixelOverlayTests
{
    private const string Esc = "\u001b";

    /// <summary>A 3×2-cell picture: six cells, twelve half-block pixels, no two alike.</summary>
    private static PixelBuffer Cells()
    {
        var cells = new PixelBuffer(3, 4);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                cells.SetPixel(x, y, new ImagePixel((byte)(40 * x + 10), (byte)(50 * y + 5), 90));
            }
        }

        return cells;
    }

    private static void Paint(CharacterBuffer buffer, PixelBuffer cells, int left, int top, int firstRow = 0)
    {
        for (var row = firstRow; row < cells.Height / 2; row++)
        {
            for (var col = 0; col < cells.Width; col++)
            {
                var t = cells.GetPixel(col, row * 2);
                var b = cells.GetPixel(col, (row * 2) + 1);
                buffer.SetNarrowCell(left + col, top + row - firstRow, '▀', new Color(t.R, t.G, t.B), new Color(b.R, b.G, b.B));
            }
        }
    }

    [Test]
    public async Task APictureIsFoundWhereItWasPaintedAndDrawnThere()
    {
        var overlay = new SixelOverlay();
        var cells = Cells();
        overlay.Register(cells, cells);
        var buffer = new CharacterBuffer(20, 10);
        Paint(buffer, cells, left: 5, top: 3);

        var output = overlay.OnFrame(buffer, left: 0, top: 0, screenHeight: 10);

        await Assert.That(output).IsNotNull();
        await Assert.That(output!).StartsWith(Esc + "7" + Esc + "[4;6H" + Esc + "P0;1;0q\"1;1;30;40");
        await Assert.That(output).EndsWith(Esc + "\\" + Esc + "8");
    }

    [Test]
    public async Task APictureThatSitsStillIsSentOnce()
    {
        var overlay = new SixelOverlay();
        var cells = Cells();
        overlay.Register(cells, cells);
        var buffer = new CharacterBuffer(20, 10);
        Paint(buffer, cells, 5, 3);

        await Assert.That(overlay.OnFrame(buffer, 0, 0, 10)).IsNotNull();
        await Assert.That(overlay.OnFrame(buffer, 0, 0, 10)).IsNull();

        overlay.Invalidate();
        await Assert.That(overlay.OnFrame(buffer, 0, 0, 10)).IsNotNull().Because("covered and uncovered");
    }

    [Test]
    public async Task AChangeOnARowThePictureSitsOnSendsItAgain()
    {
        var overlay = new SixelOverlay();
        var cells = Cells();
        overlay.Register(cells, cells);
        var buffer = new CharacterBuffer(20, 10);
        Paint(buffer, cells, 5, 3);
        overlay.OnFrame(buffer, 0, 0, 10);

        buffer.SetNarrowCell(0, 9, 'x', Color.White, Color.Black);
        await Assert.That(overlay.OnFrame(buffer, 0, 0, 10)).IsNull().Because("a row it does not cover");

        buffer.SetNarrowCell(15, 4, 'x', Color.White, Color.Black);
        await Assert.That(overlay.OnFrame(buffer, 0, 0, 10)).IsNotNull()
            .Because("the driver may rewrite a row's whole dirty span, across the picture");
    }

    [Test]
    public async Task APictureScrolledHalfOffTheTopIsDrawnFromItsFirstVisibleRow()
    {
        var overlay = new SixelOverlay();
        var cells = Cells();
        overlay.Register(cells, cells);
        var buffer = new CharacterBuffer(20, 10);
        Paint(buffer, cells, 2, 0, firstRow: 1);

        var placement = overlay.Locate(buffer).Single();

        await Assert.That(placement.FirstRow).IsEqualTo(1);
        await Assert.That(placement.Rows).IsEqualTo(1);
        await Assert.That(overlay.OnFrame(buffer, 0, 0, 10)!).Contains("\"1;1;30;20");
    }

    [Test]
    public async Task ARowThatDoesNotMatchExactlyIsLeftAsHalfBlocks()
    {
        var overlay = new SixelOverlay();
        var cells = Cells();
        overlay.Register(cells, cells);
        var buffer = new CharacterBuffer(20, 10);
        Paint(buffer, cells, 5, 3);
        buffer.SetNarrowCell(6, 3, '▀', Color.White, Color.Black); // a selection over one cell of row 0

        var placement = overlay.Locate(buffer).Single();

        await Assert.That(placement.FirstRow).IsEqualTo(1);
        await Assert.That(placement.Y).IsEqualTo(4);
    }

    [Test]
    public async Task NothingIsDrawnOnTheLastRowWhereASixelWouldScrollTheTerminal()
    {
        var overlay = new SixelOverlay();
        var cells = Cells();
        overlay.Register(cells, cells);
        var buffer = new CharacterBuffer(20, 5);
        Paint(buffer, cells, 0, 3);

        var output = overlay.OnFrame(buffer, 0, 0, screenHeight: 5);

        await Assert.That(output!).Contains("\"1;1;30;20").Because("only the row above the last is drawn");
    }

    [Test]
    [Arguments(null, 10, 20)]
    [Arguments("8x16", 8, 16)]
    [Arguments("bogus", 10, 20)]
    [Arguments("0x16", 10, 20)]
    public async Task TheCellSizeDefaultsToWindowsTerminals(string? value, int width, int height)
    {
        await Assert.That(SharpMUTermApp.SixelCellSize(value)).IsEqualTo((width, height));
    }

    // ---- In the app -----------------------------------------------------------------------------

    [Test]
    public async Task InTheAppAPictureIsDrawnAsSixelOverItsHalfBlocks()
    {
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Aetherfall",
            Host = "aetherfall.example.org",
            Port = 4201,
            ContentFormat = ContentFormat.Mxp,
            Characters = { new CharacterDefinition { Name = "Corvid", Logging = new LoggingSettings() } },
        });
        Console.SetIn(TextReader.Null);
        var capabilities = new TerminalCapabilities(
            GraphicsProtocol.Sixel, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: true,
            supportsSixelOverlay: true);
        await using var app = new SharpMUTermApp(config, capabilities, new HeadlessConsoleDriver(120, 40));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        await Assert.That(app.DispatchCommand(CommandIds.Character("Aetherfall.Corvid"))).IsTrue();
        await app.FindSession("Aetherfall.Corvid")!.ConnectAsync();
        app.RenderNextFrame();
        app.ImageFetch = (_, _) => Task.FromResult<byte[]?>(TestPng.Encode(64, 64, (x, y) => ((byte)(x * 4), (byte)(y * 4), 120)));

        telnet.Receive("\x1b[1zA map: <IMAGE map.png URL=\"https://mud.example/\">");
        app.RenderNextFrame();
        await app.InlineImagesSettled;
        app.RenderNextFrame();
        var grid = FrameGrid.Decode(app.RenderWholeFrame(), 120, 40);

        var row = grid.ToList().FindIndex(l => l.Contains('▀'));
        var column = grid[row].IndexOf('▀');
        await Assert.That(app.LastSixelOverlay).IsNotNull();
        await Assert.That(app.LastSixelOverlay!).StartsWith($"{Esc}7{Esc}[{row + 1};{column + 1}H{Esc}P0;1;0q\"1;1;80;80");
    }
}
