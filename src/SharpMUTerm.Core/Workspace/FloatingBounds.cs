using System.Text.Json.Serialization;

namespace SharpMUTerm.Core.Workspaces;

/// <summary>
/// Where a popped-out panel's own window sits on the desktop, in screen pixels: its left and top edge, and its width
/// and height. Host-neutral data: a host that can float a panel in a window of its own (the desktop window) puts it
/// here; a host that cannot (the terminal) reads a floating panel as placed in its region and ignores this, so the
/// same workspace and the same layout serve both.
/// <para>
/// <see cref="Unplaced"/> (no size) means "popped out, nowhere chosen yet": the host picks a place beside its main
/// window the first time it shows the panel and reports where it put it.
/// </para>
/// </summary>
/// <param name="X">The left edge, in screen pixels (may be negative on a screen left of the primary one).</param>
/// <param name="Y">The top edge, in screen pixels.</param>
/// <param name="Width">The width, in screen pixels; zero or less is <see cref="Unplaced"/>.</param>
/// <param name="Height">The height, in screen pixels; zero or less is <see cref="Unplaced"/>.</param>
public sealed record FloatingBounds(double X, double Y, double Width, double Height)
{
    /// <summary>Popped out with no place chosen yet.</summary>
    public static FloatingBounds Unplaced { get; } = new(0, 0, 0, 0);

    /// <summary>How much of a window must stay on a screen, each way, for it to count as reachable there.</summary>
    public const double MinimumVisible = 48;

    /// <summary>Whether a place has been chosen (a size on both axes).</summary>
    [JsonIgnore]
    public bool IsPlaced => Width > 0 && Height > 0;

    private double Right => X + Width;

    private double Bottom => Y + Height;

    /// <summary>
    /// These bounds moved and shrunk onto the screens a person can see, given each screen's working area in the same
    /// pixels. A window remembered on a monitor that has since been unplugged, or a size larger than the screen it is
    /// now on, would otherwise open somewhere nobody can reach to close it. The screen the window overlaps most is the
    /// one it is kept on (the nearest, by centre, when it overlaps none); it is shrunk to fit that screen's working area
    /// and then moved the least distance that puts all of it inside. Bounds already wholly on a screen come back
    /// unchanged; <see cref="Unplaced"/> and an empty screen list come back as they are.
    /// </summary>
    public FloatingBounds ClampTo(IReadOnlyList<FloatingBounds> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        var usable = screens.Where(s => s.IsPlaced).ToList();
        if (!IsPlaced || usable.Count == 0)
        {
            return this;
        }

        if (usable.Any(s => X >= s.X && Y >= s.Y && Right <= s.Right && Bottom <= s.Bottom))
        {
            return this;
        }

        double Overlap(FloatingBounds s) =>
            Math.Max(0, Math.Min(Right, s.Right) - Math.Max(X, s.X)) * Math.Max(0, Math.Min(Bottom, s.Bottom) - Math.Max(Y, s.Y));

        double Distance(FloatingBounds s)
        {
            var dx = (X + (Width / 2)) - (s.X + (s.Width / 2));
            var dy = (Y + (Height / 2)) - (s.Y + (s.Height / 2));
            return (dx * dx) + (dy * dy);
        }

        var screen = usable.Max(Overlap) > 0
            ? usable.OrderByDescending(Overlap).First()
            : usable.OrderBy(Distance).First();

        var width = Math.Min(Width, screen.Width);
        var height = Math.Min(Height, screen.Height);
        var x = Math.Clamp(X, screen.X, screen.Right - width);
        var y = Math.Clamp(Y, screen.Y, screen.Bottom - height);
        return new FloatingBounds(x, y, width, height);
    }
}
