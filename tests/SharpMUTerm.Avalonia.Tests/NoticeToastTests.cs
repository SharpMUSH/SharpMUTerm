using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;
using static SharpMUTerm.Avalonia.Tests.DesktopLayerTests;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window's toasts (<see cref="NoticeToasts"/>) and the native map's legend and colours: a toast is the notice the
/// status row drew, stacked over the cells above the command line, held while hovered, faded after its time, never
/// focused; a click runs its action through the client and nothing else reaches the client. The map canvas draws its
/// areas in the model's colours.
/// </summary>
public class NoticeToastTests
{
    private static readonly NoticePalette Palette = new("#333344", "#e0e0e0", "#5fafff", "#ff6b6b", "#ffaf5f");

    private static NoticeOverlay Overlay(long sequence, string text, NoticeAction? action, Action<string> run, int aboveRow = 8) =>
        new(new NoticeModel(text, NoticeSeverity.Info, "⌥M", action, DateTimeOffset.UnixEpoch, sequence), aboveRow, Palette, run);

    /// <summary>
    /// A toast is a native card: the tokens' card plane, not the status row's, with every severity's edge (which inks its
    /// chip and action line) held to the text floor on that card, in each built-in theme.
    /// </summary>
    [Test]
    public async Task AToastIsDrawnOnTheTokensCardWithItsEdgesHeldToTheFloor()
    {
        foreach (var theme in SharpMUTerm.Core.Theming.ThemeLibrary.Names.Select(SharpMUTerm.Core.Theming.ThemeLibrary.Get))
        {
            var tokens = NativeTokens.For(theme);
            foreach (var severity in Enum.GetValues<NoticeSeverity>())
            {
                var overlay = new NoticeOverlay(new NoticeModel("x", severity, null, null, DateTimeOffset.UnixEpoch, 1), 8, Palette, _ => { })
                {
                    Theme = theme,
                };
                var colours = NoticeToasts.ColoursOf(overlay);
                await Assert.That(colours.Plane).IsEqualTo(tokens.Card).Because(theme.Name);
                var ratio = SharpMUTerm.Core.Text.Contrast.Ratio(
                    new SharpMUTerm.Core.Text.Rgb(colours.Edge.R, colours.Edge.G, colours.Edge.B),
                    new SharpMUTerm.Core.Text.Rgb(colours.Plane.R, colours.Plane.G, colours.Plane.B));
                await Assert.That(ratio).IsGreaterThanOrEqualTo(NativeTokens.TextFloor - 0.01).Because($"{theme.Name} {severity}");
            }
        }

        await Assert.That(NativeTokens.Dark.Card).IsEqualTo(global::Avalonia.Media.Color.FromRgb(0x17, 0x1b, 0x24)).Because("the mockup's card");
    }

    /// <summary>A notice's source is a caption on the toast, not a key chip: a word in a chip reads as a key to press.</summary>
    [Test]
    public async Task ASourceIsACaptionAndAKeyIsAChip()
    {
        var (labels, words) = await Run((window, view, layer, driver) =>
        {
            var notices = (IPointerPresenter)driver.Pointer;
            notices.Notice(new NoticeOverlay(new NoticeModel("muted.", NoticeSeverity.Info, null, null, DateTimeOffset.UnixEpoch, 1, NoticeModel.Sources.Sound), 8, Palette, _ => { }));
            notices.Notice(Overlay(2, "nothing to split", null, _ => { }));
            Drain();
            return (layer.Toasts.LabelIsChip(), layer.Toasts.DrawnText());
        }, columns: 80, rows: 20);

        await Assert.That(labels).IsEquivalentTo(new bool?[] { false, true });
        await Assert.That(words).Contains("Sound");
        await Assert.That(words).Contains("Alt+M");
    }

