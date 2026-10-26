using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Tui;

/// <summary>
/// Packages: installing one from a file or an https link through the review, and the Packages screen (F10)
/// that lists, switches off, copies from and removes them.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Installed packages; memory-only unless <c>Program</c> hands it a folder.</summary>
    private readonly PackageStore _packages;

    private readonly ListOverlay _packageView;

    /// <summary>The media folders this app added to the sound cache, so a change can take them back out.</summary>
    private readonly List<string> _packageMedia = new();

    private int _packageSelected;
    private bool _confirmRemove;

    /// <summary>How an https package is downloaded; a test supplies one, the client uses <see cref="DownloadPackageAsync"/>.</summary>
    internal Func<Uri, CancellationToken, Task<byte[]?>>? PackageFetch { get; set; }

    internal ListOverlay PackageView => _packageView;

    internal PackageStore Packages => _packages;

    /// <summary>Reads the installed packages and hands their sets and media to the rest of the client.</summary>
    private void LoadPackages()
    {
        _packages.Load();
        SyncPackages();
    }

    private void SyncPackages()
    {
        _config.Packages = _packages.Installed;
        foreach (var folder in _packageMedia)
        {
            _media.LocalFolders.Remove(folder);
        }

        _packageMedia.Clear();
        foreach (var folder in _packages.MediaFolders)
        {
            if (!_config.DisabledPackages.Contains(Path.GetFileName(Path.GetDirectoryName(folder)!)))
            {
                _media.LocalFolders.Add(folder);
                _packageMedia.Add(folder);
            }
        }
    }

    /// <summary><c>/package</c> opens the Packages screen; <c>/package install &lt;file or https link&gt;</c> installs one.</summary>
    private void PackageCommand(string argument)
    {
        if (argument.Length == 0)
        {
            ShowPackages();
            return;
        }

        if (argument.StartsWith("install", StringComparison.OrdinalIgnoreCase))
        {
            InstallPackage(argument[7..].Trim());
            return;
        }

        if (argument.Equals("browse", StringComparison.OrdinalIgnoreCase))
        {
            BrowsePackages();
            return;
        }

        RefuseCommand("/package, /package browse, or /package install <file or https link>");
    }

    /// <summary>Reads a package from a path or an https link and opens its review. Nothing is written here.</summary>
    internal void InstallPackage(string source)
    {
        source = source.Trim().Trim('"');
        if (source.Length == 0)
        {
            RefuseCommand("/package install needs a file or an https link");
            return;
        }

        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            RefuseCommand("packages are fetched over https only");
            return;
        }

        if (source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                RefuseCommand("that is not a link");
                return;
            }

            Fetch(uri, "package", bytes => ReviewPackage(bytes));
            return;
        }

        byte[] archive;
        try
        {
            var info = new FileInfo(Path.GetFullPath(source));
            if (!info.Exists)
            {
                RefuseCommand($"no file at {Snippet(source)}");
                return;
            }

            if (info.Length > PackageArchive.MaxArchiveBytes)
            {
                RefuseCommand("that file is too large to be a package");
                return;
            }

            archive = File.ReadAllBytes(info.FullName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            RefuseCommand($"could not read {Snippet(source)}: {ex.Message}");
            return;
        }

        ReviewPackage(archive);
    }

    /// <summary>
    /// Opens the review for an archive, or says why it is not a package. From an index, the archive must be
    /// the one <paramref name="listed"/> names — its SHA-256, id and version — before anything is shown.
    /// </summary>
    internal void ReviewPackage(byte[] archive, PackageIndexEntry? listed = null)
    {
        if (listed?.Verify(archive, inside: null) is { } mismatch)
        {
            RefuseCommand($"not installed: {mismatch}");
            return;
        }

        var (contents, problem) = PackageArchive.Read(archive);
        if (contents is null)
        {
            RefuseCommand($"not installed: {problem}");
            return;
        }

        if (listed?.Verify(archive, contents.Manifest) is { } different)
        {
            RefuseCommand($"not installed: {different}");
            return;
        }

        if (AnyOverlayOpen && !_packageView.IsOpen)
        {
            RefuseCommand($"close {OpenOverlayName()} first");
            return;
        }

        var existing = _packages.Find(contents.Manifest.Id);
        _packageView.Open(
            "review",
            existing is null ? "Install package" : "Update package",
            width => PackageScreen.Review(contents, CharactersFor(contents.Manifest), existing, width),
            key =>
            {
                if (key.Key != ConsoleKey.Enter)
                {
                    return true;
                }

                if (_packages.Install(contents) is { } failed)
                {
                    RefuseCommand($"not installed: {failed}");
                    return false;
                }

                SyncPackages();
                SaveConfiguration();
                Notice(
                    $"Installed {contents.Manifest.Name} {contents.Manifest.Version} · F10 lists your packages",
                    MessageSeverity.Info,
                    key: "package");
                return false;
            });
    }

    /// <summary>
    /// Downloads <paramref name="uri"/> off the UI thread and hands the bytes back on it, or says why not.
    /// </summary>
    private void Fetch(Uri uri, string what, Action<byte[]> then)
    {
        Notice($"Fetching {Snippet(uri.Host)}…", MessageSeverity.Info, key: "package");
        var fetch = PackageFetch ?? DownloadPackageAsync;
        _ = Task.Run(async () =>
        {
            byte[]? bytes = null;
            string? problem = null;
            try
            {
                bytes = await fetch(uri, CancellationToken.None).ConfigureAwait(false);
                problem = bytes is null ? "the download failed or was too large" : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                problem = ex.Message;
            }

            OnUiThread(() =>
            {
                if (bytes is null)
                {
                    RefuseCommand($"{what} not fetched: {problem}");
                    return;
                }

                then(bytes);
            });
        });
    }

    /// <summary>
    /// Browse packages: fetches the index — only now, never at start — and lists it with the focused world's
    /// packages first. ⏎ fetches one, checks it against the index's SHA-256 and opens the review.
    /// </summary>
    internal void BrowsePackages()
    {
        if (ComposerIsInTheWay("the packages") || (AnyOverlayOpen && !_packageView.IsOpen && Refuse($"close {OpenOverlayName()} first")))
        {
            return;
        }

        if (!Uri.TryCreate(_config.PackageIndexUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            RefuseCommand("the package index is not an https link (packageIndexUrl in config.json)");
            return;
        }

        Fetch(uri, "package index", bytes =>
        {
            if (bytes.Length > PackageIndex.MaxIndexBytes)
            {
                RefuseCommand("the package index is too large");
                return;
            }

            var (index, problem) = PackageIndex.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            if (index is null)
            {
                RefuseCommand($"package index not read: {problem}");
                return;
            }

            ShowIndex(index);
        });
    }

    /// <summary>
    /// The world of the focused window: its session's, else its recorded owner's — so a disconnected
    /// character's window still names its game. Never <c>_active</c>.
    /// </summary>
    private WorldDefinition? FocusedWorld()
    {
        if (SendTarget()?.World is { } live)
        {
            return live;
        }

        var owner = _workspace.FindWindow(ActiveWindowId())?.SessionKey;
        return owner is null
            ? null
            : _config.Worlds.FirstOrDefault(w => w.Characters.Any(c => string.Equals($"{w.Name}.{c.Name}", owner, StringComparison.Ordinal)));
    }

    private void ShowIndex(PackageIndex index)
    {
        var world = FocusedWorld();
        var entries = index.For(world?.Host, world?.Port ?? 0);
        _packageSelected = 0;
        _packageView.Open(
            "browse",
            "Browse packages",
            width => PackageScreen.Browse(entries, _packages.Installed, _packageSelected, world?.Name, index.Dropped, width),
            key =>
            {
                switch (key.Key)
                {
                    case ConsoleKey.UpArrow:
                        _packageSelected = Math.Max(0, _packageSelected - 1);
                        return true;
                    case ConsoleKey.DownArrow:
                        _packageSelected = Math.Min(Math.Max(0, entries.Count - 1), _packageSelected + 1);
                        return true;
                    case ConsoleKey.Enter when entries.Count > 0:
                        var entry = entries[Math.Clamp(_packageSelected, 0, entries.Count - 1)];
                        Fetch(new Uri(entry.Url), "package", bytes => ReviewPackage(bytes, entry));
                        return false;
                    default:
                        return true;
                }
            });
    }

    /// <summary>The Packages screen, or closes it.</summary>
    internal void ShowPackages()
    {
        if (_packageView.IsOpen)
        {
            _packageView.Close();
            return;
        }

        if (ComposerIsInTheWay("the packages") || (AnyOverlayOpen && Refuse($"close {OpenOverlayName()} first")))
        {
            return;
        }

        _packageSelected = 0;
        _confirmRemove = false;
        _packageView.Open(
            "list",
            "Packages",
            width => PackageScreen.List(
                _packages.Installed, _config.DisabledPackages, _packageSelected, _confirmRemove, p => CharactersFor(p.Manifest), width),
            OnPackageKey);
    }

    private bool Refuse(string reason)
    {
        RefuseCommand(reason);
        return true;
    }

    private bool OnPackageKey(ConsoleKeyInfo key)
    {
        var installed = _packages.Installed;
        var confirming = _confirmRemove;
        _confirmRemove = false;
        if (key.Key == ConsoleKey.B)
        {
            BrowsePackages();
            return false;
        }

        if (installed.Count == 0)
        {
            return key.Key is not (ConsoleKey.F10 or ConsoleKey.Enter);
        }

        var current = installed[Math.Clamp(_packageSelected, 0, installed.Count - 1)];
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                _packageSelected = Math.Max(0, _packageSelected - 1);
                break;
            case ConsoleKey.DownArrow:
                _packageSelected = Math.Min(installed.Count - 1, _packageSelected + 1);
                break;
            case ConsoleKey.Spacebar:
                if (!_config.DisabledPackages.Remove(current.Id))
                {
                    _config.DisabledPackages.Add(current.Id);
                }

                SyncPackages();
                SaveConfiguration();
                break;
            case ConsoleKey.C:
                CopyPackageSets(current);
                break;
            case ConsoleKey.Delete or ConsoleKey.Backspace when confirming:
                if (_packages.Remove(current.Id) is { } problem)
                {
                    RefuseCommand(problem);
                }

                _config.DisabledPackages.Remove(current.Id);
                _packageSelected = Math.Max(0, Math.Min(_packageSelected, _packages.Installed.Count - 1));
                SyncPackages();
                SaveConfiguration();
                Notice($"Removed {current.Manifest.Name}.", MessageSeverity.Info, key: "package");
                break;
            case ConsoleKey.Delete or ConsoleKey.Backspace:
                _confirmRemove = true;
                break;
            case ConsoleKey.F10:
                return false;
        }

        return true;
    }

    /// <summary>
    /// Copies a package's sets into the player's own, where F2–F6 can edit them and an update will not
    /// replace them. A name already taken gets <c>(copy)</c>.
    /// </summary>
    private void CopyPackageSets(InstalledPackage package)
    {
        foreach (var set in package.Sets)
        {
            var copy = JsonSerializer.Deserialize<TriggerSet>(
                JsonSerializer.SerializeToUtf8Bytes(set, ConfigurationStore.SerializerOptions), ConfigurationStore.SerializerOptions)!;
            var name = set.Name;
            for (var n = 1; _config.TriggerSets.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            {
                name = n == 1 ? $"{set.Name} (copy)" : $"{set.Name} (copy {n})";
            }

            copy.Name = name;
            _config.TriggerSets.Add(copy);
        }

        SaveConfiguration();
        Notice(
            $"Copied {package.Sets.Count} set(s) to yours; assign them on F5, and switch the package off (Space) to use only your copy.",
            MessageSeverity.Info,
            key: "package");
    }

    /// <summary>The characters a package would apply to: everyone on a world it is for.</summary>
    private IReadOnlyList<string> CharactersFor(PackageManifest manifest) =>
        _config.Worlds
            .Where(w => manifest.IsFor(w.Host, w.Port))
            .SelectMany(w => w.Characters.Select(c => $"{c.Name} ({w.Name})"))
            .ToList();

    /// <summary>
    /// The snapshot's packages, built as archives and read through <see cref="PackageArchive.Read"/> like any
    /// other, into the store the snapshot was given — which has no folder, so nothing is written.
    /// </summary>
    internal static byte[] DemoPackage(string id, string name, string version, string games, string set, string description)
    {
        var manifest = $$"""
            {"id":"{{id}}","name":"{{name}}","version":"{{version}}","author":"the Aetherfall builders",
             "description":"{{description}}","games":[{{games}}]}
            """;
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entry, text) in new[]
            {
                ("package.json", manifest),
                ("sets/main.json", set),
                ("scripts/status.lua", "-- keeps the status gauges current\nfunction on_vitals() end\n"),
                ("media/bell.ogg", "OggS"),
                ("README.md", "read me"),
            })
            {
                using var stream = zip.CreateEntry(entry).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        return buffer.ToArray();
    }

    private static readonly byte[] DemoBasics = DemoPackage(
        "aetherfall.basics",
        "Aetherfall basics",
        "1.2.0",
        "\"aetherfall.mux:4201\"",
        """
        {"name":"Aetherfall basics","triggers":[
          {"name":"low health","pattern":"^Your wounds","actions":{"sendResponse":"quaff red"}},
          {"name":"tells","pattern":"^\\w+ tells you","actions":{"spawnTarget":"Tells"}}],
         "aliases":[{"name":"k","pattern":"^k (.+)$","substitution":"kill $1"}],
         "scriptFiles":["scripts/status.lua"]}
        """,
        "Health warnings, a tells window and the status gauges.");

    private void LoadDemoPackages(string view)
    {
        if (view == "package-review")
        {
            ReviewPackage(DemoBasics);
            return;
        }

        if (view == "package-browse")
        {
            // Installed at an older version, so the list shows an update beside it.
            _packages.Install(PackageArchive.Read(DemoPackage(
                "aetherfall.basics", "Aetherfall basics", "1.1.0", "\"aetherfall.mux:4201\"", """{"name":"Aetherfall basics"}""", "")).Contents!);
            SyncPackages();
            var hash = new string('0', 64);
            var (index, _) = PackageIndex.Parse($$"""
                {"packages":[
                  {"id":"mush.colours","name":"MUSH colour names","version":"0.3.1","games":["*"],
                   "description":"Highlights pages and channel names on any game.",
                   "url":"https://packages.example/mush-colours-0.3.1.zip","sha256":"{{hash}}"},
                  {"id":"grapevine.channels","name":"Grapevine channels","version":"2.0.0","games":["grapevine.haus"],
                   "url":"https://packages.example/grapevine-channels-2.0.0.zip","sha256":"{{hash}}"},
                  {"id":"aetherfall.basics","name":"Aetherfall basics","version":"1.2.0","author":"the Aetherfall builders",
                   "games":["aetherfall.mux:4201"],"description":"Health warnings, a tells window and the status gauges.",
                   "url":"https://packages.example/aetherfall-basics-1.2.0.zip","sha256":"{{hash}}"},
                  {"id":"aetherfall.mapper","name":"Aetherfall mapper rooms","version":"0.9.0","games":["aetherfall.mux"],
                   "url":"https://packages.example/aetherfall-mapper-0.9.0.zip","sha256":"{{hash}}"},
                  {"id":"bad","name":"Bad","version":"1","games":["*"],"url":"http://x","sha256":"x"}]}
                """);
            ShowIndex(index!);
            return;
        }

        _packages.Install(PackageArchive.Read(DemoBasics).Contents!);
        _packages.Install(PackageArchive.Read(DemoPackage(
            "mush.colours",
            "MUSH colour names",
            "0.3.1",
            "\"*\"",
            """{"name":"MUSH colours","triggers":[{"name":"pages","pattern":"^From afar","actions":{"spawnTarget":"Pages"}}]}""",
            "Highlights pages and channel names on any game.")).Contents!);
        _config.DisabledPackages.Add("mush.colours");
        SyncPackages();
        ShowPackages();
    }

    private static readonly HttpClient PackageClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Downloads an archive over https, stopping at the size cap.</summary>
    private static async Task<byte[]?> DownloadPackageAsync(Uri uri, CancellationToken token)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        using var response = await PackageClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > PackageArchive.MaxArchiveBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > PackageArchive.MaxArchiveBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
