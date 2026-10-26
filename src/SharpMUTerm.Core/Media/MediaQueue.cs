namespace SharpMUTerm.Core.Media;

/// <summary>The player's own levels, 0 to 100 each; the effective volume of a file is their product with its own.</summary>
public sealed record MediaMix(int Master = 100, int Sound = 100, int Music = 100, bool Muted = false)
{
    public float For(MediaRequest request)
    {
        if (Muted)
        {
            return 0f;
        }

        var kind = request.Kind == MediaKind.Music ? Music : Sound;
        return Math.Clamp(Master, 0, 100) / 100f * Math.Clamp(kind, 0, 100) / 100f * Math.Clamp(request.Volume, 1, 100) / 100f;
    }
}

/// <summary>
/// One connection's sounds and music: applies MCMP's rules to what the server asks for and drives an
/// <see cref="IAudioOutput"/>.
/// <list type="bullet">
/// <item><b>Priority</b> — while media with a priority plays, anything of lower priority is halted and refused;
/// equal priorities play together. An <see cref="MediaRequest.Exclusive"/> sound (MSP's) replaces the sound
/// playing only at a higher priority, so a tie keeps what is playing.</item>
/// <item><b>Key</b> — a different file under the same key replaces the one before.</item>
/// <item><b>Continue</b> — music already playing is left alone when asked for again, unless continue is false.</item>
/// <item><b>Pause</b> — a later play that matches paused media resumes it rather than starting it again.</item>
/// </list>
/// Every method is called on one thread (the client's UI thread); a fetch completes through <see cref="Post"/>.
/// </summary>
public sealed class MediaQueue : IDisposable
{
    private readonly IAudioOutput _output;
    private readonly MediaCache _cache;
    private readonly string _world;
    private readonly TimeProvider _time;
    private readonly List<Playing> _playing = new();

    /// <summary>Requests waiting on a fetch, by number; a stop that matches one takes it out, so it never starts.</summary>
    private readonly Dictionary<long, MediaRequest> _pending = new();
    private readonly CancellationTokenSource _closing = new();
    private string? _defaultUrl;
    private long _generation;

    public MediaQueue(IAudioOutput output, MediaCache cache, string world, TimeProvider? time = null)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _world = world;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The player's levels. Setting it re-levels everything playing.</summary>
    public MediaMix Mix
    {
        get => _mix;
        set
        {
            _mix = value ?? new MediaMix();
            foreach (var playing in _playing)
            {
                playing.Voice.SetVolume(_mix.For(playing.Request));
            }
        }
    }

    private MediaMix _mix = new();

    /// <summary>Whether files may be fetched for this world.</summary>
    public bool Download { get; set; } = true;

    /// <summary>Where a fetch's completion is run; the client sets this to its UI thread. Null runs it in place.</summary>
    public Action<Action>? Post { get; init; }

    /// <summary>Raised when something starts playing, with the request, so its caption can be shown.</summary>
    public event EventHandler<MediaRequest>? Started;

    /// <summary>Raised when a request cannot be played, with why.</summary>
    public event EventHandler<(MediaRequest Request, string Problem)>? Failed;

    /// <summary>What is playing or paused now, in the order it started.</summary>
    public IReadOnlyList<MediaRequest> Current => _playing.Select(p => p.Request).ToArray();

    public void Apply(MediaCommand command)
    {
        switch (command)
        {
            case MediaCommand.SetDefault d:
                _defaultUrl = d.Url;
                break;
            case MediaCommand.Load l:
                _ = _cache.ResolveAsync(_world, l.Name, l.Url ?? _defaultUrl, Download, _closing.Token);
                break;
            case MediaCommand.Play p:
                Play(p.Request);
                break;
            case MediaCommand.Stop s:
                Stop(s.Filter, s.FadeAway, s.FadeOut);
                break;
            case MediaCommand.Pause pause:
                foreach (var playing in _playing.Where(x => !x.Paused && pause.Filter.Matches(x.Request)))
                {
                    playing.Voice.Pause();
                    playing.Paused = true;
                }

                break;
        }
    }

