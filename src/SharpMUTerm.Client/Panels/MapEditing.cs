using System.Globalization;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Client.Panels;

/// <summary>What a map edit does.</summary>
public enum MapEditKind
{
    Rename,
    Area,
    Lock,
    Unlock,
    Weight,

    /// <summary>An exit pointed at a room the map already has.</summary>
    AddExit,

    /// <summary>An exit that will lead to the next room the character walks into.</summary>
    LinkNext,
    RemoveExit,
    Move,
    Delete,
    Note,
    Symbol,

    /// <summary>The last edit taken back.</summary>
    Undo,

    /// <summary>The room's hand-edited fields given back to the server (<see cref="MapRecorder.ResetToServer"/>).</summary>
    ResetToServer,
}

/// <summary>
/// One edit to a map, as both hosts ask for it: the cell map's keys and <c>/map</c> commands parse into one, and the
/// window's room menu and inspector build one. Every edit goes through <see cref="MapEditing.Apply"/>, so a native
/// gesture and a cell key that ask for the same thing leave the same map.
/// </summary>
/// <param name="Kind">What it does.</param>
/// <param name="Room">The room it acts on (ignored by <see cref="MapEditKind.Undo"/>).</param>
/// <param name="Text">A name, an area, a note, a symbol or an exit's direction or command.</param>
/// <param name="Number">A weight.</param>
/// <param name="Target">The room an added exit leads to.</param>
/// <param name="Dx">A move's step east.</param>
/// <param name="Dy">A move's step south.</param>
/// <param name="Dz">A move's step up.</param>
public sealed record MapEdit(MapEditKind Kind, long Room, string? Text = null, int Number = 0, long? Target = null, int Dx = 0, int Dy = 0, int Dz = 0)
{
    public static MapEdit Rename(long room, string name) => new(MapEditKind.Rename, room, name);

    public static MapEdit Area(long room, string? area) => new(MapEditKind.Area, room, area);

    public static MapEdit Lock(long room, bool locked) => new(locked ? MapEditKind.Lock : MapEditKind.Unlock, room);

    public static MapEdit Weight(long room, int weight) => new(MapEditKind.Weight, room, Number: weight);

    public static MapEdit AddExit(long room, string exit, long to) => new(MapEditKind.AddExit, room, exit, Target: to);

    public static MapEdit LinkNext(long room, string exit) => new(MapEditKind.LinkNext, room, exit);

    public static MapEdit RemoveExit(long room, string exit) => new(MapEditKind.RemoveExit, room, exit);

    public static MapEdit Move(long room, int dx, int dy, int dz = 0) => new(MapEditKind.Move, room, Dx: dx, Dy: dy, Dz: dz);

    public static MapEdit Delete(long room) => new(MapEditKind.Delete, room);

    public static MapEdit Note(long room, string? note) => new(MapEditKind.Note, room, note);

    public static MapEdit Symbol(long room, string? symbol) => new(MapEditKind.Symbol, room, symbol);

    public static MapEdit Undo { get; } = new(MapEditKind.Undo, 0);

    public static MapEdit ResetToServer(long room) => new(MapEditKind.ResetToServer, room);
}

/// <summary>What an edit did: whether the map changed, and a sentence for the player either way.</summary>
public sealed record MapEditResult(bool Done, string Message);

/// <summary>
/// Map editing, for both hosts: applying an edit to a recorder (the one place an edit meets the map), reading
/// <c>/map</c> commands into edits, the keys the cell map answers in edit mode, and the window's room menu.
/// </summary>
public static class MapEditing
{
    /// <summary>A room menu entry's verbs (<see cref="MenuEntry.Command"/>). A verb that needs words opens the inspector at that field.</summary>
    public const string WalkVerb = "walk", RenameVerb = "rename", AreaVerb = "area", NoteVerb = "note", SymbolVerb = "symbol",
        WeightVerb = "weight", ExitVerb = "exit", UnexitVerb = "unexit", LockVerb = "lock", UnlockVerb = "unlock", DeleteVerb = "delete",
        UndoVerb = "undo", ResetVerb = "reset";

