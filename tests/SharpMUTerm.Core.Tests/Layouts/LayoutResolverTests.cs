using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;

namespace SharpMUTerm.Core.Tests.Layouts;

/// <summary>
/// <see cref="LayoutResolver"/>: character, then world, then the accepted suggestion, then Roleplay — and an id that
/// names nothing is passed over rather than failing.
/// </summary>
public class LayoutResolverTests
{
    private static (LayoutLibrary Library, LayoutDefinition Saved) WithOneSaved()
    {
        var library = new LayoutLibrary([], () => "saved");
        return (library, library.SaveAsNew("Scene", [new PanelPlacement { PanelId = PanelIds.Output }]));
    }

    [Test]
    public async Task NothingSetIsRoleplay()
    {
        var (library, _) = WithOneSaved();

        var resolved = LayoutResolver.Resolve(library, new WorldDefinition(), new CharacterDefinition());

        await Assert.That(resolved.Layout).IsSameReferenceAs(BuiltInLayouts.Roleplay);
        await Assert.That(resolved.Source).IsEqualTo(LayoutSource.Default);
        await Assert.That(LayoutResolver.Resolve(library, null, null).Source).IsEqualTo(LayoutSource.Default);
    }

    [Test]
    public async Task TheCharacterWinsOverTheWorldWhichWinsOverTheSuggestion()
    {
        var (library, saved) = WithOneSaved();
        var world = new WorldDefinition { Layout = BuiltInLayouts.CombatId, AcceptedLayoutSuggestion = BuiltInLayouts.RoleplayId };
        var character = new CharacterDefinition { Layout = saved.Id };

        var both = LayoutResolver.Resolve(library, world, character);
        await Assert.That(both.Layout.Id).IsEqualTo(saved.Id);
        await Assert.That(both.Source).IsEqualTo(LayoutSource.Character);

        character.Layout = null;
        var worldOnly = LayoutResolver.Resolve(library, world, character);
        await Assert.That(worldOnly.Layout.Id).IsEqualTo(BuiltInLayouts.CombatId);
        await Assert.That(worldOnly.Source).IsEqualTo(LayoutSource.World);

        world.Layout = null;
        world.AcceptedLayoutSuggestion = BuiltInLayouts.CombatId;
        var suggestion = LayoutResolver.Resolve(library, world, character);
        await Assert.That(suggestion.Layout.Id).IsEqualTo(BuiltInLayouts.CombatId);
        await Assert.That(suggestion.Source).IsEqualTo(LayoutSource.AcceptedSuggestion);
    }

    [Test]
    public async Task ADeletedLayoutFallsThroughToTheNextStep()
    {
        var (library, saved) = WithOneSaved();
        var world = new WorldDefinition { Layout = BuiltInLayouts.CombatId };
        var character = new CharacterDefinition { Layout = saved.Id };

        library.Delete(saved.Id);
        var resolved = LayoutResolver.Resolve(library, world, character);

        await Assert.That(resolved.Layout.Id).IsEqualTo(BuiltInLayouts.CombatId);
        await Assert.That(resolved.Source).IsEqualTo(LayoutSource.World);

        // Every step dangling, all the way down.
        world.Layout = "gone";
        world.AcceptedLayoutSuggestion = "also-gone";
        await Assert.That(LayoutResolver.Resolve(library, world, character).Source).IsEqualTo(LayoutSource.Default);
    }
}
