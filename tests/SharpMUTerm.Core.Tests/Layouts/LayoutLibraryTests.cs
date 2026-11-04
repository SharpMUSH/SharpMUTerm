using SharpMUTerm.Core.Layouts;

namespace SharpMUTerm.Core.Tests.Layouts;

/// <summary>
/// <see cref="LayoutLibrary"/>: the built-ins are listed and never changed, the player's layouts are saved, updated,
/// renamed and deleted in the list handed in, and no two layouts share a name.
/// </summary>
public class LayoutLibraryTests
{
    private static readonly PanelPlacement[] OutputOnly = [new() { PanelId = PanelIds.Output }];

    private static LayoutLibrary Library(List<LayoutDefinition>? user = null)
    {
        var n = 0;
        return new LayoutLibrary(user ?? [], () => $"id{++n}");
    }

    [Test]
    public async Task TheBuiltInsAreListedFirstAndAreReadOnly()
    {
        var user = new List<LayoutDefinition>();
        var library = Library(user);
        library.SaveAsNew("Mine", OutputOnly);

        await Assert.That(library.All.Select(l => l.Id)).IsEquivalentTo(new[] { "roleplay", "combat", "id1" });
        await Assert.That(library.All[0].IsBuiltIn).IsTrue();
        await Assert.That(library.All[2].IsBuiltIn).IsFalse();

        // Never in the player's list, so never in config.json.
        await Assert.That(user.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SavingUnderATakenNameNumbersIt()
    {
        var library = Library();

        var first = library.SaveAsNew("Scene", OutputOnly);
        var second = library.SaveAsNew("scene", OutputOnly);
        var builtInName = library.SaveAsNew("Roleplay", OutputOnly);
        var blank = library.SaveAsNew("  ", OutputOnly);

        await Assert.That(first.Name).IsEqualTo("Scene");
        await Assert.That(second.Name).IsEqualTo("scene 2");
        await Assert.That(builtInName.Name).IsEqualTo("Roleplay 2");
        await Assert.That(blank.Name).IsEqualTo("Layout");
    }

    [Test]
    public async Task UpdatingASavedLayoutReplacesItInPlaceAndKeepsItsId()
    {
        var user = new List<LayoutDefinition>();
        var library = Library(user);
        var saved = library.SaveAsNew("Scene", OutputOnly);
        PanelPlacement[] changed = [new() { PanelId = PanelIds.Output }, new() { PanelId = PanelIds.Map, Region = LayoutRegion.Side }];

        var result = library.Update(saved.Id, changed);

        await Assert.That(result.CreatedNew).IsFalse();
        await Assert.That(result.Layout.Id).IsEqualTo(saved.Id);
        await Assert.That(user.Single().Placements.Count).IsEqualTo(2);
        await Assert.That(user.Single().Name).IsEqualTo("Scene");
    }

    [Test]
    public async Task UpdatingABuiltInSavesACopyAndLeavesTheBuiltInAlone()
    {
        var user = new List<LayoutDefinition>();
        var library = Library(user);
        var before = BuiltInLayouts.Roleplay.Placements.Count;

        var result = library.Update(BuiltInLayouts.RoleplayId, OutputOnly);

        await Assert.That(result.CreatedNew).IsTrue();
        await Assert.That(result.Layout.Name).IsEqualTo("Roleplay 2");
        await Assert.That(result.Layout.IsBuiltIn).IsFalse();
        await Assert.That(user.Count).IsEqualTo(1);
        await Assert.That(BuiltInLayouts.Roleplay.Placements.Count).IsEqualTo(before);
        await Assert.That(library.Find(BuiltInLayouts.RoleplayId)).IsSameReferenceAs(BuiltInLayouts.Roleplay);
    }

    [Test]
    public async Task UpdatingAnUnknownIdThrows()
    {
        await Assert.That(() => Library().Update("nope", OutputOnly)).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task RenameRefusesBuiltInsBlanksAndTakenNames()
    {
        var library = Library();
        var a = library.SaveAsNew("A", OutputOnly);
        library.SaveAsNew("B", OutputOnly);

        await Assert.That(library.Rename(BuiltInLayouts.CombatId, "Fight")).IsFalse();
        await Assert.That(library.Rename(a.Id, " ")).IsFalse();
        await Assert.That(library.Rename(a.Id, "b")).IsFalse();
        await Assert.That(library.Rename(a.Id, "combat")).IsFalse();
        await Assert.That(library.Rename("missing", "C")).IsFalse();

        await Assert.That(library.Rename(a.Id, "a")).IsTrue();
        await Assert.That(library.Find(a.Id)!.Name).IsEqualTo("a");
        await Assert.That(library.Rename(a.Id, "  Duel ")).IsTrue();
        await Assert.That(library.Find(a.Id)!.Name).IsEqualTo("Duel");
    }

    [Test]
    public async Task DeleteRemovesASavedLayoutAndRefusesABuiltIn()
    {
        var user = new List<LayoutDefinition>();
        var library = Library(user);
        var saved = library.SaveAsNew("Scene", OutputOnly);

        await Assert.That(library.Delete(BuiltInLayouts.RoleplayId)).IsFalse();
        await Assert.That(library.Delete("missing")).IsFalse();
        await Assert.That(library.Delete(saved.Id)).IsTrue();
        await Assert.That(user).IsEmpty();
        await Assert.That(library.Find(saved.Id)).IsNull();
    }

    [Test]
    public async Task ASavedLayoutCannotShadowABuiltInById()
    {
        var user = new List<LayoutDefinition> { new() { Id = BuiltInLayouts.RoleplayId, Name = "Impostor" } };
        var library = Library(user);

        await Assert.That(library.Find(BuiltInLayouts.RoleplayId)!.Name).IsEqualTo("Roleplay");
        await Assert.That(library.Delete(BuiltInLayouts.RoleplayId)).IsFalse();
    }

    [Test]
    public async Task AnUnknownPanelIsKeptInTheLayoutAndSkippedByReaders()
    {
        var layout = new LayoutDefinition
        {
            Id = "x",
            Name = "X",
            Placements = [new() { PanelId = PanelIds.Output }, new() { PanelId = "hologram", Region = LayoutRegion.Side }],
        };

        await Assert.That(layout.Placements.Count).IsEqualTo(2);
        await Assert.That(layout.KnownPlacements.Select(p => p.PanelId)).IsEquivalentTo(new[] { PanelIds.Output });
        await Assert.That(layout.PlacementOf("hologram")).IsNull();
        await Assert.That(LayoutFold.Fold(layout).Root!.Panes().Select(p => p.Id)).IsEquivalentTo(new[] { "panel:output" });
    }

    [Test]
    public async Task EveryPanelTheBuiltInsPlaceIsKnown()
    {
        foreach (var layout in BuiltInLayouts.All)
        {
            await Assert.That(layout.Placements.All(p => PanelIds.IsKnown(p.PanelId))).IsTrue();
            await Assert.That(layout.IsBuiltIn).IsTrue();
        }
    }
}
