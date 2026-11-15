using Avalonia;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// Keeps a window's first size inside the screen it opens on. The window's size is asked for in cells (<c>--size</c>,
/// 120×36 by default) and in logical pixels for the settings window, and both were fine at 100%: at 150% on a 1080p
/// screen the main window's 822 logical rows are 1,233 device pixels, so its command line and status row opened below
/// the screen's edge. A size is therefore shrunk to the screen's working area less <see cref="ScreenMargin"/> each side,
/// the main window by whole cells, so the grid it holds has no leftover strip.
/// </summary>
internal static class WindowFit
{
    /// <summary>What is kept free between a fitted window and the working area's edges, in logical pixels.</summary>
    public const double ScreenMargin = NativeTokens.Space24;

    /// <summary>
    /// The logical room a window has on a screen whose working area is <paramref name="workingArea"/> device pixels at
    /// <paramref name="scaling"/>, after <see cref="ScreenMargin"/> each side.
    /// </summary>
    public static Size Room(PixelRect workingArea, double scaling)
    {
        var scale = scaling > 0 ? scaling : 1;
        return new Size(
            Math.Max(0, (workingArea.Width / scale) - (2 * ScreenMargin)),
            Math.Max(0, (workingArea.Height / scale) - (2 * ScreenMargin)));
    }

    /// <summary>
    /// The main window's size for a grid wanted at <paramref name="wanted"/> logical pixels around cells of
    /// <paramref name="cell"/>, inside <paramref name="room"/>: unchanged when it fits, else fewer whole columns and rows
    /// (around <paramref name="chrome"/>, the native chrome and the field reserve, which do not shrink). Never below
    /// <paramref name="minimumColumns"/> × <paramref name="minimumRows"/>.
    /// </summary>
    public static Size InCells(Size wanted, Size room, Size cell, Size chrome, int minimumColumns = 20, int minimumRows = 8)
    {
        var width = wanted.Width;
        var height = wanted.Height;
        if (cell.Width > 0 && width > room.Width)
        {
            var columns = Math.Max(minimumColumns, (int)Math.Floor((room.Width - chrome.Width) / cell.Width));
            width = Math.Min(width, chrome.Width + (columns * cell.Width));
        }

        if (cell.Height > 0 && height > room.Height)
        {
            var rows = Math.Max(minimumRows, (int)Math.Floor((room.Height - chrome.Height) / cell.Height));
            height = Math.Min(height, chrome.Height + (rows * cell.Height));
        }

        return new Size(width, height);
    }

    /// <summary>
    /// A window's size inside <paramref name="room"/>: <paramref name="wanted"/> where it fits, never below
    /// <paramref name="minimum"/> (a window that cannot be smaller still opens whole at its minimum).
    /// </summary>
    public static Size Within(Size wanted, Size room, Size minimum) => new(
        Math.Max(minimum.Width, Math.Min(wanted.Width, room.Width)),
        Math.Max(minimum.Height, Math.Min(wanted.Height, room.Height)));

    /// <summary>
    /// The device-pixel position that centres a window of <paramref name="size"/> logical pixels in
    /// <paramref name="workingArea"/>, clamped so its top-left corner is on the screen.
    /// </summary>
    public static PixelPoint Centre(Size size, PixelRect workingArea, double scaling)
    {
        var scale = scaling > 0 ? scaling : 1;
        var x = workingArea.X + Math.Max(0, (int)Math.Round((workingArea.Width - (size.Width * scale)) / 2));
        var y = workingArea.Y + Math.Max(0, (int)Math.Round((workingArea.Height - (size.Height * scale)) / 2));
        return new PixelPoint(x, y);
    }

    /// <summary>
    /// How far, in screen pixels, a remembered window may reach past a screen's working area and still count as on it.
    /// A window snapped to an edge on Windows has its invisible resize border outside the working area, so without this
    /// a window left against the left edge would come back eight pixels in, and move again each run.
    /// </summary>
    public const int RememberedSlack = 16;

    /// <summary>
    /// Where a window left at <paramref name="saved"/> opens on the screens there are now (each one's working area in
    /// screen pixels and its scaling): its position in screen pixels and its size in logical pixels of the screen it lands
    /// on, kept wholly on a screen (<see cref="WindowPlacement.ClampTo"/>). Null when nothing usable was saved or there is
    /// no screen to put it on, and the window opens at its <c>--size</c> as before.
    /// </summary>
    public static (PixelPoint Position, Size Size)? Remembered(WindowPlacement? saved, IReadOnlyList<(PixelRect WorkingArea, double Scaling)> screens)
    {
        if (saved is not { IsUsable: true } || screens.Count == 0)
        {
            return null;
        }

        var areas = screens
            .Select(s => new FloatingBounds(
                s.WorkingArea.X - RememberedSlack,
                s.WorkingArea.Y - RememberedSlack,
                s.WorkingArea.Width + (2 * RememberedSlack),
                s.WorkingArea.Height + (2 * RememberedSlack)))
            .ToList();
        var placed = saved.ClampTo(areas);
        var rect = new PixelRect(placed.X, placed.Y, placed.Width, placed.Height);

        static long Overlap(PixelRect a, PixelRect b)
        {
            var i = a.Intersect(b);
            return (long)i.Width * i.Height;
        }

        var screen = screens.OrderByDescending(s => Overlap(s.WorkingArea, rect)).First();
        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        return (new PixelPoint(placed.X, placed.Y), new Size(placed.Width / scale, placed.Height / scale));
    }
}
