using Avalonia.Controls;
using Avalonia.Media;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The panes' overlay scrollbars, drawn over the native views the window lays on a pane rather than under them. A pane
/// split by scrolling back has a native band over its foot (<see cref="ScrollBandView"/>); drawn in the cells' view, the
/// track stopped at the band, so the bar read as shorter than the pane it scrolls. It lies over the cells at the cells'
/// origin and takes no input: a press on a bar is still <see cref="TerminalView"/>'s, which the band lets through.
/// </summary>
internal sealed class ScrollbarLayer : Control
{
    private readonly TerminalView _view;

    public ScrollbarLayer(TerminalView view)
    {
        _view = view;
        IsHitTestVisible = false;
        Focusable = false;
    }

    public override void Render(DrawingContext context) => _view.DrawScrollbars(context);
}
