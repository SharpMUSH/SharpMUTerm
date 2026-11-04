using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// A menu the client built (<see cref="PointerMenuItem"/>s), as a native <see cref="ContextMenu"/>: the right-click
/// menu and the header buttons' menus are drawn by this one function, so they look and behave alike. Each entry's
/// <see cref="PointerMenuItem.Run"/> is already marshalled onto the client's thread.
/// </summary>
internal static class NativeMenu
{
    public static ContextMenu Build(IReadOnlyList<PointerMenuItem> items, PlacementMode placement, Control target)
    {
        var menu = new ContextMenu { Placement = placement, PlacementTarget = target };
        Grid.SetIsSharedSizeScope(menu, true);
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                menu.Items.Add(new Separator());
                continue;
            }

            var entry = new MenuItem { Header = Header(item), IsEnabled = item.Enabled };
            if (item.Checked)
            {
                entry.ToggleType = MenuItemToggleType.CheckBox;
                entry.IsChecked = true;
            }

            entry.Click += (_, _) => item.Run();
            menu.Items.Add(entry);
        }

        return menu;
    }

    /// <summary>An entry's label with its key after it, the keys lined up in a column of their own.</summary>
    private static Control Header(PointerMenuItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "label" });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "hint" });
        grid.Children.Add(new TextBlock { Text = item.Label, VerticalAlignment = VerticalAlignment.Center });
        if (item.Hint is { } hint)
        {
            var key = new TextBlock { Text = hint, Opacity = 0.6, Margin = new Thickness(28, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(key, 1);
            grid.Children.Add(key);
        }

        return grid;
    }
}
