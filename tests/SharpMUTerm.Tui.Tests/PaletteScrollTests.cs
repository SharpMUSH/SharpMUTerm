using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <b>⌃P's selected row stays on screen.</b> The catalog is taller than the palette at 80×24, and ↓ used to walk the
/// selection off the bottom with nothing scrolling after it. Read off the painted frame: the selected entry's title
/// must be drawn after every key, and the prompt must not move.
/// </summary>
[NotInParallel]
public class PaletteScrollTests
{
    private const int Width = 80;

    private const int Height = 24;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly ConsoleKeyInfo Down = new('\0', ConsoleKey.DownArrow, false, false, false);
    private static readonly ConsoleKeyInfo Up = new('\0', ConsoleKey.UpArrow, false, false, false);
    private static readonly ConsoleKeyInfo PageDown = new('\0', ConsoleKey.PageDown, false, false, false);

    [Test]
    public async Task ArrowingDownTheWholeCatalogKeepsTheSelectedRowOnScreen()
    {
        var app = App();
        var first = FrameGrid.Decode(app.RenderSnapshot("menu"), Width, Height);
        var prompt = PromptRow(first);
        var palette = app.Palette;
        var count = palette.View.Entries.Count;
        await Assert.That(count).IsGreaterThan(palette.WindowHeight).Because("the catalog must overflow the palette for this to test anything");

        for (var step = 1; step < count; step++)
        {
            palette.SimulateKey(Down);
            var rows = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
            await Assert.That(palette.Selected).IsEqualTo(step);
            await AssertSelectedShown(app, rows, prompt);
        }

        await Assert.That(palette.ListTop).IsGreaterThan(0).Because("the list scrolled to follow the selection");

        palette.SimulateKey(Down); // wraps to the first entry
        var wrapped = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
        await Assert.That(palette.Selected).IsEqualTo(0);
        await Assert.That(palette.ListTop).IsEqualTo(0).Because("the first entry scrolls back to the top");
        await AssertSelectedShown(app, wrapped, prompt);

        palette.SimulateKey(Up); // wraps to the last
        var last = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
        await Assert.That(palette.Selected).IsEqualTo(count - 1);
        await AssertSelectedShown(app, last, prompt);

        for (var step = count - 2; step >= 0; step--)
        {
            palette.SimulateKey(Up);
            var rows = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
            await AssertSelectedShown(app, rows, prompt);
        }
    }

    [Test]
    public async Task PageDownMovesTheSelectionAPageAndKeepsItOnScreen()
    {
        var app = App();
        var first = FrameGrid.Decode(app.RenderSnapshot("menu"), Width, Height);
        var prompt = PromptRow(first);
        var palette = app.Palette;
        palette.SimulateKey(PageDown);
        var rows = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
        await Assert.That(palette.Selected).IsGreaterThan(1);
        await AssertSelectedShown(app, rows, prompt);
    }

    private static async Task AssertSelectedShown(SharpMUTermApp app, IReadOnlyList<string> rows, int prompt)
    {
        var palette = app.Palette;
        var title = palette.View.Entries[palette.Selected].Item.Title;
        var shown = title.Length > 14 ? title[..14] : title;
        var frame = string.Join('\n', rows);
        await Assert.That(PromptRow(rows)).IsEqualTo(prompt).Because("the prompt stays put:\n" + frame);
        var at = rows.ToList().FindIndex(r => r.Contains(shown, StringComparison.Ordinal));
        await Assert.That(at).IsGreaterThan(prompt).Because($"entry {palette.Selected} \"{title}\" is drawn under the prompt:\n" + frame);
    }

    private static int PromptRow(IReadOnlyList<string> rows) =>
        rows.ToList().FindIndex(r => r.Contains("│›", StringComparison.Ordinal) || r.Contains("│ ›", StringComparison.Ordinal));

    private static SharpMUTermApp App()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ThemeName = "Dark";
        config.Theme = ThemeLibrary.Get("Dark");
        return new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
    }
}
