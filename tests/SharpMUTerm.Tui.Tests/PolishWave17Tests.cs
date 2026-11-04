using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Help;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The wave-17 polish read off frames: a notice's action named on the status row where it fits, F5's world column at
/// 80×24 showing a page of fields rather than one, and a help page's header on one row with its keys drawn as keys.
/// </summary>
[NotInParallel]
public class PolishWave17Tests
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
    public async Task TheStatusRowNamesANoticesActionWhereItFits()
    {
        await using var wide = App(120, 32);
        wide.SimulateNotice("could not connect to aetherfall.mux:4201 — no route to host", MessageSeverity.Error, null,
            new NoticeAction("term:messages", "Show messages"));
        await Assert.That(MarkupText.Plain(wide.StatusMarkup)).EndsWith("· ⌃P ▸ Show client messages");

        // At 80 the palette's title does not fit beside the words; the action's own, shorter label does.
        await using var narrow = App(80, 24);
        narrow.SimulateNotice("could not connect to aetherfall.mux:4201 — no route to host", MessageSeverity.Error, null,
            new NoticeAction("term:messages", "Show messages"));
        var row = MarkupText.Plain(narrow.StatusMarkup);
        await Assert.That(row).EndsWith("· ⌃P ▸ Show messages");
        await Assert.That(row.Length).IsLessThanOrEqualTo(80);
        await Assert.That(row).DoesNotContain("…").Because("the hint is added whole or not at all; the words are never cut for it");
    }

    [Test]
    public async Task AnActionWithAKeyIsNamedByItsKey_AndOneThePaletteDoesNotListIsNotNamed()
    {
        await using var app = App(120, 32);
        await Assert.That(app.NoticeActionHints(new NoticeAction("world:reconnect", "Reconnect"))).IsEquivalentTo(new[] { "⌥R Reconnect" });
        await Assert.That(app.NoticeActionHints(new NoticeAction("scene:open:0", "Open"))).IsEmpty();

        app.SimulateNotice("a notice too long to leave room for anything beside it at all, said at length, on and on and on and on",
            MessageSeverity.Error, null, new NoticeAction("term:messages", "Show messages"));
        await Assert.That(MarkupText.Plain(app.StatusMarkup)).DoesNotContain("⌃P ▸");
    }

    [Test]
    public async Task F5At80x24ShowsAPageOfWorldFieldsUnderAnInlineGroupLabel()
    {
        await using var app = App(80, 24);
        var rows = FrameGrid.Decode(app.RenderSnapshot("settings"), 80, 24);
        string[] labels = ["name", "host", "port", "security", "encoding", "keepalive", "reconnect", "content", "gmcp modules"];
        var shown = labels.Count(l => rows.Any(r => r.Contains($" {l}  ", StringComparison.Ordinal)));

        await Assert.That(shown).IsGreaterThanOrEqualTo(8).Because(string.Join('\n', rows));
        await Assert.That(rows.Any(r => r.Contains("├ WORLD · CONNECTION", StringComparison.Ordinal))).IsTrue()
            .Because("the heading the short column gave up is named on its top row");
        await Assert.That(rows.Any(r => r.Contains("more", StringComparison.Ordinal) && r.Contains('⌄'))).IsTrue()
            .Because("the character form held back says what it holds back");
    }

    [Test]
    public async Task AHelpPagesHeaderIsOneRowAndItsKeysAreDrawnAsKeys()
    {
        var model = HelpModel.Build([], "⌥⏎", []);
        var lines = HelpSurface.Render(model, string.Empty, 0, "reading", 0, 70, 20);

        await Assert.That(MarkupText.VisibleLength(lines[0])).IsLessThanOrEqualTo(69);
        await Assert.That(lines.Any(l => l.Contains($"[bold {ScreenPalette.Value}]Search[/]", StringComparison.Ordinal))).IsTrue();
        await Assert.That(lines.Any(l => l.Contains($"[bold {ScreenPalette.Accent}]⌃F[/]", StringComparison.Ordinal))).IsTrue();
    }
}
