using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The quit prompt, ⌃F, ⌃R and ⌥M in a host that draws them itself (<see cref="ClientHost.NativeModals"/>): each
/// funnel asks the host first, the cell modal is not opened under it, and a host whose modals are not attached gets
/// the cells. The quit prompt is never refused and always ends up in front: over a native modal or the native F1 it
/// opens beside them and N leaves them as they were; with no native modals the host's F1 is closed first, keeping its
/// draft, so the cell prompt is not drawn under it.
/// </summary>
/// <remarks>Serialised: rendering a frame redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class NativeModalRoutingTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo Key(ConsoleKey key, ConsoleModifiers modifiers = 0, char c = '\0') =>
        new(c, key, modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control));

    private static readonly ConsoleKeyInfo CtrlQ = new('\x11', ConsoleKey.Q, false, false, true);

    private sealed class FakeModals(bool available = true) : INativeModals
    {
        public bool Available { get; } = available;

        public List<ModalSession> Shown { get; } = [];

        public List<ModalSession> Closed { get; } = [];

        public void Show(ModalSession session) => Shown.Add(session);

        public void Close(ModalSession session) => Closed.Add(session);
    }

    private sealed class FakeComposer : INativeComposer
    {
        public bool Available => true;

        public List<ComposerSession> Opened { get; } = [];

        public int Closed { get; private set; }

        public void Open(ComposerSession session, PanelInk ink) => Opened.Add(session);

        public void Close() => Closed++;
    }

    private static async Task<(SharpMUTermApp App, FakeModals? Modals, FakeComposer? Composer)> Connected(
        FakeModals? modals, FakeComposer? composer = null)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null) with
        {
            NativeModals = modals,
            NativeComposer = composer,
        };
        var app = new SharpMUTermApp(config, host);
        app.TelnetFactory = _ => new RecordingTelnetSession();
        await app.BindWorldWithoutConnecting(world).ConnectAsync();
        app.RenderNextFrame();
        return (app, modals, composer);
    }

    [Test]
    public async Task EachModalOpensInTheHostsControlsAndNotInCells()
    {
        var (app, modals, _) = await Connected(new FakeModals());
        await using var _ = app;

        foreach (var (chord, surface) in new[]
        {
            (Key(ConsoleKey.F, ConsoleModifiers.Control, '\x06'), SurfaceCatalog.Search),
            (Key(ConsoleKey.R, ConsoleModifiers.Control, '\x12'), SurfaceCatalog.History),
            (Key(ConsoleKey.M, ConsoleModifiers.Alt, 'm'), SurfaceCatalog.Layouts),
        })
        {
            app.SimulateKey(chord);
            await Assert.That(modals!.Shown[^1].Surface).IsEqualTo(surface);
            await Assert.That(app.DrawnModel(surface)).IsNull().Because($"{surface}: no cell modal under the native one");

            // The chord again closes it, as it closes the cell modal.
            app.SimulateKey(chord);
            await Assert.That(modals.Closed).Contains(modals.Shown[^1]).Because($"{surface}: the chord closes it");
            await Assert.That(modals.Shown[^1].IsEnded).IsTrue();
        }

        await Assert.That(app.SearchIsOpen || app.HistorySearchOpen || app.LayoutsOpen).IsFalse();
    }

    [Test]
    public async Task AHostWhoseModalsAreNotAttachedGetsTheCells()
    {
        var (app, modals, _) = await Connected(new FakeModals(available: false));
        await using var _ = app;

        app.SimulateKey(Key(ConsoleKey.F, ConsoleModifiers.Control, '\x06'));
        await Assert.That(modals!.Shown).IsEmpty();
        await Assert.That(app.DrawnModel(SurfaceCatalog.Search)).IsNotNull().Because("the cell search is open");
    }

    [Test]
    public async Task EscInTheHostClosesTheModalAndRunsNothing()
    {
        var (app, modals, _) = await Connected(new FakeModals());
        await using var _ = app;

        app.SimulateKey(Key(ConsoleKey.F, ConsoleModifiers.Control, '\x06'));
        var search = (SearchSession)modals!.Shown.Single();
        search.Dismiss();

        await Assert.That(app.SearchIsOpen).IsFalse();
        search.Go(0);
        await Assert.That(app.SearchBarIndex(app.ActiveWindowId())).IsNull().Because("a late gesture on a closed session does nothing");
    }

    [Test]
    public async Task TheQuitPromptOpensOverANativeModalAndStayLeavesItOpen()
    {
        var (app, modals, _) = await Connected(new FakeModals());
        await using var _ = app;

        app.SimulateKey(Key(ConsoleKey.F, ConsoleModifiers.Control, '\x06'));
        var search = modals!.Shown.Single();

        app.SimulateKey(CtrlQ);
        var quit = modals.Shown.OfType<QuitSession>().Single();
        await Assert.That(quit.AlwaysInFront).IsTrue();
        await Assert.That(quit.Model.ConnectedCharacters).IsEquivalentTo(new[] { "Ann · Quiet" });
        await Assert.That(search.IsEnded).IsFalse().Because("the search stays under the prompt");

        quit.Answer(false);
        await Assert.That(app.QuitPromptOpen).IsFalse();
        await Assert.That(app.SearchIsOpen).IsTrue().Because("Stay goes back to the search");
        await Assert.That(app.ExitRequested).IsFalse();
    }

    [Test]
    public async Task CtrlQAgainClosesTheNativePrompt()
    {
        var (app, modals, _) = await Connected(new FakeModals());
        await using var _ = app;

        app.SimulateKey(CtrlQ);
        var quit = modals!.Shown.OfType<QuitSession>().Single();
        app.SimulateKey(CtrlQ);

        await Assert.That(modals.Closed).Contains(quit);
        await Assert.That(app.QuitPromptOpen).IsFalse();
        await Assert.That(app.ExitRequested).IsFalse();
    }

    [Test]
    public async Task YesInTheNativePromptQuits()
    {
        var (app, modals, _) = await Connected(new FakeModals());
        await using var _ = app;

        app.SimulateKey(CtrlQ);
        modals!.Shown.OfType<QuitSession>().Single().Answer(true);

        await Assert.That(app.ExitRequested).IsTrue();
    }

    /// <summary>
    /// Closing the window while the native F1 is open: the window sends ⌃Q. With native modals the prompt opens in front
    /// of F1, which stays open under it, and Stay goes back to the post.
    /// </summary>
    [Test]
    public async Task TheQuitPromptOpensInFrontOfTheNativeComposerWhichStaysOpen()
    {
        var (app, modals, composer) = await Connected(new FakeModals(), new FakeComposer());
        await using var _ = app;

        app.SimulateKey(Key(ConsoleKey.F1));
        composer!.Opened.Single().Edited("half a post");
        app.SimulateKey(CtrlQ);

        var quit = modals!.Shown.OfType<QuitSession>().Single();
        await Assert.That(app.NativeComposerIsOpen).IsTrue().Because("F1 is not closed to make room for the prompt");
        await Assert.That(composer.Closed).IsEqualTo(0);

        quit.Answer(false);
        await Assert.That(app.NativeComposerIsOpen).IsTrue();
        await Assert.That(app.NativeComposerSession!.Model.Body).IsEqualTo("half a post");
    }

    /// <summary>
    /// The known bug: with a host that draws F1 but not the modals, the cell prompt is a framework window in the cells,
    /// and F1 drawn over the cells would hide it. F1 is closed first, keeping its draft, and the prompt counts it.
    /// </summary>
    [Test]
    public async Task WithNoNativeModalsTheNativeComposerClosesSoTheCellPromptIsInFront()
    {
        var (app, _, composer) = await Connected(modals: null, new FakeComposer());
        await using var _ = app;

        app.SimulateKey(Key(ConsoleKey.F1));
        composer!.Opened.Single().Edited("half a post");
        app.SimulateKey(CtrlQ);

        await Assert.That(app.NativeComposerIsOpen).IsFalse().Because("nothing native is left over the cell prompt");
        await Assert.That(composer.Closed).IsGreaterThanOrEqualTo(1);
        await Assert.That(app.QuitPromptOpen).IsTrue();
        await Assert.That(string.Join("\n", app.QuitPromptLines)).Contains("draft");

        app.SimulateQuitKey(Key(ConsoleKey.N, 0, 'n'));
        app.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(composer.Opened[^1].Model.Body).IsEqualTo("half a post").Because("the draft was kept");
    }
}