    [Test]
    public async Task AToastStacksAboveTheCommandLineAndAClickRunsItsActionWithoutTakingFocus()
    {
        var (shown, rects, bottom, ran, focused, clientSaw, remaining, passedThrough) = await Run((window, view, layer, driver) =>
        {
            var ran = new List<string>();
            var notices = (IPointerPresenter)driver.Pointer;
            notices.Notice(Overlay(1, "Using the Combat layout: the game sends vitals.", new NoticeAction("layout:layouts", "Change layout"), ran.Add));
            notices.Notice(Overlay(2, "nothing to split", null, ran.Add));
            Drain();
            window.UpdateLayout();

            var toasts = layer.Toasts;
            var shown = toasts.Shown.Select(n => n.Sequence).ToList();
            var rects = toasts.Rects();
            var bottom = toasts.Bottom;

            var clientSaw = new List<List<MouseFlags>>();
            driver.MouseEvent += (_, flags, _) => clientSaw.Add(flags);

            // A click on the toast with an action: it runs, the toast goes, and the client never sees the press.
            var on = toasts.TranslatePoint(rects[0].Center, window)!.Value;
            window.MouseDown(on, MouseButton.Left);
            window.MouseUp(on, MouseButton.Left);
            var focused = view.IsFocused;
            var remaining = toasts.Shown.Select(n => n.Sequence).ToList();
            var sawOnToast = clientSaw.Count;

            // A press beside the toasts goes through to the cells.
            var beside = toasts.TranslatePoint(new Point(40, 30), window)!.Value;
            window.MouseDown(beside, MouseButton.Left);
            window.MouseUp(beside, MouseButton.Left);
            return (shown, rects, bottom, ran, focused, sawOnToast, remaining, clientSaw.Count > sawOnToast);
        }, columns: 80, rows: 12);

        await Assert.That(shown).IsEquivalentTo(new long[] { 1, 2 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(rects[1].Y).IsGreaterThan(rects[0].Y).Because("the newest is at the bottom of the stack");
        await Assert.That(rects[1].Bottom).IsLessThanOrEqualTo(bottom).Because("the stack sits above the row the pane area ends at");
        await Assert.That(ran).IsEquivalentTo(new[] { "layout:layouts" });
        await Assert.That(focused).IsTrue().Because("a toast never takes the keyboard from the cells");
        await Assert.That(clientSaw).IsEqualTo(0).Because("a press on a toast is the window's");
        await Assert.That(remaining).IsEquivalentTo(new long[] { 2 });
        await Assert.That(passedThrough).IsTrue().Because("only a toast is hit; the rest of the layer is the cells'");
    }

    /// <summary>A notice with two actions (a saved scene: Open and Copy path) draws both, and a click on the second runs it.</summary>
    [Test]
    public async Task AToastsSecondActionIsDrawnAndAClickOnItRunsIt()
    {
        var (words, ran) = await Run((window, view, layer, driver) =>
        {
            var ran = new List<string>();
            var model = new NoticeModel("scene saved", NoticeSeverity.Info, null, new NoticeAction("scene:open:0", "Open"), DateTimeOffset.UnixEpoch, 1, NoticeModel.Sources.Scene)
            {
                Second = new NoticeAction("scene:copy:0", "Copy path"),
            };
            ((IPointerPresenter)driver.Pointer).Notice(new NoticeOverlay(model, 8, Palette, ran.Add));
            Drain();
            window.UpdateLayout();

            var toasts = layer.Toasts;
            var words = toasts.DrawnText();
            var on = toasts.TranslatePoint(toasts.SecondAreas()[0].Center, window)!.Value;
            window.MouseDown(on, MouseButton.Left);
            window.MouseUp(on, MouseButton.Left);
            return (words, ran);
        }, columns: 80, rows: 12);

        await Assert.That(words.Any(w => w.Contains("Open", StringComparison.Ordinal))).IsTrue();
        await Assert.That(words.Any(w => w.Contains("Copy path", StringComparison.Ordinal))).IsTrue();
        await Assert.That(ran).IsEquivalentTo(new[] { "scene:copy:0" });
    }

    [Test]
    public async Task AToastFadesAfterItsTimeButStaysWhileHovered()
    {
        var (held, faded, gone) = await Run((window, view, layer, driver) =>
        {
            ((IPointerPresenter)driver.Pointer).Notice(Overlay(1, "saved", null, _ => { }));
            Drain();
            window.UpdateLayout();
            var toasts = layer.Toasts;
            var over = toasts.TranslatePoint(toasts.Rects()[0].Center, window)!.Value;

            window.MouseMove(over);
            toasts.Advance(NoticeModel.Duration + TimeSpan.FromSeconds(2));
            var held = toasts.Shown.Count;

            window.MouseMove(toasts.TranslatePoint(new Point(2, 2), window)!.Value);
            toasts.Advance(NoticeToasts.HoverGrace + TimeSpan.FromMilliseconds(100));
            var faded = toasts.Shown.Count;
            toasts.Advance(NoticeToasts.FadeTime);
            return (held, faded, toasts.Shown.Count);
        }, columns: 80, rows: 12);

        await Assert.That(held).IsEqualTo(1).Because("a hovered toast stays");
        await Assert.That(faded).IsEqualTo(1).Because("it is fading, not gone");
        await Assert.That(gone).IsEqualTo(0);
    }

    /// <summary>
    /// A hovered toast is held past its time, so the newer toasts under it fade first. The stack used to close up as they
    /// went and carry the hovered toast out from under the pointer; it now holds still while the pointer is on it, and a
    /// notice arriving meanwhile goes above it. Once the pointer leaves, the stack closes up.
    /// </summary>
    [Test]
    public async Task AHoveredToastKeepsItsPlaceWhileTheToastsUnderItFade()
    {
        var (before, during, frozen, shown, arrivedAbove, after, closedUp) = await Run((window, view, layer, driver) =>
        {
            var notices = (IPointerPresenter)driver.Pointer;
            notices.Notice(Overlay(1, "first", null, _ => { }));
            notices.Notice(Overlay(2, "second", null, _ => { }));
            Drain();
            window.UpdateLayout();
            var toasts = layer.Toasts;
            var before = toasts.Rects()[0];
            window.MouseMove(toasts.TranslatePoint(before.Center, window)!.Value);

            // The newer toast's time and fade run out while the pointer holds the older one.
            toasts.Advance(NoticeModel.Duration + NoticeToasts.FadeTime + TimeSpan.FromSeconds(1));
            var shown = toasts.Shown.Select(n => n.Sequence).ToList();
            var during = toasts.Rects()[0];
            var frozen = toasts.Frozen;

            notices.Notice(Overlay(3, "third", null, _ => { }));
            Drain();
            var rects = toasts.Rects();
            var arrivedAbove = rects.Count == 2 && rects[0] == during && rects[1].Bottom < during.Top;

            // The pointer leaves: the stack closes up again.
            window.MouseMove(toasts.TranslatePoint(new Point(2, 2), window)!.Value);
            var after = toasts.Rects();
            return (before, during, frozen, shown, arrivedAbove, after, !toasts.Frozen);
        }, columns: 80, rows: 16);

        await Assert.That(shown).IsEquivalentTo(new long[] { 1 }).Because("the toast under the hovered one has gone");
        await Assert.That(frozen).IsTrue();
        await Assert.That(during).IsEqualTo(before).Because("the hovered toast stays under the pointer");
        await Assert.That(arrivedAbove).IsTrue().Because("a notice arriving while the stack is held goes above it");
        await Assert.That(closedUp).IsTrue();
        await Assert.That(after[^1].Bottom).IsGreaterThan(before.Bottom).Because("off the stack, it closes up to the bottom");
    }

    /// <summary>
    /// A native quick bar across the bottom of the pane area: a toast is lifted above its keys, as above a legend. And
    /// while something modal is in front the toasts are hidden rather than left under it.
    /// </summary>
    [Test]
    public async Task AToastStaysOffTheQuickBarAndHidesWhileAModalIsInFront()
    {
        var (bar, rects, hiddenUnderModal, backAfter) = await Run((window, view, layer, driver) =>
        {
            var model = new QuickBarModel([new QuickBarItem("F1", "F1", "look", Fires: true)]);
            var notices = (IPointerPresenter)driver.Pointer;
            notices.Panels(
                [new PanelOverlay(PanelIds.QuickBar, "panel:quick-bar:demo", new PaneRect(0, 16, 80, 2), new PanelInk("#33ccff", "#ffaa33", "#555555"), null, QuickBar: model, Pick: _ => { })]);
            notices.Notice(Overlay(1, "Using the Combat layout: the game sends vitals.", null, _ => { }, aboveRow: 18));
            Drain();
            window.UpdateLayout();
            driver.Flush();
            Drain();
            var buttons = layer.QuickBars.Values.Single();
            var topLeft = buttons.TranslatePoint(new Point(0, 0), layer.Toasts)!.Value;
            var bar = new Rect(topLeft, buttons.Bounds.Size);
            var rects = layer.Toasts.Rects();

            var modal = true;
            layer.ToastsHidden = () => modal;
            driver.Flush();
            Drain();
            var hidden = !layer.Toasts.IsVisible;
            modal = false;
            driver.Flush();
            Drain();
            return (bar, rects, hidden, layer.Toasts.IsVisible);
        }, columns: 80, rows: 20);

        await Assert.That(rects).IsNotEmpty();
        await Assert.That(rects.Any(r => r.Intersects(bar))).IsFalse().Because("no toast covers a quick bar key");
        await Assert.That(hiddenUnderModal).IsTrue().Because("a toast is never left under a settings window, ⌃P or a modal");
        await Assert.That(backAfter).IsTrue();
    }

    /// <summary>
    /// In the combat layout the map sits right of the output, down to the command line, and its legend is in its bottom-left
    /// corner — under the toasts' column. A toast is stacked above the legend rather than over it.
    /// </summary>
    [Test]
    public async Task AToastIsStackedClearOfTheMapsLegend()
    {
        var (legend, rects) = await Run((window, view, layer, driver) =>
        {
            var model = new MapViewModel(
                "Town",
                0,
                [
                    new MapViewRoom(1, "Plaza", 0, 0, Current: true, Ahead: false, Locked: false),
                    new MapViewRoom(2, "North Road", 0, -1, Current: false, Ahead: false, Locked: false),
                ],
                [new MapViewExit(1, 2)],
                null);
            var notices = (IPointerPresenter)driver.Pointer;
            notices.Panels(
                [new PanelOverlay(PanelIds.Map, "map:demo", new PaneRect(44, 1, 36, 17), new PanelInk("#33ccff", "#ffaa33", "#555555"), null, model, _ => { })]);
            notices.Notice(Overlay(1, "Using the Combat layout: the game sends vitals.", new NoticeAction("layout:layouts", "Change layout"), _ => { }, aboveRow: 18));
            Drain();
            window.UpdateLayout();
            driver.Flush();
            Drain();
            var canvas = layer.Maps.Values.Single();
            var plate = canvas.LegendRect(canvas.Bounds.Size)!.Value;
            var topLeft = canvas.TranslatePoint(plate.TopLeft, layer.Toasts)!.Value;
            return (new Rect(topLeft, plate.Size), layer.Toasts.Rects());
        }, columns: 80, rows: 20);

        await Assert.That(rects).IsNotEmpty();
        await Assert.That(rects.Any(r => r.Intersects(legend))).IsFalse().Because("no toast covers the map's legend");
        await Assert.That(rects[^1].Bottom).IsLessThanOrEqualTo(legend.Top).Because("the stack sits above the legend");
    }

    [Test]
    public async Task TheNativeMapDrawsItsAreasInTheModelsColours()
    {
        var (room, elsewhere, legend) = await Run((window, view, layer, driver) =>
        {
            var model = new MapViewModel(
                "Town",
                0,
                [
                    new MapViewRoom(1, "Plaza", 0, 0, Current: true, Ahead: false, Locked: false, Up: true),
                    new MapViewRoom(2, "Gate", 1, 0, Current: false, Ahead: false, Locked: false, Elsewhere: "Docks"),
                ],
                [new MapViewExit(1, 2)],
                null)
            {
                Areas = [new MapViewArea("Town", "#abcdef", Drawn: true), new MapViewArea("Docks", "#fedcba", Drawn: false)],
            };
            ((IPointerPresenter)driver.Pointer).Panels(
                [new PanelOverlay(PanelIds.Map, "map:demo", new PaneRect(0, 1, 40, 9), new PanelInk("#33ccff", "#ffaa33", "#555555"), null, model, _ => { })]);
            Drain();
            window.UpdateLayout();
            var canvas = layer.Maps.Values.Single();
            return (canvas.AreaColourOf(model.Rooms[1]), canvas.ElsewhereColourOf(model.Rooms[1]), canvas.LegendEntries());
        });

        await Assert.That(room.ToString()).IsEqualTo("#ffabcdef");
        await Assert.That(elsewhere?.ToString()).IsEqualTo("#fffedcba");
        await Assert.That(legend.Select(e => e.Label)).IsEquivalentTo(new[] { "you", "stairs up / down", "Town", "Docks" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(legend[2].Swatch.ToString()).IsEqualTo("#ffabcdef");
    }
}
