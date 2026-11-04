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
/// <param name="NoDictionary">
/// Whether the client's own dictionaries folder holds none, so <c>Get a spelling dictionary</c> is offered.
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
    bool NoDictionary = false);

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
