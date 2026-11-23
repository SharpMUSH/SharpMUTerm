using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A world connected with no character — <c>/connect game.convergencemush.com</c>, a link, Browse — has a session keyed
/// by the world's name and a window titled with it. Reported: that tab could not be closed (⌃W, ⌃B x and the tab's ×
/// did nothing), because the first session takes the main window and the main window was closable only once its
/// character was deleted; and once it was gone the sidebar said "no characters" and offered no way back.
/// </summary>
[NotInParallel]
public class WorldWithoutCharacterTests
{
    private const string Host = "game.convergencemush.com";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App(AppConfiguration config)
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        app.TelnetFactory = _ => new RecordingTelnetSession();
        return app;
    }

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    [Test]
    public async Task ItsTabClosesAndTheConnectionEndsWithIt()
    {
        await using var app = App(new AppConfiguration());
        Type(app, $"/connect {Host}");
        await app.LastCommand;
        app.RenderSnapshot();
        await Assert.That(app.FindSession(Host)!.IsConnected).IsTrue();
        await Assert.That(app.PrefixFactsSnapshot.ActiveWindowIsMain).IsFalse(); // ⌃B x is not dimmed

        await Assert.That(app.DispatchCommand("layout:close")).IsTrue();
        await app.LastCommand;

        await Assert.That(app.FindSession(Host)).IsNull();
        await Assert.That(app.WindowIds()).IsEquivalentTo(new[] { "main" });
        await Assert.That(app.ActiveSessionKey).IsNull();
        await Assert.That(app.StatusMarkup).Contains($"closed {Host} and disconnected");
    }

    [Test]
    public async Task ItsTabCarriesTheCloseButton()
    {
        await using var app = App(new AppConfiguration());
        Type(app, $"/connect {Host}");
        await app.LastCommand;
        app.RenderSnapshot();

        await Assert.That(app.PaneTabStrip(app.FocusedPaneId).Single(t => t.WindowId == "main").Closable).IsTrue();
    }

    [Test]
    public async Task TheSidebarAndThePaletteOpenItAgain()
    {
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition { Name = Host, Host = Host, Port = 4201 });
        await using var app = App(config);
        app.RenderSnapshot();

        var row = app.BuildSidebar().Rows().Single(r => r.Label == "no character");
        await Assert.That(row.Target).IsEqualTo(CommandIds.Character(Host));
        await Assert.That(app.BuildCatalog().Any(c => c.Title == $"Switch to {Host}" && c.Id == CommandIds.Character(Host))).IsTrue();

        await Assert.That(app.DispatchRailTarget(row.Target!)).IsTrue();

        await Assert.That(app.ActiveSessionKey).IsEqualTo(Host);
        await Assert.That(app.FindSession(Host)!.Character).IsNull();
    }

    [Test]
    public async Task TheFoldedRailGivesItTheWorldsLetter()
    {
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition { Name = Host, Host = Host, Port = 4201 });
        await using var app = App(config);

        var strip = RailRenderer.RenderCollapsed(app.BuildSidebar().Rows());
        var plain = string.Join('\n', strip.Select(SharpConsoleUI.Parsing.MarkupParser.Remove));

        await Assert.That(plain).Contains("○g");
        await Assert.That(plain).DoesNotContain("○n");
    }

    [Test]
    public async Task ACharactersOwnWindowStillStaysAndSaysSo()
    {
        await using var app = App(DemoScene.Build());
        app.DispatchCommand("char:Aetherfall.Corvid");
        app.RenderSnapshot();

        await Assert.That(app.DispatchCommand("layout:close")).IsTrue();

        await Assert.That(app.WindowIds()).Contains("main");
        await Assert.That(app.StatusMarkup).Contains("stays open");
    }
}
