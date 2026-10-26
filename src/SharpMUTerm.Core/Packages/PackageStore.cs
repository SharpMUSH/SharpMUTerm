using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Packages;

/// <summary>One installed package: its manifest and its sets, with script names pointing into its own folder.</summary>
public sealed record InstalledPackage(PackageManifest Manifest, IReadOnlyList<TriggerSet> Sets)
{
    public string Id => Manifest.Id;
}

/// <summary>
/// The installed packages, one folder each under <c>packages/</c> beside <c>config.json</c>:
/// <c>&lt;id&gt;/package.json</c>, <c>sets/</c>, <c>scripts/</c>, <c>media/</c>.
/// <para>
/// <b>A package's sets are never written into the configuration.</b> They are read from its folder at start
/// and handed to <see cref="AppConfiguration.PackageSets"/>, which the settings screens do not list, so an
/// update replaces them wholesale and can never touch a rule the player wrote. A player who wants to change
/// one copies it into their own sets first.
/// </para>
/// <para>
/// <b>A package's scripts are named <c>package:&lt;id&gt;/&lt;file&gt;</c></b> in its sets, and that form
/// resolves under that package's own <c>scripts/</c> folder and nowhere else (<see cref="ResolveScript"/>).
/// </para>
/// <para>
/// With no root it is memory-only by construction — the <c>mssp:</c> rule: an install holds the sets for the
/// run, writes nothing, and has no scripts or media to offer, which is what a test and a snapshot get.
/// </para>
/// </summary>
public sealed class PackageStore
{
    public const string ScriptPrefix = "package:";

    private readonly string? _root;
    private readonly Dictionary<string, InstalledPackage> _installed = new(StringComparer.Ordinal);

    public PackageStore(string? root = null)
    {
        _root = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
    }

    public ILogger Logger { get; init; } = NullLogger.Instance;

    public bool HasRoot => _root is not null;

    /// <summary>The folder beside a configuration file: <c>packages/</c>.</summary>
    public static string PathFor(string configPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".", "packages");

