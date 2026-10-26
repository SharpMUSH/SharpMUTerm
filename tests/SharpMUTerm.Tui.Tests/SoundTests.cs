using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Media;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>Game sound in the app: MCMP to the queue, the device opened on demand, captions, mute and F7.</summary>
[NotInParallel]
public class SoundTests
{
    private const string Ann = "Convergence.Ann";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class Rig : IAsyncDisposable
    {
        public required SharpMUTermApp App { get; init; }

        public required RecordingTelnetSession Wire { get; init; }

        public required RecordingAudio Audio { get; init; }

        public required string Folder { get; init; }

        public int Opened { get; set; }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            Directory.Delete(Folder, recursive: true);
        }
    }

    private static async Task<Rig> Open(bool device = true, Action<AppConfiguration>? configure = null, ManualTimeProvider? clock = null)
    {
        Console.SetIn(TextReader.Null);
        var folder = Directory.CreateTempSubdirectory("sharpmuterm-sound-").FullName;
        File.WriteAllBytes(Path.Combine(folder, "bell.wav"), [0x52, 0x49, 0x46, 0x46]);
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Convergence",
            Host = "convergence.example",
            Port = 4201,
            Characters = { new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() } },
        });
        configure?.Invoke(config);

        var audio = new RecordingAudio();
        var media = new MediaCache();
        media.LocalFolders.Add(folder);
        Rig? rig = null;
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(160, 32), null, null, null) with
        {
            Audio = device ? () => { rig!.Opened++; return audio; } : null,
        };
        var wire = new RecordingTelnetSession();
        var app = new SharpMUTermApp(config, host, time: clock, media: media);
        rig = new Rig { App = app, Wire = wire, Audio = audio, Folder = folder };
        app.TelnetFactory = _ => wire;
        if (!app.DispatchCommand(CommandIds.Character(Ann)))
        {
            throw new InvalidOperationException("the app would not switch to Ann");
        }

        await app.FindSession(Ann)!.ConnectAsync();
        return rig;
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    private const string Bell = """{"name":"bell.wav","type":"sound","caption":"A bell rings"}""";

    [Test]
    public async Task APlayReachesTheDeviceAndTheDeviceOpensOnlyThen()
    {
        await using var rig = await Open();
        await Assert.That(rig.Opened).IsEqualTo(0);

        rig.Wire.ReceiveGmcp("Client.Media.Play", Bell);
        await Until(() => rig.Audio.Played.Count > 0);

        await Assert.That(rig.Opened).IsEqualTo(1);
        await Assert.That(rig.Audio.Played.Select(p => Path.GetFileName(p)!)).IsEquivalentTo(new[] { "bell.wav" });
    }

    [Test]
    public async Task ASessionAdvertisesMediaOnlyWhereItCanPlayIt()
    {
        await using (var playing = await Open())
        {
            await Assert.That(playing.App.FindSession(Ann)!.PlaysMedia).IsTrue();
        }

        await using (var silent = await Open(device: false))
        {
            await Assert.That(silent.App.FindSession(Ann)!.PlaysMedia).IsFalse();
        }

        await using (var off = await Open(configure: c => c.Sound.Enabled = false))
        {
            await Assert.That(off.App.FindSession(Ann)!.PlaysMedia).IsFalse();
        }
    }

    [Test]
    public async Task ACaptionIsALineInTheCharactersWindow()
    {
        await using var rig = await Open(configure: c => c.Sound.Captions = true);

        rig.Wire.ReceiveGmcp("Client.Media.Play", Bell);
        await Until(() => rig.App.PaneLines(SharpMUTermApp.MainWindowId).Any(l => l.Contains("A bell rings")));

        await Assert.That(rig.App.PaneLines(SharpMUTermApp.MainWindowId).Any(l => MarkupText.Plain(l).Contains("♪ A bell rings"))).IsTrue();
    }

    [Test]
    public async Task WithNoDeviceACaptionIsStillShown()
    {
        await using var rig = await Open(device: false, configure: c => c.Sound.Captions = true);

        rig.Wire.ReceiveGmcp("Client.Media.Play", Bell);

        await Assert.That(rig.App.PaneLines(SharpMUTermApp.MainWindowId).Any(l => MarkupText.Plain(l).Contains("♪ A bell rings"))).IsTrue();
    }

    [Test]
    public async Task MuteSilencesWhatIsPlayingAndSaysSoOnTheStatusRow()
    {
        var clock = new ManualTimeProvider();
        await using var rig = await Open(clock: clock);
        rig.Wire.ReceiveGmcp("Client.Media.Play", Bell);
        await Until(() => rig.Audio.Voices.Count > 0);
        await Assert.That(rig.App.BuildCatalog().Any(i => i.Id == "term:mute")).IsTrue();

        rig.App.DispatchCommand("term:mute");
        clock.Advance(SharpMUTermApp.NoticeDuration + TimeSpan.FromSeconds(1)); // the notice holds the row
        rig.App.RenderNextFrame();

        await Assert.That(rig.Audio.Voices[0].Volume).IsEqualTo(0f);
        await Assert.That(rig.App.BuildCatalog().Any(i => i.Id == "term:unmute")).IsTrue();
        await Assert.That(MarkupText.Plain(rig.App.StatusMarkup)).Contains(SharpMUTermApp.MutedCell);

        rig.App.DispatchCommand("term:unmute");
        clock.Advance(SharpMUTermApp.NoticeDuration + TimeSpan.FromSeconds(1));
        rig.App.RenderNextFrame();

        await Assert.That(rig.Audio.Voices[0].Volume).IsGreaterThan(0f);
        await Assert.That(MarkupText.Plain(rig.App.StatusMarkup)).DoesNotContain(SharpMUTermApp.MutedCell);
    }

    [Test]
    public async Task NoMuteIsOfferedWhereNothingCanPlay()
    {
        await using var rig = await Open(device: false);

        await Assert.That(rig.App.BuildCatalog().Any(i => i.Id is "term:mute" or "term:unmute")).IsFalse();
    }

    [Test]
    public async Task TheF7SoundRowsAreThere()
    {
        var sound = new SoundSettings();
        var labels = OptionsScreenRenderer.TextAnsiScreen(new TextSettings(), sound).Rows.Select(r => r.Label).ToList();

        await Assert.That(labels).Contains("game sound");
        await Assert.That(labels).Contains("master volume (%)");
        await Assert.That(labels).Contains("sound captions");
    }

    [Test]
    public async Task TheTestHarnessHasNoDevice()
    {
        var host = ClientHost.ForHarness(Headless, null, null, null, null);

        await Assert.That(host.Audio is null).IsTrue();
    }

    /// <summary>A device that records what it was asked to play.</summary>
    private sealed class RecordingAudio : IAudioOutput
    {
        public List<string> Played { get; } = new();

        public List<Voice> Voices { get; } = new();

        public IAudioVoice? Play(string path, AudioStart start)
        {
            Played.Add(path);
            var voice = new Voice(start.Volume);
            Voices.Add(voice);
            return voice;
        }

        public void Dispose()
        {
        }

        internal sealed class Voice(float volume) : IAudioVoice
        {
            public float Volume { get; private set; } = volume;

            public event EventHandler? Ended
            {
                add { }
                remove { }
            }

            public void SetVolume(float value) => Volume = value;

            public void FadeTo(float value, TimeSpan over) => Volume = value;

            public void Pause()
            {
            }

            public void Resume()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
