namespace SharpMUTerm.Core.Mapping;

/// <summary>
/// The twelve directions a map knows, by their long names (<c>north</c>, <c>up</c>, <c>in</c>), with the
/// short command each is walked by, its reverse, and the grid step it takes. Every exit key a
/// <see cref="MapRoom"/> holds is one of <see cref="All"/>; anything else is a special exit.
/// </summary>
public static class MapDirections
{
    /// <summary>The directions in the order a room's exits are listed.</summary>
    public static readonly IReadOnlyList<string> All =
        ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest", "up", "down", "in", "out"];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["n"] = "north", ["ne"] = "northeast", ["e"] = "east", ["se"] = "southeast",
        ["s"] = "south", ["sw"] = "southwest", ["w"] = "west", ["nw"] = "northwest",
        ["u"] = "up", ["d"] = "down",
    };

    private static readonly Dictionary<string, string> Short = new(StringComparer.Ordinal)
    {
        ["north"] = "n", ["northeast"] = "ne", ["east"] = "e", ["southeast"] = "se",
        ["south"] = "s", ["southwest"] = "sw", ["west"] = "w", ["northwest"] = "nw",
        ["up"] = "u", ["down"] = "d", ["in"] = "in", ["out"] = "out",
    };

    private static readonly Dictionary<string, string> Reverses = new(StringComparer.Ordinal)
    {
        ["north"] = "south", ["northeast"] = "southwest", ["east"] = "west", ["southeast"] = "northwest",
        ["south"] = "north", ["southwest"] = "northeast", ["west"] = "east", ["northwest"] = "southeast",
        ["up"] = "down", ["down"] = "up", ["in"] = "out", ["out"] = "in",
    };

    /// <summary>
    /// The long name for <paramref name="direction"/> (<c>n</c>, <c>North</c> and <c>north</c> all give
    /// <c>north</c>), or null when it is not a direction.
    /// </summary>
    public static string? Normalise(string? direction)
    {
        var trimmed = direction?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (Aliases.TryGetValue(trimmed, out var full))
        {
            return full;
        }

        var lower = trimmed.ToLowerInvariant();
        return Short.ContainsKey(lower) ? lower : null;
    }

    /// <summary>The command that walks <paramref name="direction"/>: <c>n</c> for north.</summary>
    public static string Command(string direction) => Short.GetValueOrDefault(direction, direction);

    /// <summary>The opposite direction, or null for one that is not a direction.</summary>
    public static string? Reverse(string direction) => Reverses.GetValueOrDefault(direction);

    /// <summary>
    /// The grid step a direction takes, with north as −y (rows grow downwards on screen) and up as +z.
    /// <c>in</c> and <c>out</c> take none: they lead somewhere, but not anywhere a grid can say.
    /// </summary>
    public static (int Dx, int Dy, int Dz) Offset(string direction) => direction switch
    {
        "north" => (0, -1, 0),
        "northeast" => (1, -1, 0),
        "east" => (1, 0, 0),
        "southeast" => (1, 1, 0),
        "south" => (0, 1, 0),
        "southwest" => (-1, 1, 0),
        "west" => (-1, 0, 0),
        "northwest" => (-1, -1, 0),
        "up" => (0, 0, 1),
        "down" => (0, 0, -1),
        _ => (0, 0, 0),
    };
}
