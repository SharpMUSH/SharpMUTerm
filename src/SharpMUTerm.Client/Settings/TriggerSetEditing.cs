using System.Globalization;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

/// <summary>One character a trigger set can be used by, with whether it is.</summary>
/// <param name="World">The world the character plays on.</param>
/// <param name="Character">The character.</param>
/// <param name="Name">How the character is named on every surface (<c>Corvid · Aetherfall</c>).</param>
/// <param name="Uses">Whether the character uses the set.</param>
public sealed record SetUser(WorldDefinition World, CharacterDefinition Character, string Name, bool Uses);

/// <summary>
/// What a trigger set is, and the things done to one as a whole rather than to a rule inside it: making one, naming it,
/// deleting it, and choosing which characters use it. Both hosts' list screens (F2, F3, F4, F6) and the cell sets screen
/// read these, so a set is explained, counted and changed one way everywhere.
/// <para>
/// A character selects its automation by a set's <em>name</em>, so a rename here rewrites every character's reference
/// in place (their order decides a clash) and a deletion takes them out. Lists are replaced, never edited in place: the
/// window reads the configuration on its own thread.
/// </para>
/// </summary>
public static class TriggerSetEditing
{
    /// <summary>A new set's name before the player gives it one.</summary>
    public const string NewSetName = "New Set";

    /// <summary>What a trigger set is, in one sentence, where sets are made and chosen.</summary>
    public const string Explainer =
        "A trigger set groups triggers, aliases, keys and timers. A character runs only the sets it uses; one set can serve several characters.";

    /// <summary>What a set no character uses says, wherever its usage is shown.</summary>
    public const string Unused = "used by no character";

    /// <summary>Why a name is refused when it is blank.</summary>
    public const string BlankName = "a set needs a name";

    /// <summary>Every character in the configuration, in world then character order, with whether it uses <paramref name="set"/>.</summary>
    public static IReadOnlyList<SetUser> Users(AppConfiguration config, TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(set);
        return Users(config.Worlds, set.Name);
    }

    /// <summary>Every character of <paramref name="worlds"/>, with whether it uses the set called <paramref name="name"/>.</summary>
    public static IReadOnlyList<SetUser> Users(IReadOnlyList<WorldDefinition> worlds, string name)
    {
        ArgumentNullException.ThrowIfNull(worlds);
        ArgumentNullException.ThrowIfNull(name);
        var users = new List<SetUser>();
        foreach (var world in worlds.ToArray())
        {
            foreach (var character in world.Characters.ToArray())
            {
                users.Add(new SetUser(
                    world,
                    character,
                    CharacterNames.Display(world.Name, character.Name),
                    Uses(character, name)));
            }
        }

        return users;
    }

    /// <summary>Whether <paramref name="character"/> uses the set called <paramref name="name"/> (case-insensitive, as the resolver matches).</summary>
    public static bool Uses(CharacterDefinition character, string name)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.TriggerSets.ToArray().Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Who uses the set called <paramref name="name"/>, in words short enough for a set's heading: <c>used by Corvid</c>,
    /// <c>used by Corvid, Ann</c>, <c>used by Corvid and 2 more</c>, or <see cref="Unused"/>. Characters are named alone
    /// here; the heading has no room for the world, and the sets screen lists them in full.
    /// </summary>
    public static string Usage(IReadOnlyList<WorldDefinition> worlds, string name)
    {
        var names = Users(worlds, name).Where(u => u.Uses).Select(u => u.Character.Name).ToList();
        return names.Count switch
        {
            0 => Unused,
            1 => $"used by {names[0]}",
            2 => $"used by {names[0]}, {names[1]}",
            _ => string.Create(CultureInfo.InvariantCulture, $"used by {names[0]} and {names.Count - 1} more"),
        };
    }

    /// <summary>Whether any character uses the set called <paramref name="name"/>.</summary>
    public static bool IsUsed(IReadOnlyList<WorldDefinition> worlds, string name) =>
        Users(worlds, name).Any(u => u.Uses);

