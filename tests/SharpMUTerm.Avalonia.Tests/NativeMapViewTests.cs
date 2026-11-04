using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The native map's look buttons, ⇧-wheel and legend (another level or area, and back), a room dragged to another grid
/// cell while editing, and the inspector's reset to the server: each handed to the client as the model's own
/// <see cref="MapLook"/> or <see cref="MapEdit"/>.
/// </summary>
public class NativeMapViewTests
{
    private static MapViewModel Model(bool editing, bool viewing = false) => new(
        "Town",
        0,
        [
            new MapViewRoom(1, "Plaza", 0, 0, Current: !viewing, Ahead: false, Locked: false, Selected: editing),
            new MapViewRoom(2, "North Road", 0, -1, Current: false, Ahead: false, Locked: false, HandEdited: true),
        ],
        [new MapViewExit(1, 2)],
        null)
    {
        AnchorId = 1,
        Viewing = viewing,
        Levels = [0, 1],
        Areas = [new MapViewArea("Town", "#5fafff", Drawn: true), new MapViewArea("Docks", "#ffaf5f", Drawn: false)],
        AreaChoices = [new MapViewArea("Docks", "#ffaf5f", Drawn: false), new MapViewArea("Town", "#5fafff", Drawn: true)],
        Selected = editing ? new MapRoomDetails(1, "Plaza", "Town", 0, 0, 0, 1, false, null, null, []) { HandEdited = [Core.Mapping.MapFields.Name] } : null,
    };

    private sealed record Seen(List<MapLook> Looks, List<MapEdit> Edits, List<long?> Selects);

    private static Seen Publish(AvaloniaConsoleDriver driver, MapViewModel model)
    {
        var seen = new Seen([], [], []);
        ((IPointerPresenter)driver.Pointer).Panels(
        [
            new PanelOverlay(
                PanelIds.Map, "map:demo", new PaneRect(0, 1, 60, 14), new PanelInk("#33ccff", "#ffaa33", "#555555"), null, model, _ => { },
                SelectRoom: seen.Selects.Add, EditMap: seen.Edits.Add, LookMap: seen.Looks.Add),
        ]);
        return seen;
    }

    [Test]
    public async Task TheButtonsTheWheelAndTheLegendLookElsewhereAndTheWayBackIsOffered()
    {
        var (looks, labels, menu) = await DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            var seen = Publish(driver, Model(editing: false, viewing: true));
            Dispatcher();
            window.UpdateLayout();
            var canvas = layer.Maps.Values.Single();
            var labels = canvas.LookButtons().Select(b => (b.Id, b.Enabled)).ToList();
            Point Centre(MapCanvas.LookButton b) => canvas.TranslatePoint(b.Rect.Center, window)!.Value;

            var up = canvas.LookButtons().Single(b => b.Id == MapCanvas.UpButton);
            window.MouseDown(Centre(up), MouseButton.Left);
            window.MouseUp(Centre(up), MouseButton.Left);
            var here = canvas.LookButtons().Single(b => b.Id == MapCanvas.HereButton);
            window.MouseDown(Centre(here), MouseButton.Left);
            window.MouseUp(Centre(here), MouseButton.Left);

            var middle = canvas.TranslatePoint(new Point(canvas.Bounds.Width / 2, canvas.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(middle, new Vector(0, 1), RawInputModifiers.Shift);

            canvas.OpenAreaMenu();
            canvas.RunAreaVerb(canvas.Model, MapLooking.AreaVerbPrefix + "0");
            return (seen.Looks, labels, canvas.OpenedAreaMenu);
        }, columns: 60, rows: 16);

        await Assert.That(labels).IsEquivalentTo(new[]
        {
            (MapCanvas.UpButton, true), (MapCanvas.DownButton, false), (MapCanvas.AreasButton, true), (MapCanvas.HereButton, true),
        });
        await Assert.That(looks).IsEquivalentTo(new[] { MapLook.Up, MapLook.Here, MapLook.Up, MapLook.ToArea("Docks") });
        await Assert.That(menu!.Entries.Select(e => e.Label)).Contains("Back to where I am");
    }

    [Test]
    public async Task ARoomDraggedWhileEditingIsMovedByTheGridCellsItCameToInOneEdit()
    {
        var seen = await DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            var seen = Publish(driver, Model(editing: true));
            Dispatcher();
            window.UpdateLayout();
            var canvas = layer.Maps.Values.Single();
            var room = canvas.Model.Rooms[1];
            var from = canvas.TranslatePoint(canvas.CentreOf(room), window)!.Value;
            var step = MapCanvas.Spacing * canvas.Zoom;

            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from + new Vector(step * 0.6, 0));
            window.MouseMove(from + new Vector((step * 2) + 3, step - 4));
            window.MouseUp(from + new Vector((step * 2) + 3, step - 4), MouseButton.Left);
            return (seen, canvas.Pan);
        }, columns: 60, rows: 16);

        await Assert.That(seen.seen.Edits).IsEquivalentTo(new[] { MapEdit.Move(2, 2, 1) });
        await Assert.That(seen.seen.Selects).Contains(2L);
        await Assert.That(seen.Pan).IsEqualTo(default(Vector)).Because("dragging a room moves the room, not the map");
    }

    [Test]
    public async Task OutsideEditingADragStillPansAndMovesNothing()
    {
        var seen = await DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            var seen = Publish(driver, Model(editing: false));
            Dispatcher();
            window.UpdateLayout();
            var canvas = layer.Maps.Values.Single();
            var from = canvas.TranslatePoint(canvas.CentreOf(canvas.Model.Rooms[1]), window)!.Value;
            window.MouseDown(from, MouseButton.Left);
            window.MouseMove(from + new Vector(80, 0));
            window.MouseUp(from + new Vector(80, 0), MouseButton.Left);
            return (seen, canvas.Pan);
        }, columns: 60, rows: 16);

        await Assert.That(seen.seen.Edits).IsEmpty();
        await Assert.That(seen.Pan).IsEqualTo(new Vector(80, 0));
    }

    [Test]
    public async Task TheInspectorSaysWhatWasEditedByHandAndResetsItToTheServer()
    {
        var (text, enabled, edits) = await DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            var seen = Publish(driver, Model(editing: true));
            Dispatcher();
            window.UpdateLayout();
            var inspector = layer.Maps.Values.Single().Inspector;
            var enabled = inspector.ResetButton.IsEnabled;
            inspector.ResetButton.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            return (inspector.HandEditedText, enabled, seen.Edits);
        }, columns: 60, rows: 16);

        await Assert.That(text).Contains("name");
        await Assert.That(enabled).IsTrue();
        await Assert.That(edits).IsEquivalentTo(new[] { MapEdit.ResetToServer(1) });
    }

    [Test]
    public async Task TheRoomMenuOffersResetOnlyForARoomEditedByHand()
    {
        var model = Model(editing: false);

        await Assert.That(MapEditing.Menu(model.Rooms[1], null).Entries.Select(e => e.Command)).Contains(MapEditing.ResetVerb);
        await Assert.That(MapEditing.Menu(model.Rooms[0], null).Entries.Select(e => e.Command)).DoesNotContain(MapEditing.ResetVerb);
    }

    private static void Dispatcher() => global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
}
