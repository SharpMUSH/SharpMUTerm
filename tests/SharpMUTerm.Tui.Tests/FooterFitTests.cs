using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <b>Every cell modal and footer fits 80×24</b>: each view below is rendered at the default terminal size and its key
/// row is read off the painted frame. A row's first and last hints must both be on screen and on <em>one</em> row — a
/// footer that wrapped puts its last hint (almost always the way out) on the row under it, and a footer that scrolled
/// with its body is not on screen at all. Both happened: the import review at 80×24 scrolled its Enter/Esc away, and
/// F1's footer wrapped "(keeps the draft)" onto a second row. Read off the frame, never the renderer: the renderers
/// were internally consistent both times.
/// </summary>
[NotInParallel]
public class FooterFitTests
{
    private const int Width = 80;

    private const int Height = 24;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>
    /// A view and the hints that must share one painted row: the row's first key and its last (its way out, or the
    /// key that accepts). Add a view here when it gains a key row.
    /// </summary>
    internal static readonly (string View, string[] OneRow)[] Footers =
    [
        ("import-review", ["⏎ import", "↑↓ scroll", "Esc cancel"]),
        ("compose", ["⌃S send", "⌥W", "Esc close"]),
        ("compose-literal", ["⌃S send", "Esc close"]),
        ("moo-edit", ["⌃S save", "Esc close"]),
        ("script-console", ["⏎ runs", "Esc closes"]),
        ("map-edit", ["←↑→↓ select", "? all keys", "Esc done"]),
        ("help", ["type to search", "Esc closes"]),
        ("help-topic", ["↑↓ PgUp PgDn scroll", "Esc back to the list"]),
        ("dictionary-offer", ["⏎ download", "Esc not now"]),
        ("browse-worlds", ["↑↓ pick", "Esc closes"]),
        ("browse-worlds-failed", ["↑↓ pick", "Esc closes"]),
        ("browse-worlds-picked", ["↑↓ field", "Esc back"]),
        ("browse-worlds-endpoints", ["↑↓ pick", "Esc back"]),
        ("quit", ["⏎ stay", "Esc stays"]),
        ("search", ["↑↓ pick", "⌃F closes"]),
        ("search", ["⌥H searches older history", "lines held"]),
        ("history-search", ["type to filter", "⌃R closes"]),
        ("layouts", ["↑↓ pick", "[⏎] Apply"]),
        ("welcome", ["↑↓ pick", "Esc closes"]),
        ("welcome-connect", ["↑↓ field", "Esc back"]),
        ("tour", ["Keys:", "⌃P", "Key tour"]),
        ("packages", ["installed", "Space", "Esc close"]),
        ("package-review", ["⏎ install", "Esc cancel"]),
        ("package-browse", ["↑↓ select", "Esc close"]),
        ("messages", ["+/- level", "Esc close"]),
        ("prefix-panel", ["Esc cancels", "any other key disarms"]),
        ("settings", ["[Esc] Close", "[⏎] Edit"]),
        ("triggers", ["[Esc] Close", "[⏎] Edit"]),
        ("keypad", ["[Esc] Close", "[⏎] Edit"]),
        ("trigger-builder", ["[Esc] Revert", "[⏎] Commit"]),
        ("search-older", ["↑↓ pick", "⌃F closes"]),
        ("search-older", ["this window 3 of 39", "older 4 of 6,000"]),
        ("search-history", ["↑↓ a line", "Esc closes"]),
        ("search-history", ["OLDER HISTORY · Corvid · read only"]),
        ("script-edit", ["⌃S save and reload", "Esc close"]),
        ("script-list", ["Script corvid.lua", "line 14 in the console"]),
    ];

    public static IEnumerable<Func<(string View, string Hints)>> Cases() =>
        Footers.Select(f => (Func<(string, string)>)(() => (f.View, string.Join(" | ", f.OneRow))));

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task TheKeyRowIsOnScreenAndOnOneRowAt80x24(string view, string hints)
    {
        var rows = FrameGrid.Decode(Render(view), Width, Height);
        var wanted = hints.Split(" | ");
        var row = rows.FirstOrDefault(r => wanted.All(h => r.Contains(h, StringComparison.Ordinal)));
        await Assert.That(row).IsNotNull()
            .Because($"{view} at 80×24 must show '{string.Join("', '", wanted)}' on one row:\n{string.Join('\n', rows)}");
    }

