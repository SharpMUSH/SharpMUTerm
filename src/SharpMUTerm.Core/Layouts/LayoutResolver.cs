using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Layouts;

/// <summary>Which step of <see cref="LayoutResolver.Resolve"/> chose a layout.</summary>
public enum LayoutSource
{
    /// <summary>The character names it (<see cref="CharacterDefinition.Layout"/>).</summary>
    Character,

    /// <summary>The world names it (<see cref="WorldDefinition.Layout"/>).</summary>
    World,

    /// <summary>The player accepted it when the client suggested it (<see cref="WorldDefinition.AcceptedLayoutSuggestion"/>).</summary>
    AcceptedSuggestion,

    /// <summary>Nothing named one that exists, so it is <see cref="BuiltInLayouts.Roleplay"/>.</summary>
    Default,
}

/// <summary>A resolved layout and the step that chose it.</summary>
public sealed record ResolvedLayout(LayoutDefinition Layout, LayoutSource Source);

/// <summary>
/// Which layout a character's windows use: the character's own, else the world's, else the suggestion the player
/// accepted for the world, else <see cref="BuiltInLayouts.Roleplay"/>. An id that names no layout (deleted, or
/// from another machine's file) is passed over as though it were not set.
/// </summary>
public static class LayoutResolver
{
    /// <summary>Resolves the layout for <paramref name="character"/> on <paramref name="world"/>; either may be null.</summary>
    public static ResolvedLayout Resolve(LayoutLibrary library, WorldDefinition? world, CharacterDefinition? character)
    {
        ArgumentNullException.ThrowIfNull(library);

        if (library.Find(character?.Layout) is { } own)
        {
            return new ResolvedLayout(own, LayoutSource.Character);
        }

        if (library.Find(world?.Layout) is { } worlds)
        {
            return new ResolvedLayout(worlds, LayoutSource.World);
        }

        if (library.Find(world?.AcceptedLayoutSuggestion) is { } accepted)
        {
            return new ResolvedLayout(accepted, LayoutSource.AcceptedSuggestion);
        }

        return new ResolvedLayout(BuiltInLayouts.Roleplay, LayoutSource.Default);
    }
}
