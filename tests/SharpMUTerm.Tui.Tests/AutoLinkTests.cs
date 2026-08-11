using SharpConsoleUI.Drivers;
using SharpConsoleUI.Parsing;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The plain-text URLs a world prints, driven the whole distance: server bytes into a connected session,
/// through the detector, the formatter, the framework's markup parse, a real click on the painted pane,
/// and out the other side as a URL handed to the desktop — or not handed to it.
/// <para>
/// The defect these were written for is invisible in any single component. A game prints a URL; the
/// terminal emulator finds it and makes it clickable, but only across the terminal's own row, and an
/// output pane is narrower than that. So the URL that wrapped inside a pane was two fragments with a
/// divider between them, neither clickable, and nothing in this client had an opinion about it either
/// way. <see cref="AWrappedUrlIsOneLinkAcrossBothRows"/> is that case.
/// </para>
/// </summary>
/// <remarks>
/// Serialised with the other end-to-end suites: constructing the app and rendering a frame both touch
/// the process-global console streams.
/// </remarks>
[NotInParallel]
public class AutoLinkTests
{
    private const int Width = 160;
    private const int Height = 40;
    private const string MainWindow = "main";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    // ---- What becomes clickable -------------------------------------------------------------

    [Test]
    public async Task APlainUrlInServerOutputIsClickableAndOpensTheBrowser()
    {
        var world = await Connected();
        world.Receive("The board reads: https://example.org/news for details\n");

        await Assert.That(Click(world, "https://example.org/news")).IsTrue();

        await Assert.That(world.Opened).IsEquivalentTo(new[] { "https://example.org/news" });
        await Assert.That(world.Telnet.Lines).IsEmpty(); // a URL is never a command
    }

    /// <summary>
    /// The reported defect. The pane is narrower than the terminal, so a long URL wraps — and the
    /// framework splits the one link span across both rendered rows, hit-testing each. The claim is made
    /// on the <em>second</em> row: clicking the tail of a wrapped URL opens the whole URL, which is
    /// exactly what the emulator's own detection could not do.
    /// </summary>
    [Test]
    public async Task AWrappedUrlIsOneLinkAcrossBothRows()
    {
        const string url = "https://example.org/news/2026/the-long-winter-patch-notes?from=board#changes";

        // A narrow terminal, then split in two: the pane is a fraction of the row the emulator would have
        // done its own detection over, which is the whole situation being reproduced.
        var world = await Connected(width: 100);
        await Assert.That(world.App.DispatchCommand("layout:split-right")).IsTrue();
        world.App.RenderNextFrame();
        world.Receive($"patch notes: {url}\n");

        // Rendered rows, not buffered lines: this URL does not fit the pane, so the framework has split
        // the one span over the rows it painted.
        var rows = world.App.PaneRowLinks(MainWindow);
        var carrying = Enumerable.Range(0, rows.Count)
            .Where(row => rows[row].Any(link => url.Contains(link.Text, StringComparison.Ordinal)))
            .ToList();

        // It really did wrap in this pane — otherwise everything below proves nothing.
        await Assert.That(carrying.Count).IsGreaterThanOrEqualTo(2);

        // Every row it wraps onto carries the whole target, which is what the emulator's own detection
        // could not do: it sees one terminal row, and half a URL is not a URL.
        foreach (var row in carrying)
        {
            foreach (var link in rows[row])
            {
                await Assert.That(link.Url).IsEqualTo(LinkPayload.WebScheme + LinkUrl.Escape(url));
            }
        }

        // The click lands on the *last* row — the tail of the wrap, the half that was unreachable.
        var tail = carrying[^1];
        await Assert.That(world.App.SimulatePaneClick(MainWindow, rows[tail][0].StartCol, tail)).IsTrue();
        await Assert.That(world.Opened).IsEquivalentTo(new[] { url });
    }

