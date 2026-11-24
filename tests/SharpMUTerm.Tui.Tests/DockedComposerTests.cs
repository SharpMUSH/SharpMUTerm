using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui.Panels;

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

    /// <summary>
    /// Roleplay's side composer, reached from the output pane's Chat tab: Esc hands the keyboard back to Chat — the tab
    /// that was in front — not to the composer's own pane, whose command line is the character's main window's.
    /// </summary>
    [Test]
    public async Task EscGoesBackToTheTabTheComposerWasReachedFrom()
    {
        var app = Roleplay();
        var chat = ReachTheComposerFromChat(app);

        app.SimulateKey(Key(ConsoleKey.Escape));
        app.RenderNextFrame();

        await Assert.That(app.ComposerArmed).IsFalse();
        await Assert.That(app.ActiveWindowId()).IsEqualTo(chat);
        await Assert.That(app.FocusedPaneId).IsEqualTo(app.PaneIdOf(chat));
        await Assert.That(app.ArmedBarHasFocus).IsTrue();
    }

    /// <summary>
    /// A narrow client docks the composer as a tab of the output's pane. Esc there leaves it in front: going back to the
    /// window before it would put that window over the composer and hide what was being written.
    /// </summary>
    [Test]
    public async Task EscLeavesAComposerThatSharesItsPaneInFront()
    {
        var world = await Connected(80, 24);
        var windowId = world.OpenComposer();
        var pane = world.App.PaneIdOf(windowId);
        await Assert.That(world.App.PaneIds.Count).IsEqualTo(1).Because("the fixture's composer shares the output's pane");

        world.App.SimulateKey(Key(ConsoleKey.Escape));

        await Assert.That(world.App.ComposerArmed).IsFalse();
        await Assert.That(world.App.PaneActiveTab(pane!)).IsEqualTo(windowId);
    }

    /// <summary>⌥⏎ is Esc then Enter: the Esc took the keyboard back to Chat, and the Enter puts it in the composer again.</summary>
    [Test]
    public async Task AltEnterStillInsertsALineAfterEscLeftForAnotherTab()
    {
        var app = Roleplay();
        ReachTheComposerFromChat(app);
        var composer = app.ActiveWindowId();
        app.SimulateComposerTyping("one");

        app.SimulateKey(Key(ConsoleKey.Escape));
        app.SimulateKey(Enter);
        app.SimulateComposerTyping("two");

        await Assert.That(app.ComposerArmed).IsTrue();
        await Assert.That(app.ActiveWindowId()).IsEqualTo(composer);
        await Assert.That(app.DockedComposerIn(composer)!.Body).EndsWith("one\ntwo");
    }

    /// <summary>
    /// A pane holding the composer alone draws no tab strip, so its heading carries a <c>×</c>: a click closes the pane,
    /// records it closed, and keeps the draft for the next composer.
    /// </summary>
    [Test]
    public async Task TheHeadingsCloseLinkClosesTheComposerAndKeepsTheDraft()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();
        world.App.SimulateComposerTyping("half a post");
        var composer = world.App.DockedComposerIn(windowId)!;
        await Assert.That(composer.HeaderMarkup).Contains($"[link={DockedComposer.CloseLink}]×[/]");
        await Assert.That(world.App.RenderWholeFrame()).Contains("×");

        composer.SimulateHeaderLink(DockedComposer.CloseLink);
        world.App.RenderNextFrame();

        await Assert.That(world.App.PaneIdOf(windowId)).IsNull();
        await Assert.That(world.App.ComposerArmed).IsFalse();
        await Assert.That(world.App.ArmedBarHasFocus).IsTrue();

        world.OpenComposer();
        await Assert.That(world.App.DockedComposerIn(windowId)!.Body).IsEqualTo("half a post");
    }

    /// <summary>⌃W from inside the composer closes it too, and the keyboard lands on the command line.</summary>
    [Test]
    public async Task CtrlWInTheComposerClosesIt()
    {
        var world = await Connected();
        var windowId = world.OpenComposer();
        await Assert.That(world.App.ComposerArmed).IsTrue();

        world.App.SimulateKey(Key(ConsoleKey.W, ConsoleModifiers.Control));
        world.App.RenderNextFrame();

        await Assert.That(world.App.PaneIdOf(windowId)).IsNull();
        await Assert.That(world.App.ArmedBarHasFocus).IsTrue();
    }

    /// <summary>Every width the heading is drawn at, the × is its last cell but one and the row is never wider.</summary>
    [Test]
    public async Task TheCloseLinkKeepsItsCellAtEveryWidth()
    {
        var ink = new HeadingBand.Ink("#222222", "#dddddd", "#00f5b7");
        foreach (var width in new[] { 5, 8, 20, 40, 80 })
        {
            var row = HeadingBand.Row("COMPOSE", "  as typed", "12 chars", width, ink, close: "×");
            var text = MarkupText.Plain(row);
            await Assert.That(text.Length).IsEqualTo(width).Because($"{width} cells");
            await Assert.That(text[^2]).IsEqualTo('×').Because($"{width} cells");
        }
    }

    /// <summary>The demo under the Roleplay layout: the output pane holds Corvid and Chat, the composer is beside it.</summary>
    private static SharpMUTermApp Roleplay()
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(Width, Height));
        app.RenderSnapshot("layout-roleplay");
        app.RenderNextFrame();
        return app;
    }

    /// <summary>Brings Chat to the front of the output pane, then ⌃→ into the composer. Returns Chat's window id.</summary>
    private static string ReachTheComposerFromChat(SharpMUTermApp app)
    {
        app.SimulateKey(Key(ConsoleKey.N, ConsoleModifiers.Control));
        var chat = app.ActiveWindowId();
        app.SimulateKey(Key(ConsoleKey.RightArrow, ConsoleModifiers.Control));
        app.RenderNextFrame();
        if (!app.ComposerArmed || chat.StartsWith("panel:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"the fixture did not reach the composer from a tab ({chat})");
        }

        return chat;
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

    private static async Task<Wired> Connected(int width = Width, int height = Height)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(width, height));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        await app.BindWorldWithoutConnecting(world).ConnectAsync();
        app.RenderNextFrame();
        return new Wired(app, telnet);
    }
}
