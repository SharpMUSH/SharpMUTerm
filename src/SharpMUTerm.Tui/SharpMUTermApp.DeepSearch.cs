using System.Globalization;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// ⌃F's older history (⌥H) and the read-only history view an older hit opens.
/// <para>
/// <b>What is searched.</b> ⌃F searches the pane buffers and states that bound (<c>N lines held</c>). ⌥H adds the
/// session's own <see cref="ScrollbackBuffer"/> for each session window in scope — the in-memory ring and the
/// file-backed spill behind it, which is what holds a long evening — through <see cref="ScrollbackSearch"/>: newest to
/// oldest, ranged reads of at most <see cref="ScrollbackBuffer.MaxRangeLines"/>, on a worker thread with a cancellation
/// token, every line under <see cref="OutputSearch"/>'s match timeout. Lines the pane still holds are skipped (a session
/// window's pane shares the ring's instances), so an older row is never a second copy of a row above it. A capture
/// window has no scrollback and no spill — its lines exist only in its pane — and the note says so rather than implying a
/// deeper search that is not there.
/// </para>
/// <para>
/// <b>Results stream.</b> Each chunk's hits are posted to the UI thread and the open surface (cells or native) is
/// redrawn from <see cref="SearchModelFor"/>, older rows after the pane's, newest first, at most
/// <see cref="ScrollbackSearch.MaxHits"/>. Typing or a toggle cancels the walk and starts the next one after a short
/// pause; closing ⌃F cancels it.
/// </para>
/// <para>
/// <b>⏎ on an older hit</b> goes to the pane when the pane still holds that line (by reference), through the one path to a
/// hit; otherwise it opens the <em>history view</em>: a modal page of the scrollback around the line, read a page at a
/// time (<see cref="ScrollHistoryView"/>), never inserted into a pane buffer — a pane's indices are what every bar and
/// boundary counts by, and a page of the past put there would move all of them.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The older-history search running or last finished, or null.</summary>
    private DeepSearch? _deep;

    /// <summary>Whether the older-history search runs inline (headless, and every snapshot), so a frame is deterministic.</summary>
    private bool _deepInline;

    /// <summary>The demo's scrollback, so the snapshot has older history to search with no session.</summary>
    private ScrollbackBuffer? _demoHistory;

    /// <summary>The open history view's state, in either host; null while it is shut.</summary>
    private HistoryViewState? _historyView;

    /// <summary>How long typing pauses before an older-history search starts.</summary>
    private static readonly TimeSpan DeepSearchPause = TimeSpan.FromMilliseconds(150);

    /// <summary>One source of older history: a session window and its session's scrollback.</summary>
    private sealed record HistorySource(string WindowId, string Label, string SessionKey, ScrollbackBuffer Buffer);

    /// <summary>The state of one older-history search, written on the UI thread only.</summary>
    private sealed class DeepSearch(string key, IReadOnlyList<HistorySource> sources, long total)
    {
        public string Key { get; } = key;

        public IReadOnlyList<HistorySource> Sources { get; } = sources;

        public CancellationTokenSource Cancel { get; } = new();

        public ScrollbackSearchProgress?[] Progress { get; } = new ScrollbackSearchProgress?[sources.Count];

        public long Total { get; } = total;

        public bool Running { get; set; } = true;
    }

    /// <summary>The history view: whose scrollback, the search it came from, the line it found, and where the page starts.</summary>
    private sealed class HistoryViewState(HistorySource source, string query, bool regex, long marked)
    {
        public HistorySource Source { get; } = source;

        public string Query { get; } = query;

        public bool Regex { get; } = regex;

        public long Marked { get; } = marked;

        public long Top { get; set; }

        public ScrollbackViewModel? Model { get; set; }

        public ScrollbackViewSession? Native { get; set; }
    }

    /// <summary>Whether the history view is open, in cells or natively.</summary>
    internal bool HistoryViewIsOpen => _historyView is not null;

    /// <summary>The history view's model as it stands, for the tests; null while it is shut.</summary>
    internal ScrollbackViewModel? HistoryViewModel => _historyView?.Model;

    /// <summary>The native history view's session while one is open, for the tests.</summary>
    internal ScrollbackViewSession? NativeHistoryView => _nativeModal as ScrollbackViewSession;

    /// <summary>
    /// The search for <paramref name="query"/> over the windows ⌃F looks in now — the one function both the cell surface
    /// and the native one ask, so the two list the same rows — with older history's rows after them when
    /// <paramref name="older"/> (⌥H), as far as its search has got.
    /// </summary>
    internal SearchModel SearchModelFor(string query, bool regex, bool all, bool older = false)
    {
        var held = SearchSurface.Compute(SearchableWindows(all), query, regex, all);
        var sources = HistorySources(all, out var captures);
        EnsureDeepSearch(query, regex, all, older && held.Error is null, sources);

        var total = sources.Sum(s => s.Buffer.AvailableLines);
        var who = sources.Count == 1 ? sources[0].Label : $"{sources.Count} windows";
        var spill = captures switch
        {
            0 => string.Empty,
            _ when sources.Count == 0 && !all => "this window is a capture: it keeps no older history — every line it holds is searched",
            _ => " · captures keep no older history",
        };

        if (!older || held.Error is not null || _deep is not { } deep)
        {
            var note = sources.Count == 0
                ? (spill.Length > 0 ? spill.TrimStart(' ', '·') : null)
                : $"⌥H searches older history · {N(total)} lines in {who}{spill}";
            return held with { Older = older, HistoryNote = note };
        }

        var rows = new List<SearchRow>(held.Rows);
        long searched = 0;
        var found = 0;
        var capped = false;
        string? error = null;
        for (var i = 0; i < deep.Sources.Count; i++)
        {
            if (deep.Progress[i] is not { } progress)
            {
                continue;
            }

            searched += progress.Searched;
            capped |= progress.Capped;
            error ??= progress.Error;
            var source = deep.Sources[i];
            foreach (var match in progress.Found)
            {
                if (rows.Count - held.Rows.Count >= ScrollbackSearch.MaxHits)
                {
                    capped = true;
                    break;
                }

                rows.Add(new SearchRow(source.WindowId, source.Label, -1, match.Line.Text, match.MatchStart, match.MatchLength, match.Index, source.SessionKey));
                found++;
            }
        }

        var said = error is not null
            ? $"older history: {error}"
            : deep.Running
                ? $"searching older history · {N(searched)} of {N(deep.Total)} lines…"
                : $"older history · {found} found in {N(deep.Total)} lines{(capped ? $" · the first {ScrollbackSearch.MaxHits}" : string.Empty)}";
        return held with { Rows = rows, Older = true, HistoryNote = said + spill, HistoryRunning = deep.Running && error is null };
    }

    /// <summary>
    /// The session windows in ⌃F's scope whose sessions keep a scrollback, and how many capture windows in it keep none.
    /// The demo's main window has a scrollback of its own, so the snapshot has older history to show.
    /// </summary>
    private IReadOnlyList<HistorySource> HistorySources(bool all, out int captures)
    {
        captures = 0;
        var sources = new List<HistorySource>();
        foreach (var window in SearchableWindows(all))
        {
            var session = _sessionWindows.FirstOrDefault(p => string.Equals(p.Value, window.WindowId, StringComparison.Ordinal)).Key;
            if (session is not null)
            {
                sources.Add(new HistorySource(window.WindowId, window.Label, session.SessionKey, session.Scrollback));
            }
            else if (string.Equals(window.WindowId, MainWindowId, StringComparison.Ordinal)
                     && _workspace.FindWindow(MainWindowId)?.SessionKey is { } owner && IsDemoKey(owner))
            {
                sources.Add(new HistorySource(window.WindowId, window.Label, owner, _demoHistory ??= DemoHistory()));
            }
            else if (_workspace.FindWindow(window.WindowId)?.Kind == Core.Workspaces.WindowKind.Spawn)
            {
                captures++;
            }
        }

        return sources;
    }

    /// <summary>
    /// Starts the older-history search for this query and scope unless the one running is already it; cancels it when
    /// ⌥H is off or the query is empty.
    /// </summary>
    private void EnsureDeepSearch(string query, bool regex, bool all, bool older, IReadOnlyList<HistorySource> sources)
    {
        if (!older || query.Length == 0 || sources.Count == 0)
        {
            CancelDeepSearch();
            return;
        }

        var key = string.Join('\u0001', query, regex, all, ActiveWindowId(), string.Join(',', sources.Select(s => s.WindowId)));
        if (_deep is { } running && running.Key == key)
        {
            return;
        }

        CancelDeepSearch();

        // Which lines each pane still holds, by reference, taken here on the UI thread: the worker never touches a pane.
        var held = sources.Select(s => _lines.TryGetValue(s.WindowId, out var buffer)
            ? buffer.Select(l => l.Source).OfType<StyledLine>().ToHashSet(ReferenceEqualityComparer.Instance)
            : new HashSet<object>(ReferenceEqualityComparer.Instance)).ToArray();
        var deep = new DeepSearch(key, sources, sources.Sum(s => s.Buffer.AvailableLines));
        _deep = deep;
        var token = deep.Cancel.Token;

        void Walk()
        {
            for (var i = 0; i < sources.Count && !token.IsCancellationRequested; i++)
            {
                var index = i;
                var last = ScrollbackSearch.Run(
                    sources[i].Buffer,
                    query,
                    regex,
                    line => held[index].Contains(line),
                    progress => Post(() => deep.Progress[index] = progress),
                    token);
                if (last?.Error is not null)
                {
                    break;
                }
            }

            Post(() => deep.Running = false);
        }

        void Post(Action change)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            if (deep.Running && _deepInline)
            {
                change();
                return;
            }

            OnUiThread(() =>
            {
                if (!ReferenceEquals(_deep, deep) || token.IsCancellationRequested)
                {
                    return;
                }

                change();
                RefreshSearchSurfaces();
            });
        }

        if (_deepInline || _headless)
        {
            _deepInline = true;
            Walk();
            return;
        }

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(DeepSearchPause, token).ConfigureAwait(false);
                    Walk();
                }
                catch (OperationCanceledException)
                {
                    // A newer query, or ⌃F closed.
                }
            },
            token);
    }

    /// <summary>Stops the older-history search, if one is running; its rows go with it.</summary>
    private void CancelDeepSearch()
    {
        if (_deep is { } deep)
        {
            deep.Cancel.Cancel();
            deep.Cancel.Dispose();
            _deep = null;
        }
    }

    /// <summary>Redraws whichever ⌃F is open from a fresh model, as older history's results stream in.</summary>
    private void RefreshSearchSurfaces()
    {
        if (_search.IsOpen)
        {
            _search.Refresh();
        }
        else if (NativeSearch is { } native)
        {
            var model = native.Model;
            native.Publish(SearchModelFor(model.Query, model.Regex, model.AllWindows, model.Older));
        }
    }

    /// <summary>
    /// ⏎ on an older hit: to the pane when it still holds that line, through the one path to a hit; otherwise the history
    /// view, opened on a page around it.
    /// </summary>
    private void GoToOlderHit(SearchModel model, SearchRow row)
    {
        CancelDeepSearch();
        var source = HistorySources(model.AllWindows, out _).FirstOrDefault(s => s.WindowId == row.WindowId && s.SessionKey == row.SessionKey);
        if (source is null)
        {
            RefuseCommand("that window's history is gone");
            return;
        }

        var line = source.Buffer.GetRange(row.History, 1);
        if (line.Count == 0)
        {
            RefuseCommand("that line is no longer held — the scrollback has moved past it");
            return;
        }

        if (_lines.TryGetValue(row.WindowId, out var buffer))
        {
            var at = buffer.FindIndex(l => ReferenceEquals(l.Source, line[0]));
            if (at >= 0)
            {
                GoToSearchHit(row with { LineIndex = at, History = -1 }, model.Query, model.Regex, model.AllWindows, 1, 1);
                return;
            }
        }

        OpenHistoryView(source, model.Query, model.Regex, row.History);
    }

    /// <summary>Opens the read-only history view on <paramref name="source"/>'s scrollback around line <paramref name="marked"/>.</summary>
    private void OpenHistoryView(HistorySource source, string query, bool regex, long marked)
    {
        if (AnyOverlayOpen || ComposerOpen)
        {
            RefuseCommand($"close {(ComposerOpen ? "the composer" : OpenOverlayName())} first");
            return;
        }

        var view = new HistoryViewState(source, query, regex, marked);
        _historyView = view;
        view.Top = ScrollbackViewModel.TopFor(marked, source.Buffer.OldestIndex, source.Buffer.TotalLines);
        view.Model = HistoryPage(view);
        if (NativeModalsHost is { } host)
        {
            ScrollbackViewSession? session = null;
            session = new ScrollbackViewSession(
                view.Model,
                ThemeIsDark(),
                top => OnUiThread(() => ScrollHistoryView(top)),
                () => OnUiThread(() =>
                {
                    NativeModalClosed(session!);
                    if (ReferenceEquals(_historyView, view))
                    {
                        _historyView = null;
                    }
                }))
            {
                Theme = _theme.Clone(),
            };
            view.Native = session;
            ShowNativeModal(host, session);
            return;
        }

        _helpView.Open(
            "history-view",
            "Older history",
            width =>
            {
                var model = view.Model!;
                Drawing(SurfaceCatalog.HistoryView, string.Empty, model);
                return ScrollbackViewSurface.Render(model, width);
            },
            key =>
            {
                if (key.Modifiers == 0 && view.Model!.Move(key.Key) is { } top)
                {
                    ScrollHistoryView(top);
                }

                return true;
            },
            width: ScrollbackViewSurface.PreferredWidth,
            closed: () =>
            {
                if (ReferenceEquals(_historyView, view))
                {
                    _historyView = null;
                }
            });
    }

    /// <summary>
    /// The history view's one move, from either host: the page starting at <paramref name="top"/>, read from the
    /// scrollback in one ranged read and drawn.
    /// </summary>
    internal void ScrollHistoryView(long top)
    {
        if (_historyView is not { } view)
        {
            return;
        }

        view.Top = ScrollbackViewModel.Clamp(top, view.Source.Buffer.OldestIndex, view.Source.Buffer.TotalLines);
        view.Model = HistoryPage(view);
        if (view.Native is { } native)
        {
            native.Publish(view.Model);
            return;
        }

        _helpView.Repaint();
    }

    /// <summary>A page of the view's scrollback from its top: one ranged read, each line's match marked.</summary>
    private static ScrollbackViewModel HistoryPage(HistoryViewState view)
    {
        var buffer = view.Source.Buffer;
        var oldest = buffer.OldestIndex;
        var end = buffer.TotalLines;
        var top = ScrollbackViewModel.Clamp(view.Top, oldest, end);
        var lines = buffer.GetRange(top, ScrollbackViewModel.PageRows);
        var first = Math.Max(top, oldest);
        var texts = lines.Select(l => l.Text).ToArray();
        var matches = OutputSearch.Match(texts, view.Query, view.Regex).Matches.ToDictionary(m => m.LineIndex);
        var page = new List<ScrollbackViewLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var hit = matches.TryGetValue(i, out var m) ? m : default;
            page.Add(new ScrollbackViewLine(first + i, texts[i], matches.ContainsKey(i) ? hit.MatchStart : -1, matches.ContainsKey(i) ? hit.MatchLength : 0));
        }

        return new ScrollbackViewModel(view.Source.Label, view.Query, top, oldest, end, view.Marked, page);
    }

    /// <summary>
    /// The demo's older history: an evening of the same streets, a few thousand lines older than anything the pane holds,
    /// with a goblin fight in it to find.
    /// </summary>
    private static ScrollbackBuffer DemoHistory()
    {
        var buffer = new ScrollbackBuffer(10_000);
        string[] streets =
        [
            "You walk along the Spice Road. Lanterns sway over the stalls.",
            "Ashlyn says, \"Have you seen the new market charter?\"",
            "A town crier calls the hour from the steps of the guildhall.",
            "Merrow pages: meet at the Grand Plaza after the bell.",
            "The smell of roasting chestnuts drifts from a cart nearby.",
        ];
        for (var i = 0; i < 6_000; i++)
        {
            var text = i switch
            {
                1_200 => "A goblin leaps from behind a barrel and snarls!",
                1_203 => "You hit the goblin for 9 damage.",
                1_207 => "The goblin flees north, clutching a stolen purse.",
                4_410 => "Ashlyn says, \"That goblin came back — it is in the sewers now.\"",
                _ => $"[{18 + i / 1_000:00}:{i * 7 % 60:00}] {streets[i % streets.Length]}",
            };
            buffer.Append(StyledLine.FromText(text, TextStyle.Default));
        }

        return buffer;
    }

    /// <summary>⏎ on row <paramref name="index"/> of <paramref name="model"/>, for the tests.</summary>
    internal void GoToSearchHitForTest(SearchModel model, int index) => GoToSearchHit(model, index);

    private static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
