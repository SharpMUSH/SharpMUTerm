using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Tui;

/// <summary>
/// A host that is a desktop window and can cover the screen: windowed, maximised, full screen or windowed full screen.
/// The terminal has no window of its own to size and supplies none, so the client offers none of it there.
/// </summary>
internal interface IWindowModes
{
    /// <summary>
    /// Whether there is a window to put in a mode yet (false until the window that owns it exists, so a test, the parity
    /// harness and <c>--render-png</c> offer none of it). Read on the client's thread.
    /// </summary>
    bool Available { get; }

    /// <summary>The mode the window is in now. Read on the client's thread; set on the host's.</summary>
    WindowMode Mode { get; }

    /// <summary>
    /// Puts the window in <paramref name="mode"/>, or — when it is already in that full screen — back to the windowed or
    /// maximised state it was in before. Called on the client's thread; the host does it on its own.
    /// </summary>
    void Toggle(WindowMode mode);

    /// <summary>Raised on the host's thread whenever <see cref="Mode"/> changes, however it was changed.</summary>
    event Action? Changed;
}
