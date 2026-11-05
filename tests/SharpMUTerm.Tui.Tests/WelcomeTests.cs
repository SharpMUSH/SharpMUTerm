using SharpMUTerm.Client.Modals;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Accessibility;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The first run: a client with no worlds opens on the welcome, its form dials through <c>/connect</c>'s own
/// path, and the first connect of a new configuration leaves a one-line key tour, once.
/// </summary>
[NotInParallel]
public class WelcomeTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class Recorder : IAnnouncer
    {
        public List<string> Heard { get; } = [];

        public void Announce(string text, AnnouncementKind kind) => Heard.Add(text);
    }

    private sealed record Run(SharpMUTermApp App, RecordingTelnetSession Wire, AppConfiguration Config, List<bool> Saved)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }

    private static async Task<Run> Start(AppConfiguration? config = null, Func<ClientHost, ClientHost>? host = null)
    {
        Console.SetIn(TextReader.Null);
        config ??= new AppConfiguration();
        var saved = new List<bool>();
        var wire = new RecordingTelnetSession();
        var baseHost = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null);
        var app = new SharpMUTermApp(config, host?.Invoke(baseHost) ?? baseHost, save: c => saved.Add(c.KeyTourPending));
        app.TelnetFactory = _ => wire;
        await app.StartAsync([]);
        return new Run(app, wire, config, saved);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0', bool shift = false) => new(c, key, shift, false, false);

    private static void Type(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateWelcomeKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }
    }

    private static void TypeCommand(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.SimulateKey(Key(ConsoleKey.Enter, '\r'));
    }

    private static AppConfiguration OneWorld() => new()
    {
        Worlds =
        {
            new WorldDefinition
            {
                Name = "Mush",
                Host = "mush.example",
                Port = 4201,
                Characters = { new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() } },
            },
        },
    };

    [Test]
    public async Task AClientWithNoWorldsOpensOnTheWelcomeAndKeepsTheNotice()
    {
        await using var run = await Start();

        await Assert.That(run.App.WelcomeIsOpen).IsTrue();
        var text = string.Join('\n', run.App.WelcomeLines.Select(MarkupText.Plain));
        await Assert.That(text).Contains("Connect to a world").And.Contains("Browse worlds").And.Contains("Open settings");
        await Assert.That(text).Contains(WelcomeSurface.ExampleCommand);
        await Assert.That(run.App.StatusMarkup).Contains("No world configured");
    }

    /// <summary>
    /// The window's demo lays the <c>welcome</c> scene out (which runs the startup pass) and then its loop's
    /// <c>Run</c> schedules the startup pass again. That raised the notice twice — two identical toasts in the
    /// window. A launch has one startup pass.
    /// </summary>
    [Test]
    public async Task TheWelcomeSceneThenTheLoopsStartupRaiseTheNoticeOnce()
    {
        Console.SetIn(TextReader.Null);
        await using var app = new SharpMUTermApp(DemoScene.Build(), ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null));

        app.RenderSnapshot("welcome");
        app.ScheduleStartup([]);

        var raised = app.Messages.Entries.Count(e => e.Text.Contains("No world configured", StringComparison.Ordinal));
        await Assert.That(raised).IsEqualTo(1);
        await Assert.That(app.WelcomeIsOpen).IsTrue();
    }

    [Test]
    public async Task AClientWithWorldsDoesNotOpenIt()
    {
        await using var run = await Start(OneWorld());

        await Assert.That(run.App.WelcomeIsOpen).IsFalse();
    }

    [Test]
    public async Task EscClosesItForTheRunAndSlashWelcomeBringsItBack()
    {
        await using var run = await Start();

        run.App.SimulateWelcomeKey(Key(ConsoleKey.Escape, '\x1b'));
        await Assert.That(run.App.WelcomeIsOpen).IsFalse();
        await Assert.That(run.App.StatusMarkup).Contains("No world configured");

        TypeCommand(run.App, "/welcome");
        await Assert.That(run.App.WelcomeIsOpen).IsTrue();
    }

    [Test]
    public async Task TheCommandSurfaceReopensIt()
    {
        await using var run = await Start();
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Escape, '\x1b'));

        await Assert.That(run.App.DispatchCommand("term:welcome")).IsTrue();
        await Assert.That(run.App.WelcomeIsOpen).IsTrue();
    }

    /// <summary>The form, driven with keys, adds the world <c>/connect</c> would have, with the character it named, and dials it.</summary>
    [Test]
    public async Task TheFormAddsTheWorldAndCharacterAndDials()
    {
        await using var run = await Start();

        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));
        await Assert.That(run.App.Welcome!.InForm).IsTrue();
        Type(run.App, "mud.example.org");
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Tab, '\t'));
        for (var i = 0; i < 4; i++)
        {
            run.App.SimulateWelcomeKey(Key(ConsoleKey.Backspace, '\b'));
        }

        Type(run.App, "4201");
        run.App.SimulateWelcomeKey(Key(ConsoleKey.DownArrow));
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Spacebar, ' '));
        run.App.SimulateWelcomeKey(Key(ConsoleKey.DownArrow));
        Type(run.App, "Corvid");
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));
        await run.App.LastCommand;

        await Assert.That(run.App.WelcomeIsOpen).IsFalse();
        await Assert.That(run.Config.Worlds.Count).IsEqualTo(1);
        var world = run.Config.Worlds[0];
        await Assert.That(world.Host).IsEqualTo("mud.example.org");
        await Assert.That(world.Port).IsEqualTo(4201);
        await Assert.That(world.UseTls).IsTrue();
        await Assert.That(world.Characters.Single().Name).IsEqualTo("Corvid");
        await Assert.That(world.Layout).IsNull().Because("the suggestion is left to apply when nothing was chosen");
        await Assert.That(run.App.FindSession("mud.example.org.Corvid")!.State).IsEqualTo(ConnectionState.Connected);
    }

    [Test]
    public async Task APickedLayoutIsTheNewWorldsOwn()
    {
        await using var run = await Start();

        run.App.SimulateWelcomeKey(Key(ConsoleKey.RightArrow));
        run.App.SimulateWelcomeKey(Key(ConsoleKey.RightArrow));
        await Assert.That(run.App.Welcome!.Layout).IsEqualTo(WelcomeLayout.Combat);
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));
        Type(run.App, "mud.example.org");
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));
        await run.App.LastCommand;

        await Assert.That(run.Config.Worlds.Single().Layout).IsEqualTo(BuiltInLayouts.CombatId);
        await Assert.That(run.Config.Worlds.Single().Port).IsEqualTo(SharpMUTermApp.DefaultConnectPort);
    }

    /// <summary>The layout row applies to a <c>/connect</c> typed under the welcome too: it is the first world either way.</summary>
    [Test]
    public async Task ThePickedLayoutReachesAConnectTypedOnTheCommandLine()
    {
        await using var run = await Start();
        run.App.SimulateWelcomeKey(Key(ConsoleKey.RightArrow));
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Escape, '\x1b'));

        TypeCommand(run.App, "/connect mud.example.org 4201");
        await run.App.LastCommand;

        await Assert.That(run.Config.Worlds.Single().Layout).IsEqualTo(BuiltInLayouts.RoleplayId);
    }

    [Test]
    public async Task ABadPortIsRefusedInPlace()
    {
        await using var run = await Start();
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));
        Type(run.App, "mud.example.org");
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Tab, '\t'));
        Type(run.App, "0000"); // 40000000 is too many digits to take, and 4000 0000 is not a port
        for (var i = 0; i < 5; i++)
        {
            run.App.SimulateWelcomeKey(Key(ConsoleKey.Backspace, '\b'));
        }

        Type(run.App, "0");
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(run.App.WelcomeIsOpen).IsTrue();
        await Assert.That(run.Config.Worlds.Count).IsEqualTo(0);
        await Assert.That(string.Join('\n', run.App.WelcomeLines.Select(MarkupText.Plain))).Contains("port is a number");
    }

    [Test]
    public async Task EscOnTheFormGoesBackToTheMenu()
    {
        await using var run = await Start();
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));

        run.App.SimulateWelcomeKey(Key(ConsoleKey.Escape, '\x1b'));

        await Assert.That(run.App.WelcomeIsOpen).IsTrue();
        await Assert.That(run.App.Welcome!.InForm).IsFalse();
    }

    /// <summary>With no world directory to read, Browse says so where the player is looking and fetches nothing.</summary>
    [Test]
    public async Task BrowseWithNoIndexSaysSoInPlace()
    {
        await using var run = await Start(new AppConfiguration { WorldDirectoryUrl = string.Empty });

        run.App.SimulateWelcomeKey(Key(ConsoleKey.DownArrow));
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(run.App.WelcomeIsOpen).IsTrue();
        await Assert.That(string.Join('\n', run.App.WelcomeLines.Select(MarkupText.Plain))).Contains("no world directory is set");
    }

    [Test]
    public async Task OpenSettingsGoesToF5()
    {
        await using var run = await Start();

        run.App.SimulateWelcomeKey(Key(ConsoleKey.DownArrow));
        run.App.SimulateWelcomeKey(Key(ConsoleKey.DownArrow));
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(run.App.WelcomeIsOpen).IsFalse();
        await Assert.That(run.App.OpenSettingsKey).IsEqualTo(ConsoleKey.F5);
    }

    // ---- The key tour ---------------------------------------------------------------------------

    [Test]
    public async Task TheFirstConnectOfANewConfigurationShowsTheTourOnceAndSaysSoOnDisk()
    {
        await using var run = await Start();
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Escape, '\x1b'));

        TypeCommand(run.App, "/connect mud.example.org 4201");
        await run.App.LastCommand;

        await Assert.That(run.App.KeyTourShown).IsTrue();
        await Assert.That(run.App.PaneLines(SharpMUTermApp.MainWindowId).Any(l => l.Contains(SharpMUTermApp.KeyTourLead))).IsTrue();
        await Assert.That(run.Saved).Contains(true).Because("the world's save carries the tour as owed");
        await Assert.That(run.Config.KeyTourPending).IsFalse();
        await Assert.That(run.Saved[^1]).IsFalse().Because("and the save after it, once shown, does not");
    }

    [Test]
    public async Task AConfigurationThatStartedWithWorldsNeverGetsTheTour()
    {
        await using var run = await Start(OneWorld());

        TypeCommand(run.App, "/connect Mush");
        await run.App.LastCommand;

        await Assert.That(run.App.KeyTourShown).IsFalse();
        await Assert.That(run.Config.KeyTourPending).IsFalse();
    }

    [Test]
    public async Task AnOwedTourIsShownOnTheNextRunsConnect()
    {
        var config = OneWorld();
        config.KeyTourPending = true;
        await using var run = await Start(config);

        TypeCommand(run.App, "/connect Mush");
        await run.App.LastCommand;

        await Assert.That(run.App.KeyTourShown).IsTrue();
        await Assert.That(config.KeyTourPending).IsFalse();
    }

    [Test]
    public async Task TheCommandSurfaceHidesTheTourAndShowsItAgain()
    {
        await using var run = await Start();
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Escape, '\x1b'));
        TypeCommand(run.App, "/connect mud.example.org 4201");
        await run.App.LastCommand;

        run.App.DispatchCommand("term:tour");
        await Assert.That(run.App.KeyTourShown).IsFalse();
        await Assert.That(run.App.PaneLines(SharpMUTermApp.MainWindowId).Any(l => l.Contains(SharpMUTermApp.KeyTourLead))).IsFalse();

        run.App.DispatchCommand("term:tour");
        await Assert.That(run.App.KeyTourShown).IsTrue();
    }

    // ---- Where nobody sees the screen ------------------------------------------------------------

    /// <summary>
    /// <c>--plain</c>: no modal to take the reader's typing; the welcome is said as lines naming commands, and
    /// <c>/welcome layout</c> stands in for the arrows.
    /// </summary>
    [Test]
    public async Task WhereTheScreenIsNotSeenTheWelcomeIsLinesAndCommands()
    {
        var heard = new Recorder();
        await using var run = await Start(host: h => h with { Announcer = heard, ScreenIsSeen = false });

        await Assert.That(run.App.WelcomeIsOpen).IsFalse();
        await Assert.That(heard.Heard).Contains(line => line.Contains("/connect") && line.Contains(WelcomeSurface.ExampleCommand));

        TypeCommand(run.App, "/welcome layout combat");
        TypeCommand(run.App, "/connect mud.example.org 4201");
        await run.App.LastCommand;

        await Assert.That(run.Config.Worlds.Single().Layout).IsEqualTo(BuiltInLayouts.CombatId);
    }

    /// <summary>
    /// The cell welcome is the mockup's shape: its heading on a band across the whole inside, its keys on a band across the
    /// foot, the body indented between them, and the layout note beside the chips where the row has room for both.
    /// </summary>
    [Test]
    public async Task TheCellWelcomeHasAHeadingBandAndAKeyBand_AndTheLayoutNoteSitsBesideTheChipsWhereItFits()
    {
        var model = new WelcomeSurface(null).Model;
        var wide = WelcomeSurface.Render(model, WelcomeSurface.PreferredWidth + 2);
        var narrow = WelcomeSurface.Render(model, 60);
        var form = WelcomeSurface.Render(model with { InForm = true }, WelcomeSurface.PreferredWidth + 2);

        await Assert.That(wide[0]).StartsWith($"[on {ScreenPalette.HeaderBg}]");
        await Assert.That(MarkupText.VisibleLength(wide[0])).IsEqualTo(WelcomeSurface.PreferredWidth + 2);
        await Assert.That(MarkupText.Plain(wide[0]).Trim()).IsEqualTo(WelcomeModel.Heading);
        await Assert.That(wide[^1]).StartsWith($"[on {ScreenPalette.FooterBg}]");
        await Assert.That(MarkupText.VisibleLength(wide[^1])).IsEqualTo(WelcomeSurface.PreferredWidth + 2);
        await Assert.That(wide.Any(l => MarkupText.Plain(l).Contains("Combat") && MarkupText.Plain(l).Contains(WelcomeModel.LayoutNote))).IsTrue();
        await Assert.That(narrow.Any(l => MarkupText.Plain(l).Contains("Combat") && MarkupText.Plain(l).Contains(WelcomeModel.LayoutNote))).IsFalse();
        await Assert.That(narrow.Any(l => MarkupText.Plain(l).Contains(WelcomeModel.LayoutNote))).IsTrue();
        await Assert.That(form.Count).IsEqualTo(wide.Count).Because("one window holds the menu and the form");
        await Assert.That(MarkupText.Plain(form[0]).Trim()).IsEqualTo(WelcomeModel.FormHeading);
    }
}