    /// <summary>
    /// A capture is a copy of the line, so a URL routed to a spawn window is clickable there too. This is
    /// the path that would silently miss out: a spawn window is fed from the trigger's result rather than
    /// from the session's own output handler, and those were two different lines until this feature made
    /// them one.
    /// <para>
    /// Driven through the payload the pane really drew and then <c>OnLinkClicked</c>, rather than a mouse
    /// click: a spawn window opens in the background, so its pane has no hit-test geometry to click on.
    /// </para>
    /// </summary>
    [Test]
    public async Task AUrlRoutedToASpawnWindowIsClickableThere()
    {
        var world = await Connected(withChatCapture: true);
        world.Receive("[Chat] Rivane: see https://example.org/map\n");

        var spawn = world.App.WindowIds().Single(id => id.StartsWith("spawn:", StringComparison.Ordinal));
        var link = LinksIn(world, spawn).Single();

        await Assert.That(link.Text).IsEqualTo("https://example.org/map");

        world.App.OnLinkClicked(spawn, LinkUrl.Unescape(link.Url));

        await Assert.That(world.Opened).IsEquivalentTo(new[] { "https://example.org/map" });
    }

    [Test]
    public async Task WithDetectionOffAUrlIsPlainText()
    {
        var world = await Connected(detectLinks: false);
        world.Receive("The board reads: https://example.org/news for details\n");

        await Assert.That(LinksIn(world, MainWindow)).IsEmpty();
    }

    // ---- Where a click goes -----------------------------------------------------------------

    /// <summary>
    /// The web view's own anchors must keep navigating the web view. Routing every <c>mux:web:</c> to the
    /// desktop would make the built-in browser a page that ejects you on the first link — which is why
    /// the destination is decided by the surface the click arrived from and not by the payload.
    /// </summary>
    [Test]
    public async Task AClickInsideTheWebViewNavigatesTheWebViewRatherThanTheDesktop()
    {
        var world = await Connected();

        world.App.OnLinkClicked("web", LinkPayload.WebScheme + "https://example.org/page");

        await Assert.That(world.Opened).IsEmpty();
        await Assert.That(string.Join("\n", world.App.PaneLines(MainWindow)))
            .Contains("Opening https://example.org/page in the web view");
    }

    /// <summary>
    /// The security boundary, and the reason it is at the moment of opening rather than in the detector:
    /// the detector only ever produces http(s), but this same path carries what a <em>server</em> marked
    /// up. Handing any of these to the desktop's handler is letting the world choose which program runs.
    /// </summary>
    [Test]
    [Arguments("file:///etc/passwd")]
    [Arguments("javascript:alert(1)")]
    [Arguments("ms-msdt:/id")]
    [Arguments("mailto:someone@example.org")]
    [Arguments("mux:send:@shutdown")]
    [Arguments("not a url at all")]
    public async Task ALinkThatIsNotHttpOpensNothing(string target)
    {
        var world = await Connected();

        world.App.OnLinkClicked(MainWindow, LinkPayload.WebScheme + target);

        await Assert.That(world.Opened).IsEmpty();
        await Assert.That(world.App.StatusMarkup).Contains("not an http or https address");
    }

    /// <summary>
    /// An app given no opener — a snapshot, or any test that did not ask for one — launches nothing. The
    /// same shape as the save action and the log root, and for the same reason: a headless frame must not
    /// be able to start a browser on the machine rendering it.
    /// </summary>
    [Test]
    public async Task AnAppWithNoOpenerLaunchesNothingAndSaysSo()
    {
        var world = await Connected(withOpener: false);

        world.App.OnLinkClicked(MainWindow, LinkPayload.WebScheme + "https://example.org/page");

        await Assert.That(world.App.StatusMarkup).Contains("no way to open a browser");
    }

    /// <summary>The gate's own unit: what it accepts, and the canonical form it hands on.</summary>
    [Test]
    [Arguments("https://example.org/page", "https://example.org/page")]
    [Arguments("  http://example.org/x  ", "http://example.org/x")]
    [Arguments("HTTPS://Example.org/Page", "https://example.org/Page")]
    public async Task TheGateAcceptsHttpAndHandsOnWhatItParsed(string target, string expected)
    {
        await Assert.That(ExternalBrowser.TryParseOpenable(target, out var url)).IsTrue();
        await Assert.That(url).IsEqualTo(expected);
    }

