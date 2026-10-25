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

        public void Scrollbars(IReadOnlyList<PaneScrollbar> bars) => Bars = bars;
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
