using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// F5's world fields and F9's character fields are declared, and the cell screen and the desktop's settings window are
/// both drawn from the declarations: they list the same fields, an edit in either reaches the same property through the
/// one save funnel, and F7's theme picker previews as the cursor moves, reverts on Esc and commits once on ⏎.
/// </summary>
[NotInParallel]
public class DeclaredWorldSettingsTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static (SharpMUTermApp App, AppConfiguration Config, Func<int> Saves) App()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        var saves = 0;
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 40), save: _ => saves++);
        return (app, config, () => saves);
    }

    private static void Key(SharpMUTermApp app, ConsoleKey key, char ch = '\0') =>
        app.SimulateSettingsKey(new ConsoleKeyInfo(ch, key, false, false, false));

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var ch in text)
        {
            app.SimulateSettingsKey(new ConsoleKeyInfo(ch, char.IsLetter(ch) ? (ConsoleKey)char.ToUpperInvariant(ch) : ConsoleKey.Oem1, char.IsUpper(ch), false, false));
        }
    }

    [Test]
    public async Task TheCellScreenAndTheWindowListTheSameWorldFields()
    {
        var declared = BuiltInSettings.World.Fields.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var drawn = WorldsScreenRenderer.WorldFieldOrder.Concat(WorldsScreenRenderer.SecurityFieldOrder).ToList();

        await Assert.That(drawn.Count).IsEqualTo(declared.Count);
        await Assert.That(drawn.ToHashSet(StringComparer.Ordinal).SetEquals(declared)).IsTrue();

        var worlds = DemoScene.Build().Worlds;
        var model = WorldsScreenRenderer.Model(worlds, [], 0, 0);
        for (var i = 0; i < WorldsScreenRenderer.WorldFieldOrder.Count; i++)
        {
            var field = BuiltInSettings.World.Fields.Single(f => f.Id == WorldsScreenRenderer.WorldFieldOrder[i]);
            await Assert.That(model.FieldAt(0, 0, i)!.Value.Label).IsEqualTo(field.Label);
        }
    }

    [Test]
    public async Task TheCellScreenAndTheWindowListTheSameCharacterFields()
    {
        var declared = BuiltInSettings.Character.Fields.Select(f => f.Id).ToList();
        await Assert.That(WorldsScreenRenderer.CharacterFieldOrder.ToHashSet(StringComparer.Ordinal).SetEquals(declared)).IsTrue();
        await Assert.That(WorldsScreenRenderer.CharacterFieldOrder.Count).IsEqualTo(declared.Count);

        var worlds = DemoScene.Build().Worlds;
        var model = WorldsScreenRenderer.Model(worlds, [], 0, 0);
        for (var i = 0; i < WorldsScreenRenderer.CharacterFieldOrder.Count; i++)
        {
            var field = BuiltInSettings.Character.Fields.Single(f => f.Id == WorldsScreenRenderer.CharacterFieldOrder[i]);
            await Assert.That(model.FieldAt(1, 0, i)!.Value.Label).IsEqualTo(field.Label);
        }
    }

    [Test]
    public async Task AWindowEditReachesTheWorldAndIsSavedOnce()
    {
        var (app, config, saves) = App();
        var form = app.NativeSettingsForm("worlds");
        var world = form.Subject(SettingScope.World)!.World;

        await Assert.That(form.SetText((TextField)BuiltInSettings.World.Fields.Single(f => f.Id == "edit-grab"), "Edit> ")).IsNull();

        await Assert.That(world.GrabPrefix).IsEqualTo("Edit>");
        await Assert.That(config.Worlds.Contains(world)).IsTrue();
        await Assert.That(saves()).IsEqualTo(1);

        form.Reset(BuiltInSettings.World.Fields.Single(f => f.Id == "edit-grab"));
        await Assert.That(world.GrabPrefix).IsNull();
        await Assert.That(saves()).IsEqualTo(2);
    }

    [Test]
    public async Task AWindowEditOfACharacterReachesTheCellScreensCharacter()
    {
        var (app, config, saves) = App();
        var form = app.NativeSettingsForm("logging");
        var character = form.Subject(SettingScope.Character)!.Character!;

        form.Set((SettingBinding<string>)BuiltInSettings.Character.Fields.Single(f => f.Id == "tint").Binding!, "Rose");

        await Assert.That(character.Tint).IsEqualTo(PaneTint.Rose);
        await Assert.That(saves()).IsEqualTo(1);

        // The cell screen's tint field reads the same property through the same declaration.
        var world = config.Worlds.First(w => w.Characters.Contains(character));
        var model = WorldsScreenRenderer.Model(config.Worlds, [], config.Worlds.IndexOf(world), world.Characters.IndexOf(character));
        var tint = WorldsScreenRenderer.CharacterFieldOrder.ToList().IndexOf("tint");
        var row = world.Characters.IndexOf(character);
        await Assert.That(model.FieldAt(1, row, tint)!.Value.Get()).IsEqualTo("Rose");
    }

    [Test]
    public async Task ACellEditReachesTheSamePropertyThroughTheSaveFunnel()
    {
        var (app, config, saves) = App();
        var world = config.Worlds[0];
        var grab = WorldsScreenRenderer.WorldFieldOrder.ToList().IndexOf("edit-grab");

        app.DispatchCommand("screen:worlds");
        Key(app, ConsoleKey.Enter);
        for (var i = 0; i < grab; i++)
        {
            Key(app, ConsoleKey.Tab, '\t');
        }

        Type(app, "Edit;");
        Key(app, ConsoleKey.Enter, '\r');

        await Assert.That(world.GrabPrefix).IsEqualTo("Edit;");
        await Assert.That(saves()).IsGreaterThan(0);
    }

    [Test]
    public async Task MovingOverAThemePreviewsItAndEscCommitsNothing()
    {
        var (app, config, saves) = App();
        app.DispatchCommand("screen:textansi");
        Key(app, ConsoleKey.DownArrow);

        await Assert.That(app.ThemeName).IsEqualTo("Dark");
        await Assert.That(config.ThemeName).IsEqualTo("Dark");

        Key(app, ConsoleKey.Escape, '\u001b');

        await Assert.That(app.SettingsOpenKey).IsNull();
        await Assert.That(app.ThemeName).IsEqualTo("Dark");
        await Assert.That(saves()).IsEqualTo(0);
    }

    [Test]
    public async Task EnterOnAThemeUsesItAndSavesOnce()
    {
        var (app, config, saves) = App();
        app.DispatchCommand("screen:textansi");
        Key(app, ConsoleKey.DownArrow);
        Key(app, ConsoleKey.Enter, '\r');

        await Assert.That(config.ThemeName).IsEqualTo("Light");
        await Assert.That(app.ThemeName).IsEqualTo("Light");
        await Assert.That(saves()).IsEqualTo(1);
    }

    /// <summary>Space on a theme row is ⏎, as it is the commit on a checkbox row: the cursor already previews.</summary>
    [Test]
    public async Task SpaceOnAThemeUsesItAsEnterDoes()
    {
        var (app, config, saves) = App();
        app.DispatchCommand("screen:textansi");
        Key(app, ConsoleKey.DownArrow);
        Key(app, ConsoleKey.Spacebar, ' ');

        await Assert.That(config.ThemeName).IsEqualTo("Light");
        await Assert.That(app.ThemeName).IsEqualTo("Light");
        await Assert.That(saves()).IsEqualTo(1);
        await Assert.That(app.SettingsOpenKey).IsNotNull();
    }

    [Test]
    public async Task F7OpensWithTheCursorOnTheThemeInForce()
    {
        var (app, config, _) = App();
        ThemeChoices.Apply(config, "Solarized Dark");
        app.DispatchCommand("screen:textansi");

        await Assert.That(app.SettingsFocus!.Value.Index).IsEqualTo(ThemeChoices.Names(config).ToList().IndexOf("Solarized Dark"));
    }

    [Test]
    public async Task TheWindowsThemeCardsAreTheCellPickersThemes()
    {
        var (app, config, _) = App();
        var form = app.NativeSettingsForm("text");

        await Assert.That(form.Themes().Select(c => c.Name)).IsEquivalentTo(ThemeChoices.Names(config));

        form.SetTheme((ThemeField)BuiltInSettings.Text.Fields.Single(f => f.Id == BuiltInSettings.ThemeFieldId), "Light");
        await Assert.That(app.ThemeName).IsEqualTo("Light");
        await Assert.That(form.IsDark).IsFalse();
    }
}
