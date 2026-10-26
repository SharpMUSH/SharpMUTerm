namespace SharpMUTerm.Core.Mapping;

/// <summary>
/// One connection's map and the walk across it: what the map window draws, what <c>/walk</c> and a click on a
/// room start, and what a script's <c>map.*</c> reads. Held by the session (<see cref="Session.WorldSession.Map"/>)
/// so the scripting bridge can reach it without knowing about the client.
/// <para>
/// The map is changed on the client's UI thread. <see cref="Post"/> is how anything else asks for a change, and
/// every read from elsewhere takes <see cref="MapRecorder.SyncRoot"/>.
/// </para>
/// </summary>
public sealed class SessionMap : IDisposable
{
    public SessionMap(MapRecorder recorder, Speedwalk walk)
    {
        Recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        Walk = walk ?? throw new ArgumentNullException(nameof(walk));
    }

    public MapRecorder Recorder { get; }

    public Speedwalk Walk { get; }

    public WorldMap Map => Recorder.Map;

    /// <summary>Where a change asked for from another thread is run. Null runs it in place.</summary>
    public Action<Action>? Post { get; init; }

    /// <summary>The room the character is in: id, name and area name, or null before the first room.</summary>
    public (long Id, string Name, string? Area)? Current
    {
        get
        {
            lock (Recorder.SyncRoot)
            {
                if (Recorder.CurrentRoom is not { } room)
                {
                    return null;
                }

                var area = room.AreaId is { } a && Map.Areas.TryGetValue(a, out var found) ? found.Name : null;
                return (room.Id, room.Name, area);
            }
        }
    }

    /// <summary>
    /// The room <paramref name="target"/> names: a room number, or else a room name, ignoring case. Among rooms
    /// sharing a name the nearest by path wins, then the lowest number, so <c>/walk Town Square</c> on a map with
    /// two of them goes to the one you can reach soonest.
    /// </summary>
    public MapRoom? Find(string target)
    {
        var text = target.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        lock (Recorder.SyncRoot)
        {
            if (long.TryParse(text, out var id) && Map.Room(id) is { } byId)
            {
                return byId;
            }

            var named = Map.Rooms.Values
                .Where(r => string.Equals(r.Name, text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Id)
                .ToList();
            if (named.Count <= 1 || Recorder.CurrentRoomId is not { } here)
            {
                return named.FirstOrDefault();
            }

            return named
                .Select(r => (Room: r, Steps: PathFinder.Find(Map, here, r.Id)?.Count ?? int.MaxValue))
                .OrderBy(p => p.Steps)
                .ThenBy(p => p.Room.Id)
                .First().Room;
        }
    }

    /// <summary>The way from the current room to <paramref name="to"/>, or null when there is none.</summary>
    public IReadOnlyList<PathStep>? PathTo(long to)
    {
        lock (Recorder.SyncRoot)
        {
            return Recorder.CurrentRoomId is { } here ? PathFinder.Find(Map, here, to) : null;
        }
    }

    /// <summary>
    /// Starts walking to <paramref name="target"/> (a number or a name). Returns null when the walk started,
    /// otherwise what stopped it, for the player.
    /// </summary>
    public string? WalkTo(string target)
    {
        if (Find(target) is not { } room)
        {
            return $"no room on the map is called {Snippet(target)}";
        }

        return WalkTo(room.Id);
    }

    /// <inheritdoc cref="WalkTo(string)"/>
    public string? WalkTo(long to)
    {
        if (Recorder.CurrentRoomId is null)
        {
            return "the map does not know where you are yet";
        }

        if (PathTo(to) is not { } path)
        {
            return "the map knows no way there";
        }

        Run(() => Walk.Start(path));
        return null;
    }

    /// <summary>Locks or unlocks a room, so a path never goes through it.</summary>
    public bool SetLocked(long id, bool locked)
    {
        if (Map.Room(id) is null)
        {
            return false;
        }

        Run(() => Recorder.Edit(map =>
        {
            if (map.Room(id) is { } room)
            {
                room.Locked = locked;
            }
        }));
        return true;
    }

    public void Dispose()
    {
        Walk.Dispose();
        Recorder.Dispose();
    }

    private void Run(Action action)
    {
        if (Post is { } post)
        {
            post(action);
        }
        else
        {
            action();
        }
    }

    private static string Snippet(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= 40 ? $"'{trimmed}'" : $"'{trimmed[..39]}…'";
    }
}
