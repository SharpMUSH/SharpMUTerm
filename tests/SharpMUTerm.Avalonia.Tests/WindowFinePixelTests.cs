using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System.Runtime.InteropServices;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// Wave 31, measured against <c>Desktop-Combat</c> and <c>Main.png</c> at 1440×900: the native map's exits are the mockup's
/// 2 px lines on whole device pixels, and the window's text is lightly hinted, as a browser draws it, so a panel heading's
/// capitals sit in the middle of its box rather than a pixel and a half above it.
/// </summary>
public class WindowFinePixelTests
{
    [Test]
    public async Task TheMapsExitsAreTwoWholePixelsWideWhereverNamesAreDrawn()
    {
        var (width, centres, zoomedOut) = await DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            var model = new MapViewModel(
                "Streets",
                0,
                [
                    new MapViewRoom(1, "Plaza", 0, 0, Current: true, Ahead: false, Locked: false),
                    new MapViewRoom(2, "North", 0, -1, Current: false, Ahead: false, Locked: false),
                    new MapViewRoom(3, "East", 1, 0, Current: false, Ahead: false, Locked: false),
                ],
                [new MapViewExit(1, 2), new MapViewExit(1, 3)],
                null) { AnchorId = 1 };
            ((IPointerPresenter)driver.Pointer).Panels(
                [new PanelOverlay(PanelIds.Map, "map:demo", new PaneRect(0, 0, 50, 30), new PanelInk("#33ccff", "#ffaa33", "#555555"), null, model, _ => { })]);
            DesktopLayerTests.Drain();
            window.UpdateLayout();
            var canvas = layer.Maps.Values.Single();
            var room = model.Rooms.ToDictionary(r => r.Id, canvas.CentreOf);
            var w = canvas.ExitWidth;
            var up = canvas.AlignExit(room[1] + new Vector(0.3, 0), room[2] + new Vector(0.3, 0), w);
            var across = canvas.AlignExit(room[1] + new Vector(0, 0.4), room[3] + new Vector(0, 0.4), w);
            canvas.ZoomAbout(new Rect(canvas.Bounds.Size).Center, -12);
            return (Width: w, Centres: new[] { up.From.X, up.To.X, across.From.Y, across.To.Y }, ZoomedOut: canvas.ExitWidth);
        }, columns: 50, rows: 30);

        await Assert.That(width).IsEqualTo(NativeTokens.MapExit);

        // Two device pixels centred on a pixel edge, so both columns (rows) are filled and nothing is blended.
        await Assert.That(centres.All(c => Math.Abs(c - Math.Round(c)) < 1e-9)).IsTrue().Because(string.Join(", ", centres));
        await Assert.That(centres[0]).IsEqualTo(centres[1]);
        await Assert.That(centres[2]).IsEqualTo(centres[3]);

        // Zoomed far out the lines thin, but never below a device pixel.
        await Assert.That(zoomedOut).IsGreaterThanOrEqualTo(1).And.IsLessThan(NativeTokens.MapExit);
    }

    private sealed class HeadingProbe : Control
    {
        public override void Render(DrawingContext context) =>
            PanelText.Heading(context, new PanelHeading("Here", "The Grand Plaza"), 300, 20, Color.FromRgb(20, 24, 34), Colors.White);
    }

    [Test]
    public async Task APanelHeadingsCapitalsAreCentredOverItsRuleInTheWindowsHinting()
    {
        var (top, bottom) = await TerminalViewTests.Session.Dispatch(() =>
        {
            var probe = new HeadingProbe { Width = 300, Height = PanelText.PanelHeadingHeight };
            var host = new Border { Child = probe };
            NativeTokens.ApplyTextRendering(host);
            var window = new Window { Content = host, Width = 300, Height = PanelText.PanelHeadingHeight };
            window.Show();
            window.UpdateLayout();
            using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(300, (int)PanelText.PanelHeadingHeight));
            bitmap.Render(host);
            // Bgra8888, premultiplied: the red channel is index 2 of each pixel.
            var height = (int)PanelText.PanelHeadingHeight;
            var pixels = new byte[300 * 4 * height];
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                bitmap.CopyPixels(new PixelRect(0, 0, 300, height), handle.AddrOfPinnedObject(), pixels.Length, 300 * 4);
            }
            finally
            {
                handle.Free();
            }

            window.Close();

            // The title's ink: "Here" has no descender, so its rows are its capitals and its x-height.
            var inked = Enumerable.Range(0, height - 2)
                .Where(y => Enumerable.Range((int)PanelText.Pad, 34).Any(x => pixels[(((y * 300) + x) * 4) + 2] > 120))
                .ToList();
            return (inked.First(), inked.Last());
        }, CancellationToken.None);

        // Main.png's "Here": capitals 9–10 rows tall, their middle on the box's (above the rule) to within a pixel. Avalonia's
        // default hinting drew them 11 rows tall, 1.5 px above it.
        // Windows' rasteriser grows the same glyphs a row taller whatever the hinting, so the height is held there to 11;
        // the centring is held on every platform.
        var middle = (PanelText.PanelHeadingHeight - NativeTokens.Hairline) / 2;
        await Assert.That(bottom - top + 1).IsLessThanOrEqualTo(OperatingSystem.IsWindows() ? 11 : 10);
        await Assert.That(Math.Abs(((top + bottom + 1) / 2.0) - middle)).IsLessThanOrEqualTo(1);
    }
}
