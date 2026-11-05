using System.Globalization;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

/// <summary>One world as the Worlds &amp; Characters page lists it, with its characters under it.</summary>
/// <param name="World">The world.</param>
/// <param name="Name">Its name.</param>
/// <param name="Address">Where it dials: <c>host:port</c>, or <see cref="WorldEditing.NoAddress"/> while it has no host.</param>
/// <param name="Tls">Whether it connects encrypted.</param>
/// <param name="Characters">Its characters, in configuration order.</param>
public sealed record WorldListEntry(
    WorldDefinition World, string Name, string Address, bool Tls, IReadOnlyList<CharacterListEntry> Characters)
{
    /// <summary>How many of its characters are connected now.</summary>
    public int Connected => Characters.Count(c => c.Connected);
}

/// <summary>One character as the Worlds &amp; Characters page lists it.</summary>
/// <param name="World">The world it plays on.</param>
/// <param name="Character">The character.</param>
/// <param name="Name">Its name.</param>
/// <param name="SessionKey">The <c>World.Character</c> key its session goes by.</param>
/// <param name="Connected">Whether a session for it is connected now.</param>
/// <param name="AtStart">Whether it connects when the client starts.</param>
/// <param name="Sets">How many of the player's trigger sets it uses.</param>
public sealed record CharacterListEntry(
    WorldDefinition World, CharacterDefinition Character, string Name, string SessionKey, bool Connected, bool AtStart, int Sets);

/// <summary>A trigger set as one character's list of sets shows it.</summary>
/// <param name="Set">The set.</param>
/// <param name="Name">The set's name (with its package's, for a package's set).</param>
/// <param name="On">Whether the character uses it.</param>
/// <param name="Rank">Its place among the sets the character uses, from 1 (the first wins a clash); 0 when off.</param>
/// <param name="Rules">How many triggers, aliases, keys and timers it holds.</param>
/// <param name="Package">The package it came from, or null for one of the player's own.</param>
/// <param name="Description">The set's own description, or null.</param>
public sealed record SetAssignment(TriggerSet Set, string Name, bool On, int Rank, int Rules, string? Package, string? Description);

/// <summary>
/// What the Worlds &amp; Characters page does to worlds and characters that is not a field: names for new ones, what a
/// deletion costs in words, and a character's sets in the order that decides a clash. Pure, so both hosts say the same.
/// </summary>
public static class WorldEditing
{
    /// <summary>What a world with no host yet lists as its address.</summary>
    public const string NoAddress = "no address yet";

    /// <summary>A new world's name before the player gives it one.</summary>
    public const string NewWorldName = "New World";

    /// <summary>A new character's name before the player gives it one.</summary>
    public const string NewCharacterName = "New Character";

    /// <summary>
    /// <paramref name="name"/> if nothing in <paramref name="taken"/> holds it, else the same name with the first free
    /// number after it (<c>New World 2</c>). Case-insensitive, as two names differing only in case read as one.
    /// </summary>
    public static string Available(IEnumerable<string> taken, string name)
    {
        ArgumentNullException.ThrowIfNull(taken);
        ArgumentNullException.ThrowIfNull(name);
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var candidate = name;
        for (var n = 2; used.Contains(candidate); n++)
        {
            candidate = $"{name} {n.ToString(CultureInfo.InvariantCulture)}";
        }

        return candidate;
    }

