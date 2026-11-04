using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The trigger builder: ⌃P ▸ <c>Make a trigger from the selection</c> and F2's live test. Every frame here
/// is the real one — a real drag through the pane's own mouse handler, the real ⌃P entry, and the real
/// settings session typing into the pattern field.
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class TriggerBuilderTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp Demo(int width = 120, int height = 32)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(width, height));
    }

    private static string Text(string frame) => string.Join("\n", FrameGrid.Decode(frame, 120, 32));

    [Test]
    public async Task TheEntryIsOfferedOnlyWhileOutputIsSelected()
    {
        var app = Demo();
        app.RenderSnapshot();
        await Assert.That(app.BuildCatalog().Any(c => c.Id == "term:trigger-from-selection")).IsFalse();

        app.SimulatePaneDrag(SharpMUTermApp.MainWindowId, 0, 0, 10, 0);

        await Assert.That(app.BuildCatalog().Any(c => c.Id == "term:trigger-from-selection")).IsTrue();
    }

    [Test]
    public async Task WithNothingSelectedItRefusesAndMakesNothing()
    {
        var app = Demo();
        app.RenderSnapshot();
        var before = app.Configuration.TriggerSets.Sum(s => s.Triggers.Count);

        await Assert.That(app.DispatchCommand("term:trigger-from-selection")).IsFalse();
        await Assert.That(app.Configuration.TriggerSets.Sum(s => s.Triggers.Count)).IsEqualTo(before);
        await Assert.That(app.Settings.IsOpen).IsFalse();
    }

    [Test]
    public async Task TheViewOpensF2OnAWildcardRuleWithTheLiveTestCounting()
    {
        var app = Demo();
        var frame = Text(app.RenderSnapshot("trigger-builder"));

        var made = app.Configuration.TriggerSets.SelectMany(s => s.Triggers).Last(t => t.Name.StartsWith("You gain", StringComparison.Ordinal));
        await Assert.That(made.MatchType).IsEqualTo(TriggerMatchType.Wildcard);
        await Assert.That(made.Pattern).IsEqualTo("You gain * experience points.");
        await Assert.That(app.Settings.IsOpen).IsTrue();
        await Assert.That(frame).Contains("matches with * wildcards");
        await Assert.That(frame).Contains("regex You gain (.*?) experience points\\.");
        await Assert.That(frame).Contains("2 of the last");
        await Assert.That(frame).Contains("You gain 125 experience points.");
    }

    [Test]
    public async Task TheLiveTestFollowsTheBufferKeystrokeByKeystroke()
    {
        var app = Demo();
        app.RenderSnapshot("trigger-builder");

        // The pattern field is open on the suggestion: Home, then type a word that matches nothing.
        app.Settings.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false));
        foreach (var c in "Never ")
        {
            app.Settings.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        var frame = Text(app.RenderWholeFrame());

        await Assert.That(frame).Contains("none of the last");
        await Assert.That(frame).Contains("Never You gain * experience points.");
    }

    [Test]
    public async Task TheViewFitsAt80Columns()
    {
        var app = Demo(80, 24);
        var rows = FrameGrid.Decode(app.RenderSnapshot("trigger-builder"), 80, 24);

        await Assert.That(string.Join("\n", rows)).Contains("2 of the last");
        await Assert.That(rows.All(r => r.TrimEnd().Length <= 80)).IsTrue();
    }

    [Test]
    public async Task AnUnparseablePatternIsDrawnAsAStateAndTheRowsBelowDoNotMove()
    {
        var good = TriggersScreenRenderer.TestRows(new[] { "abc" }, TriggerMatchType.Regex, "b", false, 60);
        var bad = TriggersScreenRenderer.TestRows(new[] { "abc" }, TriggerMatchType.Regex, "(b", false, 60);
        var empty = TriggersScreenRenderer.TestRows(Array.Empty<string>(), TriggerMatchType.Contains, "b", false, 60);

        await Assert.That(bad[0]).Contains("not a pattern yet");
        await Assert.That(bad[0]).DoesNotContain(ScreenPalette.Warn);
        await Assert.That(empty[0]).Contains("no output held yet");
        await Assert.That(good.Count).IsEqualTo(TriggersScreenRenderer.TestSampleRows + 1);
        await Assert.That(bad.Count).IsEqualTo(good.Count);
        await Assert.That(empty.Count).IsEqualTo(good.Count);
    }

    [Test]
    public async Task ASampleKeepsItsMatchOnScreenWhenTheLineIsLong()
    {
        var line = new string('x', 100) + "HIT" + new string('y', 100);

        var sample = TriggersScreenRenderer.Sample(line, 100, 3, 30);

        await Assert.That(sample).Contains("HIT");
        await Assert.That(MarkupText.VisibleLength(sample)).IsLessThanOrEqualTo(30);
    }

    [Test]
    public async Task SwitchingToRegexKeepsWhatTheRuleMatched()
    {
        var trigger = new Trigger { Pattern = "You gain * xp.", MatchType = TriggerMatchType.Wildcard };

        TriggersScreenRenderer.SetMatchType(trigger, TriggerMatchType.Regex);

        await Assert.That(trigger.Pattern).IsEqualTo("You gain (.*?) xp\\.");
        await Assert.That(trigger.Regex.IsMatch("You gain 5 xp.")).IsTrue();
    }
}
