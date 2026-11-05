using System.Globalization;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// The words F5's world fields and F9's character fields are written in, and how each is read from and written to the
/// configuration: one list per choice field, one pair of functions per value that is not stored the way it is shown.
/// The declarations (<see cref="BuiltInSettings.World"/>, <see cref="BuiltInSettings.Character"/>) are built from
/// these, and the cell F5 screen draws its detail column with the same words, so the two cannot drift.
/// </summary>
public static class WorldFieldValues
{
    /// <summary>The encodings a world may be set to; <c>auto</c> follows CHARSET, every other entry is an override.</summary>
    public static IReadOnlyList<string> Encodings { get; } =
        [TelnetSessionOptions.AutoEncodingName, "UTF-8", "ISO-8859-1", "ASCII", "CP437", "CP1252"];

    /// <summary>The <c>content</c> field's words. <c>auto</c> is <see cref="ContentFormat.Ansi"/>, the stored default.</summary>
    public static IReadOnlyList<string> ContentChoices { get; } = ["auto", "ansi", "mxp", "pueblo"];

    /// <summary>A content format's word.</summary>
    public static string ContentName(ContentFormat format) => format switch
    {
        ContentFormat.AnsiOnly => "ansi",
        ContentFormat.Mxp => "mxp",
        ContentFormat.Pueblo => "pueblo",
        _ => "auto",
    };

    /// <summary>The content format a word names.</summary>
    public static ContentFormat ContentOf(string name) => name.ToLowerInvariant() switch
    {
        "ansi" => ContentFormat.AnsiOnly,
        "mxp" => ContentFormat.Mxp,
        "pueblo" => ContentFormat.Pueblo,
        _ => ContentFormat.Ansi,
    };

    /// <summary>The emoji field's word for "neither substitution".</summary>
    public const string EmojiOff = "off";

    /// <summary>The <c>emoji</c> field's words: which of the two substitutions run.</summary>
    public static IReadOnlyList<string> EmojiChoices { get; } = [EmojiOff, "emoticons", "shortcodes", "both"];

