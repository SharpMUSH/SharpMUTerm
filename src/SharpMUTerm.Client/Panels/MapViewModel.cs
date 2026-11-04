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
/// <param name="Selected">Whether it is the room being edited (<see cref="MapViewModel.Selected"/>).</param>
/// <param name="Symbol">The one character the player marked it with, or null.</param>
/// <param name="Noted">Whether the player wrote a note on it.</param>
/// <param name="HandEdited">Whether a field the server reports was changed by hand (<see cref="MapRoom.HandEdited"/>).</param>
public sealed record MapViewRoom(
    long Id, string Name, int X, int Y, bool Current, bool Ahead, bool Locked, bool Up = false, bool Down = false, string? Elsewhere = null,
    bool Selected = false, string? Symbol = null, bool Noted = false, bool HandEdited = false);

/// <summary>An exit as a view names it: the direction or command, and the room it leads to, or none for a stub.</summary>
/// <param name="Exit">A direction's short command (<c>n</c>, <c>up</c>) or a special exit's command.</param>
/// <param name="To">The room it leads to, or null when nobody has walked it.</param>
/// <param name="Name">That room's name, bounded, or null.</param>
public sealed record MapViewExitName(string Exit, long? To, string? Name);

/// <summary>
/// The room being edited, as both hosts' inspectors show it: the cell map's side column or footer line, and the window's
/// inspector panel.
/// </summary>
public sealed record MapRoomDetails(
    long Id, string Name, string? Area, int X, int Y, int Z, int Weight, bool Locked, string? Symbol, string? Note, IReadOnlyList<MapViewExitName> Exits)
{
    /// <summary>
    /// The fields changed by hand (<see cref="MapFields"/>, in that order), which the server's next report leaves alone
    /// until the room is reset to the server.
    /// </summary>
    public IReadOnlyList<string> HandEdited { get; init; } = [];

    /// <summary>Value equality over the exits too, which a record's list member does not give.</summary>
    public bool Equals(MapRoomDetails? other) =>
        other is not null && Id == other.Id && Name == other.Name && Area == other.Area && (X, Y, Z) == (other.X, other.Y, other.Z)
        && Weight == other.Weight && Locked == other.Locked && Symbol == other.Symbol && Note == other.Note && Exits.SequenceEqual(other.Exits)
        && HandEdited.SequenceEqual(other.HandEdited);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Area, X, Y, Z, Weight, Exits.Count);
}

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

    /// <summary>The most areas <see cref="AreaChoices"/> lists.</summary>
    public const int MaxAreaChoices = 64;

    /// <summary>
    /// Whether the view is somewhere the player asked to look (<see cref="MapViewpoint"/>) rather than where the
    /// character is: both hosts then offer the way back.
    /// </summary>
    public bool Viewing { get; init; }

    /// <summary>The levels the drawn area has rooms on, lowest first: what the level buttons and keys step through.</summary>
    public IReadOnlyList<int> Levels { get; init; } = [];

    /// <summary>Whether the drawn area has a level above the one drawn.</summary>
    public bool LevelAbove => Levels.Any(z => z > Level);

    /// <summary>Whether the drawn area has a level below the one drawn.</summary>
    public bool LevelBelow => Levels.Any(z => z < Level);

    /// <summary>
    /// Every named area of the map that holds a room, by name, each in its colour and <see cref="MapViewArea.Drawn"/> for
    /// the one drawn: what the window's area menu lists and the cell map's area key cycles through.
    /// </summary>
    public IReadOnlyList<MapViewArea> AreaChoices { get; init; } = [];

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

    /// <summary>
    /// The current room's exits with the names of the rooms they lead to, directions first in compass order: what the
    /// cell map lists beside the grid when it has the width, so a terminal names the rooms around you as the canvas does.
    /// </summary>
    public IReadOnlyList<MapViewExitName> Nearby { get; init; } = [];

    /// <summary>The room being edited, or null when the map is not in edit mode.</summary>
    public MapRoomDetails? Selected { get; init; }

    /// <summary>Whether the map is in edit mode: a room is selected.</summary>
    public bool Editing => Selected is not null;

    /// <summary>An exit waiting for the next room walked into, said as a sentence, or null.</summary>
    public string? Pending { get; init; }

    /// <summary>What an undo would take back, or null when nothing would.</summary>
    public string? UndoLabel { get; init; }

    /// <summary>What a click on a room does, as a host words it under the map; null when no click would do anything.</summary>
    public string? Hint =>
        Editing ? EditHint
        : Walking is not null || Rooms.Count < 2 ? null
        : WalkHint;

    /// <summary>The map's hint while a click walks.</summary>
    public const string WalkHint = "Click a room to walk there";

    /// <summary>The map's hint while a click picks the room to edit.</summary>
    public const string EditHint = "Click a room to edit it";

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
    /// <param name="selected">
    /// The room being edited, or null. When it is not on the current room's level and area, the view is drawn around it
    /// instead, so the room being edited is always drawn.
    /// </param>
    /// <param name="pending">An exit waiting for the next room walked into (<see cref="MapRecorder.PendingLink"/>).</param>
    /// <param name="undo">What an undo would take back (<see cref="MapRecorder.UndoLabel"/>).</param>
    /// <param name="look">
    /// Another area or level to draw (<see cref="MapLooking.Resolve"/>), or null to draw where the character is. A viewpoint
    /// with no rooms is ignored. The view is then drawn around the room of that set nearest the character's place.
    /// </param>
    public static MapViewModel From(
        WorldMap map, long? currentRoomId, IEnumerable<long> ahead, string? walking, MapAreaPalette? palette = null,
        long? selected = null, (long From, string Exit)? pending = null, string? undo = null, MapViewpoint? look = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        palette ??= MapAreaPalette.Dark;
        var anchor = currentRoomId is { } id ? map.Room(id) : null;
        anchor ??= map.Rooms.Values.LastOrDefault();
        var home = anchor;
        var viewing = false;
        if (look is { } v && home is not null && (v.AreaId != home.AreaId || v.Level != home.Z)
            && map.Rooms.Values.Where(r => r.Z == v.Level && r.AreaId == v.AreaId)
                .OrderBy(r => Math.Abs(r.X - home.X) + Math.Abs(r.Y - home.Y)).ThenBy(r => r.Id).FirstOrDefault() is { } there)
        {
            anchor = there;
            viewing = true;
        }

        var chosen = selected is { } pick ? map.Room(pick) : null;
        if (!viewing && chosen is not null && (anchor is null || chosen.Z != anchor.Z || chosen.AreaId != anchor.AreaId))
        {
            anchor = chosen;
        }

        var pendingText = pending is { } link && map.Room(link.From) is { } source
            ? $"the next room you walk into is {MapDirections.Command(link.Exit)} from {Bounded(source.Name)}"
            : null;
        if (anchor is null)
        {
            return new MapViewModel(null, 0, [], [], walking) { Pending = pendingText, UndoLabel = undo };
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
                Elsewhere(map, r),
                r.Id == chosen?.Id,
                ValidSymbol(r.Symbol),
                r.Note is not null,
                r.HandEdited.Count > 0))
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

        var here = currentRoomId is { } at ? map.Room(at) : null;
        var choices = MapLooking.Choices(map)
            .Select(a => new MapViewArea(Bounded(a.Name), palette.Colour(Bounded(a.Name)), Drawn: a.Id == anchor.AreaId))
            .ToList();
        return new MapViewModel(area, anchor.Z, rooms, exits, walking)
        {
            AnchorId = anchor.Id,
            Areas = areas,
            Viewing = viewing,
            Levels = map.Rooms.Values.Where(r => r.AreaId == anchor.AreaId).Select(r => r.Z).Distinct().Order().ToList(),
            AreaChoices = choices,
            Nearby = here is null ? [] : ExitsOf(map, here),
            Selected = chosen is null ? null : DetailsOf(map, chosen),
            Pending = pendingText,
            UndoLabel = undo,
        };
    }

    /// <summary>A room's exits named: directions in compass order, then special exits by command, then stubs.</summary>
    private static List<MapViewExitName> ExitsOf(WorldMap map, MapRoom room)
    {
        var named = new List<MapViewExitName>();
        foreach (var direction in MapDirections.All)
        {
            if (room.Exits.TryGetValue(direction, out var to))
            {
                named.Add(new(MapDirections.Command(direction), to, map.Room(to) is { } next ? Bounded(next.Name) : null));
            }
            else if (room.Stubs.Contains(direction))
            {
                named.Add(new(MapDirections.Command(direction), null, null));
            }
        }

        foreach (var (command, to) in room.SpecialExits.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            named.Add(new(Bounded(command), to, map.Room(to) is { } next ? Bounded(next.Name) : null));
        }

        foreach (var stub in room.Stubs.Where(s => !MapDirections.All.Contains(s)).Order(StringComparer.Ordinal))
        {
            named.Add(new(Bounded(stub), null, null));
        }

        return named;
    }

    private static MapRoomDetails DetailsOf(WorldMap map, MapRoom room) => new(
        room.Id,
        Bounded(room.Name),
        AreaName(map, room.AreaId),
        room.X,
        room.Y,
        room.Z,
        room.Weight,
        room.Locked,
        ValidSymbol(room.Symbol),
        room.Note is { } note ? Bounded(note) : null,
        ExitsOf(map, room))
    {
        HandEdited = MapFields.All.Where(room.HandEdited.Contains).ToList(),
    };

    /// <summary>A name as a view is given it: at most <see cref="MaxNameLength"/> characters.</summary>
    public static string Bound(string name) => Bounded(name);

    /// <summary>A symbol a view may draw: its first character when that is one a cell can hold, else null.</summary>
    private static string? ValidSymbol(string? symbol) =>
        !string.IsNullOrEmpty(symbol) && WorldMap.IsSymbol(symbol[0]) ? symbol[..1] : null;

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
        && Rooms.SequenceEqual(other.Rooms) && Exits.SequenceEqual(other.Exits) && Areas.SequenceEqual(other.Areas)
        && Nearby.SequenceEqual(other.Nearby) && Equals(Selected, other.Selected) && Pending == other.Pending && UndoLabel == other.UndoLabel
        && Viewing == other.Viewing && Levels.SequenceEqual(other.Levels) && AreaChoices.SequenceEqual(other.AreaChoices);

    public override int GetHashCode() => HashCode.Combine(Area, Level, Walking, Rooms.Count, Exits.Count, Areas.Count);
}
