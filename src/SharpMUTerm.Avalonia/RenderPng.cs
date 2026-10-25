using System.Diagnostics;
using SkiaSharp;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// <c>--render-png</c>: the demo client on this driver, drawn by this renderer, with no window. It is the
/// windowed host's snapshot — what verifies it on a machine with no display — and unlike the terminal
/// snapshot it runs the client's real main loop, because a picture arrives on the UI thread after the scene
/// is laid out and only the loop delivers it.
/// </summary>
internal static class RenderPng
{
    public static int Run(WindowOptions options)
    {
        if (!options.Demo)
        {
            Console.Error.WriteLine("--render-png needs --demo-config: it draws the demo worlds, never your own.");
            return 2;
        }

        var notices = new List<string>();
        var config = ClientLaunch.LoadConfiguration(options, notices);
        var (foreground, background) = WindowOptions.DefaultColours(config);
        using var driver = new AvaloniaConsoleDriver(options.Columns, options.Rows);
        var app = ClientLaunch.Demo(options, config, driver, clipboard: null);
        new Thread(() => app.Run([])) { IsBackground = true, Name = "SharpMUTerm client" }.Start();

        // The picture is fetched and transmitted after the scene is laid out, so a quiet second alone can come
        // before it lands. Wait for the image work too, and refuse a frame that was cut short or lost its picture.
        var imagesDone = app.InlineImagesSettled.Wait(TimeSpan.FromSeconds(15));
        var settled = Settle(driver);
        var wantsPicture = options.View is "mxp-image" or "mxp-figure";
        if (!imagesDone || !settled || (wantsPicture && driver.Pictures.Count == 0))
        {
            Console.Error.WriteLine($"--render-png: the frame did not settle (images {(imagesDone ? "done" : "timed out")}, "
                + $"frames {(settled ? "quiet" : "still changing")}, {driver.Pictures.Count} picture(s)).");
            Environment.Exit(1);
        }

        // Points to pixels at 96 dpi, the scale a window on an unscaled display would use.
        using var fonts = new CellFonts(options.FontPoints * 96f / 72f, options.FontFamily);
        var frame = driver.Frame;
        var info = new SKImageInfo(frame.Width * fonts.CellWidth, frame.Height * fonts.CellHeight);
        using var surface = SKSurface.Create(info);
        new CellRenderer(fonts, foreground, background).Render(surface.Canvas, frame, driver.Pictures);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        using (var file = File.Create(options.RenderPng!))
        {
            png.SaveTo(file);
        }

        Console.Out.WriteLine($"{options.RenderPng}: {info.Width}x{info.Height} px, {frame.Width}x{frame.Height} cells, "
            + $"font {fonts.FamilyName} ({fonts.CellWidth}x{fonts.CellHeight} px cell), {driver.Pictures.Count} picture(s)");

        // The framework keeps foreground threads alive; the frame is written, so leave as the snapshot does.
        Environment.Exit(0);
        return 0;
    }

    /// <summary>
    /// Waits until what is drawn stops changing for a second. Not until frames stop arriving: they never do, since the
    /// loop keeps flushing (the caret). False on timeout.
    /// </summary>
    private static bool Settle(AvaloniaConsoleDriver driver)
    {
        var clock = Stopwatch.StartNew();
        var last = "";
        var quietSince = clock.Elapsed;
        while (clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(50);
            var frame = driver.Frame;
            var text = driver.FrameCount == 0 ? "" : string.Join('\n', Enumerable.Range(0, frame.Height).Select(frame.RowText));
            if (text != last)
            {

                last = text;
                quietSince = clock.Elapsed;
            }
            else if (text.Length > 0 && clock.Elapsed - quietSince > TimeSpan.FromSeconds(1))
            {
                return true;
            }
        }

        return false;
    }
}
