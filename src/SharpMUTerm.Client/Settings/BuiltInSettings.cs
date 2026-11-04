using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// The client's own settings screens, declared: one section per screen, in F-key order of the screens' place
/// in the list, each carrying the key that opens it. F7 (<see cref="Text"/>) and F8 (<see cref="Input"/>)
/// declare their fields and are drawn from them; the rest are listed by title so the settings search can name
/// them and take a reader there, and declare fields as they are moved over.
/// <para>
/// <b>To add a field</b>: add a descriptor to its section's list with a <see cref="SettingsField.Group"/> (the
/// heading it sits under) and a <see cref="SettingBinding{T}"/> over the configuration property that holds it.
/// The screen draws it, the search finds it, and <c>● changed</c> and the reset key work on it with nothing
/// else written — the default is read off a fresh <see cref="AppConfiguration"/>, never restated here.
/// </para>
/// </summary>
public static class BuiltInSettings
{
    /// <summary>F7's groups, in the order they are drawn. Colour and legibility: what the client does with the colours a world sends.</summary>
    public const string ColourGroup = "Colour & legibility";

    /// <summary>Text: how a line's characters are laid out.</summary>
    public const string TextGroup = "Text";

    /// <summary>Links and pictures: what counts as a link and how one is drawn.</summary>
    public const string LinksGroup = "Links & pictures";

    /// <summary>The status row's meters.</summary>
    public const string MetersGroup = "Status row & meters";

    /// <summary>The bar marking where a reader left a window.</summary>
    public const string ActivityGroup = "Activity";

    /// <summary>Game sound; drawn only where a host passes the sound settings.</summary>
    public const string SoundGroup = "Sound";

    /// <summary>The id of the meters field, which a host draws as a table rather than one value.</summary>
    public const string MetersField = "meters";

    /// <summary>F7: how a world's text is drawn.</summary>
    public static SettingsSectionContribution Text { get; } = new()
    {
        Id = "text",
        Title = "Text & display",
        Key = "F7",
        Order = 60,
        Fields =
        [
            Flag("strip-colour", "strip incoming ANSI colour", ColourGroup,
                c => c.Text.StripIncomingColour, (c, v) => c.Text.StripIncomingColour = v,
                "draw every line in the theme's own colours", "sgr monochrome color"),
            Flag("legible", "keep text legible", ColourGroup,
                c => c.Text.KeepTextLegible, (c, v) => c.Text.KeepTextLegible = v,
                "lift a colour that would be unreadable on the pane", "contrast color"),
            Flag("blink", "allow blink", ColourGroup,
                c => c.Text.AllowBlink, (c, v) => c.Text.AllowBlink = v,
                "let a world's blinking text blink", "flash"),
            Number("transparency", "background transparency (%)", 0, TextSettings.MaxBackgroundTransparency,
                ColourGroup,
                c => c.Text.BackgroundTransparency, (c, v) => c.Text.BackgroundTransparency = v,
                "how see-through the panes are on a terminal that can do it; 0 is off",
                "opacity translucent kitty background"),

            Number("tab-width", "tab width (spaces)", 0, TextSettings.MaxTabWidth, TextGroup,
                c => c.Text.TabWidth, (c, v) => c.Text.TabWidth = v,
                "how many spaces a tab expands to; 0 deletes tabs", "whitespace tabs"),
            Flag("emoji", "emoji substitution", TextGroup,
                c => c.Text.EmojiSubstitution, (c, v) => c.Text.EmojiSubstitution = v,
                "draw :smile: style codes as the emoji they name", "unicode"),

            Flag("underline-links", "underline hyperlinks", LinksGroup,
                c => c.Text.UnderlineHyperlinks, (c, v) => c.Text.UnderlineHyperlinks = v,
                "underline every clickable link", "url"),
            Flag("detect-links", "detect links in output", LinksGroup,
                c => c.Text.DetectLinks, (c, v) => c.Text.DetectLinks = v,
                "make an http(s) address a world prints clickable", "url browser"),

            new ListField(
                MetersField,
                "meters",
                [
                    new TextField("label", "label", StatusMeter.MaxLabelLength),
                    new TextField("value", "value path"),
                    new TextField("max", "maximum path"),
                ],
                "game data the status row shows, such as hp 412/500")
            {
                Group = MetersGroup,
                Keywords = "gauge gmcp msdp vitals hp mana status",
                Binding = new SettingBinding<List<StatusMeter>>(
                    c => c.Text.StatusMeters,
                    (c, v) => c.Text.StatusMeters = v,
                    StatusMeter.Format,
                    v => v.Count == 0 ? "none" : string.Join(" · ", v.Select(m => m.Label))),
            },

            Number("activity-seconds", "activity bar holds for (seconds)", 0, TextSettings.MaxActivityBarSeconds,
                ActivityGroup,
                c => c.Text.ActivityBarSeconds, (c, v) => c.Text.ActivityBarSeconds = v,
                "how long the NEW bar stays once you are back; 0 retires it at once", "unread new away"),

            Flag("sound", "game sound", SoundGroup,
                c => c.Sound.Enabled, (c, v) => c.Sound.Enabled = v,
                "play the sounds and music a world sends", "audio mcmp msp"),
            Number("master-volume", "master volume (%)", 0, SoundSettings.MaxVolume, SoundGroup,
                c => c.Sound.MasterVolume, (c, v) => c.Sound.MasterVolume = v, null, "audio loudness"),
            Number("sound-volume", "sound volume (%)", 0, SoundSettings.MaxVolume, SoundGroup,
                c => c.Sound.SoundVolume, (c, v) => c.Sound.SoundVolume = v, null, "audio loudness effects"),
            Number("music-volume", "music volume (%)", 0, SoundSettings.MaxVolume, SoundGroup,
                c => c.Sound.MusicVolume, (c, v) => c.Sound.MusicVolume = v, null, "audio loudness"),
            Flag("captions", "sound captions", SoundGroup,
                c => c.Sound.Captions, (c, v) => c.Sound.Captions = v,
                "print a line of text for each sound", "audio subtitles accessibility"),
        ],
    };

