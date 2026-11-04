using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Accessibility;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A <c>telnet://</c> link in the client: it connects, as <c>/connect host port</c> does, and it never sends. Plus the
/// two other things a desktop expects of the client — the window title and the screen reader's text — and the hand-off
/// that gives a second launch's link to the first.
/// </summary>
[NotInParallel]
public class TelnetLinkClientTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static (SharpMUTermApp App, RecordingTelnetSession Wire, AppConfiguration Config) Open(
        Action<AppConfiguration>? configure = null, IAnnouncer? announcer = null, IWindowTitle? title = null)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        configure?.Invoke(config);
        var wire = new RecordingTelnetSession();
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null) with
        {
            Announcer = announcer,
            Title = title,
        };
        var app = new SharpMUTermApp(config, host);
        app.TelnetFactory = _ => wire;
        return (app, wire, config);
    }

    [Test]
    public async Task ALinkAddsAWorldWithNoCharacterAndDialsItSendingNothing()
    {
        var (app, wire, config) = Open();
        await using var _app = app;

        app.OpenLink("telnets://mud.example.org:6666/");
        await app.LastCommand;

        var world = config.Worlds.Single();
        await Assert.That(world.Host).IsEqualTo("mud.example.org");
        await Assert.That(world.Port).IsEqualTo(6666);
        await Assert.That(world.UseTls).IsTrue();
        await Assert.That(world.Characters).IsEmpty();
        await Assert.That(app.FindSession("mud.example.org")!.State).IsEqualTo(ConnectionState.Connected);
        await Assert.That(wire.Lines).IsEmpty();
    }

    [Test]
    public async Task ALinkToAConfiguredEndpointDialsThatWorldAndAddsNone()
    {
        var (app, _, config) = Open(c => c.Worlds.Add(new WorldDefinition { Name = "Home", Host = "mud.example.org", Port = 4201 }));
        await using var _app = app;

        app.OpenLink("telnet://MUD.example.org:4201");
        await app.LastCommand;

        await Assert.That(config.Worlds.Count).IsEqualTo(1);
        await Assert.That(app.FindSession("Home")!.State).IsEqualTo(ConnectionState.Connected);
    }

    /// <summary>A link is matched on its endpoint, never on a name: one naming a world reaches nothing of it.</summary>
    [Test]
    public async Task ALinkCannotReachAWorldByNamingIt()
    {
        var (app, _, config) = Open(c => c.Worlds.Add(new WorldDefinition { Name = "Home", Host = "home.example.org", Port = 4201 }));
        await using var _app = app;

        app.OpenLink("telnet://Home");
        await app.LastCommand;

        await Assert.That(config.Worlds.Count).IsEqualTo(2);
        await Assert.That(config.Worlds[1].Host).IsEqualTo("Home");
        await Assert.That(app.FindSession("Home 2")).IsNotNull();
    }

    [Test]
    [Arguments("telnet://wizard:pw@mud.example.org")]
    [Arguments("telnet://mud.example.org:70000")]
    [Arguments("telnet://mud.example.org:4201/@shutdown")]
    [Arguments("telnet://mud.example.org\r\n@shutdown")]
    [Arguments("javascript:alert(1)")]
    public async Task AHostileLinkAddsNothingDialsNothingAndSaysWhy(string text)
    {
        var (app, wire, config) = Open();
        await using var _app = app;

        app.OpenLink(text);

        await Assert.That(config.Worlds).IsEmpty();
        await Assert.That(wire.ConnectAttempts).IsEqualTo(0);
        await Assert.That(app.Messages.Entries.Any(m => m.Text.Contains("not following that link"))).IsTrue();
    }

    [Test]
    public async Task TheLinkOnTheCommandLineIsFoundAndIsNotReadAsAHost()
    {
        string[] args = ["--name", "telnet://not-this", "telnet://mud.example.org:23", "--tls"];

        await Assert.That(Program.LinkArgument(args)).IsEqualTo("telnet://mud.example.org:23");
        await Assert.That(Program.LinkArgument(["mud.example.org", "4201"])).IsNull();
    }

    /// <summary>
    /// A Windows link handler pastes the link into a quoted command line; a link holding a quote can close it and add
    /// arguments. With --open-link, everything but the argument after it is dropped before anything reads it.
    /// </summary>
    [Test]
    public async Task ALinkHandlersLaunchReadsTheLinkAndNothingElse()
    {
        string[] injected = ["--open-link", "telnet://mud.example.org", "--render-png", "C:\\evil.png", "--demo-config"];

        await Assert.That(ClientOptions.NarrowToLink(injected)).IsEquivalentTo(new[] { "telnet://mud.example.org" });
        await Assert.That(ClientOptions.NarrowToLink(["--open-link"])).IsEmpty();
        await Assert.That(ClientOptions.NarrowToLink(["mud.example.org", "4201"])).IsEquivalentTo(new[] { "mud.example.org", "4201" });
    }

    private sealed class Titles : IWindowTitle
    {
        public List<string> Shown { get; } = [];

        public void Show(string title) => Shown.Add(title);

        public void Restore() => Shown.Add("<restored>");
    }

    [Test]
    public async Task TheTitleFollowsTheWindowInFrontAndReachesTheHostOnlyOnceTheLoopRuns()
    {
        var titles = new Titles();
        var (app, _, _) = Open(
            c =>
            {
                var world = new WorldDefinition { Name = "Mush", Host = "mush.example", Port = 4201 };
                world.Characters.Add(new CharacterDefinition { Name = "Ann" });
                world.Characters.Add(new CharacterDefinition { Name = "Bea" });
                c.Worlds.Add(world);
            },
            title: titles);
        await using var _app = app;

        // Worked out from the first header, but not written anywhere until the loop runs.
        await Assert.That(app.ShownTitle).IsNotNull();
        await Assert.That(titles.Shown).IsEmpty();

        app.StartTitle();
        app.DispatchCommand(CommandIds.Character("Mush.Ann"));
        await Assert.That(app.ShownTitle).IsEqualTo("Ann@Mush — SharpMUTerm");

        app.DispatchCommand(CommandIds.Character("Mush.Bea"));
        await Assert.That(app.ShownTitle).IsEqualTo("Bea@Mush — SharpMUTerm");
        await Assert.That(titles.Shown.Count).IsGreaterThanOrEqualTo(3);
        await Assert.That(titles.Shown[^2..]).IsEquivalentTo(new[] { "Ann@Mush — SharpMUTerm", "Bea@Mush — SharpMUTerm" });
    }

    [Test]
    public async Task TheTerminalsTitleSequenceCannotBeEndedEarly()
    {
        var sequence = TerminalTitle.Sequence("Ann\u0007@\u001b\\Mush");

        await Assert.That(sequence).IsEqualTo("\u001b]2;Ann@\\Mush\u001b\\");
    }

    private sealed class Reader : IAnnouncer
    {
        public (string Window, IReadOnlyList<string> Lines)? Last { get; private set; }

        public void Announce(string text, AnnouncementKind kind)
        {
        }

        public void Reading(string window, IReadOnlyList<string> lines) => Last = (window, lines);
    }

    [Test]
    public async Task TheReaderIsGivenTheWindowInFrontAndItsNewestLines()
    {
        var reader = new Reader();
        var (app, wire, _) = Open(
            c =>
            {
                var world = new WorldDefinition { Name = "Mush", Host = "mush.example", Port = 4201 };
                world.Characters.Add(new CharacterDefinition { Name = "Ann" });
                c.Worlds.Add(world);
            },
            announcer: reader);
        await using var _app = app;
        app.DispatchCommand(CommandIds.Character("Mush.Ann"));
        await app.FindSession("Mush.Ann")!.ConnectAsync();

        for (var i = 0; i < IAnnouncer.ReadingLines + 5; i++)
        {
            wire.Receive($"line {i}");
        }

        app.RenderNextFrame();

        var (window, lines) = reader.Last!.Value;
        await Assert.That(window).IsEqualTo("Ann");
        await Assert.That(lines.Count).IsEqualTo(IAnnouncer.ReadingLines);
        await Assert.That(lines[^1]).IsEqualTo($"line {IAnnouncer.ReadingLines + 4}");
        await Assert.That(lines[0]).IsEqualTo("line 5");
    }

    [Test]
    public async Task ASecondLaunchHandsItsLinkToTheFirst()
    {
        var path = Path.Combine(Path.GetTempPath(), $"smt-{Guid.NewGuid():N}"[..16] + ".sock");
        var received = new TaskCompletionSource<string>();
        using (var listener = LinkHandoff.Listen(received.SetResult, path))
        {
            await Assert.That(listener).IsNotNull();

            // A second listener on the same socket stands down: one client takes the links.
            await Assert.That(LinkHandoff.Listen(_ => { }, path)).IsNull();

            await Assert.That(LinkHandoff.TrySend("telnet://mud.example.org:4201", path)).IsTrue();
            await Assert.That(await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo("telnet://mud.example.org:4201");
        }

        // Nobody listening: the launch runs the link itself.
        await Assert.That(LinkHandoff.TrySend("telnet://mud.example.org:4201", path)).IsFalse();
        await Assert.That(LinkHandoff.TrySend("telnet://x\ny", path)).IsFalse();
    }

    [Test]
    public async Task AStaleSocketFileIsTakenOver()
    {
        var path = Path.Combine(Path.GetTempPath(), $"smt-{Guid.NewGuid():N}"[..16] + ".sock");
        await File.WriteAllTextAsync(path, string.Empty);
        try
        {
            using var listener = LinkHandoff.Listen(_ => { }, path);
            await Assert.That(listener).IsNotNull();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
