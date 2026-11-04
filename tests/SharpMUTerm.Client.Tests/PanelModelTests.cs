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
