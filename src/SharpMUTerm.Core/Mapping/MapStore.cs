using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpMUTerm.Core.Mapping;

/// <summary>
/// Where maps live: one JSON file per server under <c>maps/</c> beside <c>config.json</c>, versioned on its
/// own and written through a temporary file and a rename.
/// <para>
/// <b>Keyed by endpoint, not world</b> — <c>MsspCache</c>'s reasoning. A map describes the game a server
/// runs, and a world name is a label the user can rename out from under it; two worlds pointed at one server
/// share one map.
/// </para>
/// <para>
/// <b>Memory-only by construction when built without a directory</b>, which is what every test and snapshot
/// gets: maps are still kept for the life of the process, and nothing is read or written. Only
/// <c>Program</c> hands one a directory.
/// </para>
/// <para>
/// A file from a <em>newer</em> client is never overwritten: its map loads empty, <see cref="ProblemFor"/>
/// says why, and saves to that endpoint are skipped, so running an older build cannot destroy a map. A file
/// that is not a map at all is renamed with <see cref="AsideSuffix"/> and mapping starts again. A file with
/// no version is the first layout.
/// </para>
/// </summary>
public sealed class MapStore
{
    /// <summary>The folder beside <c>config.json</c>.</summary>
    public const string FolderName = "maps";

    /// <summary>What an unreadable map file is renamed with, so a new one can be written.</summary>
    public const string AsideSuffix = ".unreadable";

    /// <summary>This file's schema version.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string? _directory;
    private readonly Dictionary<string, WorldMap> _maps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _problems = new(StringComparer.Ordinal);
    private readonly HashSet<string> _readOnly = new(StringComparer.Ordinal);

    /// <summary>A store; <paramref name="directory"/> null — the default — is memory-only.</summary>
    public MapStore(string? directory = null)
    {
        _directory = string.IsNullOrWhiteSpace(directory) ? null : Path.GetFullPath(directory);
    }

    /// <summary>The maps folder beside a configuration file.</summary>
    public static string PathFor(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(configurationPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(configurationPath));
        return Path.Combine(directory ?? string.Empty, FolderName);
    }

    public ILogger Logger { get; set; } = NullLogger.Instance;

    public bool IsPersistent => _directory is not null;