    /// <summary>Where <paramref name="world"/> dials, as its row says it.</summary>
    public static string Address(WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return string.IsNullOrWhiteSpace(world.Host)
            ? NoAddress
            : $"{world.Host}:{world.Port.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>What deleting <paramref name="world"/> costs: the world, and the characters that live on it.</summary>
    public static string Deletion(WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.Characters.Count == 0
            ? $"world {world.Name}"
            : $"world {world.Name} and its {world.Characters.Count.ToString(CultureInfo.InvariantCulture)}"
                + $" character{(world.Characters.Count == 1 ? string.Empty : "s")}";
    }

    /// <summary>What deleting <paramref name="character"/> costs.</summary>
    public static string Deletion(CharacterDefinition character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return $"character {character.Name}";
    }

    /// <summary>
    /// <paramref name="character"/>'s sets: those it uses first, in its own order (the first wins a clash), then the
    /// player's other sets in configuration order, then — read-only — each enabled package's sets for
    /// <paramref name="world"/>, which follow the character's own (<see cref="AppConfiguration.ResolveTriggerSets"/>).
    /// </summary>
    public static IReadOnlyList<SetAssignment> Assignments(AppConfiguration config, WorldDefinition world, CharacterDefinition character)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(character);
        var sets = config.TriggerSets.ToArray();
        var used = character.TriggerSets.ToArray();
        var rows = new List<SetAssignment>();
        var placed = new HashSet<TriggerSet>();
        foreach (var name in used)
        {
            if (sets.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) is { } set && placed.Add(set))
            {
                rows.Add(new SetAssignment(set, set.Name, true, rows.Count + 1, Rules(set), null, Blank(set.Description)));
            }
        }

        rows.AddRange(sets.Where(s => !placed.Contains(s))
            .Select(s => new SetAssignment(s, s.Name, false, 0, Rules(s), null, Blank(s.Description))));

        var rank = rows.Count(r => r.On);
        foreach (var package in config.Packages)
        {
            if (config.DisabledPackages.Contains(package.Id, StringComparer.Ordinal) || !package.Manifest.IsFor(world.Host, world.Port))
            {
                continue;
            }

            rows.AddRange(package.Sets.Select(s =>
                new SetAssignment(s, s.Name, true, ++rank, Rules(s), package.Manifest.Name, Blank(s.Description))));
        }

        return rows;
    }

    private static int Rules(TriggerSet set) => set.Triggers.Count + set.Aliases.Count + set.Macros.Count + set.Timers.Count;

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}

public sealed partial class SettingsForm
{
    /// <summary>
    /// The <c>World.Character</c> keys of the sessions connected now, read from the host's thread (the client keeps a
    /// snapshot it replaces whole); null where the client says nothing, and every character reads disconnected.
    /// </summary>
    public Func<IReadOnlyCollection<string>>? ConnectedKeys { get; init; }

    /// <summary>
    /// Brings a character forward by its session key — connecting it if it is not — as ⌃P's <c>Switch to</c> entry
    /// does; the client runs it on its own thread. Null where the client offers no way to connect from here.
    /// </summary>
    public Action<string>? Connect { get; init; }

    /// <summary>The command that opens Browse worlds, the one the worlds page may run.</summary>
    public const string BrowseWorlds = Commands.BuiltInCommands.BrowseWorldsCommand;

    /// <summary>The command that opens the import, the other one the worlds page may run.</summary>
    public const string ImportWorlds = Commands.BuiltInCommands.ImportCommand;

    /// <summary>
    /// Runs a registry command by id on the client's thread; null where the client takes none. The worlds page runs
    /// only <see cref="BrowseWorlds"/> and <see cref="ImportWorlds"/> through it (<see cref="Run"/>).
    /// </summary>
    public Action<string>? RunCommand { get; init; }

    /// <summary>
    /// Runs <see cref="BrowseWorlds"/> or <see cref="ImportWorlds"/>; anything else is refused, so the page never
    /// spells a command of its own. False when refused or when the client takes no command.
    /// </summary>
    public bool Run(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (RunCommand is not { } run || (id != BrowseWorlds && id != ImportWorlds))
        {
            return false;
        }

        run(id);
        return true;
    }

    /// <summary>
    /// The page a section is shown on: the characters' section (F9) is folded into the worlds page, which lists both,
    /// so its id answers <c>worlds</c>; every other section is its own page.
    /// </summary>
    public static string PageOf(string sectionId)
    {
        ArgumentNullException.ThrowIfNull(sectionId);
        return sectionId == BuiltInSettings.Character.Id ? BuiltInSettings.World.Id : sectionId;
    }

    /// <summary>What a world's server has said about itself, read on the client's thread when the form opened; null for none.</summary>
    public Func<WorldDefinition, ServerInfoModel?>? ServerInfo { get; init; }

    /// <summary>Whether the page offers a connect button.</summary>
    public bool CanConnect => Connect is not null;

