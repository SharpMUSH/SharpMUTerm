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
    private bool _touched;

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

    /// <summary>
    /// Held while the map is being changed. The client changes it only on its UI thread, so that thread needs no
    /// lock to read it; a script, which runs elsewhere, takes this to read a consistent map. It is never held
    /// while <see cref="Changed"/> is raised, so a handler that ends up waiting on a script cannot deadlock.
    /// </summary>
    public object SyncRoot { get; } = new();

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
        lock (SyncRoot)
        {
            NoteCommandCore(command);
        }
    }

    private void NoteCommandCore(string command)
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

    /// <summary>A change made by hand (a room locked, say): applied under the lock, saved, and redrawn.</summary>
    public void Edit(Action<WorldMap> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (SyncRoot)
        {
            change(Map);
            IsDirty = true;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The most edits <see cref="Undo"/> can take back.</summary>
    public const int UndoDepth = 10;

    /// <summary>What an undo puts back: the rooms an edit touched as they were (null where it made one), the areas, and the pending link.</summary>
    private sealed record UndoEntry(string Label, IReadOnlyDictionary<long, MapRoom?> Rooms, IReadOnlyDictionary<int, MapArea> Areas, (long From, string Exit)? Pending);

    private readonly LinkedList<UndoEntry> _undo = new();

    /// <summary>
    /// An exit waiting for the next room the character walks into: <c>From</c>'s <c>Exit</c> will lead there. Set by
    /// <see cref="LinkNext"/>; read by a view to say so.
    /// </summary>
    public (long From, string Exit)? PendingLink { get; private set; }

    /// <summary>What <see cref="Undo"/> would take back, or null when nothing would.</summary>
    public string? UndoLabel
    {
        get
        {
            lock (SyncRoot)
            {
                return _undo.First?.Value.Label;
            }
        }
    }

    /// <summary>How many edits <see cref="Undo"/> can take back.</summary>
    public int UndoCount
    {
        get
        {
            lock (SyncRoot)
            {
                return _undo.Count;
            }
        }
    }

    /// <summary>
    /// A change made by hand that can be undone: the rooms <paramref name="touched"/> names are copied first, the
    /// change is applied under the lock, and when it answers true it is kept for <see cref="Undo"/> (the last
    /// <see cref="UndoDepth"/>), the map is marked dirty and redrawn. When it answers false nothing is kept or raised.
    /// </summary>
    public bool Edit(string label, IEnumerable<long> touched, Func<WorldMap, bool> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        bool changed;
        lock (SyncRoot)
        {
            var entry = Snapshot(label, touched);
            changed = change(Map);
            if (changed)
            {
                Push(entry);
                IsDirty = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    /// <summary>
    /// Arms <paramref name="exit"/> of room <paramref name="from"/> to lead to the next room the character walks into
    /// (Mudlet's "the room I go to next"). Undoable like any edit; a second arming replaces the first.
    /// </summary>
    public bool LinkNext(long from, string exit)
    {
        var key = WorldMap.ExitKey(exit);
        if (key.Length == 0)
        {
            return false;
        }

        lock (SyncRoot)
        {
            if (Map.Room(from) is null || PendingLink == (from, key))
            {
                return false;
            }

            Push(Snapshot($"wait to link {key}", []));
            PendingLink = (from, key);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The last report of each room, this run: what <see cref="ResetToServer"/> puts back.</summary>
    private readonly Dictionary<long, RoomObservation> _served = new();

    /// <summary>
    /// Gives a room back to the server: forgets which of its fields were edited by hand (<see cref="MapRoom.HandEdited"/>)
    /// and, when the server has reported the room this run, puts back its name, area, exits and place at once; otherwise
    /// the next report does. Undoable like any edit. False when nothing was edited by hand.
    /// </summary>
    public bool ResetToServer(long id, string label) => Edit(label, [id], map =>
    {
        if (map.Room(id) is not { } room || room.HandEdited.Count == 0)
        {
            return false;
        }

        var edited = room.HandEdited.ToList();
        room.HandEdited.Clear();
        if (_served.TryGetValue(id, out var seen))
        {
            if (edited.Contains(MapFields.Name) && seen.Name.Length > 0)
            {
                room.Name = seen.Name;
            }

            if (edited.Contains(MapFields.Area) && !string.IsNullOrWhiteSpace(seen.Area))
            {
                room.AreaId = map.AreaNamed(seen.Area).Id;
            }

            if (edited.Contains(MapFields.Exits))
            {
                SetExits(room, seen.Exits);
            }

            if (edited.Contains(MapFields.Position) && seen.Coordinates is { } at)
            {
                (room.X, room.Y, room.Z) = at;
            }
        }

        return true;
    });

    /// <summary>Whether the server has reported this room this run, so a reset can put its values back at once.</summary>
    public bool HasServerReport(long id)
    {
        lock (SyncRoot)
        {
            return _served.ContainsKey(id);
        }
    }

    /// <summary>Takes back the last edit. Returns what it took back, or null when there was nothing to.</summary>
    public string? Undo()
    {
        string label;
        lock (SyncRoot)
        {
            if (_undo.First?.Value is not { } entry)
            {
                return null;
            }

            _undo.RemoveFirst();
            foreach (var (id, room) in entry.Rooms)
            {
                if (room is null)
                {
                    Map.Rooms.Remove(id);
                }
                else
                {
                    Map.Rooms[id] = room.Clone();
                }
            }

            // Areas the edit made go again unless a room has come to use one since; the ones it had are put back.
            foreach (var id in Map.Areas.Keys.Where(id => !entry.Areas.ContainsKey(id) && Map.Rooms.Values.All(r => r.AreaId != id)).ToList())
            {
                Map.Areas.Remove(id);
            }

            foreach (var (id, area) in entry.Areas)
            {
                Map.Areas[id] = new MapArea { Id = area.Id, Name = area.Name };
            }

            PendingLink = entry.Pending;
            IsDirty = true;
            label = entry.Label;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return label;
    }

    private UndoEntry Snapshot(string label, IEnumerable<long> touched) => new(
        label,
        touched.Distinct().ToDictionary(id => id, id => Map.Room(id)?.Clone()),
        Map.Areas.ToDictionary(a => a.Key, a => new MapArea { Id = a.Value.Id, Name = a.Value.Name }),
        PendingLink);

    private void Push(UndoEntry entry)
    {
        _undo.AddFirst(entry);
        while (_undo.Count > UndoDepth)
        {
            _undo.RemoveLast();
        }
    }

    /// <summary>Applies one observation. Public so a test, or a script, can drive the recorder directly.</summary>
    public void Apply(RoomObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        lock (SyncRoot)
        {
            _touched = false;
            ApplyCore(observation);
        }

        if (_touched)
        {
            _touched = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyCore(RoomObservation observation)
    {
        if (observation.ExitsOnly)
        {
            if (Map.Room(observation.Id ?? CurrentRoomId ?? long.MinValue) is { } here)
            {
                if (_served.TryGetValue(here.Id, out var seen))
                {
                    _served[here.Id] = seen with { Exits = observation.Exits };
                }

                if (!here.HandEdited.Contains(MapFields.Exits))
                {
                    SetExits(here, observation.Exits);
                }

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
        _served[room.Id] = observation;

        // A field the player edited by hand is theirs: the report leaves it alone until "reset to server".
        var mine = room.HandEdited;
        if (observation.Name.Length > 0 && !mine.Contains(MapFields.Name))
        {
            room.Name = observation.Name;
        }

        if (mine.Contains(MapFields.Area))
        {
            // kept as the player set it
        }
        else if (!string.IsNullOrWhiteSpace(observation.Area))
        {
            room.AreaId = Map.AreaNamed(observation.Area).Id;
        }
        else if (created && previous is not null)
        {
            room.AreaId = previous.AreaId;
        }

        room.Environment = observation.Environment ?? room.Environment;
        if (!mine.Contains(MapFields.Exits))
        {
            SetExits(room, observation.Exits);
        }

        if (observation.Coordinates is { } at)
        {
            if (!mine.Contains(MapFields.Position))
            {
                (room.X, room.Y, room.Z) = at;
            }
        }
        else if (created)
        {
            Place(room, previous, walked);
        }

        if (previous is not null && walked is not null && previous.Id != room.Id)
        {
            Link(previous, walked, room);
        }

        // An exit the player asked to lead to "the next room I go to": this is it, unless it is the room it leaves.
        if (PendingLink is { } pending && pending.From != room.Id && Map.Room(pending.From) is not null)
        {
            Push(Snapshot($"link {pending.Exit}", [pending.From]));
            Map.SetExit(pending.From, pending.Exit, room.Id);
            PendingLink = null;
        }

        CurrentRoomId = room.Id;
        Touch();
    }

    private void Touch()
    {
        IsDirty = true;
        _touched = true;
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