    /// <summary>The verbs that need words, which a host asks for in its inspector before an edit is made.</summary>
    public static IReadOnlyList<string> TextVerbs { get; } = [RenameVerb, AreaVerb, NoteVerb, SymbolVerb, WeightVerb, ExitVerb];

    /// <summary>
    /// The keys the cell map answers while it holds the keyboard in edit mode, in the order its footer names them. Every
    /// one is answered by the cell map's key handler; a test holds the two to each other.
    /// </summary>
    public static IReadOnlyList<(string Key, string Word)> Keys { get; } =
    [
        ("←↑→↓", "select"),
        ("⇧←↑→↓", "move"),
        ("PgUp PgDn", "level"),
        ("v", "next area"),
        ("h", "here"),
        ("r", "rename"),
        ("a", "area"),
        ("l", "lock"),
        ("w", "weight"),
        ("x", "exit"),
        ("-", "remove exit"),
        ("n", "note"),
        ("s", "symbol"),
        ("Del", "delete"),
        ("R", "reset to server"),
        ("u", "undo"),
        ("⏎", "walk"),
        ("Esc", "done"),
    ];

    /// <summary>
    /// Applies <paramref name="edit"/> to <paramref name="recorder"/>'s map: checked first, then made through
    /// <see cref="MapRecorder.Edit(string, IEnumerable{long}, Func{WorldMap, bool})"/> (under its lock, marked dirty,
    /// kept for undo), or <see cref="MapRecorder.LinkNext"/> and <see cref="MapRecorder.Undo"/>. Call it on the thread
    /// that owns the map — the client's UI thread.
    /// </summary>
    public static MapEditResult Apply(MapRecorder recorder, MapEdit edit)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.Kind == MapEditKind.Undo)
        {
            return recorder.Undo() is { } undone ? new(true, $"undid: {undone}") : new(false, "nothing to undo");
        }

        var map = recorder.Map;
        if (map.Room(edit.Room) is not { } room)
        {
            return new(false, $"no room {edit.Room.ToString(CultureInfo.InvariantCulture)} on the map");
        }

        var name = Snippet(room.Name.Length > 0 ? room.Name : "#" + room.Id.ToString(CultureInfo.InvariantCulture));
        var id = edit.Room;
        var text = edit.Text?.Trim() ?? string.Empty;
        var exit = WorldMap.ExitKey(text);
        switch (edit.Kind)
        {
            case MapEditKind.Rename when text.Length == 0:
                return new(false, "a room needs a name");
            case MapEditKind.Weight when edit.Number < 1 || edit.Number > WorldMap.MaxWeight:
                return new(false, $"a weight is 1 to {WorldMap.MaxWeight.ToString(CultureInfo.InvariantCulture)}");
            case MapEditKind.AddExit or MapEditKind.LinkNext or MapEditKind.RemoveExit when exit.Length == 0:
                return new(false, "name the exit: a direction (n, ne, up…) or the command that takes it");
            case MapEditKind.AddExit when edit.Target is not { } to || map.Room(to) is null:
                return new(false, $"no room {edit.Target?.ToString(CultureInfo.InvariantCulture) ?? "?"} to lead to");
            case MapEditKind.Symbol when text.Length > 1 || (text.Length == 1 && !WorldMap.IsSymbol(text[0])):
                return new(false, "a symbol is one printable character");
            case MapEditKind.Delete when recorder.CurrentRoomId == id:
                return new(false, "you are in that room — walk out of it first");
        }

        if (edit.Kind == MapEditKind.ResetToServer)
        {
            var reported = recorder.HasServerReport(id);
            return recorder.ResetToServer(id, $"reset {name} to server")
                ? new(true, reported ? $"{name} is as the server last said" : $"{name} takes the server's next report")
                : new(false, $"nothing on {name} was edited by hand");
        }

        if (edit.Kind == MapEditKind.LinkNext)
        {
            return recorder.LinkNext(id, exit)
                ? new(true, $"the next room you walk into is {exit} from {name}")
                : new(false, "that exit is already waiting");
        }

        var touched = edit.Kind == MapEditKind.Delete
            ? map.Rooms.Values.Where(r => r.Id == id || r.AllExits().Any(e => e.To == id)).Select(r => r.Id).ToList()
            : [id];
        var (label, done) = edit.Kind switch
        {
            MapEditKind.Rename => ($"rename {name}", $"renamed {name} to {Snippet(text)}"),
            MapEditKind.Area => ($"area of {name}", text.Length == 0 ? $"{name} is in no area" : $"{name} is in {Snippet(text)}"),
            MapEditKind.Lock => ($"lock {name}", $"{name} is locked: no walk goes through it"),
            MapEditKind.Unlock => ($"unlock {name}", $"{name} is unlocked"),
            MapEditKind.Weight => ($"weight of {name}", $"{name} weighs {edit.Number.ToString(CultureInfo.InvariantCulture)}"),
            MapEditKind.AddExit => ($"exit {exit} from {name}", $"{exit} from {name} leads to room {edit.Target!.Value.ToString(CultureInfo.InvariantCulture)}"),
            MapEditKind.RemoveExit => ($"remove exit {exit}", $"{name} has no {exit} exit now"),
            MapEditKind.Move => ($"move {name}", $"moved {name}"),
            MapEditKind.Delete => ($"delete {name}", $"deleted {name} — undo brings it back"),
            MapEditKind.Note => ($"note on {name}", text.Length == 0 ? $"{name} has no note" : $"noted {name}"),
            MapEditKind.Symbol => ($"symbol of {name}", text.Length == 0 ? $"{name} has no symbol" : $"{name} is marked {text}"),
            _ => (string.Empty, string.Empty),
        };

        var changed = recorder.Edit(label, touched, m => edit.Kind switch
        {
            MapEditKind.Rename => m.Rename(id, text),
            MapEditKind.Area => m.SetArea(id, text),
            MapEditKind.Lock => m.SetLocked(id, true),
            MapEditKind.Unlock => m.SetLocked(id, false),
            MapEditKind.Weight => m.SetWeight(id, edit.Number),
            MapEditKind.AddExit => m.SetExit(id, text, edit.Target!.Value),
            MapEditKind.RemoveExit => m.RemoveExit(id, text),
            MapEditKind.Move => m.Move(id, edit.Dx, edit.Dy, edit.Dz),
            MapEditKind.Delete => m.Remove(id),
            MapEditKind.Note => m.SetNote(id, text),
            MapEditKind.Symbol => m.SetSymbol(id, text),
            _ => false,
        });
        return changed ? new(true, done) : new(false, $"nothing to change on {name}");
    }

    /// <summary>The <c>/map</c> verbs that edit, which <see cref="Parse"/> reads.</summary>
    public static IReadOnlyList<string> CommandVerbs { get; } =
        ["rename", "area", "lock", "unlock", "weight", "exit", "unexit", "move", "delete", "note", "symbol", "undo", "reset"];

    /// <summary>
    /// A <c>/map</c> command's words after <c>/map</c>, as an edit: <c>rename 12 The Hall</c>, <c>area 12 Keep</c>,
    /// <c>lock|unlock [room]</c>, <c>weight 12 5</c>, <c>exit 12 ne 14</c> (or <c>next</c>: the room walked into next),
    /// <c>unexit 12 ne</c>, <c>move 12 1 0 [0]</c>, <c>delete 12</c>, <c>note 12 [text]</c>, <c>symbol 12 [c]</c>,
    /// <c>undo</c>. A room is its number; <paramref name="resolve"/> reads a name for lock and unlock (and gives the
    /// current room for none). Null edit with a problem when the words are not one of these.
    /// </summary>
    public static (MapEdit? Edit, string? Problem) Parse(string argument, Func<string?, long?> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        var words = (argument ?? string.Empty).Trim();
        var space = words.IndexOf(' ');
        var verb = (space < 0 ? words : words[..space]).ToLowerInvariant();
        var rest = space < 0 ? string.Empty : words[(space + 1)..].Trim();
        if (verb == "undo")
        {
            return (MapEdit.Undo, null);
        }

        if (verb is "lock" or "unlock")
        {
            return resolve(rest.Length == 0 ? null : rest) is { } locked
                ? (MapEdit.Lock(locked, verb == "lock"), null)
                : (null, "no such room");
        }

        var usage = verb switch
        {
            "rename" => "/map rename <room> <name>",
            "area" => "/map area <room> [area]",
            "weight" => "/map weight <room> <1-1000>",
            "exit" => "/map exit <room> <direction or command> <room|next>",
            "unexit" => "/map unexit <room> <direction or command>",
            "move" => "/map move <room> <dx> <dy> [dz]",
            "delete" => "/map delete <room>",
            "note" => "/map note <room> [text]",
            "symbol" => "/map symbol <room> [character]",
            "reset" => "/map reset <room>",
            _ => null,
        };
        if (usage is null)
        {
            return (null, "use /map lock|unlock [room], rename, area, weight, exit, unexit, move, delete, note, symbol, reset or undo");
        }

        space = rest.IndexOf(' ');
        var first = space < 0 ? rest : rest[..space];
        var tail = space < 0 ? string.Empty : rest[(space + 1)..].Trim();
        if (!long.TryParse(first, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var room))
        {
            return (null, $"use {usage} — a room is its number");
        }

        switch (verb)
        {
            case "rename":
                return tail.Length == 0 ? (null, $"use {usage}") : (MapEdit.Rename(room, tail), null);
            case "area":
                return (MapEdit.Area(room, tail), null);
            case "note":
                return (MapEdit.Note(room, tail), null);
            case "symbol":
                return (MapEdit.Symbol(room, tail), null);
            case "delete":
                return (MapEdit.Delete(room), null);
            case "reset":
                return (MapEdit.ResetToServer(room), null);
            case "weight":
                return int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out var weight)
                    ? (MapEdit.Weight(room, weight), null)
                    : (null, $"use {usage}");
            case "unexit":
                return tail.Length == 0 ? (null, $"use {usage}") : (MapEdit.RemoveExit(room, tail), null);
            case "exit":
            {
                var last = tail.LastIndexOf(' ');
                if (last < 0)
                {
                    return (null, $"use {usage}");
                }

                var exit = tail[..last].Trim();
                var to = tail[(last + 1)..];
                if (to.Equals("next", StringComparison.OrdinalIgnoreCase))
                {
                    return (MapEdit.LinkNext(room, exit), null);
                }

                return long.TryParse(to, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var target)
                    ? (MapEdit.AddExit(room, exit, target), null)
                    : (null, $"use {usage}");
            }

            case "move":
            {
                var parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var steps = new int[3];
                for (var i = 0; i < parts.Length; i++)
                {
                    if (parts.Length is < 2 or > 3 || !int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out steps[i]))
                    {
                        return (null, $"use {usage}");
                    }
                }

                return parts.Length < 2 ? (null, $"use {usage}") : (MapEdit.Move(room, steps[0], steps[1], steps[2]), null);
            }
        }

        return (null, $"use {usage}");
    }

    /// <summary>
    /// The <c>/map</c> command a text edit starts from, put on the command line for the player to finish: the room's
    /// number and what it holds now, so ⏎ with nothing changed changes nothing. Null for a verb that needs no words.
    /// </summary>
    public static string? CommandFor(string verb, MapRoomDetails room)
    {
        ArgumentNullException.ThrowIfNull(room);
        var id = room.Id.ToString(CultureInfo.InvariantCulture);
        return verb switch
        {
            RenameVerb => $"/map rename {id} {room.Name}",
            AreaVerb => $"/map area {id} {room.Area}",
            NoteVerb => $"/map note {id} {room.Note}",
            SymbolVerb => $"/map symbol {id} {room.Symbol}",
            WeightVerb => $"/map weight {id} {room.Weight.ToString(CultureInfo.InvariantCulture)}",
            ExitVerb => $"/map exit {id} ",
            UnexitVerb => $"/map unexit {id} ",
            _ => null,
        };
    }

    /// <summary>
    /// The menu a right click on a room opens in the window: walk there, the edits that need words (each opens the
    /// inspector at its field), lock or unlock, delete, and undo when there is something to undo. Each entry carries the
    /// cell map's key for the same edit.
    /// </summary>
    public static MenuModel Menu(MapViewRoom room, string? undo)
    {
        ArgumentNullException.ThrowIfNull(room);
        var here = room.Current;
        var entries = new List<MenuEntry>
        {
            new($"Walk to {Snippet(room.Name)}", "⏎", here ? null : WalkVerb, Enabled: !here),
            MenuEntry.Separator,
            new("Rename…", "r", RenameVerb),
            new("Set area…", "a", AreaVerb),
            new("Note…", "n", NoteVerb),
            new("Symbol…", "s", SymbolVerb),
            new("Weight…", "w", WeightVerb),
            new("Add exit…", "x", ExitVerb),
            room.Locked ? new("Unlock", "l", UnlockVerb) : new("Lock", "l", LockVerb),
            MenuEntry.Separator,
            new("Delete room", "Del", here ? null : DeleteVerb, Enabled: !here),
        };
        if (room.HandEdited)
        {
            entries.Add(new("Reset to server", "R", ResetVerb));
        }

        if (undo is not null)
        {
            entries.Add(new($"Undo {Snippet(undo)}", "u", UndoVerb));
        }

        return new MenuModel(entries);
    }

    /// <summary>The edit a menu verb that needs no words makes on <paramref name="room"/>, or null for one that does.</summary>
    public static MapEdit? EditFor(string verb, long room) => verb switch
    {
        LockVerb => MapEdit.Lock(room, true),
        UnlockVerb => MapEdit.Lock(room, false),
        DeleteVerb => MapEdit.Delete(room),
        UndoVerb => MapEdit.Undo,
        ResetVerb => MapEdit.ResetToServer(room),
        _ => null,
    };

    /// <summary>
    /// The room nearest <paramref name="from"/> in a direction on the view's grid (<paramref name="dx"/> east,
    /// <paramref name="dy"/> south), or null when there is none that way. A room not in the view starts from the anchor.
    /// </summary>
    public static long? Neighbour(MapViewModel view, long from, int dx, int dy)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Rooms.FirstOrDefault(r => r.Id == from) is not { } start)
        {
            return view.AnchorId;
        }

        // Ahead in the direction asked, no further to the side than ahead; nearest first, a step aside costing two forward.
        return view.Rooms
            .Where(r => r.Id != from)
            .Select(r => (Room: r, Ahead: ((r.X - start.X) * dx) + ((r.Y - start.Y) * dy), Side: Math.Abs(((r.X - start.X) * dy) - ((r.Y - start.Y) * dx))))
            .Where(p => p.Ahead > 0 && p.Side <= p.Ahead)
            .OrderBy(p => p.Ahead + (2 * p.Side))
            .ThenBy(p => p.Room.Id)
            .Select(p => (long?)p.Room.Id)
            .FirstOrDefault();
    }

    private static string Snippet(string text) => text.Length <= 40 ? text : text[..39] + "…";
}