    /// <summary>The identity a map is filed under: the host lower-cased without a trailing dot, and the port.</summary>
    public static string Key(string? host, int port) =>
        string.Concat(
            (host ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant(),
            ":",
            port.ToString(CultureInfo.InvariantCulture));

    /// <summary>
    /// The file a key is stored in: every character outside <c>[a-z0-9.-]</c> becomes <c>_</c>, so a host
    /// can never name a path outside the folder.
    /// </summary>
    public static string FileNameFor(string key)
    {
        var name = new StringBuilder(key.Length + 5);
        foreach (var ch in key)
        {
            name.Append(ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.' ? ch : '_');
        }

        // A key of only dots would be "." or ".."; no host is that, but the folder must still hold the file.
        return (name.ToString().Trim('.') is { Length: > 0 } safe ? safe : "_") + ".json";
    }

    /// <summary>Why this endpoint's file could not be read, or null.</summary>
    public string? ProblemFor(string? host, int port) => _problems.GetValueOrDefault(Key(host, port));

    /// <summary>The map for this endpoint — read from disk the first time it is asked for, then held.</summary>
    public WorldMap For(string? host, int port)
    {
        var key = Key(host, port);
        if (_maps.TryGetValue(key, out var held))
        {
            return held;
        }

        var map = _directory is null ? new WorldMap() : Read(key);
        _maps[key] = map;
        return map;
    }

    /// <summary>Writes this endpoint's map, when the store is persistent and the file is ours to replace.</summary>
    public void Save(string? host, int port)
    {
        var key = Key(host, port);
        if (_directory is null || _readOnly.Contains(key) || !_maps.TryGetValue(key, out var map))
        {
            return;
        }

        var path = Path.Combine(_directory, FileNameFor(key));
        try
        {
            Directory.CreateDirectory(_directory);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, Serialize(map, key));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Logger.LogWarning(e, "The map for {Endpoint} could not be written to {Path}.", key, path);
        }
    }

    /// <summary>The file's text for a map.</summary>
    public static string Serialize(WorldMap map, string key)
    {
        ArgumentNullException.ThrowIfNull(map);
        var document = new JsonObject
        {
            ["version"] = CurrentVersion,
            ["endpoint"] = key,
            ["areas"] = JsonSerializer.SerializeToNode(map.Areas.Values.OrderBy(a => a.Id).ToArray(), Json),
            ["rooms"] = JsonSerializer.SerializeToNode(map.Rooms.Values.OrderBy(r => r.Id).ToArray(), Json),
        };
        return document.ToJsonString(Json);
    }

    /// <summary>
    /// A map from a file's text. Throws <see cref="NotSupportedException"/> for a version newer than this
    /// client's, and <see cref="JsonException"/> for text that is not a map.
    /// </summary>
    public static WorldMap Deserialize(string text)
    {
        var document = JsonNode.Parse(text) as JsonObject ?? throw new JsonException("A map file is a JSON object.");
        var version = document["version"]?.GetValue<int>() ?? 1;
        if (version > CurrentVersion)
        {
            throw new NotSupportedException(
                $"this map was written by a newer SharpMUTerm (map version {version}; this one reads {CurrentVersion})");
        }

        var map = new WorldMap();
        foreach (var area in document["areas"]?.Deserialize<MapArea[]>(Json) ?? [])
        {
            map.Areas[area.Id] = area;
        }

        foreach (var room in document["rooms"]?.Deserialize<MapRoom[]>(Json) ?? [])
        {
            map.Add(Normalised(room));
        }

        return map;
    }

    /// <summary>
    /// Puts back what a hand-edited file may have lost: the dictionaries' ordinal comparers (the serializer
    /// builds default ones), a weight of at least 1, and only real directions among the exits.
    /// </summary>
    private static MapRoom Normalised(MapRoom room)
    {
        var exits = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (direction, to) in room.Exits ?? [])
        {
            if (MapDirections.Normalise(direction) is { } name)
            {
                exits[name] = to;
            }
        }

        room.Exits = exits;
        room.SpecialExits = new Dictionary<string, long>(room.SpecialExits ?? [], StringComparer.Ordinal);
        room.ExitWeights = new Dictionary<string, int>(room.ExitWeights ?? [], StringComparer.Ordinal);
        room.LockedExits = new HashSet<string>(room.LockedExits ?? [], StringComparer.Ordinal);
        room.Stubs = new HashSet<string>(room.Stubs ?? [], StringComparer.Ordinal);
        room.UserData = new Dictionary<string, string>(room.UserData ?? [], StringComparer.Ordinal);
        room.Name ??= string.Empty;
        room.Weight = Math.Max(1, room.Weight);
        return room;
    }

    private WorldMap Read(string key)
    {
        var path = Path.Combine(_directory!, FileNameFor(key));
        if (!File.Exists(path))
        {
            return new WorldMap();
        }

        try
        {
            return Deserialize(File.ReadAllText(path));
        }
        catch (NotSupportedException e)
        {
            _readOnly.Add(key);
            _problems[key] = e.Message;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            // Unreadable, not newer: moved aside rather than overwritten, so mapping can start again and
            // whatever the file held is still there for someone who wants it back.
            _problems[key] = $"the map file could not be read ({e.Message}); it was moved to {Path.GetFileName(path)}{AsideSuffix}";
            try
            {
                File.Move(path, path + AsideSuffix, overwrite: true);
            }
            catch (Exception move) when (move is IOException or UnauthorizedAccessException)
            {
                _readOnly.Add(key);
                _problems[key] = $"the map file could not be read ({e.Message}) or moved aside; it is left as it is";
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _readOnly.Add(key);
            _problems[key] = $"the map file could not be read ({e.Message}); it is left as it is";
        }

        Logger.LogWarning("The map for {Endpoint} was not loaded: {Problem}", key, _problems[key]);
        return new WorldMap();
    }
}
