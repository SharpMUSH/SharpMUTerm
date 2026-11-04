using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Logging;

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
public static partial class BuiltInSettings
{
    /// <summary>F7's groups, in the order they are drawn. The theme: the client's own colours, picked with a preview.</summary>
    public const string ThemeGroup = "Theme";

    /// <summary>The id of the theme field, which a host draws as a list of previews rather than one value.</summary>
    public const string ThemeFieldId = "theme";

    /// <summary>Colour and legibility: what the client does with the colours a world sends.</summary>
    public const string ColourGroup = "Colour & legibility";

    /// <summary>Text: how a line's characters are laid out.</summary>
    public const string TextGroup = "Text";

    /// <summary>Links and pictures: what counts as a link and how one is drawn.</summary>
    public const string LinksGroup = "Links & pictures";

    /// <summary>The status row's meters.</summary>
    public const string MetersGroup = "Status row & meters";

    /// <summary>Telling the desktop while the player is in another window.</summary>
    public const string AlertsGroup = "Alerts";

    /// <summary>The bar marking where a reader left a window.</summary>
    public const string ActivityGroup = "Activity";

    /// <summary>What a pane does when it is scrolled back.</summary>
    public const string ScrollbackGroup = "Scrollback";

    /// <summary>Game sound; drawn only where a host passes the sound settings.</summary>
    public const string SoundGroup = "Sound";

    /// <summary>How a scene log is written.</summary>
    public const string SceneLogGroup = "Scene log";

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
            new ThemeField(ThemeFieldId, "theme", "the colours of the panes, the bars and these screens")
            {
                Group = ThemeGroup,
                Keywords = "colour color dark light solarized palette appearance look",
                Binding = new SettingBinding<string>(ThemeChoices.Current, ThemeChoices.Apply),
            },

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

            Flag("alerts", "alert when away", AlertsGroup,
                c => c.Text.AlertWhenAway, (c, v) => c.Text.AlertWhenAway = v,
                "tell the desktop when an alert trigger or a page arrives while you are in another window",
                "notify notification bell flash urgent page tell mention"),

            Flag("split-scrollback", "split the pane when scrolled back", ScrollbackGroup,
                c => c.Text.SplitScrollback, (c, v) => c.Text.SplitScrollback = v,
                "keep the newest lines in view below the history you are reading", "live tail history split"),

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

            new ChoiceField("scene-format", "scene log format", WorldFieldValues.SceneFormats, "HTML keeps the game's colours")
            {
                Group = SceneLogGroup,
                Keywords = "roleplay rp scene log html plain transcript",
                Binding = new SettingBinding<string>(
                    c => c.SceneLog.Html ? WorldFieldValues.SceneFormats[0] : WorldFieldValues.SceneFormats[1],
                    (c, v) => c.SceneLog.Html = v != WorldFieldValues.SceneFormats[1]),
            },
            new ChoiceField("scene-content", "scene log keeps", WorldFieldValues.SceneContents, "poses start with the name of someone present")
            {
                Group = SceneLogGroup,
                Keywords = "roleplay rp scene log poses",
                Binding = new SettingBinding<string>(
                    c => c.SceneLog.Content == SceneLogContent.Everything ? WorldFieldValues.SceneContents[1] : WorldFieldValues.SceneContents[0],
                    (c, v) => c.SceneLog.Content = v == WorldFieldValues.SceneContents[1] ? SceneLogContent.Everything : SceneLogContent.Poses),
            },
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

    /// <summary>F5's world groups.</summary>
    public const string ConnectionGroup = "Connection";

    /// <summary>What the world speaks and how it logs in.</summary>
    public const string ProtocolGroup = "Protocols & login";

    /// <summary>How the world's text is taken in.</summary>
    public const string WorldTextGroup = "Text";

    /// <summary>What the mapper reads.</summary>
    public const string MapGroup = "Map";

    /// <summary>Sound and the starting layout.</summary>
    public const string WorldMediaGroup = "Sound & layout";

