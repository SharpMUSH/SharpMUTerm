using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

public static partial class BuiltInSettings
{
    /// <summary>The Text size page's first group: the zoom over everything, and the output's own size.</summary>
    public const string ZoomGroup = "Zoom";

    /// <summary>The Text size page's second group: each area's size against its designed one.</summary>
    public const string AreasGroup = "Text size by area";

    /// <summary>The id of the Text size page, which ⌃P's zoom entries' detail and the help topic name.</summary>
    public const string TextSizeSectionId = "text-size";

    /// <summary>The zoom levels as the page lists them: <c>50%</c> … <c>300%</c>.</summary>
    public static IReadOnlyList<string> ZoomChoices { get; } = [.. ZoomLevels.Steps.Select(ZoomLevels.Label)];

    /// <summary>An area's sizes as the page lists them: <c>75%</c> … <c>200%</c>.</summary>
    public static IReadOnlyList<string> AreaChoices { get; } = [.. ZoomLevels.AreaSteps.Select(ZoomLevels.Label)];

    /// <summary>
    /// The desktop window's text sizes: a zoom over the whole window (Ctrl+= / Ctrl+- / Ctrl+0, Ctrl+wheel), the
    /// output's size in points, and the composer, interface, panels and dialogs each as a percentage. Not in
    /// <see cref="All"/> and on no F-key, like <see cref="Window"/>: a host registers it only when it draws its own text
    /// (the window), so a terminal, whose font size is its emulator's, never lists a choice that does nothing there.
    /// </summary>
    public static SettingsSectionContribution TextSize { get; } = new()
    {
        Id = TextSizeSectionId,
        Title = "Text size & zoom",
        Order = 65,
        Fields =
        [
            new ChoiceField("zoom", "zoom", ZoomChoices,
                "everything in the window, as a browser zooms; Ctrl+= and Ctrl+- step it, Ctrl+0 resets, Ctrl+wheel too")
            {
                Group = ZoomGroup,
                Keywords = "zoom scale magnify bigger smaller font size text size accessibility dpi",
                Binding = Percent(c => c.Display.Zoom, (c, v) => c.Display.Zoom = ZoomLevels.Clamp(v)),
            },
            Number("output-points", "output text (pt)", DisplaySettings.MinOutputPoints, DisplaySettings.MaxOutputPoints,
                ZoomGroup,
                c => c.Display.OutputPoints, (c, v) => c.Display.OutputPoints = v,
                "the game's text and the command line, before the zoom; the panes are re-measured and the game told their new size",
                "font size points output console monospace game text naws"),

            Area("composer-text", "composer text", c => c.Display.ComposerText, (c, v) => c.Display.ComposerText = v,
                "what you write in F1 and the docked composer, against the output's size", "compose editor post font"),
            Area("interface-text", "interface", c => c.Display.InterfaceText, (c, v) => c.Display.InterfaceText = v,
                "the header, sidebar, tabs, status row and notices", "chrome sidebar header tabs footer status toast font"),
            Area("panel-text", "panels", c => c.Display.PanelText, (c, v) => c.Display.PanelText = v,
                "vitals, who is here, the quick bar and the map", "vitals map who here quick bar gauges font"),
            Area("dialog-text", "menus & dialogs", c => c.Display.DialogText, (c, v) => c.Display.DialogText = v,
                "the command palette, dialogs, right-click menus and this settings window", "palette modal menu dialog settings font"),
        ],
    };

    private static ChoiceField Area(
        string id,
        string label,
        Func<AppConfiguration, int> get,
        Action<AppConfiguration, int> set,
        string help,
        string keywords) =>
        new(id, label, AreaChoices, help)
        {
            Group = AreasGroup,
            Keywords = "font size text size bigger smaller " + keywords,
            Binding = Percent(get, (c, v) => set(c, ZoomLevels.ClampArea(v))),
        };

    /// <summary>A percentage shown and chosen as <c>125%</c>; a word that names none leaves the value as it was.</summary>
    private static SettingBinding<string> Percent(Func<AppConfiguration, int> get, Action<AppConfiguration, int> set) =>
        new(
            c => ZoomLevels.Label(get(c)),
            (c, v) =>
            {
                if (ZoomLevels.Parse(v) is { } percent)
                {
                    set(c, percent);
                }
            });
}
