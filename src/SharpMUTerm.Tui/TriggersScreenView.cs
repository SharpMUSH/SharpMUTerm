using SharpMUTerm.Core.Theming;
using SharpMUTerm.Core.Configuration;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;

namespace SharpMUTerm.Tui;

/// <summary>
/// Composes the F2 Triggers &amp; spawn routing screen from real panels (grids) rather than one
/// merged markup blob: a header band carrying the keyboard hints, a body whose rule-list panel and
/// editor panel are separated by a vertical rule, and a Cancel/Save action bar pinned to the last
/// row. The markup for each panel comes from the pure <see cref="TriggersScreenRenderer"/> so the
/// content stays unit-tested; this only lays it out.
/// </summary>
internal static class TriggersScreenView
{
    public static IWindowControl Build(
        IReadOnlyList<TriggerSet> sets,
        int selectedTrigger,
        IReadOnlyList<string> routeTargets,
        int width,
        ScreenTheme scheme,
        ScreenFocus? focus = null,
        int height = 0,
        Theme? theme = null,
        IReadOnlyList<string>? recent = null)
    {
        var header = ScreenChrome.Band(
            TriggersScreenRenderer.HeaderLine(
                width, TriggersScreenRenderer.Model(sets, selectedTrigger, routeTargets), focus),
            ScreenPalette.HeaderBg, scheme);
        var footer = ScreenChrome.Band(
            TriggersScreenRenderer.FooterLine(sets, selectedTrigger, width, focus), ScreenPalette.FooterBg, scheme);

        // Body: rule list │ editor, as two real columns. The split is a function of the screen's width:
        // at 120 the rule list keeps the share it was designed with, and at 100 it gives cells back to
        // the editor rather than letting the attribute legend run off the right-hand edge.
        var body = ScreenChrome.Rows(height);
        if (ScreenChrome.TooNarrowForTwo(
                width, TriggersScreenRenderer.MinColumnWidth, TriggersScreenRenderer.MinEditorWidth))
        {
            // One column at a time: the rule list, or the editor once the keyboard has moved into it or
            // ⏎ has opened one of the rule's fields, which are drawn in the editor and nowhere else.
            var only = focus is { } f && (f.InPane(1) || f.IsEditing)
                ? TriggersScreenRenderer.EditorColumn(sets, selectedTrigger, routeTargets, focus, width, body, theme, recent)
                : TriggersScreenRenderer.RulesColumn(sets, selectedTrigger, focus, width);
            return ScreenChrome.Single(
                header, footer, ScreenChrome.Stretch(ScreenChrome.Panel(ScreenChrome.Window(only, body), scheme)),
                only.Count, body);
        }

        var rules = ScreenChrome.SplitWidth(
            width,
            TriggersScreenRenderer.ColumnWidth,
            TriggersScreenRenderer.MinColumnWidth,
            TriggersScreenRenderer.MinEditorWidth);
        var left = TriggersScreenRenderer.RulesColumn(sets, selectedTrigger, focus, rules);
        var right = TriggersScreenRenderer.EditorColumn(
            sets, selectedTrigger, routeTargets, focus,
            width <= 0 ? rules : width - rules - ScreenChrome.ColumnDivider, body, theme, recent);

        var rulesCol = ScreenChrome.Stretch(ScreenChrome.Panel(ScreenChrome.Window(left, body), scheme));
        var editorCol = ScreenChrome.Stretch(ScreenChrome.Panel(
            ScreenChrome.Indent(ScreenChrome.Window(right, body)), scheme));
        return ScreenChrome.Split(header, footer, rulesCol, editorCol, rules, Math.Max(left.Count, right.Count), body, scheme);
    }
}
