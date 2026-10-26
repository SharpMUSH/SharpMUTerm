using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUTerm.Scripting;

/// <summary>
/// What one character's scripts keep between runs: <c>store.get</c>, <c>store.set</c> and <c>store.keys</c>.
/// A JSON object of named values, written to one file per character.
/// </summary>
/// <remarks>
/// <para>
/// <b>No path means memory only, by construction</b>, the way <c>MsspCache</c> is built: the host always has a
/// store, so a script never needs to ask whether one exists, and a snapshot or a test that builds a store with
/// no path writes nothing.
/// </para>
/// <para>
/// <b>Bounded at the door.</b> A key is at most <see cref="MaxKeyLength"/> characters, there are at most
/// <see cref="MaxKeys"/> of them, and the whole file at most <see cref="MaxBytes"/> bytes. A <c>set</c> that
/// would pass a bound is refused and changes nothing, so a runaway timer cannot fill the disk.
/// </para>
/// <para>
/// <b>Written on every change that changes something</b>, through a temporary file and a rename, so a crash
/// leaves the old file or the new one and never half of either. A file that does not parse is moved aside to
/// <c>.bad</c> rather than overwritten by the next <c>set</c>.
/// </para>
/// </remarks>
public sealed class ScriptStore
{
    public const int MaxKeys = 1024;
    public const int MaxKeyLength = 128;
    public const int MaxBytes = 256 * 1024;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string? _path;
    private readonly object _gate = new();
    private readonly JsonObject _values;

    /// <param name="path">The file to keep the values in; null keeps them in memory only.</param>
    /// <param name="report">Where a file that could not be read or written is said.</param>
    public ScriptStore(string? path, Action<string>? report = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        Report = report ?? (_ => { });
        _values = Load();
    }

    private Action<string> Report { get; }

    /// <summary>The file this store writes, or null when it writes none.</summary>
    public string? FilePath => _path;

    /// <summary>The value under <paramref name="key"/>, as a copy, or null.</summary>
    public JsonNode? Get(string key)
    {
        lock (_gate)
        {
            return _values[key ?? string.Empty]?.DeepClone();
        }
    }

    /// <summary>Every key, in the order they were first set.</summary>
    public IReadOnlyList<string> Keys
    {
        get
        {
            lock (_gate)
            {
                return _values.Select(pair => pair.Key).ToArray();
            }
        }
    }

    /// <summary>
    /// Sets <paramref name="key"/> to <paramref name="value"/>, or removes it when the value is null. Returns
    /// null, or why nothing was changed.
    /// </summary>
    public string? Set(string key, JsonNode? value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "a key is required.";
        }

        if (key.Length > MaxKeyLength)
        {
            return $"a key may be at most {MaxKeyLength} characters.";
        }

        lock (_gate)
        {
            var previous = _values[key];
            if (value is null)
            {
                if (!_values.ContainsKey(key))
                {
                    return null;
                }

                _values.Remove(key);
                Write();
                return null;
            }

            if (previous is not null && JsonNode.DeepEquals(previous, value))
            {
                return null;
            }

            if (previous is null && _values.Count >= MaxKeys)
            {
                return $"the store already holds {MaxKeys} keys.";
            }

            var copy = value.DeepClone();
            _values[key] = copy;
            if (Encoding.UTF8.GetByteCount(_values.ToJsonString()) > MaxBytes)
            {
                if (previous is null)
                {
                    _values.Remove(key);
                }
                else
                {
                    _values[key] = previous.DeepClone();
                }

                return $"the store may hold at most {MaxBytes / 1024} KB.";
            }

            Write();
            return null;
        }
    }

    private JsonObject Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return new JsonObject();
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(_path)) is JsonObject values)
            {
                return values;
            }
        }
        catch (JsonException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"Script store {_path} could not be read: {ex.Message}");
            return new JsonObject();
        }

        try
        {
            File.Move(_path, _path + ".bad", overwrite: true);
            Report($"Script store {_path} was not valid and was moved to {_path}.bad.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"Script store {_path} is not valid and could not be moved aside: {ex.Message}");
        }

        return new JsonObject();
    }

    private void Write()
    {
        if (_path is null)
        {
            return;
        }

        var temp = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temp, _values.ToJsonString(Indented));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report($"Script store {_path} could not be written: {ex.Message}");
        }
    }

    /// <summary>
    /// The file name a character's store is kept under: the session key with every character a file name
    /// may not hold replaced, so two characters never share one and none names a path.
    /// </summary>
    public static string FileNameFor(string sessionKey)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new StringBuilder(sessionKey.Length);
        foreach (var c in sessionKey)
        {
            name.Append(invalid.Contains(c) || c is '/' or '\\' or ':' ? '_' : c);
        }

        var safe = name.ToString().Trim('.', ' ');
        if (safe.Length == 0 || !string.Equals(safe, sessionKey, StringComparison.Ordinal))
        {
            // A name that had to change could now match another character's; the hash keeps them apart.
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sessionKey)));
            safe = (safe.Length == 0 ? "_" : safe) + "-" + hash[..8].ToLowerInvariant();
        }

        return safe + ".json";
    }
}
