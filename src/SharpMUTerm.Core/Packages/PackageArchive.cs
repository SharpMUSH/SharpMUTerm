using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Packages;

/// <summary>
/// What a package archive holds, read and checked in memory. Nothing in it has touched the disk or the
/// configuration yet: that waits for the player to accept the review.
/// </summary>
public sealed record PackageContents(
    PackageManifest Manifest,
    IReadOnlyList<TriggerSet> Sets,
    IReadOnlyDictionary<string, byte[]> Scripts,
    IReadOnlyDictionary<string, byte[]> Media,
    IReadOnlyList<string> Ignored)
{
    /// <summary>What the review screen says the package would add.</summary>
    public PackageReview Review() => PackageReview.Of(this);
}

/// <summary>
/// A <c>.smtpkg</c> file: a zip holding <c>package.json</c>, <c>sets/*.json</c> in the trigger-set schema,
/// <c>scripts/**.lua</c> and <c>media/**</c>.
/// <para>
/// <b>A package is code from a stranger, and this is the door.</b> Every entry name is relative, made of
/// plain segments (no <c>..</c>, no <c>.</c>, no drive, no backslash), and inside one of the three folders;
/// anything else is left out and listed. Sizes are counted while decompressing, never taken from the
/// archive's own headers, against a per-file and a total cap, so a zip bomb stops at the cap. The manifest
/// is read and validated before any other entry is opened.
/// </para>
/// </summary>
public static class PackageArchive
{
    public const string Extension = ".smtpkg";

    public const long MaxArchiveBytes = 64L * 1024 * 1024;

    public const long MaxTotalBytes = 128L * 1024 * 1024;

    public const long MaxFileBytes = 32L * 1024 * 1024;

    public const long MaxTextBytes = 2L * 1024 * 1024;

    public const int MaxEntries = 1000;

    /// <summary>The media a package may carry: the sound types the client plays, and pictures.</summary>
    public static readonly IReadOnlyList<string> MediaExtensions = ["wav", "mp3", "ogg", "flac", "png", "jpg", "jpeg", "gif", "webp"];

    public static (PackageContents? Contents, string? Problem) Read(byte[] archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.LongLength > MaxArchiveBytes)
        {
            return (null, $"the file is larger than {MaxArchiveBytes / (1024 * 1024)} MB");
        }

        try
        {
            using var zip = new ZipArchive(new MemoryStream(archive, writable: false), ZipArchiveMode.Read);
            return Read(zip);
        }
        catch (InvalidDataException ex)
        {
            return (null, $"not a package: {ex.Message}");
        }
    }

    private static (PackageContents?, string?) Read(ZipArchive zip)
    {
        if (zip.Entries.Count > MaxEntries)
        {
            return (null, $"the package has more than {MaxEntries} files");
        }

        var manifestEntry = zip.Entries.FirstOrDefault(e => e.FullName == "package.json");
        if (manifestEntry is null)
        {
            return (null, "the package has no package.json");
        }

        long total = 0;
        var manifestBytes = ReadCapped(manifestEntry, MaxTextBytes, ref total);
        if (manifestBytes is null)
        {
            return (null, "package.json is too large");
        }

        var (manifest, problem) = PackageManifest.Parse(Encoding.UTF8.GetString(manifestBytes));
        if (manifest is null)
        {
            return (null, problem);
        }

        var sets = new List<TriggerSet>();
        var scripts = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var media = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var ignored = new List<string>();
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName;
            if (entry == manifestEntry || name.EndsWith('/'))
            {
                continue;
            }

            if (Classify(name) is not { } kind)
            {
                ignored.Add(name);
                continue;
            }

            var cap = kind == Kind.Media ? MaxFileBytes : MaxTextBytes;
            var bytes = ReadCapped(entry, cap, ref total);
            if (bytes is null)
            {
                return (null, total > MaxTotalBytes
                    ? $"the package unpacks to more than {MaxTotalBytes / (1024 * 1024)} MB"
                    : $"{name} is larger than {cap / (1024 * 1024)} MB");
            }

            switch (kind)
            {
                case Kind.Set:
                    TriggerSet? set;
                    try
                    {
                        set = JsonSerializer.Deserialize<TriggerSet>(bytes, ConfigurationStore.SerializerOptions);
                    }
                    catch (JsonException ex)
                    {
                        return (null, $"{name} is not a trigger set: {ex.Message}");
                    }

                    if (set is null || string.IsNullOrWhiteSpace(set.Name))
                    {
                        return (null, $"{name} has no set name");
                    }

                    sets.Add(set);
                    break;
                case Kind.Script:
                    scripts[name["scripts/".Length..]] = bytes;
                    break;
                default:
                    media[name["media/".Length..]] = bytes;
                    break;
            }
        }

        if (sets.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            return (null, $"two sets are called '{twice.Key}'");
        }

        return (new PackageContents(manifest, sets, scripts, media, ignored), null);
    }

    private enum Kind
    {
        Set,
        Script,
        Media,
    }

    /// <summary>Which of the three folders a safe entry name is in, or null for a name this reads nothing from.</summary>
    private static Kind? Classify(string name)
    {
        if (!IsSafe(name))
        {
            return null;
        }

        var segments = name.Split('/');
        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        return segments[0] switch
        {
            "sets" when segments.Length == 2 && extension == "json" => Kind.Set,
            "scripts" when segments.Length >= 2 && extension == "lua" => Kind.Script,
            "media" when segments.Length >= 2 && MediaExtensions.Contains(extension) => Kind.Media,
            _ => null,
        };
    }

    /// <summary>A relative name of plain segments: no absolute path, drive, backslash, <c>.</c> or <c>..</c>.</summary>
    public static bool IsSafe(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 260 || name[0] == '/' || name.Contains('\\') || name.Contains(':'))
        {
            return false;
        }

        foreach (var segment in name.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == ".." || segment.Any(char.IsControl))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The entry's bytes, or null once it passes <paramref name="cap"/> or the running total passes its own.</summary>
    private static byte[]? ReadCapped(ZipArchiveEntry entry, long cap, ref long total)
    {
        using var input = entry.Open();
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (output.Length + read > cap || total > MaxTotalBytes)
            {
                return null;
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
