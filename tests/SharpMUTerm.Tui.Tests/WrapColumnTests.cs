using System.Text.RegularExpressions;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// F7 ▸ TEXT ▸ <c>wrap output at</c>: a pane's text wraps at the column, asserted on <b>painted cells</b>, and the
/// pane keeps its rectangle. What a connected world is told is <see cref="NawsPaneReportTests"/>' half.
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class WrapColumnTests
{
    private const int Width = 120;
    private const int Height = 32;
    private const string Main = "main";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>Thirty numbered words, one line: far wider than the column and than the pane.</summary>
    private static readonly string LongLine = string.Join(' ', Enumerable.Range(0, 30).Select(i => $"w{i:00}"));

    private static (SharpMUTermApp App, WorldSession Session) Bound(int columns)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        config.Text.WrapColumns = columns;

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height), new ManualTimeProvider());
        var session = app.BindWorldWithoutConnecting(config.Worlds[0]);
        app.RenderSnapshot();
        return (app, session);
    }

    /// <summary>The frame as rows of text, walked the way a terminal walks it (<see cref="TimestampGutterTests"/>').</summary>
    private static string[] Rows(string ansi)
    {
        var cells = new char[Height][];
        for (var y = 0; y < Height; y++)
        {
            cells[y] = Enumerable.Repeat(' ', Width).ToArray();
        }

        var (row, column) = (0, 0);
        foreach (Match token in Regex.Matches(ansi, @"\x1b\[([0-9;]*)([A-Za-z])|([^\x1b\r\n])"))
        {
            if (token.Groups[3].Success)
            {
                if (row < Height && column < Width)
                {
                    cells[row][column] = token.Groups[3].Value[0];
                }

                column++;
                continue;
            }

            if (token.Groups[2].Value == "H")
            {
                var at = token.Groups[1].Value.Split(';');
                row = at[0].Length > 0 ? int.Parse(at[0]) - 1 : 0;
                column = at.Length > 1 && at[1].Length > 0 ? int.Parse(at[1]) - 1 : 0;
            }
        }

        return cells.Select(c => new string(c)).ToArray();
    }

    /// <summary>
    /// The settled frame. A margin is set after the frame that measured the viewport, so it shows from the next one,
    /// and a following viewport settles a frame after that.
    /// </summary>
    private static string[] SettledRows(SharpMUTermApp app)
    {
        app.RenderWholeFrame();
        app.RenderWholeFrame();
        return Rows(app.RenderWholeFrame());
    }

    private static PaneRect MainRect(SharpMUTermApp app)
    {
        var surface = app.PaneSnapshot();
        var paneId = surface.Rects.Keys.Single(id => surface.ActiveWindow(id) == Main);
        return app.PaneOutputRects()[paneId];
    }

    /// <summary>The main pane's cells on each row that carries a word of <see cref="LongLine"/>, in order.</summary>
    private static List<string> LongLineRows(string[] rows, PaneRect pane) =>
        rows.Skip(pane.Y).Take(pane.Height)
            .Select(r => r.Substring(pane.X, Math.Min(pane.Width, Width - pane.X)))
            .Where(r => Regex.IsMatch(r, @"\bw\d\d\b"))
            .ToList();

    [Test]
    public async Task APaneWrapsItsTextAtTheColumnAndKeepsItsRectangle()
    {
        var (app, session) = Bound(40);
        var pane = MainRect(app);
        await Assert.That(pane.Width).IsGreaterThan(60);

        session.PrintSystem(LongLine);
        var rows = LongLineRows(SettledRows(app), pane);

        // 119 characters at 40 columns is three rows at least; at the pane's width it would have been two.
        await Assert.That(rows.Count).IsGreaterThanOrEqualTo(3);
        foreach (var row in rows)
        {
            await Assert.That(row.TrimEnd().Length).IsLessThanOrEqualTo(40).Because(row);
        }

        var text = string.Join(' ', rows.Select(r => r.Trim()));
        await Assert.That(text).Contains("w00 w01");
        await Assert.That(text).Contains("w28 w29");
        await Assert.That(MainRect(app)).IsEqualTo(pane);
        await Assert.That(app.BarWidth(Main)).IsEqualTo(40);
    }

    [Test]
    public async Task ZeroWrapsAtThePaneAsBefore()
    {
        var (app, session) = Bound(0);
        var pane = MainRect(app);

        session.PrintSystem(LongLine);
        var rows = LongLineRows(SettledRows(app), pane);

        await Assert.That(rows.Any(r => r.TrimEnd().Length > 40)).IsTrue();
        await Assert.That(app.BarWidth(Main)).IsGreaterThan(40);
    }

    [Test]
    public async Task AColumnWiderThanThePaneWrapsAtThePane()
    {
        var (app, _) = Bound(400);
        app.RenderWholeFrame();
        app.RenderWholeFrame();

        await Assert.That(app.WrapWidth(70)).IsEqualTo(70);
        await Assert.That(app.WrapWidth(500)).IsEqualTo(400);
        await Assert.That(app.BarWidth(Main)).IsLessThan(400);
    }
}
