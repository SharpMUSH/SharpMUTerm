using SharpConsoleUI.Drivers;
using SharpConsoleUI.Parsing;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The tab strip against the pane it has to fit in, driven through a real layout and read off a real
/// frame. <see cref="TabStripFitTests"/> pins the rule; this pins that the cells it is counting are the
/// cells the framework actually spends — the pad either side of a title, the <c>×</c> on the active tab
/// and the <c>│</c> between tabs are all the framework's, and an arithmetic that drifted from them would
/// still satisfy a pure test while overflowing the screen.
/// </summary>
/// <remarks>
/// Serialised with the other suites that render: a frame redirects the process-global <c>Console.Out</c>.
/// </remarks>
[NotInParallel]
public class TabStripElisionTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp Crowded()
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(
            DemoScene.Build(), Headless, new HeadlessConsoleDriver(Width, Height));
        app.RenderSnapshot("tabs-many");
        return app;
    }

    /// <summary>
    /// What the framework spends on one tab besides its title: a space either side, the <c>×</c> a
    /// closable tab draws, and the <c>│</c> that follows every tab but the last
    /// (<c>TabControl.Rendering.cs</c>).
    /// </summary>
    private static int StripWidth(IReadOnlyList<(string WindowId, string Title, bool Closable)> strip) =>
        strip.Select((t, i) =>
            MarkupParser.StripLength(t.Title) + 2 + (t.Closable ? 1 : 0) + (i < strip.Count - 1 ? 1 : 0)).Sum();

    /// <summary>
    /// The headline: every tab in a crowded pane is drawn, where before the strip ran off the edge and
    /// the tabs past it were simply not there.
    /// </summary>
    [Test]
    public async Task EveryTabInACrowdedPaneFitsInsideItsOwnPane()
    {
        var app = Crowded();
        var rects = app.PaneOutputRects();

        foreach (var (paneId, titles) in app.PaneTabTitles)
        {
            var drawn = StripWidth(app.PaneTabStrip(paneId));

            await Assert.That(drawn).IsLessThanOrEqualTo(rects[paneId].Width)
                .Because($"{paneId} draws {titles.Count} tabs in {rects[paneId].Width} cells");
        }
    }

    /// <summary>
    /// And it is the crowded case that is being asserted: a pane holding one tab proves nothing about a
    /// strip that has to shed cells.
    /// </summary>
    [Test]
    public async Task TheCrowdedViewReallyIsCrowded()
    {
        var app = Crowded();
        var busiest = app.PaneTabTitles.Values.Max(t => t.Count);

        await Assert.That(busiest).IsGreaterThanOrEqualTo(5);
    }

    /// <summary>
    /// The repeated owner prefix is what goes, and it goes before anybody loses a letter of their own
    /// name — every one of those tabs belongs to the same character, so the prefix says nothing the chip
    /// behind the tab does not.
    /// </summary>
    [Test]
    public async Task TheRepeatedOwnerPrefixIsWhatTheStripGivesUp()
    {
        var app = Crowded();
        var strip = app.PaneTabTitles.Values.MaxBy(t => t.Count)!;

        await Assert.That(strip.Any(t => t.Contains(DemoScene.MainCharacterName, StringComparison.Ordinal)))
            .IsFalse();
    }

    /// <summary>
    /// The tab the pane is showing keeps a name worth reading. A strip whose every label is three letters
    /// and an ellipsis — the selected one included — has stopped answering the question it is there for.
    /// </summary>
    [Test]
    public async Task TheTabThePaneIsShowingKeepsAReadableName()
    {
        var app = Crowded();
        var strip = app.PaneTabTitles.Values.MaxBy(t => t.Count)!;
        var selected = strip.Single(t => t.Contains("bold", StringComparison.Ordinal));

        await Assert.That(MarkupParser.StripLength(selected)).IsGreaterThanOrEqualTo(4);
        await Assert.That(selected).Contains("Chat");
    }

    /// <summary>
    /// A strip with room is untouched, which is the answer for nearly every pane this client ever draws:
    /// the default workspace holds two tabs in a wide pane and neither of them is elided.
    /// </summary>
    [Test]
    public async Task AStripWithRoomIsLeftExactlyAsItWas()
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(
            DemoScene.Build(), Headless, new HeadlessConsoleDriver(Width, Height));
        app.RenderSnapshot();

        await Assert.That(app.PaneTabTitles.Values.SelectMany(t => t).Any(t => t.Contains('…')))
            .IsFalse();
    }
}
