namespace SharpMUTerm.Core.Mapping;

/// <summary>One step of a walk: the command to send, and the room it should reach.</summary>
public readonly record struct PathStep(string Command, long To);

/// <summary>
/// The cheapest walk between two rooms, as commands. Each step costs its exit's weight when it has one and
/// otherwise the room it enters; locked rooms and locked exits are never taken, and an exit to a room the map
/// does not hold is not a way anywhere.
/// <para>
/// <b>Dijkstra, not A* with a distance heuristic.</b> A heuristic has to be a lower bound to stay correct, and
/// a map's coordinates are not one: special exits and portals jump across the grid, placement nudges rooms
/// off their true step, and areas share coordinates. A zero heuristic is always admissible, and a 10,000-room
/// map is still a few milliseconds.
/// </para>
/// </summary>
public static class PathFinder
{
    /// <summary>
    /// The steps from <paramref name="from"/> to <paramref name="to"/>: empty when they are the same room, null
    /// when there is no way.
    /// </summary>
    public static IReadOnlyList<PathStep>? Find(WorldMap map, long from, long to)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Room(from) is null || map.Room(to) is not { Locked: false })
        {
            return null;
        }

        if (from == to)
        {
            return [];
        }

        var cost = new Dictionary<long, long> { [from] = 0 };
        var previous = new Dictionary<long, (long Room, string Command)>();
        var queue = new PriorityQueue<long, long>();
        queue.Enqueue(from, 0);

        while (queue.TryDequeue(out var current, out var reached))
        {
            if (current == to)
            {
                return Walk(previous, from, to);
            }

            if (reached > cost[current] || map.Room(current) is not { } room)
            {
                continue; // a stale entry, superseded by a cheaper one
            }

            foreach (var (command, key, next) in room.AllExits())
            {
                if (room.LockedExits.Contains(key) || map.Room(next) is not { Locked: false } target)
                {
                    continue;
                }

                var step = room.ExitWeights.TryGetValue(key, out var weight) && weight > 0
                    ? weight
                    : Math.Max(1, target.Weight);
                var total = reached + step;
                if (cost.TryGetValue(next, out var known) && known <= total)
                {
                    continue;
                }

                cost[next] = total;
                previous[next] = (current, command);
                queue.Enqueue(next, total);
            }
        }

        return null;
    }

    private static List<PathStep> Walk(Dictionary<long, (long Room, string Command)> previous, long from, long to)
    {
        var steps = new List<PathStep>();
        for (var at = to; at != from; at = previous[at].Room)
        {
            steps.Add(new PathStep(previous[at].Command, at));
        }

        steps.Reverse();
        return steps;
    }
}
