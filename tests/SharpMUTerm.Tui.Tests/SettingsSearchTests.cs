using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The settings rebuilt from declarations, end to end through the overlay: <c>/</c> opens the search over any
/// screen, typing narrows it, ⏎ lands on the field's own row — on another screen too — and Esc puts the screen
/// back. And the changed marks: a field away from its default says so, and <c>r</c> puts the default back
/// through the same save as every other edit.
/// </summary>
[NotInParallel]
public class SettingsSearchTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App(AppConfiguration? config = null, Action<AppConfiguration>? save = null)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(
            config ?? DemoScene.Build(), Headless, new HeadlessConsoleDriver(120, 34), save: save);
    }

    private static ConsoleKeyInfo Key(char c, ConsoleKey key = ConsoleKey.NoName) => new(c, key, false, false, false);

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateSettingsKey(Key(c));
        }
    }

    [Test]
    public async Task SlashOpensTheSearchAndEscPutsTheScreenBack()
    {
        var app = App();
        app.DispatchCommand("screen:input");

        app.SimulateSettingsKey(Key('/', ConsoleKey.Oem2));
        await Assert.That(app.SettingsSearchIsOpen).IsTrue();

        app.SimulateSettingsKey(Key('\0', ConsoleKey.Escape));
        await Assert.That(app.SettingsSearchIsOpen).IsFalse();
        await Assert.That(app.SettingsOpenKey).IsEqualTo(ConsoleKey.F8);
    }

    /// <summary>The search is opened over F8 and goes to a field of F7: the screen changes, and the cursor is on that field's row.</summary>
    [Test]
    public async Task EnterGoesToTheFieldOnItsOwnScreen()
    {
        var app = App();
        app.DispatchCommand("screen:input");
        app.SimulateSettingsKey(Key('/', ConsoleKey.Oem2));
        Type(app, "meter");

        app.SimulateSettingsKey(Key('\r', ConsoleKey.Enter));

        await Assert.That(app.SettingsSearchIsOpen).IsFalse();
        await Assert.That(app.SettingsOpenKey).IsEqualTo(ConsoleKey.F7);
        await Assert.That(app.SettingsFocus!.Value.Index).IsEqualTo(app.SettingsFieldRow("text", "meters")!.Value);
    }

    /// <summary>A section with no declared fields is found by its title and opened by ⏎.</summary>
    [Test]
    public async Task ASectionIsFoundByItsTitle()
    {
        var app = App();
        app.DispatchCommand("screen:textansi");
        app.SimulateSettingsKey(Key('/', ConsoleKey.Oem2));
        Type(app, "timers");

        app.SimulateSettingsKey(Key('\r', ConsoleKey.Enter));

        await Assert.That(app.SettingsOpenKey).IsEqualTo(ConsoleKey.F6);
    }

    /// <summary>Inside an open field `/` is a character, not the search: the field has the whole keyboard.</summary>
    [Test]
    public async Task SlashInsideAFieldIsTyped()
    {
        var app = App();
        app.DispatchCommand("screen:input");
        app.SimulateSettingsKey(Key('\0', ConsoleKey.DownArrow));
        app.SimulateSettingsKey(Key('\0', ConsoleKey.DownArrow));
        app.SimulateSettingsKey(Key('\0', ConsoleKey.DownArrow));
        app.SimulateSettingsKey(Key('\r', ConsoleKey.Enter));

        app.SimulateSettingsKey(Key('/', ConsoleKey.Oem2));

        await Assert.That(app.SettingsSearchIsOpen).IsFalse();
        await Assert.That(app.SettingsFocus!.Value.Edit!.Value.Text).EndsWith("/");
    }

    /// <summary>
    /// <c>r</c> on a changed field puts its default back and saves, the way every other committed change on
    /// these screens does; on a field at its default it is not taken.
    /// </summary>
    [Test]
    public async Task RResetsAChangedFieldThroughTheSave()
    {
        var config = DemoScene.Build();
        config.Input.LocalEcho = false;
        var saves = 0;
        var app = App(config, _ => saves++);
        app.DispatchCommand("screen:input");

        app.SimulateSettingsKey(Key('r', ConsoleKey.R));

        await Assert.That(config.Input.LocalEcho).IsTrue();
        await Assert.That(saves).IsEqualTo(1);

        app.SimulateSettingsKey(Key('r', ConsoleKey.R));
        await Assert.That(saves).IsEqualTo(1);
    }

    [Test]
    public async Task AChangedFieldIsMarkedAndTheHeaderOffersTheReset()
    {
        var input = new InputSettings { Rows = 5 };
        var screen = OptionsScreenRenderer.InputScreen(input);
        var model = OptionsScreenRenderer.Model(screen);
        var lines = OptionsScreenRenderer.BodyColumn(screen.Rows);
        var row = OptionsScreenRenderer.IndexOf(screen, "rows");

        await Assert.That(lines.Single(l => l.Contains("height (lines)")))
            .Contains(OptionsScreenRenderer.ChangedMark);
        await Assert.That(lines.Single(l => l.Contains("local echo")))
            .DoesNotContain(OptionsScreenRenderer.ChangedMark);
        await Assert.That(OptionsScreenRenderer.HeaderLine("Input", "F8", 0, model, new ScreenFocus(0, row)))
            .Contains("r reset");
        await Assert.That(OptionsScreenRenderer.HeaderLine("Input", "F8", 0, model, new ScreenFocus(0, 0)))
            .DoesNotContain("r reset");

        model.ResetAt(0, row)!();
        await Assert.That(input.Rows).IsEqualTo(new InputSettings().Rows);
    }

    /// <summary>Every settings screen names the search in its header, because every one of them answers `/`.</summary>
    [Test]
    public async Task EveryScreenNamesTheSearch()
    {
        var header = OptionsScreenRenderer.HeaderLine("Input", "F8", 0, OptionsScreenRenderer.Model(OptionsScreenRenderer.InputScreen()));
        await Assert.That(header).Contains("/ search");
    }
}
