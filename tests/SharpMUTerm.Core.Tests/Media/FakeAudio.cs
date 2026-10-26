using SharpMUTerm.Core.Media;

namespace SharpMUTerm.Core.Tests.Media;

/// <summary>An audio device that records what it was asked to do.</summary>
internal sealed class FakeAudio : IAudioOutput
{
    public List<FakeVoice> Voices { get; } = new();

    public IEnumerable<string> Playing => Voices.Where(v => !v.Stopped).Select(v => Path.GetFileName(v.Path));

    public IAudioVoice? Play(string path, AudioStart start)
    {
        var voice = new FakeVoice(path, start);
        Voices.Add(voice);
        return voice;
    }

    public void Dispose()
    {
    }
}

internal sealed class FakeVoice(string path, AudioStart start) : IAudioVoice
{
    public string Path { get; } = path;

    public AudioStart Start { get; } = start;

    public float Volume { get; private set; } = start.Volume;

    public (float To, TimeSpan Over)? Fade { get; private set; }

    public bool Paused { get; private set; }

    public bool Stopped { get; private set; }

    public event EventHandler? Ended;

    public void SetVolume(float volume) => Volume = volume;

    public void FadeTo(float volume, TimeSpan over) => Fade = (volume, over);

    public void Pause() => Paused = true;

    public void Resume() => Paused = false;

    /// <summary>The file played to its end.</summary>
    public void Finish()
    {
        Stopped = true;
        Ended?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => Stopped = true;
}
