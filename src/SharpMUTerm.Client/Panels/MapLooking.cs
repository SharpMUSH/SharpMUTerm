using System.Globalization;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Client.Panels;

/// <summary>
/// Where a map view looks when it is not where the character is: one area's one level. Held by the client per map window;
/// null means the view follows the character (<see cref="MapViewModel.Viewing"/> false).
/// </summary>
/// <param name="AreaId">The area shown (<see cref="MapArea.Id"/>), or null for rooms in no area.</param>
/// <param name="Level">The level shown (z).</param>
public sealed record MapViewpoint(int? AreaId, int Level);

/// <summary>What a request to look elsewhere on the map asks for.</summary>
public enum MapLookKind
{
    /// <summary>Back to where the character is.</summary>
    Here,

    /// <summary>The next level up in the area shown that has rooms.</summary>
    Up,

    /// <summary>The next level down in the area shown that has rooms.</summary>
    Down,

    /// <summary>An area by name (<see cref="MapLook.Area"/>).</summary>
    Area,

    /// <summary>The next area by name, after the one shown.</summary>
    NextArea,
}

/// <summary>
/// A request to look at another level or area of a map, as both hosts make it: the cell map's keys, its title and legend
/// links, a <c>/map</c> command, and the window's buttons, wheel and area menu. Every one ends in the client's
/// <c>LookMap</c>, which resolves it with <see cref="MapLooking.Resolve"/>.
/// </summary>
public sealed record MapLook(MapLookKind Kind, string? Area = null)
{
    public static MapLook Here { get; } = new(MapLookKind.Here);

    public static MapLook Up { get; } = new(MapLookKind.Up);

    public static MapLook Down { get; } = new(MapLookKind.Down);

    public static MapLook NextArea { get; } = new(MapLookKind.NextArea);

    public static MapLook ToArea(string area) => new(MapLookKind.Area, area);
}

/// <summary>A room a search found: its number, name and area (bounded), and where it is, which a view can be pointed at.</summary>
public sealed record MapFoundRoom(long Id, string Name, string? Area, int? AreaId, int Level);

/// <summary>What resolving a <see cref="MapLook"/> did: whether the view moved, where it now looks, and a sentence.</summary>
public sealed record MapLookResult(bool Done, MapViewpoint? Next, string Message);

/// <summary>Looking at another level or area of a map: resolving a request, the window's area menu, and reading <c>/map</c> words.</summary>
public static class MapLooking
{
    /// <summary>The area menu's entry verbs: <c>area:N</c> picks <see cref="MapViewModel.AreaChoices"/>[N]; <c>here</c> goes back.</summary>
    public const string AreaVerbPrefix = "area:", HereVerb = "here";

    /// <summary>
    /// Where <paramref name="look"/> takes a view that now looks at <paramref name="now"/> (null: where the character is,
    /// or with no current room the most recently recorded room). A viewpoint that is where the character is comes back
    /// null, so the view follows them again. Nothing is changed by a request that is refused.
    /// </summary>
    public static MapLookResult Resolve(WorldMap map, long? currentRoomId, MapViewpoint? now, MapLook look)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(look);
        var home = (currentRoomId is { } id ? map.Room(id) : null) ?? map.Rooms.Values.LastOrDefault();
        if (home is null)
        {
            return new(false, now, "nothing is mapped yet");
        }

        var at = now ?? new MapViewpoint(home.AreaId, home.Z);
        MapViewpoint next;
        switch (look.Kind)
        {
            case MapLookKind.Here:
                return now is null ? new(false, null, "the map already shows where you are") : new(true, null, "the map shows where you are again");
            case MapLookKind.Up or MapLookKind.Down:
            {
                var up = look.Kind == MapLookKind.Up;
                var levels = map.Rooms.Values.Where(r => r.AreaId == at.AreaId).Select(r => r.Z).Distinct().ToList();
                var further = levels.Where(z => up ? z > at.Level : z < at.Level).ToList();
                if (further.Count == 0)
                {
                    return new(false, now, $"no level {(up ? "above" : "below")} {at.Level.ToString(CultureInfo.InvariantCulture)} in {AreaLabel(map, at.AreaId)}");
                }

                next = at with { Level = up ? further.Min() : further.Max() };
                break;
            }

            case MapLookKind.Area or MapLookKind.NextArea:
            {
                var choices = Choices(map);
                MapArea? area;
                if (look.Kind == MapLookKind.NextArea)
                {
                    if (choices.Count < 2)
                    {
                        return new(false, now, "this map has only one area");
                    }

                    var index = choices.FindIndex(a => a.Id == at.AreaId);
                    area = choices[(index + 1) % choices.Count];
                }
                else
                {
                    var name = (look.Area ?? string.Empty).Trim();
                    area = choices.FirstOrDefault(a => string.Equals(MapViewModel.Bound(a.Name), name, StringComparison.Ordinal))
                        ?? choices.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                        ?? choices.FirstOrDefault(a => a.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) && name.Length > 0);
                    if (area is null)
                    {
                        return new(false, now, $"no area called {Snippet(name)} on this map");
                    }

                    if (area.Id == at.AreaId)
                    {
                        return new(false, now, $"the map already shows {Snippet(area.Name)}");
                    }
                }

                // The level the character is on when the area has rooms there, else the nearest level it has.
                var levels = map.Rooms.Values.Where(r => r.AreaId == area.Id).Select(r => r.Z).Distinct().ToList();
                var level = levels.Contains(home.Z) ? home.Z : levels.OrderBy(z => Math.Abs(z - at.Level)).ThenBy(z => z).First();
                next = new MapViewpoint(area.Id, level);
                break;
            }