    public IReadOnlyList<InstalledPackage> Installed =>
        _installed.Values.OrderBy(p => p.Manifest.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public InstalledPackage? Find(string id) => _installed.GetValueOrDefault(id);

    /// <summary>Each installed package's <c>media/</c> folder, for the sound cache to look in before fetching.</summary>
    public IEnumerable<string> MediaFolders =>
        _root is null ? [] : _installed.Keys.Select(id => Path.Combine(_root, id, "media")).Where(Directory.Exists);

    /// <summary>Reads every package folder. One that does not read is skipped and logged, never fatal.</summary>
    public void Load()
    {
        _installed.Clear();
        if (_root is null || !Directory.Exists(_root))
        {
            return;
        }

        foreach (var folder in Directory.EnumerateDirectories(_root))
        {
            var name = Path.GetFileName(folder);
            if (name.StartsWith('.'))
            {
                continue; // an install that did not finish
            }

            try
            {
                var (manifest, problem) = PackageManifest.Parse(File.ReadAllText(Path.Combine(folder, "package.json")));
                if (manifest is null || manifest.Id != name)
                {
                    Logger.LogWarning("Package folder {Folder} skipped: {Problem}", folder, problem ?? "its id is not its folder's name");
                    continue;
                }

                var sets = new List<TriggerSet>();
                var setsFolder = Path.Combine(folder, "sets");
                if (Directory.Exists(setsFolder))
                {
                    foreach (var file in Directory.EnumerateFiles(setsFolder, "*.json").Order(StringComparer.Ordinal))
                    {
                        if (JsonSerializer.Deserialize<TriggerSet>(File.ReadAllText(file), ConfigurationStore.SerializerOptions) is { } set)
                        {
                            sets.Add(set);
                        }
                    }
                }

                _installed[manifest.Id] = new InstalledPackage(manifest, Bind(manifest.Id, sets));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Logger.LogWarning("Package folder {Folder} skipped: {Reason}", folder, ex.Message);
            }
        }
    }

    /// <summary>
    /// Installs <paramref name="contents"/>, replacing any version of the same package. The new folder is
    /// written beside the old one and swapped in, so a failure part-way leaves the old version in place.
    /// Returns the problem, or null.
    /// </summary>
    public string? Install(PackageContents contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var id = contents.Manifest.Id;
        if (_root is not null)
        {
            var target = Path.Combine(_root, id);
            var staging = Path.Combine(_root, "." + id + ".new");
            try
            {
                Directory.CreateDirectory(_root);
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }

                Directory.CreateDirectory(staging);
                File.WriteAllText(Path.Combine(staging, "package.json"), contents.Manifest.ToJson());
                for (var i = 0; i < contents.Sets.Count; i++)
                {
                    Write(staging, $"sets/{i:D3}.json", JsonSerializer.SerializeToUtf8Bytes(contents.Sets[i], ConfigurationStore.SerializerOptions));
                }

                foreach (var (name, bytes) in contents.Scripts)
                {
                    Write(staging, "scripts/" + name, bytes);
                }

                foreach (var (name, bytes) in contents.Media)
                {
                    Write(staging, "media/" + name, bytes);
                }

                var old = Path.Combine(_root, "." + id + ".old");
                if (Directory.Exists(old))
                {
                    Directory.Delete(old, recursive: true);
                }

                if (Directory.Exists(target))
                {
                    Directory.Move(target, old);
                }

                Directory.Move(staging, target);
                if (Directory.Exists(old))
                {
                    Directory.Delete(old, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"could not write the package: {ex.Message}";
            }
        }

        _installed[id] = new InstalledPackage(contents.Manifest, Bind(id, contents.Sets));
        return null;
    }

    /// <summary>Removes a package and its folder. Returns the problem, or null.</summary>
    public string? Remove(string id)
    {
        if (!_installed.Remove(id))
        {
            return $"no package '{id}' is installed";
        }

        if (_root is not null && PackageArchive.IsSafe(id) && Directory.Exists(Path.Combine(_root, id)))
        {
            try
            {
                Directory.Delete(Path.Combine(_root, id), recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"removed, but its folder could not be deleted: {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>
    /// The file a <c>package:&lt;id&gt;/&lt;file&gt;</c> script name points at, inside that package's own
    /// <c>scripts/</c> folder, or null for any other name or one that leaves the folder.
    /// </summary>
    public string? ResolveScript(string file)
    {
        if (_root is null || !file.StartsWith(ScriptPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = file[ScriptPrefix.Length..];
        var slash = rest.IndexOf('/');
        if (slash <= 0)
        {
            return null;
        }

        var id = rest[..slash];
        var relative = rest[(slash + 1)..];
        if (!_installed.ContainsKey(id) || !PackageArchive.IsSafe(relative))
        {
            return null;
        }

        var folder = Path.Combine(_root, id, "scripts");
        var full = Path.GetFullPath(relative, folder);
        return full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    /// <summary>
    /// The sets as they run: each script name that is a plain relative path becomes this package's
    /// <c>package:&lt;id&gt;/…</c>, so a package's set can only name its own scripts.
    /// </summary>
    private static List<TriggerSet> Bind(string id, IEnumerable<TriggerSet> sets) => sets.Select(set =>
    {
        var copy = JsonSerializer.Deserialize<TriggerSet>(
            JsonSerializer.SerializeToUtf8Bytes(set, ConfigurationStore.SerializerOptions), ConfigurationStore.SerializerOptions)!;
        copy.ScriptFiles = set.ScriptFiles
            .Select(f => f.StartsWith("scripts/", StringComparison.Ordinal) ? f["scripts/".Length..] : f)
            .Select(f => $"{ScriptPrefix}{id}/{f}")
            .ToList();
        return copy;
    }).ToList();

    private static void Write(string root, string relative, byte[] bytes)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