    /// <summary>F8's groups.</summary>
    public const string InputGroup = "Input";

    /// <summary>The command line's own size.</summary>
    public const string CommandLineGroup = "Command line";

    /// <summary>F8: the command line.</summary>
    public static SettingsSectionContribution Input { get; } = new()
    {
        Id = "input",
        Title = "Input",
        Key = "F8",
        Order = 70,
        Fields =
        [
            Flag("echo", "local echo", InputGroup,
                c => c.Input.LocalEcho, (c, v) => c.Input.LocalEcho = v,
                "show what you send in the output", "typed commands"),
            Flag("drafts", "keep per-tab drafts", InputGroup,
                c => c.Input.KeepDrafts, (c, v) => c.Input.KeepDrafts = v,
                "each window keeps the line you were typing", "unsent"),
            Flag("no-logins", "keep logins out of history", InputGroup,
                c => c.Input.ExcludeCredentials, (c, v) => c.Input.ExcludeCredentials = v,
                "a typed connect line and its password are not recorded", "password secret credentials"),
            Number("rows", "height (lines)", InputSettings.MinRows, InputSettings.MaxRowCeiling, CommandLineGroup,
                c => c.Input.Rows, (c, v) => c.Input.Rows = v,
                "how many rows the command line starts at", "size"),
            Number("max-rows", "grows to (lines)", InputSettings.MinRows, InputSettings.MaxRowCeiling,
                CommandLineGroup,
                c => c.Input.MaxRows, (c, v) => c.Input.MaxRows = v,
                "how tall a long line may make it", "size wrap"),
            Flag("second-bar", "second bar on new windows", CommandLineGroup,
                c => c.Input.SecondBar, (c, v) => c.Input.SecondBar = v,
                "open new windows with a second command line", "split input"),
        ],
    };

    /// <summary>Every section the client declares, in their order: the screens F2 to F10.</summary>
    public static IReadOnlyList<SettingsSectionContribution> All { get; } =
    [
        Listed("worlds", "Worlds & Characters", "F5", 10),
        Listed("triggers", "Triggers & spawn routing", "F2", 20),
        Listed("aliases", "Aliases", "F3", 30),
        Listed("keypad", "Keypad & hotkeys", "F4", 40),
        Listed("timers", "Timers", "F6", 50),
        Text,
        Input,
        Listed("logging", "Character logging", "F9", 80),
        Listed("packages", "Packages", "F10", 90),
    ];

    /// <summary>Registers every built-in section.</summary>
    public static void Register(SettingsSectionList sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        foreach (var section in All)
        {
            sections.Add(section);
        }
    }

    private static SettingsSectionContribution Listed(string id, string title, string key, int order) =>
        new() { Id = id, Title = title, Key = key, Order = order };

    private static BoolField Flag(
        string id,
        string label,
        string group,
        Func<AppConfiguration, bool> get,
        Action<AppConfiguration, bool> set,
        string? help = null,
        string? keywords = null) =>
        new(id, label, help)
        {
            Group = group,
            Keywords = keywords,
            Binding = new SettingBinding<bool>(get, set, v => v ? "on" : "off"),
        };

    private static NumberField Number(
        string id,
        string label,
        int minimum,
        int maximum,
        string group,
        Func<AppConfiguration, int> get,
        Action<AppConfiguration, int> set,
        string? help = null,
        string? keywords = null) =>
        new(id, label, minimum, maximum, help)
        {
            Group = group,
            Keywords = keywords,
            Binding = new SettingBinding<int>(get, set),
        };
}
