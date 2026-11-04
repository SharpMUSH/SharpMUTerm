namespace SharpMUTerm.Core.Session;

/// <summary>How a connection ended (<see cref="WorldSession.LastClose"/>).</summary>
public enum SessionClose
{
    /// <summary>No connection has ended yet.</summary>
    None,

    /// <summary>The player closed it: Disconnect, Reconnect, quitting, closing the session.</summary>
    Player,

    /// <summary>The server closed it in an orderly way.</summary>
    Server,

    /// <summary>It failed: reset, timed out, a keepalive went unanswered.</summary>
    Failed,
}

/// <summary>What an <see cref="AutoReconnect"/> just did.</summary>
public enum ReconnectStep
{
    /// <summary>A try is waiting for its time (<see cref="AutoReconnect.DueAt"/>).</summary>
    Scheduled,

    /// <summary>A try is dialling now.</summary>
    Dialling,

    /// <summary>The connection is back.</summary>
    Connected,

    /// <summary>The last try failed; nothing more will be tried until the player connects.</summary>
    GaveUp,

    /// <summary>The player stopped it, or connected by hand.</summary>
    Cancelled,
}

/// <summary>
/// Dials a dropped connection again, for a world that asks for it (<see cref="Configuration.WorldDefinition.AutoReconnect"/>).
/// One per session; it listens to the session's state and commands and does nothing until a connection ends.
/// <para>
/// <b>A drop is a connection that ended without the player asking.</b> <see cref="SessionClose.Player"/> never
/// reconnects. Neither does <see cref="SessionClose.Server"/> within <see cref="LogoutWindow"/> of a command the player
/// sent: a server that closes the socket straight after <c>QUIT</c> or <c>@quit</c> is saying goodbye, and dialling it
/// back would log the player in again. A server closing on its own (a reboot, an idle timeout) and every failure do
/// reconnect. That last-command rule errs towards <em>not</em> dialling: a reboot in the seconds after a command is
/// missed, and ⌥R is one key. Mudlet's rule — every close but the player's — dials straight back after a logout.
/// </para>
/// <para>
/// <b>Tries back off and run out</b> (<see cref="Delays"/>: 5, 15, 30, 60, 60 seconds). A connection has to last
/// <see cref="StableAfter"/> before the count starts again, so a server that takes the connection and then refuses the
/// login is tried five times and left alone, rather than for ever. A try that fails to connect counts as one.
/// </para>
/// <para>
/// Runs on whatever thread the session raises on and the clock fires on; <see cref="Changed"/> is raised outside its
/// lock, and a host marshals it.
/// </para>
/// </summary>
public sealed class AutoReconnect : IDisposable
{
    /// <summary>The wait before each try, in order; there are as many tries as entries.</summary>
    public static readonly IReadOnlyList<TimeSpan> Delays =
    [
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(60),
    ];

    /// <summary>How long a connection must last before the tries are counted from the first again.</summary>
    public static readonly TimeSpan StableAfter = TimeSpan.FromSeconds(60);

    /// <summary>A server closing this soon after a command the player sent is a logout, not a drop.</summary>
    public static readonly TimeSpan LogoutWindow = TimeSpan.FromSeconds(10);

    private readonly WorldSession _session;
    private readonly TimeProvider _time;
    private readonly Func<WorldSession, Task> _connect;
    private readonly Lock _gate = new();
    private ConnectionState _state;
    private DateTimeOffset _connectedAt;
    private DateTimeOffset? _lastCommandAt;
    private ITimer? _timer;
    private int _tries;
    private bool _dialling;
    private bool _disposed;

