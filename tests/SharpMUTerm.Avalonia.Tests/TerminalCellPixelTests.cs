using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Telnet;
using SharpMUTerm.Tui;
using SharpMUTerm.Tui.Tests;
using SkiaSharp;

namespace SharpMUTerm.Avalonia.Tests;

// These cases create full client apps, as the native-composer fixture does.
[NotInParallel]
public class TerminalCellPixelTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ADisplayMetricChangeReachesAConnectedSessionWithoutAFrameOrGridResize(bool duringHandshake)
    {
        var result = await TerminalViewTests.Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 16, null);
            var config = new AppConfiguration();
            var world = new WorldDefinition { Name = "Pixels", Host = "example.invalid", Port = 4000 };
            config.Worlds.Add(world);
            var telnet = new RecordingTelnetSession();
            var app = new SharpMUTermApp(config, DesktopLayerTests.InlineHost(driver));
            app.TelnetFactory = _ => telnet;
            try
            {
                if (duringHandshake)
                {
                    telnet.Connecting = () => view.PrepareScale(2);
                }

                var before = telnet.CellPixelReports.Count;
                app.BindWorldWithoutConnecting(world).ConnectAsync().GetAwaiter().GetResult();
                if (!duringHandshake)
                {
                    before = telnet.CellPixelReports.Count;
                    view.PrepareScale(2);
                }
                var reports = telnet.CellPixelReports.Skip(before).ToArray();
                return (reports, expected: new TerminalCellSize(view.Renderer.Fonts.CellWidth, view.Renderer.Fonts.CellHeight));
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }, CancellationToken.None);
        await Assert.That(result.reports).IsEquivalentTo(new TerminalCellSize?[] { result.expected });
    }

    [Test]
    public async Task PixelChangesNotifyEvenWithoutAnArrangedGridResize()
    {
        var result = await TerminalViewTests.Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 16, null);
            var changes = 0;
            var resizes = 0;
            driver.Pointer.TerminalCellPixelsChanged += () => changes++;
            driver.ScreenResized += (_, _) => resizes++;
            // No bounds: there is no character grid change to piggyback a report on.
            view.PrepareScale(2);
            view.PrepareScale(2);
            return (changes, resizes, pixels: ((IPointerPresenter)driver.Pointer).TerminalCellPixels);
        }, CancellationToken.None);
        await Assert.That(result.changes).IsEqualTo(1);
        await Assert.That(result.resizes).IsEqualTo(0);
        await Assert.That(result.pixels!.Width).IsGreaterThan(10);
    }

    [Test]
    [Arguments(1.0)]
    [Arguments(1.25)]
    [Arguments(1.5)]
    [Arguments(2.0)]
    public async Task FreshSessionUsesPhysicalPixelsAndTracksZoomAndDpiWithoutSavingSettings(double scale)
    {
        var results = await TerminalViewTests.Session.Dispatch(() =>
        {
            using var driver = new AvaloniaConsoleDriver(80, 24);
            var view = new TerminalView(driver, SKColors.White, SKColors.Black, 16, null, 1.12f);
            var config = new AppConfiguration();
            var world = new WorldDefinition { Name = "Pixels", Host = "example.invalid", Port = 4000 };
            config.Worlds.Add(world);
            var app = new SharpMUTermApp(config, DesktopLayerTests.InlineHost(driver));
            try
            {
                var session = app.BindWorldWithoutConnecting(world);
                TerminalCellSize? Read() => session.TerminalCellSizeProvider?.Invoke();
                var initial = Read();
                view.PrepareScale(scale);
                var dpi = Read();
                var expectedDpi = new TerminalCellSize(view.Renderer.Fonts.CellWidth, view.Renderer.Fonts.CellHeight);
                view.SetZoom(200);
                var zoomed = Read();
                var expectedZoom = new TerminalCellSize(view.Renderer.Fonts.CellWidth, view.Renderer.Fonts.CellHeight);
                return (initial, dpi, expectedDpi, zoomed, expectedZoom);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }, CancellationToken.None);
        await Assert.That(results.initial).IsEqualTo(new TerminalCellSize(10, 24));
        await Assert.That(results.dpi).IsEqualTo(results.expectedDpi);
        await Assert.That(results.zoomed).IsEqualTo(results.expectedZoom);
        await Assert.That(results.zoomed!.Width).IsGreaterThan(results.dpi!.Width);
    }
}
