using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui.Panels;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The three markup panels — who is here, the vitals strip, the quick bar — as pure renderers, and as panes of a
/// connected client: what a click does, whose session it does it on, and that a panel costs no cell outside its pane.
/// </summary>
[NotInParallel]
public class TerminalPanelTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly VitalsStripRenderer.Ink VitalsInk = new("#00f5b7", "#f5c542", "#6a6a72");

    // --- who is here ------------------------------------------------------------------------------------------

    [Test]
    public async Task WhoHereListsPlayersThenCreaturesUnderTheRoomName()
    {
        var rows = WhoHereRenderer.Render(
            "The Grand Plaza",
            [new("Ann", "Ann the Bold", true, 30), new("Bo", "Bo", true, 600), new("rat", "a rat", false, Id: "1")],
            40, 10, new WhoHereRenderer.Ink("#00f5b7"));

        var plain = rows.Select(StripMarkup).ToList();
        await Assert.That(plain[0]).Contains("The Grand Plaza").And.Contains("3");
        await Assert.That(plain[1]).Contains("Ann the Bold").And.Contains("active");
        await Assert.That(plain[2]).Contains("idle 10m");
        await Assert.That(plain[3]).Contains("a rat");
        await Assert.That(rows[1]).Contains($"[link={WhoHereRenderer.LinkPrefix}0]");
        await Assert.That(plain.All(r => r.Length <= 40)).IsTrue();
    }

    [Test]
    public async Task WhoHereSaysHowManyItCouldNotShow()
    {
        var many = Enumerable.Range(0, 30).Select(i => new RoomOccupant($"P{i}", $"P{i}", true)).ToList();
        var rows = WhoHereRenderer.Render("Hall", many, 30, 6, new WhoHereRenderer.Ink("#00f5b7"));

        await Assert.That(rows.Count).IsLessThanOrEqualTo(6);
        await Assert.That(StripMarkup(rows[^1])).Contains("and 26 more");
    }

    [Test]
    public async Task ANameIsTheGamesTextAndIsEscaped()
    {
        var rows = WhoHereRenderer.Render(null, [new("Ann", "[bold]Ann[/]", true)], 40, 4, new WhoHereRenderer.Ink("#00f5b7"));

        await Assert.That(rows[1]).Contains("[[bold]]Ann[[/]]");
    }

    // --- vitals -----------------------------------------------------------------------------------------------

    private static StatusMeterRenderer.MeterReading Meter(string label, string value, string max) =>
        new(label, value, max, true, double.Parse(value) / double.Parse(max), double.Parse(value) / double.Parse(max) < StatusMeterRenderer.LowShare);

    [Test]
    public async Task AChangingValueMovesNoCell()
    {
        var before = VitalsStripRenderer.Render([Meter("hp", "1000", "1000"), Meter("mp", "900", "1000")], new CombatTarget("a ghoul", 0.9), 100, VitalsInk);
        var after = VitalsStripRenderer.Render([Meter("hp", "7", "1000"), Meter("mp", "90", "1000")], new CombatTarget("a ghoul", 0.05), 100, VitalsInk);

        await Assert.That(VisibleLength(after)).IsEqualTo(VisibleLength(before));
        var mp = StripMarkup(before).IndexOf("mp", StringComparison.Ordinal);
        await Assert.That(StripMarkup(after).IndexOf("mp", StringComparison.Ordinal)).IsEqualTo(mp);
        await Assert.That(StripMarkup(after)).Contains(VitalsStripRenderer.LowMark);
    }

    [Test]
    public async Task TheStripGivesUpDetailToFitAndNeverOverflows()
    {
        StatusMeterRenderer.MeterReading[] meters = [Meter("hp", "412", "500"), Meter("mp", "23", "120"), Meter("mv", "88", "100")];
        var target = new CombatTarget("a rotting ghoul", 0.4);

        var wide = StripMarkup(VitalsStripRenderer.Render(meters, target, 140, VitalsInk));
        var narrow = StripMarkup(VitalsStripRenderer.Render(meters, target, 50, VitalsInk));

        await Assert.That(wide).Contains("█").And.Contains("412/500").And.Contains("40%");
        await Assert.That(narrow.Length).IsLessThanOrEqualTo(50);
        await Assert.That(narrow).DoesNotContain("█");
        foreach (var width in new[] { 8, 20, 40, 60, 80, 100, 120 })
        {
            await Assert.That(VisibleLength(VitalsStripRenderer.Render(meters, target, width, VitalsInk))).IsLessThanOrEqualTo(width);
        }
    }

    [Test]
    public async Task NothingToShowIsNoStrip()
    {
        await Assert.That(VitalsStripRenderer.Render([], null, 80, VitalsInk)).IsEqualTo(string.Empty);
    }

    // --- quick bar --------------------------------------------------------------------------------------------

    private static readonly Macro[] Bindings =
    [
        new() { Name = "kick", Key = "F11", Enabled = true, Command = "kick" },
        new() { Name = "", Key = "Ctrl+F3", Enabled = true, Command = "cast 'cure light'" },
        new() { Name = "bash", Key = "F12", Enabled = true, Command = "bash" },
        new() { Name = "look", Key = "Num5", Enabled = true, Command = "look" },
        new() { Name = "off", Key = "F10", Enabled = false, Command = "nothing" },
    ];

    private static Macro? Resolve(string descriptor) =>
        Bindings.FirstOrDefault(m => m.Enabled && string.Equals(MacroKey.Canonicalise(m.Key), descriptor, StringComparison.OrdinalIgnoreCase));

    [Test]
    public async Task TheQuickBarListsFunctionKeysPlainFirstAndByNumber()
    {
        var entries = QuickBarRenderer.Entries(HostKeys.Terminal, Bindings, Resolve);

        await Assert.That(entries.Items.Select(e => e.Key)).IsEquivalentTo(new[] { "F11", "F12", "⌃F3" });
        await Assert.That(entries.Items[2].Title).IsEqualTo("cast 'cure light'");
    }

    [Test]
    public async Task TheQuickBarElidesAndNeverWraps()
    {
        var entries = QuickBarRenderer.Entries(HostKeys.Terminal, Bindings, Resolve);

        var wide = QuickBarRenderer.Render(entries, 100, "#00f5b7");
        await Assert.That(StripMarkup(wide).TrimEnd()).Contains("F11 kick").And.EndsWith(QuickBarRenderer.Hint);
        await Assert.That(wide).Contains($"[link={QuickBarRenderer.LinkPrefix}0]");

        foreach (var width in new[] { 6, 12, 20, 30, 45 })
        {
            var row = QuickBarRenderer.Render(entries, width, "#00f5b7");
            await Assert.That(VisibleLength(row)).IsLessThanOrEqualTo(width);
            await Assert.That(row).DoesNotContain("\n");
        }
    }

    // --- in a client ------------------------------------------------------------------------------------------

    [Test]
    public async Task ClickingANamePutsLookOnTheCommandLineAndSendsNothing()
    {
        var world = await Connected();
        world.Session.Occupants.Apply("Room.Players", """[{"name":"Ashlyn","fullname":"Ashlyn the Wanderer"}]""");
        var windowId = world.Open(PanelIds.WhoHere);

        await Assert.That(string.Join("\n", world.App.PanelMarkup(windowId))).Contains("Ashlyn the Wanderer");

        var sent = world.Telnet.Lines.Count;
        world.App.SimulatePanelClick(windowId, WhoHereRenderer.LinkPrefix + "0");

        await Assert.That(world.App.PrimaryInputText).IsEqualTo("look Ashlyn");
        await Assert.That(world.Telnet.Lines.Count).IsEqualTo(sent).Because("the demo world's connect commands are all that went");
    }

    [Test]
    public async Task ClickingAQuickBarKeyRunsItsBindingOnThePanelsCharacter()
    {
        var world = await Connected();
        var windowId = world.Open(PanelIds.QuickBar);
        var markup = string.Join("\n", world.App.PanelMarkup(windowId));
        await Assert.That(StripMarkup(markup)).Contains("⌃F1 score");

        var link = markup.Split($"[link={QuickBarRenderer.LinkPrefix}").Skip(1).First(part => part.Contains("score"));
        world.App.SimulatePanelClick(windowId, QuickBarRenderer.LinkPrefix + link[..link.IndexOf(']')]);

        await Assert.That(world.App.LastQuickMacro).IsEqualTo("Ctrl+F1");
        await Assert.That(world.Telnet.Lines).Contains("score");
    }

    [Test]
    public async Task TheVitalsPanelDrawsTheSessionsMetersAndTarget()
    {
        var world = await Connected();
        world.Session.GameData.SetGmcp("Char.Vitals", """{"hp":"412","maxhp":"500","mp":"23","maxmp":"120"}""");
        world.Session.GameData.SetGmcp("Char.Status", """{"enemy":"a rotting ghoul","enemypct":"40"}""");
        var windowId = world.Open(PanelIds.Vitals);

        var strip = StripMarkup(string.Join("\n", world.App.PanelMarkup(windowId)));
        await Assert.That(strip).Contains("412").And.Contains("a rotting").And.Contains("40%");
    }

    [Test]
    public async Task EachMeterIsDrawnInItsOwnColourAndALowOneInTheWarning()
    {
        var world = await Connected();
        world.App.Configuration.Text.StatusMeters[2].Colour = "purple";
        world.Session.GameData.SetGmcp("Char.Vitals", """{"hp":"412","maxhp":"500","mp":"23","maxmp":"120","moves":"90"}""");
        world.Session.GameData.SetGmcp("Char.MaxStats", """{"maxmoves":"100"}""");
        var windowId = world.Open(PanelIds.Vitals);

        var vitals = world.App.ReadVitals(world.Session.SessionKey)!;
        await Assert.That(vitals.Meters.Select(m => m.Colour ?? string.Empty))
            .IsEquivalentTo(new[] { "#4cc38a", "#5aa9e6", "#a98bf0" });
        await Assert.That(vitals.Meters[1].Low).IsTrue();

        // Health's bar is its green held to the floor; mana is low, so its bar is the warning, not its blue.
        var markup = string.Join("\n", world.App.PanelMarkup(windowId));
        var plane = world.App.PanelPlane;
        await Assert.That(markup).Contains(Core.Text.Contrast.Legible(new(0x4c, 0xc3, 0x8a), plane).ToHex());
        await Assert.That(markup).Contains(Core.Text.Contrast.Legible(new(0xa9, 0x8b, 0xf0), plane).ToHex());
        await Assert.That(markup).DoesNotContain(Core.Text.Contrast.Legible(new(0x5a, 0xa9, 0xe6), plane).ToHex());
        await Assert.That(markup).Contains(Core.Text.Contrast.Legible(new(0xff, 0x6b, 0x6b), plane, WorkspacePalette.HeaderTextFloor).ToHex());
    }

    [Test]
    public async Task TheStatusRowsMeterLabelsWearTheirColoursAndClearTheSmallTextFloor()
    {
        var store = new GameDataStore();
        store.SetGmcp("Char.Vitals", """{"hp":"412","maxhp":"500","mp":"23","maxmp":"120"}""");
        var plane = new Core.Text.Rgb(0x1c, 0x1c, 0x22);
        string Hold(string hex) => Core.Text.Contrast.Legible(
            new(Convert.ToByte(hex[1..3], 16), Convert.ToByte(hex[3..5], 16), Convert.ToByte(hex[5..7], 16)), plane, 4.5).ToHex();

        var runs = StatusMeterRenderer.Runs(StatusMeter.Defaults(), store, "#ff0000", "#808080", hold: Hold);

        await Assert.That(runs.Single(r => r.Text == "hp").Ink).IsEqualTo(Hold("#4cc38a"));
        await Assert.That(runs.Single(r => r.Text == "mp").Ink).IsEqualTo("#ff0000");

        // Without a hold (the markup path the tests above read) the labels are dim as they always were.
        var plain = StatusMeterRenderer.Runs(StatusMeter.Defaults(), store, "#ff0000", "#808080");
        await Assert.That(plain.Single(r => r.Text == "hp").Ink).IsEqualTo("#808080");
    }

    [Test]
    public async Task TheStripAndTheCardDrawAMetersColourAndTheTargetsThroughTheHold()
    {
        var ink = VitalsInk with { Hold = hex => hex == "#4cc38a" ? "#111111" : "#222222" };
        var model = new Client.Panels.VitalsModel(
            [new("hp", "412", "500", true, 0.82, false, "#4cc38a"), new("gold", "12", null, false, null, false)],
            new Client.Panels.VitalsTarget("a ghoul", 0.4));

        var strip = VitalsStripRenderer.Render(model, 120, ink);
        await Assert.That(strip).Contains("[#111111]█").And.Contains("[#222222]█");

        var card = string.Join("\n", VitalsStripRenderer.RenderCard(model, 40, 12, ink));
        await Assert.That(card).Contains("[#111111]█").And.Contains("[#222222]█");
    }

    /// <summary>Every registered panel is one a layout can place, and the layout counts the rest as drawing nothing.</summary>
    [Test]
    public async Task ThePanelsThisClientDrawsAreTheOnesItRegistered()
    {
        var world = await Connected();

        foreach (var id in new[] { PanelIds.WhoHere, PanelIds.Vitals, PanelIds.QuickBar, PanelIds.Composer })
        {
            await Assert.That(world.App.Panels.IsRegistered(id)).IsTrue();
        }

        await Assert.That(world.App.Panels.IsRegistered(PanelIds.GameData)).IsFalse();
        await Assert.That(world.App.LayoutAvailability(world.Session.SessionKey).HasData(PanelIds.GameData)).IsFalse();
    }

    [Test]
    public async Task APanelUpdatingMovesNoPaneRectangle()
    {
        var world = await Connected();
        var windowId = world.Open(PanelIds.WhoHere);
        world.App.DispatchCommand("layout:split-right");
        world.App.RenderNextFrame();
        var before = world.App.PaneOutputRects().ToDictionary(p => p.Key, p => p.Value);

        world.Session.Occupants.Apply("Room.Players", """[{"name":"Ann"},{"name":"Bo"},{"name":"Cy"}]""");
        world.App.RenderNextFrame();

        await Assert.That(world.App.PaneOutputRects().ToDictionary(p => p.Key, p => p.Value)).IsEquivalentTo(before);
        await Assert.That(windowId).StartsWith(TerminalPanels.WindowPrefix);
    }

    private static string StripMarkup(string markup) => Plain(markup);

    private sealed record Wired(SharpMUTermApp App, RecordingTelnetSession Telnet, Core.Session.WorldSession Session)
    {
        internal string Open(string panelId)
        {
            App.DispatchCommand("panel:" + panelId);
            App.RenderNextFrame();
            App.RenderNextFrame();
            return TerminalPanels.WindowId(panelId, Session.SessionKey);
        }
    }

    private static async Task<Wired> Connected()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        await session.ConnectAsync();
        app.RenderNextFrame();
        return new Wired(app, telnet, session);
    }

    /// <summary>
    /// The idle words go before a name is cut: at 80 columns who-here cut every name to nine cells to keep
    /// <c>active</c>/<c>idle 4m</c> (UX audit wave 11), when the dot already says which.
    /// </summary>
    [Test]
    public async Task WhoHereDropsTheIdleColumnBeforeCuttingAName()
    {
        RoomOccupant[] here = [new("Ashlyn", "Ashlyn the Wanderer", true, 30), new("Bran", "Brannoc", true, 600)];

        var narrow = WhoHereRenderer.Render("Plaza", here, 22, 6, new WhoHereRenderer.Ink("#00f5b7")).Select(StripMarkup).ToList();
        await Assert.That(narrow[1]).Contains("Ashlyn the Wanderer").And.DoesNotContain("active");
        await Assert.That(narrow.All(r => r.Length <= 22)).IsTrue();

        var wide = WhoHereRenderer.Render("Plaza", here, 40, 6, new WhoHereRenderer.Ink("#00f5b7")).Select(StripMarkup).ToList();
        await Assert.That(wide[1]).Contains("Ashlyn the Wanderer").And.Contains("active");
    }
}
