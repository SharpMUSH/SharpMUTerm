using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Tui;

/// <summary>
/// Full screen and windowed full screen, where the host is a window (<see cref="ClientHost.WindowModes"/>). The window
/// keeps F11 and ⇧F11 for itself; ⌃P, the right-click menu and the Window settings page reach the same toggles from here.
/// A terminal has none of it: its emulator has its own full screen, and the client offers nothing that would do nothing.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Whether the host is a window that can be put in a mode now.</summary>
    internal bool CanSetWindowMode => Host.WindowModes is { Available: true };

    /// <summary>
    /// Goes into <paramref name="mode"/>, or back out of it to the windowed or maximised state the window was in when it
    /// is already there. Refused out loud where the host has no window.
    /// </summary>
    internal bool ToggleWindowMode(WindowMode mode)
    {
        if (Host.WindowModes is not { Available: true } modes)
        {
            Notice("full screen is the terminal's own here", toast: false);
            return false;
        }

        modes.Toggle(mode);
        return true;
    }

    /// <summary>What the host was last told about the system title bar; null until it is told.</summary>
    private bool? _titleBarShown;

    /// <summary>
    /// Tells the window whether to wear the system title bar when the Window page's setting differs from what it was last
    /// told: after every settings commit and every frame (an undo changes it too), the text sizes' rule.
    /// </summary>
    private void SyncWindowTitleBar()
    {
        if (Host.WindowModes is not { Available: true } modes)
        {
            return;
        }

        var show = _config.WindowTitleBar;
        if (_titleBarShown == show)
        {
            return;
        }

        _titleBarShown = show;
        modes.ShowTitleBar(show);
    }
}
