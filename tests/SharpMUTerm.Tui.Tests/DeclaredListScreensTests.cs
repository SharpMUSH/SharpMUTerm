using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// F2, F3, F4 and F6 drawn from their declarations: the cell screens' fields are the declared collection's, an edit
/// on one lands on the rule and is saved once, ⌥↑/⌥↓ reorder a rule within its set, and a host with a settings window
/// shows them there — the trigger builder included, on the rule it made.
/// </summary>
[NotInParallel]
public class DeclaredListScreensTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class Presenter : INativeSettings
    {
        public List<SettingsForm> Shown { get; } = [];

        public void Show(SettingsForm form) => Shown.Add(form);

        public void Close()
        {
        }
    }

    private static (SharpMUTermApp App, Presenter Native, List<AppConfiguration> Saves) App(bool native)
    {
        Console.SetIn(TextReader.Null);
        var presenter = new Presenter();
        var saves = new List<AppConfiguration>();
        var host = ClientHost.Headless(new HeadlessConsoleDriver(120, 32), Caps) with
        {
            NativeSettings = native ? presenter : null,
        };
        return (new SharpMUTermApp(DemoScene.Build(), host, save: saves.Add), presenter, saves);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, bool alt = false) => new('\0', key, false, alt, false);

    [Test]
    public async Task EachCellScreenDrawsExactlyTheDeclaredFields()
    {
        static IEnumerable<string> Ids(SettingsSectionContribution section) =>
            section.Collection!.Fields.Select(f => f.Id).OrderBy(id => id, StringComparer.Ordinal);

        static IEnumerable<string> Cells(IEnumerable<string> row, IEnumerable<string> editor) =>
            row.Concat(editor).Append("enabled").Distinct().OrderBy(id => id, StringComparer.Ordinal);

        await Assert.That(Cells(TriggersScreenRenderer.RowFieldOrder, TriggersScreenRenderer.EditorFieldOrder))
            .IsEquivalentTo(Ids(BuiltInSettings.Triggers));
        await Assert.That(Cells(AliasesScreenRenderer.RowFieldOrder, AliasesScreenRenderer.EditorFieldOrder))
            .IsEquivalentTo(Ids(BuiltInSettings.Aliases));
        await Assert.That(Cells(TimersScreenRenderer.RowFieldOrder, TimersScreenRenderer.EditorFieldOrder))
            .IsEquivalentTo(Ids(BuiltInSettings.Timers));
        await Assert.That(Cells(KeypadScreenRenderer.RowFieldOrder, []))
            .IsEquivalentTo(Ids(BuiltInSettings.Keypad));

        // The ordinals the screen, the snapshot keys and the tests address are where the declarations put them.
        await Assert.That(TriggersScreenRenderer.RowFieldOrder[TriggersScreenRenderer.MatchTypeField]).IsEqualTo("match");
        await Assert.That(TriggersScreenRenderer.RowFieldOrder[TriggersScreenRenderer.PatternField]).IsEqualTo("pattern");
    }

    [Test]
    public async Task ACellEditLandsOnTheRuleAndIsSavedOnce()
    {
        var (app, _, saves) = App(native: false);
        app.SimulateKey(Key(ConsoleKey.F2));
        var first = app.Configuration.TriggerSets.SelectMany(s => s.Triggers).First();
        var was = first.Enabled;

        app.Settings.SimulateKey(new ConsoleKeyInfo(' ', ConsoleKey.Spacebar, false, false, false));

        await Assert.That(first.Enabled).IsEqualTo(!was);
        await Assert.That(saves.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AltArrowsMoveARuleWithinItsSetAndTheCursorGoesWithIt()
    {
        var (app, _, saves) = App(native: false);
        app.SimulateKey(Key(ConsoleKey.F2));
        var set = app.Configuration.TriggerSets.First(s => s.Triggers.Count >= 2);
        var top = set.Triggers[0];
        var next = set.Triggers[1];

        app.Settings.SimulateKey(Key(ConsoleKey.DownArrow, alt: true));

        await Assert.That(set.Triggers[0]).IsSameReferenceAs(next);
        await Assert.That(set.Triggers[1]).IsSameReferenceAs(top);
        await Assert.That(saves.Count).IsEqualTo(1);
        await Assert.That(app.SettingsFocus!.Value.Index).IsEqualTo(1);

        // At the top of its set the key does nothing and saves nothing.
        app.Settings.SimulateKey(Key(ConsoleKey.UpArrow, alt: true));
        app.Settings.SimulateKey(Key(ConsoleKey.UpArrow, alt: true));
        await Assert.That(set.Triggers[0]).IsSameReferenceAs(top);
        await Assert.That(saves.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TheListKeysOpenTheHostsWindowOnTheirSection()
    {
        var (app, native, _) = App(native: true);

        foreach (var (key, id) in new[] { (ConsoleKey.F2, "triggers"), (ConsoleKey.F3, "aliases"), (ConsoleKey.F4, "keypad"), (ConsoleKey.F6, "timers") })
        {
            app.SimulateKey(Key(key));
            await Assert.That(native.Shown[^1].Initial).IsEqualTo(id);
            await Assert.That(app.SettingsOpenKey).IsNull();
        }
    }

    [Test]
    public async Task AnEditInTheWindowLandsOnTheSameRuleAndIsSavedOnce()
    {
        var (app, native, saves) = App(native: true);
        app.SimulateKey(Key(ConsoleKey.F2));
        var form = native.Shown.Single();
        var rule = app.Configuration.TriggerSets.SelectMany(s => s.Triggers).First();
        form.Select(rule);

        await Assert.That(form.SetText((TextField)BuiltInSettings.Triggers.Collection!.Field("pattern"), "you gain")).IsNull();

        await Assert.That(rule.Pattern).IsEqualTo("you gain");
        await Assert.That(saves.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TheTriggerBuilderOpensTheWindowOnTheRuleItMade()
    {
        var (app, native, _) = App(native: true);

        app.RenderSnapshot("trigger-builder");

        var made = app.Configuration.TriggerSets.SelectMany(s => s.Triggers).Last(t => t.Name.StartsWith("You gain", StringComparison.Ordinal));
        var form = native.Shown.Single();
        await Assert.That(form.Initial).IsEqualTo("triggers");
        await Assert.That(form.Item).IsSameReferenceAs(made);
        await Assert.That(app.SettingsOpenKey).IsNull();
        await Assert.That(form.Preview()!.Value.Matched).IsEqualTo(2);
    }

    [Test]
    public async Task TheTriggerBuilderOpensTheCellEditorWhereThereIsNoWindow()
    {
        var (app, _, _) = App(native: false);

        app.RenderSnapshot("trigger-builder");

        await Assert.That(app.SettingsOpenKey).IsEqualTo(ConsoleKey.F2);
    }
}
