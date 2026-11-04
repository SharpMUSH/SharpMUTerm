using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Client.Panels;

/// <summary>One room as a map view draws it, at its map coordinates (y grows southwards, as the cell map draws it).</summary>
/// <param name="Id">The room's id, which a click hands back to walk there.</param>
/// <param name="Name">The room's name, bounded; the game's text.</param>
/// <param name="X">Its column on the map.</param>
/// <param name="Y">Its row on the map; north is smaller.</param>
/// <param name="Current">Whether the character is in it.</param>
/// <param name="Ahead">Whether a walk under way still goes through it.</param>
/// <param name="Locked">Whether the player locked it out of paths.</param>
public sealed record MapViewRoom(long Id, string Name, int X, int Y, bool Current, bool Ahead, bool Locked);

/// <summary>A drawn exit between two rooms of the view, once per pair.</summary>
public sealed record MapViewExit(long From, long To);

/// <summary>
/// The part of a character's map a view draws: the rooms of the current room's area on its level, nearest the
/// current room first and bounded, with the exits between them. The terminal draws its map from the recorder in
/// cells (<c>MapRenderer</c>); the window draws this as a canvas over the same pane. A click on a room is handed
/// back by id to the client, which walks there through the same speedwalk a cell click uses.
/// </summary>
public sealed record MapViewModel(string? Area, int Level, IReadOnlyList<MapViewRoom> Rooms, IReadOnlyList<MapViewExit> Exits, string? Walking)
{
    /// <summary>The most rooms a view is given: the nearest to the current room are kept.</summary>
    public const int MaxRooms = 1500;

    /// <summary>The longest room name a view is given.</summary>
    public const int MaxNameLength = 80;

    /// <summary>The room the character is in, or null.</summary>
    public MapViewRoom? Current => Rooms.FirstOrDefault(r => r.Current);

    /// <summary>
    /// The view of <paramref name="map"/> around <paramref name="currentRoomId"/>. With no current room, the level
    /// and area of the most recently added room are drawn, so a map recorded earlier still shows.
    /// </summary>
    public static MapViewModel From(WorldMap map, long? currentRoomId, IEnumerable<long> ahead, string? walking)
    {
        ArgumentNullException.ThrowIfNull(map);
        var anchor = currentRoomId is { } id ? map.Room(id) : null;
        anchor ??= map.Rooms.Values.LastOrDefault();
        if (anchor is null)
        {
            return new MapViewModel(null, 0, [], [], walking);
        }

        var path = ahead.ToHashSet();
        var rooms = map.Rooms.Values
            .Where(r => r.Z == anchor.Z && r.AreaId == anchor.AreaId)
            .OrderBy(r => Math.Abs(r.X - anchor.X) + Math.Abs(r.Y - anchor.Y))
            .ThenBy(r => r.Id)
            .Take(MaxRooms)
            .Select(r => new MapViewRoom(
                r.Id,
                r.Name.Length <= MaxNameLength ? r.Name : r.Name[..(MaxNameLength - 1)] + "…",
                r.X,
                r.Y,
                r.Id == currentRoomId,
                path.Contains(r.Id),
                r.Locked))
            .ToList();
        var ids = rooms.Select(r => r.Id).ToHashSet();
        var exits = new List<MapViewExit>();
        var seen = new HashSet<(long, long)>();
        foreach (var room in rooms)
        {
            foreach (var (_, _, to) in map.Room(room.Id)!.AllExits())
            {
                if (to == room.Id || !ids.Contains(to))
                {
                    continue;
                }

                var pair = room.Id < to ? (room.Id, to) : (to, room.Id);
                if (seen.Add(pair))
                {
                    exits.Add(new MapViewExit(pair.Item1, pair.Item2));
                }
            }
        }

        var area = anchor.AreaId is { } areaId && map.Areas.TryGetValue(areaId, out var named) ? named.Name : null;
        return new MapViewModel(area, anchor.Z, rooms, exits, walking);
    }

    /// <summary>Value equality over the rooms and exits, which a record's list members do not give.</summary>
    public bool Equals(MapViewModel? other) =>
        other is not null && Area == other.Area && Level == other.Level && Walking == other.Walking
        && Rooms.SequenceEqual(other.Rooms) && Exits.SequenceEqual(other.Exits);

    public override int GetHashCode() => HashCode.Combine(Area, Level, Walking, Rooms.Count, Exits.Count);
}
