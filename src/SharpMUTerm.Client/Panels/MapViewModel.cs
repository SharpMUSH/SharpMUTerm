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
/// <param name="Up">Whether it has a way to a level above: an <c>up</c> exit, or any exit to a room higher up.</param>
/// <param name="Down">Whether it has a way to a level below.</param>
/// <param name="Elsewhere">
/// The name of another area one of its exits leads into (the first by name), or null. Both views mark the way out in
/// that area's colour (<see cref="MapViewModel.ColourOf"/>).
/// </param>
public sealed record MapViewRoom(
    long Id, string Name, int X, int Y, bool Current, bool Ahead, bool Locked, bool Up = false, bool Down = false, string? Elsewhere = null);

/// <summary>A drawn exit between two rooms of the view, once per pair.</summary>
public sealed record MapViewExit(long From, long To);

/// <summary>An area a view names, with the colour every view draws it in.</summary>
/// <param name="Name">The area's name, bounded; the game's text.</param>
/// <param name="Colour">Its colour as <c>#rrggbb</c>, from <see cref="MapAreaPalette"/>: stable for the name.</param>
/// <param name="Drawn">True for the area the view draws; false for one its exits lead into.</param>
public sealed record MapViewArea(string Name, string Colour, bool Drawn);

/// <summary>
/// The part of a character's map a view draws: the rooms of the current room's area on its level, nearest the
/// current room first and bounded, with the exits between them, which rooms have a way up or down, which lead into
/// another area, and the colour of every area named. The terminal draws its map in cells (<c>MapRenderer</c>) and the
/// window draws a canvas over the same pane, both from this, so they cannot disagree about where the character is,
/// what colour an area is or which rooms have stairs. A click on a room is handed back by id to the client, which
/// walks there through the same speedwalk a cell click uses.
/// </summary>
public sealed record MapViewModel(string? Area, int Level, IReadOnlyList<MapViewRoom> Rooms, IReadOnlyList<MapViewExit> Exits, string? Walking)
{
    /// <summary>The most rooms a view is given: the nearest to the current room are kept.</summary>
    public const int MaxRooms = 1500;

    /// <summary>The longest room or area name a view is given.</summary>
    public const int MaxNameLength = 80;

    /// <summary>
    /// The room the view is drawn around: the character's, or with no current room the most recently recorded one.
    /// Null when nothing is mapped. The cell map centres on this room too, so the two views never disagree about it.
    /// </summary>
    public long? AnchorId { get; init; }

    /// <summary>
    /// The areas the view names, with their colours: the drawn area first (when it has a name), then every area an
    /// exit of a drawn room leads into, by name. Both views read their area colours here and nowhere else, and both
    /// draw their legend from this list.
    /// </summary>
    public IReadOnlyList<MapViewArea> Areas { get; init; } = [];

    /// <summary>The room the character is in, or null.</summary>
    public MapViewRoom? Current => Rooms.FirstOrDefault(r => r.Current);

    /// <summary>The colour of the area the view draws, or null when that area has no name.</summary>
    public string? AreaColour => Areas.FirstOrDefault(a => a.Drawn)?.Colour;

    /// <summary>The colour the view gives the area called <paramref name="name"/>, or null when it names no such area.</summary>
    public string? ColourOf(string? name) =>
        name is null ? null : Areas.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal))?.Colour;

    /// <summary>
    /// The view of <paramref name="map"/> around <paramref name="currentRoomId"/>. With no current room, the level
    /// and area of the most recently added room are drawn, so a map recorded earlier still shows.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="currentRoomId">The character's room, or null.</param>
    /// <param name="ahead">The rooms a walk under way still goes through.</param>
    /// <param name="walking">What the walk says about itself, or null.</param>
    /// <param name="palette">The area colours for the theme in use (<see cref="MapAreaPalette.Dark"/> by default).</param>
    public static MapViewModel From(WorldMap map, long? currentRoomId, IEnumerable<long> ahead, string? walking, MapAreaPalette? palette = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        palette ??= MapAreaPalette.Dark;
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
                Bounded(r.Name),
                r.X,
                r.Y,
                r.Id == currentRoomId,
                path.Contains(r.Id),
                r.Locked,
                Climbs(map, r, up: true),
                Climbs(map, r, up: false),
                Elsewhere(map, r)))
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

        var area = AreaName(map, anchor.AreaId);
        var areas = new List<MapViewArea>();
        if (area is not null)
        {
            areas.Add(new MapViewArea(area, palette.Colour(area), Drawn: true));
        }

        foreach (var other in rooms.Select(r => r.Elsewhere).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!string.Equals(other, area, StringComparison.Ordinal))
            {
                areas.Add(new MapViewArea(other, palette.Colour(other), Drawn: false));
            }
        }

        return new MapViewModel(area, anchor.Z, rooms, exits, walking) { AnchorId = anchor.Id, Areas = areas };
    }

    private static string Bounded(string name) => name.Length <= MaxNameLength ? name : name[..(MaxNameLength - 1)] + "…";

    /// <summary>An area's name, bounded, or null for none.</summary>
    private static string? AreaName(WorldMap map, int? areaId) =>
        areaId is { } id && map.Areas.TryGetValue(id, out var named) && !string.IsNullOrWhiteSpace(named.Name)
            ? Bounded(named.Name)
            : null;

    /// <summary>Whether <paramref name="room"/> has a way up (or down): the direction itself, or an exit to another level.</summary>
    private static bool Climbs(WorldMap map, MapRoom room, bool up)
    {
        var direction = up ? "up" : "down";
        if (room.Exits.ContainsKey(direction) || room.Stubs.Contains(direction))
        {
            return true;
        }

        foreach (var (_, _, to) in room.AllExits())
        {
            if (map.Room(to) is { } next && (up ? next.Z > room.Z : next.Z < room.Z))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The first other area, by name, that an exit of <paramref name="room"/> leads into.</summary>
    private static string? Elsewhere(WorldMap map, MapRoom room)
    {
        string? first = null;
        foreach (var (_, _, to) in room.AllExits())
        {
            if (map.Room(to) is { } next && next.AreaId != room.AreaId && AreaName(map, next.AreaId) is { } name
                && (first is null || string.CompareOrdinal(name, first) < 0))
            {
                first = name;
            }
        }

        return first;
    }

    /// <summary>Value equality over the rooms, exits and areas, which a record's list members do not give.</summary>
    public bool Equals(MapViewModel? other) =>
        other is not null && Area == other.Area && Level == other.Level && Walking == other.Walking && AnchorId == other.AnchorId
        && Rooms.SequenceEqual(other.Rooms) && Exits.SequenceEqual(other.Exits) && Areas.SequenceEqual(other.Areas);

    public override int GetHashCode() => HashCode.Combine(Area, Level, Walking, Rooms.Count, Exits.Count, Areas.Count);
}
