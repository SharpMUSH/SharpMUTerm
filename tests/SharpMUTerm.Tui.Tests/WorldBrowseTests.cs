using System.Text;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Accessibility;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Browse worlds: mu*index asked only when the player opens it, a pick that fills in the welcome's connect form and
/// saves nothing, and the player's own ⏎ there — through <c>/connect</c>'s path — that adds the world. Answered from the
/// demo's recorded directory; nothing here reaches the network.
/// </summary>
[NotInParallel]
public class WorldBrowseTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class Recorder : IAnnouncer
    {
        public List<string> Heard { get; } = [];

        public void Announce(string text, AnnouncementKind kind) => Heard.Add(text);
    }

    private sealed record Run(SharpMUTermApp App, AppConfiguration Config, List<Uri> Asked, List<bool> Saved) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => App.DisposeAsync();
    }

    private static async Task<Run> Start(
        bool fetch = true,
        Func<ClientHost, ClientHost>? host = null,
        Func<Uri, byte[]?>? answer = null,
        AppConfiguration? config = null)
    {
        Console.SetIn(TextReader.Null);
        config ??= new AppConfiguration();
        var asked = new List<Uri>();
        var saved = new List<bool>();
        var baseHost = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null);
        Func<Uri, CancellationToken, Task<byte[]?>>? worldFetch = fetch
            ? (uri, _) =>
            {
                asked.Add(uri);
                return Task.FromResult(answer is null ? Encoding.UTF8.GetBytes(DemoWorldDirectory.Answer(uri)) : answer(uri));
            }
            : null;
        var app = new SharpMUTermApp(config, host?.Invoke(baseHost) ?? baseHost, save: _ => saved.Add(true), worldFetch: worldFetch);
        app.TelnetFactory = _ => new RecordingTelnetSession();
        await app.StartAsync([]);
        return new Run(app, config, asked, saved);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static void TypeCommand(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.SimulateKey(Key(ConsoleKey.Enter, '\r'));
    }

    private static void OpenFromTheWelcome(SharpMUTermApp app)
    {
        app.SimulateWelcomeKey(Key(ConsoleKey.DownArrow));
        app.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));
    }

    [Test]
    public async Task NothingIsAskedAtStart()
    {
        await using var run = await Start();

        await Assert.That(run.Asked.Count).IsEqualTo(0);
        await Assert.That(run.App.WorldBrowseOpen).IsFalse();
    }

    [Test]
    public async Task TheWelcomesBrowseRowOpensTheListFromTheDirectory()
    {
        await using var run = await Start();

        OpenFromTheWelcome(run.App);

        await Assert.That(run.App.WorldBrowseOpen).IsTrue();
        await Assert.That(run.App.WelcomeIsOpen).IsFalse();
        await Assert.That(run.Asked.Single().AbsoluteUri).IsEqualTo("https://mu-index.com/api/games?limit=200&sort=players&unnamed=!yes");
        var model = run.App.WorldBrowseModelNow;
        await Assert.That(model.State).IsEqualTo(WorldBrowseState.Ready);
        await Assert.That(model.Rows[0].Name).IsEqualTo("Aetherfall");
    }

    [Test]
    public async Task ARunWithNoFetchSaysSoAndAsksNothing()
    {
        await using var run = await Start(fetch: false);

        OpenFromTheWelcome(run.App);

        await Assert.That(run.App.WorldBrowseOpen).IsTrue();
        await Assert.That(run.App.WorldBrowseModelNow.State).IsEqualTo(WorldBrowseState.Unavailable);
        await Assert.That(run.App.WorldBrowseModelNow.Empty("")).Contains("nothing was fetched");
    }

    [Test]
    public async Task ADirectoryThatDoesNotAnswerIsSaidHonestly()
    {
        await using var run = await Start(answer: _ => null);

        OpenFromTheWelcome(run.App);

        await Assert.That(run.App.WorldBrowseModelNow.State).IsEqualTo(WorldBrowseState.Failed);
        await Assert.That(run.App.WorldBrowseModelNow.Empty("")).StartsWith("mu*index did not answer:");
    }

    /// <summary>A pick of a world with one address fills the welcome's form in and saves nothing; the form's ⏎ adds it.</summary>
    [Test]
    public async Task APickWithOneAddressFillsInTheConnectFormAndOnlyItsEnterAddsTheWorld()
    {
        await using var run = await Start();
        OpenFromTheWelcome(run.App);

        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.DownArrow));
        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(run.App.WorldBrowseOpen).IsFalse();
        var form = run.App.Welcome!;
        await Assert.That(form.InForm).IsTrue();
        await Assert.That(form.Host).IsEqualTo("lanternport.example");
        await Assert.That(form.Port).IsEqualTo("2860");
        await Assert.That(form.Tls).IsFalse();
        await Assert.That(form.WorldName).IsEqualTo("Lanternport");
        await Assert.That(form.Encoding).IsNull();
        await Assert.That(run.Config.Worlds.Count).IsEqualTo(0).Because("nothing is saved before the form's ⏎");
        await Assert.That(run.Saved.Count).IsEqualTo(0);
        await Assert.That(string.Join('\n', run.App.WelcomeLines.Select(MarkupText.Plain))).Contains("as listed on mu*index");

        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));
        await run.App.LastCommand;

        var world = run.Config.Worlds.Single();
        await Assert.That(world.Name).IsEqualTo("Lanternport");
        await Assert.That(world.Host).IsEqualTo("lanternport.example");
        await Assert.That(world.Encoding).IsEqualTo("auto");
    }

    /// <summary>
    /// A world with several addresses is chosen from first — TLS active, then plain active, then stale — and the address
    /// chosen fills in the form with its TLS, and the encoding mu*index's non-UTF-8 charset names; still nothing is saved
    /// before the form's ⏎.
    /// </summary>
    [Test]
    public async Task APickWithSeveralAddressesIsChosenFromAndTheChoiceFillsInTheFormWithItsEncoding()
    {
        await using var run = await Start();
        OpenFromTheWelcome(run.App);

        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(run.App.WorldBrowseOpen).IsTrue();
        var game = run.App.WorldBrowseModelNow.Choosing!;
        await Assert.That(string.Join(",", game.Endpoints.Select(WorldBrowseModel.EndpointText)))
            .IsEqualTo("aetherfall.example:4202 — TLS — active,aetherfall.example:4201 — plain — active,aetherfall.example:4000 — plain — stale · not reached lately");
        await Assert.That(game.Charset).IsEqualTo("ISO-8859-1");
        var drawn = string.Join('\n', run.App.CellWorldBrowse!.Render(run.App.WorldBrowseModelNow, 96, 20).Select(MarkupText.Plain));
        await Assert.That(drawn).Contains("Aetherfall answers at 3 addresses").And.Contains("▸ aetherfall.example:4202").And.Contains("charset ISO-8859-1");

        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.DownArrow));
        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(run.App.WorldBrowseOpen).IsFalse();
        var form = run.App.Welcome!;
        await Assert.That(form.Host).IsEqualTo("aetherfall.example");
        await Assert.That(form.Port).IsEqualTo("4201");
        await Assert.That(form.Tls).IsFalse();
        await Assert.That(form.Encoding).IsEqualTo("ISO-8859-1");
        await Assert.That(run.Config.Worlds.Count).IsEqualTo(0);
        await Assert.That(string.Join('\n', run.App.WelcomeLines.Select(MarkupText.Plain))).Contains("encoding ISO-8859-1");

        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));
        await run.App.LastCommand;

        var world = run.Config.Worlds.Single();
        await Assert.That(world.Name).IsEqualTo("Aetherfall");
        await Assert.That(world.Port).IsEqualTo(4201);
        await Assert.That(world.UseTls).IsFalse();
        await Assert.That(world.Encoding).IsEqualTo("ISO-8859-1");
    }

    [Test]
    public async Task TheDefaultAddressIsTheFirstListedAndEscGoesBackToTheList()
    {
        await using var run = await Start();
        OpenFromTheWelcome(run.App);
        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Enter, '\r'));

        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Escape, '\x1b'));

        await Assert.That(run.App.WorldBrowseOpen).IsTrue().Because("Esc on the address choice goes back, not out");
        await Assert.That(run.App.WorldBrowseModelNow.Choosing).IsNull();

        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Enter, '\r'));
        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(run.App.Welcome!.Port).IsEqualTo("4202");
        await Assert.That(run.App.Welcome!.Tls).IsTrue();
    }

    [Test]
    public async Task ChangingTheHostDropsTheListedName()
    {
        await using var run = await Start();
        OpenFromTheWelcome(run.App);
        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.DownArrow));
        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Enter, '\r'));
        await Assert.That(run.App.Welcome!.Host).IsEqualTo("lanternport.example");

        // Back to the host field and a different host typed over the end of it.
        run.App.SimulateWelcomeKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, false));
        run.App.SimulateWelcomeKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, false));
        run.App.SimulateWelcomeKey(new ConsoleKeyInfo('\t', ConsoleKey.Tab, true, false, false));
        run.App.SimulateWelcomeKey(new ConsoleKeyInfo('x', ConsoleKey.NoName, false, false, false));
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Enter, '\r'));
        await run.App.LastCommand;

        var world = run.Config.Worlds.Single();
        await Assert.That(world.Host).IsNotEqualTo("lanternport.example");
        await Assert.That(world.Name).IsNotEqualTo("Lanternport");
    }

    [Test]
    public async Task APickTheDirectoryCannotPlaceStaysOnTheListAndSaysWhy()
    {
        await using var run = await Start(answer: uri => uri.AbsolutePath.EndsWith("/games", StringComparison.Ordinal)
            ? Encoding.UTF8.GetBytes(DemoWorldDirectory.Answer(uri))
            : Encoding.UTF8.GetBytes("{\"name\":\"x\",\"endpoints\":[]}"));
        OpenFromTheWelcome(run.App);

        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(run.App.WorldBrowseOpen).IsTrue();
        await Assert.That(run.App.WorldBrowseModelNow.Problem).IsEqualTo("Aetherfall not opened: the directory lists no telnet address for it");
        await Assert.That(run.App.WorldBrowseModelNow.Opening).IsNull();
    }

    [Test]
    public async Task AnAddressConnectCannotDialIsSaidAndNeverFillsTheForm()
    {
        await using var run = await Start(answer: uri => uri.AbsolutePath.EndsWith("/games", StringComparison.Ordinal)
            ? Encoding.UTF8.GetBytes(DemoWorldDirectory.Answer(uri))
            : Encoding.UTF8.GetBytes("{\"name\":\"x\",\"endpoints\":[{\"host\":\"bad host\",\"port\":4000,\"kind\":\"telnet\",\"state\":\"active\"}]}"));
        OpenFromTheWelcome(run.App);

        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(run.App.WorldBrowseOpen).IsTrue();
        await Assert.That(run.App.WelcomeIsOpen).IsFalse();
        await Assert.That(run.App.WorldBrowseModelNow.Problem).IsEqualTo("Aetherfall not opened: mu*index lists it at bad host:4000, which /connect cannot dial");
    }

    [Test]
    public async Task EnterOnASearchTheRowsCannotAnswerAsksTheDirectory()
    {
        await using var run = await Start();
        OpenFromTheWelcome(run.App);

        foreach (var c in "zork")
        {
            run.App.SimulateWorldBrowseKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        run.App.SimulateWorldBrowseKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(run.Asked.Count).IsEqualTo(2);
        await Assert.That(run.Asked[1].Query).Contains("q=zork");
        await Assert.That(run.App.WorldBrowseModelNow.AskedFor).IsEqualTo("zork");
        await Assert.That(run.App.WelcomeIsOpen).IsFalse().Because("no world was picked");
    }

    [Test]
    public async Task TheCommandPaletteEntryOpensItAndAPackageBrowseIsUntouched()
    {
        await using var run = await Start();
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Escape, '\x1b'));

        await Assert.That(run.App.DispatchCommand(BuiltInCommands.BrowseWorldsCommand)).IsTrue();

        await Assert.That(run.App.WorldBrowseOpen).IsTrue();
        await Assert.That(run.Asked.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SlashBrowseWithWordsAsksTheDirectoryForThem()
    {
        await using var run = await Start();
        run.App.SimulateWelcomeKey(Key(ConsoleKey.Escape, '\x1b'));

        TypeCommand(run.App, "/browse harbour");

        await Assert.That(run.App.WorldBrowseOpen).IsTrue();
        await Assert.That(run.Asked.Single().Query).Contains("q=harbour");
        await Assert.That(run.App.WorldBrowseModelNow.Rows.Single().Name).IsEqualTo("Lanternport");
    }

    /// <summary><c>--plain</c>: the rows are said as numbered lines, and <c>/browse 2</c> says the <c>/connect</c> line to type.</summary>
    [Test]
    public async Task WhereTheScreenIsNotSeenTheListIsLinesAndANumberSaysTheConnectLine()
    {
        var heard = new Recorder();
        await using var run = await Start(host: h => h with { Announcer = heard, ScreenIsSeen = false });

        TypeCommand(run.App, "/browse");

        await Assert.That(run.App.WorldBrowseOpen).IsFalse();
        await Assert.That(heard.Heard).Contains(WorldBrowseModel.AttributionLine);
        await Assert.That(heard.Heard).Contains(line => line.StartsWith("2. Lanternport", StringComparison.Ordinal));

        TypeCommand(run.App, "/browse 2");

        await Assert.That(heard.Heard).Contains("To add it and connect, type: /connect lanternport.example 2860");
        await Assert.That(run.Config.Worlds.Count).IsEqualTo(0);
    }

    /// <summary><c>--plain</c>: a world with several addresses has them said as numbered lines, and <c>/browse 1 2</c> says the second's line.</summary>
    [Test]
    public async Task WhereTheScreenIsNotSeenSeveralAddressesAreNumberedAndASecondNumberSaysOne()
    {
        var heard = new Recorder();
        await using var run = await Start(host: h => h with { Announcer = heard, ScreenIsSeen = false });
        TypeCommand(run.App, "/browse");

        TypeCommand(run.App, "/browse 1");

        await Assert.That(heard.Heard).Contains("Aetherfall answers at 3 addresses — choose one:");
        await Assert.That(heard.Heard).Contains("1. aetherfall.example:4202 — TLS — active");
        await Assert.That(heard.Heard).Contains("3. aetherfall.example:4000 — plain — stale · not reached lately");
        await Assert.That(heard.Heard).Contains("mu*index names its charset ISO-8859-1, for the whole game rather than per address.");

        TypeCommand(run.App, "/browse 1 1");
        await Assert.That(heard.Heard).Contains("To add it and connect, type: /connect aetherfall.example 4202 tls");

        TypeCommand(run.App, "/browse 1 2");
        await Assert.That(heard.Heard).Contains("To add it and connect, type: /connect aetherfall.example 4201");
        await Assert.That(heard.Heard).Contains(line => line.Contains("set the world's encoding to ISO-8859-1", StringComparison.Ordinal));

        TypeCommand(run.App, "/browse 1 7");
        await Assert.That(run.App.LastNotice).Contains("Aetherfall has 3 addresses");
        await Assert.That(run.Config.Worlds.Count).IsEqualTo(0);
    }
}
