using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// A pane split by scrolling back keeps a scrollbar the whole height of the pane. The track already spans the pane
/// (the client measures a split pane as if it were whole); the window draws the bars over its native views, so the
/// scrolled-back band laid over the pane's foot no longer hides the bottom of it, and a press there is still the bar's.
/// </summary>
[NotInParallel]
public class ScrollbarLayerTests
{
    [Test]
    public async Task ASplitPanesScrollbarRunsUnderTheBandAndIsDrawnOverIt()
    {
        (double zoneBottom, double bandTop, double bandBottom, bool layerAbove, bool pressReachesCells) = await TerminalViewTests.Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(120, 34);
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
                app.RenderSnapshot("scrollback-up");
                app.RenderNextFrame();
                DesktopLayerTests.Drain();
                window.UpdateLayout();
                layer.Sync();
                window.UpdateLayout();

                var (windowId, band) = layer.Bands.Single(b => b.Value.IsVisible);
                var zone = terminal.ScrollbarZones()[windowId];
                var origin = layer.CellOrigin;
                var top = Canvas.GetTop(band);
                var bottom = top + band.Bounds.Height;
                var above = layer.Children.IndexOf(layer.Scrollbars) > layer.Children.IndexOf(band)
                    && ReferenceEquals(terminal.ScrollbarLayer, layer.Scrollbars);

                // A press on the track where the band lies goes to the cells' view, which owns the bar.
                var point = new Point(origin.X + zone.Center.X, (top + bottom) / 2);
                var reaches = window.InputHitTest(point) is Visual hit
                    && (ReferenceEquals(hit, terminal) || hit.GetVisualAncestors().Contains(terminal));
                return (ZoneBottom: origin.Y + zone.Bottom, Top: top, Bottom: bottom, Above: above, Reaches: reaches);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

        await Assert.That(zoneBottom).IsGreaterThanOrEqualTo(bandBottom).Because($"the track runs past the band ({bandTop}..{bandBottom}) to the pane's foot");
        await Assert.That(layerAbove).IsTrue().Because("the bars are drawn over the band, not under it");
        await Assert.That(pressReachesCells).IsTrue();
    }
}
