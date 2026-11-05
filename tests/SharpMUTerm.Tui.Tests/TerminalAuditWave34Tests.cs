using System.Text.RegularExpressions;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Wave 34's terminal completion audit, read off painted frames: the older-history view draws each line on one row (its
/// body was laid out at the width the scroll bar takes a column of, so every line that filled it wrapped), a web page is
/// laid out at its pane's width rather than the screen's (wrapped twice, a word left alone on every other row), every
/// settings screen's title sits one cell in, and a cut word is never followed by a space before its mark.
/// </summary>
[NotInParallel]
public partial class TerminalAuditWave34Tests
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

    [GeneratedRegex(@"\[\d\d:\d\d\]")]
    private static partial Regex Stamp();

    [Test]
    [Arguments(80, 24)]
    [Arguments(120, 32)]
    public async Task EveryLineOfTheHistoryViewIsOneRow(int width, int height)
    {
        await using var app = App(width, height);
        var rows = FrameGrid.Decode(app.RenderSnapshot("search-history"), width, height);
        var page = app.HistoryViewModel!;
        var frame = string.Join('\n', rows);

        await Assert.That(rows.Count(r => Stamp().IsMatch(r))).IsEqualTo(page.Lines.Count).Because(frame);
        await Assert.That(rows.Any(r => r.Contains(page.Position, StringComparison.Ordinal))).IsTrue()
            .Because("the position is never wrapped onto a second row:\n" + frame);
        await Assert.That(rows.Any(r => r.Contains('▲') || r.Contains('▼'))).IsFalse()
            .Because("a page that fits shows no scroll bar:\n" + frame);
    }

    [Test]
    public async Task AWebPageIsLaidOutAtItsPanesWidth()
    {
        await using var app = App(80, 24);
        var rows = FrameGrid.Decode(app.RenderSnapshot("web"), 80, 24);

        await Assert.That(rows.Any(r => r.Trim() == "in")).IsFalse().Because(string.Join('\n', rows));
        await Assert.That(rows.Any(r => r.Contains("in the corner", StringComparison.Ordinal))).IsTrue()
            .Because(string.Join('\n', rows));
    }

    [Test]
    [Arguments("worlds", "Worlds & Characters")]
    [Arguments("triggers", "Triggers & spawn routing")]
    [Arguments("aliases", "Aliases")]
    [Arguments("timers", "Timers")]
    [Arguments("keypad", "Keypad & hotkeys")]
    [Arguments("textansi", "Text & display")]
    [Arguments("mssp", "Server information")]
    public async Task EverySettingsScreensTitleIsOneCellIn(string view, string title)
    {
        await using var app = App(120, 32);
        var rows = FrameGrid.Decode(app.RenderSnapshot(view), 120, 32);

        await Assert.That(rows[0]).StartsWith(" " + title);
    }

    [Test]
    public async Task ACutWordIsNotFollowedByASpaceBeforeItsMark()
    {
        await Assert.That(MarkupText.ElidePlain("Viewing · Aetherfall · level 1", 22)).IsEqualTo("Viewing · Aetherfall…");
        await Assert.That(MarkupText.ElidePlain("short", 22)).IsEqualTo("short");
        await Assert.That(MarkupText.ElidePlain("anything", 1)).IsEqualTo("…");
        await Assert.That(MarkupText.ElidePlain("anything", 0)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task TheDeletionsQuestionKeepsACellFromItsFrame()
    {
        await using var app = App(80, 24);
        var rows = FrameGrid.Decode(app.RenderSnapshot("deletions"), 80, 24);

        await Assert.That(rows.Any(r => r.Contains("│ Keep these deletions?", StringComparison.Ordinal))).IsTrue()
            .Because(string.Join('\n', rows));
    }
}
