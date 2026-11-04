using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The furniture of a layout in the client: a strip panel is as tall as its content and draws no tab strip, the status
/// row does not repeat meters the vitals strip is already showing, a panel takes no rail row and no <c>⌥N</c>, the
/// composer keeps an editor's height, and ⌥M renames and deletes saved layouts.
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class LayoutStripTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly ConsoleKeyInfo AltM = new('m', ConsoleKey.M, shift: false, alt: true, control: false);

    private static readonly ConsoleKeyInfo Down = new('\0', ConsoleKey.DownArrow, false, false, false);

    private static readonly ConsoleKeyInfo Enter = new('\r', ConsoleKey.Enter, false, false, false);

    private static readonly ConsoleKeyInfo Esc = new('\u001b', ConsoleKey.Escape, false, false, false);

    private static readonly ConsoleKeyInfo Backspace = new('\b', ConsoleKey.Backspace, false, false, false);

    private static SharpMUTermApp Demo(AppConfiguration? config = null, int width = 120, int height = 32)
    {
        Console.SetIn(TextReader.Null);
        config ??= DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        return new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(width, height));
    }

    private static ConsoleKeyInfo Letter(char c) =>
        new(c, ConsoleKey.A + (char.ToUpperInvariant(c) - 'A'), char.IsUpper(c), false, false);

    private static ConsoleKeyInfo Typed(char c) => char.IsLetter(c) ? Letter(c) : new(c, ConsoleKey.Spacebar, false, false, false);

    [Test]
    [Arguments(120, 32)]
    [Arguments(80, 24)]
    public async Task CombatsVitalsAndQuickBarAreOneRowEachWithNoTabStrip(int width, int height)
    {
        await using var app = Demo(width: width, height: height);
        app.RenderSnapshot("layout-combat");

        var output = app.PaneOutputRects();
        var whole = app.PaneSnapshot().Rects;
        await Assert.That(output["panel:vitals"].Height).IsEqualTo(1);
        await Assert.That(output["panel:quick-bar"].Height).IsEqualTo(1);

        // No tab strip: the pane is its content, so its whole rectangle is that one row too.
        await Assert.That(whole["panel:vitals"].Height).IsEqualTo(1);
        await Assert.That(whole["panel:quick-bar"].Height).IsEqualTo(1);

        // The output pane keeps its tab strip, and everything the strips did not take.
        await Assert.That(whole["panel:output"].Height - output["panel:output"].Height).IsEqualTo(1);
        await Assert.That(output["panel:output"].Height).IsGreaterThan(height / 2);
    }

    [Test]
    public async Task TheStatusRowLeavesOutTheMetersWhileTheVitalsStripShowsThem()
    {
        await using var app = Demo();
        app.RenderSnapshot("layout-combat");

        await Assert.That(app.VitalsStripShowing()).IsTrue();
        await Assert.That(app.StatusMarkup).DoesNotContain("[dim]hp[/]");
        await Assert.That(app.StatusMarkup).Contains("Corvid");
    }

    [Test]
    public async Task TheMetersComeBackToTheStatusRowWhenTheStripIsNotOnScreen()
    {
        await using var app = Demo();
        app.RenderSnapshot("meters");

        await Assert.That(app.VitalsStripShowing()).IsFalse();
        await Assert.That(app.StatusMarkup).Contains("[dim]hp[/]");
    }

    [Test]
    public async Task ZoomingAnotherPaneBringsTheMetersBack()
    {
        await using var app = Demo();
        app.RenderSnapshot("layout-combat");

        app.DispatchCommand("layout:zoom");

        await Assert.That(app.VitalsStripShowing()).IsFalse();
        await Assert.That(app.StatusMarkup).Contains("[dim]hp[/]");
    }

    [Test]
    public async Task PanelsTakeNoRailRowAndNoNumber()
    {
        await using var app = Demo();
        app.RenderSnapshot("layout-combat");

        var rail = string.Join("\n", app.RailLines);
        await Assert.That(rail).DoesNotContain("Vitals");
        await Assert.That(rail).DoesNotContain("Quick bar");
        await Assert.That(app.NumberedWindowIds.Any(id => id.StartsWith(TerminalPanels.WindowPrefix, StringComparison.Ordinal))).IsFalse();

        // The map is a place you go, so it keeps its number — the third, right after main and Chat.
        await Assert.That(app.NumberedWindowIds[2]).IsEqualTo(SharpMUTermApp.MapWindowId(DemoScene.ActiveSessionKey));
        await Assert.That(rail).Contains("⌥3");
        await Assert.That(rail).DoesNotContain("⌥4");
    }

    [Test]
    public async Task RoleplayDrawsWhoIsHereAndGivesTheComposerAnEditorsHeight()
    {
        await using var app = Demo();
        app.RenderSnapshot("layout-roleplay");

        await Assert.That(app.LiveLayoutId).IsEqualTo(BuiltInLayouts.RoleplayId);
        await Assert.That(app.PaneIds).Contains("panel:who-here");
        await Assert.That(app.PaneIds).Contains("panel:composer");
        await Assert.That(app.PaneOutputRects()["panel:composer"].Height).IsGreaterThanOrEqualTo(PanelSizing.ComposerMinRows);
    }

    [Test]
    public async Task RenameTypesANewNameOverTheSelectedLayout()
    {
        var config = DemoScene.Build();
        config.Layouts.Add(new LayoutDefinition { Id = "mine", Name = "Mine", Placements = BuiltInLayouts.Combat.Placements });
        await using var app = Demo(config);
        app.RenderSnapshot();
        app.SimulateKey(AltM);
        app.SimulateLayoutsKey(Down);
        app.SimulateLayoutsKey(Down); // Mine

        await Assert.That(string.Join("\n", app.LayoutsLines)).Contains("rename");
        app.SimulateLayoutsKey(Letter('r'));
        for (var i = 0; i < "Mine".Length; i++)
        {
            app.SimulateLayoutsKey(Backspace);
        }

        foreach (var c in "Raid night")
        {
            app.SimulateLayoutsKey(Typed(c));
        }

        await Assert.That(string.Join("\n", app.LayoutsLines)).Contains("Raid night");
        app.SimulateLayoutsKey(Enter);

        await Assert.That(app.LayoutsOpen).IsTrue(); // ⏎ keeps the name; it does not apply the layout
        await Assert.That(config.Layouts.Single().Name).IsEqualTo("Raid night");
        await Assert.That(string.Join("\n", app.LayoutsLines)).Contains("renamed to Raid night");
    }

    [Test]
    public async Task EscLeavesARenameWithoutClosingTheListOrChangingTheName()
    {
        var config = DemoScene.Build();
        config.Layouts.Add(new LayoutDefinition { Id = "mine", Name = "Mine", Placements = BuiltInLayouts.Combat.Placements });
        await using var app = Demo(config);
        app.RenderSnapshot();
        app.SimulateKey(AltM);
        app.SimulateLayoutsKey(Down);
        app.SimulateLayoutsKey(Down);
        app.SimulateLayoutsKey(Letter('r'));
        app.SimulateLayoutsKey(Letter('x'));

        app.SimulateLayoutsKey(Esc);

        await Assert.That(app.LayoutsOpen).IsTrue();
        await Assert.That(config.Layouts.Single().Name).IsEqualTo("Mine");
    }

    [Test]
    public async Task DeleteAsksFirstAndYDeletes()
    {
        var config = DemoScene.Build();
        config.Layouts.Add(new LayoutDefinition { Id = "mine", Name = "Mine", Placements = BuiltInLayouts.Combat.Placements });
        await using var app = Demo(config);
        app.RenderSnapshot();
        app.SimulateKey(AltM);
        app.SimulateLayoutsKey(Down);
        app.SimulateLayoutsKey(Down);

        app.SimulateLayoutsKey(Letter('d'));
        await Assert.That(string.Join("\n", app.LayoutsLines)).Contains("delete Mine?");
        await Assert.That(config.Layouts.Count).IsEqualTo(1); // nothing yet

        app.SimulateLayoutsKey(Letter('y'));
        await Assert.That(config.Layouts).IsEmpty();
        await Assert.That(app.LayoutsOpen).IsTrue();
    }

    [Test]
    public async Task AnyOtherAnswerKeepsTheLayout()
    {
        var config = DemoScene.Build();
        config.Layouts.Add(new LayoutDefinition { Id = "mine", Name = "Mine", Placements = BuiltInLayouts.Combat.Placements });
        await using var app = Demo(config);
        app.RenderSnapshot();
        app.SimulateKey(AltM);
        app.SimulateLayoutsKey(Down);
        app.SimulateLayoutsKey(Down);

        app.SimulateLayoutsKey(Letter('d'));
        app.SimulateLayoutsKey(Letter('n'));

        await Assert.That(config.Layouts.Count).IsEqualTo(1);
        await Assert.That(string.Join("\n", app.LayoutsLines)).Contains("kept");
    }

    [Test]
    public async Task ABuiltInIsNeitherRenamedNorDeletedAndTheFooterDoesNotOfferEither()
    {
        var config = DemoScene.Build();
        await using var app = Demo(config);
        app.RenderSnapshot();
        app.SimulateKey(AltM); // Roleplay, built in, is selected

        var lines = string.Join("\n", app.LayoutsLines);
        await Assert.That(lines).DoesNotContain("rename");
        await Assert.That(lines).DoesNotContain("delete");

        app.SimulateLayoutsKey(Letter('d'));
        await Assert.That(string.Join("\n", app.LayoutsLines)).Contains("cannot be deleted");
        app.SimulateLayoutsKey(Letter('r'));
        await Assert.That(string.Join("\n", app.LayoutsLines)).Contains("n saves a copy you can rename");
        await Assert.That(BuiltInLayouts.All.Select(l => l.Name)).IsEquivalentTo(new[] { "Roleplay", "Combat" });
    }
}
