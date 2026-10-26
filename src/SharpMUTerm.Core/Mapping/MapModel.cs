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

    /// <summary>Anything a script wants to keep on the room.</summary>
    public Dictionary<string, string> UserData { get; set; } = new(StringComparer.Ordinal);

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

    /// <summary>Whether a room other than <paramref name="except"/> already sits at this point of this area.</summary>
    public bool IsOccupied(int? areaId, int x, int y, int z, long except) =>
        Rooms.Values.Any(r => r.Id != except && r.AreaId == areaId && r.X == x && r.Y == y && r.Z == z);
}
