using System.Globalization;
using System.Text;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Mapping;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// Draws one level of one area of a map as rows of markup, centred on where the character stands.
/// <para>
/// <b>A room is three cells, <c>[ ]</c>, and the grid is four columns by two rows</b>, which leaves one cell
/// between rooms for an east–west line and one row for the north–south and diagonal ones. The middle cell says
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
    internal readonly record struct Ink(string Title, string Current, string Path, string Locked, Rgb? Plane = null);

    private enum Kind
    {
        Blank,
        Line,
        Stub,
        Room,
        Current,
        Path,
        Locked,
    }

    private readonly record struct Cell(char Glyph, Kind Kind, long? Room, string? Colour = null);

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
    internal static List<string> Render(MapViewModel view, WorldMap map, int width, int height, Ink ink)
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
        var title = new StringBuilder("Map");
        if (!string.IsNullOrWhiteSpace(view.Area))
        {
            title.Append(" · ").Append(view.Area);
        }

        title.Append(" · z ").Append(view.Level.ToString(CultureInfo.InvariantCulture));
        if (view.Walking is { } walking)
        {
            title.Append(" · ").Append(walking);
        }

        lines.Add(Title(title.ToString(), width, ink));
        if (height == 1)
        {
            return lines;
        }

        var legend = height >= LegendMinHeight;
        var rows = height - 1 - (legend ? 1 : 0);
        var grid = new Cell[rows, width];
        var ahead_ = ahead.ToHashSet();
        var byId = view.Rooms.ToDictionary(r => r.Id);
        var roomInk = AreaInk(view.AreaColour, ink);

        // Rooms are placed so the centre room's middle cell lands in the middle of the box.
        var originCol = width / 2 - 1;
        var originRow = rows / 2;
        (int Col, int Row) At(MapRoom room) =>
            (originCol + (room.X - centre.X) * CellWidth, originRow + (room.Y - centre.Y) * CellHeight);

        var level = map.Rooms.Values
            .Where(r => r.Z == centre.Z && r.AreaId == centre.AreaId)
            .ToList();

        void Put(int col, int row, char glyph, Kind kind, long? room = null, string? colour = null)
        {
            if (row < 0 || row >= rows || col < 0 || col >= width)
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
            foreach (var (_, key, to) in room.AllExits())
            {
                // A way into another area is drawn in that area's colour, which is the view's.
                var into = map.Room(to) is { } next && next.AreaId != room.AreaId && next.AreaId is { } areaId
                    && map.Areas.TryGetValue(areaId, out var area)
                        ? AreaInk(view.ColourOf(area.Name), ink)
                        : null;
                Connect(col, row, key, Kind.Line, into);
            }

            foreach (var stub in room.Stubs)
            {
                if (MapDirections.Normalise(stub) is { } direction)
                {
                    Connect(col, row, direction, Kind.Stub, null);
                }
            }
        }

        foreach (var room in level)
        {
            var (col, row) = At(room);
            var kind = room.Id == current ? Kind.Current
                : room.Locked ? Kind.Locked
                : ahead_.Contains(room.Id) ? Kind.Path
                : Kind.Room;
            var colour = kind == Kind.Room ? roomInk : null;
            Put(col, row, '[', kind, room.Id, colour);
            Put(col + 1, row, Middle(room, byId.GetValueOrDefault(room.Id), current), kind, room.Id, colour);
            Put(col + 2, row, ']', kind, room.Id, colour);
        }

        for (var row = 0; row < rows; row++)
        {
            lines.Add(Row(grid, row, width, ink));
        }

        if (legend)
        {
            lines.Add(Legend(view, width, ink));
        }

        return lines;

        void Connect(int col, int row, string direction, Kind kind, string? colour)
        {
            switch (direction)
            {
                case "east":
                    Put(col + 3, row, '─', kind, colour: colour);
                    break;
                case "west":
                    Put(col - 1, row, '─', kind, colour: colour);
                    break;
                case "south":
                    Put(col + 1, row + 1, '│', kind, colour: colour);
                    break;
                case "north":
                    Put(col + 1, row - 1, '│', kind, colour: colour);
                    break;
                case "southeast":
                    Put(col + 3, row + 1, '╲', kind, colour: colour);
                    break;
                case "northwest":
                    Put(col - 1, row - 1, '╲', kind, colour: colour);
                    break;
                case "southwest":
                    Put(col - 1, row + 1, '╱', kind, colour: colour);
                    break;
                case "northeast":
                    Put(col + 3, row - 1, '╱', kind, colour: colour);
                    break;
            }
        }
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

        List<(string Markup, int Length)> Areas(int cap) => view.Areas
            .Select(a =>
            {
                var name = a.Name.Length <= cap ? a.Name : a.Name[..(cap - 1)] + "…";
                return (Markup: $"[{AreaInk(a.Colour, ink)}]■[/] [dim]{MarkupText.Escape(name)}[/]", Length: 2 + name.Length);
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

    /// <summary>The lengths an area name is shortened through, longest first, before an area is counted instead.</summary>
    private static readonly int[] LegendNameCaps = [24, 14, 8];


    private static char Middle(MapRoom room, MapViewRoom? seen, long? current)
    {
        if (room.Id == current)
        {
            return '@';
        }

        if (!string.IsNullOrEmpty(room.Symbol) && !char.IsControl(room.Symbol[0]) && !char.IsSurrogate(room.Symbol[0]))
        {
            return room.Symbol[0];
        }

        // The stairs are the view's, so the window's marks and these arrows are one answer.
        var up = seen?.Up ?? false;
        var down = seen?.Down ?? false;
        return up && down ? '↕' : up ? '↑' : down ? '↓' : ' ';
    }

    private static string Title(string text, int width, Ink ink)
    {
        var plain = text.Length > width ? text[..Math.Max(0, width - 1)] + "…" : text;
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
                Kind.Current => $"[bold {ink.Current}]{escaped}[/]",
                Kind.Path => $"[{ink.Path}]{escaped}[/]",
                Kind.Locked => $"[{ink.Locked}]{escaped}[/]",
                _ => escaped,
            };

            markup.Append(cell.Room is { } id ? $"[link={LinkPrefix}{id.ToString(CultureInfo.InvariantCulture)}]{styled}[/]" : styled);
            col = end;
        }

        return markup.ToString();
    }
}
