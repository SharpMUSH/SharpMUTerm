using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// Wave 18's window fixes: the native map opens fitted to the rooms around the character with their names on, and stays
/// where the player puts it; a pop-out button shows only while the pointer is over its panel, and a one-row strip keeps
/// its right end for it rather than drawing text under it.
/// </summary>
public class Wave18WindowTests
{
    private static readonly VitalsModel Vitals = new(
        [new VitalsMeter("hp", "412", "500", true, 412 / 500.0, false)],
        null);

    private static MapViewModel Street() => new(
        "Town",
        0,
        [
            new MapViewRoom(1, "Plaza", 0, 0, Current: true, Ahead: false, Locked: false),
            new MapViewRoom(2, "North Road", 0, -1, Current: false, Ahead: false, Locked: false),
            new MapViewRoom(3, "East Gate", 1, 0, Current: false, Ahead: false, Locked: false),
        ],
        [new MapViewExit(1, 2), new MapViewExit(1, 3)],
        null)
    {
        AnchorId = 1,
    };

    [Test]
    public async Task TheMapOpensFittedToItsRoomsWithTheirNamesAndKeepsAViewThePlayerTook()
    {
        var (fitted, zoom, names, afterWheel, movedZoom, refitted) = await DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Panels(
                [new PanelOverlay(PanelIds.Map, "map:demo", new PaneRect(0, 0, 60, 16), new PanelInk("#33ccff", "#ffaa33", "#555555"), null, Street(), _ => { })]);
            DesktopLayerTests.Drain();
            window.UpdateLayout();
            var canvas = layer.Maps.Values.Single();
            var fitted = canvas.Fitted;
            var zoom = canvas.Zoom;
            var names = canvas.NamesShown;

            var middle = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(middle, new Vector(0, -1));
            var afterWheel = canvas.Fitted;
            var movedZoom = canvas.Zoom;

            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            return (fitted, zoom, names, afterWheel, movedZoom, canvas.Fitted);
        }, columns: 60, rows: 16);

        await Assert.That(fitted).IsTrue();
        await Assert.That(zoom).IsGreaterThan(1.0).Because("three rooms in a 60-column pane are drawn larger than the old fixed scale");
        await Assert.That(names).IsTrue().Because("the names are drawn whenever a room step has room for them");
        await Assert.That(afterWheel).IsFalse().Because("a zoom is the player taking the view");
        await Assert.That(movedZoom).IsLessThan(zoom);
        await Assert.That(refitted).IsTrue().Because("a double-click re-centres and fits again");
    }

    [Test]
    public async Task APopOutButtonShowsOnlyUnderThePointerAndAStripKeepsItsCorner()
    {
        var result = await DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Panels(
            [
                new PanelOverlay(PanelIds.Vitals, "panel:vitals:strip", new PaneRect(0, 0, 40, 1), new PanelInk("#3ccf6a", "#ff5555", "#333338"), Vitals, PopOut: () => { }),
                new PanelOverlay(PanelIds.Vitals, "panel:vitals:pane", new PaneRect(0, 2, 40, 6), new PanelInk("#3ccf6a", "#ff5555", "#333338"), Vitals, PopOut: () => { }),
            ]);
            DesktopLayerTests.Drain();
            window.UpdateLayout();
            var strip = layer.PopOuts["panel:vitals:strip"];
            var pane = layer.PopOuts["panel:vitals:pane"];
            layer.SimulatePointer(null);
            var awayStrip = (strip.IsVisible, strip.Hovering);
            var awayPane = pane.IsVisible;

            var cell = view.SizeFor(1, 1);
            layer.SimulatePointer(new Point(5 * cell.Width, 4 * cell.Height));
            var overPane = (pane.IsVisible, pane.Hovering, strip.Hovering);

            var gauges = layer.Gauges["panel:vitals:strip"];
            var stripRight = Canvas.GetLeft(gauges) + gauges.Width;
            return (awayStrip, awayPane, overPane, stripRight, Canvas.GetLeft(strip), strip.Height, cell.Height);
        }, columns: 40, rows: 10);

        await Assert.That(result.awayPane).IsFalse().Because("away from the panel the button does not cover its text");
        await Assert.That(result.awayStrip.Item1).IsTrue().Because("a strip's reserved corner stays, as its own plane");
        await Assert.That(result.awayStrip.Item2).IsFalse();
        await Assert.That(result.overPane.Item1).IsTrue();
        await Assert.That(result.overPane.Item2).IsTrue();
        await Assert.That(result.overPane.Item3).IsFalse();
        await Assert.That(result.stripRight).IsLessThanOrEqualTo(result.Item5).Because("the strip's own view ends before the button's corner");
        await Assert.That(result.Item6).IsEqualTo(result.Item7);
    }
}
