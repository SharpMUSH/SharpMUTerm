using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Scripting;

/// <summary>
/// The Lua that runs for one session: a <see cref="ScriptHost"/> loaded with the files the session's
/// trigger sets name (<see cref="WorldSession.ScriptFiles"/>), rebuilt when that list changes or on request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Files are read from under one root and nowhere else.</b> A trigger set is shared configuration and a
/// package will one day be able to bring one, so a set naming <c>/etc/passwd</c> or <c>../../.ssh/id_rsa</c>
/// must not be able to make the client read it. A relative name resolves against the root; anything that
/// resolves outside it, absolute or not, is refused and reported. The root is the app's answer to <em>may I
/// run scripts at all</em>, the same shape as the session log's root: an app with none builds none of these.
/// </para>
/// <para>
/// <b>A rebuild drops what the old scripts registered.</b> <c>trigger.add</c> and <c>alias.add</c> put rules
/// in the engines' runtime lists, which a configuration reload deliberately keeps. Re-running the same file
/// over them would register every rule twice, so the runtime lists are cleared first — they hold nothing a
/// script did not put there.
/// </para>
/// <para>
/// Lua runs on whichever thread raised the request — the telnet read loop for a trigger, the UI thread for an
/// alias or a macro, the scheduler for a timer — serialised by the host's own lock. That is the model the
/// bridge was written for, and it keeps a slow script off the UI thread for the commonest case.
/// </para>
/// </remarks>
public sealed class SessionScripts : IDisposable
{
    private readonly WorldSession _session;
    private readonly string _root;
    private readonly Action<string> _report;
    private readonly Func<string, string?>? _packageScript;
    private readonly ScriptStore _store;
    private readonly Func<string, string, string?>? _sendTo;
    private WorldSessionScriptBridge? _bridge;
    private ScriptHost? _host;
    private string[] _files = Array.Empty<string>();
    private bool _disposed;

    /// <param name="session">The session whose rules and output the scripts act on.</param>
    /// <param name="root">The directory script files are read from; nothing outside it is read.</param>
    /// <param name="report">
    /// Where a load failure, a refused path or a script's runtime error is said. Called on whichever thread
    /// the failure happened on, so a caller with a UI thread marshals.
    /// </param>
    /// <param name="packageScript">
    /// Resolves a <c>package:&lt;id&gt;/&lt;file&gt;</c> name inside that package's own folder, or null when it
    /// names nothing installed. Without it no package script loads.
    /// </param>
    /// <param name="storeRoot">
    /// The folder the character's <c>store.*</c> file is kept in, named for the session; null keeps the values
    /// in memory only. Either way they outlive a <see cref="Reload"/>.
    /// </param>
    /// <param name="sendTo">
    /// What <c>world.sendTo(name, command)</c> calls: null when the command went, otherwise why not. Called on the
    /// script's thread. Null leaves scripts unable to reach another character.
    /// </param>
    public SessionScripts(
        WorldSession session,
        string root,
        Action<string> report,
        Func<string, string?>? packageScript = null,
        string? storeRoot = null,
        Func<string, string, string?>? sendTo = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _packageScript = packageScript;
        _sendTo = sendTo;
        _store = new ScriptStore(
            string.IsNullOrWhiteSpace(storeRoot) ? null : Path.Combine(storeRoot, ScriptStore.FileNameFor(session.SessionKey)),
            report);
    }

    /// <summary>What the scripts keep between runs.</summary>
    public ScriptStore Store => _store;

    /// <summary>The file names the host was last built from, as the trigger sets spell them.</summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>How many of <see cref="Files"/> the last <see cref="Reload"/> actually loaded.</summary>
    public int LoadedCount { get; private set; }

    /// <summary>The live host, or null before the first <see cref="Reload"/>.</summary>
    public ScriptHost? Host => _host;

    /// <summary>Whether the loaded scripts define a global function by this (possibly dotted) name.</summary>
    public bool Defines(string name) => _host?.Defines(name) ?? false;

    /// <summary>
    /// Rebuilds only if the session's file list differs from the one the host was built from, so a settings
    /// change that touched nothing scripted does not restart every script's timers. Returns whether it did.
    /// </summary>
    public bool SyncFiles()
    {
        if (_host is not null && _files.SequenceEqual(_session.ScriptFiles, StringComparer.Ordinal))
        {
            return false;
        }

        Reload();
        return true;
    }

    /// <summary>
    /// Throws the old host away, drops what it registered, and builds a fresh one from the session's current
    /// file list, read from disk again. Returns how many files loaded.
    /// </summary>
    public int Reload()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        TearDown();
        _session.Triggers.ClearRuntime();
        _session.Aliases.ClearRuntime();

        _files = _session.ScriptFiles.ToArray();
        _bridge = new WorldSessionScriptBridge(_session, _sendTo);
        _host = new ScriptHost(_bridge, _store);
        _bridge.Attach(_host);
        _host.Error += OnScriptError;

        var loaded = 0;
        foreach (var file in _files)
        {
            var fromPackage = file.StartsWith("package:", StringComparison.Ordinal);
            if ((fromPackage ? _packageScript?.Invoke(file) : Resolve(_root, file)) is not { } path)
            {
                _report(fromPackage
                    ? $"Script '{file}' is not in an installed package and was not loaded."
                    : $"Script '{file}' is outside the scripts folder ({_root}) and was not loaded.");
                continue;
            }

            try
            {
                _host.LoadFile(path);
                loaded++;
            }
            catch (ScriptException ex)
            {
                _report($"Script '{file}': {ex.Message}");
            }
        }

        LoadedCount = loaded;
        return loaded;
    }

    /// <summary>
    /// The full path <paramref name="file"/> names under <paramref name="root"/>, or null when it names
    /// somewhere else. Comparison is ordinal, so on a case-insensitive file system a differently cased root is
    /// refused rather than allowed: the safe direction for this to be wrong in.
    /// </summary>
    public static string? Resolve(string root, string file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string full;
        try
        {
            full = Path.GetFullPath(file, fullRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var prefix = fullRoot + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.Ordinal) ? full : null;
    }

    private void OnScriptError(object? sender, ScriptException e) => _report($"Script error: {e.Message}");

    private void TearDown()
    {
        if (_host is { } host)
        {
            host.Error -= OnScriptError;
            host.Dispose();
        }

        _bridge?.Dispose();
        _host = null;
        _bridge = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TearDown();
    }
}
