using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;

namespace SharpMUTerm.Core.Tests.Layouts;

/// <summary>
/// Layouts in <c>config.json</c>: the player's layouts and the world/character references round-trip, the built-ins
/// are never written, and a document from before layouts loads with none — new optional fields whose defaults are
/// the old behaviour, so no migration step and no schema version change (<see cref="PaneTintConfigurationTests"/>'s
/// reasoning).
/// </summary>
public class LayoutConfigurationTests
{
    private const string DocumentWithoutLayouts = """
        {
          "version": 5,
          "worlds": [ { "name": "Aetherfall", "host": "aetherfall.mux", "port": 4201, "characters": [ { "name": "Corvid" } ] } ]
        }
        """;

    [Test]
    public async Task ADocumentFromBeforeLayoutsLoadsWithNone()
    {
        var config = ConfigurationStore.Deserialize(DocumentWithoutLayouts);

        await Assert.That(config.Layouts).IsEmpty();
        await Assert.That(config.Worlds[0].Layout).IsNull();
        await Assert.That(config.Worlds[0].AcceptedLayoutSuggestion).IsNull();
        await Assert.That(config.Worlds[0].Characters[0].Layout).IsNull();
    }

    [Test]
    public async Task LayoutsAndReferencesRoundTrip()
    {
        var config = new AppConfiguration();
        var library = new LayoutLibrary(config.Layouts, () => "my-layout");
        library.SaveAsNew(
            "Scene",
            [
                new PanelPlacement { PanelId = PanelIds.Output, Order = 0 },
                new PanelPlacement { PanelId = PanelIds.Chat, Region = LayoutRegion.Side, Order = 1, Size = 2.5, Collapsed = true, TabOf = PanelIds.WhoHere },
                new PanelPlacement { PanelId = "hologram", Region = LayoutRegion.TopStrip },
            ],
            new Dictionary<LayoutRegion, double> { [LayoutRegion.Side] = 0.35 });
        config.Worlds.Add(new WorldDefinition
        {
            Name = "W",
            Layout = BuiltInLayouts.CombatId,
            AcceptedLayoutSuggestion = BuiltInLayouts.RoleplayId,
            Characters = [new CharacterDefinition { Name = "C", Layout = "my-layout" }],
        });

        var json = ConfigurationStore.Serialize(config);
        var back = ConfigurationStore.Deserialize(json);

        // Region names, not numbers, so a hand-edited file reads.
        await Assert.That(json).Contains("\"region\": \"Side\"");
        await Assert.That(json).DoesNotContain("isBuiltIn");
        await Assert.That(json).DoesNotContain("knownPlacements");

        var layout = back.Layouts.Single();
        await Assert.That(layout.Id).IsEqualTo("my-layout");
        await Assert.That(layout.Name).IsEqualTo("Scene");
        await Assert.That(layout.IsBuiltIn).IsFalse();
        await Assert.That(layout.Placements.Count).IsEqualTo(3);
        await Assert.That(layout.Placements[1]).IsEqualTo(new PanelPlacement
        {
            PanelId = PanelIds.Chat, Region = LayoutRegion.Side, Order = 1, Size = 2.5, Collapsed = true, TabOf = PanelIds.WhoHere,
        });
        await Assert.That(layout.Placements[2].PanelId).IsEqualTo("hologram");
        await Assert.That(layout.RegionShares![LayoutRegion.Side]).IsEqualTo(0.35);

        await Assert.That(back.Worlds[0].Layout).IsEqualTo(BuiltInLayouts.CombatId);
        await Assert.That(back.Worlds[0].AcceptedLayoutSuggestion).IsEqualTo(BuiltInLayouts.RoleplayId);
        await Assert.That(back.Worlds[0].Characters[0].Layout).IsEqualTo("my-layout");

        // Resolution after the load agrees with before it.
        var resolved = LayoutResolver.Resolve(new LayoutLibrary(back.Layouts), back.Worlds[0], back.Worlds[0].Characters[0]);
        await Assert.That(resolved.Layout.Name).IsEqualTo("Scene");
    }

    [Test]
    public async Task AFileCannotMakeItsOwnLayoutBuiltIn()
    {
        var config = ConfigurationStore.Deserialize("""
            { "version": 5, "layouts": [ { "id": "x", "name": "X", "isBuiltIn": true, "placements": [] } ] }
            """);

        await Assert.That(config.Layouts.Single().IsBuiltIn).IsFalse();
    }

    [Test]
    public async Task CloningAWorldOrCharacterCarriesItsLayout()
    {
        var world = new WorldDefinition
        {
            Layout = "a",
            AcceptedLayoutSuggestion = "b",
            Characters = [new CharacterDefinition { Layout = "c" }],
        };

        var copy = world.Clone();

        await Assert.That(copy.Layout).IsEqualTo("a");
        await Assert.That(copy.AcceptedLayoutSuggestion).IsEqualTo("b");
        await Assert.That(copy.Characters[0].Layout).IsEqualTo("c");
    }
}
