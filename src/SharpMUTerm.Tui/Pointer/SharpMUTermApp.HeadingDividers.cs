using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// A docked panel's heading band as the boundary above its pane (<see cref="PaneNode.DrawsHeading"/>): in a stack, the
/// composer's COMPOSE band and who-here's HERE band already mark where one pane ends and the next begins, so no divider
/// row is drawn over them and the band's empty run is the divider's handle — hover, drag, double-click, as a tab strip's
/// run is. Cells only: where the host draws the panels natively (<see cref="HostChrome.Strips"/>, the window) a native
/// view lies over that row and takes its presses, so the window keeps its divider row.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Each heading pane's empty band run, as the last paint left it, in desktop cells; swapped whole, read off-thread.</summary>
    private Dictionary<string, PaneRect> _headingRuns = new(StringComparer.Ordinal);

    /// <summary>
    /// After <see cref="PanelSizing.ApplyAll"/>: a pane holding one panel that draws a heading band, and no tab strip,
    /// stands its band in for the divider above it — in the cells only.
    /// </summary>
    private void ApplyHeadingPanes()
    {
        foreach (var pane in _workspace.Layout.Root.Panes())
        {
            pane.DrawsHeading = !Host.Chrome.Strips
                && !pane.DrawsHeader
                && pane.FixedRows is null
                && pane.Tabs.Count == 1
                && PanelSizing.DrawsHeadingBand(PanelIdOfWindow(pane.Tabs[0]));
        }
    }

    /// <summary>
    /// The empty run of <paramref name="paneId"/>'s heading band, or null when the pane draws none or nothing has painted
    /// it yet.
    /// </summary>
    private PaneRect? HeadingFill(string paneId) =>
        Volatile.Read(ref _headingRuns).TryGetValue(paneId, out var run) ? run : null;

    /// <summary>
    /// Reads each heading pane's band off the frame just painted: the longest run of blank cells on the band's plane (or
    /// the lit rule's, which <see cref="PaintHeaderDividers"/> leaves there) along the pane's top row. Read off the paint
    /// because three renderers lay their headings out three ways (the composer's length sits at the far end), and a second
    /// arithmetic for where their text ends would drift from what is drawn.
    /// </summary>
    private void MeasureHeadingRuns(CharacterBuffer buffer)
    {
        var runs = new Dictionary<string, PaneRect>(StringComparer.Ordinal);
        if (_window is not null)
        {
            var band = WorkspacePalette.HeadingBand(_theme);
            var hover = WorkspacePalette.RuleHover(_theme);
            var top = _window.Top + _system.DesktopUpperLeft.Y;
            foreach (var slot in _dividers)
            {
                if (slot.HeaderPaneId is not { } paneId || _workspace.Layout.FindPane(paneId) is not { DrawsHeading: true })
                {
                    continue;
                }

                TabControl? tabs;
                lock (_paneTabsLock)
                {
                    tabs = _paneTabs.GetValueOrDefault(paneId);
                }

                if (tabs is null || BoundsOf(tabs) is not { Width: > 0, Height: > 0 } rect)
                {
                    continue;
                }

                var by = rect.Y - top;
                int bestFrom = 0, bestLength = 0, from = -1;
                for (var x = rect.X; x <= rect.X + rect.Width; x++)
                {
                    var bx = x - _window.Left;
                    var blank = x < rect.X + rect.Width && by >= 0 && by < buffer.Height && bx >= 0 && bx < buffer.Width
                        && buffer.GetCell(bx, by) is var cell
                        && cell.Character.Value == ' '
                        && (Same(cell.Background, band) || Same(cell.Background, hover));
                    if (blank && from < 0)
                    {
                        from = x;
                    }
                    else if (!blank && from >= 0)
                    {
                        if (x - from > bestLength)
                        {
                            (bestFrom, bestLength) = (from, x - from);
                        }

                        from = -1;
                    }
                }

                if (bestLength > 0)
                {
                    runs[paneId] = new PaneRect(bestFrom, rect.Y, bestLength, 1);
                }
            }
        }

        Volatile.Write(ref _headingRuns, runs);

        static bool Same(SharpConsoleUI.Color colour, SharpMUTerm.Core.Text.Rgb rgb) =>
            colour.R == rgb.R && colour.G == rgb.G && colour.B == rgb.B;
    }
}
