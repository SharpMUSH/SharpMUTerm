using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A host that puts the command line in the output's column (<see cref="HostChrome.InputInColumn"/>, the window) gets
/// the bars as wide as the output above them, the side column running down to the status row, and NAWS following the
/// pane rectangles that are really arranged; moving the selection between panes still moves none of them.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class InputColumnLayoutTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly HostChrome InColumn = HostChrome.None with { Input = true, InputInColumn = true };

    /// <summary>The window's arrangement: the rail drawn by the host, so the panes have all 120 columns.</summary>
    private static readonly HostChrome InColumnNoRail = InColumn with { Rail = true };

    private static SharpMUTermApp App(HostChrome chrome, string view) => App(chrome, view, out _);

    private static SharpMUTermApp App(HostChrome chrome, string view, out HeadlessConsoleDriver driver)
    {
        Console.SetIn(TextReader.Null);
        driver = new HeadlessConsoleDriver(Width, Height);
        var host = ClientHost.Headless(driver, Caps) with { Chrome = chrome };
        var app = new SharpMUTermApp(DemoScene.Build(), host);
        app.RenderSnapshot(view);
        app.RenderNextFrame();
        return app;
    }

    private static ConsoleKeyInfo Ctrl(ConsoleKey key) => new('\0', key, false, false, true);

    [Test]
    [Arguments("layout-roleplay")]
    [Arguments("layout-combat")]
    public async Task TheBarsAreAsWideAsTheOutputColumnAndTheSideColumnRunsToTheStatusRow(string view)
    {
        await using var app = App(InColumn, view);
        var band = app.InputBandNow();
        await Assert.That(band).IsNotNull();
        await Assert.That(band!.Left).IsNotNull();
        await Assert.That(band.Columns!.Value).IsLessThan(Width - 20);

        var rects = app.PaneOutputRects();
        var bottom = Height - app.LaidOutRows.Status;

        // The output pane sits above the bars, inside their columns.
        var output = rects.Values.Where(r => r.X >= band.From && r.X < band.To(Width)).ToList();
        await Assert.That(output).IsNotEmpty();
        await Assert.That(output.All(r => r.Y + r.Height <= band.Top)).IsTrue();

        // Every pane beside the bars reaches further down than they start: the side column runs past them.
        var beside = rects.Values.Where(r => r.X >= band.To(Width)).ToList();
        await Assert.That(beside).IsNotEmpty();
        await Assert.That(beside.Max(r => r.Y + r.Height)).IsGreaterThan(band.Top);
        await Assert.That(band.Top + band.Rows).IsEqualTo(bottom);

        // The caret is on the bar, inside its columns.
        var caret = app.CaretOnScreen();
        await Assert.That(caret.Visible).IsTrue();
        await Assert.That(caret.Y).IsEqualTo(band.Top);
        await Assert.That(caret.X).IsGreaterThanOrEqualTo(band.From).And.IsLessThan(band.To(Width));
    }

    [Test]
    public async Task MovingTheSelectionMovesNoPaneRectangle()
    {
        await using var app = App(InColumn, "layout-roleplay");
        var before = app.PaneOutputRects().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var band = app.InputBandNow();
        var first = app.FocusedPaneId;

        app.SimulateKey(Ctrl(ConsoleKey.RightArrow));
        app.RenderNextFrame();

        await Assert.That(app.FocusedPaneId).IsNotEqualTo(first);
        var after = app.PaneOutputRects();
        foreach (var (paneId, rect) in before)
        {
            await Assert.That(after[paneId]).IsEqualTo(rect);
        }

        await Assert.That(app.InputBandNow()?.Left).IsEqualTo(band?.Left);
    }

    [Test]
    public async Task TheTerminalKeepsTheBarsAcrossEveryPane()
    {
        await using var cells = App(HostChrome.None with { Input = true }, "layout-roleplay");
        var band = cells.InputBandNow();
        await Assert.That(band).IsNotNull();
        await Assert.That(band!.Left).IsNull();
        await Assert.That(band.Columns).IsNull();
    }

    /// <summary>
    /// The <c>tabs</c> view with the right pane's front tab dragged onto the left pane's left edge, through real mouse
    /// frames (wave 25's report): three panes side by side, the dropped one and the output halving the left half — about 29
    /// columns each at 120 — and Scenes keeping the right half.
    /// </summary>
    private static SharpMUTermApp ThreePanes(HostChrome chrome)
    {
        var app = App(chrome, "tabs", out var driver);
        var surface = app.PaneSnapshot();
        var source = surface.Rects.Single(r => surface.ActiveWindow(r.Key) != SharpMUTermApp.MainWindowId).Value;
        var target = surface.Rects.Single(r => surface.ActiveWindow(r.Key) == SharpMUTermApp.MainWindowId).Value;
        var at = new Point(target.X + 1, target.Y + (target.Height / 2));
        driver.SimulateMouseEvent([MouseFlags.Button1Pressed], new Point(source.X + 2, source.Y));
        driver.SimulateMouseEvent([MouseFlags.Button1Pressed, MouseFlags.Button1Dragged, MouseFlags.ReportMousePosition], at);
        driver.SimulateMouseEvent([MouseFlags.Button1Released], at);
        app.RenderNextFrame();
        app.RenderNextFrame();
        return app;
    }

    [Test]
    public async Task HalfTheWindowKeepsTheBarsInTheOutputsColumn()
    {
        await using var app = App(InColumnNoRail, "tabs");
        var band = app.InputBandNow();
        await Assert.That(band).IsNotNull();
        await Assert.That(band!.Columns!.Value).IsLessThan(Width / 2 + 1);
        await Assert.That(band.Columns.Value).IsGreaterThanOrEqualTo(InputColumn.MinimumColumns);
    }

    [Test]
    public async Task AColumnUnderTheFloorGivesTheBarsTheWidestColumnThatFits()
    {
        await using var app = ThreePanes(InColumnNoRail);
        await Assert.That(app.PaneIds.Count).IsEqualTo(3);

        // The output's own column is under the floor, so the bars take the left half it shares with the dropped pane —
        // the widest node on the way down that fits — rather than one 29-column field.
        var band = app.InputBandNow();
        await Assert.That(band).IsNotNull();
        await Assert.That(band!.From).IsEqualTo(0);
        await Assert.That(band.Columns!.Value).IsGreaterThanOrEqualTo(InputColumn.MinimumColumns);
        await Assert.That(band.Columns.Value).IsLessThan(Width);

        // NAWS follows what is arranged: the two panes over the bars end above them, Scenes beside them runs past.
        var rects = app.PaneOutputRects();
        var over = rects.Values.Where(r => r.X < band.To(Width)).ToList();
        var beside = rects.Values.Where(r => r.X >= band.To(Width)).ToList();
        await Assert.That(over.Count).IsEqualTo(2);
        await Assert.That(over.All(r => r.Width < InputColumn.MinimumColumns)).IsTrue();
        await Assert.That(over.All(r => r.Y + r.Height <= band.Top)).IsTrue();
        await Assert.That(beside.Count).IsEqualTo(1);
        await Assert.That(beside[0].Y + beside[0].Height).IsGreaterThan(band.Top);

        var caret = app.CaretOnScreen();
        await Assert.That(caret.Visible).IsTrue();
        await Assert.That(caret.Y).IsEqualTo(band.Top);
        await Assert.That(caret.X).IsLessThan(band.To(Width));
    }

    [Test]
    public async Task TheTerminalsFramesAreUnchangedByTheFloor()
    {
        // The terminal keeps its full-width band whatever the panes: the floor is only consulted in a column host.
        await using var cells = ThreePanes(HostChrome.None with { Input = true, Rail = true });
        var band = cells.InputBandNow();
        await Assert.That(band).IsNotNull();
        await Assert.That(band!.Left).IsNull();
        await Assert.That(band.Columns).IsNull();
    }
}
