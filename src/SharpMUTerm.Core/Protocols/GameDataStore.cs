using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpMUTerm.Core.Protocols;

/// <summary>One package the store holds: its name as the server spelt it, when it last changed, and its data.</summary>
public sealed record GameDataEntry(string Package, DateTimeOffset UpdatedAt, JsonNode? Data);

/// <summary>
/// The latest out-of-band game data a session has received: every GMCP package by name, and every MSDP
/// variable under <c>MSDP.&lt;NAME&gt;</c>. Read by dotted path — <c>Char.Vitals.hp</c>, <c>MSDP.HEALTH</c> —
/// by the status row's meters, the ⌃P inspector and Lua.
/// </summary>
/// <remarks>
/// <para>
/// <b>An object payload is merged into what is held, key by key</b>, the way Mudlet's <c>gmcp</c> table is:
/// <c>Char.Status</c> and its relatives send only what changed, and replacing the package would forget every
/// field the latest message did not repeat. Any other payload replaces the package.
/// </para>
/// <para>
/// <b>Names are case-insensitive</b> because servers disagree: IRE sends <c>Char.Vitals</c>, Aardwolf
/// <c>char.vitals</c>, and a meter configured once should read both.
/// </para>
/// <para>
/// <b>It is bounded at the door</b>: a payload over <see cref="MaxPayloadBytes"/> is refused, and once
/// <see cref="MaxPackages"/> names are held a new one is refused (an existing one still updates). The server
/// chooses both, so neither may grow memory without limit.
/// </para>
/// <para>Writes arrive on the telnet read loop and reads come from the UI thread; one lock covers both.</para>
/// </remarks>
public sealed class GameDataStore
{
    /// <summary>The largest payload kept: 64 KiB of JSON text.</summary>
    public const int MaxPayloadBytes = 64 * 1024;

    /// <summary>How many package names may be held at once.</summary>
    public const int MaxPackages = 512;

    /// <summary>The prefix MSDP variables are kept under.</summary>
    public const string MsdpPrefix = "MSDP";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, GameDataEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    public GameDataStore(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Raised after a package changes, with its name. On the writer's thread.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>How many packages are held.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Every package held, sorted by name.</summary>
    public IReadOnlyList<GameDataEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.Values
                    .OrderBy(e => e.Package, StringComparer.OrdinalIgnoreCase)
                    .Select(e => e with { Data = e.Data?.DeepClone() })
                    .ToArray();
            }
        }
    }

    /// <summary>
    /// Takes one GMCP message. An empty body (GMCP allows a package with no data) is held as null. Returns
    /// whether it was kept.
    /// </summary>
    public bool SetGmcp(string package, string? json)
    {
        if (string.IsNullOrWhiteSpace(package) || package.Length > 128 || (json?.Length ?? 0) > MaxPayloadBytes)
        {
            return false;
        }

        JsonNode? data = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                data = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return Store(package.Trim(), data);
    }

    /// <summary>
    /// Takes one MSDP message, as the telnet library renders it: a JSON object of variable → value. Each
    /// variable is held as its own package, <c>MSDP.&lt;NAME&gt;</c>. Returns whether anything was kept.
    /// </summary>
    public bool SetMsdp(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxPayloadBytes)
        {
            return false;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException)
        {
            return false;
        }

        if (root is not JsonObject variables)
        {
            return false;
        }

        var kept = false;
        foreach (var (name, value) in variables.ToArray())
        {
            if (name.Length is > 0 and <= 64)
            {
                kept |= Store($"{MsdpPrefix}.{name}", value?.DeepClone());
            }
        }

        return kept;
    }

    /// <summary>
    /// The value at a dotted path, or null. The longest package name that prefixes the path wins, and the rest
    /// of the path walks into its data: <c>Char.Vitals.hp</c> is field <c>hp</c> of package <c>Char.Vitals</c>.
    /// A numeric segment indexes an array.
    /// </summary>
    public JsonNode? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var parts = path.Trim().Split('.');
        lock (_gate)
        {
            for (var take = parts.Length; take > 0; take--)
            {
                if (!_entries.TryGetValue(string.Join('.', parts, 0, take), out var entry))
                {
                    continue;
                }

                var node = entry.Data;
                for (var i = take; i < parts.Length && node is not null; i++)
                {
                    node = Step(node, parts[i]);
                }

                return node?.DeepClone();
            }
        }

        return null;
    }

    /// <summary>
    /// The value at a path as display text: a string as itself, a number in invariant culture, a boolean as
    /// <c>true</c>/<c>false</c>. Null for a missing value or one that is an object or an array.
    /// </summary>
    public string? GetText(string? path) => Get(path) switch
    {
        JsonValue value when value.TryGetValue(out string? text) => text,
        JsonValue value when value.TryGetValue(out bool flag) => flag ? "true" : "false",
        JsonValue value => value.ToJsonString(),
        _ => null,
    };

    /// <summary>The value at a path as a number, reading numeric strings too (IRE sends <c>"hp": "450"</c>).</summary>
    public double? GetNumber(string? path) => GetText(path) is { } text
        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
        && double.IsFinite(n)
            ? n
            : null;

    /// <summary>Forgets everything, as a new connection does.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    /// <summary>The data a package holds, pretty-printed, for the inspector.</summary>
    public static string Pretty(JsonNode? data) =>
        data is null ? "(no data)" : data.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private bool Store(string package, JsonNode? data)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(package, out var existing))
            {
                if (existing.Data is JsonObject held && data is JsonObject update)
                {
                    var merged = (JsonObject)held.DeepClone();
                    foreach (var (key, value) in update.ToArray())
                    {
                        merged[key] = value?.DeepClone();
                    }

                    if (Size(merged) > MaxPayloadBytes)
                    {
                        return false;
                    }

                    data = merged;
                }
            }
            else if (_entries.Count >= MaxPackages)
            {
                return false;
            }

            _entries[package] = new GameDataEntry(existing?.Package ?? package, _time.GetUtcNow(), data);
        }

        Changed?.Invoke(this, package);
        return true;
    }

    private static int Size(JsonNode node) => Encoding.UTF8.GetByteCount(node.ToJsonString());

    private static JsonNode? Step(JsonNode node, string part) => node switch
    {
        JsonObject obj => obj.FirstOrDefault(p => string.Equals(p.Key, part, StringComparison.OrdinalIgnoreCase)).Value,
        JsonArray array when int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var i)
            && i < array.Count => array[i],
        _ => null,
    };
}
