namespace SharpMUTerm.Core.Media;

/// <summary>
/// The sound card, as the media queue needs it: start a file, and control what was started. Implemented by a
/// host (a desktop process has one; a snapshot and a test have none, or a recording fake), so everything above
/// it — the rules, the cache, the captions — is plain Core and is tested without a device.
/// </summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>Starts <paramref name="path"/>, or returns null when it cannot be played (unreadable, unsupported).</summary>
    IAudioVoice? Play(string path, AudioStart start);
}

/// <summary>How a file starts. Volume is final, 0 to 1: the queue has already applied the player's own levels.</summary>
public sealed record AudioStart(float Volume, int Loops, TimeSpan Start, TimeSpan? Finish, TimeSpan FadeIn, TimeSpan FadeOut);

/// <summary>One file playing.</summary>
public interface IAudioVoice : IDisposable
{
    /// <summary>Sets the volume at once, 0 to 1.</summary>
    void SetVolume(float volume);

    /// <summary>Moves the volume to <paramref name="volume"/> over <paramref name="over"/>.</summary>
    void FadeTo(float volume, TimeSpan over);

    void Pause();

    void Resume();

    /// <summary>Raised once, when the file has played its loops or been stopped. May be raised on any thread.</summary>
    event EventHandler? Ended;
}