    /// <summary>A world's emoji settings as one word.</summary>
    public static string EmojiName(EmojiSettings emoji)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        return (emoji.Enabled, emoji.Emoticons, emoji.Shortcodes) switch
        {
            (false, _, _) or (true, false, false) => EmojiOff,
            (true, true, false) => "emoticons",
            (true, false, true) => "shortcodes",
            _ => "both",
        };
    }

    /// <summary>Writes one word back into a world's three emoji switches.</summary>
    public static void SetEmoji(EmojiSettings emoji, string name)
    {
        ArgumentNullException.ThrowIfNull(emoji);
        ArgumentNullException.ThrowIfNull(name);
        var choice = name.ToLowerInvariant();
        emoji.Enabled = choice != EmojiOff;
        if (emoji.Enabled)
        {
            emoji.Emoticons = choice is "emoticons" or "both";
            emoji.Shortcodes = choice is "shortcodes" or "both";
        }
    }

    /// <summary>The words for <see cref="WorldDefinition.DownloadMedia"/>: on fetches what the game asks for.</summary>
    public static IReadOnlyList<string> SoundChoices { get; } = ["download", "cached only"];

    /// <summary>The words for <see cref="WorldDefinition.GmcpLogin"/>.</summary>
    public static IReadOnlyList<string> LoginChoices { get; } = ["off", "if offered"];

    /// <summary>The words for <see cref="WorldDefinition.DiscordPresence"/>.</summary>
    public static IReadOnlyList<string> DiscordChoices { get; } = ["off", "show status"];

    /// <summary>The words for <see cref="WorldDefinition.JoinWrappedLines"/>.</summary>
    public static IReadOnlyList<string> JoinWrapChoices { get; } = ["off", "join"];

    /// <summary>The id of the declared <c>join wraps</c> field, which a note is attached to.</summary>
    public const string JoinWrapsFieldId = "join-wraps";

    /// <summary>
    /// What F5 says beside <c>join wraps</c> while a world's pane is narrower than its server wraps — one sentence for the
    /// cell screen and the window's settings alike.
    /// </summary>
    public static string JoinWrapsAdvice(int column, int paneWidth) =>
        $"recommended · wraps at {column.ToString(System.Globalization.CultureInfo.InvariantCulture)}, "
        + $"pane is {paneWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>The words for <see cref="WorldDefinition.AutoReconnect"/>.</summary>
    public static IReadOnlyList<string> ReconnectChoices { get; } = ["off", "after a drop"];

    /// <summary>
    /// The words for <see cref="WorldDefinition.AlertOnPage"/> and <see cref="WorldDefinition.AlertOnName"/> together:
    /// nothing, pages only (a new world's default), or the character's name as well.
    /// </summary>
    public static IReadOnlyList<string> AlertChoices { get; } = ["off", "pages", "name & pages"];

    /// <summary>The <c>alert on</c> row's word for <paramref name="world"/>.</summary>
    public static string AlertName(WorldDefinition world) =>
        world.AlertOnName ? AlertChoices[2] : world.AlertOnPage ? AlertChoices[1] : AlertChoices[0];

    /// <summary>Sets both alert fields from one of <see cref="AlertChoices"/>.</summary>
    public static void SetAlert(WorldDefinition world, string choice)
    {
        world.AlertOnName = choice == AlertChoices[2];
        world.AlertOnPage = choice == AlertChoices[1] || choice == AlertChoices[2];
    }

    /// <summary>
    /// The words for <see cref="WorldDefinition.CommandSeparator"/>: off, or the separator itself. A closed list, so a
    /// separator is never a letter or a space that would split ordinary text.
    /// </summary>
    public static IReadOnlyList<string> SeparatorChoices { get; } = ["off", ";", "|", ";;"];

    /// <summary>The stacking row's word for <paramref name="separator"/>.</summary>
    public static string SeparatorName(string? separator) =>
        separator is { Length: > 0 } s && SeparatorChoices.Contains(s) ? s : SeparatorChoices[0];

    /// <summary>The separator <paramref name="name"/> names, or null for off.</summary>
    public static string? SeparatorOf(string name) =>
        name != SeparatorChoices[0] && SeparatorChoices.Contains(name) ? name : null;

    /// <summary>What a world's layout row says while it names none.</summary>
    public const string WorldLayoutDefault = "(suggested)";

    /// <summary>What a character's layout row says while it names none.</summary>
    public const string CharacterLayoutDefault = "(world default)";

    /// <summary>The <c>at start</c> word for connecting at launch.</summary>
    public const string StartupOn = "connect";

    /// <summary>The <c>at start</c> word for not.</summary>
    public const string StartupOff = "off";

    /// <summary>The <c>at start</c> field's words.</summary>
    public static IReadOnlyList<string> StartupChoices { get; } = [StartupOn, StartupOff];

    /// <summary>The <c>restore</c> word for on.</summary>
    public const string RestoreOn = "on";

    /// <summary>The <c>restore</c> word for off.</summary>
    public const string RestoreOff = "off";

    /// <summary>The <c>restore</c> field's words.</summary>
    public static IReadOnlyList<string> RestoreChoices { get; } = [RestoreOn, RestoreOff];

    /// <summary>The log formats, by name.</summary>
    public static IReadOnlyList<string> LogFormats { get; } = Enum.GetNames<LogFormat>();

    /// <summary>The pane tints, by name.</summary>
    public static IReadOnlyList<string> Tints { get; } = Enum.GetNames<PaneTint>();

    /// <summary>Every layout a world or character may name: the built-ins and the player's own.</summary>
    public static IReadOnlyList<LayoutDefinition> Layouts(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new LayoutLibrary(config.Layouts).All;
    }

    /// <summary>The layout row's choices: the default word, then every layout by name.</summary>
    public static IReadOnlyList<string> LayoutChoices(IReadOnlyList<LayoutDefinition> layouts, string unset)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        return [unset, .. layouts.Select(l => l.Name)];
    }

    /// <summary>The name of the layout <paramref name="id"/> names, or null for none (or one that is gone).</summary>
    public static string? LayoutName(IReadOnlyList<LayoutDefinition> layouts, string? id)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        return id is null ? null : layouts.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.Ordinal))?.Name;
    }

    /// <summary>The id of the layout called <paramref name="name"/>, or null for the default word.</summary>
    public static string? LayoutId(IReadOnlyList<LayoutDefinition> layouts, string name)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        return layouts.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>A world's extra GMCP modules as one line, or null for none.</summary>
    public static string? JoinModules(WorldDefinition world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return world.GmcpModules.Count == 0 ? null : string.Join(", ", world.GmcpModules);
    }

    /// <summary>
    /// Writes the line back: split on commas, and only entries <see cref="GmcpHandshake.Parse"/> accepts are kept,
    /// normalised to <c>Name Version</c>, so what is saved is what will be sent. A new list, never an edit of the old
    /// one, so a surface reading on another thread never meets a half-made change.
    /// </summary>
    public static void SplitModules(WorldDefinition world, string? value)
    {
        ArgumentNullException.ThrowIfNull(world);
        var modules = new List<string>();
        foreach (var part in (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (GmcpHandshake.Parse(part) is { } module
                && !modules.Any(m => m.StartsWith(module.Name + " ", StringComparison.OrdinalIgnoreCase)))
            {
                modules.Add($"{module.Name} {module.Version.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        world.GmcpModules = modules;
    }

    /// <summary>The words for <see cref="WorldDefinition.CaptureChannels"/>.</summary>
    public static IReadOnlyList<string> ChannelCaptureChoices { get; } = ["off", "copy", "move"];

    /// <summary>A capture mode as its word.</summary>
    public static string ChannelCaptureName(SharpMUTerm.Core.Protocols.ChannelCaptureMode mode) => ChannelCaptureChoices[(int)mode];

    /// <summary>A word as a capture mode; anything else is off.</summary>
    public static SharpMUTerm.Core.Protocols.ChannelCaptureMode ChannelCaptureOf(string? word) =>
        (SharpMUTerm.Core.Protocols.ChannelCaptureMode)Math.Max(0, ChannelCaptureChoices.ToList().IndexOf(word ?? string.Empty));

    /// <summary>F7's words for a scene log's format.</summary>
    public static IReadOnlyList<string> SceneFormats { get; } = ["html", "plain text"];

    /// <summary>F7's words for what a scene log keeps.</summary>
    public static IReadOnlyList<string> SceneContents { get; } = ["poses", "every line"];
}
