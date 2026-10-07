using Color = SharpConsoleUI.Color;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Parsing;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// MXP <c>&lt;IMAGE&gt;</c> in an output pane: a picture is a run of rows inserted under the line that
/// named it — Kitty placeholders or half-blocks, both plain markup — and on a terminal that can draw
/// neither, the line's own <c>[image: name]</c> link is the whole of it.
/// </summary>
[NotInParallel]
public class MxpImageTests
{
    private const string Secure = "\x1b[1z";
    private const string MainWindow = "main";

    // ---- Sizing -------------------------------------------------------------------------------

    [Test]
    public async Task APictureIsItsOwnSizeInEightBySixteenCells()
    {
        var box = InlineImageLayout.Fit(160, 160, null, null, availableColumns: 100);

        await Assert.That(box).IsEqualTo(new WebImageLayout.CellBox(20, 10));
    }

    [Test]
    public async Task APictureWiderThanThePaneShrinksAndKeepsItsShape()
    {
        var box = InlineImageLayout.Fit(800, 400, null, null, availableColumns: 50);

        await Assert.That(box).IsEqualTo(new WebImageLayout.CellBox(50, 13));
    }

    [Test]
    public async Task ATallPictureIsHeldToTheRowCeiling()
    {
        var box = InlineImageLayout.Fit(100, 2000, null, null, availableColumns: 100);

        await Assert.That(box.Rows).IsEqualTo(InlineImageLayout.MaxRows);
        await Assert.That(box.Columns).IsEqualTo(2);
    }

    [Test]
    public async Task OneHintScalesTheOtherEdge()
    {
        var box = InlineImageLayout.Fit(160, 80, new ImageExtent(40, ImageExtentUnit.Characters), null, 100);

        await Assert.That(box).IsEqualTo(new WebImageLayout.CellBox(40, 10));
    }

    /// <summary>Spec: "If the specified height is different from the image, the image is stretched."</summary>
    [Test]
    public async Task BothHintsStretch()
    {
        var box = InlineImageLayout.Fit(
            160, 160, new ImageExtent(50, ImageExtentUnit.Percent), new ImageExtent(32, ImageExtentUnit.Pixels), 80);

        await Assert.That(box).IsEqualTo(new WebImageLayout.CellBox(40, 2));
    }

    // ---- Rows ---------------------------------------------------------------------------------

    /// <summary>
    /// The rows go through the framework's own markup parser, which is what a pane uses: each cell
    /// must come out as U+10EEEE carrying its row and column diacritics, in a foreground that is the
    /// image id. A parser that dropped the combiners or a formatter that "corrected" the colour would
    /// name a different cell or a different image, and nothing else would notice.
    /// </summary>
    [Test]
    public async Task KittyRowsSurviveTheFrameworksMarkupParser()
    {
        const int id = 0xC00123;
        var rows = InlineImageRows.Kitty(id, columns: 3, rows: 2);

        var cells = MarkupParser.Parse(rows[1], Color.White, Color.Black);

        await Assert.That(cells.Count).IsEqualTo(3);
        for (var col = 0; col < 3; col++)
        {
            await Assert.That(cells[col].Character.Value).IsEqualTo(KittyGraphicsProtocol.PlaceholderCodePoint);
            await Assert.That(cells[col].Foreground).IsEqualTo(new Color(0xC0, 0x01, 0x23));
            await Assert.That(cells[col].Combiners).IsEqualTo(
                char.ConvertFromUtf32(KittyGraphicsProtocol.RowColumnDiacritics[1]) +
                char.ConvertFromUtf32(KittyGraphicsProtocol.RowColumnDiacritics[col]));
        }
    }

    [Test]
    public async Task TheDiacriticTableIsKittysWholeTable()
    {
        await Assert.That(KittyGraphicsProtocol.RowColumnDiacritics.Length).IsEqualTo(297);
        await Assert.That(KittyGraphicsProtocol.RowColumnDiacritics[^1]).IsEqualTo(0x1D244);
    }

    [Test]
    public async Task HalfBlockRowsCarryTwoPixelsACell()
    {
        var rows = InlineImageRows.HalfBlock((x, y) => y == 0 ? ((byte)255, (byte)0, (byte)0) : ((byte)0, (byte)0, (byte)255), 2, 1);

        var cells = MarkupParser.Parse(rows[0], Color.White, Color.Black);

        await Assert.That(cells.Count).IsEqualTo(2);
        await Assert.That(cells[0].Character.ToString()).IsEqualTo("▀");
        await Assert.That(cells[0].Foreground).IsEqualTo(new Color(255, 0, 0));
        await Assert.That(cells[0].Background).IsEqualTo(new Color(0, 0, 255));
        await Assert.That(rows[0].Split("[/]").Length).IsEqualTo(2); // one run, one tag
    }

    // ---- In a pane ----------------------------------------------------------------------------

    [Test]
    public async Task OnAHalfBlockTerminalThePictureLandsUnderItsLine()
    {
        await using var run = await Start(GraphicsProtocol.HalfBlock);
        run.App.ImageFetch = (_, _) => Task.FromResult<byte[]?>(Png(64, 64));

        run.Receive(Secure + "A map: <IMAGE map.png URL=\"https://mud.example/\">");
        await run.App.InlineImagesSettled;
        run.App.RenderNextFrame();

        var lines = run.App.PaneLines(MainWindow).ToList();
        var at = lines.FindIndex(l => l.Contains("[image: map.png]", StringComparison.Ordinal));
        await Assert.That(at).IsGreaterThanOrEqualTo(0);
        var picture = lines.Skip(at + 1).TakeWhile(l => l.Contains('▀')).Count();
        await Assert.That(picture).IsEqualTo(4); // 64×64 px is 8×4 cells
    }

