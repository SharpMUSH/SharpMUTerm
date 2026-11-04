using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Tui;

/// <summary>
/// Dialling a dropped connection again, for a world with F5 ▸ <c>reconnect</c> on. The rule — what counts as a drop,
/// the waits, when it gives up — is Core's (<see cref="AutoReconnect"/>, one per session); this is how the client
/// says it: a line in the session's window and a notice for each try (its action stops them), and the status row's
/// state reading <c>reconnecting in 12 s · ⌥R now</c> while one waits, counted down each second.
/// <para>
/// ⌥R while one waits dials at once (the wait is cancelled first); Disconnect or ⌃P ▸ <c>Stop reconnecting</c> cancels
/// it. A dial goes through <see cref="WorldSession.ConnectAsync"/>, so the login is sent exactly as a connect by hand
/// would send it: auto-login if the character has it, nothing if not.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Stops the focused connection's waiting reconnect.</summary>
    internal const string StopReconnectCommand = SharpMUTerm.Client.Commands.BuiltInCommands.StopReconnectCommand;

    private readonly Dictionary<WorldSession, AutoReconnect> _reconnects = [];
    private ITimer? _reconnectTicker;

    /// <summary>The focused session's reconnect, for the tests and the catalog; null when the session has none.</summary>
    internal AutoReconnect? ReconnectFor(WorldSession session) => _reconnects.GetValueOrDefault(session);

    private void StartReconnecting(WorldSession session)
    {
        if (_reconnects.ContainsKey(session))
        {
            return;
        }

        var auto = new AutoReconnect(session, _time, DialAgainAsync);
        auto.Changed += (_, step) => OnUi(() => OnReconnectStep(session, auto, step));
        _reconnects[session] = auto;
    }

    private void StopReconnecting(WorldSession session)
    {
        if (_reconnects.Remove(session, out var auto))
        {
            auto.Dispose();
        }
    }

    private async Task DialAgainAsync(WorldSession session)
    {
        await session.ConnectAsync().ConfigureAwait(false);

        // A freshly connected session has never been told a size (ReconnectAsync's reason).
        OnUiThread(ReportPaneSizes);
    }

    private void OnReconnectStep(WorldSession session, AutoReconnect auto, ReconnectStep step)
    {
        var title = SessionTitle(session);
        switch (step)
        {
            case ReconnectStep.Scheduled when auto.SecondsLeft is { } seconds:
                SayInSessionWindow(session, ReconnectLabel, ReconnectLine(seconds, auto.Try));
                Notice(
                    $"{title} dropped — reconnecting in {seconds} s · ⌥R now",
                    MessageSeverity.Info,
                    "⌥R",
                    new NoticeAction(StopReconnectCommand, "Stop"));
                break;

            case ReconnectStep.GaveUp:
                SayInSessionWindow(session, ReconnectLabel, $"stopped after {AutoReconnect.Delays.Count} tries · ⌥R dials again");
                Notice($"{title} did not come back after {AutoReconnect.Delays.Count} tries — ⌥R dials again", MessageSeverity.Error, "⌥R");
                break;
        }

        SyncReconnectTicker();
        UpdateStatus();
    }

    /// <summary>A one-second tick while any reconnect waits, so the status row counts down; stopped when none does.</summary>
    private void SyncReconnectTicker()
    {
        var waiting = _reconnects.Values.Any(r => r.DueAt is not null);
        if (waiting && _reconnectTicker is null)
        {
            _reconnectTicker = _time.CreateTimer(
                _ => OnUiThread(OnReconnectTick), null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        }
        else if (!waiting && _reconnectTicker is not null)
        {
            _reconnectTicker.Dispose();
            _reconnectTicker = null;
        }
    }

    private void OnReconnectTick()
    {
        // Re-armed by hand each second rather than periodic, so a tick never queues behind a slow frame.
        _reconnectTicker?.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        SyncReconnectTicker();
        if (_active is { } session && ReconnectFor(session)?.DueAt is not null)
        {
            RefreshStatusBar();
        }
    }

    /// <summary>
    /// The status row's state while <paramref name="session"/> waits to dial again, or null. The seconds take a
    /// two-digit field, so the row does not move as they count down.
    /// </summary>
    private string? ReconnectStatus(WorldSession? session) =>
        session is not null && ReconnectFor(session)?.SecondsLeft is { } seconds ? ReconnectState(seconds)
        : session is null && _sessions.Sessions.Count == 0 && _demoReconnect is { } demo ? ReconnectState(demo)
        : null;

    /// <summary>The status row's state for a reconnect <paramref name="seconds"/> away; one writer for the live row and the demo's.</summary>
    internal static string ReconnectState(int seconds) => $"reconnecting in {seconds,2} s · ⌥R now";

    /// <summary>The line a session's window gets when a try is scheduled; one writer for the live session and the demo's.</summary>
    internal static string ReconnectLine(int seconds, int attempt) =>
        $"in {seconds} s · try {attempt} of {AutoReconnect.Delays.Count} · ⌥R dials now · ⌃P ▸ Stop reconnecting";

    /// <summary>The label of every row a reconnect writes into its session's window.</summary>
    internal const string ReconnectLabel = "RECONNECT";

    /// <summary>
    /// Writes a client row into <paramref name="session"/>'s own window (<see cref="AppendClientRow"/>): chrome, not a
    /// session line, so the client's own bookkeeping never lands in the character's transcript or restore log.
    /// </summary>
    private void SayInSessionWindow(WorldSession session, string label, string text)
    {
        if (_sessionWindows.TryGetValue(session, out var windowId))
        {
            AppendClientRow(windowId, label, text);
        }
    }

    /// <summary>
    /// Appends one client row (<see cref="ClientRowRenderer"/>) to a window: drawn as the bars are, held as chrome (a bar
    /// function and no plain text), with no stamp, because it did not arrive. Fed to nothing a session line is fed to.
    /// </summary>
    private void AppendClientRow(string windowId, string label, string text)
    {
        if (BarWidth(windowId) <= 0)
        {
            // Not arranged yet: drawn at the fallback width, so the next frame must redraw it at the pane's.
            _chromeBarWidths.Remove(windowId);
        }

        AppendWindowLine(
            windowId,
            ClientRowRenderer.Bar(label, text, FrozenAccentHex(), BarWidth(windowId)),
            bar: accent => ClientRowRenderer.Bar(label, text, accent, BarWidth(windowId)));
    }

    /// <summary>The demo scene's waiting reconnect, in seconds: the `reconnect` view's, which has no live session to drop.</summary>
    private int? _demoReconnect;

    /// <summary>
    /// The `reconnect` view: Corvid's connection dropped and the second try waits twelve seconds. Written with the live
    /// path's own writers (<see cref="ReconnectLine"/>, <see cref="ReconnectState"/>) because the demo has no session.
    /// </summary>
    private void ShowDemoReconnect()
    {
        _demoReconnect = 12;
        AppendClientRow(MainWindowId, ConnectionNote.Lost, "connection lost · Connection reset by peer");
        AppendClientRow(MainWindowId, ConnectionNote.Connect, "dialling aetherfall.mux:4201…");
        AppendClientRow(MainWindowId, ConnectionNote.Failed, "could not connect to aetherfall.mux:4201 · Connection refused");
        AppendClientRow(MainWindowId, ReconnectLabel, ReconnectLine(12, 2));
        _statusIdentity = ("Corvid", "aetherfall.mux", 4201, "faulted");
        RefreshStatusBar();
        SettleScroll();
    }

    /// <summary>Cancels <paramref name="session"/>'s waiting reconnect; true when there was one.</summary>
    private bool CancelReconnect(WorldSession session)
    {
        if (ReconnectFor(session) is not { IsActive: true } auto)
        {
            return false;
        }

        auto.Cancel();
        return true;
    }

    /// <summary>⌃P ▸ <c>Stop reconnecting</c>, and the notice's action: the focused connection's.</summary>
    private void StopReconnectCommandRun()
    {
        if (SendTarget() is { } session && CancelReconnect(session))
        {
            SayInSessionWindow(session, ReconnectLabel, "stopped · ⌥R dials again");
            Notice($"stopped reconnecting {SessionTitle(session)} — ⌥R dials it", MessageSeverity.Info, "⌥R");
            return;
        }

        // The notice's action can arrive after focus moved: any one waiting is the one the notice was about.
        if (_reconnects.FirstOrDefault(r => r.Value.IsActive) is { Key: { } other } && CancelReconnect(other))
        {
            SayInSessionWindow(other, ReconnectLabel, "stopped · ⌥R dials again");
            Notice($"stopped reconnecting {SessionTitle(other)}", MessageSeverity.Info);
            return;
        }

        RefuseCommand("nothing is waiting to reconnect");
    }
}
