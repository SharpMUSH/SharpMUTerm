using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Layouts in the client: ⌥M and ⌃P open the list, applying realises panes for the panels that have data, a bound
/// session gets its layout unless the restored workspace arranged it, a re-fold waits out a burst and rebuilds only
/// when a panel appears, and the player rearranging the panes ends the re-folding.
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class LayoutWiringTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly ConsoleKeyInfo AltM = new('m', ConsoleKey.M, shift: false, alt: true, control: false);

    private static SharpMUTermApp Demo(AppConfiguration? config = null)
    {
        Console.SetIn(TextReader.Null);
        config ??= DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        return new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
    }

    private static (SharpMUTermApp App, AppConfiguration Config, ManualTimeProvider Time) Live(bool restored = false)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        if (!restored)
        {
            config.LastSession = null;
        }

        var time = new ManualTimeProvider();
        var app = new SharpMUTermApp(
            config, ClientHost.Headless(new HeadlessConsoleDriver(120, 32), Headless), time, autoLayout: true);
        return (app, config, time);
    }

    private static ConsoleKeyInfo Letter(char c) => new(c, ConsoleKey.A + (char.ToUpperInvariant(c) - 'A'), false, false, false);

    [Test]
    public async Task AltMIsAClaimedChordThatOpensTheLayoutsList()
    {
        await Assert.That(MacroKeys.AppShortcuts.Any(s => s.Modifiers == ConsoleModifiers.Alt && s.Key == ConsoleKey.M)).IsTrue();

        await using var app = Demo();
        app.RenderSnapshot();
        app.SimulateKey(AltM);

        await Assert.That(app.LayoutsOpen).IsTrue();
        var text = string.Join("\n", app.LayoutsLines);
        await Assert.That(text).Contains("Roleplay");
        await Assert.That(text).Contains("Combat");
        await Assert.That(text).Contains("in use");
        await Assert.That(text).Contains("[Esc]] Close");

        app.SimulateLayoutsKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));
        await Assert.That(app.LayoutsOpen).IsFalse();
    }

    [Test]
    public async Task TheCommandSurfaceOpensTheSameList()
    {
        await using var app = Demo();
        app.RenderSnapshot();

        await Assert.That(app.DispatchCommand("layout:layouts")).IsTrue();
        await Assert.That(app.LayoutsOpen).IsTrue();
    }

    [Test]
    public async Task CombatOverTheDemoPutsTheMapBesideTheOutputWithChatTabbedUnderIt()
    {
        await using var app = Demo();
        app.RenderSnapshot("layout-combat");

        await Assert.That(app.LiveLayoutId).IsEqualTo(BuiltInLayouts.CombatId);
        await Assert.That(app.PaneIdOf("main")).IsEqualTo("panel:output");
        await Assert.That(app.PaneIdOf(SharpMUTermApp.MapWindowId(DemoScene.ActiveSessionKey))).IsEqualTo("panel:map");
        await Assert.That(app.PaneIdOf(DemoScene.ChatWindowId)).IsEqualTo("panel:map");
        await Assert.That(app.FocusedPaneId).IsEqualTo("panel:output");
        // Vitals and the quick bar have nothing to draw them in this build, so they take no pane.
        await Assert.That(app.PaneIds).IsEquivalentTo(new[] { "panel:output", "panel:map" });
    }

    [Test]
    public async Task ARegisteredPanelIsDrawnInItsOwnPaneOnceItHasData()
    {
        await using var app = Demo();
        app.Panels.Register(PanelIds.Vitals, _ => new MarkupControl(["vitals"]));
        app.RenderSnapshot("layout-combat");

        var window = TerminalPanels.WindowId(PanelIds.Vitals, DemoScene.ActiveSessionKey);
        await Assert.That(app.PaneIdOf(window)).IsEqualTo("panel:vitals");
        await Assert.That(app.Panels.ControlFor(window)).IsNotNull();
        await Assert.That(TerminalPanels.TryParseWindowId(window, out var panel, out var key)).IsTrue();
        await Assert.That(panel).IsEqualTo(PanelIds.Vitals);
        await Assert.That(key).IsEqualTo(DemoScene.ActiveSessionKey);
    }

    [Test]
    public async Task ABoundSessionGetsItsLayoutWhenNothingWasRestored()
    {
        var (app, config, _) = Live();
        await using var _app = app;
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);

        await Assert.That(app.LiveLayoutId).IsEqualTo(BuiltInLayouts.RoleplayId);
        await Assert.That(app.LiveLayoutOwner).IsEqualTo(session.SessionKey);
        await Assert.That(app.PaneIdOf("main")).IsEqualTo("panel:output");
    }

    [Test]
    public async Task ARestoredWorkspaceWinsOverTheLayoutAtStartup()
    {
        var (app, config, _) = Live(restored: true);
        await using var _app = app;
        var before = app.PaneIds.ToArray();
        app.BindWorldWithoutConnecting(config.Worlds[0]);

        await Assert.That(app.LiveLayoutId).IsNull();
        await Assert.That(app.PaneIds).IsEquivalentTo(before);
    }

    [Test]
    public async Task ARefoldWaitsForTheBurstAndRebuildsOnlyWhenAPanelAppears()
    {
        var (app, config, time) = Live();
        await using var _app = app;
        config.Worlds[0].Layout = BuiltInLayouts.CombatId;
        app.Panels.Register(PanelIds.Vitals, _ => new MarkupControl(["vitals"]));
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        await Assert.That(app.PaneIds).IsEquivalentTo(new[] { "panel:output" });

        var rebuilds = app.PaneAreaRebuilds;
        for (var hp = 400; hp < 410; hp++)
        {
            session.GameData.SetGmcp("Char.Vitals", $$"""{"hp":"{{hp}}","maxhp":"500"}""");
        }

        await Assert.That(app.PaneAreaRebuilds).IsEqualTo(rebuilds); // nothing until the burst settles
        time.Advance(SharpMUTermApp.LayoutRefoldDelay);
        await Assert.That(app.PaneIds).Contains("panel:vitals");
        await Assert.That(app.PaneAreaRebuilds).IsEqualTo(rebuilds + 1);

        rebuilds = app.PaneAreaRebuilds;
        session.GameData.SetGmcp("Char.Vitals", """{"hp":"12","maxhp":"500"}""");
        time.Advance(SharpMUTermApp.LayoutRefoldDelay);
        await Assert.That(app.PaneAreaRebuilds).IsEqualTo(rebuilds); // the same panels: no reflow
    }

    [Test]
    public async Task RearrangingThePanesHandsThemToThePlayer()
    {
        var (app, config, time) = Live();
        await using var _app = app;
        config.Worlds[0].Layout = BuiltInLayouts.CombatId;
        app.Panels.Register(PanelIds.Vitals, _ => new MarkupControl(["vitals"]));
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);

        app.OpenUnownedWindowForTest("aside", "Aside"); // a second tab, so the split has something to move
        var shape = app.PaneIds.Count;
        app.DispatchCommand("layout:split-right");
        await Assert.That(app.PaneIds.Count).IsEqualTo(shape + 1);
        session.GameData.SetGmcp("Char.Vitals", """{"hp":"400","maxhp":"500"}""");
        time.Advance(SharpMUTermApp.LayoutRefoldDelay);

        await Assert.That(app.PaneIds).DoesNotContain("panel:vitals");
        await Assert.That(app.LiveLayoutId).IsNull();
    }

    [Test]
    public async Task SaveAsNewCapturesTheArrangementAndCPutsItToUse()
    {
        var config = DemoScene.Build();
        await using var app = Demo(config);
        app.RenderSnapshot("layout-combat");
        app.SimulateKey(AltM);

        app.SimulateLayoutsKey(Letter('n'));
        await Assert.That(config.Layouts.Count).IsEqualTo(1);
        var saved = config.Layouts[0];
        await Assert.That(saved.Name).IsEqualTo("Combat 2");
        await Assert.That(saved.PlacementOf(PanelIds.Map)!.Region).IsEqualTo(LayoutRegion.Side);
        await Assert.That(saved.PlacementOf(PanelIds.Chat)!.TabOf).IsEqualTo(PanelIds.Map);

        app.SimulateLayoutsKey(Letter('c'));
        var corvid = config.Worlds[0].Characters.First(c => c.Name == DemoScene.MainCharacterName);
        await Assert.That(corvid.Layout).IsEqualTo(saved.Id);
        await Assert.That(string.Join("\n", app.LayoutsLines)).Contains("Corvid uses Combat 2");
    }

    [Test]
    public async Task SavingChangesToABuiltInSavesACopyAndPointsTheCharacterAtIt()
    {
        var config = DemoScene.Build();
        await using var app = Demo(config);
        app.RenderSnapshot("layout-combat");
        app.SimulateKey(AltM);
        app.SimulateLayoutsKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false)); // Combat

        app.SimulateLayoutsKey(Letter('s'));

        await Assert.That(BuiltInLayouts.Combat.Placements.Count).IsEqualTo(5); // untouched
        var copy = config.Layouts.Single();
        var corvid = config.Worlds[0].Characters.First(c => c.Name == DemoScene.MainCharacterName);
        await Assert.That(corvid.Layout).IsEqualTo(copy.Id);
        await Assert.That(app.LiveLayoutId).IsEqualTo(copy.Id);
    }

    [Test]
    public async Task TheSuggestionIsTakenOnceAndSaidOnce()
    {
        var (app, config, _) = Live();
        await using var _app = app;
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        session.GameData.SetGmcp("Char.Vitals", """{"hp":"400","maxhp":"500"}""");

        app.AcceptLayoutSuggestion(session);
        app.AcceptLayoutSuggestion(session);

        await Assert.That(config.Worlds[0].AcceptedLayoutSuggestion).IsEqualTo(BuiltInLayouts.CombatId);
        var said = app.Messages.Entries.Count(m => m.Text.Contains("Using the Combat layout", StringComparison.Ordinal));
        await Assert.That(said).IsEqualTo(1);
        await Assert.That(app.LiveLayoutId).IsEqualTo(BuiltInLayouts.CombatId);
    }

    [Test]
    public async Task TheWorldsScreenOffersEveryLayoutAndAWordForNone()
    {
        IReadOnlyList<LayoutDefinition> all = [.. BuiltInLayouts.All, new LayoutDefinition { Id = "x1", Name = "Mine" }];

        await Assert.That(WorldsScreenRenderer.LayoutChoices(all, WorldsScreenRenderer.WorldLayoutDefault))
            .IsEquivalentTo(new[] { "(suggested)", "Roleplay", "Combat", "Mine" });
        await Assert.That(WorldsScreenRenderer.LayoutId(all, "mine")).IsEqualTo("x1");
        await Assert.That(WorldsScreenRenderer.LayoutId(all, WorldsScreenRenderer.CharacterLayoutDefault)).IsNull();
        await Assert.That(WorldsScreenRenderer.LayoutName(all, "gone")).IsNull();
    }
}
