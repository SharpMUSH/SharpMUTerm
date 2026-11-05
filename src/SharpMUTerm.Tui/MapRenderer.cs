using System.Globalization;
using System.Text;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Mapping;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// Draws one level of one area of a map as rows of markup, centred on where the character stands.
/// <para>
/// <b>A room is three cells, <c>[ ]</c>, and the grid is four columns by two rows</b> (six columns where the level fits,
/// <see cref="WideCellWidth"/>), which leaves a gap between rooms for an east–west line and one row for the north–south and diagonal ones. The middle cell says
/// what the room is: <c>@</c> where you are, the room's own symbol when it has one, an arrow when it has a way
/// up or down (the view's <see cref="MapViewRoom.Up"/>/<see cref="MapViewRoom.Down"/>), otherwise blank. A room is
/// drawn in its area's colour (<see cref="MapViewModel.AreaColour"/>); a room on the walk ahead wears the notice ink,
/// a locked one the draft ink, and an exit into another area is drawn in <em>that</em> area's colour. Every area
/// colour is the view's, held to the legibility floor on the pane (<see cref="Ink.Plane"/>), which leaves the
/// palette's hexes as they are.
/// </para>
/// <para>
/// <b>Each room is a link</b> (<see cref="LinkPrefix"/> and its number), which the map window's own handler
/// answers by walking there. The payload is never a <c>mux:send:</c>: what a click on a map does is the
/// client's decision, and nothing on the map is text a server chose — room and area names are drawn only in the
/// title and the legend.
/// </para>
/// <para>
/// An exit leading to a room off this level or area is drawn as its first segment only; an unexplored exit
/// (a stub) is drawn dim. Nothing is drawn past the box it was given, so a map can never wrap. With room for it
/// (<see cref="LegendMinHeight"/> rows), the last row is a one-line legend: <c>@ you</c>, the stairs marks when any
/// room has one, and each area the view names in its colour.
/// </para>
/// </summary>
internal static class MapRenderer
{
    /// <summary>The payload prefix a room's link carries.</summary>
    internal const string LinkPrefix = "mux:map:";

    internal const int CellWidth = 4;

    internal const int CellHeight = 2;

    /// <summary>The fewest rows a map needs before it spends one on the legend.</summary>
    internal const int LegendMinHeight = 6;

    /// <summary>
    /// The inks a map is drawn in. Hexes, already held to the legibility floor by the caller; <paramref name="Plane"/>
    /// is the pane plane the area colours are held to, or null to draw them as the view gives them.
    /// </summary>
    internal readonly record struct Ink(
        string Title,
        string Current,
        string Path,
        string Locked,
        Rgb? Plane = null,
        string? Selected = null,
        Panels.HeadingBand.Ink? Band = null);

    /// <summary>
    /// The cell a map's words keep from the pane's edge (title, exits, hint, legend), the terminal mockup's 14 px inset,
    /// from <see cref="InsetFromWidth"/> cells; the grid is centred and needs none.
    /// </summary>
    internal const int InsetFromWidth = 24;

    /// <summary>
    /// The column step of a plain grid when the whole level fits at it: <c>[ ]───[ ]</c>, the terminal mockup's exits,
    /// three cells of line between rooms instead of one. A level that does not fit keeps <see cref="CellWidth"/>.
    /// </summary>
    internal const int WideCellWidth = 6;

    /// <summary>The narrowest map that lists names beside its grid: the current room's exits, or the room being edited.</summary>
    internal const int SideFromWidth = 64;

    /// <summary>The side column's width in a map <paramref name="width"/> wide, or 0 for none.</summary>
    internal static int SideWidth(int width) => width >= SideFromWidth ? Math.Clamp(width / 3, 22, 36) : 0;

    /// <summary>The shortest map that lists names under its grid when it has no side column.</summary>
    internal const int UnderFromHeight = 18;

    private enum Kind
    {
        Blank,
        Line,
        Stub,
        Label,
        Room,
        Current,
        Path,
        Locked,
        Selected,
    }

    private readonly record struct Cell(char Glyph, Kind Kind, long? Room, string? Colour = null);

    /// <summary>The payload prefix of a look link: the title's level arrows and way back, and the legend's areas.</summary>
    internal const string LookPrefix = LinkPrefix + "look:";

