using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Wave 18, read off painted frames: the cell script editor marks the line the reload's error named (the model's
/// <c>ErrorLine</c>, which the window marks too); the history view opens centred on its hit with its context drawn apart;
/// ⌃F offers ⌥H on its status line while the pane cap trimmed what it searched; and F5's short column names the group of
/// the first field it shows after every scroll step.
/// </summary>
[NotInParallel]
public class SearchAndScriptWaveTests
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
    public async Task TheCellEditorMarksTheLineTheReloadsErrorNamed()
    {
        await using var app = App(120, 32);
        var frame = app.RenderSnapshot("script-edit");

        await Assert.That(app.Composer.Model.Edit!.ErrorLine).IsEqualTo(14);

        var rows = FrameGrid.Decode(frame, 120, 32);
        var row = rows.ToList().FindIndex(r => r.Contains("local room = map.current()", StringComparison.Ordinal));
        await Assert.That(row).IsGreaterThan(0).Because(string.Join('\n', rows));
        var column = rows[row].IndexOf("local room", StringComparison.Ordinal);
        var backgrounds = FrameGrid.Backgrounds(frame);
        var band = FrameGrid.Sgr(ComposeOverlay.ErrorBand(ScreenTheme.Dark));
        await Assert.That(backgrounds[(row, column)]).IsEqualTo(band).Because("line 14 is on the error band");

        var other = rows.ToList().FindIndex(r => r.Contains("function on_room()", StringComparison.Ordinal));
        await Assert.That(backgrounds[(other, rows[other].IndexOf("function", StringComparison.Ordinal))]).IsNotEqualTo(band)
            .Because("and only line 14");
    }

    [Test]
    public async Task ASaveThatLoadsCleanlyClearsTheMarkAndAnErrorElsewhereDoesNotSetIt()
    {
        var edit = new ScriptEdit("corvid.lua", null, false, ErrorLine: 14);

        await Assert.That(edit.Said("saved 12:00:00 · reloaded · 2 files loaded").ErrorLine).IsEqualTo(0);
        await Assert.That(edit.Said("! main.lua:(3,1): unexpected symbol").ErrorLine).IsEqualTo(0);
        await Assert.That(edit.Said("! corvid.lua:(6,2): unexpected symbol").ErrorLine).IsEqualTo(6);
        await Assert.That(edit.Said("! corvid.lua:(6,2): x").Describe().ErrorLine).IsEqualTo(6).Because("both hosts read Describe");
    }

    [Test]
    public async Task TheHistoryViewOpensCentredOnItsHitWithItsContextDrawnApart()
    {
        await using var app = App(120, 32);
        var frame = app.RenderSnapshot("search-history");
        var page = app.HistoryViewModel!;

        await Assert.That(page.Marked - page.Top).IsEqualTo(ScrollbackViewModel.PageRows / 2);
        var rows = FrameGrid.Decode(frame, 120, 32);
        var context = rows.Count(r => r.Contains("│ ", StringComparison.Ordinal) && r.Contains("] ", StringComparison.Ordinal));
        await Assert.That(context).IsGreaterThanOrEqualTo(2 * ScrollbackViewModel.ContextRows).Because(string.Join('\n', rows));
        await Assert.That(rows.Any(r => r.Contains("▸ ", StringComparison.Ordinal) && r.Contains("goblin", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    [Arguments(80, 24)]
    [Arguments(120, 32)]
    public async Task CtrlFOffersOlderHistoryOnItsStatusLineWhileThePaneCapTrimmedIt(int width, int height)
    {
        await using var app = App(width, height);
        var rows = FrameGrid.Decode(app.RenderSnapshot("search"), width, height);

        var status = rows.FirstOrDefault(r => r.Contains("lines held", StringComparison.Ordinal));
        await Assert.That(status).IsNotNull().Because(string.Join('\n', rows));
        await Assert.That(status!).Contains("⌥H").And.Contains("older history");
        await Assert.That(status!.Contains('…')).IsFalse().Because("a rung is chosen whole, never cut");
    }

    [Test]
    public async Task F5sShortColumnNamesTheFirstShownFieldsGroupAfterEveryScrollStep()
    {
        await using var app = App(80, 24);
        app.RenderSnapshot("worlds");
        app.SimulateSettingsKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        var groups = BuiltInSettings.World.Fields.Where(f => f.Group is not null)
            .ToDictionary(f => f.Label, f => f.Group!.ToUpperInvariant(), StringComparer.Ordinal);
        var steps = 0;
        var scrolled = 0;
        for (var step = 0; step < 22; step++)
        {
            var rows = FrameGrid.Decode(app.RenderWholeFrame(), 80, 24);
            var top = rows.ToList().FindIndex(r => r.Contains("├ WORLD", StringComparison.Ordinal) || r.Contains("⌃ ", StringComparison.Ordinal));
            if (top >= 0 && top + 1 < rows.Count)
            {
                var right = rows[top + 1][32..];
                var label = right.Trim().Split("  ")[0];
                var group = label is "security" || right.TrimStart().StartsWith('[') ? "CONNECTION" : groups.GetValueOrDefault(label);
                if (group is not null && !rows[top].Contains("▾", StringComparison.Ordinal))
                {
                    steps++;
                    scrolled += rows[top].Contains("⌃ ", StringComparison.Ordinal) ? 1 : 0;
                    await Assert.That(rows[top]).Contains($"· {group}").Because($"step {step}: first shown '{label}'\n{string.Join('\n', rows)}");
                }
            }

            app.SimulateSettingsKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false));
        }

        await Assert.That(steps).IsGreaterThan(15);
        await Assert.That(scrolled).IsGreaterThan(10).Because("the walk scrolled the column");
    }
}