    public void Play(MediaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A paused match is resumed, not started again (Mudlet's pause extension).
        if (_playing.FirstOrDefault(p => p.Paused && p.Request.Name == request.Name && p.Request.Kind == request.Kind) is { } paused)
        {
            paused.Paused = false;
            paused.Voice.Resume();
            return;
        }

        if (request.Kind == MediaKind.Music && request.Continue
            && _playing.Any(p => p.Request.Kind == MediaKind.Music && p.Request.Name == request.Name))
        {
            return;
        }

        if (_playing.Any(p => p.Request.Priority is { } held && held > (request.Priority ?? 0)))
        {
            return; // something more important is playing
        }

        if (request.Exclusive
            && _playing.Any(p => p.Request.Exclusive && (p.Request.Priority ?? 0) >= (request.Priority ?? 0)))
        {
            return; // MSP: a tie keeps what is playing
        }

        var generation = ++_generation;
        _pending[generation] = request;
        var resolving = _cache.ResolveAsync(_world, request.Name, request.Url ?? _defaultUrl, Download, _closing.Token);
        if (resolving.IsCompleted)
        {
            Begin(request, resolving.Result, generation);
            return;
        }

        _ = Finish(request, resolving, generation);
    }

    /// <summary>Stops what matches <paramref name="filter"/>, fading it out first when asked.</summary>
    public void Stop(MediaFilter filter, bool fadeAway = false, TimeSpan? fadeOut = null)
    {
        foreach (var (id, waiting) in _pending.ToArray())
        {
            if (filter.Matches(waiting))
            {
                _pending.Remove(id);
            }
        }

        foreach (var playing in _playing.Where(p => filter.Matches(p.Request)).ToArray())
        {
            var fade = fadeOut ?? (playing.Request.FadeOut > TimeSpan.Zero ? playing.Request.FadeOut : DefaultFadeAway);
            if (fadeAway && fade > TimeSpan.Zero)
            {
                playing.Voice.FadeTo(0f, fade);
                var timer = _time.CreateTimer(_ => Run(() => End(playing)), null, fade, Timeout.InfiniteTimeSpan);
                playing.Timer = timer;
            }
            else
            {
                End(playing);
            }
        }
    }

    /// <summary>How long a fade-away lasts when neither the stop nor the media says.</summary>
    public static readonly TimeSpan DefaultFadeAway = TimeSpan.FromSeconds(1);

    public void Dispose()
    {
        _closing.Cancel();
        Stop(MediaFilter.All);
        _closing.Dispose();
    }

    private async Task Finish(MediaRequest request, Task<(string? Path, string? Problem)> resolving, long generation)
    {
        (string? Path, string? Problem) result;
        try
        {
            result = await resolving.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Run(() => Begin(request, result, generation));
    }

    private void Begin(MediaRequest request, (string? Path, string? Problem) resolved, long generation)
    {
        if (_closing.IsCancellationRequested || !_pending.Remove(generation))
        {
            return;
        }

        if (resolved.Path is not { } path)
        {
            Failed?.Invoke(this, (request, resolved.Problem ?? "no file"));
            return;
        }

        // The rules are applied again: a fetch takes time, and what is playing may have changed while it ran.
        if (_playing.Any(p => p.Request.Priority is { } held && held > (request.Priority ?? 0)))
        {
            return;
        }

        if (request.Kind == MediaKind.Music && _playing.FirstOrDefault(p => p.Request.Kind == MediaKind.Music && p.Request.Name == request.Name) is { } same)
        {
            if (request.Continue)
            {
                return;
            }

            End(same);
        }

        foreach (var playing in _playing.ToArray())
        {
            var replacedByKey = request.Key is not null && playing.Request.Key == request.Key && playing.Request.Name != request.Name;
            var outranked = request.Priority is { } priority && (playing.Request.Priority ?? 0) < priority;
            var displaced = request.Exclusive && playing.Request.Exclusive;
            if (replacedByKey || outranked || displaced)
            {
                End(playing);
            }
        }

        var start = new AudioStart(_mix.For(request), request.Loops, request.Start, request.Finish, request.FadeIn, request.FadeOut);
        if (_output.Play(path, start) is not { } voice)
        {
            Failed?.Invoke(this, (request, "it could not be played"));
            return;
        }

        var entry = new Playing(request, voice);
        _playing.Add(entry);
        voice.Ended += (_, _) => Run(() => Forget(entry));
        Started?.Invoke(this, request);
    }

    private void End(Playing playing)
    {
        playing.Timer?.Dispose();
        if (_playing.Remove(playing))
        {
            playing.Voice.Dispose();
        }
    }

    private void Forget(Playing playing)
    {
        playing.Timer?.Dispose();
        _playing.Remove(playing);
    }

    private void Run(Action action)
    {
        if (Post is { } post)
        {
            post(action);
        }
        else
        {
            action();
        }
    }

    private sealed class Playing(MediaRequest request, IAudioVoice voice)
    {
        public MediaRequest Request { get; } = request;

        public IAudioVoice Voice { get; } = voice;

        public bool Paused { get; set; }

        public ITimer? Timer { get; set; }
    }
}
