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
        if (Host.Announcer is not { } announcer)
        {
            return;
        }

        if (!_quietWindows.Contains(windowId) && text.Trim().Length > 0)
        {
            announcer.Announce(text, AnnouncementKind.Output);
        }

        // A quiet window is not spoken as lines arrive, but a reader that asks the output view for its text still
        // gets the window in front, quiet or not.
        if (windowId == ActiveWindowId())
        {
            PublishReading(windowId);
        }
    }

    /// <summary>
    /// Hands the host the window in front and its newest lines (<see cref="IAnnouncer.Reading"/>), which the window
    /// exposes as its output view's text for a screen reader to read at its own pace. Plain text from the pane's own
    /// buffer, so it is what ⌃F searches: chrome rows carry none and are left out.
    /// </summary>
    private void PublishReading(string windowId)
    {
        if (Host.Announcer is not { } announcer)
        {
            return;
        }

        var lines = new List<string>(IAnnouncer.ReadingLines);
        if (_lines.TryGetValue(windowId, out var buffer))
        {
            for (var i = buffer.Count - 1; i >= 0 && lines.Count < IAnnouncer.ReadingLines; i--)
            {
                if (buffer[i].Plain is { Length: > 0 } plain)
                {
                    lines.Add(plain);
                }
            }

            lines.Reverse();
        }

        announcer.Reading(WindowTitle(windowId), lines);
    }

    private void AnnounceNotice(string text) => Host.Announcer?.Announce(text, AnnouncementKind.Notice);

    private void AnnounceFocus(string windowId)
    {
        if (Host.Announcer is { } announcer)
        {
            announcer.Announce(WindowTitle(windowId), AnnouncementKind.Focus);
            PublishReading(windowId);
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
