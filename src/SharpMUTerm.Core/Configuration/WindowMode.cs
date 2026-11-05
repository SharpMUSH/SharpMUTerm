namespace SharpMUTerm.Core.Configuration;

/// <summary>How the desktop window covers the screen.</summary>
public enum WindowMode
{
    /// <summary>An ordinary window, at the size it was left.</summary>
    Windowed,

    /// <summary>Maximised to the screen's working area: the taskbar or panels stay.</summary>
    Maximised,

    /// <summary>The platform's full-screen state: the whole screen, over the taskbar.</summary>
    FullScreen,

    /// <summary>
    /// A borderless window the size of the whole screen (games call it "borderless windowed"): it covers the taskbar
    /// but stays an ordinary window, so other windows and notifications can come over it and switching away does not
    /// leave a full-screen mode.
    /// </summary>
    WindowedFullScreen,
}