    /// <summary>
    /// The first-connect views at 60×20 as well (wave 32): the welcome, Browse, its addresses, the form and the tour. Each key
    /// row on one row, the header one row, nothing elided on the status row, and the command line clear of the modal.
    /// </summary>
    internal static readonly (string View, string[] OneRow)[] FirstConnect =
    [
        ("welcome", ["↑↓ pick", "Esc closes"]),
        ("welcome-connect", ["↑↓ field", "Esc back"]),
        ("browse-worlds", ["↑↓ pick", "Esc closes"]),
        ("browse-worlds-failed", ["↑↓ pick", "Esc closes"]),
        ("browse-worlds-picked", ["↑↓ field", "Esc back"]),
        ("browse-worlds-endpoints", ["↑↓ pick", "Esc back"]),
        ("tour", ["Keys:", "⌃P", "Key tour"]),
    ];

    public static IEnumerable<Func<(string View, string Hints)>> FirstConnectCases() =>
        FirstConnect.Select(f => (Func<(string, string)>)(() => (f.View, string.Join(" | ", f.OneRow))));

    [Test]
    [MethodDataSource(nameof(FirstConnectCases))]
    public async Task TheFirstConnectViewsFitA60x20Terminal(string view, string hints)
    {
        const int width = 60, height = 20;
        var rows = FrameGrid.Decode(App(width, height).RenderSnapshot(view), width, height);
        var all = string.Join('\n', rows);
        var wanted = hints.Split(" | ");
        await Assert.That(rows.Any(r => wanted.All(h => r.Contains(h, StringComparison.Ordinal)))).IsTrue()
            .Because($"{view} at 60×20: '{string.Join("', '", wanted)}' on one row:\n{all}");
        await Assert.That(rows[0].Contains("LOG off", StringComparison.Ordinal)).IsTrue().Because($"{view}: the header is one row:\n{all}");
        await Assert.That(rows[^1].Contains('…')).IsFalse().Because($"{view}: the status row elides nothing:\n{all}");
        await Assert.That(rows[^2].Contains('│') || rows[^2].Contains('└')).IsFalse().Because($"{view}: the command line is clear:\n{all}");
    }

    [Test]
    public async Task EveryViewInTheListIsOneTheSnapshotKnows()
    {
        foreach (var (view, _) in Footers.Concat(SettingsHeaders.Select(v => (v, Array.Empty<string>()))))
        {
            await Assert.That(SnapshotViews.All.Contains(view)).IsTrue().Because(view);
        }
    }

    /// <summary>
    /// Every settings screen, navigating and mid-edit. Their header's keys are fitted by <see cref="KeyLadder"/>
    /// (<c>ScreenChrome.Hints</c>): at 80 columns they used to be cut from the middle (<c>⏎ …</c>, <c>Del remo…</c>).
    /// </summary>
    internal static readonly string[] SettingsHeaders =
    [
        "settings", "worlds", "triggers", "route", "highlight", "set", "aliases", "timers", "keypad", "textansi", "input",
        "logging", "password", "startup", "settings-search", "settings-meters", "settings-changed", "settings-undo",
        "settings-themes", "triggers-edit", "logging-edit", "keypad-edit", "password-edit", "startup-edit", "channels-f5",
        "join-wraps-f5",
    ];

    public static IEnumerable<Func<string>> SettingsCases() => SettingsHeaders.Select(v => (Func<string>)(() => v));

    [Test]
    [MethodDataSource(nameof(SettingsCases))]
    public async Task ASettingsHeaderKeepsWholeKeysAndItsWayOutAt80x24(string view)
    {
        var rows = FrameGrid.Decode(Render(view), Width, Height);
        var header = rows[0];
        await Assert.That(header.Contains('…')).IsFalse()
            .Because($"{view}: a key is given up whole, never cut: '{header}'");
        await Assert.That(header.Contains("close", StringComparison.Ordinal) || header.Contains("cancels", StringComparison.Ordinal)
                || header.Contains("Esc back", StringComparison.Ordinal))
            .IsTrue().Because($"{view}: the way out stays: '{header}'");
        var footer = rows.LastOrDefault(r => r.Contains("[Esc]", StringComparison.Ordinal));
        await Assert.That(footer).IsNotNull().Because($"{view}: its action bar is on screen:\n{string.Join('\n', rows)}");
    }

