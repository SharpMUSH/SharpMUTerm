using System.Text.RegularExpressions;

namespace SharpMUTerm.Core.Mapping;

/// <summary>
/// Walks a path one step at a time: a step is sent, and the next goes when the map says the character has
/// arrived where that step leads — the generic mapper's <c>speedwalk_wait</c>. Sending them all at once would
/// leave a game that drops a step (a closed door, a fight) walking the rest of the path from the wrong room.
/// <para>
/// It stops on its own when the walk is done; when the character arrives somewhere off the path; when a line
/// matches the world's failure pattern (<see cref="Configuration.MapSettings.WalkFailurePattern"/>); and when a
/// step has not arrived within <see cref="StepTimeout"/>, which is what a game that reports no rooms at all
/// would otherwise leave hanging for ever.
/// </para>
/// </summary>
public sealed class Speedwalk : IDisposable
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);

    private readonly MapRecorder _recorder;
    private readonly Func<string, Task> _send;
    private readonly TimeProvider _time;
    private readonly Regex? _failure;
    private readonly List<PathStep> _steps = new();
    private ITimer? _timer;
    private int _next;
    private long? _from;
    private long _version;

    public Speedwalk(MapRecorder recorder, Func<string, Task> send, string? failurePattern = null, TimeProvider? time = null)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _time = time ?? TimeProvider.System;
        if (!string.IsNullOrWhiteSpace(failurePattern))
        {
            try
            {
                _failure = new Regex(failurePattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, MatchTimeout);
            }
            catch (ArgumentException)
            {
                _failure = null; // a bad pattern only costs the early stop; the timeout still ends a stuck walk
            }
        }

        _recorder.Changed += OnMapChanged;
    }

    /// <summary>How long a step may take to arrive before the walk gives up.</summary>
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(8);

    /// <summary>Where timer callbacks run; the client sets this to its UI thread. Null runs them in place.</summary>
    public Action<Action>? Post { get; init; }

    public bool IsWalking => _next < _steps.Count;

    /// <summary>The room the walk is going to, while it walks.</summary>
    public long? Destination => IsWalking ? _steps[^1].To : null;

    /// <summary>How many steps have not yet been sent or arrived, while walking.</summary>
    public int Remaining => Math.Max(0, _steps.Count - _next);

    /// <summary>The rooms still ahead, in order — what the map window highlights.</summary>
    public IEnumerable<long> RoomsAhead => _steps.Skip(_next).Select(s => s.To);

    /// <summary>Raised when the walk starts, takes a step, or stops; the text says why it stopped, or is null.</summary>
    public event EventHandler<string?>? Changed;

    /// <summary>Walks <paramref name="steps"/>, replacing any walk already going. An empty path is already there.</summary>
    public void Start(IReadOnlyList<PathStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps.Clear();
        _steps.AddRange(steps);
        _next = 0;
        _from = _recorder.CurrentRoomId;
        if (_steps.Count == 0)
        {
            Changed?.Invoke(this, "already there");
            return;
        }

        SendNext();
    }

    /// <summary>Stops walking; the reason is shown to the player.</summary>
    public void Stop(string reason)
    {
        if (!IsWalking)
        {
            return;
        }

        _steps.Clear();
        _next = 0;
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Changed?.Invoke(this, reason);
    }

    /// <summary>A line of output, checked against the failure pattern while walking.</summary>
    public void Observe(string line)
    {
        if (!IsWalking || _failure is null)
        {
            return;
        }

        try
        {
            if (_failure.IsMatch(line))
            {
                Stop($"stopped: {line.Trim()}");
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
    }

    public void Dispose()
    {
        _recorder.Changed -= OnMapChanged;
        _timer?.Dispose();
    }

    private void SendNext()
    {
        var step = _steps[_next];
        _version++;
        _timer ??= _time.CreateTimer(_ => OnTimeout(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _timer.Change(StepTimeout, Timeout.InfiniteTimeSpan);
        _ = _send(step.Command);
        Changed?.Invoke(this, null);
    }

    private void OnTimeout()
    {
        var version = _version;
        void Check()
        {
            if (IsWalking && version == _version)
            {
                Stop("stopped: the game did not say where that step went");
            }
        }

        if (Post is { } post)
        {
            post(Check);
        }
        else
        {
            Check();
        }
    }

    private void OnMapChanged(object? sender, EventArgs e)
    {
        if (!IsWalking || _recorder.CurrentRoomId is not { } here)
        {
            return;
        }

        var expected = _steps[_next].To;
        if (here == expected)
        {
            _from = expected;
            _next++;
            if (IsWalking)
            {
                SendNext();
            }
            else
            {
                _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _steps.Clear();
                _next = 0;
                Changed?.Invoke(this, "arrived");
            }

            return;
        }

        // An exits update or a look leaves the character where it was; anywhere else is off the path.
        if (here != _from)
        {
            Stop("stopped: that step went somewhere else");
        }
    }
}
