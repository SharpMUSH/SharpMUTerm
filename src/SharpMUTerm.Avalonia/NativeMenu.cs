using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// A menu the client built, as a native <see cref="ContextMenu"/>: the right-click menu (<see cref="PointerMenuItem"/>s)
/// and the header buttons' menus (a <see cref="MenuModel"/>) are drawn by this one class, so they look and behave
/// alike. A right-click entry's <see cref="PointerMenuItem.Run"/> is already marshalled onto the client's thread; a
/// header menu entry hands its command id back through the action's own dispatch, which the client checks against
/// the menu it built.
/// </summary>
internal static class NativeMenu
{
    public static ContextMenu Build(IReadOnlyList<PointerMenuItem> items, PlacementMode placement, Control target) =>
        Build(
            items.Select(i => i.IsSeparator ? null : ((string, string?, bool, bool, Action)?)(i.Label, i.Hint, i.Enabled, i.Checked, i.Run)),
            placement,
            target);

    /// <summary>A header button's menu: each entry hands its command id to <paramref name="dispatch"/>.</summary>
    public static ContextMenu Build(MenuModel model, Action<string> dispatch, PlacementMode placement, Control target) =>
        Build(
            model.Entries.Select(e => e.IsSeparator
                ? null
                : ((string, string?, bool, bool, Action)?)(e.Label, e.Key, e.Enabled && e.Command is not null, e.Checked, () =>
                {
                    if (e.Command is { } command)
                    {
                        dispatch(command);
                    }
                })),
            placement,
            target);

    private static ContextMenu Build(
        IEnumerable<(string Label, string? Hint, bool Enabled, bool Checked, Action Run)?> items, PlacementMode placement, Control target)
    {
        // The palette's card: the client's planes, ink and highlight, a rounded edge — not Simple's grey square.
        var tokens = NativeTokens.For(null, target.ActualThemeVariant != global::Avalonia.Styling.ThemeVariant.Light);
        var menu = new ContextMenu
        {
            Placement = placement,
            PlacementTarget = target,
            Background = tokens.Brush(tokens.Card),
            Foreground = tokens.Brush(tokens.Text),
            BorderBrush = tokens.Brush(tokens.Edge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            Padding = new Thickness(4),
            FontSize = NativeTokens.Body,
        };
        tokens.ApplySimple(menu.Resources);
        Grid.SetIsSharedSizeScope(menu, true);
        foreach (var item in items)
        {
            if (item is not { } entry)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var menuItem = new MenuItem { Header = Header(entry.Label, entry.Hint), IsEnabled = entry.Enabled };
            if (entry.Checked)
            {
                menuItem.ToggleType = MenuItemToggleType.CheckBox;
                menuItem.IsChecked = true;
            }

            var run = entry.Run;
            menuItem.Click += (_, _) => run();
            menu.Items.Add(menuItem);
        }

        return menu;
    }

    /// <summary>An entry's label with its key after it, the keys lined up in a column of their own, both in the window's words.</summary>
    private static Control Header(string label, string? hint)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "label" });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "hint" });
        grid.Children.Add(new TextBlock { Text = NativeKeys.Words(label), VerticalAlignment = VerticalAlignment.Center });
        if (hint is { } key)
        {
            var text = new TextBlock { Text = NativeKeys.Words(key), Opacity = 0.7, Margin = new Thickness(28, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }

        return grid;
    }
}
