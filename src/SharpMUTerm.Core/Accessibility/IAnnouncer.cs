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
}
