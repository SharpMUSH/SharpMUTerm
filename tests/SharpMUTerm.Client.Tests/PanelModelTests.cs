using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Client.Tests;

/// <summary>The view-models both hosts draw native or cell views from, and the layout library's command ids.</summary>
public class PanelModelTests
{
    [Test]
    public async Task TwoVitalsModelsWithTheSameMetersAreEqual()
    {
        static VitalsModel Make() => new([new VitalsMeter("hp", "10", "20", true, 0.5, false)], new VitalsTarget("a rat", 0.1));

        await Assert.That(Make()).IsEqualTo(Make());
        await Assert.That(Make().GetHashCode()).IsEqualTo(Make().GetHashCode());
        await Assert.That(Make().Target!.Low).IsTrue();
        await Assert.That(new VitalsMeter("mp", "1", null, false, null, false).Title).IsEqualTo("Mana");
        await Assert.That(new VitalsMeter("rage", "1", null, false, null, false).Title).IsEqualTo("rage");
    }

    [Test]
    public async Task TheMapViewIsTheCurrentLevelAndAreaWithEachExitOnce()
    {
        var map = new WorldMap();
        map.Add(new MapRoom { Id = 1, Name = "Plaza", AreaId = 1, X = 0, Y = 0, Exits = { ["north"] = 2, ["up"] = 3 } });
        map.Add(new MapRoom { Id = 2, Name = "Road", AreaId = 1, X = 0, Y = -1, Exits = { ["south"] = 1 } });
        map.Add(new MapRoom { Id = 3, Name = "Tower", AreaId = 1, X = 0, Y = 0, Z = 1 });
        map.Add(new MapRoom { Id = 4, Name = "Elsewhere", AreaId = 2, X = 5, Y = 5 });

        var view = MapViewModel.From(map, 1, [2], "walking, 1 to go");

        await Assert.That(view.Rooms.Select(r => r.Id)).IsEquivalentTo(new long[] { 1, 2 });
        await Assert.That(view.Exits).IsEquivalentTo(new[] { new MapViewExit(1, 2) });
        await Assert.That(view.Current!.Name).IsEqualTo("Plaza");
        await Assert.That(view.Rooms.Single(r => r.Id == 2).Ahead).IsTrue();
        await Assert.That(MapViewModel.From(map, 1, [2], "walking, 1 to go")).IsEqualTo(view);
    }

    /// <summary>
    /// The view says which rooms have stairs, which lead into another area, and the colour of every area it names —
    /// the drawn area first, from the palette the client chose, stable for the name.
    /// </summary>
    [Test]
    public async Task TheMapViewCarriesStairsWaysOutAndAreaColours()
    {
        var map = new WorldMap();
        var town = map.AreaNamed("Town");
        var docks = map.AreaNamed("Docks");
        map.Add(new MapRoom { Id = 1, Name = "Plaza", AreaId = town.Id, Exits = { ["north"] = 2, ["up"] = 3, ["south"] = 4 } });
        map.Add(new MapRoom { Id = 2, Name = "Cellar Stair", AreaId = town.Id, Y = -1, Exits = { ["south"] = 1, ["east"] = 5 } });
        map.Add(new MapRoom { Id = 3, Name = "Tower", AreaId = town.Id, Z = 1, Exits = { ["down"] = 1 } });
        map.Add(new MapRoom { Id = 4, Name = "Quay", AreaId = docks.Id, Y = 1, Exits = { ["north"] = 1 } });
        map.Add(new MapRoom { Id = 5, Name = "Crypt", AreaId = town.Id, X = 1, Y = -1, Z = -1 });

        var view = MapViewModel.From(map, 1, [], null, MapAreaPalette.Light);

        var plaza = view.Rooms.Single(r => r.Id == 1);
        var stair = view.Rooms.Single(r => r.Id == 2);
        await Assert.That(plaza.Up).IsTrue();
        await Assert.That(plaza.Down).IsFalse();
        await Assert.That(stair.Down).IsTrue().Because("an exit to a room on a lower level is a way down whatever its direction");
        await Assert.That(plaza.Elsewhere).IsEqualTo("Docks");
        await Assert.That(stair.Elsewhere).IsNull();

        await Assert.That(view.Areas).IsEquivalentTo(
            new[]
            {
                new MapViewArea("Town", MapAreaPalette.Light.Colour("Town"), Drawn: true),
                new MapViewArea("Docks", MapAreaPalette.Light.Colour("Docks"), Drawn: false),
            },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(view.AreaColour).IsEqualTo(MapAreaPalette.Light.Colour("Town"));
        await Assert.That(view.ColourOf("Docks")).IsEqualTo(MapAreaPalette.Light.Colour("Docks"));
        await Assert.That(MapAreaPalette.IndexOf("Town")).IsEqualTo(MapAreaPalette.IndexOf("Town"));
        await Assert.That(MapViewModel.From(map, 1, [], null, MapAreaPalette.Dark)).IsNotEqualTo(view).Because("the palette is part of the view");
        await Assert.That(MapViewModel.From(map, 1, [], null, MapAreaPalette.Light)).IsEqualTo(view);
    }

    [Test]
    public async Task TwoNoticesThatSayTheSameThingAreStillTwo()
    {
        var at = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        var first = new SharpMUTerm.Client.Chrome.NoticeModel("saved", SharpMUTerm.Client.Chrome.NoticeSeverity.Info, null, null, at, 1);
        await Assert.That(first with { }).IsEqualTo(first);
        await Assert.That(first with { Sequence = 2 }).IsNotEqualTo(first);
        await Assert.That(SharpMUTerm.Client.Chrome.NoticeModel.Duration).IsEqualTo(TimeSpan.FromSeconds(6));
    }

    [Test]
    public async Task TheLayoutLibraryIsAFamilyThatListsNothingButHandlesItsIds()
    {
        var registry = new ContributionRegistry();
        BuiltInCommands.Register(registry, _ => true);

        await Assert.That(registry.Commands.Resolve(LayoutCommands.Apply("combat"))).IsNotNull();
        await Assert.That(registry.Commands.Resolve(LayoutCommands.SaveChanges)).IsNotNull();
        await Assert.That(registry.Commands.Resolve("layout:layouts")).IsNotNull();
    }
}
