using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Combat's side column where the host draws it natively (<see cref="HostChrome.Desktop"/>): the vitals card is as tall as
/// its gauges and grows only when a target arrives, and the map's pane draws no tab strip. The terminal keeps its rules.
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class MapColumnTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp Demo(HostChrome chrome)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        return new SharpMUTermApp(config, ClientHost.Headless(new HeadlessConsoleDriver(120, 36), Caps) with { Chrome = chrome });
    }

    [Test]
    public async Task TheWindowsMapPaneDrawsNoTabStripAndItsVitalsCardIsAsTallAsItsGauges()
    {
        await using var app = Demo(HostChrome.Desktop);
        app.RenderSnapshot("layout-combat");

        var whole = app.PaneSnapshot().Rects;
        var output = app.PaneOutputRects();
        await Assert.That(output["panel:map"].Height).IsEqualTo(whole["panel:map"].Height).Because("no tab strip over the map: its heading names it");
        await Assert.That(whole["panel:vitals"].Height).IsEqualTo(PanelSizing.VitalsGaugeCardRows(3, target: false));
        await Assert.That(whole["panel:map"].Y).IsEqualTo(whole["panel:vitals"].Y + whole["panel:vitals"].Height);
    }

    [Test]
    public async Task TheTerminalKeepsTheMapsTabStripAndTheReservedCard()
    {
        await using var app = Demo(HostChrome.None);
        app.RenderSnapshot("layout-combat");

        var whole = app.PaneSnapshot().Rects;
        var output = app.PaneOutputRects();
        await Assert.That(output["panel:map"].Height).IsLessThan(whole["panel:map"].Height);
        await Assert.That(whole["panel:vitals"].Height).IsEqualTo(PanelSizing.VitalsCardRows(3));
    }

    [Test]
    public async Task ATargetArrivingGrowsTheWindowsCardByItsRows()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        config.LastSession = null;
        config.Worlds[0].Layout = BuiltInLayouts.CombatId;
        var time = new ManualTimeProvider();
        await using var app = new SharpMUTermApp(
            config, ClientHost.Headless(new HeadlessConsoleDriver(120, 36), Caps) with { Chrome = HostChrome.Desktop }, time, autoLayout: true);
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        session.GameData.SetGmcp("Char.Vitals", """{"hp":"412","maxhp":"500","mp":"23","maxmp":"120"}""");
        time.Advance(SharpMUTermApp.LayoutRefoldDelay);
        app.RenderNextFrame();

        var meters = app.ReadVitals(session.SessionKey)!.Meters.Count;
        int? Card() => app.PaneFixedRows("panel:vitals");
        await Assert.That(Card()).IsEqualTo(PanelSizing.VitalsGaugeCardRows(meters, target: false));

        session.GameData.SetGmcp("Char.Status", """{"enemy":"a rotting ghoul","enemypct":"40"}""");
        app.RenderNextFrame();
        await Assert.That(Card()).IsEqualTo(PanelSizing.VitalsGaugeCardRows(meters, target: true));

        var rebuilds = app.PaneAreaRebuilds;
        session.GameData.SetGmcp("Char.Status", """{"enemy":"a rotting ghoul","enemypct":"30"}""");
        await Assert.That(app.PaneAreaRebuilds).IsEqualTo(rebuilds).Because("the same rows: no reflow");
    }
}
