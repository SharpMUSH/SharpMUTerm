using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Tui;

/// <summary>
/// A host that draws its own text and so can size it: the desktop window. The client owns the sizes (they are the
/// configuration's <see cref="AppConfiguration.Display"/>, saved like any setting) and tells the host each time they
/// change; the host owns the keys and the wheel that zoom, and asks the client to step, so a zoom is saved and offered on
/// ⌃P exactly as a change on the settings page is.
/// </summary>
internal interface IWindowTextSize
{
    /// <summary>
    /// Says the sizes to draw at, already held to their ranges. Called on the client's thread, once a frame has been
    /// painted, and only when they changed.
    /// </summary>
    void Show(DisplaySettings sizes);

    /// <summary>
    /// Whether the host draws native views the page's areas (composer, interface, panels, dialogs) size. False where
    /// everything is cells (the window's <c>--emulated</c>), where only the zoom and the output's size apply.
    /// </summary>
    bool SizesAreas { get; }

    /// <summary>
    /// Raised on the host's thread when the player zooms with the host's own keys or wheel: a positive step zooms in, a
    /// negative one out, zero resets to 100%.
    /// </summary>
    event Action<int>? ZoomRequested;
}
