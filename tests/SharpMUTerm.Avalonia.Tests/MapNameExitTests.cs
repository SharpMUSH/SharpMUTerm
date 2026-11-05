using Avalonia;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// A room's name on the native map never crosses a line the map draws: every exit between two rooms of the view (not only
/// the named room's own) and every spur into another area are obstacles, and a name that has no clear place is elided or
/// left to the hover rather than drawn across one. Checked over the demo's streets and a dense synthetic grid, at several
/// pane sizes and zooms.
/// </summary>
public class MapNameExitTests
{
    private static readonly (int Columns, int Rows)[] Panes = [(60, 30), (44, 20), (30, 16), (26, 14), (80, 40)];

    private static readonly double[] Notches = [0, 1, 2, 3, -1];

    /// <summary>The demo's recorded streets, as the client publishes them in the Combat layout.</summary>
    private static async Task<MapViewModel> DemoStreets()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = DesktopLayerTests.InlineWindowApp(driver);
        app.RenderSnapshot("layout-combat");
        return driver.Pointer.Panels.Single(p => p.PanelId == PanelIds.Map).Map!;
    }

    /// <summary>
    /// A 9×9 grid of rooms with long names, every room joined to its east and south neighbours, a diagonal through every
    /// other square, and a few rooms leading into another area: as crowded as a city map gets.
    /// </summary>
    private static MapViewModel DenseGrid()
    {
        const int side = 9;
        var rooms = new List<MapViewRoom>();
        var exits = new List<MapViewExit>();
        long Id(int x, int y) => (y * side) + x + 1;
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                rooms.Add(new MapViewRoom(
                    Id(x, y),
                    $"{(x % 3 == 0 ? "The Long Arcade of" : "Lane")} {x}-{y}",
                    x - 4,
                    y - 4,
                    Current: x == 4 && y == 4,
                    Ahead: false,
                    Locked: false,
                    Elsewhere: (x + y) % 7 == 0 ? "Docks" : null));
                if (x + 1 < side)
                {
                    exits.Add(new MapViewExit(Id(x, y), Id(x + 1, y)));
                }

                if (y + 1 < side)
                {
                    exits.Add(new MapViewExit(Id(x, y), Id(x, y + 1)));
                }

                if (x + 1 < side && y + 1 < side && (x + y) % 2 == 0)
                {
                    exits.Add(new MapViewExit(Id(x, y), Id(x + 1, y + 1)));
                }
            }
        }

        return new MapViewModel("City", 0, rooms, exits, null)
        {
            AnchorId = Id(4, 4),
            Areas = [new MapViewArea("City", "#5aa9e6", true), new MapViewArea("Docks", "#ff8a65", false)],
        };
    }

    /// <summary>Every name rectangle that crosses a line, at every pane size and zoom, as readable failures.</summary>
    private static Task<List<string>> Crossings(MapViewModel model) =>
        Task.WhenAll(Panes.Select(pane => DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Panels(
                [new PanelOverlay(PanelIds.Map, "map:demo", new PaneRect(0, 0, pane.Columns, pane.Rows), new PanelInk("#33ccff", "#ffaa33", "#555555"), null, model, _ => { })]);
            DesktopLayerTests.Drain();
            window.UpdateLayout();
            var canvas = layer.Maps.Values.Single();
            var bounds = new Rect(canvas.Bounds.Size);
            var found = new List<string>();
            var named = 0;
            foreach (var notches in Notches)
            {
                if (notches != 0)
                {
                    canvas.ZoomAbout(bounds.Center, notches);
                }

                var segments = canvas.ExitSegments();
                foreach (var name in canvas.Labels(bounds))
                {
                    named++;
                    var plate = MapCanvas.PlateOf(name.Rect);
                    foreach (var (from, to) in segments.Where(s => MapCanvas.Crosses(s.From, s.To, plate)))
                    {
                        found.Add($"{pane.Columns}x{pane.Rows} zoom {canvas.Zoom:0.00}: room {name.Room}'s name at {name.Rect} crosses {from}→{to}");
                    }
                }
            }

            if (named == 0)
            {
                found.Add($"{pane.Columns}x{pane.Rows}: no name was placed at all, which proves nothing");
            }

            return found;
        }, columns: pane.Columns, rows: pane.Rows))).ContinueWith(t => t.Result.SelectMany(f => f).ToList());

    [Test]
    public async Task NoNameOnTheDemoStreetsCrossesAnExitOrASpur()
    {
        var crossings = await Crossings(await DemoStreets());
        await Assert.That(crossings).IsEmpty().Because(string.Join("\n", crossings.Take(10)));
    }

    [Test]
    public async Task NoNameOnADenseGridCrossesAnExitOrASpur()
    {
        var crossings = await Crossings(DenseGrid());
        await Assert.That(crossings).IsEmpty().Because(string.Join("\n", crossings.Take(10)));
    }

    [Test]
    public async Task ADenseGridStillNamesTheCharactersRoomOrLeavesItToTheHover()
    {
        // Strictness costs names, never the guarantee: whatever is placed is clear, and the grid still names some rooms.
        var names = await DesktopLayerTests.Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Panels(
                [new PanelOverlay(PanelIds.Map, "map:demo", new PaneRect(0, 0, 80, 40), new PanelInk("#33ccff", "#ffaa33", "#555555"), null, DenseGrid(), _ => { })]);
            DesktopLayerTests.Drain();
            window.UpdateLayout();
            var canvas = layer.Maps.Values.Single();
            return canvas.Labels(new Rect(canvas.Bounds.Size)).Count;
        }, columns: 80, rows: 40);

        await Assert.That(names).IsGreaterThan(0);
    }
}
