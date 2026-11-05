using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;
using static SharpMUTerm.Avalonia.Tests.DesktopLayerTests;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// Dragging a tab off the window's native strip: a press pulled past the threshold hands the pointer to the layer, which
/// reports each move to the client as a cell (with the slot among the strip's tabs while over that strip), the release as
/// a drop, and draws the hint the client publishes back. The window decides no drop.
/// </summary>
[NotInParallel]
public class NativeTabDragWindowTests
{
    private static readonly Theme Dark = ThemeLibrary.Dark();

    private static TabStripModel Strip => new(
        "p2",
        false,
        [
            new TabChipModel("chat", "Chat", "Chat", true, 0, false, false, true, null),
            new TabChipModel("scenes", "Scenes", "Scenes", false, 0, false, false, false, null),
        ]);

    private static void Publish(AvaloniaConsoleDriver driver, List<TabDragStep> steps, TabDragModel? drag = null) =>
        ((IPointerPresenter)driver.Pointer).PaneChrome(new PaneChromeOverlay(
            [
                new TabStripOverlay(
                    new PaneRect(0, 0, 40, 2), Strip, "#222226", "#1c1c1f", [null, null], null,
                    (_, _) => { }, (_, _) => { }, (_, _) => { }, _ => { }, steps.Add),
            ],
            [],
            Dark,
            true,
            drag));

    [Test]
    public async Task APulledTabIsCarriedByTheLayerAndEveryStepIsACellForTheClient()
    {
        var (steps, dragging, released) = await Run((window, view, layer, driver) =>
        {
            var steps = new List<TabDragStep>();
            Publish(driver, steps);
            Drain();
            var strip = layer.Strips["p2"];
            var tab = strip.Tabs[0].Area;
            var press = new Point(tab.Center.X, tab.Center.Y);
            window.MouseMove(press);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(press + new Point(2, 0), RawInputModifiers.LeftMouseButton);
            var pulled = press + new Point(strip.Tabs[1].Area.Right - tab.Center.X + 4, 0);
            window.MouseMove(pulled, RawInputModifiers.LeftMouseButton);
            var body = new Point(view.CellExtent.Width * 30.5, view.CellExtent.Height * 6.5);
            window.MouseMove(body, RawInputModifiers.LeftMouseButton);
            var dragging = layer.TabDragging;
            window.MouseUp(body, MouseButton.Left);
            Drain();
            return (steps, dragging, layer.TabDragging);
        });

        await Assert.That(dragging).IsTrue().Because("past the threshold the layer holds the pointer");
        await Assert.That(released).IsFalse();
        await Assert.That(steps.Select(s => s.Phase).First()).IsEqualTo(TabDragPhase.Begin);
        await Assert.That(steps.Select(s => s.Phase).Last()).IsEqualTo(TabDragPhase.Drop);
        await Assert.That(steps.All(s => s.WindowId == "chat" && s.PaneId == "p2")).IsTrue();

        // Over its own strip, beyond the other tab: the second slot. Over the body: no slot, and the cell under the pointer.
        await Assert.That(steps.Any(s => s.Phase != TabDragPhase.Drop && s.ReorderIndex == 1 && s.Y <= 1)).IsTrue().Because(string.Join(";", steps));
        var last = steps[^1];
        await Assert.That(last.ReorderIndex).IsNull();
        await Assert.That((last.X, last.Y)).IsEqualTo((30, 6));
    }

    [Test]
    public async Task AShortPressIsStillAClickAndNoDrag()
    {
        var steps = await Run((window, view, layer, driver) =>
        {
            var steps = new List<TabDragStep>();
            Publish(driver, steps);
            Drain();
            var tab = layer.Strips["p2"].Tabs[1].Area;
            window.MouseMove(tab.Center);
            window.MouseDown(tab.Center, MouseButton.Left);
            window.MouseMove(tab.Center + new Point(2, 1), RawInputModifiers.LeftMouseButton);
            window.MouseUp(tab.Center + new Point(2, 1), MouseButton.Left);
            Drain();
            return steps;
        });

        await Assert.That(steps).IsEmpty();
    }

    [Test]
    public async Task TheHintIsWhatTheClientSaysTheDropWouldDo()
    {
        var (zone, cell, visible, after) = await Run((window, view, layer, driver) =>
        {
            var steps = new List<TabDragStep>();
            Publish(driver, steps);
            Drain();
            var strip = layer.Strips["p2"];
            layer.StartTabDrag(strip, strip.Tabs[0].Tab, strip.Tabs[0].Area.Center);
            layer.MoveTabDrag(new Point(view.CellExtent.Width * 2, view.CellExtent.Height * 5));
            Publish(driver, steps, new TabDragModel("chat", "p2", "p1", Edge.Left, null, new PaneRect(0, 3, 10, 6), "split pane 1 left"));
            Drain();
            var zone = layer.DragView.ZoneRect;
            var visible = layer.DragView.IsVisible;
            layer.EndTabDrag(new Point(4, 60), drop: false);
            Drain();
            return (zone, view.CellExtent, visible, layer.DragView.IsVisible);
        });

        await Assert.That(visible).IsTrue();
        await Assert.That(zone).IsEqualTo(new Rect(0, 3 * cell.Height, 10 * cell.Width, 6 * cell.Height));
        await Assert.That(after).IsFalse().Because("the hint goes with the drag");
    }

    [Test]
    public async Task TheSlotCountsTheOtherTabsLeftOfThePointerAndTheMarkSitsInThatGap()
    {
        IReadOnlyList<(string, Rect)> areas = [("a", new Rect(0, 0, 40, 20)), ("b", new Rect(42, 0, 40, 20)), ("c", new Rect(84, 0, 40, 20))];
        await Assert.That(NativeTabStrip.Slot(areas, "a", 10)).IsEqualTo(0);
        await Assert.That(NativeTabStrip.Slot(areas, "a", 70)).IsEqualTo(1);
        await Assert.That(NativeTabStrip.Slot(areas, "a", 200)).IsEqualTo(2);
        await Assert.That(NativeTabStrip.Slot(areas, "c", 10)).IsEqualTo(0);
        await Assert.That(NativeTabStrip.MarkAt(areas, "a", 2)).IsEqualTo(125);
        await Assert.That(NativeTabStrip.MarkAt(areas, "c", 0)).IsEqualTo(-1);
    }
}
