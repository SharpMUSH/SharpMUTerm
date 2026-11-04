using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Mapping;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The terminal's half of two surfaces: the notice, whose status-row markup is what it always was now that it is drawn
/// from a <see cref="NoticeModel"/>; and the map, whose cells take their area colours, stairs and legend from the
/// <see cref="MapViewModel"/> the window's canvas is handed.
/// </summary>
[NotInParallel]
public class NoticeAndMapSurfaceTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App()
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(new HeadlessConsoleDriver(120, 34), Caps));
    }

    // ── notices ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The row reads exactly as before the model: a key chip in the notice ink, the text dim — or in the warning red when
    /// something failed — and an action changes nothing on it, since a cell is not a button.
    /// </summary>
    [Test]
    public async Task TheStatusRowDrawsANoticeAsItAlwaysHas()
    {
        await using var app = App();
        app.RenderSnapshot(null);

        app.SimulateNotice("nothing to split", MessageSeverity.Warning, "⌃B", null);
        var plain = app.LastNotice!;
        await Assert.That(plain).EndsWith("]⌃B[/] [dim]nothing to split[/]");
        var chip = plain[..plain.IndexOf("⌃B", StringComparison.Ordinal)];

        app.SimulateNotice("nothing to split", MessageSeverity.Warning, "⌃B", new NoticeAction("layout:layouts", "Change layout"));
        await Assert.That(app.LastNotice).IsEqualTo(plain).Because("the action is the window's to offer; the row is unchanged");

        app.SimulateNotice("no route to host", MessageSeverity.Error, null, null);
        await Assert.That(app.LastNotice).IsEqualTo($"[{ScreenPalette.Warn}]no route to host[/]");

        app.SimulateNotice("saved [x]", MessageSeverity.Info, "F5", null);
        await Assert.That(app.LastNotice).IsEqualTo($"{chip}F5[/] [dim]saved [[x]][/]");
    }

    /// <summary>The model the row was drawn from is recorded as the notice surface's drawn model, sequence and all.</summary>
    [Test]
    public async Task EachNoticeIsOneModelTheRowIsDrawnFrom()
    {
        await using var app = App();
        app.RenderSnapshot(null);
        var action = new NoticeAction("layout:layouts", "Change layout");

        app.SimulateNotice("first", MessageSeverity.Info, "⌥M", action);
        var first = (NoticeModel?)app.DrawnModel(SurfaceCatalog.Notice);
        app.SimulateNotice("second", MessageSeverity.Error, null, null);
        var second = (NoticeModel?)app.DrawnModel(SurfaceCatalog.Notice);

        await Assert.That(first).IsNotNull();
        await Assert.That(first!.Severity).IsEqualTo(NoticeSeverity.Info);
        await Assert.That(first.Action).IsEqualTo(action);
        await Assert.That(first.Key).IsEqualTo("⌥M");
        await Assert.That(second!.Severity).IsEqualTo(NoticeSeverity.Warning).Because("the log's Error is the notice's warning");
        await Assert.That(second.Sequence).IsEqualTo(first.Sequence + 1);
        await Assert.That(app.NoticeMarkup(second)).IsEqualTo(app.LastNotice);
        await Assert.That(SharpMUTermApp.NoticeSeverityOf(MessageSeverity.Warning)).IsEqualTo(NoticeSeverity.Refusal);
        await Assert.That(app.Messages.Entries.Select(e => e.Text)).Contains("second").Because("the message log stays the history");
    }

    /// <summary>A notice action runs only if a notice offered it, and the terminal offers none: it has no toast to click.</summary>
    [Test]
    public async Task ANoticeActionNoNoticeOfferedIsRefused()
    {
        await using var app = App();
        app.RenderSnapshot(null);
        app.SimulateNotice("first", MessageSeverity.Info, "⌥M", new NoticeAction("layout:layouts", "Change layout"));

        await Assert.That(app.RunNoticeAction("layout:layouts")).IsFalse().Because("a terminal host publishes no notice, so offers no click");
        await Assert.That(app.LastNotice).Contains("no notice offered");
    }

    // ── the map ──────────────────────────────────────────────────────────────────────────────

    private static (WorldMap Map, MapViewModel View) TwoAreas(MapAreaPalette palette)
    {
        var map = new WorldMap();
        var town = map.AreaNamed("Town");
        var docks = map.AreaNamed("Docks");
        map.Add(new MapRoom { Id = 1, Name = "Plaza", AreaId = town.Id, Exits = { ["east"] = 2, ["south"] = 3 } });
        map.Add(new MapRoom { Id = 2, Name = "Stair", AreaId = town.Id, X = 1, Exits = { ["west"] = 1, ["up"] = 4 } });
        map.Add(new MapRoom { Id = 3, Name = "Quay", AreaId = docks.Id, Y = 1, Exits = { ["north"] = 1 } });
        map.Add(new MapRoom { Id = 4, Name = "Loft", AreaId = town.Id, X = 1, Z = 1, Exits = { ["down"] = 2 } });
        return (map, MapViewModel.From(map, 1, [], null, palette));
    }

    /// <summary>
    /// Rooms in the drawn area's colour, the way into another area in that area's colour, and the legend naming both —
    /// every colour the view's own. A view handed made-up colours draws exactly those, so the cells read the model and
    /// not a palette of their own.
    /// </summary>
    [Test]
    public async Task TheCellMapDrawsTheViewsAreaColoursStairsAndLegend()
    {
        var (map, view) = TwoAreas(MapAreaPalette.Dark);
        var invented = view with
        {
            Areas = [new MapViewArea("Town", "#abcdef", Drawn: true), new MapViewArea("Docks", "#fedcba", Drawn: false)],
        };
        var ink = new MapRenderer.Ink("#ffffff", "#00ff00", "#ffff00", "#ff00ff");

        var lines = MapRenderer.Render(invented, map, 40, 9, ink);
        var all = string.Join('\n', lines);

        await Assert.That(all).Contains("[link=mux:map:2][#abcdef][[↑]][/][/]").Because("a room wears its area's colour and the view's stairs");
        await Assert.That(all).Contains("[#fedcba]│[/]").Because("the way south into the docks is drawn in the docks' colour");
        await Assert.That(MarkupText.Plain(lines[^1])).IsEqualTo("@ you · ↑↓ stairs · ■ Town · ■ Docks");
        await Assert.That(lines[^1]).Contains("[#abcdef]■[/]");
        await Assert.That(lines[^1]).Contains("[#fedcba]■[/]");

        // The stairs mark is the view's: a room the view says has none draws none, whatever the map holds.
        var flat = invented with { Rooms = invented.Rooms.Select(r => r with { Up = false, Down = false }).ToList() };
        await Assert.That(string.Join('\n', MapRenderer.Render(flat, map, 40, 9, ink))).DoesNotContain("↑");
    }

    /// <summary>On every built-in theme the floor leaves the palette's colours as the model says them.</summary>
    [Test]
    public async Task TheFloorLeavesThePalettesAreaColoursAlone()
    {
        foreach (var name in Core.Theming.ThemeLibrary.Names)
        {
            var theme = Core.Theming.ThemeLibrary.Get(name);
            var (map, view) = TwoAreas(MapAreaPalette.For(WorkspacePalette.DarkPanes(theme)));
            var ink = new MapRenderer.Ink("#ffffff", "#00ff00", "#ffff00", "#ff00ff", WorkspacePalette.ReadingPlane(theme));
            var all = string.Join('\n', MapRenderer.Render(view, map, 40, 9, ink));
            await Assert.That(all).Contains($"[{view.AreaColour}]").Because(name);
            await Assert.That(all).Contains($"[{view.ColourOf("Docks")}]").Because(name);
        }
    }

    /// <summary>The demo's map window draws from the model the client recorded for it, area colour included.</summary>
    [Test]
    public async Task TheDemoMapIsDrawnFromItsRecordedModel()
    {
        await using var app = App();
        app.RenderSnapshot("map");
        var window = SharpMUTermApp.MapWindowId(DemoScene.ActiveSessionKey);
        var view = (MapViewModel?)app.DrawnModel(Core.Layouts.PanelIds.Map, window);

        await Assert.That(view).IsNotNull();
        await Assert.That(view!.Areas.Select(a => a.Name)).IsEquivalentTo(new[] { "Aetherfall", "Saltmarsh Docks" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(view.Rooms.Single(r => r.Name == "Temple Steps").Up).IsTrue();
        var markup = string.Join('\n', app.MapMarkup(window));
        await Assert.That(markup).Contains($"[{view.AreaColour}]");
        await Assert.That(markup).Contains($"[{view.ColourOf("Saltmarsh Docks")}]■[/]");
    }
}
