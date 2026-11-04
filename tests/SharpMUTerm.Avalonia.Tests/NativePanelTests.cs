using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;
using SharpMUTerm.Tui.Panels;
using static SharpMUTerm.Avalonia.Tests.DesktopLayerTests;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window's native who-here list and quick bar (<see cref="WhoHereList"/>, <see cref="QuickBarButtons"/>): drawn
/// over their panes from the models the cell panels are drawn from, moving no pane, and handing a click to the
/// client's own callback — never to the driver, and never as a command the window built.
/// </summary>
public class NativePanelTests
{
    private static readonly WhoHereModel Who = WhoHereModel.From(
        "The Grand Plaza",
        [
            new RoomOccupant("Ashlyn", "Ashlyn the Wanderer", true, 30),
            new RoomOccupant("Bo", "Bo", true, 900),
            new RoomOccupant("rat", "a sewer rat", false, Id: "7"),
        ]);

    private static readonly QuickBarModel Bar = new(
    [
        new QuickBarItem("F11", "F11", "kick", true),
        new QuickBarItem("F12", "F12", "bash", true),
        new QuickBarItem("⌃F3", "Ctrl+F3", "cure light", true),
        new QuickBarItem("⌃F4", "Ctrl+F4", "quaff mana", true),
        new QuickBarItem("⌃F5", "Ctrl+F5", "rescue", false),
    ]);

    private static readonly PanelInk Ink = new("#3ccf6a", "#ff5555", "#333338");

    // ── The client's half ────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task TheClientPublishesWhoHereWithTheModelItsCellRowsAreDrawnFrom()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = InlineWindowApp(driver);
        app.RenderSnapshot("layout-roleplay");

        var panel = driver.Pointer.Panels.SingleOrDefault(p => p.PanelId == PanelIds.WhoHere);
        await Assert.That(panel).IsNotNull();
        var pane = app.PaneOutputRects().Single(r => app.PaneWindows().GetValueOrDefault(r.Key) == panel!.WindowId);
        await Assert.That(panel!.Rect).IsEqualTo(pane.Value);
        await Assert.That(panel.WhoHere).IsEqualTo(app.ReadWhoHere(DemoScene.ActiveSessionKey));
        await Assert.That(panel.WhoHere!.Rows.Count).IsGreaterThan(0);
        await Assert.That(panel.Pick).IsNotNull();

