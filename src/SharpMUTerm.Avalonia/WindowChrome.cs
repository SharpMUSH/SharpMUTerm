using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// What a press does to the window when it has no title bar (<see cref="WindowDecorations.BorderOnly"/>): the
/// two jobs a title bar did, given to places the client does not use.
/// <para>
/// <b>Moving</b> is a drag on a blank cell of the header row — the band the client draws along the top, which
/// is where a title bar was and where a hand goes to look for one. Only a blank cell: the header's segments are
/// clickable (the brand button, the world and character), so a press on text is the client's. A double-click
/// there maximises and restores, as a title bar's does. <b>Resizing</b> is a thin band at each edge, handled here
/// rather than left to the platform, because a borderless window on Linux gets a resize border only from some
/// window managers.
/// </para>
/// </summary>
internal static class WindowChrome
{
    /// <summary>How wide the resize band is, in logical pixels.</summary>
    public const double EdgeBand = 5;

    /// <summary>The edge a press at <paramref name="position"/> would resize, or null.</summary>
    public static WindowEdge? EdgeAt(Point position, Size bounds)
    {
        var left = position.X < EdgeBand;
        var right = position.X >= bounds.Width - EdgeBand;
        var top = position.Y < EdgeBand;
        var bottom = position.Y >= bounds.Height - EdgeBand;
        return (top, bottom, left, right) switch
        {
            (true, _, true, _) => WindowEdge.NorthWest,
            (true, _, _, true) => WindowEdge.NorthEast,
            (_, true, true, _) => WindowEdge.SouthWest,
            (_, true, _, true) => WindowEdge.SouthEast,
            (true, _, _, _) => WindowEdge.North,
            (_, true, _, _) => WindowEdge.South,
            (_, _, true, _) => WindowEdge.West,
            (_, _, _, true) => WindowEdge.East,
            _ => null,
        };
    }

    /// <summary>Whether a press on <paramref name="cell"/> takes hold of the window rather than reaching the client.</summary>
    public static bool IsMoveHandle(ScreenFrame frame, System.Drawing.Point cell) =>
        cell.Y == 0 && cell.X >= 0 && cell.X < frame.Width
        && frame[cell.X, 0] is { IsWideContinuation: false } c && c.Rune.Value == ' ' && c.Combiners is null;

    public static StandardCursorType CursorFor(WindowEdge edge) => edge switch
    {
        WindowEdge.North or WindowEdge.South => StandardCursorType.SizeNorthSouth,
        WindowEdge.West or WindowEdge.East => StandardCursorType.SizeWestEast,
        WindowEdge.NorthWest => StandardCursorType.TopLeftCorner,
        WindowEdge.NorthEast => StandardCursorType.TopRightCorner,
        WindowEdge.SouthWest => StandardCursorType.BottomLeftCorner,
        _ => StandardCursorType.BottomRightCorner,
    };
}
