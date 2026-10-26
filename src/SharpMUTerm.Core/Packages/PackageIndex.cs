using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMUTerm.Core.Packages;

/// <summary>One package an index lists: its manifest's fields, where the archive is, and the archive's SHA-256.</summary>
public sealed record PackageIndexEntry
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public string? Author { get; init; }

    public string? Description { get; init; }

    public List<string> Games { get; init; } = new();

    public required string Url { get; init; }

    public required string Sha256 { get; init; }

    /// <summary>The entry as a manifest, so it is held to the rules a package's own <c>package.json</c> is.</summary>
    [JsonIgnore]
    public PackageManifest Manifest => new()
    {
        Id = Id,
        Name = Name,
        Version = Version,
        Author = Author,
        Description = Description,
        Games = Games,
    };

    /// <summary>
    /// Whether <paramref name="archive"/> is the file this entry names: its SHA-256 and, inside it, the same id
    /// and version. Null when it is; otherwise what differs.
    /// </summary>
    public string? Verify(byte[] archive, PackageManifest? inside)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var hash = Convert.ToHexStringLower(SHA256.HashData(archive));
        if (!string.Equals(hash, Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return "the download does not match the index's SHA-256";
        }

        if (inside is not null && (inside.Id != Id || inside.Version != Version))
        {
            return $"the archive is {inside.Id} {inside.Version}, not {Id} {Version}";
        }

        return null;
    }
}

/// <summary>
/// A package index: <c>{"packages":[…]}</c>, each entry a manifest plus <c>url</c> and <c>sha256</c>. It is
/// fetched only when the player opens Browse packages. An entry that breaks a rule is dropped rather than
/// failing the whole index, and the count of dropped entries is kept so the screen can say so.
/// </summary>
public sealed class PackageIndex
{
    /// <summary>The cap on an index download.</summary>
    public const int MaxIndexBytes = 1024 * 1024;

    /// <summary>The cap on entries kept from one index.</summary>
    public const int MaxEntries = 2000;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private PackageIndex(IReadOnlyList<PackageIndexEntry> entries, int dropped)
    {
        Entries = entries;
        Dropped = dropped;
    }

    public IReadOnlyList<PackageIndexEntry> Entries { get; }

    /// <summary>Entries the index listed that were not kept: a bad manifest, a link that is not https, or a bad hash.</summary>
    public int Dropped { get; }

    private sealed record Document(List<JsonElement>? Packages);

    /// <summary>The index in <paramref name="json"/>, or why it is not one.</summary>
    public static (PackageIndex? Index, string? Problem) Parse(string json)
    {
        Document? document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(json, Options);
        }
        catch (JsonException ex)
        {
            return (null, $"the index does not parse: {ex.Message}");
        }

        if (document?.Packages is not { } packages)
        {
            return (null, "the index lists no packages");
        }

        var kept = new List<PackageIndexEntry>();
        var dropped = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in packages)
        {
            PackageIndexEntry? entry;
            try
            {
                entry = element.Deserialize<PackageIndexEntry>(Options);
            }
            catch (JsonException)
            {
                entry = null;
            }

            if (entry is null || kept.Count >= MaxEntries || Problem(entry) is not null || !seen.Add(entry.Id))
            {
                dropped++;
                continue;
            }

            kept.Add(entry);
        }

        return (new PackageIndex(kept, dropped), null);
    }

    /// <summary>What is wrong with <paramref name="entry"/>, or null.</summary>
    public static string? Problem(PackageIndexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Manifest.Problem() is { } problem)
        {
            return problem;
        }

        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return "the link is not https";
        }

        if (entry.Sha256.Length != 64 || !entry.Sha256.All(Uri.IsHexDigit))
        {
            return "the SHA-256 is not 64 hex digits";
        }

        return null;
    }

    /// <summary>The entries, with those for <paramref name="host"/>:<paramref name="port"/> first; otherwise by name.</summary>
    public IReadOnlyList<PackageIndexEntry> For(string? host, int port) =>
        Entries
            .OrderBy(e => host is not null && e.Games.Any(g => g != "*" && PackageManifest.Matches(g, host, port)) ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
