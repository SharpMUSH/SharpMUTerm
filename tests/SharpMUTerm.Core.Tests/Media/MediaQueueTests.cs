using SharpMUTerm.Core.Media;

namespace SharpMUTerm.Core.Tests.Media;

public class MediaQueueTests
{
    /// <summary>A queue whose cache resolves every name from a soundpack folder that holds it.</summary>
    private static (MediaQueue Queue, FakeAudio Audio, ManualTimeProvider Clock, string Folder) Queue()
    {
        var folder = Directory.CreateTempSubdirectory("smt-media-").FullName;
        foreach (var name in new[] { "rain.ogg", "thunder.wav", "theme.mp3", "battle.mp3", "bell.wav", "door.wav" })
        {
            File.WriteAllBytes(Path.Combine(folder, name), [1]);
        }

        var cache = new MediaCache();
        cache.LocalFolders.Add(folder);
        var audio = new FakeAudio();
        var clock = new ManualTimeProvider();
        return (new MediaQueue(audio, cache, "w", clock), audio, clock, folder);
    }

    [Test]
    public async Task VolumeIsTheProductOfThePlayersLevelsAndTheRequests()
    {
        var (queue, audio, _, _) = Queue();
        queue.Mix = new MediaMix(Master: 50, Sound: 80);

        queue.Play(new MediaRequest("rain.ogg") { Volume = 50 });

        await Assert.That(audio.Voices.Single().Start.Volume).IsEqualTo(0.5f * 0.8f * 0.5f).Within(0.0001f);

        queue.Mix = new MediaMix(Muted: true);
        await Assert.That(audio.Voices.Single().Volume).IsEqualTo(0f);
    }

    [Test]
    public async Task HigherPriorityHaltsLowerAndRefusesItWhilePlaying()
    {
        var (queue, audio, _, _) = Queue();
        queue.Play(new MediaRequest("rain.ogg") { Priority = 10 });
        queue.Play(new MediaRequest("thunder.wav") { Priority = 60 });

        await Assert.That(audio.Playing).IsEquivalentTo(new[] { "thunder.wav" });

        queue.Play(new MediaRequest("bell.wav") { Priority = 20 });
        queue.Play(new MediaRequest("door.wav"));
        await Assert.That(audio.Playing).IsEquivalentTo(new[] { "thunder.wav" });

        queue.Play(new MediaRequest("rain.ogg") { Priority = 60 }); // a tie plays alongside
        await Assert.That(audio.Playing).IsEquivalentTo(new[] { "thunder.wav", "rain.ogg" });
    }

    [Test]
    public async Task AnExclusiveSoundReplacesOnlyAtAHigherPriority()
    {
        var (queue, audio, _, _) = Queue();
        queue.Play(new MediaRequest("rain.ogg") { Exclusive = true, Priority = 50 });
        queue.Play(new MediaRequest("bell.wav") { Exclusive = true, Priority = 50 });
        await Assert.That(audio.Playing).IsEquivalentTo(new[] { "rain.ogg" });

        queue.Play(new MediaRequest("thunder.wav") { Exclusive = true, Priority = 51 });
        await Assert.That(audio.Playing).IsEquivalentTo(new[] { "thunder.wav" });
    }

    [Test]
    public async Task ADifferentFileUnderTheSameKeyReplacesTheOneBefore()
    {
        var (queue, audio, _, _) = Queue();
        queue.Play(new MediaRequest("theme.mp3") { Kind = MediaKind.Music, Key = "bgm" });
        queue.Play(new MediaRequest("rain.ogg"));
        queue.Play(new MediaRequest("battle.mp3") { Kind = MediaKind.Music, Key = "bgm" });

        await Assert.That(audio.Playing).IsEquivalentTo(new[] { "rain.ogg", "battle.mp3" });
    }

