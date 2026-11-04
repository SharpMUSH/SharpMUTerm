using System.Diagnostics;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Parsing;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A theme change repaints the history. A line's colours are resolved against the reading plane of the theme in
/// force (<see cref="Contrast"/>, <see cref="MarkupFormatter"/>), and a pane line used to be held as that markup
/// alone, so after Dark → Light every old line kept greys and whites lifted for a dark plane — faint, or white on
/// near-white, until it scrolled away. Each world line is now kept beside its markup (<c>PaneLine.Source</c>) and
/// formatted again on the switch, in place, so nothing indexing a buffer moves.
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class ThemeRepaintTests
{
    private const int Width = 120;
    private const int Height = 32;
    private const string Main = "main";
    private const string Bright = "the beacon burns a pale white";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    [Test]
    public async Task AfterASwitchToLightAnOldLinesCellsClearTheFloorOnTheNewPane()
    {
        var world = await Connected();
        world.Receive($"\u001b[97m{Bright}\u001b[0m\n");
        world.Receive("\u001b[37mand the grey stones answer it\u001b[0m\n");

        // On Dark the bright white is legible as it is; on Light it is the colour of the paper.
        await Assert.That(WorstRatio(world.App, Bright)).IsGreaterThanOrEqualTo(Contrast.Floor - 0.05);

        SwitchTo(world.App, "Light");

        await Assert.That(world.App.ThemeName).IsEqualTo("Light");
        await Assert.That(WorstRatio(world.App, Bright)).IsGreaterThanOrEqualTo(Contrast.Floor - 0.05);
        await Assert.That(WorstRatio(world.App, "the grey stones answer it")).IsGreaterThanOrEqualTo(Contrast.Floor - 0.05);
    }

    /// <summary>The line's markup after the switch is exactly what a line arriving under Light would get.</summary>
    [Test]
    public async Task AnOldLineIsFormattedAsANewLineWouldBe()
    {
        var world = await Connected();
        world.Receive($"\u001b[97m{Bright}\u001b[0m\n");
        SwitchTo(world.App, "Light");
        world.Receive($"\u001b[97m{Bright}\u001b[0m\n");

        var rows = world.App.PaneLines(Main).Where(r => r.Contains(Bright, StringComparison.Ordinal)).ToList();
        await Assert.That(rows.Count).IsEqualTo(2);
        await Assert.That(rows[0]).IsEqualTo(rows[1]);
    }

    [Test]
    public async Task ALinkStillClicksAfterTheSwitch()
    {
        var world = await Connected();
        world.Receive("\u001b[97mthe notice board:\u001b[0m https://example.org/board\n");
        SwitchTo(world.App, "Light");

        await Assert.That(Click(world, "https://example.org/board")).IsTrue();
        await Assert.That(world.Opened).IsEquivalentTo(new[] { "https://example.org/board" });
    }

    /// <summary>
    /// A bar is a row of the buffer: the switch replaces rows in place, so the away bar is at the same index, with
    /// the same lines either side of it, and drawn in the new theme's accent.
    /// </summary>
    [Test]
    public async Task TheAwayBarStaysWhereItWasAndTakesTheNewAccent()
    {
        var world = await Connected();
        world.Receive("\u001b[37mbefore you left\u001b[0m\n");
        world.App.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.LeftArrow, false, false, false));
        world.Receive("\u001b[97mwhile you were away\u001b[0m\n");
        world.App.SimulateReturnFromAway(TimeSpan.FromMinutes(12));
        world.App.RenderNextFrame();

        var index = world.App.AwayBarIndex(Main);
        await Assert.That(index).IsNotNull();
        var before = world.App.PaneLines(Main);
        var barBefore = before[index!.Value];

        SwitchTo(world.App, "Light");

        var after = world.App.PaneLines(Main);
        await Assert.That(world.App.AwayBarIndex(Main)).IsEqualTo(index);
        await Assert.That(after.Count).IsEqualTo(before.Count);
        await Assert.That(after[index.Value - 1]).Contains("before you left");
        await Assert.That(after[index.Value]).Contains(AwayBarRenderer.Label);
        await Assert.That(after[index.Value + 1]).Contains("while you were away");
        await Assert.That(after[index.Value]).IsNotEqualTo(barBefore);
    }

    /// <summary>Only colours change, so a selection made before the switch still names the same text.</summary>
    [Test]
    public async Task ASelectionSurvivesTheSwitch()
    {
        Console.SetIn(TextReader.Null);
        var copied = new List<string>();
        var config = DemoScene.Build();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height), clipboard: copied.Add);
        app.RenderSnapshot();
        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 25, 1);
        var selected = app.PaneSelection(SharpMUTermApp.MainWindowId);
        await Assert.That(selected).IsNotEmpty();

        SwitchTo(app, "Light");
        app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.C, shift: false, alt: false, control: true));

        await Assert.That(copied).HasSingleItem();
        await Assert.That(copied[0]).IsEqualTo(selected);
    }

    /// <summary>
    /// What the repaint costs: a window at the pane cap (2,000 lines), restyled and re-fed, and what keeping the
    /// lines costs. Held to generous ceilings so a slow CI box passes; the measured figures are in CLAUDE.md.
    /// </summary>
    [Test]
    public async Task RestylingAFullWindowIsBounded()
    {
        var world = await Connected();
        var text = Enumerable.Range(0, 2000)
            .Select(i => $"\u001b[3{i % 8}mline {i} the courier \u001b[1mruns on past\u001b[0m the northern watch, {i % 7} leagues out\n")
            .ToArray();
        foreach (var line in text)
        {
            world.Telnet.Receive(line);
        }

        world.App.RenderNextFrame();
        var clock = Stopwatch.StartNew();
        SwitchTo(world.App, "Light");
        clock.Stop();

        // What 2,000 such lines weigh as StyledLines, the most a window can add by keeping them (a session window's
        // are the very instances its scrollback ring already holds, so there it is a reference per line).
        var parser = new AnsiParser();
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var kept = text.SelectMany(parser.Feed).ToList();
        var after = GC.GetTotalMemory(forceFullCollection: true);
        Console.Error.WriteLine(
            $"theme switch over 2000 lines: {clock.ElapsedMilliseconds} ms; 2000 StyledLines: {(after - before) / 1024} KiB");
        GC.KeepAlive(kept);

        await Assert.That(kept.Count).IsEqualTo(2000);
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
        await Assert.That(after - before).IsLessThan(4L * 1024 * 1024);
    }

    // ---- Harness ---------------------------------------------------------------------------

    private static void SwitchTo(SharpMUTermApp app, string theme)
    {
        ThemeChoices.Apply(app.Configuration, theme);
        app.SaveConfiguration();
        app.RenderNextFrame();
    }

    /// <summary>The lowest contrast any visible cell of <paramref name="text"/> has against its own background.</summary>
    private static double WorstRatio(SharpMUTermApp app, string text)
    {
        var frame = app.RenderWholeFrame();
        var cells = FrameGrid.Cells(frame, Width, Height);
        var rows = FrameGrid.Decode(frame, Width, Height);
        var worst = double.MaxValue;
        var found = false;
        for (var row = 0; row < rows.Count; row++)
        {
            var at = rows[row].IndexOf(text, StringComparison.Ordinal);
            if (at < 0)
            {
                continue;
            }

            found = true;
            for (var col = at; col < at + text.Length; col++)
            {
                var cell = cells[(row, col)];
                if (cell.Glyph == ' ' || cell.Foreground is not { } fg || cell.Background is not { } bg)
                {
                    continue;
                }

                worst = Math.Min(worst, Contrast.Ratio(fg, bg));
            }
        }

        if (!found)
        {
            throw new InvalidOperationException($"'{text}' is not on the frame:\n{string.Join('\n', rows)}");
        }

        return worst;
    }

    private sealed record Wired(SharpMUTermApp App, RecordingTelnetSession Telnet, List<string> Opened)
    {
        public void Receive(string text)
        {
            Telnet.Receive(text);
            App.RenderNextFrame();
        }
    }

    private static async Task<Wired> Connected()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        config.ScrollbackSpill.Enabled = false;
        config.Text.ActivityBarSeconds = 0;
        config.Worlds.Add(new WorldDefinition { Name = "Beacon", Host = "beacon.example.org", Port = 4000 });

        var opened = new List<string>();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height), openUrl: opened.Add);
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        await app.BindWorldWithoutConnecting(config.Worlds[0]).ConnectAsync();
        app.RenderNextFrame();
        return new Wired(app, telnet, opened);
    }

    private static bool Click(Wired world, string text)
    {
        var lines = world.App.PaneLines(Main);
        for (var row = 0; row < lines.Count; row++)
        {
            MarkupParser.Parse(lines[row], SharpConsoleUI.Color.White, SharpConsoleUI.Color.Black, out var links);
            foreach (var link in links)
            {
                if (link.Text.Contains(text, StringComparison.Ordinal))
                {
                    return world.App.SimulatePaneClick(Main, link.StartCol, row);
                }
            }
        }

        throw new InvalidOperationException($"no link reading '{text}'");
    }
}
