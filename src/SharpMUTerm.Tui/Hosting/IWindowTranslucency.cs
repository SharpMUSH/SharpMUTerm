namespace SharpMUTerm.Tui;

/// <summary>
/// A host that draws F7's background transparency itself, because it is the thing drawing the backgrounds: the desktop
/// window, which paints every plane at the level and asks the desktop for a translucent window behind them. Handed the
/// level only; the planes stay the client's and are drawn as in any host, so nothing a cell says changes.
/// </summary>
internal interface IWindowTranslucency
{
    /// <summary>
    /// Says F7's <c>background transparency (%)</c>, 0 (opaque) to
    /// <see cref="SharpMUTerm.Core.Configuration.TextSettings.MaxBackgroundTransparency"/>. Called on the client's thread,
    /// once a frame has been painted, and only when the level changed.
    /// </summary>
    void Show(int percent);
}
