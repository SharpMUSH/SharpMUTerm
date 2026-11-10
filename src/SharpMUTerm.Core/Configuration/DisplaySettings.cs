namespace SharpMUTerm.Core.Configuration;

/// <summary>
/// How big the desktop window draws its text, area by area, and the zoom over all of them. Read by the window alone: a
/// terminal draws its cells in the font size its emulator was told, and nothing the client writes can change that, so
/// the terminal lists none of these (its help says to use the terminal's own zoom).
/// <para>
/// The output is in points, because a monospace console's size is a font size (Mudlet, Windows Terminal and kitty all
/// say it that way). Every other area is a percentage of its designed size, because each is a ramp of sizes (a heading,
/// a label, a caption) that has to move together. <see cref="Zoom"/> multiplies all of them, the way a browser's does,
/// and is what Ctrl+= / Ctrl+- / Ctrl+0 and Ctrl+wheel change.
/// </para>
/// </summary>
public sealed class DisplaySettings
{
    /// <summary>The zoom over the whole window, a percentage on <see cref="ZoomLevels.Steps"/>.</summary>
    public int Zoom { get; set; } = 100;

    /// <summary>The game's text, the command line and every cell panel, in points, before <see cref="Zoom"/>.</summary>
    public int OutputPoints { get; set; } = DefaultOutputPoints;

    /// <summary>The composer's text (F1 and the docked panel), as a percentage of the output's.</summary>
    public int ComposerText { get; set; } = 100;

    /// <summary>The header, sidebar, tab strips, footer, scrolled-back band and notices.</summary>
    public int InterfaceText { get; set; } = 100;

    /// <summary>The panels a layout places: vitals, who is here, the quick bar, the map, the docked composer's frame.</summary>
    public int PanelText { get; set; } = 100;

    /// <summary>The command palette, the dialogs, the right-click menus and the settings window.</summary>
    public int DialogText { get; set; } = 100;

    /// <summary>The output's size with nothing chosen: 10 pt, the mockups' 13 px game text.</summary>
    public const int DefaultOutputPoints = 10;

    /// <summary>The smallest output size a player can choose.</summary>
    public const int MinOutputPoints = 6;

    /// <summary>The largest output size a player can choose.</summary>
    public const int MaxOutputPoints = 48;

    /// <summary>A copy held to every range, so a hand-edited file cannot ask for a 2,000% sidebar.</summary>
    public DisplaySettings Clamped() => new()
    {
        Zoom = ZoomLevels.Clamp(Zoom),
        OutputPoints = Math.Clamp(OutputPoints, MinOutputPoints, MaxOutputPoints),
        ComposerText = ZoomLevels.ClampArea(ComposerText),
        InterfaceText = ZoomLevels.ClampArea(InterfaceText),
        PanelText = ZoomLevels.ClampArea(PanelText),
        DialogText = ZoomLevels.ClampArea(DialogText),
    };
}

/// <summary>
/// The zoom levels the window steps through, and the percentages an area's text size is chosen from. The steps are the
/// browsers' (Chrome and Firefox step 50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300): small near 100%,
/// where a reader is fine-tuning, and large past 200%, where they need it big.
/// </summary>
public static class ZoomLevels
{
    /// <summary>Every zoom level, smallest first.</summary>
    public static IReadOnlyList<int> Steps { get; } = [50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300];

    /// <summary>The percentages an area's text can be set to: the zoom steps an area is useful at.</summary>
    public static IReadOnlyList<int> AreaSteps { get; } = [75, 80, 90, 100, 110, 125, 150, 175, 200];

    /// <summary>The smallest zoom.</summary>
    public static int Minimum => Steps[0];

    /// <summary>The largest zoom.</summary>
    public static int Maximum => Steps[^1];

    /// <summary>The level after <paramref name="percent"/>, or the largest; a percentage between steps goes to the next one up.</summary>
    public static int Next(int percent)
    {
        foreach (var step in Steps)
        {
            if (step > percent)
            {
                return step;
            }
        }

        return Maximum;
    }

    /// <summary>The level before <paramref name="percent"/>, or the smallest.</summary>
    public static int Previous(int percent)
    {
        for (var i = Steps.Count - 1; i >= 0; i--)
        {
            if (Steps[i] < percent)
            {
                return Steps[i];
            }
        }

        return Minimum;
    }

    /// <summary>
    /// The level <paramref name="step"/> moves to from <paramref name="percent"/>: a positive step zooms in, a negative
    /// one out, zero resets to 100%.
    /// </summary>
    public static int Stepped(int percent, int step) => step switch
    {
        > 0 => Next(percent),
        < 0 => Previous(percent),
        _ => 100,
    };

    /// <summary><paramref name="percent"/> held to the zoom's range (a value between steps is kept, as a file may say one).</summary>
    public static int Clamp(int percent) => Math.Clamp(percent, Minimum, Maximum);

    /// <summary><paramref name="percent"/> held to an area's range.</summary>
    public static int ClampArea(int percent) => Math.Clamp(percent, AreaSteps[0], AreaSteps[^1]);

    /// <summary>How a percentage is written on every surface: <c>125%</c>.</summary>
    public static string Label(int percent) => percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>The percentage <paramref name="words"/> names (<c>125%</c> or <c>125</c>), or null.</summary>
    public static int? Parse(string? words)
    {
        var text = words?.Trim().TrimEnd('%').Trim();
        return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
