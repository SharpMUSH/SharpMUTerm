namespace SharpMUTerm.Core.Commands;

/// <summary>A character the command surface can switch to, with enough detail to label the entry.</summary>
/// <param name="Connected">Whether its socket is up.</param>
/// <param name="Open">
/// Whether the client has a session for it in this run — which is a different question from
/// <paramref name="Connected"/>: a character you have switched to and then disconnected is open and
/// offline. It is what <see cref="CommandCatalog.CharacterCycle"/> filters on, because the cycle keys
/// may not <em>create</em> anything.
/// </param>
public sealed record CharacterRef(
    string WorldName,
    string CharacterName,
    string SessionKey,
    bool Connected,
    bool Open = false,
    bool InGroup = false);

/// <summary>Live flags the catalog reads so stateful commands show their current value.</summary>
/// <param name="LoggingOn">Whether the focused character is logging.</param>
/// <param name="Zoomed">Whether a pane is zoomed.</param>
/// <param name="Frozen">Whether the focused pane's scrollback is frozen.</param>
/// <param name="TimestampsOn">Whether output lines carry timestamps.</param>
/// <param name="SecondInputOn">
/// Whether the active window is showing its second command line. Per window, not per app — the
/// surface acts on the window you are in, the same way <c>Clear window</c> does.
/// </param>
/// <param name="ScrolledBack">
/// Whether the focused pane is showing something other than its newest output, so
/// <c>Back to live output</c> has somewhere to go.
/// </param>
/// <param name="NewlineChord">
/// The chord that inserts a newline where the client is running — <c>Alt+⏎</c> in a terminal, <c>Ctrl+⏎</c> in
/// the desktop window, which keeps ⌥⏎ for itself. Supplied by the host for the reason the screens are.
/// </param>
/// <param name="ScriptsOn">
/// Whether this client runs Lua at all, so <c>Reload scripts</c> is offered only where it can do something.
/// </param>
/// <param name="Walking">Whether the focused connection is speedwalking, so <c>Stop walking</c> has a walk to stop.</param>
/// <param name="Reconnecting">Whether a dropped connection is waiting to dial again, so <c>Stop reconnecting</c> has one to stop.</param>
/// <param name="Sound">
/// Null where the client cannot play sound, so no mute is offered; otherwise whether sound is playing
/// (offer <c>Mute sound</c>) or muted (offer <c>Unmute sound</c>).
/// </param>
/// <param name="Announced">
/// Null where the host has no screen reader to tell; otherwise whether the active window's output is read aloud.
/// </param>
/// <param name="OutputSelected">Whether some pane's output is selected, so a trigger can be made from it.</param>
/// <param name="KeyClashes">
/// How many F4 bindings share a chord with the client or with each other, so <c>Key clashes (N)</c> is offered
/// only when there is one to show.
/// </param>
/// <param name="Floats">
/// Null where the host cannot float a panel in a window of its own (the terminal), so nothing is offered; otherwise
/// every panel that can pop out, docked or floating, so <c>Pop out …</c> and <c>Dock … back</c> name real windows.
/// </param>
/// <param name="RaggedWraps">
/// The worlds whose connected pane is narrower than the column their server wraps at, and which do not join wraps — so
/// <c>Join server wraps for …</c> is offered (and suggested) only while it would change what the pane shows.
/// </param>
/// <param name="SceneLogging">Whether the focused window is being recorded as a scene, so ⌃P offers to stop it.</param>
/// <param name="NoDictionary">
/// Whether the client's own dictionaries folder holds none, so <c>Get a spelling dictionary</c> is offered.
/// </param>
/// <param name="MapUndo">What the focused character's last map edit was, so <c>Undo map edit</c> is offered only with one.</param>
/// <param name="MapLevels">How many levels the area the focused character's map shows has (0 with no map).</param>
/// <param name="MapAreas">How many named areas that map has.</param>
/// <param name="MapViewing">Whether that map shows somewhere other than where the character is.</param>
/// <param name="NewConfiguration">
/// Whether the configuration is new — no world yet, or the key tour still owed — so Help, Welcome and Import are suggested.
/// </param>
/// <param name="MapFound">The rooms the last <c>/map find</c> listed, newest search only, so ⌃P offers to walk to each.</param>
/// <param name="ScriptFiles">The focused character's script files, so ⌃P offers to open each in the editor.</param>
/// <param name="RecentAlerts">How many pages and alerts this run has kept, across every character, so ⌃P offers <c>Recent alerts</c>.</param>
/// <param name="Alerts">
/// Those alerts, newest first, once <c>Recent alerts</c> has opened the palette on them (null before), so each is an entry
/// that goes to its character's window without crowding every other ⌃P.
/// </param>
/// <param name="WindowMode">
/// How the desktop window covers the screen, so ⌃P offers full screen and windowed full screen (and leaving them); null
/// in a host with no window to size (the terminal).
/// </param>
/// <param name="Update">Where this copy's update stands; null in a host that cannot update itself.</param>
/// <param name="TextSizePage">Whether the host has the Text size &amp; zoom page (the window, not <c>--emulated</c>).</param>
/// <param name="TextZoom">
/// The desktop window's zoom, a percentage, so ⌃P offers zooming in, out and back; null in a host that draws no text of
/// its own (the terminal, whose font size is its emulator's).
/// </param>
public sealed record CommandContext(
    bool LoggingOn = false,
    bool Zoomed = false,
    bool Frozen = false,
    bool TimestampsOn = false,
    bool SecondInputOn = false,
    bool ScrolledBack = false,
    string NewlineChord = "Alt+⏎",
    bool ScriptsOn = false,
    bool Walking = false,
    bool Reconnecting = false,
    bool? Sound = null,
    bool? Announced = null,
    bool OutputSelected = false,
    int KeyClashes = 0,
    IReadOnlyList<PanelFloatRef>? Floats = null,
    IReadOnlyList<RaggedWrap>? RaggedWraps = null,
    bool NoDictionary = false,
    string? MapUndo = null,
    bool SceneLogging = false,
    int MapLevels = 0,
    int MapAreas = 0,
    bool MapViewing = false,
    bool NewConfiguration = false,
    IReadOnlyList<FoundRoom>? MapFound = null,
    IReadOnlyList<ScriptFileRef>? ScriptFiles = null,
    int RecentAlerts = 0,
    IReadOnlyList<AlertRef>? Alerts = null,
    Configuration.WindowMode? WindowMode = null,
    UpdateState? Update = null,
    int? TextZoom = null,
    bool TextSizePage = false);

