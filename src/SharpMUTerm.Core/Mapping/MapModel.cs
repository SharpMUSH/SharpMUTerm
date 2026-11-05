namespace SharpMUTerm.Core.Mapping;

/// <summary>
/// One room of a map — Mudlet's model, which players already understand: an id the game gives it, a place
/// on a grid, exits by direction and by command, and the weights and locks the path finder reads.
/// </summary>
public sealed class MapRoom
{
    /// <summary>The game's own number for the room (GMCP <c>num</c>/<c>id</c>), or one the client assigned.</summary>
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The <see cref="MapArea.Id"/> the room belongs to, or null.</summary>
    public int? AreaId { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    /// <summary>The game's terrain or environment name, when it sends one.</summary>
    public string? Environment { get; set; }

    /// <summary>Exits by direction (a <see cref="MapDirections.All"/> name) → the room they lead to.</summary>
    public Dictionary<string, long> Exits { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Exits by the command that takes them (<c>enter portal</c>) → the room they lead to.</summary>
    public Dictionary<string, long> SpecialExits { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Exits the room is known to have whose destination nobody has walked yet — Mudlet's stubs. The text
    /// source reports only these; a walk turns one into an <see cref="Exits"/> entry.
    /// </summary>
    public HashSet<string> Stubs { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The cost of entering this room, at least 1. A heavier room is walked around when there is a way.</summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    /// The cost of a particular exit, by direction or command, overriding the destination room's
    /// <see cref="Weight"/> when set — Mudlet's rule.
    /// </summary>
    public Dictionary<string, int> ExitWeights { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Exits (directions or commands) the path finder never takes.</summary>
    public HashSet<string> LockedExits { get; set; } = new(StringComparer.Ordinal);

    /// <summary>A locked room is never walked through or to.</summary>
    public bool Locked { get; set; }

    /// <summary>One or two characters drawn in the room's cell, or null.</summary>
    public string? Symbol { get; set; }

    /// <summary>The room's colour as <c>#rrggbb</c>, or null for the default.</summary>
    public string? Color { get; set; }

    /// <summary>The player's own note on the room, or null: kept with the map, never sent anywhere.</summary>
    public string? Note { get; set; }

    /// <summary>Anything a script wants to keep on the room.</summary>
    public Dictionary<string, string> UserData { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The fields the player changed by hand (<see cref="MapFields"/>): a room report from the server leaves these alone,
    /// so a GMCP world does not undo a rename, an area, an exit or a move on the next visit. Kept with the map.
    /// </summary>
    public HashSet<string> HandEdited { get; set; } = new(StringComparer.Ordinal);

    /// <summary>A deep copy, for an edit's undo.</summary>
    public MapRoom Clone() => new()
    {
        Id = Id,
        Name = Name,
        AreaId = AreaId,
        X = X,
        Y = Y,
        Z = Z,
        Environment = Environment,
        Exits = new(Exits, StringComparer.Ordinal),
        SpecialExits = new(SpecialExits, StringComparer.Ordinal),
        Stubs = new(Stubs, StringComparer.Ordinal),
        Weight = Weight,
        ExitWeights = new(ExitWeights, StringComparer.Ordinal),
        LockedExits = new(LockedExits, StringComparer.Ordinal),
        Locked = Locked,
        Symbol = Symbol,
        Color = Color,
        Note = Note,
        UserData = new(UserData, StringComparer.Ordinal),
        HandEdited = new(HandEdited, StringComparer.Ordinal),
    };

    /// <summary>Every exit, direction and special alike, as the command that takes it and where it leads.</summary>
    public IEnumerable<(string Command, string Key, long To)> AllExits()
    {
        foreach (var (direction, to) in Exits)
        {
            yield return (MapDirections.Command(direction), direction, to);
        }

        foreach (var (command, to) in SpecialExits)
        {
            yield return (command, command, to);
        }
    }
}

/// <summary>The fields of a room a hand edit protects from the server's next report (<see cref="MapRoom.HandEdited"/>).</summary>
public static class MapFields
{
    public const string Name = "name", Area = "area", Exits = "exits", Position = "position";

    /// <summary>Every field, in the order a view lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [Name, Area, Exits, Position];
}

/// <summary>A named area rooms belong to.</summary>
public sealed class MapArea
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// A world's map: its rooms and areas. Not thread-safe; the client touches it from the UI thread only.
/// </summary>
public sealed class WorldMap
{
    public Dictionary<long, MapRoom> Rooms { get; } = new();

    public Dictionary<int, MapArea> Areas { get; } = new();

    /// <summary>The room, or null when the map has none by that id.</summary>
    public MapRoom? Room(long id) => Rooms.GetValueOrDefault(id);

    /// <summary>Adds the room, or replaces the one with its id.</summary>
    public MapRoom Add(MapRoom room)
    {
        ArgumentNullException.ThrowIfNull(room);
        Rooms[room.Id] = room;
        return room;
    }

    /// <summary>The area with this name, created with the next free id when there is none.</summary>
    public MapArea AreaNamed(string name)
    {
        foreach (var area in Areas.Values)
        {
            if (string.Equals(area.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return area;
            }
        }

        var created = new MapArea { Id = Areas.Count == 0 ? 1 : Areas.Keys.Max() + 1, Name = name };
        Areas[created.Id] = created;
        return created;
    }

    // ── Edits by hand: each answers whether it changed anything, and changes nothing it refuses ──────────

    /// <summary>The longest room name, area name, note or exit command an edit keeps.</summary>
    public const int MaxEditLength = 200;

    /// <summary>The heaviest weight an edit sets.</summary>
    public const int MaxWeight = 1000;

    /// <summary>Renames a room. A blank name is refused.</summary>
    public bool Rename(long id, string name)
    {
        var text = Clean(name);
        if (Room(id) is not { } room || text.Length == 0 || room.Name == text)
        {
            return false;
        }

        room.Name = text;
        room.HandEdited.Add(MapFields.Name);
        return true;
    }

    /// <summary>Puts a room in the area called <paramref name="area"/>, creating it; blank takes it out of every area.</summary>
    public bool SetArea(long id, string? area)
    {
        if (Room(id) is not { } room)
        {
            return false;
        }

        var text = Clean(area);
        int? next = text.Length == 0 ? null : AreaNamed(text).Id;
        if (room.AreaId == next)
        {
            return false;
        }

        room.AreaId = next;
        room.HandEdited.Add(MapFields.Area);
        return true;
    }

    /// <summary>Locks or unlocks a room for paths.</summary>
    public bool SetLocked(long id, bool locked)
    {
        if (Room(id) is not { } room || room.Locked == locked)
        {
            return false;
        }

        room.Locked = locked;
        return true;
    }

    /// <summary>Sets the cost of entering a room, 1 to <see cref="MaxWeight"/>.</summary>
    public bool SetWeight(long id, int weight)
    {
        if (Room(id) is not { } room || weight < 1 || weight > MaxWeight || room.Weight == weight)
        {
            return false;
        }

        room.Weight = weight;
        return true;
    }

    /// <summary>
    /// Points an exit of room <paramref name="id"/> at room <paramref name="to"/>: a direction (any spelling
    /// <see cref="MapDirections.Normalise"/> reads) goes in <see cref="MapRoom.Exits"/>, anything else is the command
    /// of a special exit. A stub of the same name is linked. Both rooms must exist.
    /// </summary>
    public bool SetExit(long id, string exit, long to)
    {
        var key = ExitKey(exit);
        if (Room(id) is not { } room || Room(to) is null || key.Length == 0)
        {
            return false;
        }

        var exits = MapDirections.All.Contains(key) ? room.Exits : room.SpecialExits;
        var changed = !exits.TryGetValue(key, out var was) || was != to;
        exits[key] = to;
        changed |= room.Stubs.Remove(key);
        if (changed)
        {
            room.HandEdited.Add(MapFields.Exits);
        }

        return changed;
    }

    /// <summary>Removes an exit of a room, linked or a stub, by direction or command.</summary>
    public bool RemoveExit(long id, string exit)
    {
        var key = ExitKey(exit);
        if (Room(id) is not { } room || key.Length == 0)
        {
            return false;
        }

        var removed = room.Exits.Remove(key) | room.SpecialExits.Remove(key) | room.Stubs.Remove(key);
        room.ExitWeights.Remove(key);
        room.LockedExits.Remove(key);
        if (removed)
        {
            room.HandEdited.Add(MapFields.Exits);
        }

        return removed;
    }

    /// <summary>Moves a room on the grid by a step; up and down are <paramref name="dz"/>.</summary>
    public bool Move(long id, int dx, int dy, int dz)
    {
        if (Room(id) is not { } room || (dx, dy, dz) == (0, 0, 0))
        {
            return false;
        }

        (room.X, room.Y, room.Z) = (room.X + dx, room.Y + dy, room.Z + dz);
        room.HandEdited.Add(MapFields.Position);
        return true;
    }

    /// <summary>Forgets which fields of a room were edited by hand, so the server's reports set them again.</summary>
    public bool ClearHandEdits(long id)
    {
        if (Room(id) is not { } room || room.HandEdited.Count == 0)
        {
            return false;
        }

        room.HandEdited.Clear();
        return true;
    }

    /// <summary>Removes a room and every exit that led to it (those become stubs again, as before anyone walked them).</summary>
    public bool Remove(long id)
    {
        if (!Rooms.Remove(id))
        {
            return false;
        }

        foreach (var other in Rooms.Values)
        {
            foreach (var key in other.Exits.Where(e => e.Value == id).Select(e => e.Key).ToList())
            {
                other.Exits.Remove(key);
                other.Stubs.Add(key);
            }

            foreach (var key in other.SpecialExits.Where(e => e.Value == id).Select(e => e.Key).ToList())
            {
                other.SpecialExits.Remove(key);
                other.Stubs.Add(key);
            }
        }

        return true;
    }

    /// <summary>Sets a room's note; blank clears it.</summary>
    public bool SetNote(long id, string? note)
    {
        var text = Clean(note);
        string? next = text.Length == 0 ? null : text;
        if (Room(id) is not { } room || room.Note == next)
        {
            return false;
        }

        room.Note = next;
        return true;
    }

    /// <summary>Sets the one character drawn in a room's cell; blank clears it. Only a printable, single-cell character.</summary>
    public bool SetSymbol(long id, string? symbol)
    {
        var text = Clean(symbol);
        if (Room(id) is not { } room || text.Length > 1 || (text.Length == 1 && !IsSymbol(text[0])))
        {
            return false;
        }

        string? next = text.Length == 0 ? null : text;
        if (room.Symbol == next)
        {
            return false;
        }

        room.Symbol = next;
        return true;
    }

    /// <summary>Whether a character may be a room's symbol: printable, one cell, not markup's brackets.</summary>
    public static bool IsSymbol(char c) =>
        !char.IsControl(c) && !char.IsSurrogate(c) && !char.IsWhiteSpace(c) && c is not ('[' or ']') && c < 0x2E80;

    /// <summary>An exit's key: a direction's canonical name, else the command as typed, trimmed and bounded.</summary>
    public static string ExitKey(string? exit) => MapDirections.Normalise(Clean(exit)) ?? Clean(exit);

    /// <summary>Text from a player: control characters out, trimmed, bounded to <see cref="MaxEditLength"/>.</summary>
    private static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var kept = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return kept.Length <= MaxEditLength ? kept : kept[..MaxEditLength];
    }

    /// <summary>Whether a room other than <paramref name="except"/> already sits at this point of this area.</summary>
    public bool IsOccupied(int? areaId, int x, int y, int z, long except) =>
        Rooms.Values.Any(r => r.Id != except && r.AreaId == areaId && r.X == x && r.Y == y && r.Z == z);
}