    [Test]
    [Arguments("file:///etc/passwd")]
    [Arguments("javascript:alert(1)")]
    [Arguments("ms-msdt:/id")]
    [Arguments("mailto:someone@example.org")]
    [Arguments("//example.org/x")]
    [Arguments("example.org/x")]
    [Arguments("")]
    [Arguments("   ")]
    public async Task TheGateRefusesEverythingElse(string target) =>
        await Assert.That(ExternalBrowser.TryParseOpenable(target, out _)).IsFalse();

    // ---- Harness ---------------------------------------------------------------------------

    private sealed record Wired(SharpMUTermApp App, RecordingTelnetSession Telnet, List<string> Opened)
    {
        public void Receive(string text)
        {
            Telnet.Receive(text);
            App.RenderNextFrame();
        }
    }

    /// <summary>
    /// One connected world printing into the main window, with the launcher replaced by a list so a test
    /// can assert what would have been opened without a browser existing.
    /// </summary>
    private static async Task<Wired> Connected(
        bool detectLinks = true,
        bool withOpener = true,
        bool withChatCapture = false,
        int width = Width)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        config.Text.DetectLinks = detectLinks;

        var world = new WorldDefinition
        {
            Name = "Hostile",
            Host = "hostile.example.org",
            Port = 4000,
        };

        if (withChatCapture)
        {
            config.TriggerSets.Add(new TriggerSet
            {
                Name = "chat",
                Triggers =
                {
                    new Trigger
                    {
                        Name = "chat",
                        Pattern = @"^\[Chat\]",
                        Actions = new TriggerActions { SpawnTarget = "Chat" },
                    },
                },
            });
            world.Characters.Add(new CharacterDefinition
            {
                Name = "Ann",
                Logging = new LoggingSettings(),
                TriggerSets = { "chat" },
            });
        }

        config.Worlds.Add(world);

        var opened = new List<string>();
        var app = new SharpMUTermApp(
            config,
            Headless,
            new HeadlessConsoleDriver(width, Height),
            openUrl: withOpener ? opened.Add : null);

        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;

        // A capture rule belongs to a *character*, so that arm is opened the way ⌃P and the rail open one
        // rather than by binding the world bare — otherwise the trigger set is configured and never
        // attached, and the spawn window under test never opens.
        if (withChatCapture)
        {
            if (!app.DispatchCommand(CommandIds.Character("Hostile.Ann")))
            {
                throw new InvalidOperationException("the app would not switch to Hostile.Ann");
            }

            await app.FindSession("Hostile.Ann")!.ConnectAsync();
        }
        else
        {
            await app.BindWorldWithoutConnecting(config.Worlds[0]).ConnectAsync();
        }

        app.RenderNextFrame();
        return new Wired(app, telnet, opened);
    }

    private static bool Click(Wired world, string text) => ClickIn(world, MainWindow, text);

    private static bool ClickIn(Wired world, string windowId, string text)
    {
        var lines = world.App.PaneLines(windowId);
        for (var row = 0; row < lines.Count; row++)
        {
            foreach (var link in LinksOn(lines[row]))
            {
                if (link.Text.Contains(text, StringComparison.Ordinal))
                {
                    return world.App.SimulatePaneClick(windowId, link.StartCol, row);
                }
            }
        }

        throw new InvalidOperationException($"no link reading '{text}' in: {string.Join(" / ", lines)}");
    }

    private static List<LinkSpan> LinksIn(Wired world, string windowId) =>
        world.App.PaneLines(windowId).SelectMany(LinksOn).ToList();

    /// <summary>
    /// The links on one buffered line, read with the framework's own parser at the pane's width — so a
    /// line that wraps comes back as the rows the control really paints, which is the whole subject here.
    /// </summary>
    private static List<LinkSpan> LinksOn(string markup)
    {
        MarkupParser.Parse(markup, SharpConsoleUI.Color.White, SharpConsoleUI.Color.Black, out var links);
        return links;
    }
}