        // Row N of the model is row N of the cells: the same name under the same link index.
        var cells = WhoHereRenderer.Render(panel.WhoHere, 60, 40, new WhoHereRenderer.Ink("#00f5b7"));
        for (var i = 0; i < panel.WhoHere.Rows.Count; i++)
        {
            var line = cells.Single(l => l.Contains($"[link={WhoHereRenderer.LinkPrefix}{i}]", StringComparison.Ordinal));
            await Assert.That(Plain(line)).Contains(panel.WhoHere.Rows[i].Display);
        }
    }

    [Test]
    public async Task TheClientPublishesTheQuickBarWithTheModelItsCellRowIsDrawnFrom()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = InlineWindowApp(driver);
        app.RenderSnapshot("layout-combat");

        var panel = driver.Pointer.Panels.SingleOrDefault(p => p.PanelId == PanelIds.QuickBar);
        await Assert.That(panel).IsNotNull();
        var pane = app.PaneOutputRects().Single(r => app.PaneWindows().GetValueOrDefault(r.Key) == panel!.WindowId);
        await Assert.That(panel!.Rect).IsEqualTo(pane.Value);
        await Assert.That(panel.QuickBar).IsEqualTo(app.QuickBarEntries(DemoScene.ActiveSessionKey));
        await Assert.That(panel.QuickBar!.Items.Select(i => i.Key).Take(2)).IsEquivalentTo(new[] { "F11", "F12" });

        var row = QuickBarRenderer.Render(panel.QuickBar, 140, "#00f5b7");
        for (var i = 0; i < panel.QuickBar.Items.Count; i++)
        {
            var item = panel.QuickBar.Items[i];
            await Assert.That(row).Contains($"[link={QuickBarRenderer.LinkPrefix}{i}]");
            await Assert.That(Plain(row)).Contains($"{item.Key} {item.Title}");
        }
    }

    [Test]
    public async Task NativeListsMoveNoPaneRectangle()
    {
        foreach (var view in new[] { "layout-roleplay", "layout-combat" })
        {
            using var headless = new HeadlessConsoleDriver(140, 36);
            await using var terminal = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, WindowOptions.Capabilities));
            terminal.RenderSnapshot(view);

            using var driver = new AvaloniaConsoleDriver(140, 36);
            await using var window = InlineWindowApp(driver);
            window.RenderSnapshot(view);

            await Assert.That(driver.Pointer.Panels.Any(p => p.WhoHere is not null || p.QuickBar is not null)).IsTrue().Because(view);
            await Assert.That(window.PaneOutputRects().OrderBy(r => r.Key).ToArray())
                .IsEquivalentTo(terminal.PaneOutputRects().OrderBy(r => r.Key).ToArray());
        }
    }

    [Test]
    public async Task APickFromTheNativeListPutsLookOnTheCommandLineAndSendsNothing()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = InlineWindowApp(driver);
        app.RenderSnapshot("layout-roleplay");

        var panel = driver.Pointer.Panels.Single(p => p.PanelId == PanelIds.WhoHere);
        var first = panel.WhoHere!.Rows[0];
        panel.Pick!(0);
        app.RenderNextFrame();

        await Assert.That(app.PrimaryInputText).IsEqualTo($"look {first.Name}");

        // Out of range is nothing: the index is read back from what the client published, never trusted.
        panel.Pick(999);
        app.RenderNextFrame();
        await Assert.That(app.PrimaryInputText).IsEqualTo($"look {first.Name}");
        await Assert.That(ReferenceEquals(driver.Pointer.Panels.Single(p => p.PanelId == PanelIds.WhoHere).Pick, panel.Pick))
            .IsTrue().Because("one pick callback per window, so an overlay compares equal");
    }

    [Test]
    public async Task TheEditLinkDispatchesTheF4ScreenAndNoNativePanelCoversIt()
    {
        using var driver = new AvaloniaConsoleDriver(140, 36);
        await using var app = InlineWindowApp(driver);
        app.RenderSnapshot("layout-combat");

        var panel = driver.Pointer.Panels.Single(p => p.PanelId == PanelIds.QuickBar);
        await Assert.That(app.Contributions.Commands.Resolve(QuickBarModel.EditCommand)).IsNotNull();
        panel.Dispatch!(QuickBarModel.EditCommand);
        app.RenderNextFrame();
        await Assert.That(app.OpenSettingsKey).IsEqualTo(ConsoleKey.F4);
        await Assert.That(driver.Pointer.Panels).IsEmpty().Because("a native view would sit over the modal's cells");
    }

    // ── The window's half ────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task TheWhoHereListCoversItsPaneAndAClickReachesThePickNotTheDriver()
    {
        var (listRect, paneRect, picked, clientSaw, rows, hovered, tip) = await Run((window, view, layer, driver) =>
        {
            var picked = new List<int>();
            ((IPointerPresenter)driver.Pointer).Panels([new PanelOverlay(PanelIds.WhoHere, "panel:who:demo", new PaneRect(2, 1, 30, 8), Ink, null, WhoHere: Who, Pick: picked.Add)]);
            Drain();
            window.UpdateLayout();
            var list = layer.WhoHere.Values.Single();
            var cell = view.SizeFor(1, 1);
            var paneRect = new Rect(2 * cell.Width, 1 * cell.Height, 30 * cell.Width, 8 * cell.Height);
            var listRect = new Rect(Canvas.GetLeft(list), Canvas.GetTop(list), list.Bounds.Width, list.Bounds.Height);

            var clientSaw = new List<List<MouseFlags>>();
            driver.MouseEvent += (_, flags, _) => clientSaw.Add(flags);
            var second = list.Rows.Single(r => r.Index == 1).Rect;
            var at = list.TranslatePoint(second.Center, window)!.Value;
            window.MouseMove(at);
            var hovered = list.Hovered;
            var tip = ToolTip.GetTip(list) as string;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);

            // A right click is the client's: the pane's menu, as over cells.
            window.MouseDown(at, MouseButton.Right);
            window.MouseUp(at, MouseButton.Right);
            return (listRect, paneRect, picked, clientSaw, list.Rows.Count, hovered, tip);
        }, columns: 40, rows: 12);

        await Assert.That(listRect).IsEqualTo(paneRect);
        await Assert.That(rows).IsEqualTo(3);
        await Assert.That(picked).IsEquivalentTo(new[] { 1 });
        await Assert.That(hovered).IsEqualTo(1);
        await Assert.That(tip).IsNotNull().And.Contains("Bo").And.Contains("idle 15m");
        await Assert.That(clientSaw.Count).IsEqualTo(2).Because("only the right click is handed on, pressed and released");
        await Assert.That(clientSaw[0]).Contains(MouseFlags.Button3Pressed);
    }

    [Test]
    public async Task AShortPaneSaysHowManyTheListCouldNotShow()
    {
        var (rows, overflow) = await Run((window, view, layer, driver) =>
        {
            var many = WhoHereModel.From("Hall", Enumerable.Range(0, 20).Select(i => new RoomOccupant($"P{i}", $"P{i}", true)).ToList());
            ((IPointerPresenter)driver.Pointer).Panels([new PanelOverlay(PanelIds.WhoHere, "panel:who:demo", new PaneRect(0, 1, 30, 6), Ink, null, WhoHere: many, Pick: _ => { })]);
            Drain();
            window.UpdateLayout();
            var list = layer.WhoHere.Values.Single();
            return (list.Rows.Count, list.Overflow);
        }, columns: 40, rows: 10);

        await Assert.That(rows).IsGreaterThan(0).And.IsLessThan(20);
        await Assert.That(overflow).IsEqualTo($"… and {20 - rows} more");
    }

    [Test]
    public async Task TheQuickBarIsAButtonPerBindingWithTheLinkAndAClickReachesThePickNotTheDriver()
    {
        var (barRect, paneRect, kinds, picked, dispatched, clientSaw) = await Run((window, view, layer, driver) =>
        {
            var picked = new List<int>();
            var dispatched = new List<string>();
            ((IPointerPresenter)driver.Pointer).Panels(
                [new PanelOverlay(PanelIds.QuickBar, "panel:quickbar:demo", new PaneRect(0, 8, 100, 1), Ink, null, QuickBar: Bar, Pick: picked.Add, Dispatch: dispatched.Add)]);
            Drain();
            window.UpdateLayout();
            var bar = layer.QuickBars.Values.Single();
            var cell = view.SizeFor(1, 1);
            var paneRect = new Rect(0, 8 * cell.Height, 100 * cell.Width, cell.Height);
            var barRect = new Rect(Canvas.GetLeft(bar), Canvas.GetTop(bar), bar.Bounds.Width, bar.Bounds.Height);
            var clientSaw = new List<List<MouseFlags>>();
            driver.MouseEvent += (_, flags, _) => clientSaw.Add(flags);

            void Click(QuickBarButtons.Slot slot)
            {
                var at = bar.TranslatePoint(slot.Rect.Center, window)!.Value;
                window.MouseDown(at, MouseButton.Left);
                window.MouseUp(at, MouseButton.Left);
            }

            var slots = bar.Slots.ToList();
            Click(slots.Single(s => s.Kind == QuickBarButtons.SlotKind.Item && s.Index == 2));
            Click(slots.Single(s => s.Kind == QuickBarButtons.SlotKind.Edit));
            return (barRect, paneRect, slots.Select(s => s.Kind).ToArray(), picked, dispatched, clientSaw);
        }, columns: 100, rows: 10);

        await Assert.That(barRect).IsEqualTo(paneRect);
        await Assert.That(kinds.Count(k => k == QuickBarButtons.SlotKind.Item)).IsEqualTo(5).Because("every binding fits a wide bar");
        await Assert.That(kinds).Contains(QuickBarButtons.SlotKind.Edit);
        await Assert.That(kinds).DoesNotContain(QuickBarButtons.SlotKind.More);
        await Assert.That(picked).IsEquivalentTo(new[] { 2 });
        await Assert.That(dispatched).IsEquivalentTo(new[] { QuickBarModel.EditCommand });
        await Assert.That(clientSaw).IsEmpty().Because("a left click on the bar is the bar's");
    }

    [Test]
    public async Task ANarrowBarPutsWhatDoesNotFitUnderPlusNAndItsMenuPicksOnTheSameIndex()
    {
        var (more, overflowed, menu, picked) = await Run((window, view, layer, driver) =>
        {
            var picked = new List<int>();
            ((IPointerPresenter)driver.Pointer).Panels(
                [new PanelOverlay(PanelIds.QuickBar, "panel:quickbar:demo", new PaneRect(0, 8, 40, 1), Ink, null, QuickBar: Bar, Pick: picked.Add, Dispatch: _ => { })]);
            Drain();
            window.UpdateLayout();
            var bar = layer.QuickBars.Values.Single();
            var more = bar.Slots.Single(s => s.Kind == QuickBarButtons.SlotKind.More);
            var overflowed = bar.Overflowed.ToArray();
            bar.PerformClick(more);
            Drain();
            var items = bar.OpenedMenu!.Items.OfType<MenuItem>().ToList();
            items[^1].RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
            bar.OpenedMenu.Close();
            return (more, overflowed, items.Count, picked);
        }, columns: 40, rows: 10);

        await Assert.That(overflowed.Length).IsGreaterThan(0);
        await Assert.That(more.Index).IsEqualTo(overflowed.Length).Because("+N counts what it holds");
        await Assert.That(menu).IsEqualTo(overflowed.Length);
        await Assert.That(picked).IsEquivalentTo(new[] { overflowed[^1] });
    }

    [Test]
    public async Task NeitherNativePanelTakesKeyboardFocus()
    {
        var (whoFocusable, barFocusable, viewFocused) = await Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Panels(
            [
                new PanelOverlay(PanelIds.WhoHere, "panel:who:demo", new PaneRect(0, 1, 20, 6), Ink, null, WhoHere: Who, Pick: _ => { }),
                new PanelOverlay(PanelIds.QuickBar, "panel:quickbar:demo", new PaneRect(20, 8, 40, 1), Ink, null, QuickBar: Bar, Pick: _ => { }, Dispatch: _ => { }),
            ]);
            Drain();
            window.UpdateLayout();
            var list = layer.WhoHere.Values.Single();
            var bar = layer.QuickBars.Values.Single();
            var at = list.TranslatePoint(list.Rows[0].Rect.Center, window)!.Value;
            window.MouseDown(at, MouseButton.Left);
            window.MouseUp(at, MouseButton.Left);
            var b = bar.TranslatePoint(bar.Slots[0].Rect.Center, window)!.Value;
            window.MouseDown(b, MouseButton.Left);
            window.MouseUp(b, MouseButton.Left);
            return (list.Focusable, bar.Focusable, view.IsFocused);
        }, columns: 60, rows: 10);

        await Assert.That(whoFocusable).IsFalse();
        await Assert.That(barFocusable).IsFalse();
        await Assert.That(viewFocused).IsTrue();
    }

    [Test]
    public async Task ANativePanelGoesWhenItLeavesTheScreen()
    {
        var (shown, after) = await Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Panels(
            [
                new PanelOverlay(PanelIds.WhoHere, "panel:who:demo", new PaneRect(0, 1, 20, 6), Ink, null, WhoHere: Who, Pick: _ => { }),
                new PanelOverlay(PanelIds.QuickBar, "panel:quickbar:demo", new PaneRect(20, 8, 20, 1), Ink, null, QuickBar: Bar, Pick: _ => { }, Dispatch: _ => { }),
            ]);
            Drain();
            var shown = layer.WhoHere.Count + layer.QuickBars.Count;
            ((IPointerPresenter)driver.Pointer).Panels([]);
            Drain();
            return (shown, layer.WhoHere.Count + layer.QuickBars.Count);
        });

        await Assert.That(shown).IsEqualTo(2);
        await Assert.That(after).IsEqualTo(0);
    }
}
