namespace SharpMUTerm.Core.Mapping;

/// <summary>
/// Builds a map from where the character goes: every <see cref="RoomObservation"/> a source reports adds or
/// updates a room, links it to the room it was walked to from, and places it on the grid.
/// <para>
/// <b>The way a room was left is the last movement command sent</b> (<see cref="NoteCommand"/>): a direction,
/// or a command one of the current room's special exits is taken by. Anything else is not a move and leaves
/// it alone, so <c>say hi</c> between <c>n</c> and the room that answers it changes nothing.
/// </para>
/// <para>
/// <b>Placement</b>, for a room the server gave no coordinates: one step from the room it was walked from, in
/// the direction walked (<see cref="MapDirections.Offset"/>; up and down change z), pushed further along that
/// direction while the cell is taken. A room reached by <c>in</c>, <c>out</c> or a special exit has no
/// direction to step in, so it is put in the nearest free cell around the room before it. Coordinates a
/// server sends are taken as they are, every time.
/// </para>
/// <para>
/// <b>A room with no number</b> (the text source) is the room the walked exit already leads to when the map
/// knows one, the current room again when nothing was walked and the name agrees, and otherwise a new room
/// numbered past every id the map holds.
/// </para>
/// </summary>
public sealed class MapRecorder : IDisposable
{
    private readonly IReadOnlyList<IRoomSource> _sources;
    private string? _walked;

    public MapRecorder(WorldMap map, params IRoomSource[] sources)
    {
        Map = map ?? throw new ArgumentNullException(nameof(map));
        _sources = sources;
        foreach (var source in _sources)
        {
            source.RoomEntered += OnRoom;
        }
    }

    public WorldMap Map { get; }

    /// <summary>The room the character is in, or null before the first observation.</summary>
    public long? CurrentRoomId { get; private set; }

    public MapRoom? CurrentRoom => CurrentRoomId is { } id ? Map.Room(id) : null;

    /// <summary>Whether the map has changed since the last <see cref="MarkSaved"/>.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Raised after an observation has been applied: the character moved, or the room changed.</summary>
    public event EventHandler? Changed;

    public void MarkSaved() => IsDirty = false;

    public void Dispose()
    {
        foreach (var source in _sources)
        {
            source.RoomEntered -= OnRoom;
            source.Dispose();
        }
    }

    /// <summary>
    /// A command the player sent. A direction, or a special exit of the current room (linked or still a stub),
    /// is the next move.
    /// </summary>
    public void NoteCommand(string command)
    {
        var trimmed = command.Trim();
        if (MapDirections.Normalise(trimmed) is { } direction)
        {
            _walked = direction;
        }
        else if (CurrentRoom is { } room && (room.SpecialExits.ContainsKey(trimmed) || room.Stubs.Contains(trimmed)))
        {
            _walked = trimmed;
        }
    }

    /// <summary>
    /// Where observations are applied. Sources raise on the telnet read loop; the client sets this to its UI
    /// thread so the map is only ever touched there. Null applies in place, which is what a test wants.
    /// </summary>
    public Action<Action>? Post { get; init; }

    private void OnRoom(object? sender, RoomObservation room)
    {
        if (Post is { } post)
        {
            post(() => Apply(room));
        }
        else
        {
            Apply(room);
        }
    }

    /// <summary>Applies one observation. Public so a test, or a script, can drive the recorder directly.</summary>
    public void Apply(RoomObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.ExitsOnly)
        {
            if (Map.Room(observation.Id ?? CurrentRoomId ?? long.MinValue) is { } here)
            {
                SetExits(here, observation.Exits);
                Touch();
            }

            return;
        }

        var previous = CurrentRoom;
        var walked = _walked;
        _walked = null;

        var id = observation.Id ?? Identify(observation, previous, walked);
        var room = Map.Room(id);
        var created = room is null;
        room ??= Map.Add(new MapRoom { Id = id });

        if (observation.Name.Length > 0)
        {
            room.Name = observation.Name;
        }

        if (!string.IsNullOrWhiteSpace(observation.Area))
        {
            room.AreaId = Map.AreaNamed(observation.Area).Id;
        }
        else if (created && previous is not null)
        {
            room.AreaId = previous.AreaId;
        }

        room.Environment = observation.Environment ?? room.Environment;
        SetExits(room, observation.Exits);

