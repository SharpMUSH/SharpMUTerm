using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using SharpMUTerm.Client.Packages;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Tui;

/// <summary>
/// Packages: installing one from a file, an https link or the index through the review, and F10 that lists, switches
/// off, copies from and removes them — as cell screens in a terminal and as the settings window's package page where
/// the host has one. Both surfaces call the same methods here: <see cref="SetPackageEnabled"/>,
/// <see cref="RemovePackage"/>, <see cref="CopyPackageSets"/>, and one consent path, <see cref="AcceptPackage"/>.
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

    /// <summary>The index as last fetched this run, or null: it is fetched only when the player browses.</summary>
    private PackageIndex? _packageIndex;

    /// <summary>The archive under review and what its review says, until it is installed or dropped.</summary>
    private (PackageContents Contents, PackageReviewModel Model)? _pendingPackage;

    private PackageManager? _packageManager;

    /// <summary>How an https package is downloaded; a test supplies one, the client uses <see cref="DownloadPackageAsync"/>.</summary>
    internal Func<Uri, CancellationToken, Task<byte[]?>>? PackageFetch { get; set; }

    internal ListOverlay PackageView => _packageView;

    internal PackageStore Packages => _packages;

    /// <summary>F10's declaration, from the registry.</summary>
    private PackageCollection PackageDeclaration => Section("packages").Packages ?? BuiltInSettings.Packages.Packages!;

    /// <summary>
    /// F10 as the window's package page reads it — one per app, so the index fetched and a review waiting outlive the
    /// settings window being closed and opened again.
    /// </summary>
    internal PackageManager PackageManager => _packageManager ??= new PackageManager(_config, PackageDeclaration, new PackageHostCalls(this));

    /// <summary>Whether packages are shown in the host's own settings window rather than as cell screens.</summary>
    private bool NativePackages => Host.NativeSettings is not null;

    /// <summary>The review waiting for consent, for a test.</summary>
    internal PackageReviewModel? PendingPackageReview => _pendingPackage?.Model;

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

        if (_packageIndex is not null)
        {
            _packageManager?.PublishIndex(_packageIndex, PackageBrowseModel.From(_packageIndex, _packages.Installed, FocusedWorld()));
        }

        _packageManager?.NotifyChanged();
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

    /// <summary>Says why a package step did not happen: on the status row, and on the window's package page.</summary>
    private void PackageRefused(string reason)
    {
        RefuseCommand(reason);
        _packageManager?.PublishStatus(reason);
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
    /// Opens the review for an archive, or says why it is not a package. From an index, the archive must be the one
    /// <paramref name="listed"/> names — its SHA-256, id and version — before anything is shown. The review is one
    /// model (<see cref="PackageReviewModel"/>) whichever host draws it, and nothing is written until
    /// <see cref="AcceptPackage"/>.
    /// </summary>
    internal void ReviewPackage(byte[] archive, PackageIndexEntry? listed = null)
    {
        if (listed?.Verify(archive, inside: null) is { } mismatch)
        {
            PackageRefused($"not installed: {mismatch}");
            return;
        }

        var (contents, problem) = PackageArchive.Read(archive);
        if (contents is null)
        {
            PackageRefused($"not installed: {problem}");
            return;
        }

        if (listed?.Verify(archive, contents.Manifest) is { } different)
        {
            PackageRefused($"not installed: {different}");
            return;
        }

        if (!NativePackages && AnyOverlayOpen && !_packageView.IsOpen)
        {
            RefuseCommand($"close {OpenOverlayName()} first");
            return;
        }

        var model = PackageReviewModel.From(
            contents,
            PackageCollection.CharactersFor(_config, contents.Manifest),
            _packages.Find(contents.Manifest.Id));
        _pendingPackage = (contents, model);
        if (NativePackages)
        {
            PackageManager.PublishReview(model);
            TryOpenNativeSettings(Section("packages"));
            return;
        }

        _packageView.Open(
            "review",
            model.Title,
            width => PackageScreen.Review(model, width),
            key =>
            {
                if (key.Key != ConsoleKey.Enter)
                {
                    return true;
                }

                AcceptPackage(model);
                return false;
            });
    }

    /// <summary>
    /// The consent: installs the package <paramref name="review"/> described — only while it is still the review
    /// waiting — and saves. Both hosts' install buttons end here, and nothing else writes a package.
    /// </summary>
    internal bool AcceptPackage(PackageReviewModel review)
    {
        if (_pendingPackage is not { } pending || !ReferenceEquals(pending.Model, review))
        {
            PackageRefused("that review is no longer open");
            return false;
        }

        _pendingPackage = null;
        _packageManager?.PublishReview(null);
        var manifest = pending.Contents.Manifest;
        if (_packages.Install(pending.Contents) is { } failed)
        {
            PackageRefused($"not installed: {failed}");
            return false;
        }

        SyncPackages();
        SaveConfiguration();
        var said = $"Installed {manifest.Name} {manifest.Version} · F10 lists your packages";
        Notice(said, MessageSeverity.Info, source: SharpMUTerm.Client.Chrome.NoticeModel.Sources.Packages);
        _packageManager?.PublishStatus(said);
        return true;
    }

    /// <summary>Drops the review <paramref name="review"/> without writing anything.</summary>
    internal void CancelPackage(PackageReviewModel review)
    {
        if (_pendingPackage is { } pending && ReferenceEquals(pending.Model, review))
        {
            _pendingPackage = null;
            _packageManager?.PublishReview(null);
        }
    }

    /// <summary>
    /// Downloads <paramref name="uri"/> off the UI thread and hands the bytes back on it, or says why not.
    /// </summary>
    private void Fetch(Uri uri, string what, Action<byte[]> then)
    {
        Notice($"Fetching {Snippet(uri.Host)}…", MessageSeverity.Info, source: SharpMUTerm.Client.Chrome.NoticeModel.Sources.Packages);
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
                    PackageRefused($"{what} not fetched: {problem}");
                    return;
                }

                then(bytes);
            });
        });
    }

    /// <summary>
    /// Browse packages: fetches the index — only now, never at start — and lists it with the focused world's
    /// packages first. In a host with a settings window the list is the package page's Browse tab; otherwise the
    /// cell list, where ⏎ fetches one, checks it against the index's SHA-256 and opens the review.
    /// </summary>
    internal void BrowsePackages()
    {
        if (!NativePackages
            && (ComposerIsInTheWay("the packages") || (AnyOverlayOpen && !_packageView.IsOpen && Refuse($"close {OpenOverlayName()} first"))))
        {
            return;
        }

        if (!Uri.TryCreate(_config.PackageIndexUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            PackageRefused("the package index is not an https link (packageIndexUrl in config.json)");
            return;
        }

        if (NativePackages)
        {
            PackageManager.PublishFetching();
            TryOpenNativeSettings(Section("packages"));
        }

        Fetch(uri, "package index", bytes =>
        {
            if (bytes.Length > PackageIndex.MaxIndexBytes)
            {
                PackageRefused("the package index is too large");
                return;
            }

            var (index, problem) = PackageIndex.Parse(Encoding.UTF8.GetString(bytes));
            if (index is null)
            {
                PackageRefused($"package index not read: {problem}");
                return;
            }

            ShowIndex(index);
        });
    }

    /// <summary>Fetches the archive an index entry names and opens its review once it is the archive the entry names.</summary>
    internal void InstallFromIndex(PackageIndexEntry entry) =>
        Fetch(new Uri(entry.Url), "package", bytes => ReviewPackage(bytes, entry));

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

    /// <summary>The index arrived: kept for the run (so F10 can say which packages have updates), then listed.</summary>
    private void ShowIndex(PackageIndex index)
    {
        _packageIndex = index;
        var model = PackageBrowseModel.From(index, _packages.Installed, FocusedWorld());
        if (NativePackages)
        {
            PackageManager.PublishIndex(index, model);
            return;
        }

        var entries = model.Rows;
        _packageSelected = 0;
        _packageView.Open(
            "browse",
            "Browse packages",
            width => PackageScreen.Browse(model, _packageSelected, width),
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
                        InstallFromIndex(entries[Math.Clamp(_packageSelected, 0, entries.Count - 1)].Entry);
                        return false;
                    default:
                        return true;
                }
            });
    }

    /// <summary>The installed packages as F10 lists them, each with what the index said of it this run.</summary>
    private IReadOnlyList<PackageItem> PackageItems() => PackageCollection.Items(_config, _packageIndex);

    /// <summary>The Packages screen — the settings window's package page where the host has one — or closes it.</summary>
    internal void ShowPackages()
    {
        if (NativePackages)
        {
            TryOpenNativeSettings(Section("packages"));
            return;
        }

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
            SectionTitle("packages"),
            width => PackageScreen.List(PackageDeclaration, _config, PackageItems(), _packageSelected, _confirmRemove, width),
            OnPackageKey);
    }

    /// <summary>The ids of the fields the cell F10 draws, in order, for a test to hold against the window's.</summary>
    internal IReadOnlyList<string> CellPackageFields()
    {
        var drawn = new List<string>();
        PackageScreen.List(PackageDeclaration, _config, PackageItems(), _packageSelected, false, 100, drawn);
        return drawn;
    }

    private bool Refuse(string reason)
    {
        RefuseCommand(reason);
        return true;
    }

    private bool OnPackageKey(ConsoleKeyInfo key)
    {
        var installed = PackageItems();
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
                SetPackageEnabled(current.Id, !PackageCollection.IsEnabled(_config, current.Id));
                break;
            case ConsoleKey.C:
                CopyPackageSets(current.Id);
                break;
            case ConsoleKey.Delete or ConsoleKey.Backspace when confirming:
                RemovePackage(current.Id);
                _packageSelected = Math.Max(0, Math.Min(_packageSelected, _packages.Installed.Count - 1));
                break;
            case ConsoleKey.Delete or ConsoleKey.Backspace:
                _confirmRemove = true;
                break;
            case ConsoleKey.F10:
                return false;
        }

        return true;
    }

    /// <summary>Switches a package on or off and saves once — F10's Space and the window's check box.</summary>
    internal void SetPackageEnabled(string id, bool on)
    {
        if (_packages.Find(id) is null || !PackageCollection.SetEnabled(_config, id, on))
        {
            return;
        }

        SyncPackages();
        SaveConfiguration();
    }

    /// <summary>Removes a package and its folder and saves — F10's Del Del and the window's Remove.</summary>
    internal void RemovePackage(string id)
    {
        if (_packages.Find(id) is not { } package)
        {
            PackageRefused($"no package '{id}' is installed");
            return;
        }

        if (_packages.Remove(id) is { } problem)
        {
            PackageRefused(problem);
        }

        PackageCollection.SetEnabled(_config, id, on: true);
        SyncPackages();
        SaveConfiguration();
        var said = $"Removed {package.Manifest.Name}.";
        Notice(said, MessageSeverity.Info, source: SharpMUTerm.Client.Chrome.NoticeModel.Sources.Packages);
        _packageManager?.PublishStatus(said);
    }

    /// <summary>
    /// Copies a package's sets into the player's own, where F2–F6 can edit them and an update will not
    /// replace them. A name already taken gets <c>(copy)</c>.
    /// </summary>
    internal void CopyPackageSets(string id)
    {
        if (_packages.Find(id) is not { } package)
        {
            PackageRefused($"no package '{id}' is installed");
            return;
        }

        foreach (var set in package.Sets)
        {
            SettingsCollection.CopyToEdit(_config, set);
        }

        SaveConfiguration();
        var said = $"Copied {package.Sets.Count} set(s) to yours; assign them on F5, and switch the package off (Space) to use only your copy.";
        Notice(said, MessageSeverity.Info, source: SharpMUTerm.Client.Chrome.NoticeModel.Sources.Packages);
        _packageManager?.PublishStatus(said);
        _packageManager?.NotifyChanged();
    }

    /// <summary>The window's package page's gestures, each run on the client's thread.</summary>
    private sealed class PackageHostCalls(SharpMUTermApp app) : IPackageHost
    {
        public void SetEnabled(string id, bool on) => app.OnUiThread(() => app.SetPackageEnabled(id, on));

        public void Remove(string id) => app.OnUiThread(() => app.RemovePackage(id));

        public void CopySets(string id) => app.OnUiThread(() => app.CopyPackageSets(id));

        public void Browse() => app.OnUiThread(app.BrowsePackages);

        public void Install(PackageIndexEntry entry) => app.OnUiThread(() => app.InstallFromIndex(entry));

        public void Accept(PackageReviewModel review) => app.OnUiThread(() => app.AcceptPackage(review));

        public void Cancel(PackageReviewModel review) => app.OnUiThread(() => app.CancelPackage(review));
    }

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

    private static readonly byte[] DemoColours = DemoPackage(
        "mush.colours",
        "MUSH colour names",
        "0.3.1",
        "\"*\"",
        """{"name":"MUSH colours","triggers":[{"name":"pages","pattern":"^From afar","actions":{"spawnTarget":"Pages"}}]}""",
        "Highlights pages and channel names on any game.");

    private static readonly byte[] DemoGrapevine = DemoPackage(
        "grapevine.channels", "Grapevine channels", "2.0.0", "\"grapevine.haus\"",
        """{"name":"Grapevine channels","triggers":[{"name":"gossip","pattern":"^\\[gossip\\]","actions":{"spawnTarget":"Gossip"}}]}""",
        "Puts each Grapevine channel in a window of its own.");

    private static readonly byte[] DemoMapper = DemoPackage(
        "aetherfall.mapper", "Aetherfall mapper rooms", "0.9.0", "\"aetherfall.mux\"",
        """{"name":"Aetherfall mapper","triggers":[{"name":"exits","pattern":"^Obvious exits:","actions":{}}]}""",
        "Reads room names and exits for the map.");

    /// <summary>The demo's index: four packages, the files it names served by <see cref="DemoFetch"/>, and one broken entry.</summary>
    private static string DemoIndex() => $$"""
        {"packages":[
          {"id":"mush.colours","name":"MUSH colour names","version":"0.3.1","games":["*"],
           "description":"Highlights pages and channel names on any game.",
           "url":"https://packages.example/mush-colours-0.3.1.zip","sha256":"{{Sha(DemoColours)}}"},
          {"id":"grapevine.channels","name":"Grapevine channels","version":"2.0.0","games":["grapevine.haus"],
           "description":"Puts each Grapevine channel in a window of its own.",
           "url":"https://packages.example/grapevine-channels-2.0.0.zip","sha256":"{{Sha(DemoGrapevine)}}"},
          {"id":"aetherfall.basics","name":"Aetherfall basics","version":"1.2.0","author":"the Aetherfall builders",
           "games":["aetherfall.mux:4201"],"description":"Health warnings, a tells window and the status gauges.",
           "url":"https://packages.example/aetherfall-basics-1.2.0.zip","sha256":"{{Sha(DemoBasics)}}"},
          {"id":"aetherfall.mapper","name":"Aetherfall mapper rooms","version":"0.9.0","games":["aetherfall.mux"],
           "description":"Reads room names and exits for the map.",
           "url":"https://packages.example/aetherfall-mapper-0.9.0.zip","sha256":"{{Sha(DemoMapper)}}"},
          {"id":"bad","name":"Bad","version":"1","games":["*"],"url":"http://x","sha256":"x"}]}
        """;

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>
    /// The demo's network: the index and the four archives it names, from memory. Only the demo scene sets it, so a
    /// window demo can browse and install for real without a byte leaving the machine.
    /// </summary>
    private static Task<byte[]?> DemoFetch(Uri uri, CancellationToken token) => Task.FromResult<byte[]?>(uri.AbsolutePath switch
    {
        var p when p.EndsWith("index.json", StringComparison.Ordinal) => Encoding.UTF8.GetBytes(DemoIndex()),
        "/mush-colours-0.3.1.zip" => DemoColours,
        "/grapevine-channels-2.0.0.zip" => DemoGrapevine,
        "/aetherfall-basics-1.2.0.zip" => DemoBasics,
        "/aetherfall-mapper-0.9.0.zip" => DemoMapper,
        _ => null,
    });

    private void LoadDemoPackages(string view)
    {
        PackageFetch ??= DemoFetch;
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
            if (NativePackages)
            {
                PackageManager.PublishFetching();
                TryOpenNativeSettings(Section("packages"));
            }

            ShowIndex(PackageIndex.Parse(DemoIndex()).Index!);
            return;
        }

        _packages.Install(PackageArchive.Read(DemoBasics).Contents!);
        _packages.Install(PackageArchive.Read(DemoColours).Contents!);
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
