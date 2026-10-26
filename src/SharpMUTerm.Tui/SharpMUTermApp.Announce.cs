using SharpMUTerm.Core.Accessibility;

namespace SharpMUTerm.Tui;

/// <summary>
/// What a screen reader is told: each line of output as it arrives, the client's own messages, and the window
/// that has come to the front. Sent to <see cref="ClientHost.Announcer"/>, so only a host that has one (the
/// window's live region, <c>--plain</c>) pays anything for it. A window can be kept quiet from ⌃P.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Windows whose output is not announced. In memory: a run's choice, like the send group.</summary>
    private readonly HashSet<string> _quietWindows = new(StringComparer.Ordinal);

    /// <summary>Whether a window's output is announced, for the tests and the ⌃P entry.</summary>
    internal bool IsAnnounced(string windowId) => Host.Announcer is not null && !_quietWindows.Contains(windowId);

    private void AnnounceLine(string windowId, string text)
    {
        if (Host.Announcer is { } announcer && !_quietWindows.Contains(windowId) && text.Trim().Length > 0)
        {
            announcer.Announce(text, AnnouncementKind.Output);
        }
    }

    private void AnnounceNotice(string text) => Host.Announcer?.Announce(text, AnnouncementKind.Notice);

    private void AnnounceFocus(string windowId)
    {
        if (Host.Announcer is { } announcer)
        {
            announcer.Announce(WindowTitle(windowId), AnnouncementKind.Focus);
        }
    }

    /// <summary>⌃P ▸ <c>Stop reading this window aloud</c> and back.</summary>
    private void ToggleAnnounced()
    {
        var windowId = ActiveWindowId();
        if (_quietWindows.Remove(windowId))
        {
            Notice($"reading {Snippet(WindowTitle(windowId))} aloud", Core.Diagnostics.MessageSeverity.Info);
            return;
        }

        _quietWindows.Add(windowId);
        Notice($"{Snippet(WindowTitle(windowId))} is no longer read aloud", Core.Diagnostics.MessageSeverity.Info);
    }
}
