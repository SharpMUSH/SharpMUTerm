using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Surfaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// The client's modals in a host that draws them with its own controls (<see cref="ClientHost.NativeModals"/>): the
/// quit prompt, ⌃F search, ⌃R history, the ⌥M layouts list and the first-run welcome. Each has one funnel every way in
/// goes through (<see cref="ToggleQuit"/>, <see cref="ToggleSearch"/>, <see cref="ToggleHistorySearch"/>,
/// <see cref="ShowLayouts"/>, <see cref="ShowWelcome"/>), and the funnel asks the host first. A host without one — the
/// terminal, the snapshot, the tests, the parity harness — gets the cell modal, unchanged.
/// <para>
/// <b>The model is the cell modal's.</b> Each session carries the Client model the cell modal draws (the quit facts,
/// the search results, the history, the layout rows, the welcome's state), and every gesture comes back to the client
/// method the cell modal's key ends in: <see cref="Quit"/>, <see cref="GoToSearchHit(SearchModel, int)"/>,
/// <see cref="InsertHistoryEntry"/>, <see cref="RunLayoutsVerb"/>, <see cref="ConnectFromWelcome"/>.
/// </para>
/// <para>
/// <b>The quit prompt is always in front.</b> It can be asked for from anywhere — ⌃Q from any modal, the window's close
/// button while F1 is open — and is never refused: it opens over an open modal, which stays under it, and in the
/// window the host keeps it the topmost thing drawn. With a host that has no native modals the cell prompt is a
/// framework modal window, and anything the host draws over the cells would hide it, so the host's F1 and ⌃P are
/// closed first (F1 keeping its draft).
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The native quit prompt, or null while none is open.</summary>
    private QuitSession? _nativeQuit;

    /// <summary>The native search, history, layouts list or welcome, or null while none is open.</summary>
    private ModalSession? _nativeModal;

    /// <summary>The host's modals, when it has somewhere to draw them; null for cells.</summary>
    private INativeModals? NativeModalsHost => Host.NativeModals is { Available: true } native ? native : null;

    /// <summary>Whether a native modal is open, the quit prompt included.</summary>
    private bool NativeModalOpen => _nativeQuit is not null || _nativeModal is not null;

    /// <summary>The native modal open under the quit prompt, for the tests; null for none.</summary>
    internal ModalSession? NativeModal => _nativeModal;

    /// <summary>The native quit prompt, for the tests; null for none.</summary>
    internal QuitSession? NativeQuit => _nativeQuit;

    /// <summary>The native search's session while one is open, for the tests.</summary>
    internal SearchSession? NativeSearch => _nativeModal as SearchSession;

    /// <summary>The native history's session while one is open, for the tests.</summary>
    internal HistorySession? NativeHistory => _nativeModal as HistorySession;

    /// <summary>The native layouts list's session while one is open, for the tests.</summary>
    internal LayoutsSession? NativeLayouts => _nativeModal as LayoutsSession;

    /// <summary>The native welcome's session while one is open, for the tests.</summary>
    internal WelcomeSession? NativeWelcome => _nativeModal as WelcomeSession;

    /// <summary>What the native modal open is called in a refusal, or null with none.</summary>
    private string? NativeModalName => _nativeQuit is not null ? "the quit prompt" : _nativeModal?.Surface switch
    {
        SurfaceCatalog.Search => "the search surface",
        SurfaceCatalog.History => "the history search",
        SurfaceCatalog.Layouts => "the layouts",
        SurfaceCatalog.Welcome => "the welcome",
        SurfaceCatalog.Help => "help",
        SurfaceCatalog.Dictionary => "the dictionary offer",
        null => null,
        _ => "what is open",
    };

    // ── the quit prompt ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⌃Q, and the window's close button (which sends it): opens the quit prompt, or dismisses it when it is up — the
    /// toggle <see cref="QuitPrompt"/> explains. Never refused, whatever is open.
    /// </summary>
    private void ToggleQuit()
    {
        if (_nativeQuit is { } open)
        {
            CloseNativeModal(open);
            return;
        }

        if (_quit.IsOpen)
        {
            _quit.Toggle();
            return;
        }

        if (NativeModalsHost is { } native)
        {
            QuitSession? session = null;
            session = new QuitSession(
                QuitFactsNow(),
                ThemeIsDark(),
                () => OnUiThread(Quit),
                () => OnUiThread(() => NativeModalClosed(session!)))
            {
                Theme = _theme.Clone(),
            };
            _nativeQuit = session;
            native.Show(session);
            return;
        }

        // The cell prompt is a framework window drawn in the cells. Anything the host draws over the cells would hide
        // it, so the host's F1 (keeping its draft, which the prompt then counts) and ⌃P go first.
        if (_nativeCompose is not null)
        {
            CloseComposer();
        }

        CloseNativePalette();
        _quit.Toggle();
    }

    /// <summary>The quit prompt closed without quitting, in either host: <c>n</c>, Esc, ⌃Q again.</summary>
    private void QuitPromptClosed() => _header.SetContent(new List<string> { HeaderMarkup() });

    // ── ⌃F, ⌃R, ⌥M and the welcome ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The search for <paramref name="query"/> over the windows ⌃F looks in now — the one function both the cell
    /// surface and the native one ask, so the two list the same rows.
    /// </summary>
    internal SearchModel SearchModelFor(string query, bool regex, bool all) =>
        SearchSurface.Compute(SearchableWindows(all), query, regex, all);

    /// <summary>Opens ⌃F in the host's controls; false when the host has none.</summary>
    private bool TryOpenNativeSearch()
    {
        if (NativeModalsHost is not { } native)
        {
            return false;
        }

        SearchSession? session = null;
        session = new SearchSession(
            SearchModelFor(string.Empty, false, false),
            ThemeIsDark(),
            (query, regex, all) => OnUiThread(() =>
            {
                if (ReferenceEquals(_nativeModal, session))
                {
                    session!.Publish(SearchModelFor(query, regex, all));
                }
            }),
            (model, index) => OnUiThread(() => GoToSearchHit(model, index)),
            () => OnUiThread(() => NativeModalClosed(session!)))
        {
            Theme = _theme.Clone(),
        };
        ShowNativeModal(native, session);
        return true;
    }

    /// <summary>Opens ⌃R in the host's controls, <paramref name="query"/> typed into its filter; false when the host has none.</summary>
    private bool TryOpenNativeHistory(string query = "")
    {
        if (NativeModalsHost is not { } native)
        {
            return false;
        }

        HistorySession? session = null;
        session = new HistorySession(
            new HistoryModel(HistoryFor(BarKind(ActiveBar())).Entries.ToArray(), HistoryBarLabel()),
            ThemeIsDark(),
            index => OnUiThread(() => InsertHistoryEntry(index)),
            () => OnUiThread(() => NativeModalClosed(session!)))
        {
            Theme = _theme.Clone(),
            InitialQuery = query,
        };
        ShowNativeModal(native, session);
        return true;
    }

    /// <summary>Opens ⌥M for <paramref name="key"/> in the host's controls; false when the host has none.</summary>
    private bool TryOpenNativeLayouts(string key)
    {
        if (NativeModalsHost is not { } native)
        {
            return false;
        }

        LayoutsSession? session = null;
        session = new LayoutsSession(
            LayoutsModelFor(key),
            ThemeIsDark(),
            (verb, id, name) => OnUiThread(() =>
            {
                RunLayoutsVerb(key, verb, id, name);
                if (ReferenceEquals(_nativeModal, session))
                {
                    session!.Publish(LayoutsModelFor(key));
                }
            }),
            () => OnUiThread(() => NativeModalClosed(session!)))
        {
            Theme = _theme.Clone(),
        };
        ShowNativeModal(native, session);
        return true;
    }

    /// <summary>Opens the welcome over <paramref name="welcome"/> in the host's controls; false when the host has none.</summary>
    private bool TryOpenNativeWelcome(WelcomeSurface welcome)
    {
        if (NativeModalsHost is not { } native)
        {
            return false;
        }

        WelcomeSession? session = null;
        void Republish()
        {
            if (ReferenceEquals(_nativeModal, session))
            {
                session!.Publish(welcome.Model);
            }
        }

        session = new WelcomeSession(
            welcome.Model,
            ThemeIsDark(),
            choice => OnUiThread(() =>
            {
                if (ReferenceEquals(_nativeModal, session) && RunWelcomeAction(welcome, welcome.Choose(choice)))
                {
                    Republish();
                }
            }),
            layout => OnUiThread(() =>
            {
                SetWelcomeLayout(welcome, layout);
                Republish();
            }),
            form => OnUiThread(() =>
            {
                if (ReferenceEquals(_nativeModal, session)
                    && RunWelcomeAction(welcome, welcome.Submit(form, words => ParseEndpoint(words))))
                {
                    Republish();
                }
            }),
            () => OnUiThread(() =>
            {
                welcome.Back();
                Republish();
            }),
            () => OnUiThread(() => NativeModalClosed(session!)))
        {
            Theme = _theme.Clone(),
        };
        ShowNativeModal(native, session);
        return true;
    }

    private void ShowNativeModal(INativeModals native, ModalSession session)
    {
        if (_nativeModal is { } previous)
        {
            previous.End();
        }

        _nativeModal = session;
        native.Show(session);
    }

    /// <summary>Closes a native modal the client is done with, running nothing.</summary>
    private void CloseNativeModal(ModalSession session)
    {
        session.End();
        Host.NativeModals?.Close(session);
        NativeModalClosed(session);
    }

    /// <summary>A native modal has closed, however it did; a session an earlier opening left behind is ignored.</summary>
    private void NativeModalClosed(ModalSession session)
    {
        if (ReferenceEquals(_nativeQuit, session))
        {
            _nativeQuit = null;
            QuitPromptClosed();
        }
        else if (ReferenceEquals(_nativeModal, session))
        {
            _nativeModal = null;
            if (session is WelcomeSession && _welcome is { } welcome)
            {
                _welcomeLayout = welcome.Layout;
            }

            _header.SetContent(new List<string> { HeaderMarkup() });
        }
    }

    /// <summary>The native modal of <paramref name="surface"/>, if that is the one open.</summary>
    private ModalSession? NativeModalOf(string surface) =>
        _nativeModal is { } open && open.Surface == surface ? open : null;
}
