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
