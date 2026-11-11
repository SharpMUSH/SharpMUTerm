using SharpConsoleUI.Controls;

namespace SharpMUTerm.Tui;

/// <summary>
/// The wrap column (F7 ▸ TEXT ▸ <c>wrap output at</c>, <see cref="SharpMUTerm.Core.Configuration.TextSettings.WrapColumns"/>): a
/// pane's output wraps at that many columns, and each connected world is told the same width over NAWS, so a server
/// that wraps to its client wraps where the pane does.
/// <para>
/// <b>The pane keeps its rectangle; the text gets a right margin.</b> Each output control's right padding is the
/// viewport less the column, so the framework wraps at the column and leaves the cells past it blank in the pane's
/// own plane. Nothing that measures a pane moves: dividers, NAWS's rows, selection, the scrollbar and the split halves
/// all keep the pane they had. <see cref="MeasureRows"/> already copies a control's padding, so a measured row is
/// still a row on screen, and <see cref="BarWidth"/> answers the column, so a bar in the buffer fits the text it sits
/// in rather than wrapping past it.
/// </para>
/// <para>
/// <b>Never wider than the pane.</b> A pane narrower than the column wraps at the pane, and that is the width
/// reported: a server told a width the pane does not have wraps every line twice, once at its column and again at
/// ours. Zero, the default, is the pane exactly as it was.
/// </para>
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The right padding last set on each output control, so an unchanged frame sets nothing.</summary>
    private readonly Dictionary<MarkupControl, int> _wrapPadding = new();

    /// <summary>The column a pane <paramref name="room"/> cells wide wraps at: the setting, or the pane if narrower.</summary>
    internal int WrapWidth(int room) =>
        _config.Text.WrapColumns is > 0 and var column && column < room ? column : room;

    /// <summary>
    /// Sets every output control's right margin from its own viewport. Rides <c>PostBufferPaint</c>, where viewport
    /// metrics are fresh, ahead of <see cref="SyncChromeBarWidths"/>, which then redraws the bars at the new width; it
    /// converges, because the next frame finds every margin as it left it.
    /// </summary>
    private void SyncWrapWidths()
    {
        foreach (var (windowId, control) in _panes)
        {
            if (!string.Equals(windowId, WebWindowId, StringComparison.Ordinal))
            {
                SetWrapPadding(control, windowId);
            }
        }

        foreach (var (windowId, control) in _frozenPanes)
        {
            SetWrapPadding(control, FrozenRegionKey(windowId));
        }

        foreach (var (windowId, control) in _scrollTails)
        {
            SetWrapPadding(control, TailRegionKey(windowId));
        }
    }

    /// <summary>One control's margin, from the viewport registered under <paramref name="viewportKey"/>.</summary>
    private void SetWrapPadding(MarkupControl control, string viewportKey)
    {
        if (_paneScrolls.GetValueOrDefault(viewportKey) is not { ViewportWidth: > 0 } panel)
        {
            return;
        }

        var right = panel.ViewportWidth - WrapWidth(panel.ViewportWidth);
        if (_wrapPadding.GetValueOrDefault(control) == right)
        {
            return;
        }

        _wrapPadding[control] = right;
        control.Padding = new SharpConsoleUI.Layout.Padding(0, 0, right, 0);
    }
}
