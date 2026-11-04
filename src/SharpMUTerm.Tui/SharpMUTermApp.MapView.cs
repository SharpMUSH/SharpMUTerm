using System.Globalization;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Mapping;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// Looking at another level or area of a map, in both hosts, from one funnel. A map window draws where its character is
/// until the player asks to look elsewhere; then it holds a <see cref="MapViewpoint"/> (<see cref="_mapLooks"/>, per map
/// window, for the run) until they ask to come back. Every way of asking — the cell map's edit keys, its title and legend
/// links, a <c>/map</c> command, ⌃P, and the window's level buttons, wheel and area menu — is a <see cref="MapLook"/> that
/// ends in <see cref="LookMap"/>. Walking from a viewed map still walks to the room clicked: the view changes what is
/// drawn, never which room an id names.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Where each map window looks, when it is not where its character is.</summary>
    private readonly Dictionary<string, MapViewpoint> _mapLooks = new(StringComparer.Ordinal);

    /// <summary>The window's look callback, one instance per map window so an overlay compares equal frame to frame.</summary>
    private readonly Dictionary<string, Action<MapLook>> _mapLookers = new(StringComparer.Ordinal);

    /// <summary>Where a map window looks, or null while it follows its character. Internal for tests.</summary>
    internal MapViewpoint? MapLookOf(string windowId) => _mapLooks.GetValueOrDefault(windowId);

    /// <summary>
    /// Points a map window at another level or area, or back at its character: resolved by <see cref="MapLooking.Resolve"/>,
    /// said in a notice either way, and redrawn. While the map is being edited the selection moves to the room the view is
    /// now drawn around, so the keys edit a room that is on screen. Returns what refused it, or null.
    /// </summary>
    internal string? LookMap(string windowId, MapLook look)
    {
        if (MapOf(MapOwner(windowId)) is not { } map)
        {
            Notice("no map here: this window has no connection.", source: NoticeModel.Sources.Map);
            return "no map";
        }

        var result = MapLooking.Resolve(map.Map, map.Recorder.CurrentRoomId, MapLookOf(windowId), look);
        if (result.Done)
        {
            SetMapLook(windowId, map, result.Next);
        }

        Notice(result.Message + ".", result.Done ? MessageSeverity.Info : MessageSeverity.Warning, source: NoticeModel.Sources.Map);
        return result.Done ? null : result.Message;
    }

    private void SetMapLook(string windowId, SessionMap map, MapViewpoint? next)
    {
        if (next is null)
        {
            _mapLooks.Remove(windowId);
        }
        else
        {
            _mapLooks[windowId] = next;
        }

        if (_mapSelection.ContainsKey(windowId)
            && MapViewModel.From(map.Map, map.Recorder.CurrentRoomId, [], null, look: next).AnchorId is { } anchor)
        {
            _mapSelection[windowId] = anchor;
        }

        RenderMap(windowId);
    }

    /// <summary>The window's look callback for a map window: every native request through <see cref="LookMap"/>.</summary>
    private Action<MapLook> MapLookerFor(string windowId) =>
        _mapLookers.TryGetValue(windowId, out var known)
            ? known
            : _mapLookers[windowId] = look => OnUiThread(() => LookMap(windowId, look));

    /// <summary>The focused character's map window and map, for ⌃P's entries.</summary>
    private (string WindowId, SessionMap Map, string Key)? FocusedMap()
    {
        var key = SendTarget()?.SessionKey ?? _workspace.FindWindow(ActiveWindowId())?.SessionKey;
        return key is not null && MapOf(key) is { } map ? (MapWindowId(key), map, key) : null;
    }

    /// <summary>What ⌃P needs to know about the focused character's map: levels in the area shown, named areas, and whether it looks elsewhere.</summary>
    private (int Levels, int Areas, bool Viewing) MapCommandState()
    {
        if (FocusedMap() is not { } focused)
        {
            return (0, 0, false);
        }

        var (windowId, map, _) = focused;
        var home = map.Recorder.CurrentRoom ?? map.Map.Rooms.Values.LastOrDefault();
        if (home is null)
        {
            return (0, 0, false);
        }

        var look = MapLookOf(windowId);
        var areaId = look?.AreaId ?? home.AreaId;
        var levels = map.Map.Rooms.Values.Where(r => r.AreaId == areaId).Select(r => r.Z).Distinct().Count();
        return (levels, MapLooking.Choices(map.Map).Count, look is not null);
    }

    /// <summary>⌃P's map view entries: level up and down, the next area, back to where you are, and Find room….</summary>
    private void RunMapViewCommand(string id)
    {
        if (FocusedMap() is not { } focused)
        {
            RefuseCommand("no map here — pick a character first (⌃P ▸ Switch to …)");
            return;
        }

        var (windowId, _, key) = focused;
        if (id == "map:find")
        {
            if (ArmedComposer() is not null)
            {
                ReleaseComposer();
            }

            PlaceGrab(ActiveBar(), "/map find ", key);
            Notice("type part of a room's name or area — ⏎ lists the rooms.", MessageSeverity.Info, source: NoticeModel.Sources.Map);
            return;
        }

        if (_workspace.Layout.FindWindow(windowId) is null && !IsFloating(windowId))
        {
            ShowMap();
        }

        LookMap(windowId, id switch
        {
            "map:level-up" => MapLook.Up,
            "map:level-down" => MapLook.Down,
            "map:next-area" => MapLook.NextArea,
            _ => MapLook.Here,
        });
    }

    /// <summary>
    /// <c>/map level up|down</c>, <c>/map view &lt;area&gt;</c>, <c>/map here</c> and <c>/map find &lt;text&gt;</c>, for the
    /// window's own character's map. A search lists the rooms in the window it was typed in and points the map at the first.
    /// </summary>
    private void MapViewCommand(string windowId, SessionMap map, string sessionKey, string argument)
    {
        var mapWindow = MapWindowId(sessionKey);
        var words = argument.Trim();

        // Typed in the map's own tab, the answer goes to the character's window: a map window draws no lines.
        if (IsMapWindow(windowId) && FindSession(sessionKey) is { } owner && _sessionWindows.TryGetValue(owner, out var own))
        {
            windowId = own;
        }
        if (words.StartsWith("find", StringComparison.OrdinalIgnoreCase))
        {
            var text = words.Length > 4 ? words[4..].Trim() : string.Empty;
            if (text.Length == 0)
            {
                AppendWindowLine(windowId, "[dim]*** use /map find <part of a room's name or area>.[/]");
                return;
            }

            var (rooms, total) = MapLooking.Find(map.Map, text);
            if (rooms.Count == 0)
            {
                AppendWindowLine(windowId, $"[dim]*** no room on the map matches {Escape(text)}.[/]");
                return;
            }

            var shown = total > rooms.Count ? $"the first {rooms.Count.ToString(CultureInfo.InvariantCulture)} of {total.ToString(CultureInfo.InvariantCulture)}" : total.ToString(CultureInfo.InvariantCulture);
            AppendWindowLine(windowId, $"[dim]*** rooms matching {Escape(text)} ({shown}) · /walk <number> goes there:[/]");
            foreach (var room in rooms)
            {
                var where = (room.Area is { } area ? Escape(area) + " · " : string.Empty) + "level " + room.Level.ToString(CultureInfo.InvariantCulture);
                AppendWindowLine(windowId, $"[dim]***[/]   [bold]{room.Id.ToString(CultureInfo.InvariantCulture)}[/]  {Escape(room.Name)}  [dim]{where}[/]");
            }

            // The map shows the first match's area and level, so the room found is on screen.
            var first = rooms[0];
            var home = map.Recorder.CurrentRoom ?? map.Map.Rooms.Values.LastOrDefault();
            SetMapLook(mapWindow, map, home is not null && home.AreaId == first.AreaId && home.Z == first.Level ? null : new MapViewpoint(first.AreaId, first.Level));
            return;
        }

        var (look, problem) = MapLooking.Parse(words);
        if (look is null)
        {
            AppendWindowLine(windowId, $"[dim]*** {Escape(problem ?? "no such view")}.[/]");
            return;
        }

        LookMap(mapWindow, look);
    }
}
