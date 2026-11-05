using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;
using SkiaSharp;
using CellColour = SharpConsoleUI.Color;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// Rules meet a pane divider's hairline with no gap (wave 35). A native view covers only its pane, and the divider's cell
/// is the cells' — the two planes meeting at a hairline in its middle — so a tab strip's foot, a panel's heading and a
/// strip's seam stopped half a cell short of it, as did a horizontal divider and the field's strip beside a vertical one.
/// <see cref="SeamRules"/> carries each native rule on to the hairline and <c>DrawDividers</c> the cells' own; neither
/// takes a press, so the divider's grab area is the cells' as before.
/// </summary>
[NotInParallel]
public class DividerSeamTests
{
    private static readonly SKColor Hairline = new(0x2c, 0x34, 0x47);

    [Test]
    public async Task AHorizontalDividerRunsOnToTheVerticalHairlineBesideIt()
    {
        var (inHalfCell, beyond) = await TerminalViewTests.Session.Dispatch(() =>
        {
            const int columns = 40, rows = 10;
            var driver = new AvaloniaConsoleDriver(columns, rows);
            var view = new TerminalView(driver, SKColors.White, new SKColor(0x10, 0x13, 0x1a), 16, null) { DividerRule = Hairline };
            var window = new Window { Content = view };
            var size = view.SizeFor(columns, rows);
            window.Width = size.Width;
            window.Height = size.Height;
            window.Show();
            try
            {
                for (var y = 0; y < rows; y++)
                {
                    driver.FillCells(0, y, 19, ' ', CellColour.Default, new CellColour(0x20, 0x30, 0x40));
                    driver.FillCells(20, y, 20, ' ', CellColour.Default, new CellColour(0x40, 0x30, 0x20));
                }

                ((IPointerPresenter)driver.Pointer).Dividers(
                [
                    new PaneDividerOverlay(new PaneRect(19, 0, 1, rows), Vertical: true, Lit: false),
                    new PaneDividerOverlay(new PaneRect(20, 5, 20, 1), Vertical: false, Lit: false),
                ]);
                driver.Flush();
                DesktopLayerTests.Drain();
                using var frame = window.CaptureRenderedFrame()!;
                var cw = (double)view.Renderer.Fonts.CellWidth;
                var ch = (double)view.Renderer.Fonts.CellHeight;
                var hairX = Math.Round((19 * cw) + ((cw - 1) / 2));
                var hairY = Math.Round((5 * ch) + ((ch - 1) / 2));
                // Between the vertical hairline and the horizontal divider's first cell: the half cell that used to be plane.
                return (Pixel(frame, hairX + 1, hairY), Pixel(frame, (20 * cw) + 2, hairY));
            }
            finally
            {
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

        var rule = Color.FromRgb(Hairline.Red, Hairline.Green, Hairline.Blue);
        await Assert.That(inHalfCell).IsEqualTo(rule).Because("the horizontal hairline runs on to the vertical one");
        await Assert.That(beyond).IsEqualTo(rule);
    }

    [Test]
    [Arguments("roleplay-scrolled", 4)]
    [Arguments("layout-combat", 6)]
    public async Task EveryNativeRuleBesideADividerMeetsItsHairline(string view, int least)
    {
        var (runs, hairlines, misses, hits) = await TerminalViewTests.Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(140, 40);
            var terminal = new TerminalView(driver, SKColors.White, new SKColor(0x1e, 0x1e, 0x24), 10f, null, 1.12f);
            var root = new Panel();
            root.Children.Add(terminal);
            var layer = new DesktopLayer(terminal, HostChrome.Desktop);
            root.Children.Add(layer);
            var window = new Window { Content = root, Width = 1400, Height = 900 };
            window.Show();
            var app = new SharpMUTermApp(DemoScene.Build(), WindowHost.Create(driver, clipboard: null) with
            {
                Capabilities = WindowOptions.Capabilities,
                Audio = null,
                Presence = null,
                Announcer = null,
                RunsInline = true,
            });
            try
            {
                app.RenderSnapshot(view);
                app.RenderNextFrame();
                DesktopLayerTests.Drain();
                window.UpdateLayout();
                layer.Sync();
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame()!;
                var origin = layer.CellOrigin;
                var hairs = terminal.VerticalHairlines().Select(h => h.Translate(new Vector(origin.X, origin.Y))).ToList();
                var seen = layer.Seams.Runs.ToList();
                var wrong = new List<string>();
                var pressed = new List<object?>();
                foreach (var run in seen)
                {
                    // Each run starts or ends on a hairline's edge, and its pixels, up to that edge, are the rule's colour.
                    var touches = hairs.Any(h => Math.Abs(run.Rect.Right - h.Left) < 0.01 || Math.Abs(run.Rect.Left - h.Right) < 0.01);
                    var y = run.Rect.Y + (run.Rect.Height / 2);
                    var first = Pixel(frame, run.Rect.Left + 0.5, y);
                    var last = Pixel(frame, run.Rect.Right - 0.5, y);
                    if (!touches || first != run.Colour || last != run.Colour)
                    {
                        wrong.Add($"{run.Rect} {run.Colour} drew {first}..{last} touches {touches}");
                    }

                    // A press there is still the cells', the divider's grab area unchanged.
                    pressed.Add(window.InputHitTest(new Point(run.Rect.X + (run.Rect.Width / 2), y)) is Visual hit
                        && (ReferenceEquals(hit, terminal) || hit.GetVisualAncestors().Contains(terminal)));
                }

                return (seen, hairs, wrong, pressed);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

        await Assert.That(hairlines.Count).IsGreaterThan(0).Because("the layout has a vertical divider");
        await Assert.That(runs.Count).IsGreaterThanOrEqualTo(least).Because($"the strip, headings and seams beside the divider each meet it: {string.Join("; ", runs.Select(r => r.Rect))}");
        await Assert.That(misses).IsEmpty();
        await Assert.That(hits.All(h => h is true)).IsTrue().Because("the runs take no press: the divider's hover, drag and double-click are the cells'");
    }

    private static Color Pixel(global::Avalonia.Media.Imaging.WriteableBitmap bitmap, double x, double y)
    {
        using var buffer = bitmap.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        var i = ((int)y * buffer.RowBytes) + ((int)x * 4);
        return buffer.Format == global::Avalonia.Platform.PixelFormat.Rgba8888
            ? Color.FromRgb(bytes[i], bytes[i + 1], bytes[i + 2])
            : Color.FromRgb(bytes[i + 2], bytes[i + 1], bytes[i]);
    }
}
