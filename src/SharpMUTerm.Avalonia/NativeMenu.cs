using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
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

    /// <summary>
    /// How much larger than designed menus are drawn: the window's dialog text size and zoom (MainWindow sets it). A
    /// menu is a popup window of its own, so it is sized rather than transformed.
    /// </summary>
    internal static double Scale { get; set; } = 1;

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
            BorderThickness = new Thickness(NativeTokens.Hairline),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            Padding = new Thickness(NativeTokens.Space6),
            FontSize = NativeTokens.Body * Scale,
        };
        tokens.ApplySimple(menu.Resources);

        // The highlighted entry is the palette's selected row, a plane; Simple outlines it in its accent too, which on a
        // menu reads as a second, louder selection.
        menu.Resources["ThemeAccentBrush"] = tokens.Brush(tokens.Selected);

        // The popup's own window shows in the menu's rounded corners: clear where the platform composites it, else the
        // card's own plane, so a corner is never the popup's default white.
        menu.Opened += (_, _) =>
        {
            if (TopLevel.GetTopLevel(menu) is { } root and not Window)
            {
                root.TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
                root.Background = root.ActualTransparencyLevel == WindowTransparencyLevel.Transparent ? null : tokens.Brush(tokens.Card);
            }
        };
        KeepWhole(menu, target);
        Grid.SetIsSharedSizeScope(menu, true);
        foreach (var item in items)
        {
            if (item is not { } entry)
            {
                menu.Items.Add(new Separator { Background = tokens.Brush(tokens.Rule), Margin = new Thickness(NativeTokens.Space8, NativeTokens.Space4) });
                continue;
            }

            // The palette's row: the same height and inset, so a menu and ⌃P read as one list.
            var menuItem = new MenuItem
            {
                Header = Header(entry.Label, entry.Hint, tokens),
                IsEnabled = entry.Enabled,
                MinHeight = NativeTokens.ControlHeightSmall * Scale,
            };
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

    /// <summary>
    /// Holds a menu opened at the pointer where it opened, as an anchored placement, so it can grow to fit what it holds.
    /// Avalonia sizes a pointer-placed popup once, at its first arrange, and never again (<c>PopupRoot.ArrangeSetBounds</c>
    /// drops every later size change for <see cref="PlacementMode.Pointer"/>). That first arrange is laid out before the
    /// popup knows its screen's scaling and before the key column's shared size has settled, so on a scaled display the
    /// menu came out narrower and shorter than its entries: the keys were cut at its right edge (<c>Ctr</c>, <c>Shi</c>)
    /// and its last entry hidden under a scroll. Re-placing it at the same point re-sends the position with the size the
    /// entries now want, and from then on it is not a pointer placement, so a later change of size is honoured too.
    /// A menu drawn in the window's overlay layer (no popup window of its own) is left alone: its host already follows a
    /// change of size, and its top level is the window, whose corner is not where the menu opened.
    /// </summary>
    internal static void KeepWhole(ContextMenu menu, Control target) => menu.Opened += (_, _) => PinWherePointed(menu, target);

    private static void PinWherePointed(ContextMenu menu, Control target)
    {
        if (menu.Placement != PlacementMode.Pointer
            || menu.Parent is not Popup popup
            || TopLevel.GetTopLevel(menu) is not PopupRoot root
            || root.PointToScreen(default) is var corner && target.PointToClient(corner) is var at && !double.IsFinite(at.X))
        {
            return;
        }

        popup.PlacementRect = new Rect(at, new Size(1, 1));
        popup.PlacementAnchor = PopupAnchor.TopLeft;
        popup.PlacementGravity = PopupGravity.BottomRight;
        popup.Placement = PlacementMode.AnchorAndGravity;
    }

    /// <summary>An entry's label with its key after it, the keys lined up in a column of their own, both in the window's words.</summary>
    private static Control Header(string label, string? hint, NativeTokens tokens)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "label" });
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { SharedSizeGroup = "hint" });
        grid.Children.Add(new TextBlock { Text = NativeKeys.Words(label), VerticalAlignment = VerticalAlignment.Center });
        if (hint is { } key)
        {
            // The key in the quiet ink and the keys' face, as the mockup's rail spells Alt+1 — an ink held to the floor,
            // never an opacity.
            var text = new TextBlock
            {
                Text = NativeKeys.Words(key),
                FontFamily = NativeTokens.KeyFace,
                FontSize = NativeTokens.Small * Scale,
                Foreground = tokens.Brush(tokens.Dim),
                Margin = new Thickness(NativeTokens.Space32, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }

        return grid;
    }
}
