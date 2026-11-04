using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Telnet;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The composer in a host with its own editor (<see cref="ClientHost.NativeComposer"/>): F1 opens there instead of the
/// cell modal, on the same draft and the same model; what the host hands back through the session ends in the methods
/// the cell composer's keys end in, so the command a world receives is the cell path's for the same text. The docked
/// composer keeps its cells, and the host's editor rides the panel channel with a session of its own.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class NativeComposerRoutingTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly MooEdit Look = new("#98.look", "Test:look", "moo-code", ["player:tell(\"Hi.\");", "return 1;"]);

    private static ConsoleKeyInfo Key(ConsoleKey key, ConsoleModifiers modifiers = 0) =>
        new('\0', key, modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control));

    /// <summary>A host's editor that records what it was asked to show.</summary>
    private sealed class FakeComposer(bool available = true) : INativeComposer
    {
        public bool Available { get; set; } = available;

        public List<ComposerSession> Opened { get; } = [];

        public int Closed { get; private set; }

        public ComposerSession? Last => Opened.Count > 0 ? Opened[^1] : null;

        public void Open(ComposerSession session, PanelInk ink) => Opened.Add(session);

        public void Close() => Closed++;
    }

    /// <summary>Records the panels the client publishes, which is how a docked composer's editor reaches a host.</summary>
    private sealed class PanelPresenter : IPointerPresenter
    {
        public IReadOnlyList<PanelOverlay> Last { get; private set; } = [];

        public void Hover(PointerTarget target)
        {
        }

        public void ShowMenu(Point cell, IReadOnlyList<PointerMenuItem> items)
        {
        }

        public bool DrawsMenus => true;

        public void Scrollbars(IReadOnlyList<PaneScrollbar> bars)
        {
        }

        public void Panels(IReadOnlyList<PanelOverlay> panels) => Last = panels;
    }

    private sealed record Wired(SharpMUTermApp App, RecordingTelnetSession Telnet, FakeComposer Native, PanelPresenter Pointer, string Key);

    private static async Task<Wired> Connected(bool native = true)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);

        var fake = new FakeComposer(native);
        var pointer = new PanelPresenter();
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null) with
        {
            NativeComposer = fake,
            Pointer = pointer,
        };
        var app = new SharpMUTermApp(config, host);
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        var session = app.BindWorldWithoutConnecting(world);
        await session.ConnectAsync();
        app.RenderNextFrame();
        return new Wired(app, telnet, fake, pointer, session.SessionKey);
    }

    /// <summary>The cell path's command for a draft: typed into the cell F1 and sent with ⌃S, escaping as given.</summary>
    private static async Task<string> SentThroughTheCells(string draft, bool literal)
    {
        var world = await Connected(native: false);
        await using var _ = world.App;
        world.App.SimulateKey(Key(ConsoleKey.F1));
        world.App.Composer.SimulateTyping(draft);
        if (literal)
        {
            world.App.Composer.SimulateKey(Key(ConsoleKey.L, ConsoleModifiers.Alt));
        }

        world.App.Composer.SimulateKey(Key(ConsoleKey.S, ConsoleModifiers.Control));
        return world.Telnet.Lines.Single();
    }

    // ── F1 ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The window's editor writes in monospace or a proportional face, chosen from its menu. The choice is the
    /// character's and is kept on disk through the write-only save (<c>PersistConfiguration</c>) — not the settings
    /// funnel, which would re-periodise the timers — so F1 opened again, or next run, writes in the same face.
    /// </summary>
    [Test]
    public async Task TheComposersFaceIsTheCharactersAndIsSaved()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        var ann = new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() };
        world.Characters.Add(ann);
        config.Worlds.Add(world);
        var fake = new FakeComposer();
        var saved = new List<bool>();
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null) with { NativeComposer = fake };
        await using var app = new SharpMUTermApp(config, host, save: c => saved.Add(c.Worlds[0].Characters[0].ComposerProportional));
        app.TelnetFactory = _ => new RecordingTelnetSession();
        var session = app.BindWorldWithoutConnecting(world);
        await session.ConnectAsync();
        app.RenderNextFrame();

        app.SimulateKey(Key(ConsoleKey.F1));
        var first = fake.Last!;
        await Assert.That(first.Proportional).IsFalse().Because("monospace by default");

        first.ChooseFace(true);
        await Assert.That(ann.ComposerProportional).IsTrue();
        await Assert.That(saved).IsEquivalentTo(new[] { true }).Because("saved once, with the choice in it");
        await Assert.That(first.Proportional).IsTrue();

        first.Close(string.Empty);
        app.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(fake.Last).IsNotSameReferenceAs(first);
        await Assert.That(fake.Last!.Proportional).IsTrue().Because("F1 opened again writes in the character's face");

        fake.Last.ChooseFace(true);
        await Assert.That(saved.Count).IsEqualTo(1).Because("choosing the face it already has writes nothing");
    }

    [Test]
    public async Task F1OpensTheHostsEditorAndNotTheCellModal()
    {
        var world = await Connected();
        await using var _ = world.App;

        world.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(world.Native.Opened.Count).IsEqualTo(1);
        await Assert.That(world.App.Composer.IsOpen).IsFalse().Because("the cell modal is not opened under the native editor");
        await Assert.That(world.App.NativeComposerIsOpen).IsTrue();

        var model = world.Native.Last!.Model;
        await Assert.That(model.Place).IsEqualTo(ComposerPlace.FullScreen);
        await Assert.That(model.Target).IsEqualTo("Ann");
        await Assert.That(model.CanSend).IsTrue();
        await Assert.That(model.Keys.Select(k => k.Key)).IsEquivalentTo(new[] { "⌃S", "⌥L", "⌥W", "⌥P", "Esc" });
    }

    [Test]
    public async Task AHostWithNowhereToDrawItGetsTheCellComposer()
    {
        var world = await Connected(native: false);
        await using var _ = world.App;

        world.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(world.Native.Opened).IsEmpty();
        await Assert.That(world.App.Composer.IsOpen).IsTrue();
    }

    [Test]
    public async Task TheNativeModelIsTheModelTheCellWindowDrawsForTheSameDraft()
    {
        var world = await Connected();
        await using var _ = world.App;
        world.App.SimulateKey(Key(ConsoleKey.F1));
        var session = world.Native.Last!;

        session.Edited("+pose waves\r\nand sits");
        session.ToggleEscaping();

        var cell = ComposeOverlay.ModelFor("Ann", "+pose waves\nand sits", ComposeEscaping.Literal, null, null, canSend: true);
        await Assert.That(session.Model).IsEqualTo(cell);
        await Assert.That(ComposeOverlay.Render(session.Model)).IsEqualTo(ComposeOverlay.Render(cell));
    }

    [Test]
    [Arguments("+pose waves\nand sits", false)]
    [Arguments("100% [sure]; {x}", true)]
    [Arguments("100% [sure]; {x}", false)]
    [Arguments("+bbpost 1=Title\n\nFirst paragraph.\n\nSecond, 50% [done].\n", true)]
    [Arguments("+bbpost 1=Title\n\nFirst paragraph.\n\nSecond, 50% [done].\n", false)]
    public async Task CtrlSInTheHostsEditorSendsWhatTheCellComposerSends(string draft, bool literal)
    {
        var expected = await SentThroughTheCells(draft, literal);

        var world = await Connected();
        await using var _ = world.App;
        world.App.SimulateKey(Key(ConsoleKey.F1));
        var session = world.Native.Last!;
        if (literal)
        {
            session.ToggleEscaping();
        }

        session.Send(draft);

        await Assert.That(world.Telnet.Lines).IsEquivalentTo(new[] { expected });
        await Assert.That(expected).IsEqualTo(ComposeMessage.Build(draft, literal ? ComposeEscaping.Literal : ComposeEscaping.AsTyped));
        await Assert.That(world.App.NativeComposerIsOpen).IsFalse().Because("a send closes it");
        await Assert.That(world.Native.Closed).IsEqualTo(1);

        // Close first, forget after: the next F1 opens empty.
        world.App.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(world.Native.Last!.Model.Body).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task EscKeepsTheDraftForTheNextF1()
    {
        var world = await Connected();
        await using var _ = world.App;
        world.App.SimulateKey(Key(ConsoleKey.F1));
        var first = world.Native.Last!;
        first.ToggleEscaping();

        first.Close("half a post");

        await Assert.That(world.App.NativeComposerIsOpen).IsFalse();
        await Assert.That(world.Telnet.Lines).IsEmpty();
        await Assert.That(first.IsEnded).IsTrue();

        world.App.SimulateKey(Key(ConsoleKey.F1));
        var second = world.Native.Last!;
        await Assert.That(second).IsNotSameReferenceAs(first);
        await Assert.That(second.Model.Body).IsEqualTo("half a post");
        await Assert.That(second.Model.Escaping).IsEqualTo(ComposeEscaping.Literal).Because("the mode travels with the draft");
    }

    [Test]
    public async Task AGestureFromAnEndedSessionDoesNothing()
    {
        var world = await Connected();
        await using var _ = world.App;
        world.App.SimulateKey(Key(ConsoleKey.F1));
        var session = world.Native.Last!;
        session.Close("kept");

        session.Send("sent late");

        await Assert.That(world.Telnet.Lines).IsEmpty();
    }

    [Test]
    public async Task F1StillRefusesOverASettingsScreen()
    {
        var world = await Connected();
        await using var _ = world.App;
        world.App.SimulateKey(Key(ConsoleKey.F7));

        world.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(world.Native.Opened).IsEmpty();
        await Assert.That(world.App.StatusMarkup).Contains("close the settings screen first");
    }

    [Test]
    public async Task NothingOpensOverTheHostsEditor()
    {
        var world = await Connected();
        await using var _ = world.App;
        world.App.SimulateKey(Key(ConsoleKey.F1));

        world.App.SimulateKey(Key(ConsoleKey.P, ConsoleModifiers.Control));
        world.App.SimulateKey(Key(ConsoleKey.F7));

        await Assert.That(world.App.MenuIsOpen).IsFalse();
        await Assert.That(world.App.OpenSettingsKey).IsNull();
        await Assert.That(world.App.NativeComposerIsOpen).IsTrue();
    }

    // ── MOO edits ────────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AMooEditOpensInTheHostsEditorAndSavesItsLines()
    {
        var world = await Connected();
        await using var _ = world.App;

        world.Telnet.ReceiveMooEdit(Look);

        var session = world.Native.Last;
        await Assert.That(session).IsNotNull();
        await Assert.That(session!.Model.Edit).IsEqualTo(new ComposerEdit("Test:look", "moo-code"));
        await Assert.That(session.Model.Dictionary).IsNull().Because("a MOO edit is not checked");
        await Assert.That(session.Model.Keys.Select(k => k.Key)).IsEquivalentTo(new[] { "⌃S", "Esc" });
        await Assert.That(session.Model.Body).IsEqualTo("player:tell(\"Hi.\");\nreturn 1;");

        session.Send(session.Model.Body + "\n// 100% [sure]; done\n");

        var saved = world.Telnet.SavedEdits.Single();
        await Assert.That(saved.Lines).IsEquivalentTo(new[] { "player:tell(\"Hi.\");", "return 1;", "// 100% [sure]; done" });
        await Assert.That(world.Telnet.Lines).IsEmpty();
        await Assert.That(world.App.HeldMooEdit(world.Key)).IsNull();
    }

    [Test]
    public async Task AMooEditIsHeldWhileSomethingElseIsOpen()
    {
        var world = await Connected();
        await using var _ = world.App;
        world.App.SimulateKey(Key(ConsoleKey.F7));

        world.Telnet.ReceiveMooEdit(Look);

        await Assert.That(world.Native.Opened).IsEmpty();
        await Assert.That(world.App.HeldMooEdit(world.Key)).IsEqualTo(Look);

        world.App.SimulateKey(Key(ConsoleKey.F7));
        world.App.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(world.Native.Last!.Model.Edit).IsNotNull();
    }

    [Test]
    public async Task AMooEditIsHeldWhileTheHostsEditorHasAPostOpen()
    {
        var world = await Connected();
        await using var _ = world.App;
        world.App.SimulateKey(Key(ConsoleKey.F1));
        world.Native.Last!.Edited("+bbpost 1=half written");

        world.Telnet.ReceiveMooEdit(Look);

        await Assert.That(world.Native.Opened.Count).IsEqualTo(1).Because("a post being written is not replaced by wire input");
        await Assert.That(world.App.HeldMooEdit(world.Key)).IsEqualTo(Look);
    }

    // ── the docked composer ──────────────────────────────────────────────────────────────────

    private static string OpenDocked(Wired world)
    {
        world.App.DispatchCommand("panel:composer");
        world.App.RenderNextFrame();
        world.App.RenderNextFrame();
        return TerminalPanels.WindowId(PanelIds.Composer, world.Key);
    }

    private static PanelOverlay? DockedPanel(Wired world, string windowId) =>
        world.Pointer.Last.FirstOrDefault(p => p.PanelId == PanelIds.Composer && p.WindowId == windowId);

    [Test]
    public async Task TheDockedComposerReachesTheHostWithTheModelItsCellsDrew()
    {
        var world = await Connected();
        await using var _ = world.App;
        var windowId = OpenDocked(world);

        var panel = DockedPanel(world, windowId);
        await Assert.That(panel).IsNotNull();
        await Assert.That((object?)panel!.Composer).IsEqualTo(world.App.DrawnModel(PanelIds.Composer, windowId));
        await Assert.That(panel.ComposerSession).IsSameReferenceAs(world.App.DockedComposerSession(windowId));
        await Assert.That(panel.Composer!.Armed).IsTrue();
        await Assert.That(panel.Composer.Place).IsEqualTo(ComposerPlace.Docked);
    }

    [Test]
    public async Task TheHostsTextIsTheDockedDraftAndCtrlSSendsIt()
    {
        var world = await Connected();
        await using var _ = world.App;
        var windowId = OpenDocked(world);
        var session = world.App.DockedComposerSession(windowId)!;
        var before = session.Model.Revision;

        session.Edited("+pose waves\nand sits");

        var docked = world.App.DockedComposerIn(windowId)!;
        await Assert.That(docked.Body).IsEqualTo("+pose waves\nand sits");
        await Assert.That(session.Model.Body).IsEqualTo("+pose waves\nand sits");
        await Assert.That(session.Model.Revision).IsEqualTo(before).Because("the host's own typing does not move the revision");

        session.Send("+pose waves\nand sits");

        await Assert.That(world.Telnet.Lines).IsEquivalentTo(new[] { "+pose waves%rand sits" });
        await Assert.That(docked.Body).IsEqualTo(string.Empty);
        await Assert.That(session.Model.Revision).IsGreaterThan(before).Because("the client cleared it, so the host takes the client's text");
    }

    [Test]
    public async Task EscFromTheHostsEditorReleasesAndAClickArmsAgain()
    {
        var world = await Connected();
        await using var _ = world.App;
        var windowId = OpenDocked(world);
        var session = world.App.DockedComposerSession(windowId)!;

        session.Release();
        await Assert.That(world.App.ComposerArmed).IsFalse();
        await Assert.That(session.Model.Armed).IsFalse();
        await Assert.That(world.App.ArmedBarHasFocus).IsTrue();

        session.Arm();
        await Assert.That(world.App.ComposerArmed).IsTrue();
        await Assert.That(session.Model.Armed).IsTrue();
    }

    [Test]
    public async Task TheDockedEditorIsNotPublishedWhileF1IsOpenNatively()
    {
        var world = await Connected();
        await using var _ = world.App;
        var windowId = OpenDocked(world);
        world.App.DockedComposerSession(windowId)!.Edited("half a post");

        world.App.SimulateKey(Key(ConsoleKey.F1));
        world.App.RenderNextFrame();

        await Assert.That(DockedPanel(world, windowId)).IsNull().Because("F1 covers the panes, as the cell modal does");
        await Assert.That(world.Native.Last!.Model.Body).IsEqualTo("half a post").Because("the docked buffer is the draft F1 opens");

        world.Native.Last!.Close("half a post and the rest");
        world.App.RenderNextFrame();
        await Assert.That(world.App.DockedComposerIn(windowId)!.Body).IsEqualTo("half a post and the rest");
        await Assert.That(DockedPanel(world, windowId)).IsNotNull();
    }

    [Test]
    public async Task WithoutAHostEditorTheDockedPanelCarriesNoSession()
    {
        var world = await Connected(native: false);
        await using var _ = world.App;
        var windowId = OpenDocked(world);

        await Assert.That(DockedPanel(world, windowId)).IsNull();
    }
}
