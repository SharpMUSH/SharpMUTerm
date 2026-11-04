using Avalonia;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The native map uses its pane: rooms toward the mockup's 32 px, names wider than their rooms placed where they cover no
/// room and no other name, and elided only where they would.
/// </summary>
public class MapNamesTests
{
    private static MapViewModel Plaza() => new(
        "Town",
        0,
        [
            new MapViewRoom(1, "The Grand Plaza", 0, 0, Current: true, Ahead: false, Locked: false),
            new MapViewRoom(2, "Temple of the Tide", 0, -1, Current: false, Ahead: false, Locked: false),
            new MapViewRoom(3, "Market Row", 1, 0, Current: false, Ahead: false, Locked: false),
            new MapViewRoom(4, "Guildhall", -1, 0, Current: false, Ahead: false, Locked: false),
            new MapViewRoom(5, "Lantern Street", 0, 1, Current: false, Ahead: false, Locked: false),
            new MapViewRoom(6, "Spice Stall", 2, 0, Current: false, Ahead: false, Locked: false),
        ],
        [new MapViewExit(1, 2), new MapViewExit(1, 3), new MapViewExit(1, 4), new MapViewExit(1, 5), new MapViewExit(3, 6)],
        null)
    {
        AnchorId = 1,
    };

    private static Task<(double Room, IReadOnlyList<MapCanvas.PlacedName> Names, List<Rect> Rooms)> Fit(MapViewModel model, int columns, int rows) =>
        DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Panels(
                [new PanelOverlay(PanelIds.Map, "map:demo", new PaneRect(0, 0, columns, rows), new PanelInk("#33ccff", "#ffaa33", "#555555"), null, model, _ => { })]);
            DesktopLayerTests.Drain();
            window.UpdateLayout();
            var canvas = layer.Maps.Values.Single();
            var size = canvas.RoomSize;
            var rooms = canvas.Model.Rooms.Select(r => canvas.CentreOf(r)).Select(c => new Rect(c.X - (size / 2), c.Y - (size / 2), size, size)).ToList();
            return (size, canvas.Labels(new Rect(canvas.Bounds.Size)), rooms);
        }, columns: columns, rows: rows);

    [Test]
    public async Task APaneWithRoomDrawsTheMockupsRoomsAndEveryNameWholeWithNoTwoTouching()
    {
        var (room, names, rooms) = await Fit(Plaza(), 60, 30);

        await Assert.That(room).IsEqualTo(MapCanvas.RoomTarget).Because("a pane this size fits the rooms at the mockup's size");
        await Assert.That(names.Count).IsEqualTo(6);
        await Assert.That(names.Any(n => n.Elided)).IsFalse();
        await Assert.That(names.Any(n => n.Rect.Width > room)).IsTrue().Because("a name is drawn wider than its room");
        for (var i = 0; i < names.Count; i++)
        {
            await Assert.That(rooms.Any(r => r.Intersects(names[i].Rect))).IsFalse();
            for (var j = i + 1; j < names.Count; j++)
            {
                await Assert.That(names[i].Rect.Intersects(names[j].Rect)).IsFalse();
            }
        }
    }

    [Test]
    public async Task ACrampedPaneStillLetsNoNameCoverAnother()
    {
        var (_, names, rooms) = await Fit(Plaza(), 26, 14);

        for (var i = 0; i < names.Count; i++)
        {
            await Assert.That(rooms.Any(r => r.Intersects(names[i].Rect))).IsFalse();
            for (var j = i + 1; j < names.Count; j++)
            {
                await Assert.That(names[i].Rect.Intersects(names[j].Rect)).IsFalse();
            }
        }

        await Assert.That(names.Any(n => n.Room == 1)).IsTrue().Because("the character's room is named first");
    }

    [Test]
    public async Task ASegmentCrossesOnlyTheRectanglesItPassesThrough()
    {
        var box = new Rect(10, 10, 10, 10);
        await Assert.That(MapCanvas.Crosses(new Point(0, 15), new Point(30, 15), box)).IsTrue();
        await Assert.That(MapCanvas.Crosses(new Point(0, 0), new Point(30, 0), box)).IsFalse();
        await Assert.That(MapCanvas.Crosses(new Point(0, 0), new Point(30, 30), box)).IsTrue();
        await Assert.That(MapCanvas.Crosses(new Point(0, 30), new Point(5, 25), box)).IsFalse();
    }
}
