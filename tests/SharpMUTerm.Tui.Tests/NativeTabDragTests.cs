using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A tab dragged off the window's native strip: the window hands back only the tab and the cell under the pointer (and
/// the slot among its own strip's tabs while it is over that strip), and the client resolves and makes the drop through
/// the cell drag's own pieces — the frozen geometry, <see cref="DropZones"/>, the drop preview and prompt,
/// <see cref="SharpMUTermApp.DropWindowOnPane"/> — or, along its own strip, the ⌃B &lt; / ⌃B &gt; move. What it would do is
/// published back for the window to draw. Nothing the strip did not offer is dragged.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class NativeTabDragTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class PanePresenter : IPointerPresenter
    {
        public List<PaneChromeOverlay> Published { get; } = [];

        public void Hover(PointerTarget target)
        {
        }

        public void ShowMenu(Point cell, IReadOnlyList<PointerMenuItem> items)
        {
        }

        public bool DrawsMenus => true;

        public void Scrollbars(IReadOnlyList<PaneScrollbar> bars)
        {
        }

        public void PaneChrome(PaneChromeOverlay chrome) => Published.Add(chrome);
    }

    private static SharpMUTermApp App(HostChrome chrome, IPointerPresenter? pointer, out HeadlessConsoleDriver driver)
    {
        Console.SetIn(TextReader.Null);
        driver = new HeadlessConsoleDriver(Width, Height);
        var host = ClientHost.Headless(driver, Caps) with { Chrome = chrome, Pointer = pointer };
        var app = new SharpMUTermApp(DemoScene.Build(), host);
        app.RenderSnapshot("tabs");
        app.RenderNextFrame();
        return app;
    }

    /// <summary>The strip with two tabs (the pane that does not hold the focus, in the <c>tabs</c> view).</summary>
    private static TabStripOverlay TwoTabStrip(SharpMUTermApp app) =>
        app.PaneChromePublished!.Strips.First(s => s.Model.Tabs.Count > 1);

    private static TabDragStep Step(TabDragPhase phase, TabStripOverlay strip, string window, int x, int y, int? slot = null) =>
        new(phase, strip.Model.PaneId, window, x, y, slot);

    /// <summary>The middle of a pane's left quarter: a split to its left.</summary>
    private static Point LeftEdgeOf(PaneRect rect) => new(rect.X + 1, rect.Y + (rect.Height / 2));

    [Test]
    public async Task ATabDroppedOnAnotherPanesEdgeSplitsItExactlyAsTheCellDragDoes()
    {
        var pointer = new PanePresenter();
        await using var native = App(HostChrome.Native, pointer, out _);
        var strip = TwoTabStrip(native);
        var window = strip.Model.Tabs.Single(t => t.Selected).WindowId;
        var other = native.PaneSnapshot().Rects.Single(r => r.Key != strip.Model.PaneId);
        var at = LeftEdgeOf(other.Value);

        await Assert.That(native.DragStripTab(Step(TabDragPhase.Begin, strip, window, strip.Row.X + 2, strip.Row.Y, 0))).IsTrue();
        await Assert.That(native.DragStripTab(Step(TabDragPhase.Move, strip, window, at.X, at.Y))).IsTrue();

        // What the drop would do, as the cells say it, published for the window's hint.
        var drag = native.TabDragNow!;
        await Assert.That(drag.TargetPaneId).IsEqualTo(other.Key);
        await Assert.That(drag.Edge).IsEqualTo(Edge.Left);
        await Assert.That(drag.Zone).IsEqualTo(TabDragModel.ZoneOf(other.Value, Edge.Left));
        await Assert.That(drag.Label).Contains("left");
        await Assert.That(pointer.Published.Last().Drag).IsEqualTo(drag);

        await Assert.That(native.DragStripTab(Step(TabDragPhase.Drop, strip, window, at.X, at.Y))).IsTrue();
        await Assert.That(native.TabDragNow).IsNull();

        // The terminal's own drag of the same tab onto the same edge, through real mouse frames.
        await using var cells = App(HostChrome.None, null, out var driver);
        var surface = cells.PaneSnapshot();
        var source = surface.Rects.Single(r => surface.ActiveWindow(r.Key) == window).Value;
        var target = LeftEdgeOf(surface.Rects.Single(r => surface.ActiveWindow(r.Key) != window).Value);
        driver.SimulateMouseEvent([MouseFlags.Button1Pressed], new Point(source.X + 2, source.Y));
        driver.SimulateMouseEvent([MouseFlags.Button1Pressed, MouseFlags.Button1Dragged, MouseFlags.ReportMousePosition], target);
        driver.SimulateMouseEvent([MouseFlags.Button1Released], target);

        await Assert.That(native.WorkspaceDigest()).IsEqualTo(cells.WorkspaceDigest());
        await Assert.That(native.PaneIds.Count).IsEqualTo(3);
    }

    /// <summary>
    /// The window washes the zone and says the drop itself, so under a native drag the target pane's cells draw neither the
    /// zone band nor the words — they framed the zone twice. The status row keeps its prompt. The terminal's own mouse
    /// drag over the same edge still draws both.
    /// </summary>
    [Test]
    public async Task ANativeDragLeavesTheZoneToTheWindowAndTheTerminalsDragStillDrawsIt()
    {
        await using var native = App(HostChrome.Native, new PanePresenter(), out _);
        var strip = TwoTabStrip(native);
        var window = strip.Model.Tabs.Single(t => t.Selected).WindowId;
        var other = native.PaneSnapshot().Rects.Single(r => r.Key != strip.Model.PaneId);
        var at = LeftEdgeOf(other.Value);
        native.DragStripTab(Step(TabDragPhase.Begin, strip, window, strip.Row.X + 2, strip.Row.Y, 0));
        native.DragStripTab(Step(TabDragPhase.Move, strip, window, at.X, at.Y));
        var nativeFrame = native.RenderWholeFrame();

        await using var cells = App(HostChrome.None, null, out var driver);
        var surface = cells.PaneSnapshot();
        var source = surface.Rects.Single(r => surface.ActiveWindow(r.Key) == window).Value;
        var targetRect = surface.Rects.Single(r => surface.ActiveWindow(r.Key) != window).Value;
        var target = LeftEdgeOf(targetRect);
        driver.SimulateMouseEvent([MouseFlags.Button1Pressed], new Point(source.X + 2, source.Y));
        driver.SimulateMouseEvent([MouseFlags.Button1Pressed, MouseFlags.Button1Dragged, MouseFlags.ReportMousePosition], target);
        var cellFrame = cells.RenderWholeFrame();

        static (string Text, int Zone) Read(string frame, PaneRect rect, SharpMUTerm.Core.Text.Rgb? zoneColour)
        {
            var rows = FrameGrid.Decode(frame, Width, Height);
            var grid = FrameGrid.Cells(frame, Width, Height);
            var text = string.Join("\n", Enumerable.Range(rect.Y, rect.Height).Select(y => rows[y].Substring(rect.X, rect.Width)));
            var zone = 0;
            for (var y = rect.Y; y < rect.Y + rect.Height; y++)
            {
                if (zoneColour is not null && grid[(y, rect.X)].Background == zoneColour)
                {
                    zone++;
                }
            }

            return (text, zone);
        }

        // The terminal's zone colour: the left band's background, read off its paint.
        var terminalZone = FrameGrid.Cells(cellFrame, Width, Height)[(targetRect.Y + (targetRect.Height / 2), targetRect.X)].Background;
        var terminal = Read(cellFrame, targetRect, terminalZone);
        await Assert.That(terminal.Zone).IsGreaterThan(0).Because("the terminal's drag draws its zone band");
        await Assert.That(terminal.Text).Contains("left");

        var window2 = Read(nativeFrame, other.Value, terminalZone);
        await Assert.That(window2.Zone).IsEqualTo(0).Because("the window washes the zone; the cells draw none under it");
        await Assert.That(window2.Text).DoesNotContain("left");
        await Assert.That(native.BuildStatusModel().Text).Contains("DRAG").Because("the status row keeps the prompt");
    }

    [Test]
    public async Task ATabDroppedInAPanesMiddleJoinsItsTabs()
    {
        await using var app = App(HostChrome.Native, new PanePresenter(), out _);
        var strip = TwoTabStrip(app);
        var window = strip.Model.Tabs.First(t => !t.Selected).WindowId;
        var other = app.PaneSnapshot().Rects.Single(r => r.Key != strip.Model.PaneId);
        var middle = new Point(other.Value.X + (other.Value.Width / 2), other.Value.Y + (other.Value.Height / 2));

        app.DragStripTab(Step(TabDragPhase.Begin, strip, window, strip.Row.X + 2, strip.Row.Y, 0));
        app.DragStripTab(Step(TabDragPhase.Move, strip, window, middle.X, middle.Y));
        await Assert.That(app.TabDragNow!.Edge).IsNull();
        await Assert.That(app.DragStripTab(Step(TabDragPhase.Drop, strip, window, middle.X, middle.Y))).IsTrue();

        await Assert.That(app.PaneWindows()[other.Key]).IsEqualTo(window).Because("it came forward in the pane it was dropped on");
        await Assert.That(app.ActiveWindowId()).IsEqualTo(window);
    }

    [Test]
    public async Task ATabDraggedAlongItsOwnStripTakesTheSlotAsCtrlBAngleBracketsWould()
    {
        await using var app = App(HostChrome.Native, new PanePresenter(), out _);
        var strip = TwoTabStrip(app);
        var first = strip.Model.Tabs[0].WindowId;
        var second = strip.Model.Tabs[1].WindowId;

        app.DragStripTab(Step(TabDragPhase.Begin, strip, first, strip.Row.X + 2, strip.Row.Y, 0));
        app.DragStripTab(Step(TabDragPhase.Move, strip, first, strip.Row.X + 20, strip.Row.Y, 1));
        var drag = app.TabDragNow!;
        await Assert.That(drag.ReorderIndex).IsEqualTo(1);
        await Assert.That(drag.Zone).IsNull();
        await Assert.That(app.DragStripTab(Step(TabDragPhase.Drop, strip, first, strip.Row.X + 20, strip.Row.Y, 1))).IsTrue();

        var pane = app.PaneChromePublished!.Strips.Single(s => s.Model.PaneId == strip.Model.PaneId);
        await Assert.That(string.Join(",", pane.Model.Tabs.Select(t => t.WindowId))).IsEqualTo($"{second},{first}");
        await Assert.That(app.ActiveWindowId()).IsEqualTo(first);
    }

    [Test]
    public async Task ASlotIsUsedOnlyOnTheStripsOwnRowAndOnlyWhenItExists()
    {
        await using var app = App(HostChrome.Native, new PanePresenter(), out _);
        var strip = TwoTabStrip(app);
        var window = strip.Model.Tabs[0].WindowId;
        var body = new Point(strip.Row.X + 4, strip.Row.Y + 6);

        app.DragStripTab(Step(TabDragPhase.Begin, strip, window, strip.Row.X + 2, strip.Row.Y, 0));
        app.DragStripTab(Step(TabDragPhase.Move, strip, window, body.X, body.Y, 1));
        await Assert.That(app.TabDragNow!.ReorderIndex).IsNull().Because("a slot off the strip's row is the window's mistake");

        app.DragStripTab(Step(TabDragPhase.Move, strip, window, strip.Row.X + 2, strip.Row.Y, 9));
        await Assert.That(app.TabDragNow!.ReorderIndex).IsNull().Because("the strip holds two tabs");

        app.DragStripTab(Step(TabDragPhase.Cancel, strip, window, 0, 0));
        await Assert.That(app.TabDragNow).IsNull();
    }

    [Test]
    public async Task ATabTheStripNeverOfferedIsNotDraggedAndEscAbandonsADrag()
    {
        await using var app = App(HostChrome.Native, new PanePresenter(), out _);
        var strip = TwoTabStrip(app);
        var before = app.WorkspaceDigest();

        await Assert.That(app.DragStripTab(Step(TabDragPhase.Begin, strip, "spawn:not-a-window", strip.Row.X, strip.Row.Y))).IsFalse();
        await Assert.That(app.TabDragNow).IsNull();
        await Assert.That(app.LastNotice).Contains("never offered");

        var window = strip.Model.Tabs[0].WindowId;
        var other = app.PaneSnapshot().Rects.Single(r => r.Key != strip.Model.PaneId);
        var at = LeftEdgeOf(other.Value);
        app.DragStripTab(Step(TabDragPhase.Begin, strip, window, strip.Row.X + 2, strip.Row.Y, 0));
        app.DragStripTab(Step(TabDragPhase.Move, strip, window, at.X, at.Y));
        app.SimulateKey(new ConsoleKeyInfo('\x1b', ConsoleKey.Escape, false, false, false));

        await Assert.That(app.TabDragNow).IsNull();
        await Assert.That(app.DragStripTab(Step(TabDragPhase.Drop, strip, window, at.X, at.Y))).IsFalse().Because("the drag was abandoned");
        await Assert.That(app.WorkspaceDigest()).IsEqualTo(before);
    }

    [Test]
    public async Task OnlyAHostThatDrawsTheStripsIsHandedTheDrag()
    {
        await using var app = App(HostChrome.Native, new PanePresenter(), out _);
        await Assert.That(app.PaneChromePublished!.Strips.All(s => s.Drag is not null)).IsTrue();
    }
}
