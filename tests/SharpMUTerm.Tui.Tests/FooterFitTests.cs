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
        ("quit", ["⏎ stay", "Esc stays"]),
        ("search", ["↑↓ pick", "⌃F closes"]),
        ("history-search", ["type to filter", "⌃R closes"]),
        ("layouts", ["↑↓ pick", "[⏎] Apply"]),
        ("welcome", ["↑↓ pick", "Esc closes"]),
        ("welcome-connect", ["↑↓ field", "Esc back"]),
        ("packages", ["installed", "Space", "Esc close"]),
        ("package-review", ["⏎ install", "Esc cancel"]),
        ("package-browse", ["↑↓ select", "Esc close"]),
        ("messages", ["+/- level", "Esc close"]),
        ("prefix-panel", ["Esc cancels", "any other key disarms"]),
        ("settings", ["[Esc] Close", "[⏎] Edit"]),
        ("triggers", ["[Esc] Close", "[⏎] Edit"]),
        ("keypad", ["[Esc] Close", "[⏎] Edit"]),
        ("trigger-builder", ["[Esc] Revert", "[⏎] Commit"]),
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

    [Test]
    public async Task EveryViewInTheListIsOneTheSnapshotKnows()
    {
        foreach (var (view, _) in Footers)
        {
            await Assert.That(SnapshotViews.All.Contains(view)).IsTrue().Because(view);
        }
    }

    [Test]
    public async Task TheImportReviewScrollsItsBodyUnderAPinnedFooter()
    {
        var app = App();
        var first = FrameGrid.Decode(app.RenderSnapshot("import-review"), Width, Height);
        await Assert.That(first.Any(r => r.Contains("LEFT OUT", StringComparison.Ordinal) && !r.Contains("password", StringComparison.Ordinal))).IsTrue();

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

    private static SharpMUTermApp App()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ThemeName = "Dark";
        config.Theme = ThemeLibrary.Get("Dark");
        return new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
    }

    private static string Render(string view) => App().RenderSnapshot(view);
}
