using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// ⌃Z and ⌃Y on the cell settings screens: the section's last committed edit undone and redone through the save funnel,
/// said in the header, refused out loud when there is nothing, and kept out of the closing review once undone.
/// </summary>
[NotInParallel]
public class SettingsUndoTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly ConsoleKeyInfo Undo = new('\u001a', ConsoleKey.Z, false, false, true);
    private static readonly ConsoleKeyInfo Redo = new('\u0019', ConsoleKey.Y, false, false, true);

    private static SharpMUTermApp App(AppConfiguration config, Action? saved = null)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 34), save: _ => saved?.Invoke());
    }

    [Test]
    public async Task UndoPutsAResetBackAndRedoMakesItAgainEachSaved()
    {
        var config = DemoScene.Build();
        config.Input.LocalEcho = false;
        var saves = 0;
        var app = App(config, () => saves++);
        app.DispatchCommand("screen:input");
        app.SimulateSettingsKey(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));
        await Assert.That(config.Input.LocalEcho).IsTrue();
        await Assert.That(saves).IsEqualTo(1);

        app.SimulateSettingsKey(Undo);
        await Assert.That(config.Input.LocalEcho).IsFalse();
        await Assert.That(saves).IsEqualTo(2).Because("an undo is a change and goes through the save funnel");
        await Assert.That(app.SettingsFocus!.Value.Note!).StartsWith("Undid");

        app.SimulateSettingsKey(Redo);
        await Assert.That(config.Input.LocalEcho).IsTrue();
        await Assert.That(app.SettingsFocus!.Value.Note!).StartsWith("Redid");

        // The note lasts until the next key.
        app.SimulateSettingsKey(new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));
        await Assert.That(app.SettingsFocus!.Value.Note).IsNull();
    }

    [Test]
    public async Task NothingToUndoIsSaidInTheHeaderAndNothingIsSaved()
    {
        var saves = 0;
        var app = App(DemoScene.Build(), () => saves++);
        app.DispatchCommand("screen:input");
        app.SimulateSettingsKey(Undo);

        var note = app.SettingsFocus!.Value.Note;
        await Assert.That(note).IsNotNull();
        await Assert.That(note!).StartsWith("Nothing to undo in");
        await Assert.That(saves).IsEqualTo(0);

        var header = ScreenChrome.Hints(ScreenChrome.SingleListHints, "F8", focus: app.SettingsFocus, room: 100);
        await Assert.That(MarkupText.Plain(header)).Contains(note);
        await Assert.That(MarkupText.Plain(header)).Contains("⌃Z undo");
    }

    [Test]
    public async Task TheHeaderNamesTheUndoKeyWhereThereIsRoom()
    {
        var wide = MarkupText.Plain(ScreenChrome.Hints(ScreenChrome.SingleListHints, "F7", room: 120));
        await Assert.That(wide).Contains("⌃Z undo");

        // Narrow, it is given up before the screen's own keys and never before the way out.
        var narrow = MarkupText.Plain(ScreenChrome.Hints(ScreenChrome.SingleListHints, "F7", room: 30));
        await Assert.That(narrow).DoesNotContain("⌃Z");
        await Assert.That(narrow).Contains("F7/Esc close");
    }

    [Test]
    public async Task ADeletionUndoneIsNotReviewedOnTheWayOut()
    {
        var config = DemoScene.Build();
        var names = config.Worlds.Select(w => w.Name).ToList();
        var app = App(config);
        app.DispatchCommand("screen:worlds");
        app.SimulateSettingsKey(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));
        await Assert.That(config.Worlds.Count).IsEqualTo(names.Count - 1);

        app.SimulateSettingsKey(Undo);
        await Assert.That(config.Worlds.Select(w => w.Name)).IsEquivalentTo(names);

        app.SimulateSettingsKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));
        await Assert.That(app.Settings.IsOpen).IsFalse();
        await Assert.That(app.Settings.Review.IsOpen).IsFalse().Because("nothing is left deleted to ask about");
        await Assert.That(config.Worlds.Select(w => w.Name)).IsEquivalentTo(names).Because("and nothing is put back twice");
    }

    [Test]
    public async Task ADeletionRedoneIsReviewedAgain()
    {
        var config = DemoScene.Build();
        var app = App(config);
        app.DispatchCommand("screen:worlds");
        app.SimulateSettingsKey(new ConsoleKeyInfo('\0', ConsoleKey.Delete, false, false, false));
        app.SimulateSettingsKey(Undo);
        app.SimulateSettingsKey(Redo);
        app.SimulateSettingsKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));
        await Assert.That(app.Settings.Review.IsOpen).IsTrue();
    }

    [Test]
    public async Task TheHistoryOutlivesTheScreen()
    {
        var config = DemoScene.Build();
        config.Input.LocalEcho = false;
        var app = App(config);
        app.DispatchCommand("screen:input");
        app.SimulateSettingsKey(new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));
        app.SimulateSettingsKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));

        // Opened again, the screen undoes the edit made the last time it was open — and another screen does not.
        app.DispatchCommand("screen:textansi");
        app.SimulateSettingsKey(Undo);
        await Assert.That(config.Input.LocalEcho).IsTrue();
        app.SimulateSettingsKey(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));
        app.DispatchCommand("screen:input");
        app.SimulateSettingsKey(Undo);
        await Assert.That(config.Input.LocalEcho).IsFalse();
    }

    [Test]
    public async Task UndoInsideAFieldIsNotTheScreens()
    {
        var app = App(DemoScene.Build());
        app.DispatchCommand("screen:worlds");
        app.SimulateSettingsKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        await Assert.That(app.Settings.Focus!.Value.IsEditing).IsTrue();

        app.SimulateSettingsKey(Undo);
        await Assert.That(app.Settings.Focus!.Value.IsEditing).IsTrue().Because("the open field has the whole keyboard");
        await Assert.That(app.Settings.Focus!.Value.Note).IsNull();
    }
}
