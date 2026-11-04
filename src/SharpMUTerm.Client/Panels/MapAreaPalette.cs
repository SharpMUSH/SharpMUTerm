namespace SharpMUTerm.Client.Panels;

/// <summary>
/// The colours a map gives its areas: one per area <em>name</em>, the same every run and in both hosts, from a closed
/// set of eight hues.
/// <para>
/// <b>Two sets of hexes, one set of hues.</b> A colour is read against the pane it lands on, and the pane planes of a
/// dark theme and a light one leave no colour that clears the legibility floor (3:1) on both: the darkest plane a
/// Light pane can wear wants a foreground no brighter than about 0.13 in relative luminance, the brightest a Dark or
/// Solarized Dark pane can wear wants one no darker than about 0.18. So each hue has a dark-theme hex and a
/// light-theme hex, and the client picks the set by its theme (<see cref="For"/>). The index an area gets is the same
/// in both, so an area keeps its hue when the theme changes. <c>MapAreaPaletteTests</c> holds every hex to the floor
/// against every pane plane of the themes it serves.
/// </para>
/// <para>
/// The index is a hash of the name (FNV-1a over its characters, ordinal), never <see cref="string.GetHashCode()"/>,
/// which changes from run to run. Two areas can share a hue; the legend names them, and an area's name is the second
/// cue wherever its colour is the first.
/// </para>
/// </summary>
public sealed class MapAreaPalette
{
    private readonly string[] _colours;

    private MapAreaPalette(string name, string[] colours)
    {
        Name = name;
        _colours = colours;
    }

    /// <summary>Amber, jade, azure, orchid, coral, moss, cyan, lavender — for a dark theme's panes.</summary>
    public static MapAreaPalette Dark { get; } = new(
        "dark",
        ["#e0a458", "#5ec79f", "#7fa8ff", "#e08fd6", "#f08a7e", "#b5c46a", "#5cc3dc", "#b9a2ff"]);

    /// <summary>The same hues, deep enough for a light theme's panes.</summary>
    public static MapAreaPalette Light { get; } = new(
        "light",
        ["#7a4600", "#0f6a4a", "#2a52b0", "#86307c", "#a02c22", "#4c5a06", "#0b5a6e", "#5b3aa8"]);

    /// <summary>Which set this is (<c>dark</c>, <c>light</c>).</summary>
    public string Name { get; }

    /// <summary>The colours, by index.</summary>
    public IReadOnlyList<string> Colours => _colours;

    /// <summary>The set for a theme whose panes are dark (<paramref name="darkPanes"/>) or light.</summary>
    public static MapAreaPalette For(bool darkPanes) => darkPanes ? Dark : Light;

    /// <summary>The index <paramref name="area"/> gets in every set.</summary>
    public static int IndexOf(string area)
    {
        ArgumentNullException.ThrowIfNull(area);
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in area)
            {
                hash = (hash ^ c) * 16777619u;
            }

            return (int)(hash % 8u);
        }
    }

    /// <summary>The colour of the area called <paramref name="area"/>, as <c>#rrggbb</c>.</summary>
    public string Colour(string area) => _colours[IndexOf(area)];
}
