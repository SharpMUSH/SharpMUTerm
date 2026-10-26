using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Media;
using SharpMUTerm.Core.Session;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// Game sound: each connection's <see cref="MediaQueue"/>, fed by its MCMP commands, played on the host's audio
/// device. The device is opened the first time a game asks for something, never at start, so a client that
/// is never sent a sound never touches the sound system.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    /// <summary>Where sound files are kept: memory-only (nothing fetched) unless <c>Program</c> hands it a folder.</summary>
    private readonly MediaCache _media;

    /// <summary>Each connection's queue, made on its first media command.</summary>
    private readonly Dictionary<WorldSession, MediaQueue> _queues = new();

    private IAudioOutput? _audio;

    private bool _audioOpened;

    /// <summary>⌃P ▸ Mute sound. For this run only: a mute is for the next few minutes, not a setting.</summary>
    private bool _muted;

    /// <summary>Whether this client can play sound at all: the host has a device to offer.</summary>
    private bool CanPlaySound => Host.Audio is not null;

    /// <summary>The status row's mute cell while muted.</summary>
    internal const string MutedCell = "♪ muted";

    /// <summary>Test seam: the queue a session's media goes to, or null before its first command.</summary>
    internal MediaQueue? MediaQueueFor(WorldSession session) => _queues.GetValueOrDefault(session);

    internal bool Muted => _muted;

    private void StartSound(WorldSession session)
    {
        session.PlaysMedia = CanPlaySound && _config.Sound.Enabled;
        session.MediaReceived += (_, command) => OnUiThread(() => OnMedia(session, command));
        session.StateChanged += (_, e) =>
        {
            if (e.State is ConnectionState.Disconnected or ConnectionState.Faulted)
            {
                OnUiThread(() => MediaQueueFor(session)?.Stop(MediaFilter.All));
            }
        };
    }

    private void OnMedia(WorldSession session, MediaCommand command)
    {
        if (QueueFor(session) is { } queue)
        {
            queue.Apply(command);
            return;
        }

        // Nothing plays — no device, or sound is off — so a caption is the only thing a sound can be.
        if (command is MediaCommand.Play play && _config.Sound.Captions)
        {
            Caption(session, play.Request);
        }
    }

    private MediaQueue? QueueFor(WorldSession session)
    {
        if (_queues.TryGetValue(session, out var queue))
        {
            return queue;
        }

        if (!_config.Sound.Enabled || OpenAudio() is not { } output)
        {
            return null;
        }

        queue = new MediaQueue(output, _media, $"{session.World.Host}:{session.World.Port}", _time)
        {
            Post = OnUiThread,
            Mix = _config.Sound.Mix(_muted),
            Download = session.World.DownloadMedia,
        };
        queue.Started += (_, request) =>
        {
            if (_config.Sound.Captions)
            {
                Caption(session, request);
            }
        };
        queue.Failed += (_, failure) =>
            _messages.Record(_time.GetUtcNow(), MessageSeverity.Info, $"sound {failure.Request.Name} not played — {failure.Problem}", SessionTitle(session));
        _queues[session] = queue;
        return queue;
    }

    private IAudioOutput? OpenAudio()
    {
        if (!_audioOpened)
        {
            _audioOpened = true;
            _audio = Host.Audio?.Invoke();
            if (CanPlaySound && _audio is null)
            {
                Notice("No sound device could be opened; game sound is captions only.", MessageSeverity.Info, key: "sound");
            }
        }

        return _audio;
    }

    /// <summary>A dim <c>♪ caption</c> row in the session's window. Chrome: it is the client's line, not the game's.</summary>
    private void Caption(WorldSession session, MediaRequest request)
    {
        if (_sessionWindows.TryGetValue(session, out var windowId))
        {
            AppendWindowLine(windowId, $"[dim]♪ {Escape(Snippet(request.CaptionText))}[/]", StampNow());
        }
    }

    /// <summary>Re-applies the F7 levels and the mute to every queue; a level edit is heard at once.</summary>
    private void ApplySoundMix()
    {
        foreach (var (session, queue) in _queues)
        {
            queue.Mix = _config.Sound.Mix(_muted);
            queue.Download = session.World.DownloadMedia;
            if (!_config.Sound.Enabled)
            {
                queue.Stop(MediaFilter.All);
            }
        }
    }

    private void SetMuted(bool muted)
    {
        _muted = muted;
        ApplySoundMix();
        Notice(muted ? "Sound muted." : "Sound on.", MessageSeverity.Info, key: "sound");
        RefreshStatusBar();
    }

    private void StopSound(WorldSession session)
    {
        if (_queues.Remove(session, out var queue))
        {
            queue.Dispose();
        }
    }

    private void DisposeSound()
    {
        foreach (var queue in _queues.Values)
        {
            queue.Dispose();
        }

        _queues.Clear();
        (_audio as IDisposable)?.Dispose();
        _audio = null;
    }
}
