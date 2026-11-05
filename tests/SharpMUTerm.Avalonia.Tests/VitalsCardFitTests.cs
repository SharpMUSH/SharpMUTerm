using Avalonia;
using Avalonia.Controls;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The side column's vitals card ends where its gauges do: the window answers its rows from the gauges' pixel height
/// (<see cref="IPointerPresenter.VitalsCardRows"/>, rounded up), and the gauges spread the part of a row left over across
/// their gaps, so the last bar ends the mockup's 16 px above the map rather than a row and more. The output pane, and so
/// what a server is told, does not move.
/// </summary>
[NotInParallel]
public class VitalsCardFitTests
{
    private static Task<(double Height, double LastBar, bool Headed, PaneRect Output, int Meters, bool Target, double Cell, int Columns, int Rows)> Card(float points, float lineHeight) =>
        TerminalViewTests.Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(140, 40);
            var terminal = new TerminalView(driver, SKColors.White, new SKColor(0x1e, 0x1e, 0x24), points, null, lineHeight);
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
                gauges.Measure(gauges.Bounds.Size);
                using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)gauges.Bounds.Width, (int)gauges.Bounds.Height));
                bitmap.Render(gauges);
                var output = app.PaneOutputRects().Single(r => app.PaneWindows().GetValueOrDefault(r.Key) == SharpMUTermApp.MainWindowId).Value;
                return (gauges.Bounds.Height, gauges.LastBarBottom, gauges.Headed, output, gauges.Model.Meters.Count, gauges.Model.Target is not null, terminal.CellExtent.Height, driver.ScreenSize.Width, driver.ScreenSize.Height);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

    [Test]
    [Arguments(10f, 1.12f)]
    [Arguments(14f, 1f)]
    [Arguments(12f, 1.3f)]
    public async Task TheLastBarEndsTheMockupsPaddingAboveTheMap(float points, float lineHeight)
    {
        var card = await Card(points, lineHeight);
        var gaps = card.Meters + (card.Target ? 1 : 0);

        await Assert.That(card.Headed).IsTrue();
        await Assert.That(card.LastBar).IsGreaterThan(0);
        var under = card.Height - card.LastBar;
        await Assert.That(under).IsGreaterThanOrEqualTo(16 - 0.5).Because($"the card is {card.Height} px on {card.Cell} px rows");
        await Assert.That(under).IsLessThan(16 + gaps).Because($"what is left of a row is spread over the gaps, not left at the foot ({card.Height} px on {card.Cell} px rows)");
        await Assert.That(card.Height).IsEqualTo(VitalsGauges.CardRows(card.Meters, card.Target, card.Cell) * card.Cell).Within(0.5);
    }

    [Test]
    public async Task TheOutputPaneIsWhereATerminalToldTheSameChromeHasIt()
    {
        var card = await Card(10f, 1.12f);

        using var headless = new HeadlessConsoleDriver(card.Columns, card.Rows);
        await using var terminal = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, WindowOptions.Capabilities) with { Chrome = HostChrome.Desktop });
        terminal.RenderSnapshot("layout-combat");
        var told = terminal.PaneOutputRects().Single(r => terminal.PaneWindows().GetValueOrDefault(r.Key) == SharpMUTermApp.MainWindowId).Value;

        await Assert.That(card.Output).IsEqualTo(told).Because("the card's rows come out of the side column, never the output pane");
    }

    [Test]
    public async Task CardRowsRoundTheGaugesHeightUp()
    {
        foreach (var cell in new[] { 14.0, 17.0, 17.92, 20.0, 22.4 })
        {
            foreach (var (meters, target) in new[] { (1, false), (3, false), (3, true), (5, true) })
            {
                var rows = VitalsGauges.CardRows(meters, target, cell);
                var needed = VitalsGauges.NeededHeight(meters, target, cell);
                await Assert.That(rows * cell).IsGreaterThanOrEqualTo(needed - 0.01);
                await Assert.That((rows - 1) * cell).IsLessThan(needed);
            }
        }
    }
}
