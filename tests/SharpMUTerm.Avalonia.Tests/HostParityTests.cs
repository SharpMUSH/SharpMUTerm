using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Palette;
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
    /// Every view under each chrome arrangement: the client's chrome in cells (the terminal's, and the window's with
    /// <c>--cell-chrome</c>), the header and rail drawn by the host, and the window as a person runs it — header, rail,
    /// status footer, the command line's field and every pane's tab strip drawn by the host (<see cref="HostChrome.Desktop"/>). Both hosts are
    /// told the same arrangement each time, so the comparison is still of drivers and nothing else, over every cell
    /// either one draws.
    /// </summary>
    public static IEnumerable<(string View, string HostChrome)> ViewsUnderBothChromes() =>
        from view in Views()
        from chrome in new[] { "none", "header-rail", "desktop" }
        select (view, chrome);

    private static HostChrome ChromeFor(string chrome) => chrome switch
    {
        "header-rail" => HostChrome.HeaderAndRail,
        "desktop" => HostChrome.Desktop,
        _ => HostChrome.None,
    };

    /// <summary>
    /// A view drawn in the window is the view drawn in the terminal, cell for cell. What this catches is the
    /// driver falling behind the framework — a cell it does not store, a write path it does not implement —
    /// for any view, including the next one added (<c>EveryViewTheSnapshotKnowsIsInTheParityList</c>).
    /// </summary>
    [Test]
    [MethodDataSource(nameof(ViewsUnderBothChromes))]
    public async Task EveryViewDrawsTheSameCellsInTheWindowAsInTheTerminal(string name, string hostChrome)
    {
        var view = name == "(default)" ? null : name;
        var chrome = ChromeFor(hostChrome);
        Console.SetIn(TextReader.Null);

        string ansi;
        using (var headless = new HeadlessConsoleDriver(Width, Height))
        {
            await using var terminal = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, Caps) with { Chrome = chrome });
            ansi = terminal.RenderSnapshot(view);
        }

        using var driver = new AvaloniaConsoleDriver(Width, Height);
        await using (var window = new SharpMUTermApp(
            DemoScene.Build(),
            WindowHost.Create(driver, clipboard: null, chrome) with { Capabilities = Caps, Keys = HostKeys.Terminal, Audio = null, Presence = null, Announcer = null, NativeSettings = null, NativePalette = null, FloatingPanels = null, WindowModes = null, RunsInline = true }))
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
    /// started — and so are the views that read the clock, which a loop does and a snapshot pins. Drawn with the
    /// window's own chrome, because that is how a person runs it; the terminal is told the same.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(Views))]
    public async Task EveryViewDrawsTheSameCellsWhenTheWindowsLoopIsRunning(string name)
    {
        var chrome = HostChrome.Desktop;
        var view = name == "(default)" ? null : name;
        if (view is "timestamps" or "timestamps-toggled")
        {
            return;
        }

        Console.SetIn(TextReader.Null);
        string ansi;
        using (var headless = new HeadlessConsoleDriver(Width, Height))
        {
            await using var terminal = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, Caps) with { Chrome = chrome });
            ansi = terminal.RenderSnapshot(view);
        }

        using var driver = new AvaloniaConsoleDriver(Width, Height);
        await using var window = new SharpMUTermApp(
            DemoScene.Build(),
            WindowHost.Create(driver, clipboard: null, chrome) with { Capabilities = Caps, Keys = HostKeys.Terminal, Audio = null, Presence = null, Announcer = null, NativeSettings = null, NativePalette = null, FloatingPanels = null, WindowModes = null });
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
            if (!string.Equals(expected, actual, StringComparison.Ordinal) && !DifferOnlyInStamp(expected, actual))
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
    /// True when two rows differ only in the time a message was stamped with: the first <c>hh:mm:ss</c> in
    /// each, at the same column, with every other cell equal. The client's messages are stamped by the
    /// logger's wall clock, which no test pins, and the two hosts draw a second apart often enough to fail on
    /// the second changing. A clock in a message's own text comes after the stamp and is still compared.
    /// </summary>
    internal static bool DifferOnlyInStamp(string expected, string actual)
    {
        var a = ClockTime().Match(expected);
        var b = ClockTime().Match(actual);
        return a.Success && b.Success && a.Index == b.Index && expected.Length == actual.Length
            && string.Equals(expected[..a.Index], actual[..a.Index], StringComparison.Ordinal)
            && string.Equals(expected[(a.Index + a.Length)..], actual[(b.Index + b.Length)..], StringComparison.Ordinal);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\b\d{2}:\d{2}:\d{2}\b")]
    private static partial System.Text.RegularExpressions.Regex ClockTime();

    /// <summary>Only the stamp is allowed to differ: a clock in a message's text is compared like any other text.</summary>
    [Test]
    [Arguments("│ 10:00:00 info  hello", "│ 10:00:01 info  hello", true)]
    [Arguments("│ 10:00:00 info  at 12:34:56", "│ 10:00:00 info  at 12:35:56", false)]
    [Arguments("│ 10:00:00 info  hello", "│ 10:00:01 info  hullo", false)]
    [Arguments("│ 10:00:00 info  hello", "│  10:00:01 info hello", false)]
    public async Task OnlyTheStampMayDifferBetweenHosts(string terminal, string window, bool same) =>
        await Assert.That(DifferOnlyInStamp(terminal, window)).IsEqualTo(same);

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

    public static IEnumerable<(string Host, string View, StatusMode Mode, int Width, int Height)> ModeViews() =>
        from host in Hosts()
        from view in new (string View, StatusMode Mode)[]
        {
            ("scrollback-up", StatusMode.SplitScrollback),
            ("scrollback-split", StatusMode.SplitScrollback),
            ("freeze", StatusMode.Frozen),
            ("freeze-scrollback", StatusMode.Frozen),
            ("search-landed", StatusMode.SearchResult),
            ("panel-composer", StatusMode.Composer),
        }
        from size in new[] { (80, 24), (120, 32) }
        select (host, view.View, view.Mode, size.Item1, size.Item2);

    /// <summary>
    /// While a mode holds, the status row names that mode's keys — at least the first, the way out — as this host
    /// spells them, and none of the chords the host keeps; and the row still fits on one row at 80 columns, where a
    /// status line that wrapped once took a row off every pane and re-announced every pane's size.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(ModeViews))]
    public async Task TheStatusRowNamesEachModesKeysAsThisHostSpellsThem(string name, string view, StatusMode mode, int width, int height)
    {
        var (keys, _) = HostNamed(name);
        Console.SetIn(TextReader.Null);
        using var headless = new HeadlessConsoleDriver(width, height);
        await using var app = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(headless, Caps) with { Keys = keys });
        var frame = app.RenderSnapshot(view);

        await Assert.That(app.CurrentStatusMode()).IsEqualTo(mode);
        var glyphs = new SharpMUTerm.Client.Contributions.HostKeyGlyphs(keys.Newline.Glyph);
        var status = System.Text.RegularExpressions.Regex.Replace(app.StatusMarkup, @"\[[^\]]*\]", string.Empty);
        await Assert.That(status).Contains(ModeHints.Candidates(mode, glyphs)[^1]);
        if (width >= 120)
        {
            await Assert.That(status).Contains(ModeHints.Candidates(mode, glyphs)[0]).Because("a wide row has room for every key");
        }

        foreach (var reserved in keys.Reserved)
        {
            await Assert.That(status).DoesNotContain(reserved.Chord.Glyph);
            await Assert.That(status).DoesNotContain(KeyGlyphs.Normalize(reserved.Chord.Glyph));
        }

        await Assert.That(app.LaidOutRows.Status).IsEqualTo(1).Because("the status row never wraps");
        await Assert.That(SharpMUTermApp.MarkupWidth(app.StatusMarkup)).IsLessThanOrEqualTo(width);
        await Assert.That(FrameGrid.Decode(frame, width, height)[height - 1]).Contains(ModeHints.Candidates(mode, glyphs)[^1]);
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
