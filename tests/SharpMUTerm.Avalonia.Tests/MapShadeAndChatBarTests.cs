using Avalonia.Controls;
using Avalonia.Media;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Tui;
using SkiaSharp;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// Wave 30: the native map's rooms and exits lean toward the tokens' focus edge (on Dark, <c>Desktop-Combat</c>'s bluer
/// <c>#1d2333</c> and <c>#3a4257</c>), and the collapsed Chat bar at the foot of the combat column is the mockup's
/// <see cref="CollapsedBarView.BarHeight"/>, whatever the cells and the field reserve under it come to.
/// </summary>
[NotInParallel]
public class MapShadeAndChatBarTests
{
    [Test]
    public async Task OnDarkTheMapsRoomsAndExitsAreTheMockupsBlueAndTheChatBarIsItsHeight()
    {
        var (room, rule, lean, plane, barHeight, barTop, total) = await TerminalViewTests.Session.Dispatch(() =>
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
                var map = layer.Maps.Values.Single();
                var bar = layer.CollapsedBars.Values.Single();
                return (map.RoomFill, map.RuleColour, map.Lean, map.Plane, CollapsedBarView.BarHeight, bar.BarTop, bar.Bounds.Height);
            }
            finally
            {
                app.DisposeAsync().AsTask().GetAwaiter().GetResult();
                window.Close();
                driver.Dispose();
            }
        }, CancellationToken.None);

        await Assert.That(lean).IsEqualTo(NativeTokens.For(ThemeLibrary.Dark()).Focus);
        await Assert.That(room.B - room.R).IsGreaterThanOrEqualTo(15).Because("a room is a blue step of the plane, as #1d2333 is of #141822");
        await Assert.That(rule.B - rule.R).IsGreaterThanOrEqualTo(20).Because("an exit is the mockup's blue #3a4257, not a neutral grey");
        await Assert.That(room.R - plane.R).IsBetween(4, 14).Because("a room is still a quiet step of the pane's plane");
        await Assert.That(total).IsGreaterThanOrEqualTo(barHeight);
        await Assert.That(total - barTop).IsEqualTo(barHeight).Because("the bar is the mockup's height at the foot of what it covers");
    }

    [Test]
    public async Task WithNoLeanTheMapKeepsItsNeutralSteps()
    {
        var (room, rule) = await TerminalViewTests.Session.Dispatch(() =>
        {
            var driver = new AvaloniaConsoleDriver(80, 24);
            var terminal = new TerminalView(driver, SKColors.White, new SKColor(0x1e, 0x1e, 0x24), 10f, null, 1.12f);
            var map = new MapCanvas(terminal);
            var model = new SharpMUTerm.Client.Panels.MapViewModel(null, 0, [], [], null);
            map.Show(model, new PanelInk("#5fafff", "#ffaf5f", "#6a6a72"), Color.FromRgb(0x20, 0x20, 0x20), Color.FromRgb(0xe0, 0xe0, 0xe0), null);
            driver.Dispose();
            return (map.RoomFill, map.RuleColour);
        }, CancellationToken.None);

        await Assert.That(room).IsEqualTo(Color.FromRgb(0x2d, 0x2d, 0x2d));
        await Assert.That(rule.R).IsEqualTo(rule.B);
    }
}
