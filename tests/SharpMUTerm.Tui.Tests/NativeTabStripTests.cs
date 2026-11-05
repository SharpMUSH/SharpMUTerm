using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A host that draws the panes' tab strips (<see cref="HostChrome.Tabs"/>) is handed each strip as the model the cells'
/// titles are written from, the client paints each strip's row blank under it, and nothing moves: the pane rectangles,
/// and so what every world is told over NAWS, are the terminal's. Every gesture on a native strip, and on the native
/// scrolled-back band, ends in the method the cell gesture ends in, and only if the client offered it.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class NativeTabStripTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class PanePresenter : IPointerPresenter
    {
        public List<PaneChromeOverlay> Published { get; } = [];

        public List<IReadOnlyList<PointerMenuItem>> Menus { get; } = [];

        public void Hover(PointerTarget target)
        {
        }

        public void ShowMenu(Point cell, IReadOnlyList<PointerMenuItem> items) => Menus.Add(items);

        public bool DrawsMenus => true;

        public void Scrollbars(IReadOnlyList<PaneScrollbar> bars)
        {
        }

        public void PaneChrome(PaneChromeOverlay chrome) => Published.Add(chrome);
    }

    private static SharpMUTermApp App(HostChrome chrome, out string frame, string? view, IPointerPresenter? pointer = null)
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.Headless(new HeadlessConsoleDriver(Width, Height), Caps) with { Chrome = chrome, Pointer = pointer };
        var app = new SharpMUTermApp(DemoScene.Build(), host);
        frame = app.RenderSnapshot(view);
        app.RenderNextFrame();
        return app;
    }

    [Test]
    public async Task TheStripTakesTwoRowsAndTheGutterTwoColumnsEachSideAndARowAndThePanesSayso()
    {
        // The same arrangement with the strips and the gutter left to the cells: a native strip is two rows where a cell
        // strip is one, and the gutter is two columns either side and a row above output, and the output rectangles
        // (NAWS) lose exactly those.
        await using var cells = App(HostChrome.Native with { Tabs = false, PaneGutter = false }, out _, "split");
        await using var native = App(HostChrome.Native, out _, "split");

        var expected = cells.PaneOutputRects().ToDictionary(
            p => p.Key, p => new PaneRect(p.Value.X + 2, p.Value.Y + 2, p.Value.Width - 4, p.Value.Height - 2));
        await Assert.That(native.PaneOutputRects()).IsEquivalentTo(expected);
        await Assert.That(native.PaneChromePublished?.Strips.All(s => s.Row.Height == 2) ?? true).IsTrue();
    }

    [Test]
    public async Task EachStripRowIsBlankWhereTheHostDrawsItAndTheModelIsWhatTheCellsWrote()
    {
        var pointer = new PanePresenter();
        await using var cellApp = App(HostChrome.HeaderAndRail, out var cellFrame, "tabs");
        await using var app = App(HostChrome.Native, out var frame, "tabs", pointer);

        var chrome = pointer.Published.Last();
        await Assert.That(chrome.Strips.Count).IsGreaterThanOrEqualTo(2).Because("one strip per pane of the split");
        var rows = FrameGrid.Decode(frame, Width, Height);
        var cellRows = FrameGrid.Decode(cellFrame, Width, Height);
        foreach (var strip in chrome.Strips)
        {
            await Assert.That((object)strip.Model).IsEqualTo(app.DrawnModel(SurfaceCatalog.TabStrip, strip.Model.PaneId));
            var row = rows[strip.Row.Y].Substring(strip.Row.X, strip.Row.Width);
            await Assert.That(row.Trim()).IsEmpty().Because($"the strip of {strip.Model.PaneId} is the host's to draw");
            var first = strip.Model.Tabs[0].Name;
            await Assert.That(cellRows[strip.Row.Y]).Contains(first[..Math.Min(4, first.Length)]).Because("the terminal still writes it");
        }

        await Assert.That(chrome.Strips.Count(s => s.Model.Focused)).IsEqualTo(1);
    }

    [Test]
    public async Task AHostThatDrawsNoStripsIsHandedNone()
    {
        var pointer = new PanePresenter();
        await using var app = App(HostChrome.HeaderAndRail, out _, "tabs", pointer);
        await Assert.That(pointer.Published.Last().Strips).IsEmpty();
    }

    [Test]
    public async Task ANativeTabClickBringsTheWindowForwardAndARefusedOneDoesNothing()
    {
        var pointer = new PanePresenter();
        await using var app = App(HostChrome.Native, out _, "tabs", pointer);
        var strip = pointer.Published.Last().Strips.First(s => s.Model.Tabs.Count > 1 && !s.Model.Focused);
        var other = strip.Model.Tabs.First(t => !t.Selected);

        await Assert.That(app.ActivateOfferedStripTab(strip.Model.PaneId, "no-such-window")).IsFalse();
        await Assert.That(app.ActivateOfferedStripTab(strip.Model.PaneId, other.WindowId)).IsTrue();
        await Assert.That(app.ActiveWindowId()).IsEqualTo(other.WindowId);
    }

    [Test]
    public async Task OnlyAClosableTabIsClosedAndARightClickShowsTheTabsMenu()
    {
        var pointer = new PanePresenter();
        await using var app = App(HostChrome.Native, out _, "tabs", pointer);
        var strip = pointer.Published.Last().Strips.First(s => s.Model.Focused);
        var main = strip.Model.Tabs.First(t => !t.Closable);

        await Assert.That(app.CloseOfferedStripTab(strip.Model.PaneId, main.WindowId)).IsFalse().Because("the main window has no close");
        await Assert.That(app.OpenTabMenu(strip.Model.PaneId, main.WindowId)).IsTrue();
        await Assert.That(pointer.Menus.Last().Any(i => i.Label == "Split right" || i.Label.StartsWith("Split", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task APressOnAStripRowNeverReachesTheFrameworkThere()
    {
        var pointer = new PanePresenter();
        await using var app = App(HostChrome.Native, out _, "tabs", pointer);
        var row = pointer.Published.Last().Strips[0].Row;
        var frame = new List<MouseFlags> { MouseFlags.Button1Pressed };
        await Assert.That(app.TakesNativeStripPress(frame, new Point(row.X + 1, row.Y))).IsTrue();
        await Assert.That(frame).IsEquivalentTo(new[] { MouseFlags.ReportMousePosition });

        var below = new List<MouseFlags> { MouseFlags.Button1Pressed };
        await Assert.That(app.TakesNativeStripPress(below, new Point(row.X + 1, row.Y + 2))).IsFalse();
    }

    [Test]
    public async Task TheBandIsPublishedAndItsButtonEndsWhereEndDoes()
    {
        var pointer = new PanePresenter();
        await using var app = App(HostChrome.Native, out _, "scrollback-split", pointer);
        var band = pointer.Published.Last().Bands.Single();
        await Assert.That((object)band.Model).IsEqualTo(app.DrawnModel(SurfaceCatalog.ScrollBand, band.Model.WindowId));
        await Assert.That(band.Model.RowsBack).IsGreaterThan(0);
        await Assert.That(app.IsScrollSplit(band.Model.WindowId)).IsTrue();

        await Assert.That(app.ReturnFromOfferedBand("no-such-window")).IsFalse();
        await Assert.That(app.ReturnFromOfferedBand(band.Model.WindowId)).IsTrue();
        await Assert.That(app.IsScrollSplit(band.Model.WindowId)).IsFalse();
    }

    [Test]
    public async Task TheWindowsBandIsTwoRowsAcrossTheGutterAndTheHistoryPlaneReachesThePanesEdges()
    {
        var pointer = new PanePresenter();
        await using var app = App(HostChrome.Native, out _, "scrollback-split", pointer);
        var frame = app.RenderWholeFrame();
        var band = pointer.Published.Last().Bands.Single();
        var output = app.PaneOutputRects().Values.Single(r => band.Row.Y >= r.Y && band.Row.Y < r.Y + r.Height);

        await Assert.That(band.Row.Height).IsEqualTo(2);
        await Assert.That(band.Inset).IsEqualTo(2);
        await Assert.That(band.Row.X).IsEqualTo(output.X - 2);
        await Assert.That(band.Row.Width).IsEqualTo(output.Width + 4);

        // The gutter beside the history wears the history's plane, so the history is one plane edge to edge.
        var cells = FrameGrid.Cells(frame, Width, Height);
        var row = band.Row.Y - 2;
        await Assert.That(cells[(row, output.X - 1)].Background).IsEqualTo(cells[(row, output.X + output.Width - 2)].Background);
        await Assert.That(cells[(row, output.X - 1)].Background).IsNotEqualTo(cells[(band.Row.Y + 3, output.X - 1)].Background);
    }

    [Test]
    public async Task TheBandIsHandedToAHostThatDrawsNoStripsToo()
    {
        var pointer = new PanePresenter();
        await using var app = App(HostChrome.None, out _, "scrollback-split", pointer);
        await Assert.That(pointer.Published.Last().Bands).Count().IsEqualTo(1);
        await Assert.That(pointer.Published.Last().Strips).IsEmpty();
    }

    private sealed class OpenComposer : INativeComposer
    {
        public bool Available => true;

        public ComposerSession? Session { get; private set; }

        public void Open(ComposerSession session, PanelInk ink) => Session = session;

        public void Close()
        {
        }
    }

    /// <summary>
    /// The window's F1 lies over the panes, and the native strips lie over the window's cells, so a strip left published
    /// is drawn over the composer. Opening F1 paints no frame (the editor covers the cells), so the strips are withdrawn
    /// when F1 opens and come back when it closes, without waiting for a frame that may never come.
    /// </summary>
    [Test]
    public async Task OpeningTheNativeComposerWithdrawsEveryStripWithoutWaitingForAFrame()
    {
        Console.SetIn(TextReader.Null);
        var pointer = new PanePresenter();
        var composer = new OpenComposer();
        var host = ClientHost.Headless(new HeadlessConsoleDriver(Width, Height), Caps) with
        {
            Chrome = HostChrome.Native,
            Pointer = pointer,
            NativeComposer = composer,
        };
        await using var app = new SharpMUTermApp(DemoScene.Build(), host);
        app.RenderSnapshot("split");
        app.RenderNextFrame();
        await Assert.That(pointer.Published.Last().Strips.Count).IsGreaterThanOrEqualTo(2);

        app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
        await Assert.That(composer.Session).IsNotNull().Because("F1 opened in the host's editor");
        await Assert.That(pointer.Published.Last().Strips).IsEmpty().Because("no strip may sit over the composer");

        composer.Session!.Close("draft");
        await Assert.That(pointer.Published.Last().Strips.Count).IsGreaterThanOrEqualTo(2).Because("closing F1 brings them back");
    }
}
