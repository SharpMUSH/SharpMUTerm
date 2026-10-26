using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Accessibility;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>What a screen reader is told: output as it arrives, the client's messages, the window in front.</summary>
[NotInParallel]
public class AnnounceTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class Recorder : IAnnouncer
    {
        public List<(string Text, AnnouncementKind Kind)> Heard { get; } = [];

        public void Announce(string text, AnnouncementKind kind)
        {
            lock (Heard)
            {
                Heard.Add((text, kind));
            }
        }
    }

    private static async Task<(SharpMUTermApp App, Recorder Heard, RecordingTelnetSession Wire)> Open(IAnnouncer? announcer)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Mush", Host = "mush.example", Port = 4201 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new() });
        config.Worlds.Add(world);
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null) with { Announcer = announcer };
        var app = new SharpMUTermApp(config, host);
        var wire = new RecordingTelnetSession();
        app.TelnetFactory = _ => wire;
        app.DispatchCommand(CommandIds.Character("Mush.Ann"));
        await app.FindSession("Mush.Ann")!.ConnectAsync();
        app.RenderNextFrame();
        return (app, announcer as Recorder ?? new Recorder(), wire);
    }

    [Test]
    public async Task OutputIsAnnouncedAsPlainTextInArrivalOrder()
    {
        var (app, heard, wire) = await Open(new Recorder());
        await using var _ = app;

        wire.Receive("\x1b[1;31mA red\x1b[0m line.");
        wire.Receive("And another.");
        app.RenderNextFrame();

        var output = heard.Heard.Where(h => h.Kind == AnnouncementKind.Output).Select(h => h.Text).ToList();
        await Assert.That(output.IndexOf("A red line.")).IsGreaterThanOrEqualTo(0);
        await Assert.That(output.IndexOf("And another.")).IsGreaterThan(output.IndexOf("A red line."));
        await Assert.That(output.Any(t => t.Contains('\x1b'))).IsFalse();
    }

    [Test]
    public async Task AQuietWindowIsNotReadButTheClientsMessagesStillAre()
    {
        var (app, heard, wire) = await Open(new Recorder());
        await using var _ = app;
        var titles = app.BuildCatalog().Select(i => i.Title).ToArray();
        await Assert.That(titles).Contains("Stop reading this window aloud");

        app.DispatchCommand("a11y:toggle-window");
        wire.Receive("Unheard.");
        app.RenderNextFrame();

        await Assert.That(heard.Heard.Any(h => h.Text == "Unheard.")).IsFalse();
        await Assert.That(heard.Heard[^1].Kind).IsEqualTo(AnnouncementKind.Notice);
        await Assert.That(app.BuildCatalog().Select(i => i.Title)).Contains("Read this window aloud");
    }

    [Test]
    public async Task AHostWithNoAnnouncerOffersNothingToToggle()
    {
        var (app, _, _) = await Open(null);
        await using var _app = app;

        await Assert.That(app.BuildCatalog().Any(i => i.Id == "a11y:toggle-window")).IsFalse();
    }

    [Test]
    public async Task ThePlainHostMarksTheClientsOwnLines()
    {
        var output = new StringWriter();
        var announcer = new PlainAnnouncer(output);

        announcer.Announce("You see a troll.", AnnouncementKind.Output);
        announcer.Announce("nothing was sent", AnnouncementKind.Notice);
        announcer.Announce("Chat", AnnouncementKind.Focus);

        await Assert.That(output.ToString().ReplaceLineEndings("\n"))
            .IsEqualTo("You see a troll.\n[client] nothing was sent\n[window] Chat\n");
    }
}
