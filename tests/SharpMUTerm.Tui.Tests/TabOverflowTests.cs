using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The tab strip's overflow chip: past its floors a strip cannot draw every tab, and the chip at its right edge says
/// how many are missing and lists them. Read off a real 80×24 frame of the <c>tabs-many</c> view, where the Chat pane
/// holds more captures than it can draw.
/// </summary>
[NotInParallel]
public class TabOverflowTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static (SharpMUTermApp App, string Frame) Crowded(int width = 80, int height = 24)
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(new HeadlessConsoleDriver(width, height), Caps));
        var frame = app.RenderSnapshot("tabs-many");
        return (app, frame);
    }

    [Test]
    public async Task AStripThatCannotDrawEveryTabCarriesAChipCountingTheRest()
    {
        var (app, frame) = Crowded();

        var (paneId, menu) = app.TabOverflowMenus.Single();
        await Assert.That(menu.Entries.Count).IsGreaterThan(0);
        await Assert.That(FrameText(frame)).Contains(SharpMUTermApp.TabOverflowChip(menu));
        await Assert.That(menu.Entries.All(e => e.Command!.StartsWith(CommandIds.WindowPrefix, StringComparison.Ordinal))).IsTrue();
        await Assert.That(app.DrawnModel(SurfaceCatalog.TabOverflow, paneId)).IsEqualTo(menu)
            .Because("the list the window's menu is handed is the one the cells recorded");
    }

    [Test]
    public async Task TheChipSpendsNoCellAndMovesNoPane()
    {
        Console.SetIn(TextReader.Null);
        var plain = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(new HeadlessConsoleDriver(80, 24), Caps));
        plain.RenderSnapshot("tabs-many");
        var (app, _) = Crowded();

        await Assert.That(app.PaneOutputRects()).IsEquivalentTo(plain.PaneOutputRects());
        var (paneId, _) = app.TabOverflowMenus.Single();
        var chip = app.TabOverflowChipAt(paneId)!.Value;
        var strip = app.PaneOutputRects()[paneId];
        await Assert.That(chip.X + chip.Width).IsLessThanOrEqualTo(strip.X + strip.Width)
            .Because("the chip is painted over the strip's own last cells");
    }

    [Test]
    public async Task AWideStripHasNoChip()
    {
        var (app, _) = Crowded(200, 40);

        await Assert.That(app.TabOverflowMenus.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InATerminalTheChipOpensACellListAndADigitGoesToThatTab()
    {
        var (app, _) = Crowded();
        var (paneId, menu) = app.TabOverflowMenus.Single();
        var target = menu.Entries[0].Command![CommandIds.WindowPrefix.Length..];

        await Assert.That(app.OpenTabOverflow(paneId)).IsTrue();
        await Assert.That(app.SendMenu.IsOpen).IsTrue();
        await Assert.That(string.Join("\n", app.SendMenu.Lines.Select(MarkupText.Plain))).Contains(menu.Entries[0].Label);

        app.SendMenu.SimulateKey(new ConsoleKeyInfo('1', ConsoleKey.D1, false, false, false));

        await Assert.That(app.SendMenu.IsOpen).IsFalse();
        await Assert.That(app.ActiveWindowId()).IsEqualTo(target);
    }

    [Test]
    public async Task ACommandTheListDidNotOfferIsRefused()
    {
        var (app, _) = Crowded();
        var (paneId, _) = app.TabOverflowMenus.Single();
        app.OpenTabOverflow(paneId);
        app.SendMenu.Close();

        await Assert.That(app.RunTabOverflowEntry("term:quit")).IsFalse();
    }

    /// <summary>
    /// The chip is painted over the strip's last cells, which can be the tail of a tab the framework drew. A press on it
    /// opens the list and nothing else: the frame the framework is left (it reads the same list after this client) carries
    /// no button, so no tab under the chip is selected or closed.
    /// </summary>
    [Test]
    public async Task AClickOnTheChipDoesNotAlsoSelectTheTabUnderIt()
    {
        var (app, _) = Crowded();
        var (paneId, _) = app.TabOverflowMenus.Single();
        var chip = app.TabOverflowChipAt(paneId)!.Value;
        var press = new List<MouseFlags> { MouseFlags.Button1Pressed };
        var click = new List<MouseFlags> { MouseFlags.Button1Released, MouseFlags.Button1Clicked };
        var at = new System.Drawing.Point(chip.X + 1, chip.Y);

        await Assert.That(app.AChipClickReachesTheTabUnderIt(paneId, filtered: false)).IsTrue()
            .Because("unfiltered, the framework's own click on the chip's cells lands on the tab drawn under it — the defect");

        await Assert.That(app.TakesTabOverflowPress(press, at)).IsTrue();
        await Assert.That(app.TakesTabOverflowPress(click, at)).IsTrue();
        await Assert.That(press.Concat(click).Any(f => f.ToString().StartsWith("Button1", StringComparison.Ordinal))).IsFalse()
            .Because("the framework is left a movement, not a click");
        await Assert.That(app.AChipClickReachesTheTabUnderIt(paneId, filtered: true)).IsFalse()
            .Because("no cell of the chip selects or closes the tab drawn under it");

        var elsewhere = new List<MouseFlags> { MouseFlags.Button1Pressed };
        await Assert.That(app.TakesTabOverflowPress(elsewhere, new System.Drawing.Point(chip.X - 3, chip.Y))).IsFalse();
        await Assert.That(elsewhere).Contains(MouseFlags.Button1Pressed).Because("a press beside the chip is the strip's as before");
    }

    [Test]
    public async Task TheCellListNamesEachHiddenTabWithADigit()
    {
        var menu = new SharpMUTerm.Client.Commands.MenuModel([
            new("Chat", null, "win:a"),
            new("Ooc (3)", null, "win:b"),
        ]);

        var lines = SharpMUTermApp.TabOverflowLines(menu).Select(MarkupText.Plain).ToList();

        await Assert.That(lines[0]).Contains("1 Chat");
        await Assert.That(lines[1]).Contains("2 Ooc (3)");
        await Assert.That(SharpMUTermApp.TabOverflowChip(menu)).IsEqualTo("+2 ▾");
    }

    private static string FrameText(string ansi) =>
        System.Text.RegularExpressions.Regex.Replace(ansi, @"\x1b\[[0-9;?]*[A-Za-z]", string.Empty);
}
