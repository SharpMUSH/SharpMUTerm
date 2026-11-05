namespace SharpMUTerm.Core.Accessibility;

/// <summary>What an announcement is, so a host can speak or print each kind its own way.</summary>
public enum AnnouncementKind
{
    /// <summary>A line of output arriving in a window.</summary>
    Output,

    /// <summary>A message from the client itself: a refusal, a confirmation, a warning.</summary>
    Notice,

    /// <summary>Which window is now in front.</summary>
    Focus,
}

/// <summary>
/// Where a host sends text for a screen reader or for speech. A host supplies one or none; the client calls it on
/// its own thread, with plain text (no markup), and an implementation must not block.
/// </summary>
public interface IAnnouncer
{
    void Announce(string text, AnnouncementKind kind);

    /// <summary>
    /// The window in front and its most recent lines, oldest first, as plain text: what a screen reader finds when it
    /// asks the output view for its text, rather than what it is told as lines arrive. Said when the window in front
    /// changes and when a line lands in it. A host with nothing to expose ignores it (the default).
    /// </summary>
    /// <param name="window">The window's title.</param>
    /// <param name="lines">Its newest lines, at most <see cref="ReadingLines"/>; chrome rows (bars) left out.</param>
    void Reading(string window, IReadOnlyList<string> lines)
    {
    }

    /// <summary>How many of the front window's lines <see cref="Reading"/> carries.</summary>
    public const int ReadingLines = 50;
}
