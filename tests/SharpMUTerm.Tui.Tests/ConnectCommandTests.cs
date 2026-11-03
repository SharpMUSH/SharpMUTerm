using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <c>/connect</c>: the way into a world from the command line alone, which is the only way <c>--plain</c> has.
/// </summary>
[NotInParallel]
public class ConnectCommandTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static (SharpMUTermApp App, RecordingTelnetSession Wire, AppConfiguration Config) Open(
        Action<AppConfiguration>? configure = null)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        configure?.Invoke(config);
        var wire = new RecordingTelnetSession();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        app.TelnetFactory = _ => wire;
        return (app, wire, config);
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
    public async Task AHostAndPortAddAWorldAndDialIt()
    {
        var (app, _, config) = Open();
        await using var _app = app;

        Type(app, "/connect mud.example.org 4201 tls");
        await app.LastCommand;

        await Assert.That(config.Worlds.Count).IsEqualTo(1);
        var world = config.Worlds[0];
        await Assert.That(world.Name).IsEqualTo("mud.example.org");
        await Assert.That(world.Host).IsEqualTo("mud.example.org");
        await Assert.That(world.Port).IsEqualTo(4201);
        await Assert.That(world.UseTls).IsTrue();
        await Assert.That(app.FindSession("mud.example.org")!.State)
            .IsEqualTo(ConnectionState.Connected);
    }

    [Test]
    public async Task AConfiguredWorldIsReachedByNameAndNotAddedAgain()
    {
        var (app, _, config) = Open(c => c.Worlds.Add(new WorldDefinition
        {
            Name = "Convergence",
            Host = "convergence.example",
            Port = 4201,
            Characters = { new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() } },
        }));
        await using var _app = app;

        Type(app, "/connect convergence");
        await app.LastCommand;

        await Assert.That(config.Worlds.Count).IsEqualTo(1);
        await Assert.That(app.FindSession("Convergence.Ann")!.State).IsEqualTo(ConnectionState.Connected);
    }

    [Test]
    public async Task ConnectingTwiceReusesTheSession()
    {
        var (app, _, config) = Open();
        await using var _app = app;

        Type(app, "/connect mud.example.org 4201");
        await app.LastCommand;
        var first = app.FindSession("mud.example.org");
        Type(app, "/connect mud.example.org");
        await app.LastCommand;

        await Assert.That(config.Worlds.Count).IsEqualTo(1);
        await Assert.That(app.FindSession("mud.example.org")).IsSameReferenceAs(first);
    }

    [Test]
    public async Task AMistypedNameIsRefusedRatherThanSavedAsAHost()
    {
        var (app, wire, config) = Open();
        await using var _app = app;

        Type(app, "/connect my favourite world");

        await Assert.That(config.Worlds).IsEmpty();
        await Assert.That(wire.Lines).IsEmpty();
    }

    [Test]
    [Arguments("host.example", "host.example", 4000)]
    [Arguments("host.example:23", "host.example", 23)]
    [Arguments("[::1]:4201", "::1", 4201)]
    [Arguments("10.0.0.5", "10.0.0.5", 4000)]
    public async Task EndpointsAreReadInEveryUsualShape(string text, string host, int port)
    {
        var endpoint = SharpMUTermApp.ParseEndpoint(new[] { text });

        await Assert.That(endpoint).IsEqualTo((host, port));
    }

    [Test]
    [Arguments("host.example", "0")]
    [Arguments("host.example", "70000")]
    [Arguments("host.example", "telnet")]
    public async Task ABadPortIsNoEndpoint(string host, string port)
    {
        await Assert.That(SharpMUTermApp.ParseEndpoint(new[] { host, port })).IsNull();
    }
}
