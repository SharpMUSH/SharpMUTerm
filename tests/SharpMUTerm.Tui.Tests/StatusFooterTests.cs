using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Parsing;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A host that draws the status row (<see cref="HostChrome.Status"/>) gets no cell for it and is told what the row
/// would have said; a host that draws the command line's field (<see cref="HostChrome.Input"/>) is told which rows the
/// bars are on, and the bars stay the cell editor.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class StatusFooterTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class FooterPresenter : IPointerPresenter
    {
        public List<StatusOverlay> Statuses { get; } = [];

        public List<InputBand?> Bands { get; } = [];

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

        public void Status(StatusOverlay status) => Statuses.Add(status);

        public void InputBand(InputBand? band) => Bands.Add(band);
    }

    private static SharpMUTermApp App(HostChrome chrome, out string frame, IPointerPresenter? pointer = null, string? view = null)
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.Headless(new HeadlessConsoleDriver(Width, Height), Caps) with { Chrome = chrome, Pointer = pointer };
        var app = new SharpMUTermApp(DemoScene.Build(), host);
        frame = app.RenderSnapshot(view);
        app.RenderNextFrame();
        return app;
    }

    private static string Squash(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [Test]
    public async Task AHostThatDrawsTheStatusRowGetsNoStatusRowAndIsToldWhatItSays()
    {
        await using var cells = App(HostChrome.None, out var cellFrame);
        var presenter = new FooterPresenter();
        await using var native = App(HostChrome.None with { Status = true }, out _, presenter);

        await Assert.That(cells.LaidOutRows.Status).IsEqualTo(1);
        await Assert.That(native.LaidOutRows.Status).IsEqualTo(0);
        await Assert.That(native.LaidOutRows.Workspace).IsEqualTo(cells.LaidOutRows.Workspace + 1);

        // What the footer was told is what the terminal's last row says.
        await Assert.That(presenter.Statuses).IsNotEmpty();
        var said = presenter.Statuses[^1].Model;
        var lastRow = FrameGrid.Decode(cellFrame, Width, Height)[^1];
        await Assert.That(Squash(said.Text)).IsEqualTo(Squash(lastRow));
        await Assert.That(said.Targets()).Contains(HeaderModel.PaletteTarget);

        // The cell renderer's markup of the model reads the same.
        var back = MarkupParser.Remove(SharpMUTermApp.StatusRowMarkup(said));
        await Assert.That(Squash(back)).IsEqualTo(Squash(lastRow));
    }

    [Test]
    public async Task AStatusClickRunsOnlyWhatTheRowOffered()
    {
        await using (var refused = App(HostChrome.None with { Status = true }, out _, new FooterPresenter()))
        {
            await Assert.That(refused.ActivateStatusTarget("term:quit")).IsFalse();
            await Assert.That(refused.MenuIsOpen).IsFalse();
        }

        await using var app = App(HostChrome.None with { Status = true }, out _, new FooterPresenter());
        await Assert.That(app.ActivateStatusTarget(HeaderModel.PaletteTarget)).IsTrue();
        await Assert.That(app.MenuIsOpen).IsTrue();
    }

    [Test]
    public async Task AHostThatDrawsTheFieldIsToldTheBarsRowsAndThePanesDoNotMove()
    {
        await using var cells = App(HostChrome.None, out _);
        var presenter = new FooterPresenter();
        await using var native = App(HostChrome.None with { Input = true }, out _, presenter);

        // The field is the host's pixels: no pane rectangle, so nothing said over NAWS, moves for it.
        await Assert.That(native.PaneOutputRects()).IsEquivalentTo(cells.PaneOutputRects());
        await Assert.That(native.LaidOutRows).IsEqualTo(cells.LaidOutRows);

        var band = native.InputBandNow();
        await Assert.That(band).IsNotNull();
        await Assert.That(band!.Pad).IsEqualTo(InputLayout.HostFieldPad);
        await Assert.That(band.Top + band.Rows).IsEqualTo(Height - native.LaidOutRows.Status);
        await Assert.That(band.Prompts.Single().Text).Contains("›");
        await Assert.That(band.Hint).Contains("new line");
        await Assert.That(presenter.Bands.OfType<InputBand>()).IsNotEmpty();

        // A host that does not draw the field is told nothing.
        await Assert.That(cells.InputBandNow()).IsNull();
    }

    [Test]
    public async Task UnderAModalTheFieldIsNotDrawn()
    {
        await using var app = App(HostChrome.None with { Input = true }, out _, new FooterPresenter(), "menu");
        await Assert.That(app.InputBandNow()).IsNull();
    }
}
