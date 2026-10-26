using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Telnet;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A MOO's edit, end to end: the buffer opens in the composer, ⌃S saves it back as lines, an edit that
/// lands over something else waits for F1, and a failed save keeps it.
/// </summary>
[NotInParallel]
public class MooEditTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly MooEdit Look = new("#98.look", "Test:look", "moo-code", ["player:tell(\"Hi.\");", "return 1;"]);

    private static ConsoleKeyInfo Key(ConsoleKey key, ConsoleModifiers modifiers = 0) =>
        new('\0', key, modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control));

    private static async Task<(SharpMUTermApp App, RecordingTelnetSession Telnet, string Key)> Connected()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Moo", Host = "moo.example.org", Port = 7777 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        var session = app.BindWorldWithoutConnecting(world);
        await session.ConnectAsync();
        app.RenderNextFrame();
        return (app, telnet, session.SessionKey);
    }

    [Test]
    public async Task AnEditOpensInTheComposerAndCtrlSSavesItsLines()
    {
        var (app, telnet, key) = await Connected();
        telnet.ReceiveMooEdit(Look);

        await Assert.That(app.Composer.IsOpen).IsTrue();
        await Assert.That(app.Composer.Edit).IsEqualTo(Look);
        await Assert.That(app.Composer.Body).IsEqualTo("player:tell(\"Hi.\");\nreturn 1;");
        await Assert.That(app.Composer.HeaderMarkup).Contains("EDIT");
        await Assert.That(app.Composer.HeaderMarkup).Contains("Test:look");

        app.Composer.SimulateTyping("\n// 100% [sure]; done\n");
        await Assert.That(app.Composer.SimulateKey(Key(ConsoleKey.S, ConsoleModifiers.Control))).IsTrue();

        await Assert.That(app.Composer.IsOpen).IsFalse();
        var saved = telnet.SavedEdits.Single();
        await Assert.That(saved.Edit).IsEqualTo(Look);

        // As typed: no %r join and no escaping, and only the caret's last newline dropped.
        await Assert.That(saved.Lines).IsEquivalentTo(new[] { "player:tell(\"Hi.\");", "return 1;", "// 100% [sure]; done" });
        await Assert.That(telnet.Lines).IsEmpty();
        await Assert.That(app.HeldMooEdit(key)).IsNull();
    }

    [Test]
    public async Task AltLDoesNothingToAnEdit()
    {
        var (app, telnet, _) = await Connected();
        telnet.ReceiveMooEdit(Look);
        var before = app.Composer.Escaping;

        app.Composer.SimulateKey(Key(ConsoleKey.L, ConsoleModifiers.Alt));

        await Assert.That(app.Composer.Escaping).IsEqualTo(before);
        await Assert.That(app.Composer.HeaderMarkup).DoesNotContain("literal");
    }

    [Test]
    public async Task AnEditOverAnotherSurfaceWaitsForF1AndLeavesThePostDraftAlone()
    {
        var (app, telnet, key) = await Connected();
        app.SimulateKey(Key(ConsoleKey.F1));
        app.Composer.SimulateTyping("+bbpost 1=half written");
        app.Composer.SimulateKey(Key(ConsoleKey.Escape));
        await Assert.That(app.Composer.IsOpen).IsFalse();

        app.SimulateKey(Key(ConsoleKey.F7));
        telnet.ReceiveMooEdit(Look);
        await Assert.That(app.Composer.IsOpen).IsFalse();
        await Assert.That(app.HeldMooEdit(key)).IsEqualTo(Look);
        await Assert.That(app.StatusMarkup).Contains("F1 in that window opens it");

        app.SimulateKey(Key(ConsoleKey.F7));
        app.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(app.Composer.Edit).IsEqualTo(Look);

        // With the edit saved, F1 is the post again.
        app.Composer.SimulateKey(Key(ConsoleKey.S, ConsoleModifiers.Control));
        app.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(app.Composer.Edit).IsNull();
        await Assert.That(app.Composer.Body).IsEqualTo("+bbpost 1=half written");
    }

    [Test]
    public async Task AFailedSaveKeepsTheEdit()
    {
        var (app, telnet, key) = await Connected();
        telnet.RefusesEdits = true;
        telnet.ReceiveMooEdit(Look);
        app.Composer.SimulateTyping("\nchanged");

        app.Composer.SimulateKey(Key(ConsoleKey.S, ConsoleModifiers.Control));

        await Assert.That(app.Composer.IsOpen).IsFalse();
        await Assert.That(app.HeldMooEdit(key)).IsEqualTo(Look);
        app.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(app.Composer.Body).EndsWith("changed");
    }

    [Test]
    public async Task TheMoosStatusIsOnTheStatusRowInAReservedCell()
    {
        var (app, telnet, _) = await Connected();
        telnet.ReceiveMcpStatus("You are hungry.");
        var hungry = app.StatusMarkup;
        telnet.ReceiveMcpStatus("Sated.");

        await Assert.That(hungry).Contains("You are hungry.");
        await Assert.That(app.StatusMarkup).Contains("Sated.");
        await Assert.That(MarkupText.VisibleLength(app.StatusMarkup)).IsEqualTo(MarkupText.VisibleLength(hungry));
    }
}
