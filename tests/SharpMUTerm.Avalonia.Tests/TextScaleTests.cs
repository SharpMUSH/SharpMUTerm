using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The Text size page's areas in the window: each native view is drawn at its area's scale and still covers exactly the
/// rectangle it covered at 100%, the chrome beside the cells grows on the 4 px grid, and the cells move out of its way.
/// </summary>
[NotInParallel]
public class TextScaleTests
{
    [Test]
    public async Task AreasMultiplyTheZoomAndTheComposerFollowsTheCellsOwnZoom()
    {
        var scales = NativeScales.From(new DisplaySettings { Zoom = 125, InterfaceText = 110, PanelText = 150, DialogText = 90, ComposerText = 120 });

        await Assert.That(scales.Interface).IsEqualTo(1.25 * 1.1).Within(1e-9);
        await Assert.That(scales.Panels).IsEqualTo(1.25 * 1.5).Within(1e-9);
        await Assert.That(scales.Dialogs).IsEqualTo(1.25 * 0.9).Within(1e-9);
        await Assert.That(scales.Composer).IsEqualTo(1.2).Within(1e-9);
        await Assert.That(NativeScales.From(new DisplaySettings())).IsEqualTo(NativeScales.One);
    }

    [Test]
    public async Task ChromeSizesStayOnTheFourPixelGridAndAreUnchangedAt100()
    {
        var at100 = NativeLayout.Insets(HostChrome.Desktop, collapsed: false);
        var at150 = NativeLayout.Insets(HostChrome.Desktop, collapsed: false, 1.5);

        await Assert.That(at100.Top).IsEqualTo(NativeLayout.HeaderHeight);
        await Assert.That(at150.Top).IsEqualTo(NativeLayout.Scaled(NativeLayout.HeaderHeight, 1.5));
        foreach (var size in new[] { at150.Left, at150.Top, at150.Bottom, NativeLayout.Scaled(NativeLayout.HeaderHeight, 1.1) })
        {
            await Assert.That(size % 4).IsEqualTo(0);
        }

        await Assert.That(at150.Top).IsGreaterThan(at100.Top);
        await Assert.That(at150.Left).IsGreaterThan(at100.Left);
    }

    [Test]
    public async Task AScaledViewCoversItsAreaAndIsLaidOutSmaller()
    {
        var border = new Border();
        NativeScale.Cover(border, 300, 120, 1.5);

        await Assert.That(border.Width).IsEqualTo(200);
        await Assert.That(border.Height).IsEqualTo(80);
        await Assert.That(NativeScale.Of(border)).IsEqualTo(1.5);
        await Assert.That(NativeScale.Extent(border)).IsEqualTo(new Size(300, 120));

        NativeScale.Cover(border, 300, 120, 1);
        await Assert.That(border.RenderTransform).IsNull();
        await Assert.That(border.Width).IsEqualTo(300);
    }

    [Test]
    public async Task TheLayerMovesTheCellsForAScaledHeaderAndSidebar_AndTheHeaderIsDrawnLarger()
    {
        var result = await TerminalViewTests.Session.Dispatch(() =>
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
                app.RenderSnapshot("layout-combat");
                app.RenderNextFrame();
                DesktopLayerTests.Drain();
                window.UpdateLayout();
                var before = (margin: terminal.Margin, header: layer.Header.Bounds.Size, scale: NativeScale.Of(layer.Header));
                var gaugesBefore = layer.Gauges.Values.Single();
                var gaugeArea = NativeScale.Extent(gaugesBefore);

                layer.Scales = new NativeScales(1.5, 1.5, 1, 1);
                app.RenderNextFrame();
                DesktopLayerTests.Drain();
                window.UpdateLayout();
                var gauges = layer.Gauges.Values.Single();
                return (before, after: (margin: terminal.Margin, header: layer.Header.Bounds.Size, scale: NativeScale.Of(layer.Header)),
                    gaugeScale: NativeScale.Of(gauges), gaugeArea, gaugeAreaAfter: NativeScale.Extent(gauges),
                    headerOnScreen: layer.Header.Bounds.Height * NativeScale.Of(layer.Header));
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

        await Assert.That(result.before.scale).IsEqualTo(1);
        await Assert.That(result.before.margin.Top).IsEqualTo(NativeLayout.HeaderHeight);
        await Assert.That(result.after.margin.Top).IsEqualTo(NativeLayout.Scaled(NativeLayout.HeaderHeight, 1.5));
        await Assert.That(result.after.margin.Left).IsGreaterThan(result.before.margin.Left);
        await Assert.That(result.after.scale).IsEqualTo(1.5);
        await Assert.That(result.headerOnScreen).IsEqualTo(result.after.margin.Top).Within(0.01);
        await Assert.That(result.gaugeScale).IsEqualTo(1.5);
    }

    [Test]
    public async Task WithNoClientTheWindowStepsTheZoomItself_AndAClientIsAskedInstead()
    {
        var alone = new WindowTextSize();
        var said = new List<int>();
        alone.Changed += s => said.Add(s.Zoom);
        alone.RequestZoom(1);

        var asked = new List<int>();
        var withClient = new WindowTextSize();
        withClient.Changed += s => said.Add(-s.Zoom);
        ((IWindowTextSize)withClient).ZoomRequested += asked.Add;
        withClient.RequestZoom(-1);

        await Assert.That(alone.Current.Zoom).IsEqualTo(110);
        await Assert.That(said).IsEquivalentTo(new[] { 110 });
        await Assert.That(asked).IsEquivalentTo(new[] { -1 });
        await Assert.That(withClient.Current.Zoom).IsEqualTo(100);
    }
}
