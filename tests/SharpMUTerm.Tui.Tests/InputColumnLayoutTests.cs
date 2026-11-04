using SharpConsoleUI.Drivers;
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

    private static SharpMUTermApp App(HostChrome chrome, string view)
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.Headless(new HeadlessConsoleDriver(Width, Height), Caps) with { Chrome = chrome };
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
}
