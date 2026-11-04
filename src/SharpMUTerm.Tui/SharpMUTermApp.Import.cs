using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Accessibility;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Import;

namespace SharpMUTerm.Tui;

/// <summary>
/// Import from another client (<c>/import &lt;path&gt;</c>, ⌃P ▸ <em>Import from another client…</em>, the welcome's
/// <em>Import</em> row): MUSHclient, Mudlet and TinTin++ setups read by Core's bounded readers
/// (<see cref="ClientImport"/>), planned against the configuration (<see cref="ImportPlan"/>), and shown as one review
/// (<see cref="ImportReviewModel"/>) — a cell modal here, a native one in a host that has them.
/// <para>
/// <b>The review is the consent.</b> Nothing is written until ⏎ (or the window's Import), which both end in
/// <see cref="AcceptImport"/>. Nothing is overwritten: the automation is a new set, an existing world only gains
/// characters it lacks, and a clashing name gets a suffix. Passwords the file held are stored only if the player
/// switches them on in the review, and then through <see cref="SaveConfiguration"/> like every password (the OS
/// credential store, else <c>secrets.json</c>). Script code of every kind is listed as left out and never run.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The review waiting for ⏎: the plan, whether its passwords are to be stored, and the model shown.</summary>
    private (ImportPlan Plan, bool StorePasswords, ImportReviewModel Model)? _pendingImport;

    /// <summary>Whether the waiting review was said as lines (<c>--plain</c>), so <c>/import yes</c> may answer it.</summary>
    private bool _importSaid;

    /// <summary>Whether the import review is open, in cells or natively.</summary>
    internal bool ImportReviewOpen =>
        (_helpView.IsOpen && _helpView.Mode == "import") || NativeModalOf(SurfaceCatalog.Import) is not null;

    /// <summary>The review waiting, for the tests.</summary>
    internal ImportReviewModel? PendingImport => _pendingImport?.Model;

    /// <summary>The native review's session while one is open, for the tests.</summary>
    internal ImportReviewSession? NativeImportReview => _nativeModal as ImportReviewSession;

    /// <summary>
    /// <c>/import</c>: with a path, reads it and opens the review; alone, starts an import the way ⌃P does. <c>/import
    /// yes</c>, <c>/import no</c> and <c>/import passwords</c> answer a review said as lines, where no modal is drawn.
    /// </summary>
    private void ImportCommand(string argument)
    {
        var word = argument.Trim();
        if (_importSaid && _pendingImport is not null)
        {
            switch (word.ToLowerInvariant())
            {
                case "yes":
                    AcceptImport();
                    return;
                case "no":
                    _pendingImport = null;
                    _importSaid = false;
                    Notice("Import cancelled; nothing was written", MessageSeverity.Info, source: NoticeModel.Sources.Import);
                    return;
                case "passwords":
                    ToggleImportPasswords();
                    return;
            }
        }

        if (word.Length == 0)
        {
            StartImport();
            return;
        }

        ImportFrom(word);
    }

    /// <summary>
    /// ⌃P ▸ Import and the welcome's row: the desktop's file picker where the host has one, else <c>/import </c> on the
    /// command line with a notice saying what path it wants.
    /// </summary>
    internal void StartImport()
    {
        if (Host.FilePicker is { Available: true } picker)
        {
            picker.Pick("Import from another client", path => OnUiThread(() =>
            {
                if (path is not null)
                {
                    ImportFrom(path);
                }
            }));
            return;
        }

        ActiveBar().SetAndNotify("/import ");
        Notice(
            $"Type the path to a setup from {ImportFormats.Listed} (a .mcl world, a Mudlet profile folder or save, a TinTin++ script), then Enter",
            MessageSeverity.Info,
            source: NoticeModel.Sources.Import);
    }

    /// <summary>Reads <paramref name="path"/> and opens its review, or says why it cannot be imported. Writes nothing.</summary>
    internal void ImportFrom(string path)
    {
        path = path.Trim().Trim('"', '\'');
        if (path.StartsWith('~'))
        {
            path = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('/', '\\'));
        }

        var (result, problem) = ClientImport.Read(path);
        if (result is null)
        {
            ImportRefused($"{Snippet(path)} was not imported: {problem}");
            return;
        }

        ReviewImport(result);
    }

    /// <summary>Opens the review for <paramref name="result"/>: one model whichever host draws it. Writes nothing.</summary>
    internal void ReviewImport(ImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (ImportReviewOpen)
        {
            CloseImportReview();
        }

        if (ComposerIsInTheWay("the import review") || (AnyOverlayOpen && Refuse($"close {OpenOverlayName()} first")))
        {
            return;
        }

        var plan = ImportPlan.For(_config, result);
        var model = ImportReviewModel.From(plan);
        _pendingImport = (plan, false, model);
        _importSaid = false;

        if (!Host.ScreenIsSeen)
        {
            SayImport(model);
            return;
        }

        if (NativeModalsHost is { } native)
        {
            ImportReviewSession? session = null;
            session = new ImportReviewSession(
                model,
                ThemeIsDark(),
                () => OnUiThread(AcceptImport),
                () => OnUiThread(ToggleImportPasswords),
                () => OnUiThread(() => NativeModalClosed(session!)))
            {
                Theme = _theme.Clone(),
            };
            ShowNativeModal(native, session);
            return;
        }

        _helpView.Open(
            "import",
            "Import",
            width => ImportReview.Render(Drawing(SurfaceCatalog.Import, string.Empty, _pendingImport?.Model ?? model), width),
            key =>
            {
                if (key.Key == ConsoleKey.Enter)
                {
                    // Accepted before the overlay closes, so its closing finds nothing waiting to cancel.
                    if (!(_pendingImport?.Model.Empty ?? true))
                    {
                        AcceptImport();
                    }

                    return false;
                }

                if (key.Key == ConsoleKey.P && key.Modifiers == 0)
                {
                    ToggleImportPasswords();
                }

                return true;
            },
            width: ImportReview.PreferredWidth,
            closed: () => _pendingImport = null);
    }

    /// <summary>
    /// The consent, from either host: writes the waiting import and saves. Planned again first; if the configuration
    /// changed while the review was open (a world added on F5), the review is shown again rather than writing something
    /// the player did not see.
    /// </summary>
    internal void AcceptImport()
    {
        if (_pendingImport is not { } pending)
        {
            ImportRefused("no import is waiting for an answer");
            return;
        }

        _importSaid = false;
        var fresh = ImportPlan.For(_config, pending.Plan.Result);
        if (!fresh.SameAs(pending.Plan))
        {
            ImportRefused("your setup changed while the review was open; nothing was written — check the review again");
            ReviewImport(pending.Plan.Result);
            return;
        }

        if (pending.Model.Empty)
        {
            _pendingImport = null;
            ImportRefused("there was nothing to import");
            return;
        }

        _pendingImport = null;
        var applied = fresh.Apply(_config, pending.StorePasswords);
        SaveConfiguration();

        var parts = new List<string>();
        if (applied.Worlds > 0)
        {
            parts.Add(ImportReviewModel.Count(applied.Worlds, "world"));
        }

        if (applied.Characters > 0)
        {
            parts.Add(ImportReviewModel.Count(applied.Characters, "character"));
        }

        if (applied.SetName is { } set)
        {
            parts.Add($"{ImportReviewModel.Count(applied.Rules, "rule")} in {set}");
        }

        if (applied.PasswordsStored > 0)
        {
            parts.Add(ImportReviewModel.Count(applied.PasswordsStored, "password") + " stored");
        }

        Notice(
            $"Imported {string.Join(", ", parts)} from {pending.Model.Format} · F5 lists worlds, F2 triggers",
            MessageSeverity.Info,
            source: NoticeModel.Sources.Import);
    }

    /// <summary>Switches whether the waiting review stores the file's passwords, and redraws it in whichever host shows it.</summary>
    internal void ToggleImportPasswords()
    {
        if (_pendingImport is not { } pending || pending.Plan.PasswordCount == 0)
        {
            return;
        }

        var store = !pending.StorePasswords;
        var model = ImportReviewModel.From(pending.Plan, store);
        _pendingImport = (pending.Plan, store, model);
        if (NativeImportReview is { } native)
        {
            native.Publish(model);
        }
        else if (_helpView.IsOpen && _helpView.Mode == "import")
        {
            _helpView.Repaint();
        }
        else if (_importSaid)
        {
            SayImportLine(model.PasswordLine ?? string.Empty);
        }
    }

    /// <summary>Feeds a key to the cell review, as the window's own handler would.</summary>
    internal void SimulateImportKey(ConsoleKeyInfo key) => _helpView.SimulateKey(key);

    /// <summary>What the cell review shows, for a test.</summary>
    internal IReadOnlyList<string> ImportReviewLines => _helpView.Mode == "import" ? _helpView.Lines : [];

    private void CloseImportReview()
    {
        if (NativeModalOf(SurfaceCatalog.Import) is { } native)
        {
            CloseNativeModal(native);
        }

        if (_helpView.IsOpen && _helpView.Mode == "import")
        {
            _helpView.Close();
        }
    }

    private void ImportRefused(string reason) =>
        Notice(reason, MessageSeverity.Warning, source: NoticeModel.Sources.Import);

    /// <summary>The review said as lines, for a host with no screen to read, ending in the commands that answer it.</summary>
    private void SayImport(ImportReviewModel model)
    {
        _importSaid = true;
        SayImportLine(model.Title + ". " + model.Intro);
        foreach (var act in model.ActsAsYou)
        {
            SayImportLine(ImportReviewModel.ActsHeading.ToLowerInvariant() + ": " + act);
        }

        foreach (var row in model.Adds)
        {
            SayImportLine($"adds {row.Kind} {row.Name}: {row.Detail}");
        }

        foreach (var row in model.LeftOut)
        {
            SayImportLine($"left out {row.Kind} {row.Name}: {row.Detail}");
        }

        if (model.MoreLeftOut is { } more)
        {
            SayImportLine("left out " + more);
        }

        if (model.PasswordLine is { } passwords)
        {
            SayImportLine(passwords + " Type /import passwords to switch.");
        }

        SayImportLine(model.Empty ? "There is nothing in it to import." : "Type /import yes to import it, or /import no.");
    }

    private void SayImportLine(string line)
    {
        if (line.Length == 0)
        {
            return;
        }

        AppendWindowLine(ActiveWindowId(), $"[dim]*** {MarkupText.Escape(line)}[/]");
        Host.Announcer?.Announce(line, AnnouncementKind.Notice);
    }
}
