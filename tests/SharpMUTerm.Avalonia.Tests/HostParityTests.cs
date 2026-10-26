using SharpConsoleUI.Drivers;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;
using SharpMUTerm.Tui.Tests;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The terminal and the desktop window are one client, and these are the checks that it stays one. Every
/// snapshot view is drawn through the terminal's headless driver and through this project's driver, and the
/// two must put the same character on the same background in every cell; and every surface that names a key
/// is read once per host, against that host's own <see cref="HostKeys"/>.
/// </summary>
/// <remarks>Serialised: a headless frame is captured by redirecting the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public partial class HostParityTests
{
    private const int Width = 140;
    private const int Height = 40;

    /// <summary>The same for both drivers, so a difference is the driver's and not the capabilities' or the keys'.</summary>
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    public static IEnumerable<string> Views() => SnapshotViews.All.Select(v => v ?? "(default)");

    /// <summary>
    /// A view drawn in the window is the view drawn in the terminal, cell for cell. What this catches is the
    /// driver falling behind the framework — a cell it does not store, a write path it does not implement —
    /// for any view, including the next one added (<c>EveryViewTheSnapshotKnowsIsInTheParityList</c>).
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Views))]
    public async Task EveryViewDrawsTheSameCellsInTheWindowAsInTheTerminal(string name)
    {
        var view = name == "(default)" ? null : name;
        Console.SetIn(TextReader.Null);

        string ansi;
        using (var headless = new HeadlessConsoleDriver(Width, Height))
        {
            await using var terminal = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, Caps));
            ansi = terminal.RenderSnapshot(view);
        }

        using var driver = new AvaloniaConsoleDriver(Width, Height);
        await using (var window = new SharpMUTermApp(
            DemoScene.Build(),
            WindowHost.Create(driver, clipboard: null) with { Capabilities = Caps, Keys = HostKeys.Terminal, Audio = null, Presence = null, Announcer = null, RunsInline = true }))
        {
            window.RenderSnapshot(view);
        }

        var differences = Differences(ansi, driver.Frame, Height);
        await Assert.That(differences).IsEmpty().Because(string.Join('\n', differences));
    }

    /// <summary>
    /// The same comparison with the window's client running its real loop, the way a person runs it. The test
    /// above draws every view inline, with no loop, and that is how a ⌃P menu and an F1 composer that the window
    /// drew as empty frames went unnoticed: inline they were whole. The bottom row is left out — a running client
    /// has been through startup, and says on that row that nothing connects at start, where a snapshot never
    /// started — and so are the views that read the clock, which a loop does and a snapshot pins.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Views))]
    public async Task EveryViewDrawsTheSameCellsWhenTheWindowsLoopIsRunning(string name)
    {
        var view = name == "(default)" ? null : name;
        if (view is "timestamps" or "timestamps-toggled")
        {
            return;
        }

        Console.SetIn(TextReader.Null);
        string ansi;
        using (var headless = new HeadlessConsoleDriver(Width, Height))
        {
            await using var terminal = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, Caps));
            ansi = terminal.RenderSnapshot(view);
        }

        using var driver = new AvaloniaConsoleDriver(Width, Height);
        await using var window = new SharpMUTermApp(
            DemoScene.Build(),
            WindowHost.Create(driver, clipboard: null) with { Capabilities = Caps, Keys = HostKeys.Terminal, Audio = null, Presence = null, Announcer = null });
        window.OnUiThread(() => _ = window.RenderSnapshot(view));
        var loop = new Thread(() => window.Run([])) { IsBackground = true };
        loop.Start();
        LiveLoop.Settle(driver);
        window.EndLoop();
        loop.Join(TimeSpan.FromSeconds(5));

        var differences = Differences(ansi, driver.Frame, Height - 1);
        await Assert.That(differences).IsEmpty().Because(string.Join('\n', differences));
    }

    /// <summary>Where the window's frame differs from the terminal's, glyph or background, over the first <paramref name="rows"/> rows.</summary>
    private static List<string> Differences(string ansi, ScreenFrame frame, int rows)
    {
        var cells = FrameGrid.Cells(ansi, Width, Height);
        var differences = new List<string>();
        for (var y = 0; y < rows && differences.Count < 5; y++)
        {
            var expected = FrameGrid.Decode(ansi, Width, Height)[y];
            var actual = frame.RowText(y);
            if (!string.Equals(WithoutClockTimes(expected), WithoutClockTimes(actual), StringComparison.Ordinal))
            {
                differences.Add($"row {y}:\n  terminal |{expected}|\n  window   |{actual}|");
                continue;
            }

            for (var x = 0; x < Width; x++)
            {
                if (cells.TryGetValue((y, x), out var cell) && cell.Background is { } bg
                    && frame[x, y].Background is var wb && (wb.R, wb.G, wb.B) != (bg.R, bg.G, bg.B))
                {
                    differences.Add($"row {y} col {x} '{cell.Glyph}': terminal bg {bg}, window bg {wb.R},{wb.G},{wb.B}");
                    break;
                }
            }
        }

        return differences;
    }

    /// <summary>
    /// A row with each <c>hh:mm:ss</c> blanked. The client's messages are stamped by the logger's wall clock,
    /// which no test pins, and the two hosts draw a second apart often enough to fail on the second changing.
    /// </summary>
    private static string WithoutClockTimes(string row) => ClockTime().Replace(row, "--:--:--");

    [System.Text.RegularExpressions.GeneratedRegex(@"\b\d{2}:\d{2}:\d{2}\b")]
    private static partial System.Text.RegularExpressions.Regex ClockTime();

    public static IEnumerable<string> Hosts() => ["terminal", "window"];

    private static (HostKeys Keys, UsageLauncher Launcher) HostNamed(string name) => name == "window"
        ? (WindowHost.Keys, WindowOptions.Launcher)
        : (HostKeys.Terminal, ClientUsage.Terminal);

    /// <summary>
    /// ⌃P and <c>--help</c> name each host's own newline chord, and none of the chords the host keeps for
    /// itself. In the window ⌥⏎ maximises, so a surface still saying ⌥⏎ inserts a newline would send the
    /// reader to the key that resizes the window instead.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Hosts))]
    public async Task EverySurfaceNamesThisHostsKeys(string name)
    {
        var (keys, launcher) = HostNamed(name);
        Console.SetIn(TextReader.Null);
        using var headless = new HeadlessConsoleDriver(160, 48);
        await using var app = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, Caps) with { Keys = keys });
        app.RenderSnapshot("focus");

        var usage = new StringWriter();
        ClientUsage.Write(usage, launcher, keys);
        var help = string.Join('\n', usage.ToString().Split('\n').Where(l => !l.StartsWith("Host:", StringComparison.Ordinal)));
        var catalog = app.BuildCatalog();

        await Assert.That(catalog.Single(c => c.Id == "term:newline").Subtitle).IsEqualTo(keys.Newline.Glyph);
        await Assert.That(help).Contains($"{keys.Newline.Text} inserts a newline");

        var surfaces = new (string Where, string Text)[]
        {
            ("--help", help),
            ("the status row", app.StatusMarkup),
            ("the ⌃P surface", string.Join("\n", catalog.Select(c => $"{c.Title} {c.Subtitle}"))),
        };
        foreach (var (where, text) in surfaces)
        {
            foreach (var reserved in keys.Reserved)
            {
                await Assert.That(text).DoesNotContain(reserved.Chord.Glyph)
                    .Because($"{where} in the {name} names {reserved.Chord.Glyph}, which the {name} {reserved.Action}");
                await Assert.That(text).DoesNotContain(reserved.Chord.Text)
                    .Because($"{where} in the {name} names {reserved.Chord.Text}, which the {name} {reserved.Action}");
            }
        }
    }

    /// <summary>The chord the window names as its newline is one it hands the client, not one it keeps.</summary>
    [Test]
    public async Task TheWindowsNewlineIsNotOneOfItsOwnChords()
    {
        var newline = WindowHost.Keys.Newline;
        var key = new ConsoleKeyInfo('\r', newline.Key, shift: false, alt: false, control: true);
        await Assert.That(WindowHost.Keys.Reserves(key)).IsFalse();
        await Assert.That(WindowHost.Keys.Reserves(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, true, false))).IsTrue();
    }
}
