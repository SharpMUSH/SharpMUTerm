using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SharpMUTerm.Core.Media;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Enums;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace SharpMUTerm.Audio;

/// <summary>
/// The sound card, through SoundFlow (miniaudio). Every file is decoded to one device format — 48 kHz stereo
/// float — because a SoundFlow player does not resample: a 22 kHz file played on a 48 kHz device plays at
/// twice the speed. miniaudio's own decoder converts WAV, MP3 and FLAC as it reads; OGG Vorbis is decoded by
/// NVorbis into a WAV the same decoder then converts (<see cref="OggToWav"/>).
/// <para>
/// <b>Opening a device can write to the terminal.</b> The native libraries print diagnostics on stderr — ALSA
/// on a machine with no card, JACK when it is tried and not running — and in a terminal client stderr is the
/// screen. So the backends are named per platform (JACK never), and on Unix stderr is pointed at
/// <c>/dev/null</c> for the moment the device opens.
/// </para>
/// </summary>
public sealed class DesktopAudio : IAudioOutput
{
    internal static readonly AudioFormat DeviceFormat = new()
    {
        SampleRate = 48000,
        Channels = 2,
        Format = SampleFormat.F32,
        Layout = AudioFormat.GetLayoutFromChannels(2),
    };

    private readonly MiniAudioEngine _engine;
    private readonly AudioPlaybackDevice _device;
    private readonly ILogger? _logger;
    private readonly List<Voice> _voices = new();
    private readonly object _gate = new();

    private DesktopAudio(MiniAudioEngine engine, AudioPlaybackDevice device, ILogger? logger)
    {
        _engine = engine;
        _device = device;
        _logger = logger;
    }

