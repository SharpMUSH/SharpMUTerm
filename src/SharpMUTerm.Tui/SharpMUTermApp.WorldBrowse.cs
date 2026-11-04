using System.Globalization;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Accessibility;
using SharpMUTerm.Core.WorldIndex;

namespace SharpMUTerm.Tui;

/// <summary>
/// Browse worlds (the welcome's Browse row, ⌃P ▸ <em>Browse worlds…</em>, <c>/browse [words]</c>): mu*index's listing,
/// one Client model (<see cref="WorldBrowseModel"/>) drawn as a cell <see cref="ListOverlay"/> here and as a native modal
/// in a host that has them. Every gesture in either ends in one method: <see cref="PickWorld"/> and
/// <see cref="AskWorldDirectory"/>.
/// <para>
/// <b>Asked only when the player opens it, never at start</b>, through a fetch the caller supplies (<c>worldFetch:</c>,
/// the <c>save:</c>/<c>logRoot:</c> family): null in a test and a snapshot, which then say they asked nothing. The demo
/// scene answers from memory. <b>A pick saves nothing and dials nothing</b>: it asks the directory where the world is and
/// opens the welcome's connect form filled in, so the player's own ⏎ there — through <c>/connect</c>'s path — is what
/// adds the world.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The cell overlay's mode while Browse worlds is in it.</summary>
    internal const string WorldBrowseMode = "browse-worlds";

    /// <summary>The fetch the live entry point supplies; null in a run that may ask nothing.</summary>
    private readonly Func<Uri, CancellationToken, Task<byte[]?>>? _worldFetch;

    /// <summary>The cell surface's state while it is open.</summary>
    private WorldBrowseSurface? _worldBrowse;

    /// <summary>What Browse worlds lists now, in either host.</summary>
    private WorldBrowseModel _worldModel = new(WorldBrowseState.Loading, [], 0, string.Empty, null, null);

    /// <summary>Bumped by every request, so an answer that arrives after a newer question (or a close) is dropped.</summary>
    private int _worldGeneration;

    /// <summary>The rows <c>/browse</c> last said as lines, so <c>/browse 3</c> names one of them.</summary>
    private IReadOnlyList<WorldListing> _saidWorlds = [];

    /// <summary>
    /// How the directory is asked: the caller's fetch, or one a test or the demo scene sets. Null asks nothing.
    /// </summary>
    internal Func<Uri, CancellationToken, Task<byte[]?>>? WorldFetch { get; set; }

    /// <summary>Whether Browse worlds is open, in cells or natively.</summary>
    internal bool WorldBrowseOpen =>
        (_helpView.IsOpen && _helpView.Mode == WorldBrowseMode) || NativeModalOf(SurfaceCatalog.BrowseWorlds) is not null;

    /// <summary>What Browse worlds lists now, for the tests.</summary>
    internal WorldBrowseModel WorldBrowseModelNow => _worldModel;

    /// <summary>The cell surface's state, for the tests; null while it is shut.</summary>
    internal WorldBrowseSurface? CellWorldBrowse => _helpView.IsOpen && _helpView.Mode == WorldBrowseMode ? _worldBrowse : null;

    /// <summary>The native session while one is open, for the tests.</summary>
    internal WorldBrowseSession? NativeWorldBrowse => _nativeModal as WorldBrowseSession;

    /// <summary>Feeds a key to the cell surface.</summary>
    internal void SimulateWorldBrowseKey(ConsoleKeyInfo key) => _helpView.SimulateKey(key);

    /// <summary>
    /// Opens Browse worlds with <paramref name="query"/> typed into its search — the welcome's Browse row, ⌃P and
    /// <c>/browse</c> — or closes it when it is open (⌃P's entry is a toggle). With no screen to read, the rows are said
    /// as lines instead.
    /// </summary>
    internal void ShowWorldBrowse(string query = "")
    {
        if (NativeModalOf(SurfaceCatalog.BrowseWorlds) is { } native)
        {
            CloseNativeModal(native);
            return;
        }

        if (_helpView.IsOpen && _helpView.Mode == WorldBrowseMode)
        {
            _helpView.Close();
            return;
        }

        query = WorldDirectory.Clean(query, WorldDirectory.MaxQuery);
        if (!Host.ScreenIsSeen)
        {
            SayWorlds(query);
            return;
        }

        if (ComposerIsInTheWay("Browse worlds") || (AnyOverlayOpen && Refuse($"close {OpenOverlayName()} first")))
        {
            return;
        }

        // The list is the directory's most played; what is typed narrows it as the player types. Opened on a query
        // (/browse words), the directory itself is asked, since its most played may hold none of them.
        StartWorldList(query);
        if (NativeModalsHost is { } host)
        {
            WorldBrowseSession? session = null;
            session = new WorldBrowseSession(
                _worldModel,
                ThemeIsDark(),
                index => OnUiThread(() => PickWorld(session!.Model, index)),
                words => OnUiThread(() => AskWorldDirectory(words)),
                () => OnUiThread(() =>
                {
                    NativeModalClosed(session!);
                    _worldGeneration++;
                }))
            {
                Theme = _theme.Clone(),
                InitialQuery = query,
            };
            ShowNativeModal(host, session);
            return;
        }

        var browse = new WorldBrowseSurface(query);
        _worldBrowse = browse;
        var rows = Math.Clamp(_system.DesktopDimensions.Height - 6, 10, 30);
        _helpView.Open(
            WorldBrowseMode,
            WorldBrowseModel.Heading,
            width => browse.Render(Drawing(SurfaceCatalog.BrowseWorlds, string.Empty, _worldModel), width, rows),
            key =>
            {
                switch (browse.Key(_worldModel, key))
                {
                    case WorldBrowseAction.Pick:
                        PickWorld(_worldModel, browse.SelectedRow(_worldModel));
                        break;
                    case WorldBrowseAction.Ask:
                        AskWorldDirectory(browse.Query);
                        break;
                }

                return WorldBrowseOpen;
            },
            width: WorldBrowseSurface.PreferredWidth,
            closed: () =>
            {
                _worldBrowse = null;
                _worldGeneration++;
            });
    }

    /// <summary>
    /// Asks for the listing — the directory's most played, or its search for <paramref name="query"/> — and publishes
    /// the model it leaves: loading now, then the rows or why there are none. Nothing is asked with no https directory
    /// or no fetch, and the model says which.
    /// </summary>
    /// <param name="settled">Told the model once the list is in, failed or could not be asked.</param>
    private void StartWorldList(string query, Action<WorldBrowseModel>? settled = null)
    {
        var generation = ++_worldGeneration;
        void Settle(WorldBrowseModel model)
        {
            PublishWorldModel(model);
            settled?.Invoke(model);
        }

        if (WorldDirectoryProblem() is { } problem)
        {
            Settle(new WorldBrowseModel(WorldBrowseState.Unavailable, [], 0, query, problem, null));
            return;
        }

        if ((WorldFetch ?? _worldFetch) is null)
        {
            Settle(new WorldBrowseModel(
                WorldBrowseState.Unavailable, [], 0, query, "this run asks no world directory, so nothing was fetched", null));
            return;
        }

        PublishWorldModel(new WorldBrowseModel(WorldBrowseState.Loading, [], 0, query, null, null));
        FetchWorlds(WorldDirectory.ListUri(_config.WorldDirectoryUrl, query)!, generation, (bytes, failure) =>
        {
            if (bytes is null)
            {
                Settle(new WorldBrowseModel(WorldBrowseState.Failed, [], 0, query, failure, null));
                return;
            }

            var (page, unread) = WorldDirectory.ParseList(bytes);
            Settle(page is null
                ? new WorldBrowseModel(WorldBrowseState.Failed, [], 0, query, unread, null)
                : new WorldBrowseModel(WorldBrowseState.Ready, page.Rows, page.Total, query, null, null));
        });
    }

    /// <summary>
    /// The pick, from either host: row <paramref name="index"/> of <paramref name="model"/>. Asks the directory where that
    /// world is; once it answers with an address <c>/connect</c> would take, Browse worlds closes and the welcome's connect
    /// form opens filled in. Nothing is saved or dialled here. A refusal is said in place and the list stays.
    /// </summary>
    internal void PickWorld(WorldBrowseModel model, int index)
    {
        if (index < 0 || index >= model.Rows.Count || _worldModel.Opening is not null)
        {
            return;
        }

        var row = model.Rows[index];
        if (WorldDirectory.GameUri(_config.WorldDirectoryUrl, row.Id) is not { } uri || (WorldFetch ?? _worldFetch) is null)
        {
            PublishWorldModel(_worldModel with { Problem = $"{row.Name} not opened: {WorldDirectoryProblem() ?? "this run asks no world directory"}" });
            return;
        }

        var generation = ++_worldGeneration;
        PublishWorldModel(_worldModel with { Opening = row.Name, Problem = null });
        FetchWorlds(uri, generation, (bytes, failure) =>
        {
            var (address, unread) = bytes is null ? (null, failure) : WorldDirectory.ParseGame(bytes);
            if (address is null)
            {
                PublishWorldModel(_worldModel with { Opening = null, Problem = $"{row.Name} not opened: {unread}" });
                return;
            }

            // The listing's name, which the player chose by; the page's own is the same world.
            OpenWorldForm(address with { Name = row.Name });
        });
    }

    /// <summary>
    /// ⏎ on a query none of the rows held match, from either host: asks the directory itself to search for it, and lists
    /// what it says in place of its most played.
    /// </summary>
    internal void AskWorldDirectory(string query)
    {
        if (!_worldModel.AsksDirectory(query))
        {
            return;
        }

        StartWorldList(WorldDirectory.Clean(query, WorldDirectory.MaxQuery));
    }

    /// <summary>Closes Browse worlds and opens the welcome's connect form filled in with <paramref name="address"/>.</summary>
    private void OpenWorldForm(WorldAddress address)
    {
        CloseWorldBrowse();
        var welcome = new WelcomeSurface(WorldDirectoryProblem()) { Layout = _welcomeLayout };
        welcome.Prefill(address.Name, address.Host, address.Port, address.Tls);
        if (WelcomeIsOpen)
        {
            CloseWelcome();
        }

        _welcome = welcome;
        if (!TryOpenNativeWelcome(welcome))
        {
            OpenWelcomeView();
        }
    }

    /// <summary>Closes Browse worlds in whichever host it is open.</summary>
    private void CloseWorldBrowse()
    {
        if (NativeModalOf(SurfaceCatalog.BrowseWorlds) is { } native)
        {
            CloseNativeModal(native);
        }

        if (_helpView.IsOpen && _helpView.Mode == WorldBrowseMode)
        {
            _helpView.Close();
        }
    }

    /// <summary>A new model: the native session is told, the cell surface repainted.</summary>
    private void PublishWorldModel(WorldBrowseModel model)
    {
        _worldModel = model;
        if (NativeWorldBrowse is { } session)
        {
            session.Publish(model);
        }

        if (_helpView.IsOpen && _helpView.Mode == WorldBrowseMode)
        {
            _helpView.Repaint();
        }
    }

    /// <summary>
    /// Asks <paramref name="uri"/> through the fetch and hands back the bytes, or why there are none, on the UI thread —
    /// unless a newer request (or a close) has bumped <paramref name="generation"/> since, when the answer is dropped. An
    /// answer already in hand (the demo's, a test's) is handled at once.
    /// </summary>
    private void FetchWorlds(Uri uri, int generation, Action<byte[]?, string?> then)
    {
        var fetch = WorldFetch ?? _worldFetch;
        if (fetch is null)
        {
            return;
        }

        void Deliver(byte[]? bytes, string? problem)
        {
            if (generation == _worldGeneration)
            {
                then(bytes, bytes is null ? problem ?? "it did not answer, or the answer was too large" : null);
            }
        }

        Task<byte[]?> task;
        try
        {
            task = fetch(uri, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            Deliver(null, ex.Message);
            return;
        }

        if (task.IsCompleted)
        {
            Deliver(task.IsCompletedSuccessfully ? task.Result : null, task.Exception?.InnerException?.Message);
            return;
        }

        _ = Task.Run(async () =>
        {
            byte[]? bytes = null;
            string? problem = null;
            try
            {
                bytes = await task.WaitAsync(WorldDirectory.Timeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                problem = "it took too long to answer";
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
            {
                problem = ex.Message;
            }

            OnUiThread(() => Deliver(bytes, problem));
        });
    }

    // ── /browse, and the lines a host with no screen hears ────────────────────────────────────────

    /// <summary>
    /// <c>/browse [words]</c> opens Browse worlds (asking the directory for the words); <c>/browse N</c> picks row N of the
    /// list <c>/browse</c> last said as lines.
    /// </summary>
    private void BrowseCommand(string arguments)
    {
        var words = arguments.Trim();
        if (int.TryParse(words, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && _saidWorlds.Count > 0)
        {
            if (n < 1 || n > _saidWorlds.Count)
            {
                RefuseCommand($"/browse lists {_saidWorlds.Count} worlds; say a number from 1 to {_saidWorlds.Count}");
                return;
            }

            SayWorldAddress(_saidWorlds[n - 1]);
            return;
        }

        if (WorldBrowseOpen)
        {
            CloseWorldBrowse();
        }

        ShowWorldBrowse(words);
    }

    /// <summary>The listing said as numbered lines, for a host nobody reads the screen of; <c>/browse N</c> picks one.</summary>
    private void SayWorlds(string query) => StartWorldList(query, model =>
    {
        var lines = new List<string> { WorldBrowseModel.AttributionLine };
        if (model.Empty(string.Empty) is { } empty)
        {
            lines.Add(empty);
            _saidWorlds = [];
        }
        else
        {
            _saidWorlds = model.Rows.Take(20).ToArray();
            lines.Add($"{model.CountLine(string.Empty)}; the first {_saidWorlds.Count}, most played first:");
            lines.AddRange(_saidWorlds.Select((r, i) => string.Join(
                " — ",
                new[] { $"{i + 1}. {r.Name}", $"{WorldBrowseModel.Players(r)} players", r.Codebase, r.Tagline }.Where(p => p.Length > 0))));
            lines.Add("Type /browse and a number to see where that world is, or /browse and words to search.");
        }

        SayLines(lines);
    });

    /// <summary>Where a world <c>/browse N</c> named is, said as the <c>/connect</c> line to type; nothing is saved or dialled.</summary>
    private void SayWorldAddress(WorldListing row)
    {
        if (WorldDirectory.GameUri(_config.WorldDirectoryUrl, row.Id) is not { } uri || (WorldFetch ?? _worldFetch) is null)
        {
            RefuseCommand($"{row.Name} not looked up: {WorldDirectoryProblem() ?? "this run asks no world directory"}");
            return;
        }

        var generation = ++_worldGeneration;
        FetchWorlds(uri, generation, (bytes, failure) =>
        {
            var (address, unread) = bytes is null ? (null, failure) : WorldDirectory.ParseGame(bytes);
            if (address is null)
            {
                RefuseCommand($"{row.Name} not looked up: {unread}");
                return;
            }

            var host = address.Host.Contains(':') ? $"[{address.Host}]" : address.Host;
            var command = $"/connect {host} {address.Port}" + (address.Tls ? " tls" : string.Empty);
            if (Host.ScreenIsSeen)
            {
                OpenWorldForm(address with { Name = row.Name });
                return;
            }

            SayLines(
            [
                $"{row.Name} is at {address.Host} port {address.Port}{(address.Tls ? ", encrypted" : string.Empty)}, as listed on mu*index.",
                $"To add it and connect, type: {command}",
            ]);
        });
    }

    /// <summary>Lines into the focused window and the announcer, as help says its rows.</summary>
    private void SayLines(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            AppendWindowLine(ActiveWindowId(), $"[dim]*** {MarkupText.Escape(line)}[/]");
            Host.Announcer?.Announce(line, AnnouncementKind.Notice);
        }
    }

    // ── the demo ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>The demo's directory, answered from memory, so a window demo can browse and pick without a byte leaving.</summary>
    private static Task<byte[]?> DemoWorldFetch(Uri uri, CancellationToken token) =>
        Task.FromResult<byte[]?>(System.Text.Encoding.UTF8.GetBytes(DemoWorldDirectory.Answer(uri)));
}
