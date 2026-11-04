namespace SharpMUTerm.Tui;

/// <summary>
/// The boundary bars in a pane's buffer (restore, away, activity, search) are as wide as the pane
/// (<see cref="BarRule"/>). Each keeps the function that draws it (<see cref="PaneLine.Bar"/>), and that function
/// reads the pane's width when it runs, so a resize redraws the bars in place — no index moves.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>The width each window's bars were last drawn at; a change redraws them.</summary>
    private readonly Dictionary<string, int> _chromeBarWidths = new(StringComparer.Ordinal);

    /// <summary>
    /// The cells a bar in <paramref name="windowId"/> may take: its viewport's width, or 0 while the window has no
    /// arranged pane (the bar then draws its fallback rule, and is redrawn once the pane is laid out).
    /// </summary>
    internal int BarWidth(string windowId) =>
        _paneScrolls.GetValueOrDefault(windowId) is { ViewportWidth: > 0 } panel ? panel.ViewportWidth : 0;

    /// <summary>
    /// Redraws the bars of every window whose pane width moved since they were drawn. Rides <c>PostBufferPaint</c>,
    /// where viewport metrics are fresh; it converges, because the next frame finds the width unchanged.
    /// </summary>
    private void SyncChromeBarWidths()
    {
        var accent = (string?)null;
        foreach (var (windowId, buffer) in _lines)
        {
            var width = BarWidth(windowId);
            if (width <= 0 || _chromeBarWidths.GetValueOrDefault(windowId) == width)
            {
                continue;
            }

            _chromeBarWidths[windowId] = width;
            var changed = false;
            for (var i = 0; i < buffer.Count; i++)
            {
                if (buffer[i].Bar is { } bar)
                {
                    accent ??= FrozenAccentHex();
                    var markup = bar(accent);
                    if (!string.Equals(markup, buffer[i].Markup, StringComparison.Ordinal))
                    {
                        buffer[i] = buffer[i] with { Markup = markup };
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                RepaintPane(windowId, keepSelection: true);
            }
        }
    }
}