    /// <summary>
    /// A picture arrives after its line, and more output can land in between. The rows go under the
    /// line that named them, not at the bottom where the pane happens to be when they arrive.
    /// </summary>
    [Test]
    public async Task APictureThatArrivesLateStillGoesUnderItsOwnLine()
    {
        await using var run = await Start(GraphicsProtocol.HalfBlock);
        var gate = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        run.App.ImageFetch = (_, _) => gate.Task;

        run.Receive(Secure + "<IMAGE map.png URL=\"https://mud.example/\">");
        run.Receive("A town guard stands watch.");
        run.Receive("The fountain burbles.");
        gate.SetResult(Png(32, 32));
        await run.App.InlineImagesSettled;
        run.App.RenderNextFrame();

        var lines = run.App.PaneLines(MainWindow).ToList();
        var at = lines.FindIndex(l => l.Contains("[image: map.png]", StringComparison.Ordinal));
        await Assert.That(lines[at + 1]).Contains("▀");
        await Assert.That(lines[at + 2]).Contains("▀");
        await Assert.That(lines[at + 3]).Contains("A town guard stands watch.");
        await Assert.That(lines[^1]).Contains("The fountain burbles.");
    }

    /// <summary>
    /// Nothing to draw with, nothing fetched: a world cannot make a text-only client contact a host
    /// by naming one. The link stays, and opens the picture in the desktop's browser when clicked.
    /// </summary>
    [Test]
    public async Task OnATextOnlyTerminalNothingIsFetchedAndTheLinkStays()
    {
        await using var run = await Start(GraphicsProtocol.None);
        var fetched = 0;
        run.App.ImageFetch = (_, _) =>
        {
            Interlocked.Increment(ref fetched);
            return Task.FromResult<byte[]?>(Png(64, 64));
        };

        run.Receive(Secure + "<IMAGE map.png URL=\"https://mud.example/\">");
        await run.App.InlineImagesSettled;

        var line = run.App.PaneLines(MainWindow).Single(l => l.Contains("map.png", StringComparison.Ordinal));
        await Assert.That(line).Contains(LinkPayload.WebScheme);
        await Assert.That(fetched).IsEqualTo(0);
        await Assert.That(run.App.PaneLines(MainWindow).Any(l => l.Contains('▀'))).IsFalse();
    }

    /// <summary>A game re-sending its room map on every look fetches it once.</summary>
    [Test]
    public async Task TheSamePictureIsFetchedOnce()
    {
        await using var run = await Start(GraphicsProtocol.HalfBlock);
        var fetched = 0;
        run.App.ImageFetch = (_, _) =>
        {
            Interlocked.Increment(ref fetched);
            return Task.FromResult<byte[]?>(Png(32, 32));
        };

        run.Receive(Secure + "<IMAGE map.png URL=\"https://mud.example/\">");
        await run.App.InlineImagesSettled;
        run.Receive(Secure + "<IMAGE map.png URL=\"https://mud.example/\">");
        await run.App.InlineImagesSettled;
        run.App.RenderNextFrame();

        await Assert.That(fetched).IsEqualTo(1);
        await Assert.That(run.App.PaneLines(MainWindow).Count(l => l.Contains('▀'))).IsEqualTo(4);
    }

    [Test]
    public async Task ABrokenPictureLeavesTheLinkAndNothingElse()
    {
        await using var run = await Start(GraphicsProtocol.HalfBlock);
        run.App.ImageFetch = (_, _) => Task.FromResult<byte[]?>(new byte[] { 1, 2, 3 });

        run.Receive(Secure + "<IMAGE map.png URL=\"https://mud.example/\">");
        await run.App.InlineImagesSettled;

        await Assert.That(run.App.PaneLines(MainWindow).Any(l => l.Contains('▀'))).IsFalse();
        await Assert.That(run.App.PaneLines(MainWindow).Any(l => l.Contains("[image: map.png]"))).IsTrue();
    }

    // ---- Harness ------------------------------------------------------------------------------

    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgb24>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image[x, y] = x < width / 2 ? new Rgb24(200, 40, 40) : new Rgb24(40, 40, 200);
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private sealed record Run(SharpMUTermApp App, RecordingTelnetSession Telnet) : IAsyncDisposable
    {
        public void Receive(string text)
        {
            Telnet.Receive(text);
            App.RenderNextFrame();
        }

        public ValueTask DisposeAsync() => App.DisposeAsync();
    }

    private static async Task<Run> Start(GraphicsProtocol protocol)
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
            protocol, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);
        var app = new SharpMUTermApp(config, capabilities, new HeadlessConsoleDriver(120, 40));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        if (!app.DispatchCommand(CommandIds.Character("Aetherfall.Corvid")))
        {
            throw new InvalidOperationException("the app would not switch to Aetherfall.Corvid");
        }

        await app.FindSession("Aetherfall.Corvid")!.ConnectAsync();
        app.RenderNextFrame();
        return new Run(app, telnet);
    }
}
