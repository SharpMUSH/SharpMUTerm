using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// F10 at the smallest terminal this client supports. Its key line is wider than the modal at 80 columns and wraps to a
/// second row; the modal used to be sized by its line count, so the last detail row sat under its bottom edge.
/// </summary>
[NotInParallel]
public class PackageScreenFitTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    [Test]
    public async Task ThePackagesListFitsItsModalAt80By24()
    {
        Console.SetIn(TextReader.Null);
        await using var app = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(80, 24));
        var rows = FrameGrid.Decode(app.RenderSnapshot("packages"), 80, 24);
        var text = string.Join("\n", rows);

        await Assert.That(text).Contains("Packages");
        await Assert.That(text).Contains("characters").Because("the last detail row is on screen, inside the modal");
        await Assert.That(text).Contains("Esc close").Because("the wrapped key line is whole");
        await Assert.That(text).DoesNotContain("░").Because("nothing scrolls: the modal is as tall as what it holds");
    }

    [Test]
    public async Task TheRowsAreMeasuredWrappedNotCounted()
    {
        var lines = new List<string> { new string('x', 50), "short" };
        await Assert.That(ListOverlay.Rows(lines, 30)).IsEqualTo(3);
        await Assert.That(ListOverlay.Rows(lines, 60)).IsEqualTo(2);
    }
}