    /// <summary>
    /// F5: one world's own fields, edited for the world a surface has chosen. The cell screen builds its world row from
    /// these (in its own ⇥ order, by id — see <c>WorldsScreenRenderer.WorldFieldOrder</c>), and its CHARACTERS and TRIGGER
    /// SETS panes — lists, not fields — stay the cell screen's, which the native window links to.
    /// </summary>
    public static SettingsSectionContribution World { get; } = new()
    {
        Id = "worlds",
        Title = "Worlds & Characters",
        Key = "F5",
        Order = 10,
        Scope = SettingScope.World,
        Fields =
        [
            WorldText("name", "name", TextRule.Name, ConnectionGroup,
                (_, w) => w.Name, (_, w, v) => w.Name = v ?? w.Name, "what the world is called here", resettable: false),
            WorldText("host", "host", TextRule.Required, ConnectionGroup,
                (_, w) => w.Host, (_, w, v) => w.Host = v ?? w.Host, "the address to dial", "address server dns", resettable: false),
            new NumberField("port", "port", 1, 65535, "the port to dial")
            {
                Group = ConnectionGroup,
                Binding = SettingBinding<int>.ForWorld((_, w) => w.Port, (_, w, v) => w.Port = v, resettable: false),
            },
            WorldFlag("tls", "TLS", ConnectionGroup, w => w.UseTls, (w, v) => w.UseTls = v,
                "connect over an encrypted connection", "ssl secure encrypted"),
            WorldFlag("invalid-certificates", "accept invalid certificates", ConnectionGroup,
                w => w.AllowInvalidCertificates, (w, v) => w.AllowInvalidCertificates = v,
                "connect even when the server's certificate does not check out", "ssl tls self-signed insecure"),
            WorldChoice("encoding", "encoding", WorldFieldValues.Encodings, ConnectionGroup,
                (_, w) => w.Encoding, (_, w, v) => w.Encoding = v,
                "auto follows what the server agrees; anything else overrides it", "charset utf-8 latin"),
            new NumberField("keepalive", "keepalive", 0, 86400, "seconds between keepalives; 0 is off")
            {
                Group = ConnectionGroup,
                Keywords = "idle timeout ping",
                Binding = SettingBinding<int>.ForWorld((_, w) => w.KeepaliveSeconds, (_, w, v) => w.KeepaliveSeconds = v),
            },

            WorldChoice("content", "content", WorldFieldValues.ContentChoices, ProtocolGroup,
                (_, w) => WorldFieldValues.ContentName(w.ContentFormat), (_, w, v) => w.ContentFormat = WorldFieldValues.ContentOf(v),
                "the markup the world sends; auto follows what it negotiates", "mxp pueblo ansi markup"),
            WorldText("gmcp-modules", "gmcp modules", TextRule.Optional, ProtocolGroup,
                (_, w) => WorldFieldValues.JoinModules(w), (_, w, v) => WorldFieldValues.SplitModules(w, v),
                "extra GMCP packages to ask for, such as IRE.Rift 1", "gmcp packages supports"),
            WorldChoice("gmcp-login", "gmcp login", WorldFieldValues.LoginChoices, ProtocolGroup,
                (_, w) => w.GmcpLogin ? WorldFieldValues.LoginChoices[1] : WorldFieldValues.LoginChoices[0],
                (_, w, v) => w.GmcpLogin = v == WorldFieldValues.LoginChoices[1],
                "log in over GMCP when the server offers it", "char.login password credentials"),
            WorldChoice("reconnect", "reconnect", WorldFieldValues.ReconnectChoices, ConnectionGroup,
                (_, w) => w.AutoReconnect ? WorldFieldValues.ReconnectChoices[1] : WorldFieldValues.ReconnectChoices[0],
                (_, w, v) => w.AutoReconnect = v == WorldFieldValues.ReconnectChoices[1],
                "dial again after a dropped connection, 5 s to 60 s apart, five tries", "auto reconnect drop retry"),
            WorldChoice("discord", "discord", WorldFieldValues.DiscordChoices, ProtocolGroup,
                (_, w) => w.DiscordPresence ? WorldFieldValues.DiscordChoices[1] : WorldFieldValues.DiscordChoices[0],
                (_, w, v) => w.DiscordPresence = v == WorldFieldValues.DiscordChoices[1],
                "show what the game says you are doing on Discord", "presence rich status"),

            WorldChoice("emoji", "emoji", WorldFieldValues.EmojiChoices, WorldTextGroup,
                (_, w) => WorldFieldValues.EmojiName(w.Emoji), (_, w, v) => WorldFieldValues.SetEmoji(w.Emoji, v),
                "which emoji substitutions run on this world's text", "emoticons shortcodes"),
            WorldChoice(WorldFieldValues.JoinWrapsFieldId, "join wraps", WorldFieldValues.JoinWrapChoices, WorldTextGroup,
                (_, w) => w.JoinWrappedLines ? WorldFieldValues.JoinWrapChoices[1] : WorldFieldValues.JoinWrapChoices[0],
                (_, w, v) => w.JoinWrappedLines = v == WorldFieldValues.JoinWrapChoices[1],
                "join lines the server wrapped at its own width", "reflow wrap hard"),
            WorldChoice("stacking", "command stacking", WorldFieldValues.SeparatorChoices, WorldTextGroup,
                (_, w) => WorldFieldValues.SeparatorName(w.CommandSeparator),
                (_, w, v) => w.CommandSeparator = WorldFieldValues.SeparatorOf(v),
                "split a typed line on this, and #3 cmd repeats one; off for a MUSH", "separator semicolon repeat speedwalk"),
            WorldChoice("alert-on", "alert on", WorldFieldValues.AlertChoices, WorldTextGroup,
                (_, w) => WorldFieldValues.AlertName(w),
                (_, w, v) => WorldFieldValues.SetAlert(w, v),
                "tell the desktop when you are paged, or your character is named too", "notify mention page tell"),
            WorldChoice("capture-channels", "capture channels", WorldFieldValues.ChannelCaptureChoices, WorldTextGroup,
                (_, w) => WorldFieldValues.ChannelCaptureName(w.CaptureChannels),
                (_, w, v) => w.CaptureChannels = WorldFieldValues.ChannelCaptureOf(v),
                "put each GMCP channel in a window of its own, as a copy or moved out of the main window",
                "comm.channel chat ooc gossip spawn window"),
            WorldText("highlight-names", "highlight names", TextRule.NameHighlights, WorldTextGroup,
                (_, w) => NameHighlights.Format(w.HighlightNames),
                (_, w, v) => w.HighlightNames = [.. NameHighlights.Parse(v).Names],
                "names drawn in a colour wherever they appear: Ashlyn=gold, Merrow=red",
                "friends enemies highlight name colour"),
            WorldText("edit-grab", "edit grab", TextRule.Optional, WorldTextGroup,
                (_, w) => w.GrabPrefix, (_, w, v) => w.GrabPrefix = v,
                "a line starting with this goes on the command line instead of the output", "fugueedit mush edit prefix"),

            WorldText("map-room", "map room", TextRule.Pattern, MapGroup,
                (_, w) => w.Map.RoomNamePattern, (_, w, v) => w.Map.RoomNamePattern = v,
                "a pattern matching a room's name line", "mapper regex"),
            WorldText("map-exits", "map exits", TextRule.Pattern, MapGroup,
                (_, w) => w.Map.ExitsPattern, (_, w, v) => w.Map.ExitsPattern = v,
                "a pattern matching a room's exits line", "mapper regex"),
            WorldText("walk-fail", "walk fail", TextRule.Pattern, MapGroup,
                (_, w) => w.Map.WalkFailurePattern, (_, w, v) => w.Map.WalkFailurePattern = v,
                "a pattern that stops a speedwalk", "speedwalk mapper regex"),

            WorldChoice("sounds", "sounds", WorldFieldValues.SoundChoices, WorldMediaGroup,
                (_, w) => w.DownloadMedia ? WorldFieldValues.SoundChoices[0] : WorldFieldValues.SoundChoices[1],
                (_, w, v) => w.DownloadMedia = v == WorldFieldValues.SoundChoices[0],
                "fetch the sounds the game asks for, or play only those already cached", "audio media download"),
            new ChoiceField("layout", "layout", [WorldFieldValues.WorldLayoutDefault], "the layout this world's characters start in")
            {
                Group = WorldMediaGroup,
                Keywords = "panes combat roleplay",
                ChoicesFrom = c => WorldFieldValues.LayoutChoices(WorldFieldValues.Layouts(c), WorldFieldValues.WorldLayoutDefault),
                Binding = SettingBinding<string>.ForWorld(
                    (c, w) => WorldFieldValues.LayoutName(WorldFieldValues.Layouts(c), w.Layout) ?? WorldFieldValues.WorldLayoutDefault,
                    (c, w, v) => w.Layout = WorldFieldValues.LayoutId(WorldFieldValues.Layouts(c), v)),
            },
        ],
    };

