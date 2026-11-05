using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// What the pointer finds, through the real app on the demo scene: the client says what each cell is, lights
/// and moves a divider, builds the right-click menu, and reports the panes' scroll positions to the host.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class PointerTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class RecordingPresenter : IPointerPresenter
    {
        public List<PointerTarget> Hovers { get; } = [];
        public List<IReadOnlyList<PointerMenuItem>> Menus { get; } = [];
        public IReadOnlyList<PaneScrollbar> Bars { get; private set; } = [];

        public void Hover(PointerTarget target) => Hovers.Add(target);

        public void ShowMenu(Point cell, IReadOnlyList<PointerMenuItem> items) => Menus.Add(items);

        public bool DrawsMenus => true;

        public void Scrollbars(IReadOnlyList<PaneScrollbar> bars) => Bars = bars;

        public IReadOnlyList<PaneDividerOverlay> Dividers { get; private set; } = [];

        void IPointerPresenter.Dividers(IReadOnlyList<PaneDividerOverlay> dividers) => Dividers = dividers;
    }

    private static (SharpMUTermApp App, RecordingPresenter Pointer) App(string view, int width = 120, int height = 34)
    {
        Console.SetIn(TextReader.Null);
        var pointer = new RecordingPresenter();
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(width, height), null, null, _ => { }) with { Pointer = pointer };
        var app = new SharpMUTermApp(DemoScene.Build(), host);
        app.RenderSnapshot(view);
        app.RenderNextFrame();
        return (app, pointer);
    }

    /// <summary>Every cell of the screen with what the pointer finds there.</summary>
    private static IEnumerable<(Point Cell, PointerTarget Target)> Scan(SharpMUTermApp app, int width = 120, int height = 34)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var cell = new Point(x, y);
                yield return (cell, app.TargetAt(cell));
            }
        }
    }

    private static Point DividerCell(SharpMUTermApp app) =>
        Scan(app).First(hit => hit.Target.Kind == PointerKind.Divider).Cell;

    [Test]
    public async Task TheHitTestsTheHoverNeedsAreStillInTheFramework()
    {
        await Assert.That(FrameworkHitTests.Available).IsTrue();
    }

    [Test]
    public async Task TheLineBetweenTwoPanesIsADividerThatResizesColumns()
    {
        var (app, _) = App("split");
        var rects = app.PaneOutputRects().Values.OrderBy(r => r.X).ToArray();
        var divider = Scan(app).Where(hit => hit.Target.Kind == PointerKind.Divider).ToArray();

        await Assert.That(rects.Length).IsEqualTo(2);
        await Assert.That(divider).IsNotEmpty();
        await Assert.That(divider.All(hit => hit.Target.Shape == PointerShape.ResizeColumns)).IsTrue();
        await Assert.That(divider.All(hit => hit.Cell.X >= rects[0].X + rects[0].Width && hit.Cell.X < rects[1].X)).IsTrue();
        await Assert.That(divider[0].Target.Tooltip).Contains("Drag to resize");
    }

    /// <summary>
    /// The host is told where the divider cells are, so the window can draw them as the panes meeting at a hairline: the
    /// column between the panes, the whole height, lit while hovered; and nothing while a modal is drawn over the panes.
    /// </summary>
    [Test]
    public async Task TheHostIsToldWhereTheDividersAreAndWhichIsLit()
    {
        var (app, pointer) = App("split");
        var rects = app.PaneSnapshot().Rects.Values.OrderBy(r => r.X).ToArray();
        var said = pointer.Dividers.Single();

        await Assert.That(said.Vertical).IsTrue();
        await Assert.That(said.Lit).IsFalse();
        await Assert.That(said.Area.X).IsEqualTo(rects[0].X + rects[0].Width);
        await Assert.That(said.Area.Width).IsEqualTo(1);
        await Assert.That(said.Area.Height).IsEqualTo(rects[0].Height);
        await Assert.That(app.DividersNow()).IsEquivalentTo(pointer.Dividers);

        app.SimulatePointer(DividerCell(app), MouseFlags.ReportMousePosition);
        app.RenderNextFrame();
        await Assert.That(pointer.Dividers.Single().Lit).IsTrue();

        app.SimulateKey(new ConsoleKeyInfo('\u0010', ConsoleKey.P, shift: false, alt: false, control: true));
        app.RenderNextFrame();
        await Assert.That(app.MenuIsOpen).IsTrue();
        await Assert.That(app.DividersNow()).IsEmpty().Because("⌃P is drawn over the panes, and the host must not draw over it");
    }

    /// <summary>A stacked split whose lower pane draws its strip has no divider row, and so tells the host of none.</summary>
    [Test]
    public async Task AStripStandingInForADividerIsNotADividerToDraw()
    {
        var (app, pointer) = App("split-stacked");

        await Assert.That(pointer.Dividers).IsEmpty();
    }

    [Test]
    public async Task HoveringADividerLightsItAndMovesNoPane()
    {
        var (app, pointer) = App("split");
        var before = app.PaneOutputRects();
        var cell = DividerCell(app);

        app.SimulatePointer(cell, MouseFlags.ReportMousePosition);
        app.RenderNextFrame();

        await Assert.That(app.DividerLit).IsTrue();
        await Assert.That(pointer.Hovers.Last().Kind).IsEqualTo(PointerKind.Divider);
        await Assert.That(app.PaneOutputRects()).IsEquivalentTo(before);

        app.SimulatePointer(cell with { X = 5 }, MouseFlags.ReportMousePosition);
        await Assert.That(app.DividerLit).IsFalse();
    }

    [Test]
    public async Task ALitDividerIsPaintedInADifferentColourAndNothingElseIs()
    {
        var (app, _) = App("split");
        var cell = DividerCell(app);
        var resting = FrameGrid.Backgrounds(app.RenderWholeFrame());

        app.SimulatePointer(cell, MouseFlags.ReportMousePosition);
        var lit = FrameGrid.Backgrounds(app.RenderWholeFrame());

        var changed = lit.Keys.Where(k => lit[k] != resting.GetValueOrDefault(k)).ToArray();
        await Assert.That(changed).IsNotEmpty();
        await Assert.That(changed.All(k => k.Column == cell.X)).IsTrue().Because("only the divider's own column is recoloured");
    }

    [Test]
    public async Task DraggingADividerMovesTheBorderToThePointer()
    {
        var (app, _) = App("split");
        var cell = DividerCell(app);
        var left = app.PaneOutputRects().Values.OrderBy(r => r.X).First();

        app.SimulatePointer(cell, MouseFlags.Button1Pressed);
        app.SimulatePointer(cell with { X = cell.X + 6 }, MouseFlags.Button1Pressed, MouseFlags.Button1Dragged, MouseFlags.ReportMousePosition);
        app.SimulatePointer(cell with { X = cell.X + 6 }, MouseFlags.Button1Released);
        app.RenderNextFrame();

        var moved = app.PaneOutputRects().Values.OrderBy(r => r.X).First();
        await Assert.That(moved.Width).IsEqualTo(left.Width + 6);
        await Assert.That(app.DividerLit).IsTrue().Because("the pointer is still over the divider it let go of");
    }

    [Test]
    public async Task DoubleClickingADividerEvensThePanesOut()
    {
        var (app, _) = App("split");
        var cell = DividerCell(app);
        app.SimulatePointer(cell, MouseFlags.Button1Pressed);
        app.SimulatePointer(cell with { X = cell.X + 10 }, MouseFlags.Button1Pressed, MouseFlags.Button1Dragged);
        app.SimulatePointer(cell with { X = cell.X + 10 }, MouseFlags.Button1Released);
        app.RenderNextFrame();

        app.SimulatePointer(DividerCell(app), MouseFlags.Button1DoubleClicked);
        app.RenderNextFrame();

        var widths = app.PaneOutputRects().Values.Select(r => r.Width).ToArray();
        await Assert.That(Math.Abs(widths[0] - widths[1])).IsLessThanOrEqualTo(1);
    }

    /// <summary>
    /// In a stack there is no divider row above a pane that draws its own tab strip: the strip's empty run past
    /// its last tab is the divider. It is found as one (rows shape, the divider's tooltip), the tab itself is
    /// still a tab, and no row of the screen between the two panes is spent on a rule.
    /// </summary>
    [Test]
    public async Task InAStackTheLowerPanesTabStripIsTheDivider()
    {
        var (app, _) = App("split-stacked");
        var rects = app.PaneSnapshot().Rects.Values.OrderBy(r => r.Y).ToArray();
        await Assert.That(rects.Length).IsEqualTo(2);
        await Assert.That(rects[1].Y).IsEqualTo(rects[0].Y + rects[0].Height).Because("no divider row between the panes");

        var header = Scan(app).Where(hit => hit.Cell.Y == rects[1].Y).ToArray();
        var divider = header.Where(hit => hit.Target.Kind == PointerKind.Divider).ToArray();
        var tab = header.Where(hit => hit.Target.Kind == PointerKind.Tab).ToArray();
        await Assert.That(divider).IsNotEmpty();
        await Assert.That(tab).IsNotEmpty().Because("the tab is still the framework's");
        await Assert.That(divider.All(hit => hit.Target.Shape == PointerShape.ResizeRows)).IsTrue();
        await Assert.That(divider[0].Target.Tooltip).Contains("above and below");
        await Assert.That(divider.Min(hit => hit.Cell.X)).IsGreaterThan(tab.Max(hit => hit.Cell.X))
            .Because("only the run past the last tab is the divider");
        await Assert.That(divider.Max(hit => hit.Cell.X)).IsEqualTo(rects[1].X + rects[1].Width - 1);
    }

    [Test]
    public async Task HoveringTheTabStripDividerLightsOnlyItsEmptyRun()
    {
        var (app, pointer) = App("split-stacked");
        var before = app.PaneOutputRects();
        var cell = DividerCell(app);
        var resting = FrameGrid.Backgrounds(app.RenderWholeFrame());

        app.SimulatePointer(cell, MouseFlags.ReportMousePosition);
        var lit = FrameGrid.Backgrounds(app.RenderWholeFrame());

        await Assert.That(app.DividerLit).IsTrue();
        await Assert.That(pointer.Hovers.Last().Kind).IsEqualTo(PointerKind.Divider);
        var changed = lit.Keys.Where(k => lit[k] != resting.GetValueOrDefault(k)).ToArray();
        await Assert.That(changed).IsNotEmpty();
        await Assert.That(changed.All(k => k.Row == cell.Y && k.Column >= pointer.Hovers.Last().Area.X)).IsTrue()
            .Because("only the strip's empty run is recoloured, never a tab");
        await Assert.That(app.PaneOutputRects()).IsEquivalentTo(before);

        app.SimulatePointer(cell with { Y = cell.Y + 3 }, MouseFlags.ReportMousePosition);
        await Assert.That(app.DividerLit).IsFalse();
        var unlit = FrameGrid.Backgrounds(app.RenderWholeFrame());
        await Assert.That(unlit.Keys.All(k => unlit[k] == resting.GetValueOrDefault(k))).IsTrue();
    }

    [Test]
    public async Task DraggingTheTabStripDividerResizesTheStackAndStartsNoPaneDrag()
    {
        var (app, _) = App("split-stacked");
        var cell = DividerCell(app);
        var top = app.PaneSnapshot().Rects.Values.OrderBy(r => r.Y).First();

        await Assert.That(app.PaneSnapshot().IsDragHandle(app.PaneSnapshot().PaneAt(cell.X, cell.Y)!, cell.X, cell.Y)).IsFalse()
            .Because("a press on the divider run is the divider's, not a window drag");

        app.SimulatePointer(cell, MouseFlags.Button1Pressed);
        app.SimulatePointer(cell with { Y = cell.Y - 4 }, MouseFlags.Button1Pressed, MouseFlags.Button1Dragged, MouseFlags.ReportMousePosition);
        app.SimulatePointer(cell with { Y = cell.Y - 4 }, MouseFlags.Button1Released);
        app.RenderNextFrame();

        var rects = app.PaneSnapshot().Rects.Values.OrderBy(r => r.Y).ToArray();
        await Assert.That(rects[0].Height).IsEqualTo(top.Height - 4);
        await Assert.That(rects[1].Y).IsEqualTo(cell.Y - 4).Because("the lower pane's tab strip follows the pointer");

        app.SimulatePointer(DividerCell(app), MouseFlags.Button1DoubleClicked);
        app.RenderNextFrame();
        var heights = app.PaneSnapshot().Rects.Values.Select(r => r.Height).ToArray();
        await Assert.That(Math.Abs(heights[0] - heights[1])).IsLessThanOrEqualTo(1);
    }

    [Test]
    public async Task AURLInOutputIsALinkWhoseTooltipNamesIt()
    {
        var (app, _) = App("links");
        var link = Scan(app).FirstOrDefault(hit => hit.Target.Kind == PointerKind.Link && hit.Target.Key.Contains("https://"));

        await Assert.That(link.Target).IsNotNull();
        await Assert.That(link.Target.Shape).IsEqualTo(PointerShape.Hand);
        await Assert.That(link.Target.Tooltip).Contains("https://");

        var menu = app.PointerMenu(link.Cell).Select(item => item.Label).ToArray();
        await Assert.That(menu).Contains("Open in browser");
        await Assert.That(menu).Contains("Copy link address");
    }

    [Test]
    public async Task TabsRailRowsAndTheCommandLineAreFound()
    {
        var (app, _) = App("tabs");
        var kinds = Scan(app).Select(hit => hit.Target).ToArray();

        var tabs = kinds.Where(t => t.Kind == PointerKind.Tab).Select(t => t.Area).Distinct().ToArray();
        var tab = kinds.First(t => t.Kind == PointerKind.Tab);
        await Assert.That(tab.Shape).IsEqualTo(PointerShape.Hand);
        await Assert.That(tabs.Length).IsGreaterThanOrEqualTo(2).Because("each tab covers its own columns, not the strip");
        await Assert.That(tabs.All(a => a.Height == 1 && a.Width < 40)).IsTrue();
        await Assert.That(tab.Tooltip).IsNotNull();

        var rail = kinds.First(t => t.Kind == PointerKind.RailRow);
        await Assert.That(rail.Shape).IsEqualTo(PointerShape.Hand);
        await Assert.That(rail.Tooltip).IsNotNull();

        await Assert.That(kinds.Any(t => t.Kind == PointerKind.CommandLine && t.Shape == PointerShape.Text)).IsTrue();
        await Assert.That(kinds.Any(t => t.Kind == PointerKind.Output && t.Shape == PointerShape.Text)).IsTrue();
    }

    /// <summary>
    /// A tinted character's rail row and tab say the pane colour <em>by name</em> — the cue that survives a
    /// reader who cannot tell the ten hues apart, in the one place it costs no cell. An untinted character's
    /// rows say nothing about colour.
    /// </summary>
    [Test]
    public async Task ATintedCharactersRowsNameThePaneColour()
    {
        var (app, _) = App("tint-tabs");
        var targets = Scan(app).Select(hit => hit.Target).Where(t => t.Tooltip is not null).ToArray();

        await Assert.That(targets.Any(t => t.Kind == PointerKind.RailRow && t.Tooltip!.Contains("pane colour: "))).IsTrue();
        await Assert.That(targets.Any(t => t.Kind == PointerKind.Tab && t.Tooltip!.Contains("pane colour: "))).IsTrue();
    }

    [Test]
    public async Task ARightClickOnADividerOrAPaneAsksTheHostForAMenu()
    {
        var (app, pointer) = App("split");
        app.SimulatePointer(DividerCell(app), MouseFlags.Button3Pressed);

        var output = app.PaneOutputRects().Values.First();
        app.SimulatePointer(new Point(output.X + 2, output.Y + 2), MouseFlags.Button3Pressed);

        await Assert.That(pointer.Menus.Count).IsEqualTo(2);
        await Assert.That(pointer.Menus[0].Select(i => i.Label)).Contains("Even out these panes");
        await Assert.That(pointer.Menus[1].Any(i => !i.IsSeparator && i.Hint is not null)).IsTrue()
            .Because("an entry that has a key says which");
        await Assert.That(pointer.Menus[1][0].IsSeparator).IsFalse();
        await Assert.That(pointer.Menus[1][^1].IsSeparator).IsFalse();
    }

    [Test]
    public async Task APaneWithMoreOutputThanItShowsReportsAScrollbarThatScrollsIt()
    {
        var (app, pointer) = App("scrollback");
        var bar = pointer.Bars.Single(b => b.ContentRows > b.ViewportRows);

        bar.ScrollTo(0);
        app.RenderNextFrame();
        app.RenderNextFrame();

        var top = pointer.Bars.Single(b => b.WindowId == bar.WindowId);
        await Assert.That(top.Offset).IsEqualTo(0);
        await Assert.That(bar.Offset).IsGreaterThan(0);
    }
}