/// <summary>
/// Where this copy's update stands, for ⌃P's update entries: null when the host cannot update itself at all (a build
/// run from a folder, the snapshot, the tests).
/// </summary>
/// <param name="Version">The newer release found, or null while none has been.</param>
/// <param name="Ready">Downloaded, so restarting puts it in place.</param>
/// <param name="Busy">A check or a download is running now.</param>
public sealed record UpdateState(string? Version, bool Ready, bool Busy);

/// <summary>A page or alert the client kept this run, for ⌃P's <c>Recent alerts</c> entries.</summary>
/// <param name="Id">Its number this run, which the entry's id carries.</param>
/// <param name="Who">The character it was for, as every surface names one (<c>Corvid · Aetherfall</c>).</param>
/// <param name="Why">What it was: <c>page</c>, <c>mentioned you</c> or <c>alert</c>.</param>
/// <param name="Text">The line, bounded.</param>
/// <param name="Age">How long ago, in words (<c>just now</c>, <c>4 min ago</c>).</param>
public sealed record AlertRef(int Id, string Who, string Why, string Text, string Age);

/// <summary>A script file ⌃P offers to open in the editor.</summary>
/// <param name="Name">Its name under the scripts folder (<c>combat.lua</c>), or <c>package:&lt;id&gt;/&lt;file&gt;</c>.</param>
/// <param name="Detail">What the entry says about it: where it is, whether it loaded, its last error's line.</param>
/// <param name="ReadOnly">Whether it can only be read (a package's script).</param>
public sealed record ScriptFileRef(string Name, string Detail, bool ReadOnly);

/// <summary>A room the last <c>/map find</c> listed, for the command surface's <c>Walk to …</c> entries.</summary>
/// <param name="Id">The room's id on the map.</param>
/// <param name="Name">Its name.</param>
/// <param name="Area">Its area's name, or null.</param>
/// <param name="Level">Its level (z).</param>
public sealed record FoundRoom(long Id, string Name, string? Area, int Level);

/// <summary>A world whose pane is narrower than its server's wrap column.</summary>
/// <param name="World">The world's name.</param>
/// <param name="Column">The column its server wraps at.</param>
/// <param name="PaneWidth">How wide its pane is.</param>
public sealed record RaggedWrap(string World, int Column, int PaneWidth);

/// <summary>A panel window that can pop out into a window of its own, for the command surface's entries.</summary>
/// <param name="WindowId">The panel's window (<c>map:World.Char</c>, <c>panel:vitals:World.Char</c>).</param>
/// <param name="Title">What the panel is called (<c>Map</c>, <c>Vitals</c>).</param>
/// <param name="Character">Whose panel it is, as the entry's detail.</param>
/// <param name="Floating">Whether it is popped out now: offered to dock back, else to pop out.</param>
public sealed record PanelFloatRef(string WindowId, string Title, string Character, bool Floating);

/// <summary>
/// A configuration screen the command surface can open: what it calls itself, the id the host
/// dispatches on, and the keyboard shortcut that opens it directly. Supplied by the host rather
/// than known here, because which screens exist — and which key each is bound to — is the UI's
/// business, and a catalog that guessed could advertise a key nothing is registered on.
/// </summary>
public sealed record SettingsEntry(string Title, string Id, string Shortcut);

/// <summary>
/// What the command surface's catalog shares with the shell outside it. The catalog itself is no longer
/// built here: its entries are contributions in <c>SharpMUTerm.Client</c>'s registry
/// (<c>BuiltInCommands</c>), so a package or a host can add to it. The character cycle stays here because the
/// shell's ⌥J/⌥K and the rail read it too.
/// </summary>
public static class CommandCatalog
{
    /// <summary>
    /// <b>The characters ⌥J and ⌥K walk, in the order the connection rail draws them.</b> The one
    /// definition of the cycle, read by the chord, by the rail's <c>⌥J</c>/<c>⌥K</c> column and by the ⌃P
    /// entries' subtitles, so a key and the label that advertises it cannot come to disagree.
    /// <para>
    /// <b>Only the characters that are already open.</b> Switching to a character the client has never
    /// opened <em>creates</em> a session and a window (the shell's <c>SwitchToCharacter</c>), and a cycle
    /// key that opened a session per press would dial through a configuration by accident — the user
    /// asked for a way to move between the characters they are using, not a way to start all of them.
    /// The ones you have not opened are still one click away in the rail and one entry away in ⌃P, and
    /// both of those are gestures that <em>mean</em> "open it".
    /// </para>
    /// <para>
    /// Configuration order, not the order they were opened, because that is the order the sidebar lists
    /// them in and the sidebar is where the cycle is read off. An order the rail did not draw would make
    /// "the row below me" and "the next character" two different things.
    /// </para>
    /// </summary>
    public static List<CharacterRef> CharacterCycle(IReadOnlyList<CharacterRef> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        return characters.Where(c => c.Open).ToList();
    }
}
