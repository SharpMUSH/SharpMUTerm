using SharpMUTerm.Core.Configuration;
using SharpConsoleUI;
using SharpConsoleUI.Controls;

namespace SharpMUTerm.Tui;

/// <summary>
/// Lays the trigger sets screen out as every two-column screen is (<see cref="ScreenChrome.Split"/>): the set list on
/// the left, the selected set's wells and USED BY checklist on the right. The markup is
/// <see cref="TriggerSetsScreenRenderer"/>'s.
/// </summary>
internal static class TriggerSetsScreenView
{
    public static IWindowControl Build(
        IReadOnlyList<WorldDefinition> worlds,
        IList<TriggerSet> sets,
        int selected,
        CharacterDefinition? usedBy,
        int width,
        ScreenTheme scheme,
        ScreenFocus? focus = null,
        int height = 0)
    {
        var model = TriggerSetsScreenRenderer.Model(worlds, sets, selected, usedBy);
        var header = ScreenChrome.Band(
            TriggerSetsScreenRenderer.HeaderLine(width, model, focus), ScreenPalette.HeaderBg, scheme);
        var footer = ScreenChrome.Band(
            TriggerSetsScreenRenderer.FooterLine(sets.ToList(), selected, width, focus), ScreenPalette.FooterBg, scheme);

        var list = ScreenChrome.SplitWidth(
            width,
            TriggerSetsScreenRenderer.ColumnWidth,
            TriggerSetsScreenRenderer.MinColumnWidth,
            TriggerSetsScreenRenderer.MinEditorWidth);
        var rows = ScreenChrome.Rows(height);
        var left = TriggerSetsScreenRenderer.ListColumn(worlds, sets, selected, usedBy, focus, list);
        var right = TriggerSetsScreenRenderer.DetailColumn(
            worlds, sets.ToList(), selected, focus, (width <= 0 ? list : width - list - ScreenChrome.ColumnDivider) - 2, rows);

        var listCol = ScreenChrome.Stretch(ScreenChrome.Panel(ScreenChrome.Window(left, rows), scheme));
        var detailCol = ScreenChrome.Stretch(
            ScreenChrome.Panel(ScreenChrome.Indent(ScreenChrome.Window(right, rows)), scheme));
        return ScreenChrome.Split(
            header, footer, listCol, detailCol, list, Math.Max(left.Count, right.Count), rows, scheme);
    }
}