        if (observation.Coordinates is { } at)
        {
            (room.X, room.Y, room.Z) = at;
        }
        else if (created)
        {
            Place(room, previous, walked);
        }

        if (previous is not null && walked is not null && previous.Id != room.Id)
        {
            Link(previous, walked, room);
        }

        CurrentRoomId = room.Id;
        Touch();
    }

    private void Touch()
    {
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private long Identify(RoomObservation observation, MapRoom? previous, string? walked)
    {
        if (previous is not null)
        {
            if (walked is not null
                && (previous.Exits.TryGetValue(walked, out var known) || previous.SpecialExits.TryGetValue(walked, out known))
                && Map.Room(known) is not null)
            {
                return known;
            }

            if (walked is null && string.Equals(previous.Name, observation.Name, StringComparison.Ordinal))
            {
                return previous.Id;
            }
        }

        return Map.Rooms.Count == 0 ? 1 : Math.Max(0, Map.Rooms.Keys.Max()) + 1;
    }

    /// <summary>
    /// The room's exits as the source reported them. A source with destinations (GMCP) is authoritative and
    /// replaces the exits wholesale; an exit reported with no destination keeps a link the map already has
    /// and is otherwise a stub.
    /// </summary>
    private static void SetExits(MapRoom room, IReadOnlyList<ObservedExit> exits)
    {
        var directions = new Dictionary<string, long>(StringComparer.Ordinal);
        var special = new Dictionary<string, long>(StringComparer.Ordinal);
        var stubs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, to) in exits)
        {
            var isDirection = MapDirections.All.Contains(key);
            var target = isDirection ? directions : special;
            if (to is { } id)
            {
                target[key] = id;
            }
            else if ((isDirection ? room.Exits : room.SpecialExits).TryGetValue(key, out var linked))
            {
                target[key] = linked;
            }
            else
            {
                stubs.Add(key);
            }
        }

        room.Exits = directions;
        room.SpecialExits = special;
        room.Stubs = stubs;
    }

    /// <summary>
    /// Records that <paramref name="walked"/> from <paramref name="from"/> leads to <paramref name="to"/>, and,
    /// when <paramref name="to"/> has a stub back the opposite way, that it leads back. A link the server
    /// already gave is left alone.
    /// </summary>
    private static void Link(MapRoom from, string walked, MapRoom to)
    {
        var isDirection = MapDirections.All.Contains(walked);
        var exits = isDirection ? from.Exits : from.SpecialExits;
        if (!exits.ContainsKey(walked))
        {
            exits[walked] = to.Id;
        }

        from.Stubs.Remove(walked);

        if (isDirection && MapDirections.Reverse(walked) is { } back && to.Stubs.Remove(back))
        {
            to.Exits[back] = from.Id;
        }
    }

    private void Place(MapRoom room, MapRoom? previous, string? walked)
    {
        if (previous is null)
        {
            (room.X, room.Y, room.Z) = (0, 0, 0);
            return;
        }

        var (dx, dy, dz) = walked is null ? (0, 0, 0) : MapDirections.Offset(walked);
        if ((dx, dy, dz) != (0, 0, 0))
        {
            var (x, y, z) = (previous.X + dx, previous.Y + dy, previous.Z + dz);
            for (var guard = 0; guard < 64 && Map.IsOccupied(room.AreaId, x, y, z, room.Id); guard++)
            {
                (x, y, z) = (x + dx, y + dy, z + dz);
            }

            (room.X, room.Y, room.Z) = (x, y, z);
            return;
        }

        // No direction to step in: the nearest free cell around the previous room, ring by ring.
        for (var ring = 1; ring < 64; ring++)
        {
            for (var oy = -ring; oy <= ring; oy++)
            {
                for (var ox = -ring; ox <= ring; ox++)
                {
                    if (Math.Max(Math.Abs(ox), Math.Abs(oy)) != ring)
                    {
                        continue;
                    }

                    if (!Map.IsOccupied(room.AreaId, previous.X + ox, previous.Y + oy, previous.Z, room.Id))
                    {
                        (room.X, room.Y, room.Z) = (previous.X + ox, previous.Y + oy, previous.Z);
                        return;
                    }
                }
            }
        }

        (room.X, room.Y, room.Z) = (previous.X, previous.Y, previous.Z);
    }
}
