using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Wave 18's ⌃P, F5 and tab-strip fixes, read off real frames: the script entries' group, the five-letter match,
/// Recent alerts, the palette leaving the command line in view at 80×24, F5 eliding at 80 columns and the tab floor.
/// </summary>
[NotInParallel]
public class Wave18PaletteAndScreenTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp Demo(int width = 120, int height = 32)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(new HeadlessConsoleDriver(width, height), Caps));
    }

    [Test]
    public async Task TheScriptEntriesAreUnderHelpAndSetupNotConnection()
    {
        await using var app = Demo();
        app.RenderSnapshot("script-list");
        var catalog = app.BuildCatalog();

        var console = catalog.Single(i => i.Id == BuiltInCommands.ScriptConsoleCommand);
        await Assert.That(console.Group).IsEqualTo(CommandGroup.Help);
        var files = catalog.Where(i => i.Id.StartsWith(BuiltInCommands.ScriptOpenPrefix, StringComparison.Ordinal)).ToList();
        await Assert.That(files.Count).IsGreaterThan(0);
        await Assert.That(files.All(f => f.Group == CommandGroup.Help)).IsTrue();

        // Listed together or not at all (D7c): the files are listed here, so New script… is too.
        await Assert.That(catalog.Select(i => i.Id)).Contains(BuiltInCommands.ScriptNewCommand);
        await Assert.That(catalog.Select(i => i.Id)).Contains(BuiltInCommands.ScriptEditCommand);
    }

    [Test]
    public async Task SceneListsOnlyTheSceneEntries()
    {
        await using var app = Demo();
        app.RenderSnapshot(null);
        var view = CommandPaletteModel.Build("scene", app.BuildCatalog());

        await Assert.That(view.Entries.Count).IsGreaterThan(0);
        await Assert.That(view.Entries.All(e => e.Item.Title.Contains("scene", StringComparison.OrdinalIgnoreCase)
            || (e.Item.Detail ?? e.Item.Subtitle ?? string.Empty).Contains("scene", StringComparison.OrdinalIgnoreCase))).IsTrue()
            .Because(string.Join(", ", view.Entries.Select(e => e.Item.Title)));
    }

    [Test]
    public async Task RecentAlertsListsEveryCharactersAlertsAndGoesToTheirWindow()
    {
        await using var app = Demo();
        var frame = string.Join("\n", FrameGrid.Decode(app.RenderSnapshot("menu-alerts"), 120, 32));

        await Assert.That(app.RecentAlerts.Count).IsEqualTo(3);
        await Assert.That(app.RecentAlerts[0].Text).Contains("north gate").Because("newest first");
        await Assert.That(frame).Contains("Alert · Corvid · Aetherfall: Rivane pages");
        await Assert.That(frame).Contains("Alert · Thistle · Grapevine");
        await Assert.That(frame).Contains("14 min ago");

        var corvid = app.RecentAlerts[0];
        await Assert.That(app.DispatchCommand(BuiltInCommands.Alert(corvid.Id))).IsTrue();
        await Assert.That(app.DispatchCommand(BuiltInCommands.Alert(9999))).IsFalse().Because("an alert no longer kept refuses");
    }

    [Test]
    public async Task TheAlertEntriesAreListedOnlyInThePaletteRecentAlertsOpened()
    {
        await using var app = Demo();
        app.RenderSnapshot(null);
        await Assert.That(app.BuildCatalog().Any(i => i.Id == BuiltInCommands.RecentAlertsCommand)).IsFalse().Because("none kept yet");

        app.SimulateAlert(DemoScene.ActiveSessionKey, AttentionReason.Page, "Rivane pages: hello");
        var catalog = app.BuildCatalog();
        await Assert.That(catalog.Single(i => i.Id == BuiltInCommands.RecentAlertsCommand).Title).IsEqualTo("Recent alerts (1)");
        await Assert.That(catalog.Any(i => i.Id.StartsWith(BuiltInCommands.AlertPrefix, StringComparison.Ordinal))).IsFalse()
            .Because("a plain ⌃P is not crowded with them");
    }

    [Test]
    public async Task ThePaletteLeavesTheCommandLineInViewAt80By24()
    {
        await using var app = Demo(80, 24);
        var rows = FrameGrid.Decode(app.RenderSnapshot("menu"), 80, 24);

        await Assert.That(rows[22]).StartsWith("say hello there").Because("the command line, holding the demo's draft: " + rows[22]);
        await Assert.That(rows.Take(22).Any(r => r.Contains('└'))).IsTrue().Because("the palette's bottom border is above it");
    }

    [Test]
    public async Task F5ElidesItsValuesAt80Columns()
    {
        await using var app = Demo(80, 24);
        var rows = FrameGrid.Decode(app.RenderSnapshot("worlds"), 80, 24);

        await Assert.That(rows.Any(r => r.TrimEnd().EndsWith("encrypt this conn…", StringComparison.Ordinal))).IsTrue()
            .Because(string.Join("\n", rows));
        await Assert.That(rows.Any(r => r.Contains("channel + page …", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task NoOverflowedTabIsCutBelowThreeLetters()
    {
        await using var app = Demo(80, 24);
        var rows = FrameGrid.Decode(app.RenderSnapshot("tabs-many"), 80, 24);
        var strip = rows.First(r => r.Contains('▾'));

        foreach (System.Text.RegularExpressions.Match cut in System.Text.RegularExpressions.Regex.Matches(strip, @"(\S+)…"))
        {
            await Assert.That(cut.Groups[1].Value.Length).IsGreaterThanOrEqualTo(3).Because(strip);
        }

        // Nothing left of a clipped tab beside the chip: the cell before " +N ▾" is the strip's own fill or a tab's end.
        var chip = strip.IndexOf(" +", StringComparison.Ordinal);
        await Assert.That(strip[..chip].TrimEnd()).DoesNotEndWith(" R");
    }
}
