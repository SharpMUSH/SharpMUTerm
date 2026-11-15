using System.Text.Json.Serialization;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Configuration;

/// <summary>
/// Where the desktop window was left: its place and size as an ordinary window, in screen pixels, and whether it was
/// maximised over them. Written by the window as it is moved, resized, maximised and restored, saved with the rest of the
/// configuration, and read when it next opens. A terminal has no window and never reads it.
/// <para>
/// Pixels rather than the window's logical units, because a logical size means something only on the screen it is
/// measured on: a window left on a 150% screen and reopened on a 100% one is the same rectangle of pixels on the desktop,
/// not the same number of logical units. The window converts on the screen it opens on.
/// </para>
/// </summary>
/// <param name="X">The left edge, in screen pixels (negative on a screen left of the primary one).</param>
/// <param name="Y">The top edge, in screen pixels.</param>
/// <param name="Width">The width of its ordinary (not maximised) bounds, in screen pixels.</param>
/// <param name="Height">The height of its ordinary bounds, in screen pixels.</param>
/// <param name="Maximised">Whether it was maximised when last left; <see cref="X"/>…<see cref="Height"/> are still
/// where restoring puts it.</param>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool Maximised = false)
{
    /// <summary>Smaller than this either way is not a size anybody left the window at, and is not restored.</summary>
    public const int MinimumSide = 120;

    /// <summary>Whether this holds a size worth restoring.</summary>
    [JsonIgnore]
    public bool IsUsable => Width >= MinimumSide && Height >= MinimumSide;

    /// <summary>
    /// These bounds moved and shrunk onto the screens there are now (each screen's working area, in screen pixels), so a
    /// window left on a monitor since unplugged, or larger than the screen it is now on, opens where it can be reached.
    /// Bounds wholly on a screen come back unchanged. <see cref="FloatingBounds.ClampTo"/>'s rule, which the popped-out
    /// panels already follow.
    /// </summary>
    public WindowPlacement ClampTo(IReadOnlyList<FloatingBounds> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        var clamped = new FloatingBounds(X, Y, Width, Height).ClampTo(screens);
        return this with
        {
            X = (int)Math.Round(clamped.X),
            Y = (int)Math.Round(clamped.Y),
            Width = (int)Math.Round(clamped.Width),
            Height = (int)Math.Round(clamped.Height),
        };
    }
}
