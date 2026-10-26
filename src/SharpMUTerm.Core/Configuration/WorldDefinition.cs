using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Transport;

namespace SharpMUTerm.Core.Configuration;

/// <summary>Which log formats a session writes.</summary>
public enum LogFormat
{
    None,
    Plain,
    Html,
    Both,
}

/// <summary>How a world's inbound text is interpreted for styling and interaction.</summary>
public enum ContentFormat
{
    /// <summary>
    /// Plain text with ANSI SGR colour, switched to MXP when the connection negotiates it — the default, and
    /// so it means "nobody chose" (F5's <c>auto</c>).
    /// </summary>
    Ansi,

    /// <summary>MXP markup (tags, SEND/A links) over ANSI.</summary>
    Mxp,

    /// <summary>Pueblo HTML-subset markup.</summary>
    Pueblo,

    /// <summary>
    /// ANSI colour and nothing else, chosen on purpose: a negotiated MXP does not switch the parser (F5's
    /// <c>ansi</c>). Appended, because the configuration stores these by name and <see cref="Ansi"/> has to
    /// keep meaning what every saved world already means by it.
    /// </summary>
    AnsiOnly,
}

/// <summary>Emoji/emoticon substitution options.</summary>
public sealed class EmojiSettings
{
    public bool Enabled { get; set; }

    public bool Emoticons { get; set; } = true;

    public bool Shortcodes { get; set; } = true;
}

/// <summary>
/// The mapper's per-world text patterns, for games that send neither GMCP room data nor MXP room tags. Both
/// are regular expressions; empty means the text source is off.
/// </summary>
public sealed class MapSettings
{
    /// <summary>
    /// Matches the line that names a room. The <c>name</c> group, when there is one, is the name; otherwise the
    /// whole match is.
    /// </summary>
    public string? RoomNamePattern { get; set; }

    /// <summary>
    /// Matches the line that lists a room's exits, and so ends the room. The <c>exits</c> group holds the list,
    /// split on spaces, commas and <c>and</c>.
    /// </summary>
    public string? ExitsPattern { get; set; }

    /// <summary>
    /// Matches a line saying a step of a speedwalk did not happen (<c>You can't go that way.</c>), which stops
    /// the walk at once rather than after its step timeout. Null leaves the timeout as the only stop.
    /// </summary>
    public string? WalkFailurePattern { get; set; }

    public MapSettings Clone() =>
        new() { RoomNamePattern = RoomNamePattern, ExitsPattern = ExitsPattern, WalkFailurePattern = WalkFailurePattern };
}

/// <summary>Per-character logging configuration.</summary>
public sealed class LoggingSettings
{
    public LogFormat Format { get; set; } = LogFormat.None;

    /// <summary>Directory for log files. Defaults to a per-session folder under the config dir.</summary>
    public string? Directory { get; set; }

    /// <summary>
    /// Whether this character's panes come back with their previous session's content after a restart
    /// (<see cref="SharpMUTerm.Core.Text.RestoreLog"/>). On by default, and the per-character opt-out for
    /// somebody who does not want one world's text on disk between sessions.
    /// <para>
    /// It sits beside <see cref="Format"/> because F9 is where a character's "what of mine is written
    /// down" questions are answered, and it is a separate switch from it because the two settings are
    /// different things: a transcript is a file <em>you</em> keep, read and choose a format for, and this
    /// is a small bounded tail nothing but the client's own startup ever reads. Turning one off has never
    /// implied anything about the other.
    /// </para>
    /// <para>
    /// Clearing it stops the writing <em>and</em> drops whatever is already stored for that character's
    /// windows on the next launch — an opt-out that left the last session's text lying there would be
    /// answering a different question from the one it was asked.
    /// </para>
    /// </summary>
    public bool RestoreLog { get; set; } = true;
}

/// <summary>
/// A saved MU* world — i.e. a <b>server</b> (host/port/TLS/encoding + rendering options) that
/// holds zero or more <see cref="CharacterDefinition"/>s. A character is the connection unit;
/// automation lives in world-independent <see cref="TriggerSet"/>s on <see cref="AppConfiguration"/>.
/// A world with no characters is valid — it simply cannot connect.
/// </summary>
public sealed class WorldDefinition
{
    public string Name { get; set; } = "New World";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 4000;

    public bool UseTls { get; set; }

    public bool AllowInvalidCertificates { get; set; }

    /// <summary>Echo typed commands locally into the output window.</summary>
    public bool LocalEcho { get; set; } = true;