    /// <summary>The world the scoped fields are edited for now, or null with none.</summary>
    public WorldDefinition? ChosenWorld => _world;

    /// <summary>The character the character fields are edited for now, or null with none.</summary>
    public CharacterDefinition? ChosenCharacter => _character;

    /// <summary>Every world with its characters, as the page lists them, read now.</summary>
    public IReadOnlyList<WorldListEntry> WorldList()
    {
        var connected = ConnectedKeys?.Invoke() ?? [];
        var sets = _config.TriggerSets.ToArray();
        return
        [
            .. _config.Worlds.ToArray().Select(w => new WorldListEntry(
                w,
                w.Name,
                WorldEditing.Address(w),
                w.UseTls,
                [.. w.Characters.ToArray().Select(c => new CharacterListEntry(
                    w,
                    c,
                    c.Name,
                    KeyOf(w, c),
                    connected.Contains(KeyOf(w, c)),
                    c.ConnectAtStartup,
                    c.TriggerSets.ToArray().Count(n => sets.Any(s => string.Equals(s.Name, n, StringComparison.OrdinalIgnoreCase)))))])),
        ];
    }

    /// <summary>The session key of <paramref name="character"/> on <paramref name="world"/>.</summary>
    public static string KeyOf(WorldDefinition world, CharacterDefinition character)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(character);
        return $"{world.Name}.{character.Name}";
    }

    /// <summary>Edits <paramref name="world"/>'s fields from now on and, when given, <paramref name="character"/>'s.</summary>
    public void Choose(WorldDefinition world, CharacterDefinition? character)
    {
        ArgumentNullException.ThrowIfNull(world);
        Choose(new SettingsSubject(world.Name, world, character));
        if (character is null && _character is { } kept && !world.Characters.Contains(kept))
        {
            _character = null;
        }
    }

    /// <summary>Adds a new world after the others, through the funnel, and chooses it.</summary>
    public WorldDefinition AddWorld()
    {
        var world = new WorldDefinition { Name = WorldEditing.Available(_config.Worlds.Select(w => w.Name), WorldEditing.NewWorldName) };
        _world = world;
        _character = null;
        Commit(() => _config.Worlds = [.. _config.Worlds, world], "adding a world");
        return world;
    }

    /// <summary>Deletes <paramref name="world"/> and its characters through the funnel; the choice moves to a neighbour.</summary>
    public bool DeleteWorld(WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var worlds = _config.Worlds.ToArray();
        var at = Array.IndexOf(worlds, world);
        if (at < 0)
        {
            return false;
        }

        if (ReferenceEquals(_world, world))
        {
            _world = worlds.Length > 1 ? worlds[at + 1 < worlds.Length ? at + 1 : at - 1] : null;
            _character = null;
        }

        Commit(() => _config.Worlds = [.. _config.Worlds.Where(w => !ReferenceEquals(w, world))], $"deleting {WorldEditing.Deletion(world)}");
        return true;
    }

    /// <summary>Moves <paramref name="world"/> up (−1) or down (+1) the list, through the funnel.</summary>
    public bool MoveWorld(WorldDefinition world, int delta)
    {
        ArgumentNullException.ThrowIfNull(world);
        var at = _config.Worlds.IndexOf(world);
        if (at < 0 || at + delta < 0 || at + delta >= _config.Worlds.Count)
        {
            return false;
        }

        Commit(() => _config.Worlds = Moved(_config.Worlds, world, delta), "moving a world");
        return true;
    }

    /// <summary>Adds a new character to <paramref name="world"/> through the funnel and chooses it.</summary>
    public CharacterDefinition AddCharacter(WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var character = new CharacterDefinition
        {
            Name = WorldEditing.Available(world.Characters.Select(c => c.Name), WorldEditing.NewCharacterName),
        };
        _world = world;
        _character = character;
        Commit(() => world.Characters = [.. world.Characters, character], "adding a character");
        return character;
    }

    /// <summary>Adds a copy of <paramref name="character"/> after it, through the funnel, and chooses it.</summary>
    public CharacterDefinition? DuplicateCharacter(CharacterDefinition character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (WorldOf(character) is not { } world)
        {
            return null;
        }

        var copy = character.Clone();
        copy.Name = WorldEditing.Available(world.Characters.Select(c => c.Name), character.Name + " copy");
        _world = world;
        _character = copy;
        Commit(
            () =>
            {
                var list = world.Characters.ToList();
                var at = list.IndexOf(character);
                list.Insert(at < 0 ? list.Count : at + 1, copy);
                world.Characters = list;
            },
            "copying a character");
        return copy;
    }

    /// <summary>Deletes <paramref name="character"/> through the funnel; the choice moves to its world.</summary>
    public bool DeleteCharacter(CharacterDefinition character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (WorldOf(character) is not { } world)
        {
            return false;
        }

        if (ReferenceEquals(_character, character))
        {
            _character = null;
            _world = world;
        }

        Commit(() => world.Characters = [.. world.Characters.Where(c => !ReferenceEquals(c, character))], $"deleting {WorldEditing.Deletion(character)}");
        return true;
    }

    /// <summary>Moves <paramref name="character"/> up (−1) or down (+1) within its world, through the funnel.</summary>
    public bool MoveCharacter(CharacterDefinition character, int delta)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (WorldOf(character) is not { } world)
        {
            return false;
        }

        var at = world.Characters.IndexOf(character);
        if (at + delta < 0 || at + delta >= world.Characters.Count)
        {
            return false;
        }

        Commit(() => world.Characters = Moved(world.Characters, character, delta), "moving a character");
        return true;
    }

    /// <summary><paramref name="character"/>'s sets, in the order that decides a clash (<see cref="WorldEditing.Assignments"/>).</summary>
    public IReadOnlyList<SetAssignment> Assignments(CharacterDefinition character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return WorldOf(character) is { } world ? WorldEditing.Assignments(_config, world, character) : [];
    }

    /// <summary>
    /// Has <paramref name="character"/> use <paramref name="set"/> or not, through the funnel. On appends it, so it
    /// loses a clash to every set already on; off removes the name. A package's set is not the character's to change.
    /// </summary>
    public bool Assign(CharacterDefinition character, TriggerSet set, bool on)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(set);
        if (!_config.TriggerSets.Contains(set))
        {
            return false;
        }

        var name = set.Name;
        Commit(
            () =>
            {
                var names = character.TriggerSets.Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (on)
                {
                    names.Add(name);
                }

                character.TriggerSets = names;
            },
            on ? $"using set {name}" : $"dropping set {name}");
        return true;
    }

    /// <summary>Moves a set <paramref name="character"/> uses up (−1, wins more clashes) or down (+1), through the funnel.</summary>
    public bool MoveAssignment(CharacterDefinition character, TriggerSet set, int delta)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(set);
        var names = character.TriggerSets.ToList();
        var at = names.FindIndex(n => string.Equals(n, set.Name, StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + delta < 0 || at + delta >= names.Count)
        {
            return false;
        }

        var name = names[at];
        Commit(() => character.TriggerSets = Moved(character.TriggerSets, name, delta), $"moving set {set.Name}");
        return true;
    }

    /// <summary>Connects <paramref name="character"/> (or brings it forward if it is connected); false where the client offers no way.</summary>
    public bool ConnectTo(WorldDefinition world, CharacterDefinition character)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(character);
        if (Connect is not { } connect)
        {
            return false;
        }

        connect(KeyOf(world, character));
        return true;
    }

    /// <summary>What <paramref name="world"/>'s server has said about itself, or null where the client keeps nothing.</summary>
    public ServerInfoModel? ServerInfoFor(WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return ServerInfo?.Invoke(world);
    }

    private WorldDefinition? WorldOf(CharacterDefinition character) =>
        _config.Worlds.ToArray().FirstOrDefault(w => w.Characters.Contains(character));

    private static List<T> Moved<T>(List<T> list, T item, int delta)
    {
        var copy = list.ToList();
        var at = copy.IndexOf(item);
        if (at < 0 || at + delta < 0 || at + delta >= copy.Count)
        {
            return copy;
        }

        copy.RemoveAt(at);
        copy.Insert(at + delta, item);
        return copy;
    }
}