    /// <summary>F9's character groups.</summary>
    public const string CharacterGroup = "Character";

    /// <summary>The character's transcript and restore log.</summary>
    public const string LoggingGroup = "Logging";

    /// <summary>How the character's panes look.</summary>
    public const string LookGroup = "Panes";

    /// <summary>
    /// F9: one character's own fields, edited for the character a surface has chosen. The cell screen builds its
    /// character row from these (in its ⇥ order, by id — <c>WorldsScreenRenderer.CharacterFieldOrder</c>); the trigger
    /// sets a character uses are a list, and stay on the cell screen.
    /// </summary>
    public static SettingsSectionContribution Character { get; } = new()
    {
        Id = "logging",
        Title = "Characters",
        Key = "F9",
        Order = 80,
        Scope = SettingScope.Character,
        Fields =
        [
            CharacterText("name", "name", TextRule.Name, CharacterGroup,
                (_, c) => c.Name, (_, c, v) => c.Name = v ?? c.Name, "the name the connect line logs in as", resettable: false),
            new TextField("password", "password", Masked: true, Help: "saved in secrets.json, in plain text")
            {
                Group = CharacterGroup,
                Rule = TextRule.Secret,
                Keywords = "secret login",
                Binding = SettingBinding<string?>.ForCharacter((_, c) => c.Password, (_, c, v) => c.Password = v, resettable: false),
            },
            new TextField("connect", "connect", Help: "the login line; %CHARACTER% and %PASSWORD% are filled in")
            {
                Group = CharacterGroup,
                Rule = TextRule.Defaulted,
                Fallback = ConnectStringTemplate.Default,
                Keywords = "login auto-login connect string",
                Binding = SettingBinding<string?>.ForCharacter((_, c) => c.ConnectString, (_, c, v) => c.ConnectString = v),
            },
            CharacterText("on-connect", "on connect", TextRule.Optional, CharacterGroup,
                (_, c) => c.OnConnect, (_, c, v) => c.OnConnect = v, "a command sent once logged in", "commands startup"),
            CharacterText("disconnect", "disconnect", TextRule.Optional, CharacterGroup,
                (_, c) => c.OnDisconnect, (_, c, v) => c.OnDisconnect = v, "a command sent before hanging up", "quit logout"),
            CharacterChoice("at-start", "at start", WorldFieldValues.StartupChoices, CharacterGroup,
                (_, c) => c.ConnectAtStartup ? WorldFieldValues.StartupOn : WorldFieldValues.StartupOff,
                (_, c, v) => c.ConnectAtStartup = string.Equals(v, WorldFieldValues.StartupOn, StringComparison.OrdinalIgnoreCase),
                "connect this character when the client starts", "startup launch auto connect"),

            CharacterChoice("log", "log", WorldFieldValues.LogFormats, LoggingGroup,
                (_, c) => c.Logging.Format.ToString(),
                (_, c, v) => c.Logging.Format = Enum.TryParse<LogFormat>(v, ignoreCase: true, out var f) ? f : c.Logging.Format,
                "write this character's session to a transcript", "transcript html text logging"),
            CharacterText("log-folder", "log folder", TextRule.Optional, LoggingGroup,
                (_, c) => c.Logging.Directory, (_, c, v) => c.Logging.Directory = v,
                "where transcripts go; blank is the default folder", "directory path logging"),
            CharacterChoice("restore", "restore", WorldFieldValues.RestoreChoices, LoggingGroup,
                (_, c) => c.Logging.RestoreLog ? WorldFieldValues.RestoreOn : WorldFieldValues.RestoreOff,
                (_, c, v) => c.Logging.RestoreLog = string.Equals(v, WorldFieldValues.RestoreOn, StringComparison.OrdinalIgnoreCase),
                "panes refill after a restart", "restart history"),

            new ColourField("tint", "tint", WorldFieldValues.Tints, "this character's pane colour")
            {
                Group = LookGroup,
                Keywords = "colour color pane tint",
                Binding = SettingBinding<string>.ForCharacter(
                    (_, c) => c.Tint.ToString(),
                    (_, c, v) => c.Tint = Enum.TryParse<PaneTint>(v, ignoreCase: true, out var t) ? t : c.Tint),
            },
            new ChoiceField("layout", "layout", [WorldFieldValues.CharacterLayoutDefault], "the layout this character starts in")
            {
                Group = LookGroup,
                Keywords = "panes combat roleplay",
                ChoicesFrom = c => WorldFieldValues.LayoutChoices(WorldFieldValues.Layouts(c), WorldFieldValues.CharacterLayoutDefault),
                Binding = SettingBinding<string>.ForCharacter(
                    (c, ch) => WorldFieldValues.LayoutName(WorldFieldValues.Layouts(c), ch.Layout) ?? WorldFieldValues.CharacterLayoutDefault,
                    (c, ch, v) => ch.Layout = WorldFieldValues.LayoutId(WorldFieldValues.Layouts(c), v)),
            },
        ],
    };