    [Test]
    public async Task TheImportReviewScrollsItsBodyUnderAPinnedFooter()
    {
        var app = App();
        var first = FrameGrid.Decode(app.RenderSnapshot("import-review"), Width, Height);
        await Assert.That(first.Any(r => r.Contains("WHAT IT ADDS", StringComparison.Ordinal))).IsTrue();
        await Assert.That(first.Any(r => r.Contains("Your health is", StringComparison.Ordinal))).IsFalse().Because("the end of the list is below the fold");

        app.SimulateImportKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
        var scrolled = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
        await Assert.That(scrolled.Any(r => r.Contains("Your health is", StringComparison.Ordinal))).IsTrue()
            .Because("End scrolls the body to the last of what was left out:\n" + string.Join('\n', scrolled));
        await Assert.That(scrolled.Any(r => r.Contains("⏎ import", StringComparison.Ordinal) && r.Contains("Esc cancel", StringComparison.Ordinal))).IsTrue()
            .Because("the keys stay where they were");
        await Assert.That(scrolled.Any(r => r.Contains("password", StringComparison.Ordinal))).IsTrue()
            .Because("and so does the passwords question");
        await Assert.That(app.ImportReviewLines.Count).IsGreaterThan(0).Because("the review is still open: End scrolled, it did not answer");
    }

    [Test]
    public async Task ThePackageReviewScrollsItsBodyUnderAPinnedFooterClearOfTheCommandLine()
    {
        var app = App();
        var rows = FrameGrid.Decode(app.RenderSnapshot("package-review"), Width, Height);
        var keys = rows.ToList().FindIndex(r => r.Contains("⏎ install", StringComparison.Ordinal) && r.Contains("Esc cancel", StringComparison.Ordinal));
        var line = rows.ToList().FindIndex(r => r.Contains("say hello there", StringComparison.Ordinal));
        await Assert.That(keys).IsGreaterThan(0).Because(string.Join('\n', rows));
        await Assert.That(line).IsGreaterThan(keys + 1).Because("the review's bottom edge is above the command line, which stays in view");
        await Assert.That(rows[keys - 1].Contains('─')).IsTrue().Because("a rule marks the pinned rows as the review's foot");

        app.PackageView.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.End, false, false, false));
        var scrolled = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
        await Assert.That(scrolled.Any(r => r.Contains("characters", StringComparison.Ordinal) && r.Contains("Corvid", StringComparison.Ordinal))).IsTrue()
            .Because("End scrolls to the last row:\n" + string.Join('\n', scrolled));
        await Assert.That(scrolled[keys]).Contains("⏎ install").Because("the keys stay where they were");
        await Assert.That(app.PackageView.IsOpen).IsTrue().Because("End scrolled, it did not answer");
    }

    [Test]
    public async Task TheCellPaletteShrinksToItsRowsWhenFilteredAndNeverPastTheCommandLine()
    {
        var app = App();
        app.RenderSnapshot("menu");
        var full = app.Palette.WindowHeight;
        var fullRows = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
        await Assert.That(full).IsGreaterThan(CommandPalette.FilteredMinHeight);

        app.SimulateCellPaletteQuery("freeze");
        var filtered = app.Palette.WindowHeight;
        var rows = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
        await Assert.That(filtered).IsLessThan(full).Because("a few matches: not the whole catalog's height:\n" + string.Join('\n', rows));
        await Assert.That(filtered).IsGreaterThanOrEqualTo(CommandPalette.FilteredMinHeight).Because("never below the floor");
        await Assert.That(rows.Any(r => r.Contains("Freeze", StringComparison.Ordinal))).IsTrue();
        await Assert.That(rows.ToList().FindIndex(r => r.Contains("›freeze", StringComparison.Ordinal)))
            .IsEqualTo(fullRows.ToList().FindIndex(r => r.Contains('›', StringComparison.Ordinal))).Because("the prompt does not move");
        await Assert.That(rows.Any(r => r.Contains("say hello there", StringComparison.Ordinal))).IsTrue().Because("the command line stays clear");

        app.SimulateCellPaletteQuery(string.Empty);
        await Assert.That(app.Palette.WindowHeight).IsEqualTo(full).Because("an emptied query lists the whole catalog again");
    }

    private static SharpMUTermApp App(int width = Width, int height = Height)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ThemeName = "Dark";
        config.Theme = ThemeLibrary.Get("Dark");
        return new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(width, height));
    }

    private static string Render(string view) => App().RenderSnapshot(view);
}
