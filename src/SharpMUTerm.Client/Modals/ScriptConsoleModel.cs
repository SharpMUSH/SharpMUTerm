namespace SharpMUTerm.Client.Modals;

/// <summary>What a script console line is.</summary>
public enum ScriptConsoleKind
{
    /// <summary>A line typed at the prompt.</summary>
    Input,

    /// <summary>The value a typed line returned.</summary>
    Result,

    /// <summary>Something a script printed (<c>print</c>, <c>world.print</c>, <c>log.*</c>).</summary>
    Output,

    /// <summary>A script's error, a load failure or a refused file, with <c>file:line</c> where Lua gave one.</summary>
    Error,

    /// <summary>The console's own remark (scripts reloaded, a key deleted).</summary>
    Note,
}

/// <summary>One line of a character's script console.</summary>
public sealed record ScriptConsoleLine(ScriptConsoleKind Kind, string Text);

/// <summary>One <c>store.*</c> key and its value, as JSON cut to <see cref="ScriptConsoleModel.MaxValueLength"/>.</summary>
public sealed record ScriptStoreEntry(string Key, string Value);

/// <summary>
/// A character's script console (⌃P ▸ <em>Script console</em>): what its scripts printed and the errors they raised,
/// a one-line Lua prompt run in that character's sandbox, the lines typed there, and a read-only view of its
/// <c>store.*</c> with delete. Drawn by the cell surface (<c>ScriptConsoleSurface.Render</c>) and the window's native
/// modal from the same record.
/// </summary>
/// <param name="Character">Whose scripts these are (the window's character), or empty for none.</param>
/// <param name="Unavailable">Why there is no prompt (no scripts folder, no character), or null when there is one.</param>
/// <param name="Lines">The newest console lines, oldest first, at most <see cref="MaxLines"/>.</param>
/// <param name="History">What was typed at the prompt, oldest first, at most <see cref="MaxHistory"/>.</param>
/// <param name="Store">The store's keys and values in the order they were first set, at most <see cref="MaxStoreRows"/>.</param>
/// <param name="StoreCount">How many keys the store holds in all.</param>
/// <param name="Files">The script files the character's trigger sets name.</param>
/// <param name="Loaded">How many of them loaded.</param>
/// <param name="Folder">The scripts folder, or null in a client that runs none.</param>
public sealed record ScriptConsoleModel(
    string Character,
    string? Unavailable,
    IReadOnlyList<ScriptConsoleLine> Lines,
    IReadOnlyList<string> History,
    IReadOnlyList<ScriptStoreEntry> Store,
    int StoreCount,
    IReadOnlyList<string> Files,
    int Loaded,
    string? Folder)
{
    /// <summary>The most console lines kept per character.</summary>
    public const int MaxLines = 300;

    /// <summary>The most prompt lines remembered per character.</summary>
    public const int MaxHistory = 50;

    /// <summary>The most store keys listed.</summary>
    public const int MaxStoreRows = 200;

    /// <summary>The longest store value shown.</summary>
    public const int MaxValueLength = 120;

    /// <summary>The longest line typed at the prompt.</summary>
    public const int MaxInput = 500;

    /// <summary>The keys, in glyphs, as the cells' footer says them (a native view spells them in words).</summary>
    public const string Keys = "⏎ runs · ↑↓ history · ⇥ store keys · Del deletes a key · ⌃L reloads scripts · Esc closes";

    /// <summary>What the console says when its scripts printed nothing yet.</summary>
    public const string Empty = "nothing printed yet — try  1 + 1  or  print(world.name)";

    /// <summary>Whether a line can be run.</summary>
    public bool CanRun => Unavailable is null;

    /// <summary>The line under the title: whose scripts, how many loaded, how many keys.</summary>
    public string Summary =>
        Unavailable is not null
            ? Unavailable
            : $"{Character} · {LoadedText} · {StoreCount} store key{(StoreCount == 1 ? string.Empty : "s")}";

    private string LoadedText => (Files.Count, Loaded) switch
    {
        (0, _) => "no script files",
        (var n, var l) when l < n => $"{l} of {n} files loaded",
        (1, _) => "1 file loaded",
        (var n, _) => $"{n} files loaded",
    };

    /// <summary>A console line's mark, the same in both hosts.</summary>
    public static string Mark(ScriptConsoleKind kind) => kind switch
    {
        ScriptConsoleKind.Input => "›",
        ScriptConsoleKind.Result => "=",
        ScriptConsoleKind.Error => "!",
        ScriptConsoleKind.Note => "·",
        _ => " ",
    };

    public bool Equals(ScriptConsoleModel? other) =>
        other is not null
        && Character == other.Character
        && Unavailable == other.Unavailable
        && StoreCount == other.StoreCount
        && Loaded == other.Loaded
        && Folder == other.Folder
        && ModelLists.Same(Lines, other.Lines)
        && ModelLists.Same(History, other.History)
        && ModelLists.Same(Store, other.Store)
        && ModelLists.Same(Files, other.Files);

    public override int GetHashCode() =>
        HashCode.Combine(Character, Unavailable, StoreCount, Loaded, ModelLists.Hash(Lines), ModelLists.Hash(Store));
}

