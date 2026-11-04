using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// ⌃P as the place keys are learnt: fuzzy matching over the real catalog, the RECENT group filled by the
/// palette's own dispatch path, and the key clashes F4 marks — every one read off the real app, and the
/// frames off the real paint.
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class PaletteDiscoverabilityTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp Demo(int width = 120, int height = 32)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(width, height));
    }

    private static string Text(string frame, int width = 120, int height = 32) =>
        string.Join("\n", FrameGrid.Decode(frame, width, height));

    [Test]
    public async Task TrigSelPutsTheTriggerBuilderFirst_InTheRealSurface()
    {
        var app = Demo();
        var frame = Text(app.RenderSnapshot("menu-fuzzy"));

        await Assert.That(app.Palette.IsOpen).IsTrue();
        await Assert.That(app.Palette.View.Entries[0].Item.Id).IsEqualTo("term:trigger-from-selection");
        await Assert.That(frame).Contains("Make a trigger from the selection");
    }

    [Test]
    public async Task CommandsRunFromThePaletteAreListedUnderRecent_NewestFirst()
    {
        var app = Demo();
        var frame = Text(app.RenderSnapshot("menu-recent"));

        await Assert.That(app.RecentCommandIds).IsEquivalentTo(new[] { "layout:cycle", "layout:split-right" });
        var entries = app.Palette.View.Entries.Select(e => e.Item.Id).ToList();
        await Assert.That(entries[0]).IsEqualTo("layout:cycle");
        await Assert.That(entries[1]).IsEqualTo("layout:split-right");
        await Assert.That(entries.Count(id => id == "layout:cycle")).IsEqualTo(1);
        await Assert.That(frame.IndexOf("RECENT", StringComparison.Ordinal))
            .IsLessThan(frame.IndexOf("GO TO", StringComparison.Ordinal));
    }

    [Test]
    public async Task AKeyIsNeverElidedOnANarrowTerminal()
    {
        var app = Demo(80, 24);
        var frame = Text(app.RenderSnapshot("menu"), 80, 24);

        // The copy entry's detail is longer than the surface: it is elided, and its key is still drawn whole.
        var copy = frame.Split('\n').Single(l => l.Contains("Copy the selection", StringComparison.Ordinal));
        await Assert.That(copy).Contains("…");
        await Assert.That(copy.TrimEnd(' ', '░', '│')).EndsWith("⌃C");
    }

    [Test]
    public async Task KeyClashesAreCountedOnThePalette_AndOpenTheKeypadScreen()
    {
        var app = Demo();
        app.RenderSnapshot();
        await Assert.That(app.BuildCatalog().Any(c => c.Id == "settings:key-clashes")).IsFalse();

        var macros = app.Configuration.TriggerSets[0].Macros;
        macros.Add(new Macro { Key = "Ctrl+C", Command = "kill", Name = "kill" });   // wins over copy
        macros.Add(new Macro { Key = "F2", Command = "score", Name = "score" });     // F2 opens Triggers first
        macros.Add(new Macro { Key = "F11", Command = "north", Name = "north" });
        macros.Add(new Macro { Key = "F11", Command = "south", Name = "south" });    // never runs

        var entry = app.BuildCatalog().Single(c => c.Id == "settings:key-clashes");
        await Assert.That(entry.Title).IsEqualTo("Key clashes (3)");

        await Assert.That(app.DispatchCommand("settings:key-clashes")).IsTrue();
        await Assert.That(app.Settings.IsOpen).IsTrue();
        var frame = Text(app.RenderWholeFrame());
        await Assert.That(frame).Contains("△ Ctrl+C");
        await Assert.That(frame).Contains("hide a client key");
    }
}

public class KeyClashesTests
{
    private static Macro M(string key, string command = "x", string name = "") =>
        new() { Key = key, Command = command, Name = name };

    [Test]
    public async Task AGlobalShortcutWins_AndSaysSo()
    {
        var f2 = M("F2");
        var clash = KeyClashes.For(f2, [f2]);
        await Assert.That(clash?.Kind).IsEqualTo(KeyClashKind.ClientWins);
        await Assert.That(clash!.Value.Reason).Contains("the client wins");
        await Assert.That(clash.Value.BindingRuns).IsFalse();
    }

    [Test]
    public async Task AChordTheWindowTakesAfterTheMacrosIsTheBindingsAndAWarning()
    {
        var copy = M("Ctrl+C");
        var clash = KeyClashes.For(copy, [copy]);
        await Assert.That(clash?.Kind).IsEqualTo(KeyClashKind.BindingWins);
        await Assert.That(clash!.Value.BindingRuns).IsTrue();
        await Assert.That(clash.Value.Reason).Contains("no longer copies the selection");

        // Verdict agrees that the binding fires: the warning is not a second opinion on deliverability.
        await Assert.That(MacroKeys.Verdict("Ctrl+C").Fires).IsTrue();
    }

    [Test]
    public async Task TwoBindingsOnOneChord_TheFirstAnswers_EvenDisabled()
    {
        var first = M("F11", name: "north");
        first.Enabled = false;
        var second = M("F11", name: "south");
        await Assert.That(KeyClashes.For(second, [first, second])?.Kind).IsEqualTo(KeyClashKind.Shadowed);
        await Assert.That(KeyClashes.For(second, [first, second])!.Value.Reason).Contains("north holds F11 first");

        // A disabled binding clashes with nothing itself.
        await Assert.That(KeyClashes.For(first, [first, second])).IsNull();

        first.Enabled = true;
        await Assert.That(KeyClashes.For(first, [first, second])?.Kind).IsEqualTo(KeyClashKind.Shadows);
        await Assert.That(KeyClashes.Count([first, second])).IsEqualTo(1);
    }

    [Test]
    public async Task AFreeChordClashesWithNothing()
    {
        var free = M("F11");
        await Assert.That(KeyClashes.For(free, [free])).IsNull();
        await Assert.That(KeyClashes.For(M("Num5"), [M("Num5")])).IsNull(); // never arrives: not a clash
    }

    [Test]
    public async Task EveryYieldedChordIsOneTheVerdictSaysFires()
    {
        // The warning list may only name chords a binding can actually be written on and fire on.
        foreach (var chord in new[] { "Ctrl+C", "Ctrl+Left", "Alt+Shift+Up", "Shift+Up", "Alt+Up", "Ctrl+A", "Ctrl+X" })
        {
            await Assert.That(MacroKeys.YieldedBy(chord)).IsNotNull();
            await Assert.That(MacroKeys.Verdict(chord).Fires).IsTrue();
        }
    }
}
