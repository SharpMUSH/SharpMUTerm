using System.Text.Json.Nodes;

namespace SharpMUTerm.Scripting;

/// <summary>
/// The narrow surface a <see cref="ScriptHost"/> uses to talk to a world. Abstracted so the host
/// can be unit-tested without a live connection: production wires it to a real
/// <c>SharpMUTerm.Core.Session.WorldSession</c> via <see cref="WorldSessionScriptBridge"/>, while
/// tests supply a fake that captures the calls.
/// </summary>
public interface IScriptWorld
{
    /// <summary>Queues or sends a command to the server.</summary>
    void Send(string command);

    /// <summary>Writes a line of text to the client output.</summary>
    void Print(string text);

    /// <summary>
    /// Registers a script-backed trigger. When the pattern matches, the world is expected to
    /// deliver the fire back to the host (see <see cref="ScriptHost.DispatchTrigger"/>) using the
    /// same <paramref name="callbackId"/>.
    /// </summary>
    void AddTrigger(string pattern, string callbackId);

    /// <summary>
    /// Registers an alias. When <paramref name="substitution"/> is non-empty the world expands it
    /// directly; a non-empty <paramref name="callbackId"/> marks the alias as script-backed.
    /// </summary>
    void AddAlias(string pattern, string substitution, string callbackId);

    /// <summary>Schedules a recurring callback; disposing the handle cancels it.</summary>
    IDisposable ScheduleEvery(TimeSpan interval, Action callback);

    /// <summary>Schedules a one-shot callback; disposing the handle cancels it.</summary>
    IDisposable ScheduleAfter(TimeSpan delay, Action callback);

    /// <summary>
    /// The GMCP or MSDP data at a dotted path (<c>Char.Vitals.hp</c>, <c>MSDP.HEALTH</c>), or null — what
    /// <c>gmcp.get</c> and <c>msdp.get</c> read.
    /// </summary>
    JsonNode? GameData(string path);

    /// <summary>Sends a GMCP message; <paramref name="json"/> is the body, empty for none.</summary>
    void SendGmcp(string package, string json);

    /// <summary>The room the map says the character is in, for <c>map.current()</c>; null with no map or no room.</summary>
    ScriptRoom? CurrentRoom();

    /// <summary>
    /// The commands that walk from the current room to <paramref name="target"/> (a room number or name), for
    /// <c>map.path()</c>; null when the map knows no way.
    /// </summary>
    IReadOnlyList<string>? PathTo(string target);

    /// <summary>Starts a speedwalk to <paramref name="target"/>, for <c>map.walk()</c>: null, or why it did not start.</summary>
    string? WalkTo(string target);

    /// <summary>The display name of the world, exposed to scripts as <c>world.name</c>.</summary>
    string WorldName { get; }
}

/// <summary>A room as a script sees it.</summary>
public sealed record ScriptRoom(long Id, string Name, string? Area);
