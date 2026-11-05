using SharpMUTerm.Core.Configuration;
using SharpConsoleUI;
using SharpConsoleUI.Controls;

namespace SharpMUTerm.Tui;

/// <summary>
/// Composes the F6 Timers screen from real panels (grids) rather than one merged markup blob: a
/// header band carrying the keyboard hints, a body whose timer-list panel and editor panel are
/// separated by a vertical rule, and a Cancel/Save action bar pinned to the last row. The markup for
/// each panel comes from the pure <see cref="TimersScreenRenderer"/> so the content stays
/// unit-tested; this only lays it out (through <see cref="ScreenChrome.Split"/>, which every
/// two-column screen shares).
/// </summary>
internal static class TimersScreenView
{
    public static IWindowControl Build(
        IReadOnlyList<TriggerSet> sets, int selected, int width, ScreenTheme scheme, ScreenFocus? focus = null, int height = 0)
    {
        var header = ScreenChrome.Band(
            TimersScreenRenderer.HeaderLine(width, TimersScreenRenderer.Model(sets, selected), focus),
            ScreenPalette.HeaderBg, scheme);
        var footer = ScreenChrome.Band(
            TimersScreenRenderer.FooterLine(sets, selected, width, focus), ScreenPalette.FooterBg, scheme);

        var list = ScreenChrome.SplitWidth(
            width,
            TimersScreenRenderer.ColumnWidth,
            TimersScreenRenderer.MinColumnWidth,
            TimersScreenRenderer.MinEditorWidth);
        var rows = ScreenChrome.Rows(height);
        var left = TimersScreenRenderer.ListColumn(sets, selected, focus, list);
        var right = TimersScreenRenderer.EditorColumn(
            sets, selected, focus, width <= 0 ? list : width - list - ScreenChrome.ColumnDivider, rows);

        var listCol = ScreenChrome.Stretch(ScreenChrome.Panel(ScreenChrome.Window(left, rows), scheme));
        var editorCol = ScreenChrome.Stretch(
            ScreenChrome.Panel(ScreenChrome.Indent(ScreenChrome.Window(right, rows)), scheme));
        return ScreenChrome.Split(
            header, footer, listCol, editorCol, list, Math.Max(left.Count, right.Count), rows, scheme);
    }
}
