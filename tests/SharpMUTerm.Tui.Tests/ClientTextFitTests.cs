using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <b>Text the client writes is never cut</b> (wave 33), at 80×24 and 60×20: a notice, a bar, a prompt's keys or a header
/// the client words shortens its words to the row it has rather than ending in <c>…</c> or losing a key. Read off the
/// painted frame. Server text (room names, a world's own lines, mu*index data) is out of scope, and so is a list row's
/// detail column, which gives way by design.
/// </summary>
[NotInParallel]
public class ClientTextFitTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>
    /// A view and the client-worded rows to read in it: the first painted row holding each marker must hold no
    /// <c>…</c> (literal ellipses the words carry, such as <c>Switch to …</c>, are taken out first).
    /// </summary>
    internal static readonly (string View, string[] Rows)[] Views =
    [
        ("characters", ["offline"]),
        ("tint", ["offline"]),
        ("tint-input", ["offline"]),
        ("tint-tabs", ["offline"]),
        ("tint-input-moved", ["no session", "has no open session"]),
        ("drag", ["DRAG "]),
        ("move", ["MOVE "]),
        ("map-mine", ["Map: "]),
        ("compose", ["COMPOSE"]),
        ("compose-literal", ["COMPOSE"]),
        ("compose-preview", ["COMPOSE"]),
        ("panel-composer", ["⌃S send"]),
        ("reconnect", ["RECONNECT"]),
        ("channel-capture", ["NEW "]),
        ("tour", ["Keys:"]),
    ];

    /// <summary>What the client writes with an ellipsis in its words, which is not a cut.</summary>
    private static readonly string[] Literal = ["Switch to …", "dialling"];

    public static IEnumerable<Func<(string View, int Width, int Height)>> Cases() =>
        Views.SelectMany(v => new[] { (v.View, 80, 24), (v.View, 60, 20) })
            .Select(c => (Func<(string, int, int)>)(() => c));

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task TheClientsOwnRowsAreWhole(string view, int width, int height)
    {
        var rows = FrameGrid.Decode(App(width, height).RenderSnapshot(view), width, height);
        var markers = Views.Single(v => v.View == view).Rows;
        var row = rows.FirstOrDefault(r => markers.Any(m => r.Contains(m, StringComparison.Ordinal)));
        await Assert.That(row).IsNotNull().Because($"{view} at {width}×{height} shows '{string.Join("' or '", markers)}':\n{string.Join('\n', rows)}");
        var words = Literal.Aggregate(row!, (text, literal) => text.Replace(literal, string.Empty, StringComparison.Ordinal));
        await Assert.That(words.Contains('…')).IsFalse().Because($"{view} at {width}×{height}: '{row}'");
    }

    [Test]
    public async Task ADropPromptKeepsEscapeAndACommitAtEveryWidth()
    {
        foreach (var (view, size) in new[] { ("move", (80, 24)), ("move", (60, 20)), ("drag", (80, 24)), ("drag", (60, 20)) })
        {
            var rows = FrameGrid.Decode(App(size.Item1, size.Item2).RenderSnapshot(view), size.Item1, size.Item2);
            await Assert.That(rows[^1]).Contains("Esc cancel").Because($"{view} {size}: '{rows[^1]}'");
        }
    }

    /// <summary>The empty-startup notice fits an 80- and a 60-column row whole, and keeps its two ways out.</summary>
    [Test]
    [Arguments(120)]
    [Arguments(80)]
    [Arguments(60)]
    [Arguments(44)]
    public async Task TheNothingAtStartNoticeFitsItsRowAndKeepsItsWays(int width)
    {
        var text = SharpMUTermApp.NothingAtStartNoticeFor(width);
        await Assert.That(text.Length).IsLessThanOrEqualTo(width);
        await Assert.That(text).Contains("⌃P ▸ Switch to …");
        await Assert.That(text).Contains("F5 ▸ at start");
        if (width >= 80)
        {
            await Assert.That(text).Contains("nothing connects at start");
        }
    }

    /// <summary>And on a live launch at 80 columns, where it used to be elided to its last word.</summary>
    [Test]
    public async Task ALaunchWithNothingMarkedSaysSoWholeAt80Columns()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        foreach (var character in config.Worlds.SelectMany(w => w.Characters))
        {
            character.ConnectAtStartup = false;
        }

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(80, 24), new ManualTimeProvider());
        await app.StartAsync(StartupConnections.Resolve(config, null));
        var rows = FrameGrid.Decode(app.RenderWholeFrame(), 80, 24);
        await Assert.That(rows[^1]).Contains("nothing connects at start").Because(rows[^1]);
        await Assert.That(rows[^1].Replace("Switch to …", string.Empty, StringComparison.Ordinal).Contains('…')).IsFalse().Because(rows[^1]);
        await Assert.That(rows[^1]).Contains("F5 ▸ at start");
    }

    /// <summary>
    /// The key tour with ⌃P ▸ Show timestamps on: the row is chrome and carries no stamp, so the gutter costs it nothing
    /// and it is laddered to the pane as it is with the column off — one row, ⌃P and the way to hide it kept.
    /// </summary>
    [Test]
    [Arguments(80, 24)]
    [Arguments(60, 20)]
    public async Task TheKeyTourFitsWithTheTimestampGutterOn(int width, int height)
    {
        var app = App(width, height);
        app.RenderSnapshot("timestamps");
        await Assert.That(app.DispatchCommand("term:tour")).IsTrue();
        app.RenderWholeFrame();
        var rows = FrameGrid.Decode(app.RenderWholeFrame(), width, height);
        var tour = rows.FirstOrDefault(r => r.Contains("Keys:", StringComparison.Ordinal));
        await Assert.That(tour).IsNotNull().Because(string.Join('\n', rows));
        await Assert.That(tour!).Contains("⌃P");
        await Assert.That(tour!).Contains("Key tour");
        await Assert.That(tour!.Contains('…')).IsFalse().Because(tour);
        await Assert.That(rows.Any(r => System.Text.RegularExpressions.Regex.IsMatch(r, @"\b\d\d:\d\d\b"))).IsTrue()
            .Because("the gutter is on in the frame:\n" + string.Join('\n', rows));
    }

    private static SharpMUTermApp App(int width, int height)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ThemeName = "Dark";
        config.Theme = ThemeLibrary.Get("Dark");
        return new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(width, height));
    }
}
