using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Trigger sets are made, renamed and given their characters where rules are written: <c>s</c> on F2, F3, F4 and F6
/// opens the trigger sets screen over the list, every set heading there says who uses it, and the sets screen commits
/// through the list's own log, so its undo and closing review cover it.
/// </summary>
[NotInParallel]
public class TriggerSetsScreenTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static (SharpMUTermApp App, List<AppConfiguration> Saves) App()
    {
        Console.SetIn(TextReader.Null);
        var saves = new List<AppConfiguration>();
        var host = ClientHost.Headless(new HeadlessConsoleDriver(120, 32), Caps);
        return (new SharpMUTermApp(DemoScene.Build(), host, save: saves.Add), saves);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0', bool ctrl = false) => new(c, key, false, false, ctrl);

    [Test]
    public async Task SOnEveryListScreenOpensTheTriggerSetsScreenAndEscGoesBack()
    {
        foreach (var key in new[] { ConsoleKey.F2, ConsoleKey.F3, ConsoleKey.F4, ConsoleKey.F6 })
        {
            var (app, _) = App();
            app.SimulateKey(Key(key));
            await Assert.That(app.Settings.IsShowingDetail).IsFalse();

            app.Settings.SimulateKey(Key(ConsoleKey.S, 's'));
            await Assert.That(app.Settings.IsShowingDetail).IsTrue();
            await Assert.That(app.RenderWholeFrame()).Contains(TriggerSetsScreenRenderer.Title);

            app.Settings.SimulateKey(Key(ConsoleKey.Escape));
            await Assert.That(app.Settings.IsShowingDetail).IsFalse();
            await Assert.That(app.Settings.IsOpen).IsTrue();
        }
    }

    [Test]
    public async Task TheListScreensSayWhoUsesEachSetAndNameTheKey()
    {
        var (app, _) = App();
        app.SimulateKey(Key(ConsoleKey.F2));
        var frame = app.RenderWholeFrame();

        await Assert.That(frame).Contains("s sets");
        await Assert.That(frame).Contains("used by ");
        await Assert.That(frame).Contains("make, rename, choose who uses them");
    }

    [Test]
    public async Task ItOpensOnTheSetHoldingTheSelectedRule()
    {
        var (app, _) = App();
        app.SimulateKey(Key(ConsoleKey.F2));
        var sets = app.Configuration.TriggerSets;
        var first = sets[0].Triggers.Count;

        // Down to the first rule of the second set that has any.
        var target = Enumerable.Range(1, sets.Count - 1).First(i => sets[i].Triggers.Count > 0);
        var skip = sets.Take(target).Sum(s => s.Triggers.Count);
        for (var i = 0; i < skip; i++)
        {
            app.Settings.SimulateKey(Key(ConsoleKey.DownArrow));
        }

        app.Settings.SimulateKey(Key(ConsoleKey.S, 's'));

        await Assert.That(first).IsGreaterThan(0);
        await Assert.That(app.SettingsFocus!.Value.Pane).IsEqualTo(TriggerSetsScreenRenderer.SetsPane);
        await Assert.That(app.SettingsFocus!.Value.Index).IsEqualTo(target);
    }

    [Test]
    public async Task ANewSetIsUsedByTheCharacterInFrontAndF2CanPutARuleInIt()
    {
        var (app, saves) = App();
        app.SimulateKey(Key(ConsoleKey.F2));
        app.Settings.SimulateKey(Key(ConsoleKey.S, 's'));
        var before = app.Configuration.TriggerSets.Count;

        app.Settings.SimulateKey(Key(ConsoleKey.End));
        app.Settings.SimulateKey(Key(ConsoleKey.Enter, '\r'));

        var made = app.Configuration.TriggerSets[^1];
        await Assert.That(app.Configuration.TriggerSets.Count).IsEqualTo(before + 1);
        await Assert.That(made.Name).IsEqualTo(TriggerSetEditing.NewSetName);
        await Assert.That(TriggerSetEditing.IsUsed(app.Configuration.Worlds, made.Name)).IsTrue();
        await Assert.That(saves.Count).IsEqualTo(1);

        // Back on F2 the new set is a heading, an empty one, and the rule's set field offers it.
        app.Settings.SimulateKey(Key(ConsoleKey.Escape));
        await Assert.That(app.RenderWholeFrame()).Contains(TriggerSetEditing.NewSetName);
    }

    [Test]
    public async Task ANewSetMadeFromAWindowNobodyOwnsIsUsedByNobody()
    {
        // A character's session left active behind the web view, which belongs to nobody: the set must not go to them.
        var (app, _) = App();
        app.TelnetFactory = _ => new RecordingTelnetSession();
        await Assert.That(app.DispatchCommand(CommandIds.Character("Aetherfall.Corvid"))).IsTrue();
        await app.FindSession("Aetherfall.Corvid")!.ConnectAsync();
        app.SimulateWebPage();
        app.SimulateKey(Key(ConsoleKey.F2));
        app.Settings.SimulateKey(Key(ConsoleKey.S, 's'));

        app.Settings.SimulateKey(Key(ConsoleKey.End));
        app.Settings.SimulateKey(Key(ConsoleKey.Enter, '\r'));

        var made = app.Configuration.TriggerSets[^1];
        await Assert.That(made.Name).IsEqualTo(TriggerSetEditing.NewSetName);
        await Assert.That(TriggerSetEditing.IsUsed(app.Configuration.Worlds, made.Name)).IsFalse();
    }

    [Test]
    public async Task SpaceOnUsedByTicksACharacterAndUndoOnTheListScreenTakesItBack()
    {
        var (app, saves) = App();
        app.SimulateKey(Key(ConsoleKey.F2));
        app.Settings.SimulateKey(Key(ConsoleKey.S, 's'));
        var set = app.Configuration.TriggerSets[app.SettingsFocus!.Value.Index];
        var users = TriggerSetEditing.Users(app.Configuration.Worlds, set.Name);
        var was = users[0].Uses;

        app.Settings.SimulateKey(Key(ConsoleKey.Tab, '\t'));
        app.Settings.SimulateKey(Key(ConsoleKey.Spacebar, ' '));

        await Assert.That(TriggerSetEditing.Uses(users[0].Character, set.Name)).IsEqualTo(!was);
        await Assert.That(saves.Count).IsEqualTo(1);

        // One log: back on F2, ⌃Z undoes the tick made over it.
        app.Settings.SimulateKey(Key(ConsoleKey.Escape));
        app.Settings.SimulateKey(Key(ConsoleKey.Z, '\u001a', ctrl: true));
        var after = TriggerSetEditing.Users(app.Configuration.Worlds, set.Name)[0];
        await Assert.That(after.Uses).IsEqualTo(was);
    }

    [Test]
    public async Task DeletingASetTakesItsUsesAndTheListScreensReviewAsksAboutIt()
    {
        var (app, _) = App();
        app.SimulateKey(Key(ConsoleKey.F2));
        app.Settings.SimulateKey(Key(ConsoleKey.S, 's'));
        var set = app.Configuration.TriggerSets[app.SettingsFocus!.Value.Index];
        var name = set.Name;

        app.Settings.SimulateKey(Key(ConsoleKey.Delete));

        await Assert.That(app.Configuration.TriggerSets).DoesNotContain(set);
        await Assert.That(TriggerSetEditing.IsUsed(app.Configuration.Worlds, name)).IsFalse();

        app.Settings.SimulateKey(Key(ConsoleKey.Escape));
        app.Settings.SimulateKey(Key(ConsoleKey.Escape));
        await Assert.That(app.Settings.Review.IsOpen).IsTrue();
    }

    [Test]
    public async Task RenamingASetRewritesEveryCharactersReference()
    {
        var (app, _) = App();
        app.SimulateKey(Key(ConsoleKey.F2));
        app.Settings.SimulateKey(Key(ConsoleKey.S, 's'));
        var set = app.Configuration.TriggerSets[app.SettingsFocus!.Value.Index];
        var users = TriggerSetEditing.Users(app.Configuration.Worlds, set.Name).Where(u => u.Uses).Select(u => u.Character).ToList();

        app.Settings.SimulateKey(Key(ConsoleKey.Enter, '\r'));
        for (var i = 0; i < 40; i++)
        {
            app.Settings.SimulateKey(Key(ConsoleKey.Backspace, '\b'));
        }

        foreach (var c in "Channels")
        {
            app.Settings.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.Settings.SimulateKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(set.Name).IsEqualTo("Channels");
        await Assert.That(users.Count).IsGreaterThan(0);
        foreach (var user in users)
        {
            await Assert.That(user.TriggerSets).Contains("Channels");
        }
    }
}
