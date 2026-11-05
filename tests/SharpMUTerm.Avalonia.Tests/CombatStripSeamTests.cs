using Avalonia.Controls;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// In the window the combat layout's strips are the panels' plane, as the mockup's side column is, and a strip draws a
/// hairline on each edge another pane meets (<see cref="PanelOverlay.RuleAbove"/>/<c>RuleBelow</c>): the vitals card over
/// the map has one under it, the quick bar under the output one over it. The terminal keeps the recessed step.
/// </summary>
[NotInParallel]
public class CombatStripSeamTests
{
    [Test]
    public async Task TheVitalsCardIsTheMapsPlaneWithARuleBetweenThem()
    {
        var (vitals, quick, samePlane) = await TerminalViewTests.Session.Dispatch(() =>
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
                var gauges = layer.Gauges.Values.Single();
                var map = layer.Maps.Values.Single();
                return (gauges.Seams, layer.QuickBars.Values.Single().Seams, gauges.Plane == map.Plane);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

        await Assert.That(samePlane).IsTrue().Because("an unfocused strip is not recessed where the host draws strips");
        await Assert.That(vitals.Below).IsTrue();
        await Assert.That(vitals.Above).IsFalse().Because("nothing is above the side column's card but the header");
        await Assert.That(quick.Above).IsTrue();
    }
}
