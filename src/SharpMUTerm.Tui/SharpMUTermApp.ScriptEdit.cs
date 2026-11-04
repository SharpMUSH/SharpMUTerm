using System.Globalization;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Scripting;

namespace SharpMUTerm.Tui;

/// <summary>
/// Editing script files inside the client: a loop of list, open, save and reload that never leaves it.
/// <para>
/// <b>The list is ⌃P's</b> (<c>Script &lt;name&gt;</c> entries, <see cref="ScriptFileRefs"/>), opened by ⌃P ▸
/// <em>Edit script…</em>, the script console's ⌃E and <c>/script edit</c>: every <c>.lua</c> under the scripts folder,
/// the names the character's trigger sets give that do not exist yet, and the package scripts those sets name (read
/// only). A new one is <c>/script new &lt;name&gt;</c> (⌃P ▸ <em>New script…</em> puts it on the command line).
/// </para>
/// <para>
/// <b>The editor is the composer's, in a script mode</b> — the MOO edit's shape: no <c>%r</c> join, no escaping, no
/// spelling, lines saved as typed, lines numbered in cells, monospace in the window. ⌃S saves through
/// <see cref="ScriptFiles.Save"/> (the scripts folder only, temporary file and rename), reloads the character's scripts
/// and says in the editor's header and the console what the reload did, load errors with their <c>file:line</c>. The
/// editor stays open, so the next fix is one keystroke away; Esc closes it, keeping unsaved text for that file for the
/// run, and goes back to the console when that is where it was opened from.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Unsaved text of each script, by name, for the run: Esc keeps it, a save lets it go.</summary>
    private readonly Dictionary<string, string> _scriptBuffers = new(StringComparer.Ordinal);

    /// <summary>Whether the script editor was reached from the console, so closing it goes back there.</summary>
    private bool _scriptReturnToConsole;

    /// <summary>
    /// Whether ⌃P lists the script files (<c>Script …</c>): from the first time they are asked for (Edit script…, the
    /// console's ⌃E, a bare <c>/script edit</c>) for the run, as map find's rooms are — not in every empty palette.
    /// </summary>
    private bool _scriptsListed;

    /// <summary>The demo's own script, so the snapshot's editor has something to show without a scripts folder.</summary>
    internal const string DemoScriptText =
        "-- corvid.lua: Corvid's helpers\n" +
        "\n" +
        "function on_tell(line, who)\n" +
        "  store.set('last_tell', who)\n" +
        "  print('[info] tell from ' .. who)\n" +
        "end\n" +
        "\n" +
        "function vitals_low()\n" +
        "  local hp = gmcp.get('Char.Vitals.hp')\n" +
        "  return hp ~= nil and hp < 100\n" +
        "end\n" +
        "\n" +
        "function on_room()\n" +
        "  local room = map.current()\n" +
        "  if room then store.set('last_room', room.name) end\n" +
        "end\n" +
        "\n" +
        "trigger.add('^(\\\\w+) tells you', 'on_tell')\n";

    /// <summary>Whether the focused window is the demo's, in a client with no scripts folder — the snapshot's scripts.</summary>
    private bool DemoScriptsShown =>
        _scriptsRoot is null && _workspace.FindWindow(ActiveWindowId())?.SessionKey is { } owner && IsDemoKey(owner);

    /// <summary>The script open in the editor (either host), or null.</summary>
    internal ScriptEdit? OpenScriptEdit => _nativeCompose is { } open ? open.Script : _composer.IsOpen ? _composer.Script : null;

    /// <summary>Whose scripts the focused window's: its session's key, or its recorded owner's.</summary>
    private string? ScriptOwner() => ComposeDraftKey(ActiveWindowId());

    /// <summary>
    /// The focused character's script files, as ⌃P lists them: the folder's <c>.lua</c> files, the names its trigger sets
    /// give that are not there yet, and the package scripts those sets name. Each says whether this character loads it and
    /// the line of its newest error in the console.
    /// </summary>
    internal IReadOnlyList<ScriptFileRef> ScriptFileRefs()
    {
        var owner = ScriptOwner();
        if (DemoScriptsShown)
        {
            return
            [
                new("corvid.lua", "loaded by Corvid · ! line 14 in the console", false),
                new("package:aetherfall-pack/combat.lua", "from the aetherfall-pack package · read only", true),
            ];
        }

        if (_scriptsRoot is not { } root)
        {
            return [];
        }

        var named = (owner is null ? null : FindSession(owner))?.ScriptFiles ?? [];
        var errors = owner is null ? [] : ConsoleLog(owner).Lines.Where(l => l.Kind == ScriptConsoleKind.Error).Select(l => l.Text).ToArray();
        var refs = new List<ScriptFileRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in ScriptFiles.List(root))
        {
            seen.Add(name);
            refs.Add(new ScriptFileRef(name, Describe(name, exists: true), false));
        }

        foreach (var name in named)
        {
            if (seen.Contains(name))
            {
                continue;
            }

            if (name.StartsWith(ScriptFiles.PackagePrefix, StringComparison.Ordinal))
            {
                refs.Add(new ScriptFileRef(name, "from a package · read only", true));
            }
            else if (ScriptFiles.Check(name) is null)
            {
                refs.Add(new ScriptFileRef(name, "named by a trigger set · not written yet — opening starts it", false));
            }
        }

        return refs;

        string Describe(string name, bool exists)
        {
            var parts = new List<string> { named.Contains(name, StringComparer.Ordinal) ? "loaded by this character" : "in the scripts folder" };
            if (LastErrorLine(errors, name) is { } line)
            {
                parts.Add($"! line {line.ToString(CultureInfo.InvariantCulture)} in the console");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The line of the newest console error naming <paramref name="name"/>'s file, or null.</summary>
    private static int? LastErrorLine(IReadOnlyList<string> errors, string name) => ScriptErrors.LineIn(errors, name);

    /// <summary>
    /// ⌃P ▸ Edit script…, the console's ⌃E and a bare <c>/script edit</c>: the palette, opened on the script files. From
    /// the console, closing the editor goes back to it.
    /// </summary>
    private void ListScriptsToEdit(bool fromConsole)
    {
        if (_scriptsRoot is null && !DemoScriptsShown)
        {
            RefuseCommand("this client runs no scripts — start it with a scripts folder to edit them");
            return;
        }

        _scriptReturnToConsole = fromConsole;
        _scriptsListed = true;
        OpenMenu(Client.Commands.BuiltInCommands.ScriptEditQuery);
    }

    /// <summary>The script console's ⌃E (the window's Edit a script… button): the console closes and the palette lists the files.</summary>
    internal void EditScriptFromConsole()
    {
        if (ScriptConsoleIsOpen)
        {
            if (NativeModalOf(Client.Surfaces.SurfaceCatalog.ScriptConsole) is { } native)
            {
                CloseNativeModal(native);
            }
            else
            {
                _helpView.Close();
            }
        }

        ListScriptsToEdit(fromConsole: true);
    }

    /// <summary>⌃P ▸ <c>Script &lt;name&gt;</c>: opens that file, at its newest error's line when the console has one.</summary>
    private bool OpenScriptFromPalette(string name)
    {
        var refs = ScriptFileRefs();
        if (!refs.Any(r => r.Name == name))
        {
            RefuseCommand($"{Snippet(name)} is not one of this character's scripts");
            return true;
        }

        var owner = ScriptOwner();
        var errors = owner is null ? [] : ConsoleLog(owner).Lines.Where(l => l.Kind == ScriptConsoleKind.Error).Select(l => l.Text).ToArray();
        var line = LastErrorLine(errors, name) ?? 0;
        OpenScript(name, line, errorLine: line);
        return true;
    }

    /// <summary><c>/script</c>, <c>/script edit [name]</c>, <c>/script new &lt;name&gt;</c>.</summary>
    private void ScriptCommand(string args)
    {
        var space = args.IndexOf(' ');
        var verb = (space < 0 ? args : args[..space]).ToLowerInvariant();
        var rest = space < 0 ? string.Empty : args[(space + 1)..].Trim();
        switch (verb)
        {
            case "" or "edit" when rest.Length == 0:
                ListScriptsToEdit(fromConsole: false);
                return;
            case "edit":
                _scriptReturnToConsole = false;
                OpenScript(ScriptFiles.Normalise(rest));
                return;
            case "new":
                _scriptReturnToConsole = false;
                NewScript(rest);
                return;
            default:
                RefuseCommand("/script edit [name] opens a script; /script new <name> starts one");
                return;
        }
    }

    /// <summary>
    /// <c>/script new &lt;name&gt;</c>: the name checked by the loader's own rule, then the editor on the template. Nothing is
    /// written until ⌃S. A name that already exists opens that file instead, and says so.
    /// </summary>
    internal void NewScript(string typed)
    {
        if (_scriptsRoot is not { } root)
        {
            RefuseCommand("this client runs no scripts — start it with a scripts folder to write them");
            return;
        }

        var name = ScriptFiles.Normalise(typed);
        var (path, error) = ScriptFiles.PathOf(root, name);
        if (path is null)
        {
            RefuseCommand(error ?? "that is not a script's name");
            return;
        }

        if (File.Exists(path))
        {
            Notice($"{Snippet(name)} already exists — opened it", MessageSeverity.Info);
        }

        OpenScript(name);
    }

    /// <summary>
    /// Opens script <paramref name="name"/> in the composer's script mode: a package's read-only, anything else from the
    /// scripts folder (the template when it does not exist yet), or the unsaved text Esc kept for it.
    /// </summary>
    /// <param name="errorLine">The line the console's newest error names in it, marked in the editor; 0 for none.</param>
    internal void OpenScript(string name, int line = 0, int errorLine = 0)
    {
        if (ComposerOpen || AnyOverlayOpen)
        {
            RefuseCommand($"close {(ComposerOpen ? "the composer" : OpenOverlayName())} first — the script editor cannot open over it");
            return;
        }

        var package = name.StartsWith(ScriptFiles.PackagePrefix, StringComparison.Ordinal);
        string text;
        if (DemoScriptsShown)
        {
            text = package ? "-- combat.lua, from the aetherfall-pack package\nfunction on_hit(line) print('hit!') end\n" : DemoScriptText;
        }
        else if (package)
        {
            if (_packages.ResolveScript(name) is not { } path || !File.Exists(path) || new FileInfo(path).Length > ScriptFiles.MaxBytes)
            {
                RefuseCommand($"{Snippet(name)} is not in an installed package");
                return;
            }

            text = File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);
        }
        else if (_scriptsRoot is not { } root)
        {
            RefuseCommand("this client runs no scripts — start it with a scripts folder to edit them");
            return;
        }
        else
        {
            var (path, error) = ScriptFiles.PathOf(root, name);
            if (path is null)
            {
                RefuseCommand(error ?? "that is not a script's name");
                return;
            }

            if (File.Exists(path))
            {
                var (read, problem) = ScriptFiles.Read(root, name);
                if (read is null)
                {
                    RefuseCommand(problem ?? $"{Snippet(name)} could not be read");
                    return;
                }

                text = read;
            }
            else
            {
                text = ScriptFiles.Template;
            }
        }

        string? status = null;
        if (!package && _scriptBuffers.TryGetValue(name, out var kept))
        {
            text = kept;
            status = "unsaved text kept from earlier · ⌃S saves it";
        }

        var owner = ScriptOwner();
        var session = owner is null ? null : FindSession(owner);
        var target = session is not null
            ? SessionTitle(session)
            : owner is not null ? FindCharacter(owner)?.Character.Name ?? string.Empty : string.Empty;
        OpenComposer(target, text, ComposeEscaping.AsTyped, canSend: true, edit: null, script: new ScriptEdit(name, owner, package, status, line, errorLine));
    }

    /// <summary>
    /// ⌃S on a script: written atomically under the scripts folder only, then the character's scripts reloaded, and what
    /// that did said in the editor's header and the console. The editor stays open.
    /// </summary>
    private void SaveScriptEdit(ScriptEdit script, string body)
    {
        if (script.ReadOnly)
        {
            RefuseCommand($"{Snippet(script.Name)} is a package's script and is only read — copy what you need into a script of your own");
            return;
        }

        if (DemoScriptsShown)
        {
            SetScriptStatus("! the demo saves nothing");
            RefuseCommand("the demo saves nothing — run the client with a scripts folder");
            return;
        }

        if (_scriptsRoot is not { } root)
        {
            RefuseCommand("this client runs no scripts");
            return;
        }

        if (ScriptFiles.Save(root, script.Name, body) is { } refusal)
        {
            SetScriptStatus("! not saved: " + refusal);
            RefuseCommand(refusal);
            return;
        }

        _scriptBuffers.Remove(script.Name);
        var status = $"saved {DateTime.Now:HH:mm:ss}";
        var session = script.Owner is null ? null : FindSession(script.Owner);
        if (session is null || !_scripts.TryGetValue(session, out var scripts))
        {
            status += " · not reloaded — no connection";
        }
        else if (!scripts.Files.Contains(script.Name, StringComparer.Ordinal) && !session.ScriptFiles.Contains(script.Name, StringComparer.Ordinal))
        {
            status += " · not loaded — name it in a trigger set's scripts (F5)";
        }
        else
        {
            var log = ConsoleLog(session.SessionKey);
            var before = log.Count;
            scripts.Reload();
            var errors = log.Lines.Skip(before).Where(l => l.Kind == ScriptConsoleKind.Error).Select(l => l.Text).ToList();
            log.Add(ScriptConsoleKind.Note, $"saved {script.Name} · reloaded · {ScriptsLoaded(scripts)}");
            status = errors.Count == 0
                ? $"{status} · reloaded · {ScriptsLoaded(scripts)}"
                : $"! {errors[0]}{(errors.Count > 1 ? $" (+{errors.Count - 1} more in the console)" : string.Empty)}";
            RefreshConsole();
        }

        SetScriptStatus(status);
        Notice($"{Snippet(script.Name)}: {status.TrimStart('!', ' ')}", status.StartsWith('!') ? MessageSeverity.Warning : MessageSeverity.Info, toast: false);
    }

    /// <summary>Says what the last save did in the open script editor's header, in either host.</summary>
    private void SetScriptStatus(string status)
    {
        if (_nativeCompose is { Script: { } script } open)
        {
            open.Script = script.Said(status);
            open.Session.Publish(NativeComposeModel(open));
            return;
        }

        _composer.SetScriptStatus(status);
    }

    /// <summary>
    /// The script editor closed: unsaved text is kept for that file for the run (nothing is kept when it matches the
    /// file), and an editor opened from the console goes back to it.
    /// </summary>
    private void KeepScriptBuffer(ScriptEdit script, string body)
    {
        if (!script.ReadOnly && !DemoScriptsShown && _scriptsRoot is { } root)
        {
            var (onDisk, _) = ScriptFiles.Read(root, script.Name);
            var normalised = body.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (string.Equals(normalised.TrimEnd('\n'), (onDisk ?? ScriptFiles.Template).TrimEnd('\n'), StringComparison.Ordinal))
            {
                _scriptBuffers.Remove(script.Name);
            }
            else
            {
                _scriptBuffers[script.Name] = normalised;
            }
        }

        if (_scriptReturnToConsole)
        {
            _scriptReturnToConsole = false;
            OnUiThread(() =>
            {
                if (!ScriptConsoleIsOpen && !AnyOverlayOpen && !ComposerOpen)
                {
                    ShowScriptConsole();
                }
            });
        }
    }

    /// <summary>The unsaved text kept for a script, for the tests.</summary>
    internal string? KeptScriptText(string name) => _scriptBuffers.GetValueOrDefault(name);

    /// <summary>A character's console lines of a kind, for the tests.</summary>
    internal IReadOnlyList<string> ConsoleTexts(string key, ScriptConsoleKind kind) =>
        [.. ConsoleLog(key).Lines.Where(l => l.Kind == kind).Select(l => l.Text)];
}
