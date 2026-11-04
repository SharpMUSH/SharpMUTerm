using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The docked composer, driven through the window's own key handler: arriving at its pane arms it, typing and paste
/// go to it and not the command line, ⌃S sends one command to <em>its</em> character, Esc hands the keyboard back,
/// and its buffer is the same draft F1 opens.
/// <para>
/// Sessions are connected, as <see cref="ComposeWindowTests"/>' are: <c>SendUserInputAsync</c> writes nothing without
/// a transport, so "it was sent" against an unconnected session would hold whatever the code did.
/// </para>
/// </summary>
[NotInParallel]
public class DockedComposerTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo Key(ConsoleKey key, ConsoleModifiers modifiers = 0, char ch = '\0') =>
        new(ch, key, modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control));

    private static ConsoleKeyInfo Enter => Key(ConsoleKey.Enter, ch: '\r');

    [Test]
    public async Task OpeningTheComposerArmsItAndTypingLandsThere()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();

        await Assert.That(world.App.ComposerArmed).IsTrue();
        await Assert.That(world.App.ComposerHasFocus).IsTrue().Because("paste follows the framework's focus");

        world.App.SimulateComposerTyping("say hi");
        await Assert.That(world.App.DockedComposerIn(windowId)!.Body).IsEqualTo("say hi");
        await Assert.That(world.App.PrimaryInputText).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task EnterInsertsALineAndCtrlSSendsOneCommandToTheComposersCharacter()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();

        world.App.SimulateComposerTyping("+pose waves\nand sits");
        await Assert.That(world.Telnet.Lines).IsEmpty().Because("⏎ in the composer is a new line, not a send");

        world.App.SimulateKey(Key(ConsoleKey.S, ConsoleModifiers.Control));

        await Assert.That(world.Telnet.Lines).IsEquivalentTo(new[] { "+pose waves%rand sits" });
        await Assert.That(world.App.DockedComposerIn(windowId)!.Body).IsEqualTo(string.Empty);
        await Assert.That(world.App.HistoryEntries(InputBar.Primary)).Contains("+pose waves%rand sits");
    }

    [Test]
    public async Task AltLEscapesTheWayF1Does()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();
        world.App.SimulateComposerTyping("100% [sure]");

        world.App.SimulateKey(Key(ConsoleKey.L, ConsoleModifiers.Alt));
        await Assert.That(world.App.DockedComposerIn(windowId)!.HeaderMarkup).Contains("literal");
        world.App.SimulateKey(Key(ConsoleKey.S, ConsoleModifiers.Control));

        await Assert.That(world.Telnet.Lines).IsEquivalentTo(new[] { "100%% \\[sure\\]" });
    }

    [Test]
    public async Task PasteGoesToTheComposerWhileItIsArmedAndToTheCommandLineAfterEsc()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();

        world.App.SimulatePaste("pasted");
        await Assert.That(world.App.DockedComposerIn(windowId)!.Body).IsEqualTo("pasted");

        world.App.SimulateKey(Key(ConsoleKey.Escape));
        await Assert.That(world.App.ComposerArmed).IsFalse();
        await Assert.That(world.App.ArmedBarHasFocus).IsTrue();

        world.App.SimulatePaste("look");
        await Assert.That(world.App.PrimaryInputText).IsEqualTo("look");
        await Assert.That(world.App.DockedComposerIn(windowId)!.Body).IsEqualTo("pasted");
    }

    [Test]
    public async Task AfterEscEnterSendsFromTheCommandLineToTheSameCharacter()
    {
        var world = await Connected();
        world.OpenComposer();
        world.App.SimulateComposerTyping("draft");
        world.App.SimulateKey(Key(ConsoleKey.Escape));

        foreach (var c in "look")
        {
            world.App.SimulateKey(Key((ConsoleKey)char.ToUpperInvariant(c), ch: c));
        }

        world.App.SimulateKey(Enter);
        await Assert.That(world.Telnet.Lines).IsEquivalentTo(new[] { "look" });
    }

    /// <summary>
    /// ⌥⏎ is the newline chord, and a terminal writes it as Esc then Enter. In the composer the Esc alone would hand
    /// the keyboard back and the Enter would send whatever the command line held — so the pair is put back together.
    /// </summary>
    [Test]
    public async Task AltEnterInTheComposerIsANewLineAndSendsNothing()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();
        world.App.SimulateComposerTyping("one");

        world.App.SimulateKey(Key(ConsoleKey.Escape));
        world.App.SimulateKey(Enter);
        world.App.SimulateComposerTyping("two");

        await Assert.That(world.Telnet.Lines).IsEmpty();
        await Assert.That(world.App.ComposerArmed).IsTrue();
        await Assert.That(world.App.DockedComposerIn(windowId)!.Body).IsEqualTo("one\ntwo");
    }

    [Test]
    public async Task MovingToAnotherPaneDisarmsItAndComingBackArmsIt()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();
        world.App.DispatchCommand("layout:split-right");
        world.App.RenderNextFrame();
        var composerPane = world.App.PaneIdOf(windowId);
        await Assert.That(world.App.PaneIds.Count).IsEqualTo(2);

        while (world.App.FocusedPaneId == composerPane)
        {
            world.App.SimulateKey(Key(ConsoleKey.O, ConsoleModifiers.Control));
        }

        await Assert.That(world.App.ComposerArmed).IsFalse();
        await Assert.That(world.App.ArmedBarHasFocus).IsTrue();

        world.App.SimulateKey(Key(ConsoleKey.O, ConsoleModifiers.Control));
        await Assert.That(world.App.FocusedPaneId).IsEqualTo(composerPane);
        await Assert.That(world.App.ComposerArmed).IsTrue().Because("arriving at its pane is moving into it");
        await Assert.That(world.App.ComposerHasFocus).IsTrue();
    }

    /// <summary>A click in the editor arms it — the focus pin must not take the focus straight back to the bar.</summary>
    [Test]
    public async Task AClickInTheEditorArmsItFromAnotherPane()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();
        world.App.DispatchCommand("layout:split-right");
        world.App.RenderNextFrame();
        var composerPane = world.App.PaneIdOf(windowId);
        while (world.App.FocusedPaneId == composerPane)
        {
            world.App.SimulateKey(Key(ConsoleKey.O, ConsoleModifiers.Control));
        }

        await Assert.That(world.App.ComposerArmed).IsFalse();

        world.App.SimulateComposerClick(windowId);

        await Assert.That(world.App.ComposerArmed).IsTrue();
        await Assert.That(world.App.ComposerHasFocus).IsTrue();
        await Assert.That(world.App.FocusedPaneId).IsEqualTo(composerPane);
        world.App.SimulateComposerTyping("x");
        await Assert.That(world.App.DockedComposerIn(windowId)!.Body).IsEqualTo("x");
    }

    /// <summary>While the composer holds the keyboard both command lines are drawn idle: ⏎ is not theirs.</summary>
    [Test]
    public async Task BothCommandLinesAreIdleWhileTheComposerIsArmed()
    {
        var world = await Connected();
        world.OpenComposer();
        var (armed, idle) = world.App.InputBandColors;
        await Assert.That(world.App.PrimaryBandColor).IsEqualTo(idle);

        world.App.SimulateKey(Key(ConsoleKey.Escape));
        await Assert.That(world.App.PrimaryBandColor).IsEqualTo(armed);
    }

    [Test]
    public async Task TheDockedBufferIsTheDraftF1Opens()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();
        world.App.SimulateComposerTyping("half a post");

        world.App.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(world.App.Composer.IsOpen).IsTrue();
        await Assert.That(world.App.Composer.Body).IsEqualTo("half a post");

        world.App.Composer.SimulateTyping(" and the rest");
        world.App.Composer.SimulateKey(Key(ConsoleKey.Escape));

        await Assert.That(world.App.DockedComposerIn(windowId)!.Body).IsEqualTo("half a post and the rest");
    }

    [Test]
    public async Task ItSurvivesThePaneAreaBeingRebuilt()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();
        world.App.SimulateComposerTyping("kept");

        world.App.DispatchCommand("layout:split-right");
        world.App.RenderNextFrame();

        await Assert.That(world.App.DockedComposerIn(windowId)!.Body).IsEqualTo("kept");
        await Assert.That(world.App.RenderWholeFrame()).Contains("kept");
    }

    private sealed record Wired(SharpMUTermApp App, RecordingTelnetSession Telnet)
    {
        /// <summary>Opens the composer through its ⌃P entry, in a pane of its own beside the output.</summary>
        internal string OpenComposer()
        {
            App.DispatchCommand("panel:composer");
            var windowId = TerminalPanels.WindowId(PanelIds.Composer, "Quiet.Ann");
            App.RenderNextFrame();
            return windowId;
        }
    }

    private static async Task<Wired> Connected()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        await app.BindWorldWithoutConnecting(world).ConnectAsync();
        app.RenderNextFrame();
        return new Wired(app, telnet);
    }
}