    /// <param name="session">The session to watch.</param>
    /// <param name="time">The clock the waits are measured on.</param>
    /// <param name="connect">Dials the session; a failure is the session's to report, and counts as a try.</param>
    public AutoReconnect(WorldSession session, TimeProvider time, Func<WorldSession, Task> connect)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _connect = connect ?? throw new ArgumentNullException(nameof(connect));
        _state = session.State;
        session.StateChanged += OnStateChanged;
        session.CommandSent += OnCommandSent;
    }

    /// <summary>Raised with each step; never under the lock.</summary>
    public event EventHandler<ReconnectStep>? Changed;

    /// <summary>When the waiting try dials, or null when none is waiting.</summary>
    public DateTimeOffset? DueAt { get; private set; }

    /// <summary>The try waiting or dialling, from 1; 0 when none is.</summary>
    public int Try { get; private set; }

    /// <summary>Whether a try is waiting or dialling.</summary>
    public bool IsActive => DueAt is not null || _dialling;

    /// <summary>The whole seconds until the waiting try dials, rounded up, or null.</summary>
    public int? SecondsLeft =>
        DueAt is { } due ? Math.Max(0, (int)Math.Ceiling((due - _time.GetUtcNow()).TotalSeconds)) : null;

    /// <summary>Stops a waiting try and forgets the count. Does nothing when none is waiting.</summary>
    public void Cancel()
    {
        bool cancelled;
        lock (_gate)
        {
            cancelled = DueAt is not null || _dialling;
            Reset();
        }

        if (cancelled)
        {
            Changed?.Invoke(this, ReconnectStep.Cancelled);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Reset();
        }

        _session.StateChanged -= OnStateChanged;
        _session.CommandSent -= OnCommandSent;
    }

    private void Reset()
    {
        _timer?.Dispose();
        _timer = null;
        DueAt = null;
        Try = 0;
        _tries = 0;
        _dialling = false;
    }

    private void OnCommandSent(object? sender, string command)
    {
        lock (_gate)
        {
            _lastCommandAt = _time.GetUtcNow();
        }
    }

    private void OnStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        ReconnectStep? step = null;
        lock (_gate)
        {
            var was = _state;
            _state = e.State;
            if (_disposed)
            {
                return;
            }

            var now = _time.GetUtcNow();
            switch (e.State)
            {
                case ConnectionState.Connected:
                    _connectedAt = now;
                    if (_dialling || DueAt is not null)
                    {
                        // Back, whether by our try or the player's own connect: nothing waits any more. The count
                        // is kept until the connection has lasted, so a login the server refuses still runs out.
                        _timer?.Dispose();
                        _timer = null;
                        DueAt = null;
                        _dialling = false;
                        Try = 0;
                        step = ReconnectStep.Connected;
                    }

                    break;

                case ConnectionState.Disconnected or ConnectionState.Faulted:
                    var droppedLive = was == ConnectionState.Connected;
                    var failedTry = _dialling && was == ConnectionState.Connecting;
                    if (!droppedLive && !failedTry)
                    {
                        break;
                    }

                    _dialling = false;
                    if (droppedLive && now - _connectedAt >= StableAfter)
                    {
                        _tries = 0;
                    }

                    if (!_session.World.AutoReconnect || IsGoodbye(now))
                    {
                        Reset();
                        break;
                    }

                    step = Schedule(now);
                    break;
            }
        }

        if (step is { } raised)
        {
            Changed?.Invoke(this, raised);
        }
    }

    private bool IsGoodbye(DateTimeOffset now) => _session.LastClose switch
    {
        SessionClose.Player => true,
        SessionClose.Server => _lastCommandAt is { } at && now - at <= LogoutWindow,
        _ => false,
    };

    private ReconnectStep Schedule(DateTimeOffset now)
    {
        if (_tries >= Delays.Count)
        {
            Reset();
            return ReconnectStep.GaveUp;
        }

        var delay = Delays[_tries];
        _tries++;
        Try = _tries;
        DueAt = now + delay;
        _timer?.Dispose();
        _timer = _time.CreateTimer(_ => Fire(), null, delay, Timeout.InfiniteTimeSpan);
        return ReconnectStep.Scheduled;
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed || DueAt is null)
            {
                return;
            }

            DueAt = null;
            _dialling = true;
        }

        Changed?.Invoke(this, ReconnectStep.Dialling);
        _ = DialAsync();
    }

    private async Task DialAsync()
    {
        try
        {
            await _connect(_session).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The session has printed and logged why; its Faulted state schedules the next try.
        }
    }
}
