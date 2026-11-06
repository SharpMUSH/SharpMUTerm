using Microsoft.Extensions.Logging;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Diagnostics;
using static SharpMUTerm.Client.Commands.BuiltInCommands;

namespace SharpMUTerm.Tui;

/// <summary>
/// Updating this copy, where the host can (<see cref="ClientHost.Updates"/>: an installed copy of the terminal or the
/// window). The host does the work; this decides when, and says it.
/// <list type="bullet">
/// <item>With F7 ▸ <c>check for updates</c> on (the default), the release feed is asked <see cref="FirstCheckDelay"/>
/// after start and every <see cref="CheckInterval"/> after that. ⌃P ▸ <c>Check for updates</c> asks whatever it says.</item>
/// <item>A newer release is a notice whose action downloads it. Nothing is downloaded before that.</item>
/// <item>Downloaded, the notice's action and ⌃P restart into it: through the quit prompt when there is something to lose
/// or the host has something to say about restarting, else at once. The update is put in place after the loop ends and
/// the configuration is saved (<see cref="Program.RunLive"/>), never while the client runs.</item>
/// </list>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>How long after start the first look is: the client's own start (sessions, the first frame) goes first.</summary>
    internal static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);

    /// <summary>How often a client left running looks again. One look is about a dozen GitHub requests.</summary>
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private enum UpdatePhase
    {
        Idle,
        Checking,
        Found,
        Downloading,
        Ready,
    }

    private UpdatePhase _updatePhase;
    private UpdateOffer? _updateOffer;
    private string? _updateAnnounced;
    private ITimer? _updateTimer;
    private CancellationTokenSource? _updateCancel;

    /// <summary>The quit prompt open now asks to restart into the update; read by <see cref="QuitFactsNow"/> and <see cref="Quit"/>.</summary>
    private bool _quitForUpdate;

    /// <summary>The loop ended by restarting into the update.</summary>
    private bool _restartForUpdate;

    /// <summary>The update the player restarted for, for <see cref="Program.RunLive"/> to put in place once the loop has ended.</summary>
    internal UpdateOffer? UpdateToApply => _restartForUpdate && _updatePhase == UpdatePhase.Ready ? _updateOffer : null;

    /// <summary>The cell quit prompt's question while it is open, for a test.</summary>
    internal QuitModel? QuitPromptModel => _quit.Model;

    /// <summary>The downloaded version, or null while there is none.</summary>
    private string? UpdateReadyVersion => _updatePhase == UpdatePhase.Ready ? _updateOffer?.Version : null;

    private UpdateState? UpdateCommandState() =>
        Host.Updates is null
            ? null
            : new UpdateState(
                _updatePhase is UpdatePhase.Found or UpdatePhase.Ready or UpdatePhase.Downloading ? _updateOffer?.Version : null,
                _updatePhase == UpdatePhase.Ready,
                _updatePhase is UpdatePhase.Checking or UpdatePhase.Downloading);

    private ILogger UpdateLog => _diagnostics.For("SharpMUTerm.Updates");

    private void ScheduleUpdateChecks()
    {
        if (Host.Updates is null || _updateTimer is not null)
        {
            return;
        }

        _updateTimer = _time.CreateTimer(_ => OnUi(() =>
        {
            if (_config.Updates.Check && _updatePhase is UpdatePhase.Idle or UpdatePhase.Found)
            {
                CheckForUpdates(manual: false);
            }
        }), null, FirstCheckDelay, CheckInterval);
    }

    private void StopUpdateChecks()
    {
        _updateTimer?.Dispose();
        _updateTimer = null;
        _updateCancel?.Cancel();
        _updateCancel?.Dispose();
        _updateCancel = null;
    }

    /// <summary>
    /// Asks the feed. <paramref name="manual"/> is ⌃P: it says every answer, where the timer's look says only a release
    /// it has not said before, and a failure only in the client's log.
    /// </summary>
    private bool CheckForUpdates(bool manual)
    {
        if (Host.Updates is not { } updates)
        {
            Notice("This copy cannot update itself: only the installed one can.", MessageSeverity.Warning, source: NoticeModel.Sources.Updates);
            return false;
        }

        if (_updatePhase is UpdatePhase.Checking or UpdatePhase.Downloading)
        {
            return false;
        }

        if (_updatePhase == UpdatePhase.Ready)
        {
            if (manual)
            {
                Notice($"SharpMUTerm {_updateOffer!.Version} is downloaded; restart to use it", MessageSeverity.Info,
                    action: new NoticeAction(UpdateCommand, "Restart"), source: NoticeModel.Sources.Updates);
            }

            return true;
        }

        var previous = _updatePhase;
        _updatePhase = UpdatePhase.Checking;
        var cancel = Restart(ref _updateCancel);
        var prereleases = _config.Updates.Prereleases;
        _ = Task.Run(async () =>
        {
            try
            {
                var offer = await updates.CheckAsync(prereleases, cancel).ConfigureAwait(false);
                OnUi(() => Checked(offer, manual));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                OnUi(() =>
                {
                    _updatePhase = previous;
                    UpdateLog.LogWarning(ex, "Looking for an update failed");
                    if (manual)
                    {
                        Notice($"Could not check for updates: {ex.Message}", MessageSeverity.Error, source: NoticeModel.Sources.Updates);
                    }
                });
            }
        }, cancel);
        return true;
    }

    private void Checked(UpdateOffer? offer, bool manual)
    {
        var current = Host.Updates?.CurrentVersion;
        if (offer is null)
        {
            _updatePhase = _updateOffer is null ? UpdatePhase.Idle : UpdatePhase.Found;
            if (manual)
            {
                Notice($"SharpMUTerm {current} is the newest release.", MessageSeverity.Info, source: NoticeModel.Sources.Updates);
            }

            return;
        }

        _updateOffer = offer;
        _updatePhase = UpdatePhase.Found;
        UpdateLog.LogInformation("SharpMUTerm {Version} is available (running {Current})", offer.Version, current);
        if (manual || _updateAnnounced != offer.Version)
        {
            _updateAnnounced = offer.Version;
            Notice($"SharpMUTerm {offer.Version} is out (you have {current})", MessageSeverity.Info,
                action: new NoticeAction(UpdateCommand, "Update"), source: NoticeModel.Sources.Updates);
        }
    }

    /// <summary>The notice's action and ⌃P's entry: downloads the release found, or restarts into the one downloaded.</summary>
    private bool UpdateNow()
    {
        if (Host.Updates is not { } updates || _updateOffer is not { } offer)
        {
            Notice("No update has been found. ⌃P ▸ Check for updates looks.", MessageSeverity.Warning, source: NoticeModel.Sources.Updates);
            return false;
        }

        switch (_updatePhase)
        {
            case UpdatePhase.Ready:
                RestartForUpdate();
                return true;
            case UpdatePhase.Found:
                Download(updates, offer);
                return true;
            default:
                return false;
        }
    }

    private void Download(IUpdates updates, UpdateOffer offer)
    {
        _updatePhase = UpdatePhase.Downloading;
        var cancel = Restart(ref _updateCancel);
        Notice($"Downloading SharpMUTerm {offer.Version}…", MessageSeverity.Info, source: NoticeModel.Sources.Updates);
        var said = 0;
        _ = Task.Run(async () =>
        {
            try
            {
                await updates.DownloadAsync(offer, percent =>
                {
                    // A notice per quarter, on the row alone: the toast said it started, and four more would be noise.
                    var step = Math.Clamp(percent, 0, 100) / 25 * 25;
                    if (step > said && step < 100)
                    {
                        said = step;
                        OnUi(() => Notice($"Downloading SharpMUTerm {offer.Version}… {step}%", MessageSeverity.Info,
                            source: NoticeModel.Sources.Updates, toast: false));
                    }
                }, cancel).ConfigureAwait(false);
                OnUi(() =>
                {
                    _updatePhase = UpdatePhase.Ready;
                    UpdateLog.LogInformation("SharpMUTerm {Version} is downloaded", offer.Version);
                    Notice($"SharpMUTerm {offer.Version} is ready; restart to use it", MessageSeverity.Info,
                        action: new NoticeAction(UpdateCommand, "Restart"), source: NoticeModel.Sources.Updates);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                OnUi(() =>
                {
                    _updatePhase = UpdatePhase.Found;
                    UpdateLog.LogWarning(ex, "Downloading {Version} failed", offer.Version);
                    Notice($"The download of {offer.Version} failed: {ex.Message}", MessageSeverity.Error,
                        action: new NoticeAction(UpdateCommand, "Try again"), source: NoticeModel.Sources.Updates);
                });
            }
        }, cancel);
    }

    /// <summary>
    /// Restarts into the downloaded update: at once when nothing would be lost and the host has nothing to add, else
    /// through the quit prompt, which then asks to restart and names the version.
    /// </summary>
    private void RestartForUpdate()
    {
        if (_quit.IsOpen || _nativeQuit is not null)
        {
            ToggleQuit(); // the prompt up now is the plain quit one; this one replaces it
        }

        _quitForUpdate = true;
        if (QuitFactsNow() is { Consequences.Count: 0, RestartNote: null })
        {
            Quit();
            return;
        }

        ToggleQuit(restart: true);
    }

    private static CancellationToken Restart(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = new CancellationTokenSource();
        return source.Token;
    }
}
