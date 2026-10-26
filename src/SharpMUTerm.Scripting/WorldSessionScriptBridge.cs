using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Scripting;

/// <summary>
/// Adapts a live <see cref="WorldSession"/> to <see cref="IScriptWorld"/> and pumps the session's
/// script-relevant events (trigger fires, GMCP messages) into an attached <see cref="ScriptHost"/>.
/// </summary>
/// <remarks>
/// A rule's callback is either an id <c>trigger.add</c>/<c>alias.add</c> generated, or the name of a global
/// function — the F2 <c>script</c> field. Timers and macros only ever carry a name.
/// </remarks>
public sealed class WorldSessionScriptBridge : IScriptWorld, IDisposable
{
    private readonly WorldSession _session;
    private readonly Func<string, string, string?>? _sendTo;
    private ScriptHost? _host;
    private bool _disposed;

    /// <param name="session">The session the scripts belong to.</param>
    /// <param name="sendTo">What <c>world.sendTo</c> calls; null when this client offers no other characters.</param>
    public WorldSessionScriptBridge(WorldSession session, Func<string, string, string?>? sendTo = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _sendTo = sendTo;
        _session.ScriptRequested += OnScriptRequested;
        _session.GmcpReceived += OnGmcpReceived;
    }

    /// <summary>Constructs a bridge and a host wired to each other for the given session.</summary>
    public static ScriptHost CreateHost(WorldSession session)
    {
        var bridge = new WorldSessionScriptBridge(session);
        var host = new ScriptHost(bridge);
        bridge.Attach(host);
        return host;
    }

    /// <summary>Connects this bridge to the host that will receive dispatched events.</summary>
    public void Attach(ScriptHost host) => _host = host ?? throw new ArgumentNullException(nameof(host));

    public string WorldName => _session.World.Name;

    public void Send(string command) => _ = _session.SendRawAsync(command);

    public string? SendTo(string character, string command) =>
        _sendTo is null ? "this client cannot send to another character" : _sendTo(character, command);

    public void Print(string text) => _session.PrintSystem(text);

    public void AddTrigger(string pattern, string callbackId) =>
        _session.Triggers.Add(new Trigger
        {
            Name = callbackId,
            Pattern = pattern,
            Actions = new TriggerActions { ScriptCallback = callbackId },
        });

    public void AddAlias(string pattern, string substitution, string callbackId) =>
        _session.Aliases.Add(new Alias
        {
            Name = callbackId,
            Pattern = pattern,
            Substitution = substitution,
            ScriptCallback = string.IsNullOrEmpty(callbackId) ? null : callbackId,
        });

    public System.Text.Json.Nodes.JsonNode? GameData(string path) => _session.GameData.Get(path);

    public void SendGmcp(string package, string json) => _ = _session.SendGmcpAsync(package, json);

    public ScriptRoom? CurrentRoom() =>
        _session.Map?.Current is { } room ? new ScriptRoom(room.Id, room.Name, room.Area) : null;

    public IReadOnlyList<string>? PathTo(string target) =>
        _session.Map is { } map && map.Find(target) is { } room
            ? map.PathTo(room.Id)?.Select(step => step.Command).ToArray()
            : null;

    public string? WalkTo(string target) => _session.Map is { } map ? map.WalkTo(target) : "this client keeps no map";

    public IDisposable ScheduleEvery(TimeSpan interval, Action callback) =>
        _session.Scheduler.Every(interval, callback);

    public IDisposable ScheduleAfter(TimeSpan delay, Action callback) =>
        _session.Scheduler.After(delay, callback);

    /// <summary>
    /// Runs the callback a rule asked for. A callback <c>trigger.add</c> or <c>alias.add</c> registered
    /// has a generated id and is found first; anything else is the name of a global function the user's
    /// file defines, which is what F2's <c>script</c> field holds.
    /// </summary>
    private void OnScriptRequested(object? sender, ScriptRequest e)
    {
        if (_host is not { } host)
        {
            return;
        }

        var handled = e.Source switch
        {
            ScriptSource.Trigger => host.DispatchTrigger(e.Callback, e.WholeMatch, e.Groups),
            ScriptSource.Alias => host.DispatchAlias(e.Callback, e.WholeMatch, e.Groups),
            _ => false,
        };

        // A generated id ("trigger#3") belongs to a registration a reload has dropped; it is not a name
        // anyone typed, so it is not reported as a missing function.
        if (!handled && !e.Callback.Contains('#', StringComparison.Ordinal))
        {
            host.DispatchNamed(e.Callback, e.WholeMatch, e.Groups);
        }
    }

    private void OnGmcpReceived(object? sender, GmcpMessageEventArgs e) =>
        _host?.DispatchGmcp(e.Package, e.Json);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.ScriptRequested -= OnScriptRequested;
        _session.GmcpReceived -= OnGmcpReceived;
    }
}
