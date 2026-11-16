using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A player who deletes a character can close the pane it was in. The first character opened prints into
/// the shell's main window, which never closes while it is a character's session; once the character is
/// deleted nothing else reaches it (the rail and ⌃P list configured characters only), so ⌃W, the cell
/// <c>×</c>, the tab menu and the window's native tab all refused and the pane stayed for good. Closing it
/// now also ends the session, which would otherwise stay connected with no way to it.
/// </summary>
[NotInParallel]
public class DeletedCharacterWindowTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly ConsoleKeyInfo CtrlW = new('\x17', ConsoleKey.W, false, false, true);

    private sealed class PanePresenter : IPointerPresenter
    {
        public List<PaneChromeOverlay> Published { get; } = [];

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

        public void PaneChrome(PaneChromeOverlay chrome) => Published.Add(chrome);
    }

    /// <summary>Ann (Alfa) and, when asked, Bob (Bravo), both opened; Ann first, so she has the main window, and back in front.</summary>
    private static async Task<SharpMUTermApp> Open(bool withBob, HostChrome? chrome = null, IPointerPresenter? pointer = null)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        foreach (var (world, name) in withBob ? new[] { ("Alfa", "Ann"), ("Bravo", "Bob") } : new[] { ("Alfa", "Ann") })
        {
            var definition = new WorldDefinition { Name = world, Host = $"{world.ToLowerInvariant()}.example.org", Port = 4000 };
            definition.Characters.Add(new CharacterDefinition { Name = name, Logging = new LoggingSettings() });
            config.Worlds.Add(definition);
        }

        var host = ClientHost.Headless(new HeadlessConsoleDriver(120, 32), Caps) with
        {
            Chrome = chrome ?? HostChrome.None,
            Pointer = pointer,
        };
        var app = new SharpMUTermApp(config, host) { TelnetFactory = _ => new RecordingTelnetSession() };
        foreach (var key in withBob ? new[] { "Alfa.Ann", "Bravo.Bob" } : new[] { "Alfa.Ann" })
        {
            await Assert.That(app.DispatchCommand(CommandIds.Character(key))).IsTrue();
            await app.FindSession(key)!.ConnectAsync();
        }

        await Assert.That(app.DispatchCommand(CommandIds.Character("Alfa.Ann"))).IsTrue(); // the first opened has main

        app.RenderSnapshot();
        await Assert.That(app.ActiveWindowId()).IsEqualTo("main");
        return app;
    }

    private static void DeleteAnn(SharpMUTermApp app)
    {
        app.Configuration.Worlds[0].Characters.Clear();
        app.SaveConfiguration();
        app.RenderNextFrame();
    }

    private static List<string> Windows(SharpMUTermApp app) => app.CaptureSession().Windows.Select(w => w.Id).ToList();

    [Test]
    public async Task ACharactersMainWindowStillDoesNotClose()
    {
        await using var app = await Open(withBob: true);
        app.SimulateKey(CtrlW);
        await Assert.That(Windows(app)).Contains("main");
        await Assert.That(app.FindSession("Alfa.Ann")).IsNotNull();
    }

    [Test]
    public async Task RenamingTheCharacterDoesNotMakeItsMainWindowClosable()
    {
        await using var app = await Open(withBob: true);
        app.Configuration.Worlds[0].Characters[0].Name = "Annie";
        app.SaveConfiguration();
        app.SimulateKey(CtrlW);
        await Assert.That(Windows(app)).Contains("main");
    }

    [Test]
    public async Task CtrlWClosesADeletedCharactersMainWindowAndEndsItsSession()
    {
        await using var app = await Open(withBob: true);
        DeleteAnn(app);

        app.SimulateKey(CtrlW);
        await app.LastCommand;

        await Assert.That(Windows(app)).DoesNotContain("main");
        await Assert.That(Windows(app)).Contains("char:Bravo.Bob");
        await Assert.That(app.FindSession("Alfa.Ann")).IsNull().Because("nothing could reach it any more");
        await Assert.That(app.FindSession("Bravo.Bob")).IsNotNull();
        await Assert.That(app.ActiveWindowId()).IsEqualTo("char:Bravo.Bob");
    }

    [Test]
    public async Task TheCellTabOffersItsCloseOnceTheCharacterIsDeleted()
    {
        await using var app = await Open(withBob: true);
        var pane = app.PaneSnapshot().Rects.Keys.Single(p => app.PaneTabStrip(p).Any(t => t.WindowId == "main"));
        await Assert.That(app.PaneTabStrip(pane).Single(t => t.WindowId == "main").Closable).IsFalse();

        DeleteAnn(app);

        await Assert.That(app.PaneTabStrip(pane).Single(t => t.WindowId == "main").Closable).IsTrue();
    }

    [Test]
    public async Task TheWindowsNativeTabClosesADeletedCharactersMainWindow()
    {
        var pointer = new PanePresenter();
        await using var app = await Open(withBob: true, HostChrome.Native, pointer);
        var before = pointer.Published.Last().Strips.Single(s => s.Model.Find("main") is not null);
        await Assert.That(app.CloseOfferedStripTab(before.Model.PaneId, "main")).IsFalse();

        DeleteAnn(app);

        var strip = pointer.Published.Last().Strips.Single(s => s.Model.Find("main") is not null);
        await Assert.That(strip.Model.Find("main")!.Closable).IsTrue();
        await Assert.That(app.CloseOfferedStripTab(strip.Model.PaneId, "main")).IsTrue();
        await app.LastCommand;
        await Assert.That(Windows(app)).DoesNotContain("main");
        await Assert.That(app.FindSession("Alfa.Ann")).IsNull();
    }

    [Test]
    public async Task ClosingTheLastWindowLeavesTheShellsOwnMainWindow()
    {
        await using var app = await Open(withBob: false);
        DeleteAnn(app);

        app.SimulateKey(CtrlW);
        await app.LastCommand;

        var windows = app.CaptureSession().Windows;
        await Assert.That(windows.Select(w => w.Id)).IsEquivalentTo(new[] { "main" });
        await Assert.That(windows.Single().SessionKey).IsNull().Because("it is nobody's now");
        await Assert.That(app.FindSession("Alfa.Ann")).IsNull();

        // A character opened later prints into it, as at a start with nothing open.
        app.Configuration.Worlds[0].Characters.Add(new CharacterDefinition { Name = "Ada", Logging = new LoggingSettings() });
        await Assert.That(app.DispatchCommand(CommandIds.Character("Alfa.Ada"))).IsTrue();
        await Assert.That(app.ActiveWindowId()).IsEqualTo("main");
    }
}
