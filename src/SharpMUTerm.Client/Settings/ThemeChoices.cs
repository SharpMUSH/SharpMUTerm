using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Theming;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// The themes a player can pick and how picking one is written: the built-ins (<see cref="ThemeLibrary"/>), the
/// customised theme in use if it is one of the player's own, and the player's other themes
/// (<see cref="AppConfiguration.CustomThemes"/>). One place, so the cell picker, the native cards and the client's
/// own theme resolution agree on what a name means.
/// </summary>
public static class ThemeChoices
{
    /// <summary>Whether <paramref name="name"/> names a built-in theme.</summary>
    public static bool IsBuiltIn(string? name) =>
        name is not null && ThemeLibrary.Names.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The theme in force: the inline theme when it is one of the player's own (its name is not a built-in's, or
    /// disagrees with <see cref="AppConfiguration.ThemeName"/>), otherwise the built-in by name.
    /// </summary>
    public static Theme Resolve(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Theme is { } inline
            && (!string.Equals(inline.Name, config.ThemeName, StringComparison.OrdinalIgnoreCase) || !IsBuiltIn(config.ThemeName)))
        {
            return inline;
        }

        return ThemeLibrary.Get(config.ThemeName);
    }

    /// <summary>The name of the theme in force.</summary>
    public static string Current(AppConfiguration config) => Resolve(config).Name;

    /// <summary>Every theme the picker lists, by name: the built-ins in their order, then the player's own.</summary>
    public static IReadOnlyList<string> Names(AppConfiguration config) => [.. All(config).Select(t => t.Name)];

    /// <summary>Every theme the picker lists, in order, each a copy safe to read on another thread.</summary>
    public static IReadOnlyList<Theme> All(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var themes = ThemeLibrary.Names.Select(ThemeLibrary.Get).ToList();
        var current = Resolve(config);
        if (!IsBuiltIn(current.Name))
        {
            themes.Add(current.Clone());
        }

        foreach (var own in config.CustomThemes)
        {
            if (!themes.Any(t => string.Equals(t.Name, own.Name, StringComparison.OrdinalIgnoreCase)))
            {
                themes.Add(own.Clone());
            }
        }

        return themes;
    }

    /// <summary>Whether <paramref name="name"/> is one of the player's own themes rather than a built-in.</summary>
    public static bool IsCustom(string name) => !IsBuiltIn(name);

    /// <summary>The theme called <paramref name="name"/>, or null when the picker lists none by that name.</summary>
    public static Theme? Find(AppConfiguration config, string name) =>
        All(config).FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Makes <paramref name="name"/> the theme in force. A customised theme being replaced is kept in
    /// <see cref="AppConfiguration.CustomThemes"/>, so nothing the player made is lost by trying another theme; a
    /// name the picker does not list changes nothing.
    /// </summary>
    public static void Apply(AppConfiguration config, string name)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(name);
        var chosen = Find(config, name);
        if (chosen is null)
        {
            return;
        }

        var current = Resolve(config);
        if (string.Equals(current.Name, chosen.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!IsBuiltIn(current.Name)
            && !config.CustomThemes.Any(t => string.Equals(t.Name, current.Name, StringComparison.OrdinalIgnoreCase)))
        {
            config.CustomThemes.Add(current.Clone());
        }

        config.CustomThemes.RemoveAll(t => string.Equals(t.Name, chosen.Name, StringComparison.OrdinalIgnoreCase));
        config.ThemeName = chosen.Name;
        config.Theme = chosen;
    }
}
