using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Tui;

/// <summary>
/// ⌃P ▸ <c>Recent alerts</c>: the pages and alerts this run has seen, every character's, newest first. Kept from
/// <see cref="WorldSession.AttentionRequested"/> whether or not the desktop was asked (the player may have been looking
/// at another character's pane), in memory for the run and never saved. The entry opens the palette on them
/// (<see cref="Client.Commands.BuiltInCommands.AlertQuery"/>), one entry each, and choosing one goes to that character's
/// window through the same id a ⌃P <c>Switch to …</c> runs.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>How many alerts are kept; the oldest goes first.</summary>
    internal const int RecentAlertCap = 20;

    private readonly List<KeptAlert> _recentAlerts = [];
    private int _nextAlertId = 1;
    private bool _alertsListed;

    private sealed record KeptAlert(int Id, string SessionKey, AttentionReason Reason, string Text, DateTimeOffset At);

    /// <summary>The alerts kept, newest first, as ⌃P lists them; for the tests.</summary>
    internal IReadOnlyList<AlertRef> RecentAlerts => AlertRefs();

    private void KeepAlert(string sessionKey, AttentionReason reason, string line, TimeSpan ago = default)
    {
        var text = line.ReplaceLineEndings(" ").Trim();
        _recentAlerts.Insert(0, new KeptAlert(_nextAlertId++, sessionKey, reason, text.Length <= 80 ? text : text[..79] + "…", _time.GetUtcNow() - ago));
        if (_recentAlerts.Count > RecentAlertCap)
        {
            _recentAlerts.RemoveAt(_recentAlerts.Count - 1);
        }
    }

    private IReadOnlyList<AlertRef> AlertRefs()
    {
        var now = _time.GetUtcNow();
        return _recentAlerts.Select(a => new AlertRef(a.Id, DisplayName(a.SessionKey), a.Reason switch
        {
            AttentionReason.Page => "page",
            AttentionReason.Name => "mentioned you",
            _ => "alert",
        }, a.Text, Age(now - a.At))).ToList();
    }

    private static string Age(TimeSpan ago) => ago.TotalSeconds < 60
        ? "just now"
        : ago.TotalMinutes < 60
            ? $"{(int)ago.TotalMinutes} min ago"
            : $"{(int)ago.TotalHours} h ago";

    /// <summary>⌃P ▸ Recent alerts: the palette, opened on the alerts.</summary>
    private void ShowRecentAlerts()
    {
        if (_recentAlerts.Count == 0)
        {
            RefuseCommand("no pages or alerts yet this run");
            return;
        }

        OpenMenu(Client.Commands.BuiltInCommands.AlertQuery);
    }

    /// <summary>An alert entry: goes to its character's window. False when the alert is no longer kept.</summary>
    private bool GoToAlert(string id)
    {
        var alert = int.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            ? _recentAlerts.FirstOrDefault(a => a.Id == n)
            : null;
        if (alert is null)
        {
            RefuseCommand("that alert is no longer kept");
            return false;
        }

        return DispatchCommand(CommandIds.Character(alert.SessionKey));
    }

    /// <summary>Keeps an alert as the session's event would, for a test or the demo scene.</summary>
    internal void SimulateAlert(string sessionKey, AttentionReason reason, string text, TimeSpan ago = default) =>
        KeepAlert(sessionKey, reason, text, ago);
}