            default:
                return new(false, now, "nothing to look at");
        }

        if (next.AreaId == home.AreaId && next.Level == home.Z)
        {
            return new(true, null, "the map shows where you are again");
        }

        return new(true, next, $"the map shows {AreaLabel(map, next.AreaId)}, level {next.Level.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>The areas a view can be pointed at: every named area that holds a room, by name.</summary>
    public static List<MapArea> Choices(WorldMap map)
    {
        var used = map.Rooms.Values.Select(r => r.AreaId).OfType<int>().ToHashSet();
        return map.Areas.Values
            .Where(a => used.Contains(a.Id) && !string.IsNullOrWhiteSpace(a.Name))
            .OrderBy(a => a.Name, StringComparer.Ordinal)
            .ThenBy(a => a.Id)
            .Take(MapViewModel.MaxAreaChoices)
            .ToList();
    }

    /// <summary>
    /// The window's area menu: each area the map has, the one shown checked off by being disabled, then a way back to
    /// where the character is while the view is elsewhere.
    /// </summary>
    public static MenuModel AreaMenu(MapViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var entries = new List<MenuEntry>();
        for (var i = 0; i < view.AreaChoices.Count; i++)
        {
            var area = view.AreaChoices[i];
            entries.Add(new MenuEntry(Snippet(area.Name), area.Drawn ? "shown" : null, area.Drawn ? null : AreaVerbPrefix + i.ToString(CultureInfo.InvariantCulture), Enabled: !area.Drawn));
        }

        if (view.Viewing)
        {
            entries.Add(MenuEntry.Separator);
            entries.Add(new MenuEntry("Back to where I am", "h", HereVerb));
        }

        return new MenuModel(entries);
    }

    /// <summary>The look an area menu verb asks for, or null for a verb that is not one.</summary>
    public static MapLook? LookFor(string verb, MapViewModel view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (verb == HereVerb)
        {
            return MapLook.Here;
        }

        return verb.StartsWith(AreaVerbPrefix, StringComparison.Ordinal)
            && int.TryParse(verb.AsSpan(AreaVerbPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index < view.AreaChoices.Count
                ? MapLook.ToArea(view.AreaChoices[index].Name)
                : null;
    }

    /// <summary>The <c>/map</c> verbs that look elsewhere, which <see cref="Parse"/> reads.</summary>
    public static IReadOnlyList<string> CommandVerbs { get; } = ["level", "view", "here"];

    /// <summary>
    /// <c>/map level up|down</c>, <c>/map view &lt;area&gt;</c> (or <c>next</c>), <c>/map here</c>, as a look. Null with a
    /// problem for anything else.
    /// </summary>
    public static (MapLook? Look, string? Problem) Parse(string argument)
    {
        var words = (argument ?? string.Empty).Trim();
        var space = words.IndexOf(' ');
        var verb = (space < 0 ? words : words[..space]).ToLowerInvariant();
        var rest = space < 0 ? string.Empty : words[(space + 1)..].Trim();
        return verb switch
        {
            "here" => (MapLook.Here, null),
            "level" when rest.Equals("up", StringComparison.OrdinalIgnoreCase) || rest == "+" => (MapLook.Up, null),
            "level" when rest.Equals("down", StringComparison.OrdinalIgnoreCase) || rest == "-" => (MapLook.Down, null),
            "level" => (null, "use /map level up or /map level down"),
            "view" when rest.Length == 0 => (null, "use /map view <area>, /map view next or /map here"),
            "view" when rest.Equals("next", StringComparison.OrdinalIgnoreCase) => (MapLook.NextArea, null),
            "view" => (MapLook.ToArea(rest), null),
            _ => (null, "use /map level up|down, /map view <area> or /map here"),
        };
    }

    /// <summary>The most rooms <see cref="Find"/> lists.</summary>
    public const int MaxFound = 20;

    /// <summary>
    /// Rooms whose name or area holds <paramref name="text"/>, ignoring case: name matches first, then area matches, each
    /// by name and then number; at most <see cref="MaxFound"/>, with how many matched in all.
    /// </summary>
    public static (IReadOnlyList<MapFoundRoom> Rooms, int Total) Find(WorldMap map, string text)
    {
        ArgumentNullException.ThrowIfNull(map);
        var query = (text ?? string.Empty).Trim();
        if (query.Length == 0)
        {
            return ([], 0);
        }

        string? AreaOf(MapRoom r) => r.AreaId is { } a && map.Areas.TryGetValue(a, out var area) && !string.IsNullOrWhiteSpace(area.Name) ? area.Name : null;
        var matches = map.Rooms.Values
            .Select(r => (Room: r, Area: AreaOf(r), ByName: r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Where(m => m.ByName || (m.Area?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(m => m.ByName ? 0 : 1)
            .ThenBy(m => m.Room.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Room.Id)
            .ToList();
        return (matches.Take(MaxFound)
            .Select(m => new MapFoundRoom(m.Room.Id, MapViewModel.Bound(m.Room.Name), m.Area is null ? null : MapViewModel.Bound(m.Area), m.Room.AreaId, m.Room.Z))
            .ToList(), matches.Count);
    }

    private static string AreaLabel(WorldMap map, int? areaId) =>
        areaId is { } id && map.Areas.TryGetValue(id, out var area) && !string.IsNullOrWhiteSpace(area.Name) ? Snippet(area.Name) : "no area";

    private static string Snippet(string text) => text.Length <= 40 ? text : text[..39] + "…";
}
