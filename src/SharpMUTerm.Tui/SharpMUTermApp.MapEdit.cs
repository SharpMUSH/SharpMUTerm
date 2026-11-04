using System.Globalization;
using System.Text;
using SharpConsoleUI;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Tui;

/// <summary>
/// Editing a map, in both hosts, from one model and one funnel. A map is in edit mode while a room of it is
/// <em>selected</em> (<see cref="_mapSelection"/>, per map window): ⌃P ▸ <c>Edit map</c> selects the room you are in, a
/// click on a cell room selects it while editing, and the window selects a room on a right click (its menu) or a click
/// while editing (its inspector). Every edit — a cell key, a <c>/map</c> command, a native menu entry or inspector field —
/// is a <see cref="MapEdit"/> that ends in <see cref="ApplyMapEdit"/>, which runs <see cref="MapEditing.Apply"/> on the
/// UI thread: under <see cref="MapRecorder.SyncRoot"/>, marked dirty (so it is saved with the recorded rooms), and kept
/// for undo.
/// <para>
/// <b>The keys are the map's while it holds them</b> (<see cref="_mapKeys"/>), the way ⌥C puts them on the sidebar: client
/// state the main window's key chain reads first, focus never moving. An edit that needs words puts its <c>/map</c>
/// command on the command line, filled with what the room holds now, and hands the keys over until it is sent; any other
/// key the map does not answer ends editing and goes on to the command line.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The room being edited, by map window. A window in this is in edit mode.</summary>
    private readonly Dictionary<string, long> _mapSelection = new(StringComparer.Ordinal);

    /// <summary>The map window that holds the keyboard, or null.</summary>
    private string? _mapKeys;

    /// <summary>The overlay callbacks, one instance per window so an overlay compares equal frame to frame.</summary>
    private readonly Dictionary<string, Action<long?>> _mapSelectors = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Action<MapEdit>> _mapEditors = new(StringComparer.Ordinal);

    /// <summary>What the command line held when a map key put a <c>/map</c> command on it, put back once that command runs.</summary>
    private string? _mapLentDraft;

    /// <summary>Whether a map holds the keys. Internal for tests.</summary>
    internal bool MapKeysActive => _mapKeys is not null;

    /// <summary>The room selected on a map window, or null. Internal for tests.</summary>
    internal long? MapSelection(string windowId) => _mapSelection.TryGetValue(windowId, out var room) ? room : null;

    /// <summary>
    /// One map window's model: the recorder's map around the current room, the walk, and the edit state. The cell map and
    /// the window's native map are both drawn from what this builds.
    /// </summary>
    private MapViewModel BuildMapView(string windowId, SessionMap map)
    {
        var walking = map.Walk.IsWalking ? $"walking, {map.Walk.Remaining} to go" : null;
        long? selected = _mapSelection.TryGetValue(windowId, out var room) && map.Map.Room(room) is not null ? room : null;
        return MapViewModel.From(
            map.Map, map.Recorder.CurrentRoomId, map.Walk.RoomsAhead, walking, MapPalette(), selected, map.Recorder.PendingLink, map.Recorder.UndoLabel);
    }

    /// <summary>
    /// ⌃P ▸ <c>Edit map</c>: the focused character's map goes into edit mode on the room you are in (opening the map if no
    /// pane shows it) and takes the keys. Again while editing: takes the keys back if a <c>/map</c> command had them, else
    /// ends editing.
    /// </summary>
    private void ToggleMapEdit()
    {
        var key = SendTarget()?.SessionKey ?? _workspace.FindWindow(ActiveWindowId())?.SessionKey;
        if (MapOf(key) is not { } map)
        {
            RefuseCommand("no map here — pick a character first (⌃P ▸ Switch to …)");
            return;
        }

        var windowId = MapWindowId(key!);
        if (_mapSelection.ContainsKey(windowId))
        {
            if (_mapKeys != windowId)
            {
                _mapKeys = windowId;
                RenderMap(windowId);
                return;
            }

            LeaveMapEdit(windowId);
            return;
        }

        var start = map.Recorder.CurrentRoomId ?? MapViewModel.From(map.Map, null, [], null).AnchorId;
        if (start is null)
        {
            RefuseCommand("nothing is mapped yet — walk somewhere first");
            return;
        }

        if (_workspace.Layout.FindWindow(windowId) is null || IsFloating(windowId))
        {
            ShowMap();
        }

        SelectMapRoom(windowId, start);
    }

    /// <summary>
    /// Selects a room on a map window (null ends editing): the one way selection changes, from the cell keys, a cell click
    /// while editing and the window's map. Selecting takes the keys.
    /// </summary>
    /// <summary>Whose map a map window draws: its recorded owner, else the session key its id names (a map not yet opened).</summary>
    private string? MapOwner(string windowId) =>
        _workspace.FindWindow(windowId)?.SessionKey
        ?? (windowId.StartsWith(MapWindowPrefix, StringComparison.Ordinal) ? windowId[MapWindowPrefix.Length..] : null);

    internal void SelectMapRoom(string windowId, long? room)
    {
        if (room is not { } id)
        {
            LeaveMapEdit(windowId);
            return;
        }

        if (MapOf(MapOwner(windowId)) is not { } map || map.Map.Room(id) is null)
        {
            return;
        }

        var entering = !_mapSelection.ContainsKey(windowId);
        _mapSelection[windowId] = id;
        _mapKeys = windowId;
        RenderMap(windowId);
        if (entering)
        {
            Notice("map: ←↑→↓ select · r rename · l lock · Del delete · u undo · Esc done", MessageSeverity.Info, "⌃P", source: NoticeModel.Sources.Map);
        }
    }

    /// <summary>Ends editing on a map window: no room selected, and the keys back on the command line.</summary>
    private void LeaveMapEdit(string windowId)
    {
        var was = _mapSelection.Remove(windowId);
        if (_mapKeys == windowId)
        {
            _mapKeys = null;
            was = true;
        }

        if (was)
        {
            RenderMap(windowId);
        }
    }

    /// <summary>
    /// Every map edit ends here, from either host: applied to the window's own character's map on the UI thread
    /// (<see cref="MapEditing.Apply"/>), said in a notice, and redrawn by the recorder's <c>Changed</c>. A selected room an
    /// edit took away (a delete, an undo of an added room) moves the selection to the room you are in. Returns what
    /// refused it, or null.
    /// </summary>
    internal string? ApplyMapEdit(string windowId, MapEdit edit)
    {
        if (MapOf(MapOwner(windowId)) is not { } map)
        {
            Notice("no map here: this window has no connection.", source: NoticeModel.Sources.Map);
            return "no map";
        }

        var result = MapEditing.Apply(map.Recorder, edit);
        if (_mapSelection.TryGetValue(windowId, out var selected) && map.Map.Room(selected) is null)
        {
            var next = map.Recorder.CurrentRoomId ?? MapViewModel.From(map.Map, null, [], null).AnchorId;
            if (next is { } room)
            {
                _mapSelection[windowId] = room;
            }
            else
            {
                _mapSelection.Remove(windowId);
            }
        }

        if (_mapSelection.ContainsKey(windowId) && _mapKeys is null)
        {
            _mapKeys = windowId; // an edit finished from the command line gives the keys back to the map it edits
        }

        RenderMap(windowId);
        Notice(result.Message + ".", result.Done ? MessageSeverity.Info : MessageSeverity.Warning, source: NoticeModel.Sources.Map);
        return result.Done ? null : result.Message;
    }

    /// <summary><c>/map</c> with an editing verb: parsed into an edit for the window's own character's map.</summary>
    private void MapEditCommand(string windowId, SessionMap map, string sessionKey, string argument)
    {
        var (edit, problem) = MapEditing.Parse(argument, name => (name is null ? map.Recorder.CurrentRoom : map.Find(name))?.Id);
        if (edit is null)
        {
            AppendWindowLine(windowId, $"[dim]*** {MarkupText.Escape(problem ?? "no such edit")}.[/]");
            return;
        }

        ApplyMapEdit(MapWindowId(sessionKey), edit);
        if (_mapLentDraft is { } draft)
        {
            _mapLentDraft = null;
            ActiveBar().SetAndNotify(draft); // the line being typed when a map key borrowed the command line comes back
        }
    }

    /// <summary>
    /// A key while a map holds the keyboard. Returns true for the keys it answers (<see cref="MapEditing.Keys"/>); any other
    /// ends editing and returns false, so the key goes on down the chain to the command line.
    /// </summary>
    private bool TryMapKey(KeyPressedEventArgs e)
    {
        if (_mapKeys is not { } windowId)
        {
            return false;
        }

        if (!_mapSelection.TryGetValue(windowId, out var room) || MapOf(MapOwner(windowId)) is not { } map)
        {
            _mapKeys = null;
            return false;
        }

        var key = e.KeyInfo;
        var bare = key.Modifiers == 0;
        var shift = key.Modifiers == ConsoleModifiers.Shift;
        (int Dx, int Dy)? arrow = key.Key switch
        {
            ConsoleKey.LeftArrow => (-1, 0),
            ConsoleKey.RightArrow => (1, 0),
            ConsoleKey.UpArrow => (0, -1),
            ConsoleKey.DownArrow => (0, 1),
            _ => null,
        };

        if (arrow is { } step && bare)
        {
            if (MapEditing.Neighbour(BuildMapView(windowId, map), room, step.Dx, step.Dy) is { } next)
            {
                SelectMapRoom(windowId, next);
            }
        }
        else if (arrow is { } nudge && shift)
        {
            ApplyMapEdit(windowId, MapEdit.Move(room, nudge.Dx, nudge.Dy));
        }
        else if (key.Key == ConsoleKey.Delete && bare)
        {
            ApplyMapEdit(windowId, MapEdit.Delete(room));
        }
        else if (key.Key == ConsoleKey.Escape)
        {
            LeaveMapEdit(windowId);
        }
        else if (key.Key == ConsoleKey.Enter && bare)
        {
            if (map.Recorder.CurrentRoomId != room)
            {
                WalkFromMap(windowId, room);
            }
        }
        else if (key.Modifiers is (ConsoleModifiers)0 or ConsoleModifiers.Shift && key.KeyChar switch
        {
            'l' => Edit(MapEdit.Lock(room, map.Map.Room(room)?.Locked != true)),
            'u' => Edit(MapEdit.Undo),
            'r' => Words(MapEditing.RenameVerb),
            'a' => Words(MapEditing.AreaVerb),
            'w' => Words(MapEditing.WeightVerb),
            'x' => Words(MapEditing.ExitVerb),
            '-' => Words(MapEditing.UnexitVerb),
            'n' => Words(MapEditing.NoteVerb),
            's' => Words(MapEditing.SymbolVerb),
            _ => false,
        })
        {
            // answered in the switch
        }
        else
        {
            LeaveMapEdit(windowId);
            return false;
        }

        e.Handled = true;
        return true;

        bool Edit(MapEdit edit)
        {
            ApplyMapEdit(windowId, edit);
            return true;
        }

        bool Words(string verb)
        {
            if (BuildMapView(windowId, map).Selected is not { } details || MapEditing.CommandFor(verb, details) is not { } command)
            {
                return false;
            }

            // The command goes on the command line and the keys with it; ⏎ there applies it and gives them back.
            _mapKeys = null;
            if (ArmedComposer() is not null)
            {
                ReleaseComposer();
            }

            _mapLentDraft = ActiveBar().Text.Length > 0 ? ActiveBar().Text : null;
            PlaceGrab(ActiveBar(), command, MapOwner(windowId));
            RenderMap(windowId);
            Notice("finish the /map command on the command line — ⏎ applies it.", MessageSeverity.Info, source: NoticeModel.Sources.Map);
            return true;
        }
    }

    /// <summary>Drives a key into the map's key handling as the main window would. Internal for tests.</summary>
    internal bool SimulateMapKey(ConsoleKeyInfo key) => TryMapKey(new KeyPressedEventArgs(key, false));

    /// <summary>The window's selector callback for a map window: a room selected (null closes the inspector), on the client's thread.</summary>
    private Action<long?> MapSelectorFor(string windowId) =>
        _mapSelectors.TryGetValue(windowId, out var known)
            ? known
            : _mapSelectors[windowId] = room => OnUiThread(() => SelectMapRoom(windowId, room));

    /// <summary>The window's edit callback for a map window: every native edit through <see cref="ApplyMapEdit"/>.</summary>
    private Action<MapEdit> MapEditorFor(string windowId) =>
        _mapEditors.TryGetValue(windowId, out var known)
            ? known
            : _mapEditors[windowId] = edit => OnUiThread(() => ApplyMapEdit(windowId, edit));

    /// <summary>
    /// Every map this client holds, as text: each room with its name, area, place, weight, lock, symbol, note and exits,
    /// the pending link, how many edits can be undone, and what each map window has selected. Two clients that made the
    /// same edits read the same. Internal for tests.
    /// </summary>
    internal string MapDigest()
    {
        var text = new StringBuilder();
        var maps = _recorders.Values.ToList();
        if (_demoMap is not null)
        {
            maps.Add(_demoMap);
        }

        foreach (var map in maps)
        {
            lock (map.Recorder.SyncRoot)
            {
                foreach (var room in map.Map.Rooms.Values.OrderBy(r => r.Id))
                {
                    var area = room.AreaId is { } a && map.Map.Areas.TryGetValue(a, out var named) ? named.Name : "-";
                    text.Append(CultureInfo.InvariantCulture, $"{room.Id}:{room.Name}@{area}({room.X},{room.Y},{room.Z})w{room.Weight}")
                        .Append(room.Locked ? "L" : string.Empty)
                        .Append(CultureInfo.InvariantCulture, $"[{room.Symbol}|{room.Note}]{{")
                        .AppendJoin(',', room.AllExits().Select(e => $"{e.Key}>{e.To}").Order(StringComparer.Ordinal))
                        .Append('/')
                        .AppendJoin(',', room.Stubs.Order(StringComparer.Ordinal))
                        .Append("} ");
                }
            }

            text.Append(CultureInfo.InvariantCulture, $"pending={map.Recorder.PendingLink} undo={map.Recorder.UndoCount} ");
        }

        foreach (var (window, room) in _mapSelection.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"selected {window}={room} ");
        }

        return text.Append("keys=").Append(_mapKeys).ToString();
    }
}
