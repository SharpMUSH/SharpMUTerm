using SharpMUTerm.Client.Panels;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui.Panels;

/// <summary>
/// A collapsed panel's bar as a row of cells: <c>▸ Chat  2 new ······ collapsed in combat</c>, the whole row one link
/// (<see cref="ExpandLink"/>) the bar's own control answers by expanding the panel. Drawn from a
/// <see cref="CollapsedPanelModel"/>, the one the window's native bar is drawn from. Elided to the width, never wrapped.
/// </summary>
internal static class CollapsedBarRenderer
{
    /// <summary>The link the row carries; only the bar's own control answers it.</summary>
    internal const string ExpandLink = "mux:collapsed:expand";

    /// <summary>The row for <paramref name="width"/> cells.</summary>
    internal static string Render(CollapsedPanelModel model, int width, string accent)
    {
        ArgumentNullException.ThrowIfNull(model);
        width = Math.Max(1, width);
        var left = $"▸ [bold]{Escape(model.Title)}[/]" + (model.Unread > 0 ? $"  [bold {accent}]{Escape(model.UnreadText)}[/]" : string.Empty);
        var right = $"[dim]{Escape(model.Note)}[/]";
        var row = VisibleLength(left) + VisibleLength(right) + 2 <= width ? SpreadLR(left, right, width) : ElideVisible(left, width);
        return $"[link={ExpandLink}]{row}[/]";
    }
}