    /// <summary>What a set holds, every kind counted: <c>3 triggers · 1 alias · 2 keys</c>, or <c>empty</c>.</summary>
    public static string Holds(TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var parts = new List<string>();
        Add(parts, set.Triggers.Count, "trigger", "triggers");
        Add(parts, set.Aliases.Count, "alias", "aliases");
        Add(parts, set.Macros.Count, "key", "keys");
        Add(parts, set.Timers.Count, "timer", "timers");
        return parts.Count == 0 ? "empty" : string.Join(" · ", parts);

        static void Add(List<string> parts, int count, string noun, string plural)
        {
            if (count > 0)
            {
                parts.Add(SetHeading.Count(count, noun, plural));
            }
        }
    }

    /// <summary>
    /// Makes a new, empty set named apart from the others, adds it at the end of the player's sets and, when
    /// <paramref name="usedBy"/> is given, has that character use it — so a set made where a rule is being written is
    /// already doing something. Returns the set.
    /// </summary>
    public static TriggerSet Create(AppConfiguration config, CharacterDefinition? usedBy = null, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var set = new TriggerSet
        {
            Name = WorldEditing.Available(config.TriggerSets.Select(s => s.Name), string.IsNullOrWhiteSpace(name) ? NewSetName : name.Trim()),
        };
        config.TriggerSets = [.. config.TriggerSets, set];
        if (usedBy is not null)
        {
            Use(usedBy, set.Name, on: true);
        }

        return set;
    }

    /// <summary>Why <paramref name="name"/> cannot be <paramref name="set"/>'s name, or null when it can.</summary>
    public static string? NameProblem(AppConfiguration config, TriggerSet set, string? name)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(set);
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return BlankName;
        }

        return config.TriggerSets.Any(s => !ReferenceEquals(s, set) && string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase))
            ? $"another set is called {trimmed}"
            : null;
    }

    /// <summary>
    /// Renames <paramref name="set"/> and every character's reference to it, each kept where it was in that
    /// character's list. False (and nothing changed) when <see cref="NameProblem"/> refuses the name.
    /// </summary>
    public static bool Rename(AppConfiguration config, TriggerSet set, string name)
    {
        if (NameProblem(config, set, name) is not null)
        {
            return false;
        }

        var renamed = name.Trim();
        foreach (var world in config.Worlds)
        {
            foreach (var character in world.Characters)
            {
                character.TriggerSets = character.TriggerSets
                    .Select(n => string.Equals(n, set.Name, StringComparison.OrdinalIgnoreCase) ? renamed : n)
                    .ToList();
            }
        }

        set.Name = renamed;
        return true;
    }

    /// <summary>Deletes <paramref name="set"/> and every character's use of it.</summary>
    public static void Delete(AppConfiguration config, TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(set);
        var name = set.Name;
        config.TriggerSets = config.TriggerSets.Where(s => !ReferenceEquals(s, set)).ToList();
        foreach (var world in config.Worlds)
        {
            foreach (var character in world.Characters)
            {
                if (Uses(character, name))
                {
                    Use(character, name, on: false);
                }
            }
        }
    }

    /// <summary>
    /// What deleting <paramref name="set"/> costs, in words: the set, its rules, and how many characters stop running it.
    /// </summary>
    public static string Deletion(IReadOnlyList<WorldDefinition> worlds, TriggerSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var rules = set.Triggers.Count + set.Aliases.Count + set.Macros.Count + set.Timers.Count;
        var users = Users(worlds, set.Name).Count(u => u.Uses);
        var parts = new List<string>();
        if (rules > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{rules} rule{(rules == 1 ? string.Empty : "s")}"));
        }

        if (users > 0)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{users} character{(users == 1 ? string.Empty : "s")} using it"));
        }

        return parts.Count == 0
            ? $"trigger set {set.Name}"
            : $"trigger set {set.Name} and its {string.Join(", ", parts)}";
    }

    /// <summary>
    /// Has <paramref name="character"/> use the set called <paramref name="name"/> or not. On appends it, so it loses a
    /// clash to every set already on; off removes the name. The list is replaced.
    /// </summary>
    public static void Use(CharacterDefinition character, string name, bool on)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(name);
        var names = character.TriggerSets.Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (on)
        {
            names.Add(name);
        }

        character.TriggerSets = names;
    }
}
