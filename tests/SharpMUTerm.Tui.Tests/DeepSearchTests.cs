using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// ⌃F's older-history step (⌥H): the session's scrollback behind the pane is searched, its hits listed after the pane's
/// as "older", ⏎ on one opens the read-only history view on a page around it (or goes to the pane while it still holds
/// the line), and a capture window, which keeps no older history, says so.
/// </summary>
[NotInParallel]
public class DeepSearchTests
{
    private const string Ann = "Convergence.Ann";

    private static readonly ConsoleKeyInfo AltH = new('\0', ConsoleKey.H, false, true, false);
    private static readonly ConsoleKeyInfo Enter = new('\r', ConsoleKey.Enter, false, false, false);
    private static readonly ConsoleKeyInfo Down = new('\0', ConsoleKey.DownArrow, false, false, false);

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>A connected Ann whose pane holds the newest 40 lines of 400, with goblins at lines 10 and 395.</summary>
    private static async Task<SharpMUTermApp> Live(int paneLines = 40)
    {
        var config = new AppConfiguration { PaneLines = paneLines };
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Convergence",
            Host = "convergence.example",
            Port = 4201,
            Characters = { new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() } },
        });
        Console.SetIn(TextReader.Null);
        var wire = new RecordingTelnetSession();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        app.TelnetFactory = _ => wire;
        app.DispatchCommand(CommandIds.Character(Ann));
        await app.FindSession(Ann)!.ConnectAsync();
        for (var i = 0; i < 400; i++)
        {
            wire.Receive(i is 10 or 395 ? $"a goblin, line {i}" : $"street {i}");
        }

        app.RenderNextFrame();
        return app;
    }

    [Test]
    public async Task WithoutAltHTheBoundSaysOlderHistoryCanBeSearched()
    {
        await using var app = await Live();

        var model = app.SearchModelFor("goblin", false, false);

        await Assert.That(model.Rows.Count).IsEqualTo(1);
        await Assert.That(model.Rows[0].IsOlder).IsFalse();
        await Assert.That(model.HistoryNote).Contains("⌥H searches older history");
        await Assert.That(model.OffersOlder).IsTrue();
        await Assert.That(model.OlderLines).IsGreaterThan(300).Because("400 lines arrived and the pane keeps about 40");
    }

    [Test]
    public async Task NothingIsOfferedWhileThePaneHoldsEveryLine()
    {
        await using var app = await Live(paneLines: 2_000);

        var model = app.SearchModelFor("goblin", false, false);

        await Assert.That(model.Rows.Count).IsEqualTo(2).Because("both goblins are in the pane");
        await Assert.That(model.OffersOlder).IsFalse();
        await Assert.That(model.HistoryNote).IsNull();
    }

    [Test]
    public async Task AltHListsTheScrollbacksHitsAfterThePanesAndNeverTwice()
    {
        await using var app = await Live();

        var model = app.SearchModelFor("goblin", false, false, older: true);

        // The pane's own hit (line 395) once, then line 10 from the scrollback behind it.
        await Assert.That(model.Rows.Count).IsEqualTo(2);
        await Assert.That(model.Rows[0].IsOlder).IsFalse();
        await Assert.That(model.Rows[1].IsOlder).IsTrue();
        await Assert.That(model.Rows[1].Text).IsEqualTo("a goblin, line 10");
        await Assert.That(model.HeldRows).IsEqualTo(1);
        await Assert.That(model.HistoryRunning).IsFalse();
        await Assert.That(model.HistoryNote).Contains("1 found");
        await Assert.That(model.StateText).Contains("older history");
    }

    [Test]
    public async Task EnterOnAnOlderHitOpensTheHistoryViewAroundIt()
    {
        await using var app = await Live();
        app.OpenSearchForSnapshot();
        app.SimulateSearchTyping("goblin");
        app.SimulateSearchKey(AltH);
        await Assert.That(app.SearchRows.Count).IsEqualTo(2);

        app.SimulateSearchKey(Down);
        app.SimulateSearchKey(Enter);

        await Assert.That(app.SearchIsOpen).IsFalse();
        await Assert.That(app.HistoryViewIsOpen).IsTrue();
        var page = app.HistoryViewModel!;
        await Assert.That(page.Lines.Count).IsEqualTo(ScrollbackViewModel.PageRows);
        await Assert.That(page.Lines.Any(l => l.Index == page.Marked && l.Text == "a goblin, line 10")).IsTrue();
        await Assert.That(page.Title).Contains("read only");

        // A move is a ranged read from the scrollback, never the pane.
        app.ScrollHistoryView(page.Top + 5);
        await Assert.That(app.HistoryViewModel!.Top).IsEqualTo(page.Top + 5);
        await Assert.That(app.HistoryViewModel!.Lines[0].Text).IsEqualTo(app.FindSession(Ann)!.Scrollback.GetRange(page.Top + 5, 1)[0].Text);
    }

    [Test]
    public async Task AnOlderHitThePaneStillHoldsGoesToThePane()
    {
        await using var app = await Live();
        var model = app.SearchModelFor("goblin", false, false, older: true);
        var held = model.Rows[0];

        // The same line, as if it had been found in the scrollback: ⏎ goes to the pane, not the history view.
        var session = app.FindSession(Ann)!;
        var index = session.Scrollback.TotalLines - 5;
        var asOlder = held with { LineIndex = -1, History = index, SessionKey = Ann };
        var rows = new List<SearchRow>(model.Rows) { asOlder };
        app.GoToSearchHitForTest(model with { Rows = rows }, rows.Count - 1);

        await Assert.That(app.HistoryViewIsOpen).IsFalse();
    }

    [Test]
    public async Task TheDemosCaptureWindowSaysItKeepsNoOlderHistory()
    {
        Console.SetIn(TextReader.Null);
        await using var app = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(120, 32));
        app.RenderSnapshot();
        app.SimulateWindowChange(DemoScene.ChatWindowId);

        var model = app.SearchModelFor("goblin", false, false, older: true);

        await Assert.That(model.HistoryNote).Contains("keeps no older history");
        await Assert.That(model.Rows.Any(r => r.IsOlder)).IsFalse();
    }
}