    /// <summary>Every section the client declares, in their order: the screens F2 to F10.</summary>
    /// <remarks>
    /// Built on first read rather than in a field initializer: the list sections are declared in another file of this
    /// partial class, and the order static initializers run in across files is not one to depend on.
    /// </remarks>
    public static IReadOnlyList<SettingsSectionContribution> All => _all ??=
    [
        World,
        Triggers,
        Aliases,
        Keypad,
        Timers,
        Text,
        Input,
        Character,
        Packages,
    ];

    private static IReadOnlyList<SettingsSectionContribution>? _all;

    /// <summary>Registers every built-in section.</summary>
    public static void Register(SettingsSectionList sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        foreach (var section in All)
        {
            sections.Add(section);
        }
    }

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

    private static BoolField WorldFlag(
        string id,
        string label,
        string group,
        Func<WorldDefinition, bool> get,
        Action<WorldDefinition, bool> set,
        string? help = null,
        string? keywords = null) =>
        new(id, label, help)
        {
            Group = group,
            Keywords = keywords,
            Binding = SettingBinding<bool>.ForWorld((_, w) => get(w), (_, w, v) => set(w, v), v => v ? "on" : "off"),
        };

    private static ChoiceField WorldChoice(
        string id,
        string label,
        IReadOnlyList<string> choices,
        string group,
        Func<AppConfiguration, WorldDefinition, string> get,
        Action<AppConfiguration, WorldDefinition, string> set,
        string? help = null,
        string? keywords = null) =>
        new(id, label, choices, help)
        {
            Group = group,
            Keywords = keywords,
            Binding = SettingBinding<string>.ForWorld(get, set),
        };