    [Test]
    public async Task MusicContinuesUnlessToldToRestart()
    {
        var (queue, audio, _, _) = Queue();
        var theme = new MediaRequest("theme.mp3") { Kind = MediaKind.Music };
        queue.Play(theme);
        queue.Play(theme);
        await Assert.That(audio.Voices.Count).IsEqualTo(1);

        queue.Play(theme with { Continue = false });
        await Assert.That(audio.Voices.Count).IsEqualTo(2);
        await Assert.That(audio.Voices[0].Stopped).IsTrue();
        await Assert.That(audio.Playing).IsEquivalentTo(new[] { "theme.mp3" });
    }

    [Test]
    public async Task StopMatchesEverySetFieldAndAnEmptyStopEndsAll()
    {
        var (queue, audio, _, _) = Queue();
        queue.Play(new MediaRequest("rain.ogg") { Tag = "weather" });
        queue.Play(new MediaRequest("thunder.wav") { Tag = "weather" });
        queue.Play(new MediaRequest("theme.mp3") { Kind = MediaKind.Music, Tag = "weather" });

        queue.Stop(new MediaFilter { Tag = "weather", Kind = MediaKind.Sound });
        await Assert.That(audio.Playing).IsEquivalentTo(new[] { "theme.mp3" });

        queue.Stop(MediaFilter.All);
        await Assert.That(audio.Playing).IsEmpty();
        await Assert.That(queue.Current).IsEmpty();
    }

    [Test]
    public async Task AFadeAwayFadesThenStopsOnTheClock()
    {
        var (queue, audio, clock, _) = Queue();
        queue.Play(new MediaRequest("rain.ogg"));

        queue.Stop(MediaFilter.All, fadeAway: true, fadeOut: TimeSpan.FromSeconds(2));
        await Assert.That(audio.Voices[0].Fade).IsEqualTo((0f, TimeSpan.FromSeconds(2)));
        await Assert.That(audio.Voices[0].Stopped).IsFalse();

        clock.Advance(TimeSpan.FromSeconds(2));
        await Assert.That(audio.Voices[0].Stopped).IsTrue();
    }

    [Test]
    public async Task APausedFileIsResumedByAMatchingPlay()
    {
        var (queue, audio, _, _) = Queue();
        queue.Play(new MediaRequest("theme.mp3") { Kind = MediaKind.Music, Continue = false });
        queue.Apply(new MediaCommand.Pause(new MediaFilter { Kind = MediaKind.Music }));
        await Assert.That(audio.Voices[0].Paused).IsTrue();

        queue.Play(new MediaRequest("theme.mp3") { Kind = MediaKind.Music, Continue = false });
        await Assert.That(audio.Voices.Count).IsEqualTo(1);
        await Assert.That(audio.Voices[0].Paused).IsFalse();
    }

    [Test]
    public async Task AFileThatEndsLeavesTheQueue()
    {
        var (queue, audio, _, _) = Queue();
        MediaRequest? started = null;
        queue.Started += (_, r) => started = r;
        queue.Play(new MediaRequest("rain.ogg") { Caption = "rain on the roof" });

        audio.Voices[0].Finish();

        await Assert.That(started!.CaptionText).IsEqualTo("rain on the roof");
        await Assert.That(queue.Current).IsEmpty();
    }

    [Test]
    public async Task AStopWhileFetchingMeansItNeverStarts()
    {
        var gate = new TaskCompletionSource<byte[]?>();
        var root = Directory.CreateTempSubdirectory("smt-cache-").FullName;
        var cache = new MediaCache(root, (_, _, _) => gate.Task);
        var audio = new FakeAudio();
        var queue = new MediaQueue(audio, cache, "w");
        queue.Apply(new MediaCommand.SetDefault("https://game.example/sounds/"));

        queue.Play(new MediaRequest("rain.ogg"));
        queue.Stop(MediaFilter.All);
        gate.SetResult([1, 2, 3]);
        await Task.Delay(50);

        await Assert.That(audio.Voices).IsEmpty();
    }

    [Test]
    public async Task ACaptionDefaultsToTheFileName()
    {
        await Assert.That(new MediaRequest("weather/thunder-near.wav").CaptionText).IsEqualTo("thunder-near");
    }
}