    /// <summary>
    /// The look a map link asks for, or null for anything else: <c>up</c>, <c>down</c>, <c>here</c>, or <c>area:N</c>, the
    /// Nth of <paramref name="view"/>'s <see cref="MapViewModel.Areas"/> (an index, never a name: an area's name is the
    /// game's text).
    /// </summary>
    internal static MapLook? ParseLook(string url, MapViewModel view)
    {
        if (!url.StartsWith(LookPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var what = url[LookPrefix.Length..];
        return what switch
        {
            "up" => MapLook.Up,
            "down" => MapLook.Down,
            "here" => MapLook.Here,
            _ when what.StartsWith("area:", StringComparison.Ordinal)
                && int.TryParse(what.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index < view.Areas.Count => MapLook.ToArea(view.Areas[index].Name),
            _ => null,
        };
    }

    /// <summary>The room number a map link names, or null for anything else.</summary>
    internal static long? ParseLink(string url) =>
        url.StartsWith(LinkPrefix, StringComparison.Ordinal)
        && long.TryParse(url.AsSpan(LinkPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    /// <summary>
    /// The map as <paramref name="height"/> rows at most, each at most <paramref name="width"/> cells, around
    /// <paramref name="current"/>; see the overload that takes a <see cref="MapViewModel"/>, which this builds.
    /// </summary>
    internal static List<string> Render(
        WorldMap map,
        long? current,
        IEnumerable<long> ahead,
        int width,
        int height,
        Ink ink,
        string? walking = null) =>
        Render(MapViewModel.From(map, current, ahead, walking), map, width, height, ink);

    /// <summary>
    /// The map as <paramref name="height"/> rows at most, each at most <paramref name="width"/> cells. The first
    /// row is the title; the rest is the grid, and the legend under it when there is room. <b>Where, which level, who
    /// is where, whether a walk is under way, which rooms have stairs and what colour each area is are the view's</b>
    /// (the model the window's native map is drawn from), so the two cannot centre on different rooms, title different
    /// areas or colour them differently. Only the exits' geometry (directions, stubs, ways off the level), which the
    /// view does not carry, is read from <paramref name="map"/>. An empty map says so.
    /// </summary>
    internal static List<string> Render(MapViewModel view, WorldMap map, int width, int height, Ink ink) =>
        Render(view, map, width, height, ink, keysHeld: true);

    /// <summary>
    /// The map as <see cref="Render(MapViewModel, WorldMap, int, int, Ink)"/> draws it, saying in edit mode whether the
    /// map holds the keyboard (<paramref name="keysHeld"/>) or has handed a <c>/map</c> command to the command line.
    /// </summary>
    internal static List<string> Render(MapViewModel view, WorldMap map, int width, int height, Ink ink, bool keysHeld)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var lines = new List<string>();

        var centre = view.AnchorId is { } anchor ? map.Room(anchor) : null;
        if (centre is null)
        {
            lines.Add(Title("Map · nothing mapped yet", width, ink));
            lines.Add(Fit("[dim]Walk somewhere: rooms appear as the game reports them.[/]", width));
            return lines.Take(height).ToList();
        }

        var current = view.Current?.Id;
        var ahead = view.Rooms.Where(r => r.Ahead).Select(r => r.Id);
        // The room you are in leads the title: the one name a reader wants from a map at a glance, and the one a grid
        // of [ ] cannot say. With no current room the map is a map of somewhere else, and says so.
        var title = new StringBuilder(view.Current is { } here ? here.Name : view.Viewing ? "Viewing" : "Map");
        if (!string.IsNullOrWhiteSpace(view.Area))
        {
            title.Append(" · ").Append(view.Area);
        }

        title.Append(" · z ").Append(view.Level.ToString(CultureInfo.InvariantCulture));
        if (view.Walking is { } walking)
        {
            title.Append(" · ").Append(walking);
        }

        // The ways to look elsewhere and back, after the title: links the map's own handler answers. Under a heading
        // band (a panel's, the terminal mockup's MAP band) the title is the band's bold ink and the controls sit at its
        // far end in the band's accent.
        var inset = width >= InsetFromWidth ? 1 : 0;
        if (ink.Band is { } band)
        {
            var (bandControls, bandWidth) = LookControls(view, ink with { Title = band.Accent, Current = band.Accent });
            var inner = Math.Max(1, width - 2);
            var keep = bandWidth > 0 && bandWidth + 8 <= width;
            var heading = Elide(title.ToString(), keep ? inner - bandWidth : inner);
            lines.Add(Panels.HeadingBand.Row(MarkupText.Escape(heading), string.Empty, keep ? bandControls.TrimStart() : string.Empty, width, band));
        }
        else
        {
            var (controls, controlsWidth) = LookControls(view, ink);
            var lead = new string(' ', inset);
            if (controlsWidth > 0 && controlsWidth + 8 + inset <= width)
            {
                lines.Add(lead + Title(title.ToString(), width - controlsWidth - inset, ink) + controls);
            }
            else
            {
                lines.Add(lead + Title(title.ToString(), width - inset, ink));
            }
        }
        if (height == 1)
        {
            return lines;
        }

        // In edit mode the room being edited goes beside the grid when the map is wide, else under it, and the keys that
        // edit it are one laddered row at the foot (edit mode spends the legend's row on them). Otherwise what each exit
        // of your room leads to is always under the grid, above the legend — one place, whatever the pane's shape.
        var side = view.Editing ? SideWidth(width) : 0;
        var gridWidth = side > 0 ? width - side - 2 : width;
        var legend = !view.Editing && height >= LegendMinHeight;
        var words = width - inset;
        var lead_ = new string(' ', inset);
        var under = side > 0 ? [] : UnderLines(view, words, height, ink, keysHeld).Select(l => l.Length == 0 ? l : lead_ + l).ToList();
        if (!view.Editing && height >= UnderFromHeight && view.Hint is { } hint && hint.Length <= words)
        {
            // The model's own hint, the terminal mockup's "click a room to walk", over the legend in a tall pane, and only
            // whole: an elided instruction says less than none.
            under.Add($"{lead_}[dim]{MarkupText.Escape(hint)}[/]");
        }

        var footer = side > 0 && height >= 4 ? KeysLine(width, ink, keysHeld) : null;
        var rows = Math.Max(0, height - 1 - (legend ? 1 : 0) - under.Count - (footer is null ? 0 : 1));
        var grid = new Cell[rows, gridWidth];
        var ahead_ = ahead.ToHashSet();
        var byId = view.Rooms.ToDictionary(r => r.Id);
        var roomInk = AreaInk(view.AreaColour, ink);

        // Rooms are placed so the centre room's middle cell lands in the middle of the box — unless the room being
        // edited would then fall outside it, when that room is the centre instead.
        var originCol = gridWidth / 2 - 1;
        var originRow = rows / 2;
        var level = map.Rooms.Values
            .Where(r => r.Z == centre.Z && r.AreaId == centre.AreaId)
            .ToList();
        var (label, cellWidth, labelledOrigin) = Labelling(level, centre, gridWidth, originCol);
        originCol = labelledOrigin;
        if (view.Selected is { } picked && map.Room(picked.Id) is { } chosen
            && (originCol + ((chosen.X - centre.X) * cellWidth) is var pickedCol && (pickedCol < 1 || pickedCol + 3 > gridWidth)
                || Math.Abs(chosen.Y - centre.Y) * CellHeight > originRow))
        {
            centre = chosen;
            originCol = gridWidth / 2 - 1;
        }

        (int Col, int Row) At(MapRoom room) =>
            (originCol + (room.X - centre.X) * cellWidth, originRow + (room.Y - centre.Y) * CellHeight);

        void Put(int col, int row, char glyph, Kind kind, long? room = null, string? colour = null)
        {
            if (row < 0 || row >= rows || col < 0 || col >= gridWidth)
            {
                return;
            }

            // A room's cells win over a line, a line over a stub.
            if (grid[row, col].Kind >= Kind.Room && kind < Kind.Room)
            {
                return;
            }

            if (grid[row, col].Kind == Kind.Line && kind == Kind.Stub)
            {
                return;
            }

            grid[row, col] = new Cell(glyph, kind, room, colour);
        }

        foreach (var room in level)
        {
            var (col, row) = At(room);
            var named = label > 0 ? Math.Min(NameOf(room).Length, label) : 0;
            foreach (var (_, key, to) in room.AllExits())
            {
                // A way into another area is drawn in that area's colour, which is the view's.
                var into = map.Room(to) is { } next && next.AreaId != room.AreaId && next.AreaId is { } areaId
                    && map.Areas.TryGetValue(areaId, out var area)
                        ? AreaInk(view.ColourOf(area.Name), ink)
                        : null;
                Connect(col, row, key, Kind.Line, into, named);
            }

            foreach (var stub in room.Stubs)
            {
                if (MapDirections.Normalise(stub) is { } direction)
                {
                    Connect(col, row, direction, Kind.Stub, null, named);
                }
            }
        }

        foreach (var room in level)
        {
            var (col, row) = At(room);
            var kind = room.Id == view.Selected?.Id ? Kind.Selected
                : room.Id == current ? Kind.Current
                : room.Locked ? Kind.Locked
                : ahead_.Contains(room.Id) ? Kind.Path
                : Kind.Room;
            var colour = kind == Kind.Room ? roomInk : null;
            Put(col, row, '[', kind, room.Id, colour);
            Put(col + 1, row, Middle(room, byId.GetValueOrDefault(room.Id), current), kind, room.Id, colour);
            Put(col + 2, row, ']', kind, room.Id, colour);
        }

        if (label > 0)
        {
            // The name after the room, one cell clear of it and elided to the room the grid leaves; a link to the room,
            // like the room itself. The room you are in is named in its own ink.
            foreach (var room in level)
            {
                var (col, row) = At(room);
                var name = NameOf(room);
                name = name.Length <= label ? name : name[..(label - 1)].TrimEnd() + "…";
                var labelColour = room.Id == current ? ink.Current : null;
                for (var i = 0; i < name.Length; i++)
                {
                    Put(col + 4 + i, row, name[i], Kind.Label, room.Id, labelColour);
                }
            }
        }

        var column = side > 0 ? SideLines(view, side, rows, ink) : [];
        for (var row = 0; row < rows; row++)
        {
            lines.Add(WithSide(Row(grid, row, gridWidth, ink), column, row, gridWidth));
        }

        lines.AddRange(under);
        if (footer is not null)
        {
            lines.Add(footer);
        }

        if (legend)
        {
            lines.Add(WithSide(lead_ + Legend(view, gridWidth - inset, ink), column, rows, gridWidth));
        }

        return lines;

        // A room's name as the grid draws it: the view's, else the map's, else its number.
        string NameOf(MapRoom room) =>
            byId.GetValueOrDefault(room.Id)?.Name is { Length: > 0 } seen ? seen
            : room.Name.Length > 0 ? room.Name
            : "#" + room.Id.ToString(CultureInfo.InvariantCulture);

        void Connect(int col, int row, string direction, Kind kind, string? colour, int named = 0)
        {
            // A diagonal sits in the middle of a plain grid's gap: one cell in from the room on a wide step.
            var mid = label == 0 ? (cellWidth - CellWidth) / 2 : 0;
            switch (direction)
            {
                case "east":
                    // With names on the grid the way east starts one cell past the name and runs to the next room; on a
                    // plain grid it fills the gap. An unexplored way is one cell, a stub and not a corridor.
                    for (var c = named > 0 ? col + 5 + named : col + 3; c <= col + cellWidth - 1; c++)
                    {
                        Put(c, row, '─', kind, colour: colour);
                        if (kind == Kind.Stub)
                        {
                            break;
                        }
                    }

                    break;
                case "west":
                    // A plain grid's way west fills the gap too (the room to the west may not list its way back); with
                    // names that gap holds the western room's name, so it is one cell.
                    for (var c = col - 1; c >= (label == 0 && kind != Kind.Stub ? col - (cellWidth - 3) : col - 1); c--)
                    {
                        Put(c, row, '─', kind, colour: colour);
                    }

                    break;
                case "south":
                    Put(col + 1, row + 1, '│', kind, colour: colour);
                    break;
                case "north":
                    Put(col + 1, row - 1, '│', kind, colour: colour);
                    break;
                case "southeast":
                    Put(col + 3 + mid, row + 1, '╲', kind, colour: colour);
                    break;
                case "northwest":
                    Put(col - 1 - mid, row - 1, '╲', kind, colour: colour);
                    break;
                case "southwest":
                    Put(col - 1 - mid, row + 1, '╱', kind, colour: colour);
                    break;
                case "northeast":
                    Put(col + 3 + mid, row - 1, '╱', kind, colour: colour);
                    break;
            }
        }
    }

    /// <summary>The narrowest grid that names its rooms.</summary>
    internal const int LabelFromWidth = 48;

    /// <summary>The longest and shortest room name drawn on the grid.</summary>
    internal const int LabelMax = 16, LabelMin = 6;

    /// <summary>
    /// How long a room's name on the grid may be, the grid's column step (<c>[ ] name──</c>, so a step is the name plus
    /// six), and the column the centre room is drawn at. The longest name (up to <see cref="LabelMax"/>) at which the
    /// whole level fits the grid, which is then centred as a block rather than on the centre room, so the names have
    /// the width there is; failing that, on a grid of 100 columns or more, eight, centred on the room as before (rooms
    /// past the edge are cut, as they always were); otherwise none, and the step is the plain <see cref="CellWidth"/>.
    /// </summary>
    internal static (int Label, int CellWidth, int OriginCol) Labelling(IReadOnlyList<MapRoom> level, MapRoom centre, int gridWidth, int originCol)
    {
        if (level.Count == 0)
        {
            return (0, CellWidth, originCol);
        }

        var west = Math.Max(0, centre.X - level.Min(r => r.X));
        var east = Math.Max(0, level.Max(r => r.X) - centre.X);

        // A plain grid takes the mockup's wide step when every room still lands inside the box (centred on the room as
        // before; a stub past the last room may be cut, as it always could be).
        (int, int, int) Plain() =>
            originCol - (west * WideCellWidth) >= 1 && originCol + (east * WideCellWidth) + 3 <= gridWidth
                ? (0, WideCellWidth, originCol)
                : (0, CellWidth, originCol);

        if (gridWidth < LabelFromWidth)
        {
            return Plain();
        }
        for (var label = LabelMax; label >= LabelMin; label--)
        {
            var step = label + 6;
            var span = ((west + east) * step) + 4 + label;
            if (span <= gridWidth)
            {
                return (label, step, ((gridWidth - span) / 2) + (west * step));
            }
        }

        return gridWidth >= 100 ? (8, 14, originCol) : Plain();
    }

    /// <summary>
    /// An area colour from the view, held to the floor on the pane. The palette clears it on every built-in theme,
    /// so this returns the view's hex unchanged there; a custom theme may move it.
    /// </summary>
    internal static string? AreaInk(string? colour, Ink ink)
    {
        if (colour is null || ink.Plane is not { } plane || !TryParse(colour, out var rgb))
        {
            return colour;
        }

        var legible = Contrast.Legible(rgb, plane);
        return legible == rgb ? colour : $"#{legible.R:x2}{legible.G:x2}{legible.B:x2}";
    }

    private static bool TryParse(string hex, out Rgb rgb)
    {
        rgb = default;
        if (hex.Length != 7 || hex[0] != '#'
            || !byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        rgb = new Rgb(r, g, b);
        return true;
    }

    /// <summary>
    /// The one-line legend: where you are, the stairs marks when a room has one, a walk under way, then each area in
    /// its colour. The areas are what a narrow pane keeps longest: the words beside the marks go first (<c>@ you</c>
    /// becomes <c>@</c>), then area names are shortened, and only then are areas counted (<c>+2</c>) instead of named.
    /// It never wraps.
    /// </summary>
    private static string Legend(MapViewModel view, int width, Ink ink)
    {
        var stairs = view.Rooms.Any(r => r.Up || r.Down);
        var walk = view.Rooms.Any(r => r.Ahead);
        List<(string Markup, int Length)> Marks(bool words)
        {
            var marks = new List<(string, int)> { ($"[bold {ink.Current}]@[/]" + (words ? " [dim]you[/]" : string.Empty), words ? 5 : 1) };
            if (stairs)
            {
                marks.Add(("[dim]↑↓" + (words ? " stairs" : string.Empty) + "[/]", words ? 9 : 2));
            }

            if (walk)
            {
                marks.Add(($"[{ink.Path}][[ ]][/]" + (words ? " [dim]walk[/]" : string.Empty), words ? 8 : 3));
            }

            return marks;
        }

        // Another area's entry is a link: a click shows that area (its index, never its name, is the payload).
        List<(string Markup, int Length)> Areas(int cap) => view.Areas
            .Select((a, i) =>
            {
                var name = a.Name.Length <= cap ? a.Name : a.Name[..(cap - 1)] + "…";
                var markup = $"[{AreaInk(a.Colour, ink)}]■[/] [dim]{MarkupText.Escape(name)}[/]";
                return (Markup: a.Drawn ? markup : $"[link={LookPrefix}area:{i.ToString(CultureInfo.InvariantCulture)}]{markup}[/]", Length: 2 + name.Length);
            })
            .ToList();

        for (var keep = view.Areas.Count; keep >= 0; keep--)
        {
            foreach (var words in new[] { true, false })
            {
                foreach (var cap in LegendNameCaps)
                {
                    var items = Marks(words).Concat(Areas(cap).Take(keep)).ToList();
                    if (keep < view.Areas.Count)
                    {
                        var more = $"+{view.Areas.Count - keep}";
                        items.Add(($"[dim]{more}[/]", more.Length));
                    }

                    if (items.Sum(i => i.Length) + (3 * (items.Count - 1)) <= width)
                    {
                        return string.Join(" [dim]·[/] ", items.Select(i => i.Markup));
                    }
                }
            }
        }

        return Fit(string.Join(" [dim]·[/] ", Marks(words: false).Select(p => p.Markup)), width);
    }

    /// <summary>
    /// The title's look links and their width: <c>▲ ▼</c> while the drawn area has more than one level (an arrow with no
    /// level that way is dim and not a link), and <c>⌂ here</c> while the view is somewhere the player asked to look.
    /// Empty when there is nothing to offer, so a one-level map's title is as it always was.
    /// </summary>
    internal static (string Markup, int Width) LookControls(MapViewModel view, Ink ink)
    {
        var parts = new List<(string Markup, int Width)>();
        if (view.Levels.Count > 1)
        {
            parts.Add((Arrow("▲", "up", view.LevelAbove), 1));
            parts.Add((Arrow("▼", "down", view.LevelBelow), 1));
        }

        if (view.Viewing)
        {
            parts.Add(($"[link={LookPrefix}here][bold {ink.Current}]⌂ here[/][/]", 6));
        }

        return parts.Count == 0
            ? (string.Empty, 0)
            : ("  " + string.Join(" ", parts.Select(p => p.Markup)), 2 + parts.Sum(p => p.Width) + parts.Count - 1);

        string Arrow(string glyph, string way, bool open) =>
            open ? $"[link={LookPrefix}{way}][bold {ink.Title}]{glyph}[/][/]" : $"[dim]{glyph}[/]";
    }

    /// <summary>The lengths an area name is shortened through, longest first, before an area is counted instead.</summary>
    private static readonly int[] LegendNameCaps = [24, 14, 8];


    private static char Middle(MapRoom room, MapViewRoom? seen, long? current)
    {
        if (room.Id == current)
        {
            return '@';
        }

        if (seen?.Symbol is { Length: > 0 } symbol)
        {
            return symbol[0];
        }

        // The stairs are the view's, so the window's marks and these arrows are one answer.
        var up = seen?.Up ?? false;
        var down = seen?.Down ?? false;
        return up && down ? '↕' : up ? '↑' : down ? '↓' : ' ';
    }

    /// <summary>A grid row with the side column's line for it, the column starting two cells past the grid.</summary>
    private static string WithSide(string row, IReadOnlyList<string> column, int index, int gridWidth)
    {
        if (index >= column.Count || column[index].Length == 0)
        {
            return row;
        }

        var pad = Math.Max(0, gridWidth + 2 - MarkupText.VisibleLength(row));
        return row + new string(' ', pad) + column[index];
    }

    /// <summary>
    /// The side column, beside a wide grid: the room being edited and the keys that edit it, or else where you are and
    /// what each exit leads to. Every line is cut to <paramref name="width"/> and there are at most <paramref name="rows"/>.
    /// </summary>
    private static List<string> SideLines(MapViewModel view, int width, int rows, Ink ink)
    {
        var lines = new List<string>();
        if (view.Selected is { } room)
        {
            lines.AddRange(DetailLines(view, width, ink));
            foreach (var exit in room.Exits.Take(Math.Max(0, rows - lines.Count)))
            {
                lines.Add(ExitLine(exit, width));
            }
        }

        return lines.Take(rows).ToList();
    }

    /// <summary>
    /// What goes under a grid with no side column. In edit mode, the room being edited and the keys — a few lines each
    /// in a tall pane, one each in a short one. Otherwise, in a tall pane, what each exit of your room leads to.
    /// </summary>
    private static List<string> UnderLines(MapViewModel view, int width, int height, Ink ink, bool keysHeld)
    {
        if (view.Editing)
        {
            return height >= UnderFromHeight
                ? [string.Empty, .. DetailLines(view, width, ink), KeysLine(width, ink, keysHeld)]
                : height >= 4 ? [RoomLine(view, width, ink), KeysLine(width, ink, keysHeld)] : [];
        }

        if (height < UnderFromHeight || view.Nearby.Count == 0 || view.Viewing)
        {
            return [];
        }

        return [string.Empty, .. view.Nearby.Take(Math.Max(1, height / 4)).Select(e => ExitLine(e, width))];
    }

    /// <summary>The room being edited, a fact a line: its number and name, area, place and weight, then its note and a waiting link.</summary>
    private static List<string> DetailLines(MapViewModel view, int width, Ink ink)
    {
        var room = view.Selected!;
        string Name(string text, int room) => MarkupText.Escape(Elide(text, room));
        var lines = new List<string>
        {
            Fit($"[bold {ink.Title}]{Name($"#{room.Id.ToString(CultureInfo.InvariantCulture)} {room.Name}", width)}[/]", width),
            Fit($"[dim]area  [/]{Name(room.Area ?? "—", width - 6)}", width),
            Fit(
                $"[dim]at    [/]{room.X.ToString(CultureInfo.InvariantCulture)}, {room.Y.ToString(CultureInfo.InvariantCulture)} [dim]level[/] {room.Z.ToString(CultureInfo.InvariantCulture)}",
                width),
            Fit(
                $"[dim]weight[/] {room.Weight.ToString(CultureInfo.InvariantCulture)}"
                + (room.Locked ? $"  [{ink.Locked}]locked[/]" : string.Empty)
                + (room.Symbol is { } symbol ? $"  [dim]mark[/] {MarkupText.Escape(symbol)}" : string.Empty),
                width),
        };
        if (room.Note is { } note)
        {
            lines.Add(Fit($"[dim]note  [/]{Name(note, width - 6)}", width));
        }

        if (room.HandEdited.Count > 0)
        {
            // What the server's next report will leave alone, and the key that gives it back.
            lines.Add(Fit($"[dim]mine  [/]{MarkupText.Escape(string.Join(", ", room.HandEdited))} [dim]· R resets[/]", width));
        }

        if (view.Pending is { } pending)
        {
            lines.Add(Fit($"[{ink.Path}]{Name(pending, width)}[/]", width));
        }

        return lines;
    }

    private static string ExitLine(MapViewExitName exit, int width)
    {
        var where = exit.To is null
            ? "[dim]not walked[/]"
            : MarkupText.Escape(Elide(exit.Name ?? "#" + exit.To.Value.ToString(CultureInfo.InvariantCulture), width - 6));
        return Fit($"[dim]{MarkupText.Escape(Elide(exit.Exit, 5).PadRight(5))}[/] {where}", width);
    }

    /// <summary>Edit mode's room line, under a short grid with no side column: the room being edited, in a line.</summary>
    private static string RoomLine(MapViewModel view, int width, Ink ink)
    {
        var room = view.Selected!;
        var parts = new List<string> { $"#{room.Id.ToString(CultureInfo.InvariantCulture)} {room.Name}" };
        if (room.Area is { } area)
        {
            parts.Add(area);
        }

        parts.Add($"{room.X.ToString(CultureInfo.InvariantCulture)},{room.Y.ToString(CultureInfo.InvariantCulture)},{room.Z.ToString(CultureInfo.InvariantCulture)}");
        if (room.Weight != 1)
        {
            parts.Add("weight " + room.Weight.ToString(CultureInfo.InvariantCulture));
        }

        if (room.Locked)
        {
            parts.Add("locked");
        }

        if (room.Symbol is { } symbol)
        {
            parts.Add("mark " + symbol);
        }

        if (room.Note is { } note)
        {
            parts.Add("note: " + note);
        }

        if (room.HandEdited.Count > 0)
        {
            parts.Add("mine: " + string.Join(", ", room.HandEdited));
        }

        if (view.Pending is { } pending)
        {
            parts.Add(pending);
        }

        return $"[bold {ink.Title}]{MarkupText.Escape(Elide(string.Join(" · ", parts), width))}[/]";
    }

    /// <summary>
    /// Edit mode's keys as one row (<see cref="KeyLadder"/>): in <see cref="MapEditing.Keys"/>' order, the least used given
    /// up first (<see cref="KeyDrop"/>), <c>? all keys</c> and <c>Esc done</c> never — <c>?</c> opens the help page that
    /// lists every one. One row and not a block: at 80×24 the block took half the pane and still cut keys. While a
    /// <c>/map</c> command waits on the command line, how to finish it instead.
    /// </summary>
    internal static string KeysLine(int width, Ink ink, bool keysHeld)
    {
        if (!keysHeld)
        {
            return Fit("[dim]finish the /map command on the command line · ⏎ applies it · ⌃P ▸ Edit map takes the keys back[/]", width);
        }

        var keys = MapEditing.Keys.Select(k => new LadderKey(k.Key, k.Word, Drop: KeyDrop(k.Key))).ToList();
        return KeyLadder.Fit(
            keys,
            width,
            k => $"[bold {ink.Current}]{MarkupText.Escape(k)}[/]",
            w => $"[dim]{MarkupText.Escape(w)}[/]",
            " [dim]·[/] ");
    }

    /// <summary>Which edit key a short footer gives up first: lowest first; the ones a newcomer reaches for last.</summary>
    internal static int KeyDrop(string key) => key switch
    {
        "?" or "Esc" => LadderKey.Keep,
        "←↑→↓" => 17,
        "⏎" => 16,
        "r" => 15,
        "u" => 14,
        "⇧←↑→↓" => 13,
        "Del" => 12,
        "x" => 11,
        "n" => 10,
        "l" => 9,
        "PgUp PgDn" => 8,
        "v" => 7,
        "h" => 6,
        "a" => 5,
        "w" => 4,
        "-" => 3,
        "s" => 2,
        _ => 1,
    };

    private static string Elide(string text, int width) => MarkupText.ElidePlain(text, width);

    private static string Title(string text, int width, Ink ink)
    {
        var plain = MarkupText.ElidePlain(text, width);
        return $"[bold {ink.Title}]{MarkupText.Escape(plain)}[/]";
    }

    private static string Fit(string markup, int width) =>
        MarkupText.VisibleLength(markup) <= width ? markup : MarkupText.Escape(MarkupText.Plain(markup)[..width]);

    private static string Row(Cell[,] grid, int row, int width, Ink ink)
    {
        // Trailing blanks are dropped: a row is no wider than what is drawn on it.
        var last = width - 1;
        while (last >= 0 && grid[row, last].Kind == Kind.Blank)
        {
            last--;
        }

        var markup = new StringBuilder();
        var col = 0;
        while (col <= last)
        {
            var cell = grid[row, col];
            var end = col + 1;
            while (end <= last && grid[row, end].Kind == cell.Kind && grid[row, end].Room == cell.Room && grid[row, end].Colour == cell.Colour)
            {
                end++;
            }

            var text = new StringBuilder();
            for (var i = col; i < end; i++)
            {
                text.Append(grid[row, i].Kind == Kind.Blank ? ' ' : grid[row, i].Glyph);
            }

            var escaped = MarkupText.Escape(text.ToString());
            var styled = cell.Kind switch
            {
                Kind.Blank => escaped,
                Kind.Line or Kind.Room => cell.Colour is { } colour ? $"[{colour}]{escaped}[/]" : escaped,
                Kind.Stub => $"[dim]{escaped}[/]",
                Kind.Label => cell.Colour is { } named ? $"[{named}]{escaped}[/]" : escaped,
                Kind.Current => $"[bold {ink.Current}]{escaped}[/]",
                Kind.Path => $"[{ink.Path}]{escaped}[/]",
                Kind.Locked => $"[{ink.Locked}]{escaped}[/]",
                Kind.Selected => $"[{ink.Selected ?? "bold " + ink.Current}]{escaped}[/]",
                _ => escaped,
            };

            markup.Append(cell.Room is { } id ? $"[link={LinkPrefix}{id.ToString(CultureInfo.InvariantCulture)}]{styled}[/]" : styled);
            col = end;
        }

        return markup.ToString();
    }
}
