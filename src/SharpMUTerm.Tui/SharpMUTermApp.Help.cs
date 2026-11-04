using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Accessibility;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// Help (⌃P ▸ <em>Help</em>, <c>/help [words]</c>) and the spelling dictionary offer (F1's ⌥W with no dictionary, ⌃P ▸
/// <em>Get a spelling dictionary</em>). Both are one Client model drawn as a cell <see cref="ListOverlay"/> here and as
/// a native modal in a host that has them (<see cref="ClientHost.NativeModals"/>), and every gesture in either ends in
/// one method: <see cref="RunHelpEntry"/> and <see cref="InstallDictionary"/>.
/// <para>
/// <b>Help is generated, not written twice.</b> The slash commands are <c>SlashCommands.All</c>, the table <c>--help</c>
/// prints; the keys are the command registry's for this host and state plus <see cref="HostKeys"/>; only the how-to
/// pages are prose. A command chosen in help is put on the command line and never sent; a key chosen runs its command.
/// </para>
/// <para>
/// <b>A dictionary is fetched only when asked, and the offer is the consent.</b> It says what, from where, under what
/// licence and into which folder before anything is downloaded; the files come from a pinned https address and are
/// checked against pinned SHA-256s (<see cref="DictionaryDownload"/>). The folder is the caller's
/// (<c>dictionaries:</c>, the <c>save:</c>/<c>logRoot:</c> family): null in a test and a snapshot, which then refuse.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The cell help, while it is open; null otherwise.</summary>
    private HelpSurface? _help;

    /// <summary>Help's and the dictionary offer's cell modal.</summary>
    private readonly ListOverlay _helpView;

    /// <summary>The client's own <c>dictionaries/</c> folder, or null in a run that may write none.</summary>
    private readonly string? _dictionariesRoot;

    /// <summary>The dictionary offer's model while the cell offer is open.</summary>
    private DictionaryOfferModel? _dictionaryOffer;

    /// <summary>Whether F1 was closed to show the offer, so it opens again (with its draft) when the offer closes.</summary>
    private bool _reopenComposerAfterOffer;

    /// <summary>A download is under way.</summary>
    private bool _dictionaryDownloading;

    /// <summary>
    /// Fetches one dictionary file, for the tests; null uses <see cref="DictionaryDownload.DownloadAsync"/>. Handed the
    /// address and the cap.
    /// </summary>
    internal Func<Uri, long, CancellationToken, Task<byte[]?>>? DictionaryFetch { get; set; }

    /// <summary>The last download, for the tests to await.</summary>
    internal Task? DictionaryTask { get; private set; }

    /// <summary>The dictionary a test fetches in place of the reader's language's pinned one.</summary>
    internal DictionarySource? DictionaryToFetch { get; set; }

    /// <summary>Whether help is open, in cells or natively.</summary>
    internal bool HelpIsOpen => (_helpView.IsOpen && _helpView.Mode == "help") || NativeModalOf(SurfaceCatalog.Help) is not null;

    /// <summary>Whether the dictionary offer is open, in cells or natively.</summary>
    internal bool DictionaryOfferOpen =>
        (_helpView.IsOpen && _helpView.Mode == "dictionary") || NativeModalOf(SurfaceCatalog.Dictionary) is not null;

    /// <summary>What the cell help or offer shows, for a test.</summary>
    internal IReadOnlyList<string> HelpLines => _helpView.Lines;

    /// <summary>Feeds a key to the cell help or offer.</summary>
    internal void SimulateHelpKey(ConsoleKeyInfo key) => _helpView.SimulateKey(key);

    /// <summary>Types <paramref name="text"/> into the cell help's search.</summary>
    internal void SimulateHelpTyping(string text)
    {
        foreach (var c in text)
        {
            SimulateHelpKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
    }

    /// <summary>The cell help's state, for a test; null while it is shut.</summary>
    internal HelpSurface? CellHelp => _helpView.IsOpen && _helpView.Mode == "help" ? _help : null;

    /// <summary>The native help's session while one is open, for the tests.</summary>
    internal HelpSession? NativeHelp => _nativeModal as HelpSession;

    /// <summary>The native offer's session while one is open, for the tests.</summary>
    internal DictionaryOfferSession? NativeDictionaryOffer => _nativeModal as DictionaryOfferSession;

    /// <summary>Help for this host and this moment: the registry's keys as ⌃P lists them now.</summary>
    internal HelpModel HelpModelNow() => HelpModel.Build(
        BuildCatalog(),
        Host.Keys.Newline.Glyph,
        Host.Keys.Reserved.Select(r => (r.Chord.Glyph, r.Action)).ToArray());

    /// <summary>
    /// ⌃P ▸ Help and <c>/help</c>: opens help, <paramref name="query"/> typed into its search, or closes it. With no
    /// screen to read (<c>--plain</c>) the matching rows are said as lines instead, since a modal would take the typing.
    /// </summary>
    private void ShowHelp(string query = "", string? topic = null)
    {
        if (NativeModalOf(SurfaceCatalog.Help) is { } native)
        {
            CloseNativeModal(native);
            return;
        }

        if (HelpIsOpen)
        {
            _helpView.Close();
            return;
        }

        var model = HelpModelNow();
        if (!Host.ScreenIsSeen)
        {
            SayHelp(model, query);
            return;
        }

        if (ComposerIsInTheWay("help") || (AnyOverlayOpen && Refuse($"close {OpenOverlayName()} first")))
        {
            return;
        }

        if (TryOpenNativeHelp(model, query, topic))
        {
            return;
        }

        var help = new HelpSurface(model, query, topic);
        _help = help;
        var rows = Math.Clamp(_system.DesktopDimensions.Height - 6, 10, 30);
        _helpView.Open(
            "help",
            "Help",
            width =>
            {
                Drawing(SurfaceCatalog.Help, string.Empty, help.Model);
                return help.Render(width, rows);
            },
            HelpKeyPressed,
            width: HelpSurface.PreferredWidth,
            escape: () => _help?.Back() == true);
    }

    /// <summary>A key in the cell help: ⏎ on a command or a key closes help and acts on it.</summary>
    private bool HelpKeyPressed(ConsoleKeyInfo key)
    {
        if (_help is not { } help)
        {
            return false;
        }

        var chosen = help.Key(key);
        if (chosen < 0)
        {
            return true;
        }

        _helpView.Close();
        RunHelpEntry(help.Model, chosen);
        return true;
    }

    /// <summary>
    /// The one place a chosen help row is acted on, from either host: a slash command is put on the command line (the
    /// line already there goes into history first, as a grab's does) and never sent; a key runs its registry command.
    /// </summary>
    internal void RunHelpEntry(HelpModel model, int index)
    {
        if (index < 0 || index >= model.Entries.Count)
        {
            return;
        }

        var entry = model.Entries[index];
        switch (entry.Kind)
        {
            case HelpKind.Command:
                PlaceGrab(ActiveBar(), entry.Target);
                UpdateInputChrome();
                break;
            case HelpKind.Key when entry.Target.Length > 0:
                if (!DispatchCommand(entry.Target))
                {
                    RefuseCommand($"{Snippet(entry.Title)} is not available here now");
                }

                break;
        }
    }

    /// <summary>Opens help in the host's controls; false when the host has none.</summary>
    private bool TryOpenNativeHelp(HelpModel model, string query, string? topic)
    {
        if (NativeModalsHost is not { } native)
        {
            return false;
        }

        HelpSession? session = null;
        session = new HelpSession(
            model,
            ThemeIsDark(),
            index => OnUiThread(() => RunHelpEntry(model, index)),
            () => OnUiThread(() => NativeModalClosed(session!)))
        {
            Theme = _theme.Clone(),
            InitialQuery = query,
            InitialTopic = model.Topic(topic)?.Id,
        };
        ShowNativeModal(native, session);
        return true;
    }

    /// <summary>Help said as lines, for a host with no screen to read: the matching rows, or the page they name.</summary>
    private void SayHelp(HelpModel model, string query)
    {
        var lines = new List<string>();
        var rows = model.View(query);
        if (rows.Count == 1 && rows[0].Kind == HelpKind.Topic && model.Topic(rows[0].Target) is { } page)
        {
            lines.Add(page.Title);
            lines.AddRange(page.Lines.Where(l => l.Length > 0));
        }
        else
        {
            lines.AddRange(rows.Count == 0
                ? [HelpModel.NoMatches]
                : rows.Select(r => string.Join(" — ", new[] { r.Key, r.Title, r.Detail }.Where(p => p.Length > 0))));
        }

        foreach (var line in lines)
        {
            AppendWindowLine(ActiveWindowId(), $"[dim]*** {MarkupText.Escape(line)}[/]");
            Host.Announcer?.Announce(line, AnnouncementKind.Notice);
        }
    }

    /// <summary><c>/help [words]</c>.</summary>
    private void HelpCommand(string argument) => ShowHelp(argument.Trim());

    // ── the spelling dictionary ─────────────────────────────────────────────────────────────────

    /// <summary>The client's own dictionaries folder, or null in a run that keeps none (a test, a snapshot).</summary>
    private string? DictionariesRoot => _dictionariesRoot;

    /// <summary>Whether the client's own folder holds no dictionary, so ⌃P offers to get one.</summary>
    private bool NoOwnDictionary => _dictionariesRoot is not null && !DictionaryDownload.Holds(_dictionariesRoot);

    /// <summary>The offer for this locale and this run.</summary>
    internal DictionaryOfferModel DictionaryOfferNow()
    {
        var source = DictionaryDownload.For(SpellChecker.PreferredLanguage());
        return new DictionaryOfferModel(
            source.Name,
            source.Language,
            source.Origin,
            source.Licence,
            source.Dic.Host,
            $"about {source.Bytes / 100_000 / 10.0:0.0} MB".Replace(',', '.'),
            DictionariesRoot,
            DictionaryDownload.Holds(DictionariesRoot));
    }

    /// <summary>
    /// F1's ⌥W with no dictionary (in either host) and ⌃P ▸ Get a spelling dictionary: F1 closes, keeping its draft, and
    /// the offer opens; F1 comes back when the offer closes. Nothing is fetched here.
    /// </summary>
    internal void OfferDictionary()
    {
        if (DictionaryOfferOpen)
        {
            return;
        }

        if (_dictionaryDownloading)
        {
            RefuseCommand("a dictionary is already being downloaded");
            return;
        }

        if (ComposerOpen)
        {
            _reopenComposerAfterOffer = true;
            CloseComposer();
        }

        if (AnyOverlayOpen && Refuse($"close {OpenOverlayName()} first"))
        {
            _reopenComposerAfterOffer = false;
            return;
        }

        var model = DictionaryOfferNow();
        if (NativeModalsHost is { } native)
        {
            DictionaryOfferSession? session = null;
            session = new DictionaryOfferSession(
                model,
                ThemeIsDark(),
                () => OnUiThread(InstallDictionary),
                () => OnUiThread(() =>
                {
                    NativeModalClosed(session!);
                    DictionaryOfferClosed();
                }))
            {
                Theme = _theme.Clone(),
            };
            ShowNativeModal(native, session);
            return;
        }

        _dictionaryOffer = model;
        _helpView.Open(
            "dictionary",
            "Spelling dictionary",
            width => DictionaryOffer.Render(Drawing(SurfaceCatalog.Dictionary, string.Empty, model), width),
            key =>
            {
                if (key.Key != ConsoleKey.Enter)
                {
                    return true;
                }

                _helpView.Close();
                InstallDictionary();
                return true;
            },
            width: DictionaryOffer.PreferredWidth,
            closed: DictionaryOfferClosed);
    }

    /// <summary>The offer closed, however it did: F1 comes back if the offer closed it.</summary>
    private void DictionaryOfferClosed()
    {
        _dictionaryOffer = null;
        if (_reopenComposerAfterOffer)
        {
            _reopenComposerAfterOffer = false;
            ToggleComposer();
        }
    }

    /// <summary>
    /// Yes, from either host's offer: downloads the dictionary into the client's folder off the UI thread, and says
    /// when it is in (or why not). The next F1 opens it; an F1 already open is not re-marked.
    /// </summary>
    internal void InstallDictionary()
    {
        if (DictionariesRoot is not { } root)
        {
            RefuseCommand("this run keeps no dictionaries folder, so nothing was downloaded");
            return;
        }

        if (_dictionaryDownloading)
        {
            return;
        }

        var source = DictionaryToFetch ?? DictionaryDownload.For(SpellChecker.PreferredLanguage());
        var fetch = DictionaryFetch ?? DictionaryDownload.DownloadAsync;
        _dictionaryDownloading = true;
        Notice($"Downloading the {source.Language} dictionary from {source.Dic.Host}…", MessageSeverity.Info, source: SharpMUTerm.Client.Chrome.NoticeModel.Sources.Spelling);
        DictionaryTask = Task.Run(async () =>
        {
            string? problem;
            try
            {
                problem = await DictionaryDownload.InstallAsync(root, source, fetch).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                problem = ex.Message;
            }

            OnUiThread(() =>
            {
                _dictionaryDownloading = false;
                if (problem is not null)
                {
                    RefuseCommand(problem);
                    return;
                }

                ForgetComposerSpelling();
                Notice($"{source.Name} is installed · F1 checks spelling with it", MessageSeverity.Info, source: SharpMUTerm.Client.Chrome.NoticeModel.Sources.Spelling);
            });
        });
    }

    /// <summary>Drops the dictionary the composer opened (or found missing), so the next F1 looks again.</summary>
    private void ForgetComposerSpelling()
    {
        _composerSpellingOpened = false;
        _composerSpelling = null;
        _composer.ForgetSpelling();
    }
}
