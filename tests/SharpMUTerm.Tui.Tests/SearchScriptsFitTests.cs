using System.Globalization;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Wave 18B, read off painted frames: the older-history view fits 80×24 above the command line with its keys pinned; ⌃F
/// says both bounds on one row; a console error row is a link that opens its line in the editor; the console's log is
/// not drawn under blank padding; and the demo's history and script error agree with themselves.
/// </summary>
[NotInParallel]
public class SearchScriptsFitTests
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
    public async Task TheOlderHistoryViewEndsAboveTheCommandLineAt80x24()
    {
        await using var app = App(80, 24);
        var rows = FrameGrid.Decode(app.RenderSnapshot("search-history"), 80, 24);
        var page = app.HistoryViewModel!;

        await Assert.That(page.PageRows).IsEqualTo(ScrollbackViewModel.PageRowsFor(24, 80));
        await Assert.That(page.Lines.Count).IsEqualTo(page.PageRows);
        var prompt = rows.ToList().FindIndex(r => r.Contains("Corvid · Aetherfall ›", StringComparison.Ordinal));
        await Assert.That(prompt).IsGreaterThan(0).Because("the command line is not under the view's border:\n" + string.Join('\n', rows));
        var bottom = rows.ToList().FindLastIndex(r => r.Contains('└'));
        await Assert.That(bottom).IsLessThan(prompt);
        await Assert.That(rows.Any(r => r.Contains(page.Position, StringComparison.Ordinal))).IsTrue()
            .Because("the position has a row of its own below 100 columns");
    }

    [Test]
    public async Task CtrlFsOlderCountsAreOneRowAndItsRowsSayWhereTheyAre()
    {
        await using var app = App(80, 24);
        var rows = FrameGrid.Decode(app.RenderSnapshot("search-older"), 80, 24);

        await Assert.That(rows.Count(r => r.Contains(" of ", StringComparison.Ordinal) && r.Contains("older", StringComparison.Ordinal) && r.Contains("this window", StringComparison.Ordinal)))
            .IsEqualTo(1).Because(string.Join('\n', rows));
        await Assert.That(rows.Any(r => r.Contains("found in", StringComparison.Ordinal))).IsFalse().Because("the second count is gone");
        await Assert.That(rows.Count(r => r.Contains(" here ", StringComparison.Ordinal))).IsEqualTo(3);
        await Assert.That(rows.Count(r => r.Contains("lines ago", StringComparison.Ordinal))).IsEqualTo(4);
    }

    [Test]
    public async Task TheDemosHistoryOnlyMovesForwardAndEveryLineIsStamped()
    {
        var buffer = SharpMUTermApp.DemoHistory();
        var lines = new List<SharpMUTerm.Core.Text.StyledLine>();
        for (var at = buffer.OldestIndex; at < buffer.TotalLines; at += 2_000)
        {
            lines.AddRange(buffer.GetRange(at, (int)Math.Min(2_000, buffer.TotalLines - at)));
        }

        await Assert.That(lines.Count).IsEqualTo((int)buffer.AvailableLines);
        var previous = string.Empty;
        foreach (var line in lines)
        {
            await Assert.That(line.Text.Length > 8 && line.Text[0] == '[' && line.Text[6] == ']').IsTrue().Because(line.Text);
            var stamp = line.Text[1..6];
            await Assert.That(string.CompareOrdinal(stamp, previous)).IsGreaterThanOrEqualTo(0).Because($"{previous} then {stamp}");
            previous = stamp;
        }

        await Assert.That(lines.Any(l => l.Text.Contains("goblin came back", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task TheDemoConsoleAndTheEditorShowTheOneError()
    {
        await using var console = App(120, 32);
        console.RenderSnapshot("script-console");
        await Assert.That(console.ConsoleTexts("demo", ScriptConsoleKind.Error)).Contains(SharpMUTermApp.DemoScriptError);

        await using var editor = App(120, 32);
        editor.RenderSnapshot("script-edit");
        await Assert.That(editor.Composer.Model.Edit!.Status).IsEqualTo("! " + SharpMUTermApp.DemoScriptError);
        await Assert.That(editor.Composer.Model.Edit!.ErrorLine).IsEqualTo(SharpMUTerm.Client.Composer.ScriptErrors.At(SharpMUTermApp.DemoScriptError)!.Value.Line);
    }

    [Test]
    public async Task AConsoleErrorRowIsALinkThatOpensItsLineInTheEditor()
    {
        await using var app = App(120, 32);
        app.RenderSnapshot("script-console");
        var model = app.CellConsole!.Model;
        var index = model.Lines.ToList().FindIndex(l => l.Text == SharpMUTermApp.DemoScriptError);
        var link = ScriptConsoleModel.ErrorLinkPrefix + index.ToString(CultureInfo.InvariantCulture);

        await Assert.That(app.ConsoleLines.Any(l => l.Contains($"[link={link}]", StringComparison.Ordinal))).IsTrue()
            .Because("the error row carries the console's own link");
        await Assert.That(app.ConsoleLines.Where(l => l.Contains("[link=", StringComparison.Ordinal)).All(l => l.Contains($"[link={link}]", StringComparison.Ordinal)))
            .IsTrue().Because("only the error naming a line is a link — not the limit's error, not what a script printed");

        app.SimulateConsoleLink(link);

        await Assert.That(app.ScriptConsoleIsOpen).IsFalse();
        await Assert.That(app.OpenScriptEdit?.Name).IsEqualTo("corvid.lua");
        await Assert.That(app.Composer.Model.Edit!.Line).IsEqualTo(14);
        await Assert.That(app.Composer.Model.Edit!.ErrorLine).IsEqualTo(14);
    }

    [Test]
    public async Task ALinkNotNamingAnErrorRowOpensNothing()
    {
        await using var app = App(120, 32);
        app.RenderSnapshot("script-console");

        app.SimulateConsoleLink(ScriptConsoleModel.ErrorLinkPrefix + "0");
        app.SimulateConsoleLink("mux:send:@shutdown");

        await Assert.That(app.ScriptConsoleIsOpen).IsTrue();
        await Assert.That(app.OpenScriptEdit).IsNull();
    }

    [Test]
    [Arguments(80, 24)]
    [Arguments(120, 32)]
    public async Task TheConsolesLogIsNotDrawnUnderBlankPadding(int width, int height)
    {
        await using var app = App(width, height);
        app.RenderSnapshot("script-console");
        var lines = app.ConsoleLines;

        // Title, the blank under it, then the log: its first line is the oldest console line.
        await Assert.That(lines[2]).Contains("loaded corvid.lua").Because(string.Join('\n', lines));
    }
}
