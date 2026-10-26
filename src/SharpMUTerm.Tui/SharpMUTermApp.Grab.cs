using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Tui;

/// <summary>
/// A MUSH's edit grab: a line starting with the world's grab prefix (AresMUSH's <c>FugueEdit &gt;</c>) is not
/// shown, and its text goes on the command line for the player to change and send. Nothing is ever sent for
/// them, because another player can page a line that starts with the prefix.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Grabs for windows not in front, by window id, placed when the window is next brought forward.</summary>
    private readonly Dictionary<string, string> _pendingGrabs = new(StringComparer.Ordinal);

    /// <summary>
    /// Puts a grab on the command line of the window it came from: now, when that window is in front, and
    /// otherwise when it is next brought forward, so a grab never lands on another character's bar.
    /// </summary>
    private void ReceiveGrab(WorldSession session, string windowId, string text)
    {
        if (ReferenceEquals(WindowSession(ActiveWindowId()), session))
        {
            PlaceGrab(ActiveBar(), text, session.SessionKey);
            Notice("text from the game is on the command line — check it before ⏎", MessageSeverity.Info);
            return;
        }

        _pendingGrabs[windowId] = text;
        Notice($"text from {Snippet(SessionTitle(session))} is waiting for that window's command line", MessageSeverity.Info);
    }

    /// <summary>
    /// Replaces the bar's text with the grab. What was there goes into history first, so ⌥↑ brings back a line
    /// that was being typed; through <c>Add</c>, so a connect line still meets the secret gate.
    /// </summary>
    private void PlaceGrab(InputBarControl bar, string text, string? owner = null)
    {
        if (bar.Text.Trim().Length > 0)
        {
            (owner is null ? HistoryFor(BarKind(bar)) : HistoryFor(owner, BarKind(bar))).Add(bar.Text);
        }

        bar.SetAndNotify(text);
    }

    /// <summary>The grab waiting for <paramref name="windowId"/>, for the tests.</summary>
    internal string? PendingGrab(string windowId) => _pendingGrabs.GetValueOrDefault(windowId);
}