    /// <summary>Opens the default playback device, or returns null — no device, no backend, no native library.</summary>
    public static IAudioOutput? TryCreate(ILogger? logger = null)
    {
        MiniAudioEngine? engine = null;
        try
        {
            using (QuietStderr.Begin())
            {
                engine = new MiniAudioEngine(Backends());
                var device = engine.InitializePlaybackDevice(null, DeviceFormat, null);
                device.Start();
                logger?.LogInformation("Sound: playing through {Backend}", engine.ActiveBackend);
                return new DesktopAudio(engine, device, logger);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException or TypeInitializationException
                                       or BadImageFormatException or EntryPointNotFoundException or SoundFlow.Exceptions.BackendException)
        {
            engine?.Dispose();
            logger?.LogInformation("Sound: no audio device ({Reason})", ex.Message);
            return null;
        }
    }

    private static MiniAudioBackend[] Backends() =>
        OperatingSystem.IsWindows() ? [MiniAudioBackend.Wasapi, MiniAudioBackend.DirectSound, MiniAudioBackend.WinMm]
        : OperatingSystem.IsMacOS() ? [MiniAudioBackend.CoreAudio]
        : [MiniAudioBackend.PulseAudio, MiniAudioBackend.Alsa];

    public IAudioVoice? Play(string path, AudioStart start)
    {
        Stream? stream = null;
        try
        {
            stream = Path.GetExtension(path).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
                ? OggToWav(path)
                : File.OpenRead(path);
            var provider = new StreamDataProvider(_engine, DeviceFormat, stream);
            var player = new SoundPlayer(_engine, DeviceFormat, provider);
            var voice = new Voice(this, player, provider, stream, start);
            lock (_gate)
            {
                _voices.Add(voice);
            }

            _device.MasterMixer.AddComponent(player);
            voice.Begin();
            return voice;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or InvalidDataException or ArgumentException or NotSupportedException
                                       or SoundFlow.Exceptions.BackendException)
        {
            stream?.Dispose();
            _logger?.LogInformation("Sound: could not play {File} ({Reason})", Path.GetFileName(path), ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        Voice[] voices;
        lock (_gate)
        {
            voices = _voices.ToArray();
        }

        foreach (var voice in voices)
        {
            voice.Dispose();
        }

        _device.Stop();
        _device.Dispose();
        _engine.Dispose();
    }

    private void Remove(Voice voice)
    {
        lock (_gate)
        {
            _voices.Remove(voice);
        }

        _device.MasterMixer.RemoveComponent(voice.Player);
    }

    /// <summary>
    /// An OGG Vorbis file as a 16-bit PCM WAV in memory, at the file's own rate and channels, for miniaudio to
    /// convert. Bounded by the cache's per-file cap on the compressed file, and by <paramref name="maxSeconds"/>.
    /// </summary>
    internal static MemoryStream OggToWav(string path, int maxSeconds = 20 * 60)
    {
        using var reader = new NVorbis.VorbisReader(path);
        var channels = reader.Channels;
        var rate = reader.SampleRate;
        if (channels is < 1 or > 8 || rate is < 4000 or > 192000)
        {
            throw new InvalidDataException("not a playable Vorbis stream");
        }

        var limit = (long)rate * channels * maxSeconds;
        var pcm = new MemoryStream();
        var writer = new BinaryWriter(pcm);
        WriteWavHeader(writer, channels, rate, 0);
        var buffer = new float[rate * channels / 4];
        long written = 0;
        int read;
        while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0 && written < limit)
        {
            for (var i = 0; i < read; i++)
            {
                writer.Write((short)Math.Clamp((int)(buffer[i] * 32767f), short.MinValue, short.MaxValue));
            }

            written += read;
        }

        writer.Flush();
        pcm.Position = 0;
        WriteWavHeader(writer, channels, rate, (int)(written * 2));
        writer.Flush();
        pcm.Position = 0;
        return pcm;
    }

    private static void WriteWavHeader(BinaryWriter writer, int channels, int rate, int dataBytes)
    {
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(rate);
        writer.Write(rate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
    }

    /// <summary>
    /// One file playing. Its volume is the queue's level times an envelope — the fade in at the start, the fade
    /// out before the end of the last loop, and any fade the queue asked for — recomputed on a 40 ms tick, which
    /// also ends the file at its finish time and counts its loops.
    /// <para>
    /// Loops are the player's own (<c>IsLooping</c>), counted on the tick by the play position going backwards,
    /// and switched off for the last one so it ends by itself. Restarting from <c>PlaybackEnded</c> instead does
    /// not work: that event is raised on the audio thread, and a seek and play from inside it never plays.
    /// </para>
    /// </summary>
    private sealed class Voice : IAudioVoice
    {
        private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(40);

        private readonly DesktopAudio _owner;
        private readonly StreamDataProvider _provider;
        private readonly Stream _stream;
        private readonly AudioStart _start;
        private readonly Timer _timer;
        private readonly object _gate = new();
        private float _level;
        private (float From, float To, DateTime At, TimeSpan Over)? _fade;
        private DateTime _loopStarted;
        private int _loopsLeft;
        private float _lastTime;
        private bool _paused;
        private bool _ended;

        public Voice(DesktopAudio owner, SoundPlayer player, StreamDataProvider provider, Stream stream, AudioStart start)
        {
            _owner = owner;
            Player = player;
            _provider = provider;
            _stream = stream;
            _start = start;
            _level = start.Volume;
            _loopsLeft = start.Loops;
            _timer = new Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
            player.PlaybackEnded += (_, _) => OnLoopEnded();
        }

        public SoundPlayer Player { get; }

        public event EventHandler? Ended;

        public void Begin()
        {
            lock (_gate)
            {
                Player.IsLooping = _loopsLeft != 1;
                if (_start.Start > TimeSpan.Zero)
                {
                    Player.Seek(_start.Start, SeekOrigin.Begin);
                }

                _loopStarted = DateTime.UtcNow;
                Apply();
                Player.Play();
                _timer.Change(Tick, Tick);
            }
        }

        public void SetVolume(float volume)
        {
            lock (_gate)
            {
                _level = Math.Clamp(volume, 0f, 1f);
                _fade = null;
                Apply();
            }
        }

        public void FadeTo(float volume, TimeSpan over)
        {
            lock (_gate)
            {
                _fade = (_level, Math.Clamp(volume, 0f, 1f), DateTime.UtcNow, over);
            }
        }

        public void Pause()
        {
            lock (_gate)
            {
                _paused = true;
                Player.Pause();
            }
        }

        public void Resume()
        {
            lock (_gate)
            {
                _paused = false;
                Player.Play();
            }
        }

        public void Dispose() => End(raise: true);

        /// <summary>A loop came round: count it, and let the last one end by itself.</summary>
        private void OnWrapped()
        {
            _loopStarted = DateTime.UtcNow;
            if (_loopsLeft > 1 && --_loopsLeft == 1)
            {
                Player.IsLooping = false;
            }
        }

        private void OnLoopEnded()
        {
            lock (_gate)
            {
                if (_ended)
                {
                    return;
                }
            }

            End(raise: true);
        }

        private void OnTick()
        {
            var finished = false;
            lock (_gate)
            {
                if (_ended || _paused)
                {
                    return;
                }

                var time = Player.Time;
                if (time + 0.05f < _lastTime)
                {
                    OnWrapped();
                }

                _lastTime = time;
                if (_start.Finish is { } finish && time >= finish.TotalSeconds)
                {
                    // A finish point ends the loop there: the next loop starts again from the start point.
                    if (_loopsLeft == -1 || _loopsLeft > 1)
                    {
                        Player.Seek(_start.Start > TimeSpan.Zero ? _start.Start : TimeSpan.Zero, SeekOrigin.Begin);
                        _lastTime = 0;
                        OnWrapped();
                    }
                    else
                    {
                        finished = true;
                    }
                }
                else
                {
                    Apply();
                }
            }

            if (finished)
            {
                End(raise: true);
            }
        }

        /// <summary>The level times the envelope, written to the player. Called under the lock.</summary>
        private void Apply()
        {
            var now = DateTime.UtcNow;
            if (_fade is { } fade)
            {
                var t = fade.Over <= TimeSpan.Zero ? 1f : (float)Math.Clamp((now - fade.At) / fade.Over, 0, 1);
                _level = fade.From + (fade.To - fade.From) * t;
                if (t >= 1f)
                {
                    _fade = null;
                }
            }

            var envelope = 1f;
            if (_start.FadeIn > TimeSpan.Zero)
            {
                envelope = Math.Min(envelope, (float)Math.Clamp((now - _loopStarted) / _start.FadeIn, 0, 1));
            }

            var lastLoop = _loopsLeft is 1;
            if (lastLoop && _start.FadeOut > TimeSpan.Zero)
            {
                var end = _start.Finish?.TotalSeconds ?? Player.Duration;
                var left = end - Player.Time;
                envelope = Math.Min(envelope, (float)Math.Clamp(left / _start.FadeOut.TotalSeconds, 0, 1));
            }

            Player.Volume = _level * envelope;
        }

        private void End(bool raise)
        {
            lock (_gate)
            {
                if (_ended)
                {
                    return;
                }

                _ended = true;
            }

            _timer.Dispose();
            try
            {
                Player.Stop();
            }
            catch (InvalidOperationException)
            {
            }

            _owner.Remove(this);
            Player.Dispose();
            _provider.Dispose();
            _stream.Dispose();
            if (raise)
            {
                Ended?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Points stderr at /dev/null while a native library is being opened, on Unix; does nothing elsewhere.</summary>
    private static class QuietStderr
    {
        public static IDisposable Begin()
        {
            if (OperatingSystem.IsWindows())
            {
                return new Restore(-1);
            }

            try
            {
                var saved = Dup(2);
                var devNull = Open("/dev/null", 1);
                if (saved < 0 || devNull < 0)
                {
                    return new Restore(-1);
                }

                Dup2(devNull, 2);
                Close(devNull);
                return new Restore(saved);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return new Restore(-1);
            }
        }

        private sealed class Restore(int saved) : IDisposable
        {
            public void Dispose()
            {
                if (saved >= 0)
                {
                    Dup2(saved, 2);
                    Close(saved);
                }
            }
        }

        [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
        private static extern int Dup(int fd);

        [DllImport("libc", EntryPoint = "dup2", SetLastError = true)]
        private static extern int Dup2(int fd, int fd2);

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int Open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

        [DllImport("libc", EntryPoint = "close", SetLastError = true)]
        private static extern int Close(int fd);
    }
}