/// <summary>
/// A character's console lines, kept for the run (never saved) and fed from any thread: a trigger's script prints on
/// the read loop, a timer's on the scheduler. Bounded at <see cref="ScriptConsoleModel.MaxLines"/>.
/// </summary>
public sealed class ScriptConsoleLog
{
    private readonly object _gate = new();
    private readonly LinkedList<ScriptConsoleLine> _lines = new();
    private readonly List<string> _history = new();

    /// <summary>Raised after a line is added, on the thread that added it.</summary>
    public event Action? Changed;

    /// <summary>Adds a line, cut to one row of reasonable length per line of text.</summary>
    public void Add(ScriptConsoleKind kind, string text)
    {
        lock (_gate)
        {
            foreach (var part in (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Take(20))
            {
                var clean = new string(part.Select(c => char.IsControl(c) && c != '\t' ? ' ' : c).ToArray()).Replace('\t', ' ');
                _lines.AddLast(new ScriptConsoleLine(kind, clean.Length > 400 ? clean[..399] + "…" : clean));
            }

            while (_lines.Count > ScriptConsoleModel.MaxLines)
            {
                _lines.RemoveFirst();
            }
        }

        Changed?.Invoke();
    }

    /// <summary>Remembers a line typed at the prompt (a repeat of the last is not added twice).</summary>
    public void Remember(string input)
    {
        lock (_gate)
        {
            if (_history.Count == 0 || _history[^1] != input)
            {
                _history.Add(input);
            }

            if (_history.Count > ScriptConsoleModel.MaxHistory)
            {
                _history.RemoveAt(0);
            }
        }
    }

    /// <summary>The lines, oldest first.</summary>
    public IReadOnlyList<ScriptConsoleLine> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines.ToArray();
            }
        }
    }

    /// <summary>What was typed, oldest first.</summary>
    public IReadOnlyList<string> History
    {
        get
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }
    }
}

/// <summary>
/// One opening of the script console in a host's own controls. Every gesture goes back through it to the client's
/// thread and ends in the method the cell surface's key ends in; the client republishes the model after each.
/// </summary>
public sealed class ScriptConsoleSession : ModalSession<ScriptConsoleModel>
{
    private readonly Action<string> _run;
    private readonly Action<string> _delete;
    private readonly Action _reload;

    /// <param name="model">What the console shows.</param>
    /// <param name="isDark">Whether the client's theme is a dark one.</param>
    /// <param name="run">Runs a line at the prompt (<c>RunConsoleLine</c>).</param>
    /// <param name="delete">Deletes a store key (<c>DeleteStoreKey</c>).</param>
    /// <param name="reload">Reloads the character's scripts (<c>ReloadConsoleScripts</c>).</param>
    /// <param name="closed">Told the console closed.</param>
    public ScriptConsoleSession(
        ScriptConsoleModel model,
        bool isDark,
        Action<string> run,
        Action<string> delete,
        Action reload,
        Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(delete);
        ArgumentNullException.ThrowIfNull(reload);
        _run = run;
        _delete = delete;
        _reload = reload;
    }

    public override string Surface => "script-console";

    /// <summary>The host's: ⏎ on a line. Nothing when the console cannot run one, or the line is blank.</summary>
    public bool Run(string line)
    {
        if (IsEnded || !Model.CanRun || string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        _run(line.Length > ScriptConsoleModel.MaxInput ? line[..ScriptConsoleModel.MaxInput] : line);
        return true;
    }

    /// <summary>The host's: Delete on a store key the model lists.</summary>
    public bool Delete(string key)
    {
        if (IsEnded || !Model.Store.Any(e => e.Key == key))
        {
            return false;
        }

        _delete(key);
        return true;
    }

    /// <summary>The host's: Reload scripts.</summary>
    public bool Reload()
    {
        if (IsEnded)
        {
            return false;
        }

        _reload();
        return true;
    }
}
