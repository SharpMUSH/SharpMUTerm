using System.Text.Json.Nodes;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Scripting;

namespace SharpMUTerm.Tui;

/// <summary>
/// The script console (⌃P ▸ <em>Script console</em>): one per character, holding what its scripts printed and the errors
/// they raised (with <c>file:line</c>), a one-line Lua prompt run in that character's own sandbox, what was typed there,
/// and a read-only view of its <c>store.*</c> with delete. One Client model (<see cref="ScriptConsoleModel"/>) drawn as a
/// cell <see cref="ListOverlay"/> here and as a native modal in a host that has them; every gesture ends in
/// <see cref="RunConsoleLine"/>, <see cref="DeleteStoreKey"/> or <see cref="ReloadConsoleScripts"/>.
/// <para>
/// <b>A line runs on the UI thread, through the host's lock, and is time-limited</b> (<see cref="ScriptHost.EvaluateConsole"/>:
/// two million instructions or half a second), so a loop typed at the prompt cannot hang the screen. The prompt exists
/// only where scripts can run — a client with a scripts root, whose folder exists, and a window with a character —
/// and otherwise says which of those is missing.
/// </para>
/// <para>
/// <b>The console's lines are in memory for the run</b>, per character (<see cref="ScriptConsoleLog"/>), fed from
/// <see cref="SessionScripts.Printed"/> and <see cref="ReportScript"/> on whichever thread a script ran; nothing is saved.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Each character's console lines and prompt history, by session key.</summary>
    private readonly Dictionary<string, ScriptConsoleLog> _consoleLogs = new(StringComparer.Ordinal);

    /// <summary>The cell console while it is open; null otherwise.</summary>
    private ScriptConsoleSurface? _console;

    /// <summary>The character the open console (either host) belongs to.</summary>
    private string? _consoleKey;

    /// <summary>A console repaint is already queued, so a script printing a thousand lines queues one.</summary>
    private int _consoleRefreshQueued;

    /// <summary>The demo's sandbox, so the snapshot's console has a prompt that runs.</summary>
    private (ScriptHost Host, ScriptStore Store)? _demoScripts;

    /// <summary>Whether the script console is open, in cells or natively.</summary>
    internal bool ScriptConsoleIsOpen =>
        (_helpView.IsOpen && _helpView.Mode == "script-console") || NativeModalOf(SurfaceCatalog.ScriptConsole) is not null;

    /// <summary>The cell console's state, for a test; null while it is shut.</summary>
    internal ScriptConsoleSurface? CellConsole => _helpView.IsOpen && _helpView.Mode == "script-console" ? _console : null;

    /// <summary>The native console's session while one is open, for the tests.</summary>
    internal ScriptConsoleSession? NativeConsole => _nativeModal as ScriptConsoleSession;

    /// <summary>What the cell console shows, for a test.</summary>
    internal IReadOnlyList<string> ConsoleLines => _helpView.Mode == "script-console" ? _helpView.Lines : [];

    /// <summary>Feeds a key to the cell console.</summary>
    internal void SimulateConsoleKey(ConsoleKeyInfo key) => _helpView.SimulateKey(key);

    /// <summary>Types <paramref name="text"/> at the cell console's prompt.</summary>
    internal void SimulateConsoleTyping(string text)
    {
        foreach (var c in text)
        {
            SimulateConsoleKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
    }

    /// <summary>A character's console log, made on first use.</summary>
    private ScriptConsoleLog ConsoleLog(string key)
    {
        lock (_consoleLogs)
        {
            if (!_consoleLogs.TryGetValue(key, out var log))
            {
                log = new ScriptConsoleLog();
                log.Changed += () => QueueConsoleRefresh(key);
                _consoleLogs[key] = log;
            }

            return log;
        }
    }

    /// <summary>What the console acts on: a character's scripts, the demo's sandbox, or why there is nothing.</summary>
    private sealed record ConsoleTarget(
        string Key,
        string Title,
        Func<string, ConsoleResult> Evaluate,
        ScriptStore Store,
        IReadOnlyList<string> Files,
        int Loaded,
        Func<string> Reload);

    /// <summary>The focused window's character's scripts, or why there are none to talk to.</summary>
    private (ConsoleTarget? Target, string Key, string Title, string? Why) ConsoleTargetNow()
    {
        var session = SendTarget();
        if (session is null && _workspace.FindWindow(ActiveWindowId())?.SessionKey is { } owner && IsDemoKey(owner))
        {
            var (host, store) = _demoScripts ??= NewDemoScripts();
            return (new ConsoleTarget("demo", "Corvid", line => host.EvaluateConsole(line), store, ["corvid.lua"], 1, () => "1 script loaded"),
                "demo", "Corvid", null);
        }

        if (session is null)
        {
            return (null, string.Empty, string.Empty, "this window has no character — pick one first (⌃P ▸ Switch to …)");
        }

        var title = SessionTitle(session);
        if (_scriptsRoot is null || !_scripts.TryGetValue(session, out var scripts))
        {
            return (null, session.SessionKey, title, "this client runs no scripts");
        }

        if (!Directory.Exists(_scriptsRoot))
        {
            return (null, session.SessionKey, title, $"there is no scripts folder yet — create {_scriptsRoot} and put .lua files in it");
        }

        return (new ConsoleTarget(
                session.SessionKey,
                title,
                scripts.Evaluate,
                scripts.Store,
                scripts.Files,
                scripts.LoadedCount,
                () =>
                {
                    scripts.Reload();
                    return ScriptsLoaded(scripts);
                }),
            session.SessionKey, title, null);
    }

    /// <summary>The console's model for the focused window's character, as it stands now.</summary>
    internal ScriptConsoleModel ScriptConsoleModelNow()
    {
        var (target, key, title, why) = ConsoleTargetNow();
        var log = key.Length == 0 ? null : ConsoleLog(key);
        var store = new List<ScriptStoreEntry>();
        var count = 0;
        if (target is not null)
        {
            var keys = target.Store.Keys;
            count = keys.Count;
            foreach (var name in keys.Take(ScriptConsoleModel.MaxStoreRows))
            {
                var value = target.Store.Get(name)?.ToJsonString() ?? "null";
                store.Add(new ScriptStoreEntry(name, value.Length > ScriptConsoleModel.MaxValueLength
                    ? value[..(ScriptConsoleModel.MaxValueLength - 1)] + "…"
                    : value));
            }
        }

        return new ScriptConsoleModel(
            title,
            why,
            log?.Lines ?? [],
            log?.History ?? [],
            store,
            count,
            target?.Files ?? [],
            target?.Loaded ?? 0,
            _scriptsRoot ?? (target is not null ? "scripts/" : null));
    }

    /// <summary>⌃P ▸ Script console: opens the focused window's character's console, or closes it.</summary>
    private void ShowScriptConsole()
    {
        if (NativeModalOf(SurfaceCatalog.ScriptConsole) is { } native)
        {
            CloseNativeModal(native);
            return;
        }

        if (ScriptConsoleIsOpen)
        {
            _helpView.Close();
            return;
        }

        if (ComposerIsInTheWay("the script console") || (AnyOverlayOpen && Refuse($"close {OpenOverlayName()} first")))
        {
            return;
        }

        var model = ScriptConsoleModelNow();
        _consoleKey = ConsoleTargetNow().Key;
        if (NativeModalsHost is { } host)
        {
            ScriptConsoleSession? session = null;
            session = new ScriptConsoleSession(
                model,
                ThemeIsDark(),
                line => OnUiThread(() => RunConsoleLine(line)),
                key => OnUiThread(() => DeleteStoreKey(key)),
                () => OnUiThread(ReloadConsoleScripts),
                () => OnUiThread(() =>
                {
                    NativeModalClosed(session!);
                    _consoleKey = null;
                }))
            {
                Theme = _theme.Clone(),
            };
            ShowNativeModal(host, session);
            return;
        }

        var console = new ScriptConsoleSurface(model);
        _console = console;
        var rows = Math.Clamp(_system.DesktopDimensions.Height - 6, 10, 30);
        _helpView.Open(
            "script-console",
            "Script console",
            width =>
            {
                Drawing(SurfaceCatalog.ScriptConsole, string.Empty, console.Model);
                return console.Render(width, rows);
            },
            ConsoleKeyPressed,
            width: ScriptConsoleSurface.PreferredWidth,
            closed: () =>
            {
                _console = null;
                _consoleKey = null;
            });
    }

    /// <summary>A key in the cell console.</summary>
    private bool ConsoleKeyPressed(ConsoleKeyInfo key)
    {
        if (_console is not { } console)
        {
            return false;
        }

        switch (console.Key(key))
        {
            case ScriptConsoleSurface.Ask.Run:
                var line = console.Input;
                console.Ran();
                RunConsoleLine(line);
                break;
            case ScriptConsoleSurface.Ask.Delete when console.PickedKey is { } picked:
                DeleteStoreKey(picked);
                break;
            case ScriptConsoleSurface.Ask.Reload:
                ReloadConsoleScripts();
                break;
        }

        return true;
    }

    /// <summary>
    /// The one place a console line is run, from either host: written to the console, evaluated in the character's
    /// sandbox under its lock with a limit, and its value or error written after whatever it printed.
    /// </summary>
    internal void RunConsoleLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var (target, _, _, why) = ConsoleTargetNow();
        if (target is null)
        {
            RefuseCommand(why ?? "no scripts to run here");
            return;
        }

        var log = ConsoleLog(target.Key);
        log.Remember(line);
        log.Add(ScriptConsoleKind.Input, line);
        var result = target.Evaluate(line);
        if (result.Error is { } error)
        {
            log.Add(ScriptConsoleKind.Error, error);
        }
        else if (result.Value is { } value)
        {
            log.Add(ScriptConsoleKind.Result, value);
        }

        RefreshConsole();
    }

    /// <summary>Deletes a <c>store.*</c> key of the focused window's character, from either host.</summary>
    internal void DeleteStoreKey(string key)
    {
        var (target, _, _, why) = ConsoleTargetNow();
        if (target is null)
        {
            RefuseCommand(why ?? "no store here");
            return;
        }

        if (target.Store.Set(key, (JsonNode?)null) is { } refusal)
        {
            RefuseCommand(refusal);
            return;
        }

        ConsoleLog(target.Key).Add(ScriptConsoleKind.Note, $"deleted store key {key}");
        RefreshConsole();
    }

    /// <summary>Reloads the focused window's character's scripts from the console, from either host.</summary>
    internal void ReloadConsoleScripts()
    {
        var (target, _, _, why) = ConsoleTargetNow();
        if (target is null)
        {
            RefuseCommand(why ?? "no scripts to reload");
            return;
        }

        var said = target.Reload();
        ConsoleLog(target.Key).Add(ScriptConsoleKind.Note, $"reloaded · {said}");
        RefreshConsole();
    }

    /// <summary>A script printed a line or failed: the character's console keeps a copy. Any thread.</summary>
    private void NoteConsole(WorldSession session, ScriptConsoleKind kind, string text) =>
        ConsoleLog(session.SessionKey).Add(kind, text);

    private void QueueConsoleRefresh(string key)
    {
        if (_consoleKey != key || Interlocked.Exchange(ref _consoleRefreshQueued, 1) == 1)
        {
            return;
        }

        OnUiThread(() =>
        {
            Interlocked.Exchange(ref _consoleRefreshQueued, 0);
            RefreshConsole();
        });
    }

    /// <summary>Redraws whichever console is open from a fresh model.</summary>
    private void RefreshConsole()
    {
        if (!ScriptConsoleIsOpen)
        {
            return;
        }

        var model = ScriptConsoleModelNow();
        if (NativeModalOf(SurfaceCatalog.ScriptConsole) is ScriptConsoleSession native)
        {
            native.Publish(model);
            return;
        }

        if (_console is { } console)
        {
            console.Model = model;
            console.Clamp();
            _helpView.Repaint();
        }
    }

    /// <summary>The demo's sandbox: a host over nothing, a store with a few keys, and a few lines already printed.</summary>
    private (ScriptHost, ScriptStore) NewDemoScripts()
    {
        var log = ConsoleLog("demo");
        var store = new ScriptStore(null);
        var host = new ScriptHost(new DemoScriptWorld(text => log.Add(ScriptConsoleKind.Output, text)), store);
        store.Set("kills", JsonValue.Create(42));
        store.Set("friends", new JsonArray("Ashlyn", "Merrow"));
        store.Set("last_room", JsonValue.Create("The Grand Plaza"));
        log.Add(ScriptConsoleKind.Note, "loaded corvid.lua");
        log.Add(ScriptConsoleKind.Output, "[info] corvid.lua: watching for tells");
        log.Add(ScriptConsoleKind.Error, "corvid.lua:(14,8-12): attempt to index a nil value (field 'vitals')");
        return (host, store);
    }

    /// <summary>What the demo's scripts act on: nothing but the console.</summary>
    private sealed class DemoScriptWorld(Action<string> print) : IScriptWorld
    {
        public string WorldName => "Aetherfall";

        public void Send(string command) => print($"(demo) would send: {command}");

        public void Print(string text) => print(text);

        public void AddTrigger(string pattern, string callbackId)
        {
        }

        public void AddAlias(string pattern, string substitution, string callbackId)
        {
        }

        public IDisposable ScheduleEvery(TimeSpan interval, Action callback) => new Nothing();

        public IDisposable ScheduleAfter(TimeSpan delay, Action callback) => new Nothing();

        public JsonNode? GameData(string path) => null;

        public void SendGmcp(string package, string json)
        {
        }

        public ScriptRoom? CurrentRoom() => new(1, "The Grand Plaza", "Aetherfall");

        public IReadOnlyList<string>? PathTo(string target) => null;

        public string? WalkTo(string target) => "the demo keeps no map";

        private sealed class Nothing : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
