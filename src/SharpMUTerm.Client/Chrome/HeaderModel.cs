using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Client.Chrome;

/// <summary>
/// The header band as data: what it says, with no presentation in it. The terminal draws it as one row of cells (a
/// powerline ribbon on the left, counts on the right, <c>SharpMUTermApp.HeaderMarkup</c>); the window draws a native
/// header over that row from the same instance. Both are built from one <see cref="HeaderModel"/> per frame, so the
/// two cannot disagree about which world is in front, how many characters are connected or whether a log is open.
/// </summary>
/// <param name="MenuOpen">Whether the command surface (⌃P) is up — the ribbon's caret flips while it is.</param>
/// <param name="World">The world of the character in front, or null when nothing is.</param>
/// <param name="Character">The character in front, or null when the key in front names a world alone, or nothing.</param>
/// <param name="Connected">Characters with a live connection, the numerator of the header's fraction.</param>
/// <param name="Configured">Characters configured, its denominator.</param>
/// <param name="HasWorlds">Whether any world is configured. With none the fraction is not drawn at all.</param>
/// <param name="Log">The transcript format in force, <see cref="LogFormat.None"/> when nothing is logged.</param>
/// <param name="PrefixArmed">Whether ⌃B is armed: the cell header then shows the which-key strip in place of the counts.</param>
public sealed record HeaderModel(
    bool MenuOpen,
    HeaderWorld? World,
    HeaderCharacter? Character,
    int Connected,
    int Configured,
    bool HasWorlds,
    LogFormat Log,
    bool PrefixArmed)
{
    /// <summary>
    /// The header's own click target for the command surface: what the ribbon's <c>☰</c> carries, and what the
    /// window's search box sends back. A chrome target, never a server's — only the header's own handler reads it.
    /// </summary>
    public const string PaletteTarget = "sharpmuterm-menu:toggle";

    /// <summary>The chord that opens the command surface, in the symbol vocabulary the catalog writes keys in.</summary>
    public const string PaletteKey = "⌃P";

    /// <summary>Nothing in front, nothing configured: the header of a client with no worlds.</summary>
    public static HeaderModel Empty { get; } = new(false, null, null, 0, 0, false, LogFormat.None, false);

    /// <summary>The fraction as the header words it, or empty when there are no worlds to count.</summary>
    public string ConnectedText => HasWorlds ? $"{Connected}/{Configured} characters" : string.Empty;

    /// <summary>The log cell as the header words it.</summary>
    public string LogText => Log == LogFormat.None ? "LOG off" : $"LOG {Log.ToString().ToLowerInvariant()}";

    /// <summary>
    /// While ⌃B is armed, what the next key does — the which-key keymap the cell header's strip abbreviates and the
    /// panel lists in full; null otherwise. A host drawing its own header shows this in place of the search box.
    /// </summary>
    public HeaderPrefix? Prefix { get; init; }
}

/// <summary>The ⌃B keymap as the header shows it while the prefix is armed.</summary>
/// <param name="Keys">Every key the prefix answers, in the panel's order.</param>
/// <param name="Exit">How the prefix is left, as every surface words it.</param>
public sealed record HeaderPrefix(IReadOnlyList<HeaderPrefixKey> Keys, string Exit)
{
    /// <summary>The chord that arms the prefix.</summary>
    public const string Chord = "⌃B";

    /// <summary>Value equality over the keys, which a record's list member does not give.</summary>
    public bool Equals(HeaderPrefix? other) =>
        other is not null && Exit == other.Exit && Keys.SequenceEqual(other.Keys);

    public override int GetHashCode() => HashCode.Combine(Exit, Keys.Count);
}

/// <summary>One key of the ⌃B keymap.</summary>
/// <param name="Keys">The key, or keys, as the panel writes them (<c>|</c>, <c>1–9</c>, <c>&lt; &gt;  ← →</c>).</param>
/// <param name="Title">What it does.</param>
/// <param name="Blocked">Why it cannot run now, or null when it can.</param>
public sealed record HeaderPrefixKey(string Keys, string Title, string? Blocked)
{
    /// <summary>Whether the key does something now.</summary>
    public bool Available => Blocked is null;
}

/// <summary>The world in front, as the header names it.</summary>
/// <param name="Name">The world's name.</param>
/// <param name="Accent">Its accent: the colour the world wears on the rail and in the ribbon.</param>
public sealed record HeaderWorld(string Name, TerminalColor Accent);

/// <summary>
/// The character in front, as the header names it: who, where, and whether the connection is up. The ribbon names
/// only the character; the window's pill says the rest (<c>world · TLS</c>, a presence dot).
/// </summary>
/// <param name="Name">The character's name.</param>
/// <param name="SessionKey">Its <c>World.Character</c> key, which its rail row's target is built from.</param>
/// <param name="Presence">Whether its connection is up, being made, or down.</param>
/// <param name="Host">The world's host.</param>
/// <param name="Port">The world's port.</param>
/// <param name="Tls">Whether the world is configured for TLS.</param>
/// <param name="Target">Where a click on the character goes: its rail row's own target.</param>
public sealed record HeaderCharacter(
    string Name, string SessionKey, CharacterPresence Presence, string Host, int Port, bool Tls, string Target)
{
    /// <summary>The second line of the window's pill: the world, and how the connection is carried.</summary>
    public string Detail(string world) => Tls ? $"{world} · TLS" : world;
}

/// <summary>Whether a character's connection is up, as every view of it says it.</summary>
public enum CharacterPresence
{
    /// <summary>No connection, or one that failed.</summary>
    Offline,

    /// <summary>Being dialled.</summary>
    Connecting,

    /// <summary>Up.</summary>
    Online,
}

/// <summary>Maps a session's state to a <see cref="CharacterPresence"/>.</summary>
public static class PresenceOf
{
    /// <summary>The presence a session in <paramref name="state"/> shows; null (no session) is offline.</summary>
    public static CharacterPresence State(ConnectionState? state) => state switch
    {
        ConnectionState.Connected => CharacterPresence.Online,
        ConnectionState.Connecting => CharacterPresence.Connecting,
        _ => CharacterPresence.Offline,
    };
}
