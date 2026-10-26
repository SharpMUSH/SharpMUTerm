using System.Globalization;
using System.Text;
using SharpMUTerm.Core.Mapping;

namespace SharpMUTerm.Tui;

/// <summary>
/// Draws one level of one area of a map as rows of markup, centred on where the character stands.
/// <para>
/// <b>A room is three cells, <c>[ ]</c>, and the grid is four columns by two rows</b>, which leaves one cell
/// between rooms for an east–west line and one row for the north–south and diagonal ones. The middle cell says
/// what the room is: <c>@</c> where you are, the room's own symbol when it has one, an arrow when it has a way
/// up or down, otherwise blank. A room on the walk ahead wears the notice ink; a locked one the draft ink.
/// </para>
/// <para>
/// <b>Each room is a link</b> (<see cref="LinkPrefix"/> and its number), which the map window's own handler
/// answers by walking there. The payload is never a <c>mux:send:</c>: what a click on a map does is the
/// client's decision, and nothing on the map is text a server chose — room names are drawn only in the title.
/// </para>
/// <para>
/// An exit leading to a room off this level or area is drawn as its first segment only; an unexplored exit
/// (a stub) is drawn dim. Nothing is drawn past the box it was given, so a map can never wrap.
/// </para>
/// </summary>
internal static class MapRenderer
{
    /// <summary>The payload prefix a room's link carries.</summary>
    internal const string LinkPrefix = "mux:map:";

    internal const int CellWidth = 4;

    internal const int CellHeight = 2;

    /// <summary>The inks a map is drawn in. Hexes, already held to the legibility floor by the caller.</summary>
    internal readonly record struct Ink(string Title, string Current, string Path, string Locked);

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

    private readonly record struct Cell(char Glyph, Kind Kind, long? Room);

    /// <summary>The room number a map link names, or null for anything else.</summary>
    internal static long? ParseLink(string url) =>
        url.StartsWith(LinkPrefix, StringComparison.Ordinal)
        && long.TryParse(url.AsSpan(LinkPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;

    /// <summary>
    /// The map as <paramref name="height"/> rows at most, each at most <paramref name="width"/> cells. The first
    /// row is the title; the rest is the grid around <paramref name="current"/>. With no current room the map is
    /// drawn around the lowest-numbered room, and an empty map says so.
    /// </summary>
    internal static List<string> Render(
        WorldMap map,
        long? current,
        IEnumerable<long> ahead,
        int width,
        int height,
        Ink ink,
        string? walking = null)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var lines = new List<string>();

        var centre = current is { } c ? map.Room(c) : null;
        centre ??= map.Rooms.Values.OrderBy(r => r.Id).FirstOrDefault();
        if (centre is null)
        {
            lines.Add(Title("Map · nothing mapped yet", width, ink));
            lines.Add(Fit("[dim]Walk somewhere: rooms appear as the game reports them.[/]", width));
            return lines.Take(height).ToList();
        }

        var area = centre.AreaId is { } a && map.Areas.TryGetValue(a, out var found) ? found.Name : null;
        var title = new StringBuilder("Map");
        if (!string.IsNullOrWhiteSpace(area))
        {
            title.Append(" · ").Append(area);
        }

        title.Append(" · z ").Append(centre.Z.ToString(CultureInfo.InvariantCulture));
        if (walking is not null)
        {
            title.Append(" · ").Append(walking);
        }

        lines.Add(Title(title.ToString(), width, ink));
        if (height == 1)
        {
            return lines;
        }

        var rows = height - 1;
        var grid = new Cell[rows, width];
        var ahead_ = ahead.ToHashSet();

        // Rooms are placed so the centre room's middle cell lands in the middle of the box.
        var originCol = width / 2 - 1;
        var originRow = rows / 2;
        (int Col, int Row) At(MapRoom room) =>
            (originCol + (room.X - centre.X) * CellWidth, originRow + (room.Y - centre.Y) * CellHeight);

        var level = map.Rooms.Values
            .Where(r => r.Z == centre.Z && r.AreaId == centre.AreaId)
            .ToList();

        void Put(int col, int row, char glyph, Kind kind, long? room = null)
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

            grid[row, col] = new Cell(glyph, kind, room);
        }

        foreach (var room in level)
        {
            var (col, row) = At(room);
            foreach (var (_, key, _) in room.AllExits())
            {
                Connect(col, row, key, Kind.Line);
            }

            foreach (var stub in room.Stubs)
            {
                if (MapDirections.Normalise(stub) is { } direction)
                {
                    Connect(col, row, direction, Kind.Stub);
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
            Put(col, row, '[', kind, room.Id);
            Put(col + 1, row, Middle(room, current), kind, room.Id);
            Put(col + 2, row, ']', kind, room.Id);
        }

        for (var row = 0; row < rows; row++)
        {
            lines.Add(Row(grid, row, width, ink));
        }

        return lines;

        void Connect(int col, int row, string direction, Kind kind)
        {
            switch (direction)
            {
                case "east":
                    Put(col + 3, row, '─', kind);
                    break;
                case "west":
                    Put(col - 1, row, '─', kind);
                    break;
                case "south":
                    Put(col + 1, row + 1, '│', kind);
                    break;
                case "north":
                    Put(col + 1, row - 1, '│', kind);
                    break;
                case "southeast":
                    Put(col + 3, row + 1, '╲', kind);
                    break;
                case "northwest":
                    Put(col - 1, row - 1, '╲', kind);
                    break;
                case "southwest":
                    Put(col - 1, row + 1, '╱', kind);
                    break;
                case "northeast":
                    Put(col + 3, row - 1, '╱', kind);
                    break;
            }
        }
    }

    private static char Middle(MapRoom room, long? current)
    {
        if (room.Id == current)
        {
            return '@';
        }

        if (!string.IsNullOrEmpty(room.Symbol) && !char.IsControl(room.Symbol[0]) && !char.IsSurrogate(room.Symbol[0]))
        {
            return room.Symbol[0];
        }

        var up = room.Exits.ContainsKey("up");
        var down = room.Exits.ContainsKey("down");
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
            while (end <= last && grid[row, end].Kind == cell.Kind && grid[row, end].Room == cell.Room)
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
                Kind.Blank or Kind.Line or Kind.Room => escaped,
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
