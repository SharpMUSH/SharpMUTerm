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
}