    /// <summary>
    /// This world's text encoding: <c>auto</c> (the default) to follow CHARSET negotiation, or a named
    /// encoding (e.g. <c>ISO-8859-1</c>) to <em>override</em> it.
    /// <para>
    /// An override is used for decoding whatever the server proposes, and is still offered at the head
    /// of the negotiation order so a cooperative server agrees rather than diverging. Under
    /// <c>auto</c> the session states the app-wide
    /// <see cref="AppConfiguration.CharsetOrder"/> and uses whatever the server settles on — or, when
    /// the server never implements RFC 2066, the head of that order.
    /// </para>
    /// </summary>
    public string Encoding { get; set; } = SharpMUTerm.Core.Telnet.TelnetSessionOptions.AutoEncodingName;

    /// <summary>Seconds between keepalive probes (NOP/telnet), or 0 to disable.</summary>
    public int KeepaliveSeconds { get; set; }

    /// <summary>How inbound text is parsed for styling and interactive links.</summary>
    public ContentFormat ContentFormat { get; set; } = ContentFormat.Ansi;

    /// <summary>Emoji/emoticon substitution for inbound text.</summary>
    public EmojiSettings Emoji { get; set; } = new();

    /// <summary>
    /// GMCP modules to ask this world for beyond the client's defaults, each <c>Name Version</c>
    /// (<c>IRE.Rift 1</c>) — sent in <c>Core.Supports.Set</c> once the server's GMCP is agreed.
    /// </summary>
    public List<string> GmcpModules { get; set; } = new();

    /// <summary>
    /// Whether to log in over GMCP's <c>Char.Login</c> on a server that offers it, rather than by sending the
    /// connect line. Off by default. On, the client asks for <c>Char.Login 1</c> and waits a few seconds for the
    /// server's <c>Char.Login.Default</c>; a server that offers <c>password-credentials</c> is sent the
    /// character's name and saved password, and any other answer, or none, gets the connect line as before.
    /// </summary>
    public bool GmcpLogin { get; set; }

    /// <summary>
    /// Whether this world may show what you are doing on your Discord profile, through GMCP's
    /// <c>External.Discord</c>. Off by default. On, the world is asked for <c>External.Discord 1</c> and what it
    /// sends goes to the Discord app on this computer while one of its characters' windows is in front.
    /// </summary>
    public bool DiscordPresence { get; set; }

    /// <summary>How the mapper recognises a room on a game that sends no room data (see <see cref="MapSettings"/>).</summary>
    public MapSettings Map { get; set; } = new();

    /// <summary>
    /// Whether the client may download the sound files this world asks for (MCMP, MSP). Off still plays what is
    /// already cached or in a soundpack folder; it only stops new fetches.
    /// </summary>
    public bool DownloadMedia { get; set; } = true;

    /// <summary>
    /// The prefix a MUSH starts a line with when it hands text back to edit — AresMUSH's <c>FugueEdit &gt;</c> —
    /// or null for none, the default. Such a line is not shown: its text is put on the command line, and the
    /// player sends it. Never sent by the client, because another player can page a line that starts with it.
    /// </summary>
    public string? GrabPrefix { get; set; }

    /// <summary>
    /// The world's accent colour, used to keep its windows traceable to their owner once they
    /// scatter across panes. Default resolves to a theme-derived accent.
    /// </summary>
    public TerminalColor Accent { get; set; } = TerminalColor.Default;

    /// <summary>The characters that can connect to this world.</summary>
    public List<CharacterDefinition> Characters { get; set; } = new();

    /// <summary>
    /// A deep copy of this world, characters and all. Same contract as
    /// <see cref="CharacterDefinition.Clone"/>: nothing mutable is shared with the original, so a
    /// duplicated world can be renamed and re-pointed without dragging its source along.
    /// </summary>
    public WorldDefinition Clone() => new()
    {
        Name = Name,
        Host = Host,
        Port = Port,
        UseTls = UseTls,
        AllowInvalidCertificates = AllowInvalidCertificates,
        LocalEcho = LocalEcho,
        Encoding = Encoding,
        KeepaliveSeconds = KeepaliveSeconds,
        ContentFormat = ContentFormat,
        Emoji = new EmojiSettings
        {
            Enabled = Emoji.Enabled,
            Emoticons = Emoji.Emoticons,
            Shortcodes = Emoji.Shortcodes,
        },
        Accent = Accent,
        GmcpModules = GmcpModules.ToList(),
        Map = Map.Clone(),
        DownloadMedia = DownloadMedia,
        GrabPrefix = GrabPrefix,
        GmcpLogin = GmcpLogin,
        DiscordPresence = DiscordPresence,
        Characters = Characters.Select(c => c.Clone()).ToList(),
    };

    /// <summary>Builds the transport-level options from this world.</summary>
    public ConnectionOptions ToConnectionOptions() => new()
    {
        Host = Host,
        Port = Port,
        UseTls = UseTls,
        AllowInvalidCertificates = AllowInvalidCertificates,
    };
}