    private static TextField WorldText(
        string id,
        string label,
        TextRule rule,
        string group,
        Func<AppConfiguration, WorldDefinition, string?> get,
        Action<AppConfiguration, WorldDefinition, string?> set,
        string? help = null,
        string? keywords = null,
        bool resettable = true) =>
        new(id, label, Help: help)
        {
            Group = group,
            Rule = rule,
            Keywords = keywords,
            Binding = SettingBinding<string?>.ForWorld(get, set, resettable: resettable),
        };

    private static ChoiceField CharacterChoice(
        string id,
        string label,
        IReadOnlyList<string> choices,
        string group,
        Func<AppConfiguration, CharacterDefinition, string> get,
        Action<AppConfiguration, CharacterDefinition, string> set,
        string? help = null,
        string? keywords = null) =>
        new(id, label, choices, help)
        {
            Group = group,
            Keywords = keywords,
            Binding = SettingBinding<string>.ForCharacter(get, set),
        };

    private static TextField CharacterText(
        string id,
        string label,
        TextRule rule,
        string group,
        Func<AppConfiguration, CharacterDefinition, string?> get,
        Action<AppConfiguration, CharacterDefinition, string?> set,
        string? help = null,
        string? keywords = null,
        bool resettable = true) =>
        new(id, label, Help: help)
        {
            Group = group,
            Rule = rule,
            Keywords = keywords,
            Binding = SettingBinding<string?>.ForCharacter(get, set, resettable: resettable),
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
