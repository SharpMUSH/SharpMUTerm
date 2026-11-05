using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Tui;
using Window = Avalonia.Controls.Window;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// A native menu is as wide as its widest label plus its widest key: no key is cut at the menu's edge. The report was
/// the output's right-click menu showing <c>Ctr</c>, <c>Shi</c> and a clipped <c>F11</c> in its key column, on a scaled
/// Windows display. Avalonia sizes a pointer-placed popup once, at a first arrange laid out before it knows its screen's
/// scaling, and ignores every later size; the headless platform has no such first pass, so the clipping itself is seen
/// only in a real window (under Xvfb at 150%: 299×358 for a menu wanting 301×372). What is held here is the cure: once
/// open, a pointer menu is re-placed as an anchored one at the same point, which Avalonia does resize.
/// </summary>
[NotInParallel]
public class NativeMenuFitTests
{
    private static readonly PointerMenuItem[] Pointer =
    [
        new("Copy the selection", "⌃C", () => { }),
        new("Make a trigger from the selection", null, () => { }),
        PointerMenuItem.Separator,
        new("Split right", "⌃B |", () => { }),
        new("Split down", "⌃B -", () => { }),
        new("Zoom pane", "⌃B z", () => { }),
        new("Close pane", "⌃B x", () => { }),
        PointerMenuItem.Separator,
        new("Freeze pane", "⌥F", () => { }, Checked: true),
        new("Clear window", null, () => { }),
        PointerMenuItem.Separator,
        new("Full screen", "F11", () => { }),
        new("Windowed full screen", "⇧F11", () => { }),
        PointerMenuItem.Separator,
        new("More commands…", "⌃P", () => { }),
    ];

    [Test]
    public async Task TheRightClickMenuShowsEveryKeyWhole()
    {
        var cut = await Open(target => NativeMenu.Build(Pointer, PlacementMode.Pointer, target));
        await Assert.That(cut).IsEmpty();
    }

    [Test]
    public async Task AHeaderMenuShowsEveryKeyWhole()
    {
        var model = new MenuModel(
        [
            new MenuEntry("Combat", "⌃B 1", "a"),
            new MenuEntry("Roleplay", "⌃B 2", "b"),
            MenuEntry.Separator,
            new MenuEntry("Save the layout as a new one", "⌥⇧M", "c"),
        ]);
        var cut = await Open(target => NativeMenu.Build(model, _ => { }, PlacementMode.Bottom, target));
        await Assert.That(cut).IsEmpty();
    }

    [Test]
    public async Task APointerMenuIsReAnchoredWhereItOpenedSoItCanGrow()
    {
        var (placement, anchor, gravity, rect) = await TerminalViewTests.Session.Dispatch(() =>
        {
            var target = new Border();
            var window = new Window { Content = target, Width = 900, Height = 700 };
            window.Show();
            DesktopLayerTests.Drain();
            var menu = NativeMenu.Build(Pointer, PlacementMode.Pointer, target);
            try
            {
                menu.Open(target);
                DesktopLayerTests.Drain();
                var popup = (Popup)menu.Parent!;
                return (popup.Placement, popup.PlacementAnchor, popup.PlacementGravity, popup.PlacementRect);
            }
            finally
            {
                menu.Close();
                window.Close();
            }
        }, CancellationToken.None);

        // Avalonia re-sends the position, with the size the entries want, only for a placement that is not the pointer.
        await Assert.That(placement).IsEqualTo(PlacementMode.AnchorAndGravity);
        await Assert.That(anchor).IsEqualTo(PopupAnchor.TopLeft);
        await Assert.That(gravity).IsEqualTo(PopupGravity.BottomRight);
        await Assert.That(rect).IsNotNull();
    }

    /// <summary>Opens the menu over a window and lists every text drawn narrower than it wants or past its row's edge.</summary>
    private static Task<List<string>> Open(Func<Control, ContextMenu> build) =>
        TerminalViewTests.Session.Dispatch(() =>
        {
            var target = new Border();
            var window = new Window { Content = target, Width = 900, Height = 700 };
            window.Show();
            DesktopLayerTests.Drain();
            var menu = build(target);
            try
            {
                menu.Open(target);
                DesktopLayerTests.Drain();
                var cut = new List<string>();
                var root = TopLevel.GetTopLevel(menu) ?? (Visual)menu;
                var items = menu.GetLogicalDescendants().OfType<MenuItem>().ToList();
                if (items.Count == 0)
                {
                    cut.Add("no items");
                }

                foreach (var item in items)
                {
                    foreach (var block in item.GetVisualDescendants().OfType<TextBlock>())
                    {
                        if (block.Text is not { Length: > 0 } text || !block.IsEffectivelyVisible)
                        {
                            continue;
                        }

                        // The text's own width, without the margin its desired size carries, and where its right edge
                        // falls in the popup: past the popup's edge it is cut.
                        var wanted = block.DesiredSize.Width - block.Margin.Left - block.Margin.Right;
                        var right = block.TranslatePoint(new Point(block.Bounds.Width, 0), root)?.X ?? double.NaN;
                        if (block.Bounds.Width + 0.5 < wanted || !(right <= root.Bounds.Width + 0.5))
                        {
                            cut.Add($"{text}: width {block.Bounds.Width:0.#} of {wanted:0.#}, right {right:0.#} of {root.Bounds.Width:0.#}");
                        }
                    }
                }

                return cut;
            }
            finally
            {
                menu.Close();
                window.Close();
            }
        }, CancellationToken.None);
}
