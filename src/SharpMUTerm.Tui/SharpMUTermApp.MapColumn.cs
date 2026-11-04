using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Tui;

/// <summary>
/// The side column a desktop host draws natively (<see cref="HostChrome.Strips"/>, <see cref="HostChrome.Tabs"/>): a vitals
/// card as tall as its gauges, and a map pane with no tab strip, so the map has the rows the window would otherwise leave
/// blank. Rows only; the pane rectangles, and so NAWS, are what is arranged. The terminal keeps the cell rules.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The card rows each vitals window was last laid out with, where the host draws gauges; see <see cref="ResizeVitalsCard"/>.</summary>
    private readonly Dictionary<string, int> _vitalsCardRows = new(StringComparer.Ordinal);

    /// <summary>The rows a pane claims (<see cref="PaneNode.FixedRows"/>), as the last rebuild derived them. Internal for tests.</summary>
    internal int? PaneFixedRows(string paneId) => _workspace.Layout.FindPane(paneId)?.FixedRows;

    /// <summary>The card rows a vitals window takes where the host draws gauges: its meters, and the target only while there is one.</summary>
    private int VitalsGaugeRowsOfWindow(string windowId)
    {
        var vitals = TerminalPanels.TryParseWindowId(windowId, out _, out var sessionKey) ? ReadVitals(sessionKey) : null;
        var meters = vitals is { } shown ? shown.Meters.Count : VitalsMetersOfWindow(windowId);
        var rows = PanelSizing.VitalsGaugeCardRows(meters, vitals?.Target is not null);
        _vitalsCardRows[windowId] = rows;
        return rows;
    }

    /// <summary>
    /// <see cref="PanelSizing.ApplyAll"/>'s card rule for this host: the gauges' height where the host draws strips, else
    /// null for the cell card (whose target rows are held whether or not there is one).
    /// </summary>
    private Func<string, int>? VitalsCardRowsRule()
    {
        if (!Host.Chrome.Strips)
        {
            return null;
        }

        _vitalsCardRows.Clear();
        return VitalsGaugeRowsOfWindow;
    }

    /// <summary>
    /// Re-lays the pane area when a vitals card's gauges came to need other rows (a meter or the target arriving or going).
    /// Only where the host draws gauges, and only for a card <see cref="PanelSizing.ApplyAll"/> sized.
    /// </summary>
    private void ResizeVitalsCard(string windowId)
    {
        if (!Host.Chrome.Strips || !_vitalsCardRows.TryGetValue(windowId, out var applied))
        {
            return;
        }

        var vitals = TerminalPanels.TryParseWindowId(windowId, out _, out var sessionKey) ? ReadVitals(sessionKey) : null;
        var meters = vitals is { } shown ? shown.Meters.Count : VitalsMetersOfWindow(windowId);
        if (PanelSizing.VitalsGaugeCardRows(meters, vitals?.Target is not null) != applied)
        {
            RebuildPaneArea();
        }
    }

    /// <summary>
    /// After <see cref="PanelSizing.ApplyAll"/>: where the host draws the tab strips, a pane holding the map window and
    /// nothing else draws none, as a lone panel does — the map's own heading names it (Map, the area and the level), and the
    /// two rows go to the rooms. The terminal keeps its strip: there it is the cell map's drag handle and its ×.
    /// </summary>
    private void ApplyLoneMapPanes()
    {
        if (!Host.Chrome.Tabs)
        {
            return;
        }

        foreach (var pane in _workspace.Layout.Root.Panes())
        {
            if (pane.Tabs.Count == 1 && IsMapWindow(pane.Tabs[0]) && pane.FixedRows is null)
            {
                pane.DrawsHeader = false;
                pane.MinRows = null;
            }
        }
    }
}
