using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A stacked split spends no row on a divider where something already marks the boundary: above a pane that draws
/// its own tab strip (the strip is the boundary, and its empty run is the handle), and either side of a fixed strip
/// panel, which is told apart by its plane instead. Side by side, the divider column is unchanged.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class StackedDividerTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App(string view, string theme = "Dark", int width = Width, int height = Height)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ThemeName = theme;
        config.Theme = ThemeLibrary.Get(theme);
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(width, height));
        app.RenderSnapshot(view);
        app.RenderNextFrame();
        return app;
    }

    [Test]
    public async Task AStackedSplitGivesTheDividerRowToThePanes()
    {
        var app = App("split-stacked");
        var rects = app.PaneSnapshot().Rects.Values.OrderBy(r => r.Y).ToArray();

        await Assert.That(rects.Length).IsEqualTo(2);
        await Assert.That(rects[1].Y).IsEqualTo(rects[0].Y + rects[0].Height);

        // The lower pane's output starts under its tab strip, which is the row the divider used to take.
        var outputs = app.PaneOutputRects().Values.OrderBy(r => r.Y).ToArray();
        await Assert.That(outputs[1].Y).IsEqualTo(rects[1].Y + 1);
    }

    [Test]
    public async Task ASideBySideSplitKeepsItsDividerColumn()
    {
        var app = App("split");
        var rects = app.PaneSnapshot().Rects.Values.OrderBy(r => r.X).ToArray();

        await Assert.That(rects.Length).IsEqualTo(2);
        await Assert.That(rects[1].X).IsEqualTo(rects[0].X + rects[0].Width + 1);
    }

    /// <summary>
    /// The vitals strip sits flush on the panes under it and is told apart from them by its plane, on every theme
    /// and whether or not the pane beside it holds the focus: an unfocused strip is recessed a step, so it is never
    /// the same plane as an unfocused pane. Narrow, where Combat draws its vitals as a strip across the top.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task AStripSitsFlushAndIsToldApartByItsPlane(string theme)
    {
        var app = App("layout-combat", theme, 80, 24);
        foreach (var moved in new[] { false, true })
        {
            if (moved)
            {
                // The focus goes to the map pane, so the output pane under the strip is unfocused too.
                app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, shift: false, alt: false, control: true));
            }

            var frame = FrameGrid.Backgrounds(app.RenderWholeFrame());
            var rects = app.PaneSnapshot().Rects.Values.ToArray();
            var strip = rects.Where(r => r.Height == 1).OrderBy(r => r.Y).First();
            var below = rects.Where(r => r.Y == strip.Y + strip.Height).ToArray();

            await Assert.That(below).IsNotEmpty().Because("nothing between the strip and the panes under it");
            foreach (var pane in below)
            {
                var stripPlane = Plane(frame, strip.Y, pane.X, pane.Width);
                var panePlane = Plane(frame, pane.Y + 4, pane.X, pane.Width);
                await Assert.That(Math.Abs(Lightness(stripPlane) - Lightness(panePlane))).IsGreaterThanOrEqualTo(2.5)
                    .Because($"{theme}, focus {(moved ? "moved" : "left")}: strip {stripPlane} against pane {panePlane}");
            }
        }
    }

    /// <summary>The commonest background along a run of one row.</summary>
    private static (int R, int G, int B) Plane(Dictionary<(int Row, int Column), string?> frame, int row, int x, int width) =>
        Enumerable.Range(x, width)
            .Select(column => frame.GetValueOrDefault((row, column)))
            .Where(bg => bg is not null)
            .GroupBy(bg => bg)
            .OrderByDescending(group => group.Count())
            .Select(group => Parse(group.Key!))
            .First();

    private static (int R, int G, int B) Parse(string sgr)
    {
        var parts = sgr.Split(';');
        return (int.Parse(parts[2]), int.Parse(parts[3]), int.Parse(parts[4]));
    }

    private static double Lightness((int R, int G, int B) c)
    {
        var y = Core.Text.Contrast.RelativeLuminance(new Core.Text.Rgb((byte)c.R, (byte)c.G, (byte)c.B));
        return y > 0.008856 ? (116 * Math.Cbrt(y)) - 16 : 903.3 * y;
    }
}
