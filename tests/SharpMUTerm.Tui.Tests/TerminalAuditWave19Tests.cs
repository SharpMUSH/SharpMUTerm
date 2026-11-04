using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>Wave 19's terminal audit: the cell defects found by sweeping every snapshot view at 80×24 and 120×32.</summary>
[NotInParallel]
public class TerminalAuditWave19Tests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App(int width, int height)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ThemeName = "Dark";
        config.Theme = ThemeLibrary.Get("Dark");
        return new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(width, height));
    }

    [Test]
    public async Task ANoticeThatAlreadyNamesItsKeyIsNotFollowedByASecondCopy()
    {
        await using var app = App(120, 32);
        app.SimulateNotice("switched to Corvid · offline — ⌥R (⌃P ▸ Reconnect) connects it", MessageSeverity.Info, null,
            new NoticeAction("world:reconnect", "Reconnect"));
        var row = MarkupText.Plain(app.StatusMarkup);
        await Assert.That(row).DoesNotContain("· ⌥R Reconnect");
        await Assert.That(row.Split("⌥R").Length - 1).IsEqualTo(1);
    }

    [Test]
    public async Task TheFreezeBarsRuleRunsToThePanesEdge()
    {
        foreach (var width in new[] { 30, 72, 140 })
        {
            await Assert.That(MarkupText.Plain(FreezeBarRenderer.Bar("#c678dd", width)).Length).IsEqualTo(width);
        }

        await Assert.That(MarkupText.Plain(FreezeBarRenderer.Bar("#c678dd")).Length).IsEqualTo(12 + BarRule.Fallback);
    }

    [Test]
    public async Task TheHeaderSpellsTheProductsNameWhereItFitsAndNeverWraps()
    {
        await using var wide = App(120, 32);
        await Assert.That(MarkupText.Plain(wide.HeaderText)).Contains(" SharpMUTerm ");

        await using var narrow = App(44, 16);
        var plain = MarkupText.Plain(narrow.HeaderText);
        await Assert.That(plain).Contains(" muterm ");
        await Assert.That(plain.Length).IsLessThanOrEqualTo(44);
    }

    [Test]
    public async Task TheWelcomesChoiceIsABandAcrossTheList()
    {
        var model = new WelcomeSurface(null).Model;
        var lines = WelcomeSurface.Render(model, 60);
        var band = lines.Single(l => l.Contains($"[on {ScreenPalette.MenuSelectedBg}]", StringComparison.Ordinal));
        await Assert.That(MarkupText.Plain(band)).Contains(model.Describe(model.Selected).Title);
        await Assert.That(MarkupText.Plain(band).Length).IsEqualTo(60);
    }
}
