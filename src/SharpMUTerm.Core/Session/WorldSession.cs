using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Logging;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Core.Telnet;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Transport;

namespace SharpMUTerm.Core.Session;

/// <summary>
/// The runtime for a single connected world. Orchestrates the full pipeline:
/// transport → telnet → ANSI parse → trigger engine → scrollback / logging, and the outbound
/// path: user input → alias expansion → local echo → send. UI-agnostic; the view binds to its
/// events and its <see cref="Scrollback"/>.
/// </summary>
public sealed class WorldSession : IAsyncDisposable
{
    private static readonly TextStyle EchoStyle =
        new(TerminalColor.FromIndex(11), TerminalColor.Default, TextAttributes.None);

    private static readonly TextStyle SystemStyle =
        new(TerminalColor.FromIndex(6), TerminalColor.Default, TextAttributes.Italic);

    private readonly Func<ConnectionOptions, ITelnetSession> _sessionFactory;
    private ILineParser _parser;
    private EmojiSubstitutor? _emoji;
    private ILogSink? _log;
    private readonly TextSettings? _text;
    private readonly InputSettings? _input;

    /// <summary>
    /// The app-wide CHARSET preference order (<see cref="AppConfiguration.CharsetOrder"/>), held by
    /// reference like <see cref="_text"/> and <see cref="_input"/>. Null means the built-in default
    /// order. This is what a world on <c>auto</c> states as its preference — before it was threaded
    /// through here the app-wide setting existed in the config and was read by nothing at all.
    /// </summary>
    private readonly IReadOnlyList<string>? _charsetOrder;
    private TimerDefinition[] _timers = Array.Empty<TimerDefinition>();
    private readonly List<IDisposable> _timerHandles = new();
    private ITelnetSession? _telnet;
    private ILogger _logger = NullLogger.Instance;

    /// <summary>
    /// Creates a session for a world and (optionally) the character being connected as. Automation
    /// is composed from the union of <paramref name="triggerSets"/> — resolve them for a character
    /// via <see cref="AppConfiguration.ResolveTriggerSets"/>. A null character yields an anonymous
    /// session (e.g. an ad-hoc command-line connection) that logs itself in to nothing.
    /// <para>
    /// <paramref name="triggerSets"/> is not a permanent choice: <see cref="ReloadAutomation"/> re-points
    /// a live session at whatever the configuration says now, which is what makes assigning a set or
    /// adding a rule mid-connection take effect. Without it a session opened before the user finished
    /// configuring it ran an empty engine for the rest of its life.
    /// </para>
    /// <para>
    /// <paramref name="text"/> and <paramref name="input"/> are the app-wide rendering and input
    /// preferences the F7/F8 screens edit. They are held by reference and read <em>per line</em>, not
    /// copied at construction, because those screens edit the live configuration object in place —
    /// copying them here is exactly how a checkbox ends up needing a restart to mean anything. Null
    /// (the default, and what a Core test that doesn't care passes) means "the built-in defaults".
    /// </para>
    /// </summary>
    public WorldSession(
        WorldDefinition world,
        CharacterDefinition? character = null,
        IReadOnlyList<TriggerSet>? triggerSets = null,
        Func<ConnectionOptions, ITelnetSession>? sessionFactory = null,
        ILogSink? log = null,
        int scrollbackCapacity = ScrollbackBuffer.DefaultCapacity,
        TextSettings? text = null,
        InputSettings? input = null,
        ScrollbackSpillOptions? spill = null,
        IReadOnlyList<string>? charsetOrder = null)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        Character = character;
        _sessionFactory = sessionFactory ?? DefaultSessionFactory;
        _log = log;
        _text = text;
        _input = input;
        _charsetOrder = charsetOrder;
        _parser = NewParser(world.ContentFormat);
        _emoji = NewEmoji(world.Emoji);
        Scrollback = new ScrollbackBuffer(scrollbackCapacity, CreateSpill(spill ?? new ScrollbackSpillOptions()));

        Triggers = new TriggerEngine();
        Aliases = new AliasEngine();
        Macros = new MacroEngine();
        ReloadAutomation(triggerSets ?? Array.Empty<TriggerSet>());
    }

    /// <summary>
    /// Re-points this session's automation at <paramref name="triggerSets"/> — the sets the configuration
    /// resolves for its character <em>now</em>. Rules added at runtime by the scripting layer are kept;
    /// see <see cref="TriggerEngine.ReplaceConfigured"/>.
    /// <para>
    /// This is the answer to the bug it was written for: the engines used to be handed a list at
    /// construction, so a session opened before its character had a trigger set assigned — or before a
    /// set gained the rule the user was in the middle of writing — went on matching nothing until it was
    /// reconnected, with no way to tell from the client that this was what had happened. Membership is
    /// now as live as a rule's own <see cref="Trigger.Pattern"/> already was.
    /// </para>
    /// <para>
    /// Running <see cref="Scheduler"/> timers are deliberately <em>not</em> re-armed. A timer's period is
    /// baked into its schedule and re-periodising one means tearing it down, which resets every other
    /// timer's phase — and this method runs after every committed settings change, so doing that here
    /// would reset the phase of every timer on the session each time a checkbox was ticked. The new
    /// definitions apply at the next connect, which is what the F6 screen says. Enabled/command edits
    /// were already live, because <see cref="StartTimers"/> reads those inside the callback.
    /// </para>
    /// </summary>
    public void ReloadAutomation(IReadOnlyList<TriggerSet> triggerSets)
    {
        ArgumentNullException.ThrowIfNull(triggerSets);

        Triggers.ReplaceConfigured(triggerSets.SelectMany(s => s.Triggers));
        Aliases.ReplaceConfigured(triggerSets.SelectMany(s => s.Aliases));
        Macros.ReplaceConfigured(triggerSets.SelectMany(s => s.Macros));
        _timers = triggerSets.SelectMany(s => s.Timers).ToArray();
        ScriptFiles = triggerSets.SelectMany(s => s.ScriptFiles)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The disk cache behind this session's scrollback, or null when it is memory-only. Created
    /// lazily — the directory does not exist until a line is actually evicted from memory — and
    /// deleted when the session is disposed. See <see cref="FileScrollbackSpill"/> for why this is a
    /// cache and not a transcript: the session <em>log</em> is <see cref="AttachLog"/>'s business and
    /// stays opt-in.
    /// </summary>
    private IScrollbackSpill? CreateSpill(ScrollbackSpillOptions options) =>
        options.Enabled ? new FileScrollbackSpill(options, SessionKey) : null;

    private static EmojiSubstitutor? NewEmoji(EmojiSettings emoji) =>
        emoji.Enabled ? new EmojiSubstitutor(emoji.Emoticons, emoji.Shortcodes) : null;

    private static ILineParser CreateParser(ContentFormat format) => format switch
    {
        ContentFormat.Mxp => new MxpParser(),
        ContentFormat.Pueblo => new PuebloParser(),
        _ => new AnsiParser(),
    };

    /// <summary>
    /// The one place a parser is constructed for this session. Wraps <see cref="CreateParser"/> so
    /// the two sites that build one — the constructor and <see cref="OnMxpEnabled"/>'s upgrade path —
    /// cannot diverge on what an <see cref="MxpParser"/> is wired up to: its
    /// <see cref="MxpParser.ClientReply"/> answers to a server's <c>&lt;VERSION&gt;</c>/<c>&lt;SUPPORT&gt;</c>
    /// request must reach the wire on both paths, since the upgrade path is the one every session that
    /// actually negotiates MXP takes.
    /// </summary>
    private ILineParser NewParser(ContentFormat format)
    {
        var parser = CreateParser(format);
        if (parser is MxpParser mxp)
        {
            mxp.ClientReply += (_, reply) => SendProtocolReply(reply);
            mxp.EntityChanged += (_, e) => GameData.SetMxp(e.Name, e.Value);
            mxp.Flagged += (_, e) => MxpFlagged?.Invoke(this, e);
            mxp.GaugeDeclared += (_, e) => DeclareMeter(e);
            mxp.MediaRequested += (_, e) => MediaReceived?.Invoke(this, e);
            mxp.Redirected += (_, e) => SpawnLine?.Invoke(this, new SpawnLineEventArgs(e.Frame, ApplyLinks(ApplyEmoji(e.Line))));
        }

        return parser;
    }

    /// <summary>
    /// Sends a parser-generated protocol reply, observing the result.
    /// </summary>
    /// <remarks>
    /// Nothing can await this — it is raised from the parser while the read loop is walking a line —
    /// so it is fire-and-forget by necessity, but not unobserved: a bare <c>_ = SendRawAsync(reply)</c>
    /// leaves a faulted send as an unobserved task exception, and this is the one send path the user
    /// cannot see fail. There is no line in the scrollback for it, no echo, and no error: a server that
    /// asked for <c>&lt;VERSION&gt;</c> and got nothing simply guesses at what this client supports.
    /// The diagnostics log is the only place it can surface.
    /// </remarks>
    private void SendProtocolReply(string reply)
    {
        _ = Send();

        async Task Send()
        {
            try
            {
                await SendRawAsync(reply).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // The reply body is deliberately not logged: it is prefixed with a live ESC[1z, and
                // the diagnostics log is a file a human reads.
                Logger.LogWarning(ex, "Sending an MXP protocol reply to {Host}:{Port} failed", World.Host, World.Port);
            }
        }
    }

    /// <summary>
    /// Upgrades to the MXP parser when the option negotiates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only from <see cref="ContentFormat.Ansi"/>, which is the default and therefore means "nobody
    /// chose" rather than "somebody chose ANSI". A world explicitly set to Pueblo has been configured
    /// by a user who knows what that server speaks, and a negotiation must not overrule them.
    /// </para>
    /// <para>
    /// The old parser is flushed first, so whatever it still holds — in practice just open style/tag
    /// state, since <see cref="OnOutputReceived"/> already feeds and flushes it on every call and
    /// leaves nothing genuinely undelivered between one call and the next — is emitted under the rules
    /// it arrived under rather than being silently dropped. Style does not carry across the swap:
    /// MXP's own RESET is how a server re-establishes it, and inventing a carry-over would make the
    /// first line after negotiation depend on parser internals.
    /// </para>
    /// <para>
    /// <b>This is not what protects a line that straddles the negotiation moment.</b> That hazard lives
    /// one layer down, in <c>TelnetSession._pending</c>: bytes received before and after MXP negotiates
    /// can still be accumulated there and submitted together as one <c>OutputReceived</c> chunk, which
    /// this session then parses wholesale with whichever parser is current at that point — potentially
    /// re-reading pre-negotiation text as MXP. Fixing that is out of this method's scope (it needs its
    /// own buffering change and test story at the telnet layer); the flush here is cheap, correct for
    /// what it does cover, and is the right call to keep regardless.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Asks an MSDP server for the variables the meters read. MSDP servers commonly send nothing until a
    /// client <c>REPORT</c>s, so without this a meter pointed at <c>MSDP.HEALTH</c> stayed empty for ever.
    /// Raised from the read loop; the sends are fire-and-forget because a refused one changes nothing.
    /// </summary>
    private void OnMsdpEnabled(object? sender, EventArgs e)
    {
        if (sender is not ITelnetSession telnet)
        {
            return;
        }

        foreach (var variable in MsdpReports.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _ = SendMsdpQuietly(telnet, "REPORT", variable);
        }
    }

    private async Task SendMsdpQuietly(ITelnetSession telnet, string variable, string value)
    {
        try
        {
            await telnet.SendMsdpAsync(variable, value).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ObjectDisposedException)
        {
            Logger.LogDebug(ex, "MSDP {Variable} {Value} was not sent.", variable, value);
        }
    }

    /// <summary>
    /// What ⇥ completes on this connection's command line: names the game sent over GMCP, and the words of
    /// the output it shows, in any window. Emptied on connect.
    /// </summary>
    public CompletionIndex Completions { get; } = new();

    /// <summary>
    /// Raised on the read loop when the MOO hands this connection a buffer to edit (MCP simpleedit). Only an
    /// authenticated MCP message raises it.
    /// </summary>
    public event EventHandler<MooEdit>? MooEditReceived;

    /// <summary>The MOO's latest status text (MCP <c>dns-com-awns-status</c>), or null when none arrived this connection.</summary>
    public string? McpStatus { get; private set; }

    /// <summary>Raised on the read loop when <see cref="McpStatus"/> changes.</summary>
    public event EventHandler? McpStatusChanged;

    /// <summary>
    /// Sends an edited buffer back to the MOO. False, and nothing sent, when nothing is connected or the
    /// connection that handed the edit out is gone, since a reference means nothing to another one.
    /// </summary>
    public async Task<bool> SendMooEditAsync(MooEdit edit, IReadOnlyList<string> lines)
    {
        if (_telnet is not { IsConnected: true } telnet)
        {
            return false;
        }

        try
        {
            await telnet.SendMooEditAsync(edit, lines).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ObjectDisposedException)
        {
            Logger.LogDebug(ex, "The edit of {Name} was not sent.", edit.Name);
            return false;
        }
    }

    /// <summary>Sends a GMCP message on this connection; refused (false) when nothing is connected.</summary>
    public async Task<bool> SendGmcpAsync(string package, string json)
    {
        if (_telnet is not { IsConnected: true } telnet)
        {
            return false;
        }

        await telnet.SendGmcpAsync(package, json).ConfigureAwait(false);
        return true;
    }

    private void OnMxpEnabled(object? sender, EventArgs e) => UpgradeParser(ContentFormat.Mxp);

    /// <summary>
    /// Upgrades to the Pueblo parser when the server switches the connection to Pueblo — the same rule as
    /// MXP's, above: only from the automatic default.
    /// </summary>
    private void OnPuebloEnabled(object? sender, EventArgs e) => UpgradeParser(ContentFormat.Pueblo);

    /// <summary>
    /// The one parser swap. Only from <see cref="ContentFormat.Ansi"/> and only off the plain ANSI parser, so
    /// <b>the first markup a connection negotiates keeps it</b>: a server that offers both MXP and Pueblo is
    /// not switched back and forth by the order its handshakes finish in. (The telnet layer also declines to
    /// answer a Pueblo hello once MXP is on.)
    /// </summary>
    private void UpgradeParser(ContentFormat format)
    {
        if (World.ContentFormat != ContentFormat.Ansi || _parser is not AnsiParser)
        {
            return;
        }

        if (_parser.Flush() is { } tail)
        {
            ProcessOutputLine(tail);
        }

        _parser = NewParser(format);
    }

    /// <summary>
    /// Where this session's diagnostics go — its own, and the telnet stack's, which
    /// <see cref="DefaultSessionFactory"/> hands straight to <see cref="TelnetSession"/>. Defaults to
    /// <see cref="NullLogger.Instance"/>, so Core stays free of any logging implementation; the app sets
    /// it (via <see cref="SessionManager.Logger"/>) to its client diagnostics pipeline.
    /// <para>
    /// Assigning it also re-points the <see cref="Scrollback"/> buffer (and its spill cache), which is
    /// constructed before the app gets a chance to set this — otherwise a disk fault in the scrollback
    /// cache would report itself to <c>NullLogger</c>, i.e. nowhere.
    /// </para>
    /// </summary>
    public ILogger Logger
    {
        get => _logger;
        set
        {
            _logger = value ?? NullLogger.Instance;
            Scrollback.Logger = _logger;
        }
    }

    public WorldDefinition World { get; }

    /// <summary>The character this session connects as, or null for an anonymous connection.</summary>
    public CharacterDefinition? Character { get; }

    /// <summary>
    /// Stable identity for this session: <c>world.character</c>, or just the world name when
    /// connecting anonymously. Used to key open sessions in the <see cref="SessionManager"/>.
    /// </summary>
    public string SessionKey => Character is null ? World.Name : $"{World.Name}.{Character.Name}";

    /// <summary>Lua script files contributed by the active trigger sets, de-duplicated.</summary>
    public IReadOnlyList<string> ScriptFiles { get; private set; } = Array.Empty<string>();

    public ScrollbackBuffer Scrollback { get; }

    public TriggerEngine Triggers { get; }

    public AliasEngine Aliases { get; }

    public MacroEngine Macros { get; }

    public IntervalScheduler Scheduler { get; } = new();

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>The most recent prompt, or null if none is active.</summary>
    public StyledLine? CurrentPrompt { get; private set; }

    public event EventHandler<StyledLine>? LinePrinted;

    public event EventHandler<StyledLine?>? PromptChanged;

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public event EventHandler<GmcpMessageEventArgs>? GmcpReceived;

    /// <summary>
    /// A sound or music command from the server — MCMP's <c>Client.Media.*</c> — raised on the read loop. The
    /// client hands it to the connection's media queue; with no audio device nothing listens.
    /// </summary>
    public event EventHandler<Media.MediaCommand>? MediaReceived;

    public event EventHandler<MsdpMessageEventArgs>? MsdpReceived;

    /// <summary>
    /// The latest GMCP and MSDP data this connection has received. Emptied on connect, because a package a
    /// previous connection held says nothing about this one.
    /// </summary>
    public GameDataStore GameData { get; } = new();

    /// <summary>
    /// This connection's map, when the client keeps one: set by the client as the session opens, read by the
    /// scripting bridge's <c>map.*</c>. Null in a session nothing maps.
    /// </summary>
    public Mapping.SessionMap? Map { get; set; }

    /// <summary>
    /// The MSDP variables to ask for with <c>REPORT</c> once MSDP is agreed — the ones the status row's meters
    /// read. Set by the app; read at that moment, so a change applies from the next connection.
    /// </summary>
    public IReadOnlyList<string> MsdpReports { get; set; } = [];

    /// <summary>
    /// Whether this client will play what the server sends: set by the app when it has an audio device and game
    /// sound is on. True adds <c>Client.Media 1</c> to the GMCP modules, which is how MCMP asks; read on connect.
    /// </summary>
    public bool PlaysMedia { get; set; }

    /// <summary>Whether this connection agreed MSP; written on the read loop, read there too.</summary>
    private volatile bool _mspActive;

    /// <summary>The GMCP modules a connection asks for beyond the defaults: the world's, then MCMP's when it plays media.</summary>
    public static string[] GmcpModulesFor(WorldDefinition world, bool playsMedia)
    {
        var modules = world.GmcpModules.ToList();
        if (playsMedia)
        {
            modules.Add(Media.MediaCommand.GmcpModule);
        }

        if (world.GmcpLogin)
        {
            modules.Add(GmcpLogin.Module);
        }

        if (world.DiscordPresence)
        {
            modules.Add(Presence.GamePresence.GmcpModule);
        }

        return modules.ToArray();
    }

    public event EventHandler<MsspReceivedEventArgs>? MsspReceived;

    /// <summary>
    /// Raised on the read loop when an MXP element the server flagged closes — <c>RoomName</c>, <c>RoomDesc</c>,
    /// <c>RoomExit</c>, <c>RoomNum</c> and the like — with its text. What the MXP room source reads.
    /// </summary>
    public event EventHandler<MxpFlagText>? MxpFlagged;

    /// <summary>The most meters one connection's <c>&lt;GAUGE&gt;</c>s may add.</summary>
    public const int MaxMxpMeters = 8;

    private readonly Lock _metersGate = new();
    private List<StatusMeter> _mxpMeters = [];

    /// <summary>
    /// The status meters this connection's server asked for with MXP <c>&lt;GAUGE&gt;</c> and <c>&lt;STAT&gt;</c>,
    /// read from its entities (<c>MXP.hp</c>). Shown after the player's own; emptied on connect.
    /// </summary>
    public IReadOnlyList<StatusMeter> MxpMeters
    {
        get
        {
            lock (_metersGate)
            {
                return _mxpMeters;
            }
        }
    }

    /// <summary>Raised on the read loop when <see cref="MxpMeters"/> changes.</summary>
    public event EventHandler? MxpMetersChanged;

    private void DeclareMeter(MxpGauge gauge)
    {
        var meter = new StatusMeter
        {
            Label = StatusMeter.Clip(gauge.Caption ?? gauge.Entity),
            Value = $"{GameDataStore.MxpPrefix}.{gauge.Entity}",
            Max = gauge.Max is null ? null : $"{GameDataStore.MxpPrefix}.{gauge.Max}",
        };

        lock (_metersGate)
        {
            var kept = _mxpMeters.Where(m => !string.Equals(m.Value, meter.Value, StringComparison.OrdinalIgnoreCase)).ToList();
            if (kept.Count >= MaxMxpMeters)
            {
                return;
            }

            kept.Add(meter);
            _mxpMeters = kept;
        }

        MxpMetersChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler<SpawnLineEventArgs>? SpawnLine;

    /// <summary>Raised when the encoding in force changes — see <see cref="CurrentEncoding"/>.</summary>
    public event EventHandler<SessionEncodingEventArgs>? EncodingChanged;

    /// <summary>
    /// Raised when a trigger, alias, timer or macro names a script callback (consumed by the scripting
    /// layer). Raised on whichever thread ran the rule: the read loop for a trigger, the caller for input
    /// and keys, the scheduler for a timer.
    /// </summary>
    public event EventHandler<ScriptRequest>? ScriptRequested;

    public bool IsConnected => _telnet?.IsConnected == true;

    /// <summary>
    /// The encoding this session's text is actually going over the wire in, and why — negotiated by
    /// CHARSET, pinned by the world, or assumed because nothing negotiated. Null when there is no
    /// telnet session to ask, which is the only honest answer before a connect: <see cref="World"/>'s
    /// <see cref="WorldDefinition.Encoding"/> is a <em>preference</em>, and reporting it as though it
    /// were in force is what this property exists to stop.
    /// </summary>
    public SessionEncoding? CurrentEncoding => _telnet?.CurrentEncoding;

    /// <summary>Whether this session is writing its output to a log right now.</summary>
    public bool IsLogging => _log is not null;

    /// <summary>
    /// Starts logging this session's output to <paramref name="log"/>, mid-connection. Whatever it was
    /// logging to is flushed and closed first: a session writes to one log, and swapping a sink in
    /// without closing the old one leaves a handle open on a file the user believes they stopped.
    /// </summary>
    public void AttachLog(ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(log);
        DetachLog();
        _log = log;
    }

    /// <summary>
    /// Stops logging, flushing and closing the sink. Does nothing when there is no log, so "stop" is
    /// safe to call on a session that never started one.
    /// </summary>
    public void DetachLog()
    {
        var log = _log;
        _log = null;
        if (log is null)
        {
            return;
        }

        log.Flush();
        log.Dispose();
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (State is ConnectionState.Connecting or ConnectionState.Connected)
        {
            return;
        }

        // A prior faulted/disconnected session may still be referenced; dispose it before
        // reconnecting so its read loop and transport are released (its events won't fire again).
        if (_telnet is not null)
        {
            await _telnet.DisposeAsync().ConfigureAwait(false);
            _telnet = null;
        }

        // A parser carries per-connection state — MXP's security modes and default mode, its open-tag
        // stack, the current style — and none of it means anything to the next connection. Left alone,
        // a server that sent ESC[6z made the *following* session start in secure default, and a
        // <COLOR FORE=black BACK=black> a player typed survived the reconnect that was the obvious way
        // to get rid of it. Rebuilt rather than Reset(), because the negotiated MXP upgrade is parser
        // state too: ContentFormat is what the next connection starts from, and MXP has to be
        // negotiated again to take it back off ANSI.
        _parser = NewParser(World.ContentFormat);

        // Re-read with the parser, so an F5 change to the world's emoji switches reaches the next connection.
        _emoji = NewEmoji(World.Emoji);

        SetState(ConnectionState.Connecting, null);
        PrintSystem($"*** Connecting to {World.Host}:{World.Port}...");

        var telnet = _sessionFactory(World.ToConnectionOptions());
        _telnet = telnet;
        telnet.OutputReceived += OnOutputReceived;
        GameData.Clear();
        lock (_metersGate)
        {
            _mxpMeters = [];
        }

        telnet.GmcpReceived += (_, e) =>
        {
            // Stored before the event, so a handler reading GameData sees the message that raised it.
            GameData.SetGmcp(e.Package, e.Json);
            Completions.ObserveGmcp(e.Package, e.Json);
            OnGmcpLogin(e.Package, e.Json);
            if (World.DiscordPresence
                && e.Package.StartsWith("External.Discord.", StringComparison.OrdinalIgnoreCase)
                && Presence.GamePresence.Apply(GamePresence, e.Package, e.Json) is { } presence
                && presence != GamePresence)
            {
                GamePresence = presence;
                GamePresenceChanged?.Invoke(this, EventArgs.Empty);
            }
            GmcpReceived?.Invoke(this, e);
            if (e.Package.StartsWith("Client.Media", StringComparison.Ordinal)
                && Media.MediaCommand.FromGmcp(e.Package, e.Json) is { } media)
            {
                MediaReceived?.Invoke(this, media);
            }
        };
        telnet.MsdpReceived += (_, e) =>
        {
            GameData.SetMsdp(e.Json);
            MsdpReceived?.Invoke(this, e);
        };
        telnet.MsdpEnabled += OnMsdpEnabled;
        telnet.MsspReceived += (_, e) => MsspReceived?.Invoke(this, e);
        telnet.EncodingChanged += OnEncodingChanged;
        telnet.Disconnected += OnDisconnected;
        telnet.MxpEnabled += OnMxpEnabled;
        _mspActive = false;
        telnet.MspChanged += (_, active) => _mspActive = active;
        telnet.PuebloEnabled += OnPuebloEnabled;
        McpStatus = null;
        GamePresence = null;
        Completions.Clear();
        telnet.MooEditReceived += (_, edit) => MooEditReceived?.Invoke(this, edit);
        telnet.McpStatusReceived += (_, text) =>
        {
            McpStatus = text;
            McpStatusChanged?.Invoke(this, EventArgs.Empty);
        };

        try
        {
            await telnet.ConnectAsync(cancellationToken).ConfigureAwait(false);
            SetState(ConnectionState.Connected, null);
            PrintSystem("*** Connected.");
            await SendLoginAsync(cancellationToken).ConfigureAwait(false);
            StartTimers();
        }
        catch (Exception ex)
        {
            SetState(ConnectionState.Faulted, ex);
            PrintSystem($"*** Connection failed: {ex.Message}");
            Logger.LogWarning(ex, "Connect to {Host}:{Port} failed", World.Host, World.Port);
            throw;
        }
    }

    private void OnOutputReceived(object? sender, TelnetOutputEventArgs e)
    {
        if (e.IsPrompt)
        {
            _parser.Feed(e.Text);
            var flushed = _parser.Flush();
            var raw = flushed ?? StyledLine.Empty;
            var prompt = ApplyEmoji(_text?.StripIncomingColour == true ? StyledText.StripColour(raw) : raw)
                .WithPrompt(true);
            CurrentPrompt = prompt;
            PromptChanged?.Invoke(this, prompt);

            // Uses raw, not prompt: ProcessOutputLine runs StripIncomingColour/ExpandTabs/triggers/
            // ApplyEmoji itself, so the already-substituted line would double-substitute.
            //
            // Only when flushed is not null. A prompt boundary with no printable run (a bare SGR
            // reset, an erase sequence) makes Flush() return null — that is a boundary, not a blank
            // line the server sent, unlike the deliberate ProcessOutputLine(StyledLine.Empty) call
            // below. Do not merge the two.
            if (flushed is not null)
            {
                ProcessOutputLine(raw, isPrompt: true);
            }

            return;
        }

        var emitted = false;
        foreach (var completed in _parser.Feed(e.Text))
        {
            ProcessOutputLine(completed);
            emitted = true;
        }

        var tail = _parser.Flush();
        if (tail is not null)
        {
            ProcessOutputLine(tail);
            emitted = true;
        }
        else if (!emitted && _parser is not MxpParser { LineSwallowed: true })
        {
            ProcessOutputLine(StyledLine.Empty);
        }
    }

    /// <summary>
    /// The configured tab width, clamped into range. Clamped at the point of use rather than in the
    /// setter because <c>config.json</c> is hand-edited: a negative number there would otherwise throw
    /// out of <see cref="StyledText.ExpandTabs"/> on the read loop, on a line of ordinary output.
    /// </summary>
    private int TabWidth => Math.Clamp(
        _text?.TabWidth ?? TextSettings.DefaultTabWidth,
        0,
        TextSettings.MaxTabWidth);

    /// <summary>
    /// <paramref name="isPrompt"/> is not threaded through the pipeline below (colour-strip, tab
    /// expand, triggers, emoji) — it is applied once, at the end, to the line actually delivered.
    /// </summary>
    /// <summary>
    /// Raised on the read loop with the text of a line that began with the world's
    /// <see cref="WorldDefinition.GrabPrefix"/>: a MUSH handing back an attribute or a post to edit. The line
    /// is gagged and the text is the client's to put in front of the player, who sends it themselves.
    /// </summary>
    public event EventHandler<string>? EditGrabbed;

    /// <summary>
    /// Takes a grab line out of the stream. Before the triggers, so a rule cannot send what was grabbed, and
    /// matched exactly, case and all, because the prefix is a game's fixed marker and not a pattern.
    /// </summary>
    private bool TryGrab(StyledLine line)
    {
        if (World.GrabPrefix is not { Length: > 0 } prefix || EditGrabbed is null)
        {
            return false;
        }

        var text = line.Text;
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = text[prefix.Length..];
        EditGrabbed.Invoke(this, rest.StartsWith(' ') ? rest[1..] : rest);
        return true;
    }

    private void ProcessOutputLine(StyledLine line, bool isPrompt = false)
    {
        // An MSP trigger is the server asking for a sound, and is taken out before anything else sees the
        // line — but only on a connection that agreed MSP. Anywhere else it is text a player can type.
        if (_mspActive && Media.MspTrigger.Parse(line.Text) is { } sound)
        {
            MediaReceived?.Invoke(this, sound);
            return;
        }

        if (!isPrompt && TryGrab(line))
        {
            return;
        }

        // Colour is stripped from what the *server* sent, before the triggers run: a highlight rule
        // and this client's own system/echo lines are not "incoming ANSI colour" and keep theirs.
        if (_text?.StripIncomingColour == true)
        {
            line = StyledText.StripColour(line);
        }

        // Before the triggers, so a pattern matches the spaces the reader sees rather than a tab they
        // have no way to know is there. Read per line like the setting above it, so changing the width
        // takes effect on the next line of output rather than on the next restart.
        line = StyledText.ExpandTabs(line, TabWidth);

        var result = Triggers.Process(line);

        foreach (var invocation in result.ScriptInvocations)
        {
            ScriptRequested?.Invoke(this, ScriptRequest.FromMatch(ScriptSource.Trigger, invocation.Callback, invocation.Match));
        }

        // The line as it will be *shown*, computed once and used for both destinations. A spawn window
        // used to be handed `result.Line` while the main window was handed the emoji-substituted one, so
        // the same line read differently in the two panes it landed in; a capture is a copy of the line,
        // not a different line. Links are found here for the same reason, and after the substitution
        // rather than before it, so a link's target is exactly the text under it — a span whose visible
        // text and destination disagree is the shape of a phishing link, and this client should not be
        // in the business of manufacturing one.
        var shown = ApplyLinks(ApplyEmoji(result.Line)).WithPrompt(isPrompt);
        if (result.RouteMain || !result.Suppress || result.SpawnTargets.Count > 0)
        {
            Completions.ObserveLine(shown.Text); // only what is shown somewhere: a gagged word was never read
        }

        foreach (var target in result.SpawnTargets)
        {
            SpawnLine?.Invoke(this, new SpawnLineEventArgs(target, shown));
        }

        foreach (var response in result.Responses)
        {
            _ = SendRawAsync(response);
        }

        // A gag suppresses the *default* delivery — the line arriving in this session's own window
        // because nothing routed it anywhere. It does not cancel a destination a rule asked for: that
        // has always been true of the spawn loop above, which runs whatever the gag says, and it is now
        // true of the main window too. So `route: main` plus `gag` keeps the line here and nowhere else,
        // where before it deleted the line — because `main` was the F2 screen's label for the *absence*
        // of a route rather than a route.
        if (result.RouteMain || !result.Suppress)
        {
            Print(shown);
        }
    }

    /// <summary>
    /// Marks the plain-text URLs in a line clickable when F7's <c>detect links</c> is on; a no-op
    /// otherwise. Read here rather than at construction, like the settings around it, so unticking it
    /// stops the next line rather than the next session.
    /// </summary>
    private StyledLine ApplyLinks(StyledLine line) =>
        _text?.DetectLinks == false ? line : UrlDetector.ApplyToLine(line);

    /// <summary>
    /// Substitutes emoji across the whole line when enabled for this world (preserving word
    /// boundaries across span seams and each span's style/interaction); a no-op otherwise.
    /// <para>
    /// The world opts in and says <em>which</em> substitutions it wants
    /// (<see cref="WorldDefinition.Emoji"/>); F7's <c>emoji substitution</c> is the app-wide off
    /// switch over the top of it, read here rather than at construction so unticking it stops the
    /// next line rather than the next session.
    /// </para>
    /// </summary>
    private StyledLine ApplyEmoji(StyledLine line) =>
        _emoji is null || _text?.EmojiSubstitution == false ? line : _emoji.ApplyToLine(line);

    /// <summary>Handles a line of user input: alias expansion, local echo, and send.</summary>
    public async Task SendUserInputAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var expansion = Aliases.Expand(input);

        // Two switches, and both have to be on: the world's (some servers echo for you, some don't)
        // and F8's app-wide one. The app-wide one is read per line, not captured, so unticking it
        // stops the echo on the next command rather than the next session.
        if (World.LocalEcho && _input?.LocalEcho != false)
        {
            Print(StyledLine.FromText(input, EchoStyle));
        }

        if (expansion.Matched)
        {
            if (expansion.Alias?.ScriptCallback is { Length: > 0 } callback && expansion.Match is { } match)
            {
                ScriptRequested?.Invoke(this, ScriptRequest.FromMatch(ScriptSource.Alias, callback, match));
            }

            foreach (var command in expansion.Commands)
            {
                await SendCommandAsync(command, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await SendCommandAsync(input, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Raised for each command the player sends — typed (after alias expansion), from a macro, or from a
    /// speedwalk — after it has gone to the server. What the mapper reads to know which way a room was left.
    /// <para>
    /// <b>Never the login line or the on-connect commands</b>, which go straight to <see cref="SendRawAsync"/>:
    /// the login line carries the password, and nothing that listens here needs to see it.
    /// </para>
    /// </summary>
    public event EventHandler<string>? CommandSent;

    /// <summary>Sends a command the player chose, and says so on <see cref="CommandSent"/>.</summary>
    public async Task SendCommandAsync(string command, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            return;
        }

        await SendRawAsync(command, cancellationToken).ConfigureAwait(false);
        CommandSent?.Invoke(this, command);
    }

    /// <summary>
    /// Sends the character's login line — when <see cref="CharacterDefinition.Login"/> says there is one
    /// — followed by its semicolon-separated <see cref="CharacterDefinition.OnConnect"/> commands. A
    /// no-op for anonymous sessions.
    /// <para>
    /// <b>The on-connect commands are not gated on the login.</b> They never were, and they must not
    /// become so: they are the things you would type after logging in, and someone who types their own
    /// connect line still wants <c>+who</c> afterwards. They run for a character whose
    /// <see cref="LoginPlan"/> is <see cref="LoginPlan.Nothing"/> exactly as they do for one that logged
    /// itself in.
    /// </para>
    /// <para>
    /// The login line is deliberately <see cref="SendRawAsync"/> and not
    /// <see cref="SendUserInputAsync"/>: raw goes straight to telnet with no local echo and no transcript
    /// write, so the one line in this app that carries a password never reaches the output pane or the
    /// session log. Typing the same line by hand <em>is</em> echoed and logged — which is the whole reason
    /// the password is a field and the login line a template (see <see cref="ConnectStringTemplate"/>).
    /// </para>
    /// <para>
    /// It does say that it happened, though (<see cref="LoginSent"/>), naming the character and never the
    /// line. A login that goes out in complete silence is indistinguishable from one that was never sent
    /// — which is the position a user is in when the configuration is inert, and the position this whole
    /// change exists to get them out of. The inert case gets a line of its own for the same reason.
    /// </para>
    /// </summary>
    private async Task SendLoginAsync(CancellationToken cancellationToken)
    {
        var character = Character;
        if (character is null)
        {
            return;
        }

        var plan = character.Login();
        if (World.GmcpLogin && plan is LoginPlan.WithPassword)
        {
            // Wait for the server to say whether it takes credentials over GMCP; the connect line is the fallback.
            lock (_gmcpLoginGate)
            {
                _gmcpLoginWaiting = true;
                _gmcpLoginFallback?.Dispose();
                _gmcpLoginFallback = Scheduler.After(GmcpLoginWait, () => _ = FinishGmcpLoginAsync(credentials: false), "gmcp-login");
            }

            return;
        }

        await SendTextLoginAsync(character, plan, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendTextLoginAsync(CharacterDefinition character, LoginPlan plan, CancellationToken cancellationToken)
    {
        if (plan is not LoginPlan.Nothing)
        {
            await SendRawAsync(character.ResolveConnectString(), cancellationToken).ConfigureAwait(false);
            PrintSystem(LoginSent(character.Name));
        }

        if (plan is LoginPlan.PasswordUnused)
        {
            PrintSystem(PasswordUnusedWarning(character.Name));
        }

        await SendOnConnectAsync(character, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendOnConnectAsync(CharacterDefinition character, CancellationToken cancellationToken)
    {
        foreach (var command in SplitCommands(character.OnConnect))
        {
            await SendRawAsync(command, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What the game last said about itself for Discord (<c>External.Discord</c>), when the world allows it; null
    /// before then and after a reconnect. Written on the read loop.
    /// </summary>
    public Presence.GamePresence? GamePresence { get; private set; }

    /// <summary>Raised on the read loop when <see cref="GamePresence"/> changes.</summary>
    public event EventHandler? GamePresenceChanged;

    /// <summary>How long a GMCP login waits for <c>Char.Login.Default</c> before sending the connect line.</summary>
    public TimeSpan GmcpLoginWait { get; set; } = TimeSpan.FromSeconds(5);

    private readonly object _gmcpLoginGate = new();
    private bool _gmcpLoginWaiting;
    private IDisposable? _gmcpLoginFallback;

    /// <summary>
    /// The server's half of <c>Char.Login</c>: <c>Default</c> says how it takes a login, <c>Result</c> how one
    /// went. Runs on the read loop. Nothing here writes the password anywhere but the wire.
    /// </summary>
    private void OnGmcpLogin(string package, string json)
    {
        if (string.Equals(package, GmcpLogin.DefaultPackage, StringComparison.OrdinalIgnoreCase))
        {
            _ = FinishGmcpLoginAsync(credentials: GmcpLogin.OffersCredentials(json));
        }
        else if (string.Equals(package, GmcpLogin.ResultPackage, StringComparison.OrdinalIgnoreCase)
                 && GmcpLogin.ReadResult(json) is { } result
                 && Character is { } character)
        {
            PrintSystem(result.Success
                ? $"*** Logged in as {character.Name} over GMCP."
                : $"*** The GMCP login for {character.Name} was refused{(string.IsNullOrWhiteSpace(result.Message) ? "." : ": " + result.Message)}");
        }
    }

    /// <summary>
    /// Ends the wait, once: sends the credentials when the server takes them, the connect line when it does not
    /// or never said, then the character's on-connect commands.
    /// </summary>
    private async Task FinishGmcpLoginAsync(bool credentials)
    {
        lock (_gmcpLoginGate)
        {
            if (!_gmcpLoginWaiting)
            {
                return;
            }

            _gmcpLoginWaiting = false;
            _gmcpLoginFallback?.Dispose();
            _gmcpLoginFallback = null;
        }

        if (Character is not { } character)
        {
            return;
        }

        try
        {
            if (credentials && await SendGmcpAsync(GmcpLogin.CredentialsPackage, GmcpLogin.Credentials(character.Name, character.Password ?? string.Empty)).ConfigureAwait(false))
            {
                PrintSystem($"*** Sent the login for {character.Name} over GMCP.");
                await SendOnConnectAsync(character, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            await SendTextLoginAsync(character, character.Login(), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            Logger.LogDebug(ex, "The login for {Character} was not sent.", character.Name);
        }
    }

    private void CancelGmcpLogin()
    {
        lock (_gmcpLoginGate)
        {
            _gmcpLoginWaiting = false;
            _gmcpLoginFallback?.Dispose();
            _gmcpLoginFallback = null;
        }
    }

    /// <summary>
    /// What the session prints when it has logged itself in. It names the character and not the line,
    /// because the line is the one string in this client worth keeping out of the transcript — and
    /// <see cref="PrintSystem"/> writes to the transcript.
    /// </summary>
    internal static string LoginSent(string character) => $"*** Sent the login line for {character}.";

    /// <summary>
    /// What the session prints when a saved password has nowhere to go
    /// (<see cref="LoginPlan.PasswordUnused"/>). It is a warning at the moment it matters, beside the
    /// login it was not part of; F5 says the same thing while you are editing the character.
    /// </summary>
    internal static string PasswordUnusedWarning(string character) =>
        $"*** {character}'s saved password was not sent — its connect line has no " +
        $"{ConnectStringTemplate.PasswordToken}.";

    /// <summary>
    /// Realises the active trigger sets' <see cref="TimerDefinition"/>s on the session's
    /// <see cref="Scheduler"/>. Called once the connection is up, because a timer's whole job is to
    /// send something, and cancelled again on disconnect (<see cref="StopTimers"/>) so a dropped
    /// session doesn't keep firing into a closed socket.
    /// <para>
    /// <see cref="TimerDefinition.Enabled"/> and <see cref="TimerDefinition.Command"/> are read
    /// <em>inside</em> the callback rather than here, so flipping the F6 checkbox or retyping the
    /// command takes effect on the next firing. The interval and one-shot flag are baked into the
    /// schedule itself and so apply from the next connect — a running <see cref="Timer"/> cannot be
    /// re-periodised without being torn down, and tearing one down mid-cycle would silently reset
    /// every other timer's phase.
    /// </para>
    /// </summary>
    private void StartTimers()
    {
        StopTimers();

        foreach (var timer in _timers)
        {
            if (timer.IntervalSeconds <= 0)
            {
                continue;
            }

            var definition = timer;
            var interval = TimeSpan.FromSeconds(definition.IntervalSeconds);
            void Fire()
            {
                if (!definition.Enabled)
                {
                    return;
                }

                if (definition.ScriptCallback is { Length: > 0 } callback)
                {
                    ScriptRequested?.Invoke(this, ScriptRequest.Bare(ScriptSource.Timer, callback));
                }

                if (!string.IsNullOrWhiteSpace(definition.Command))
                {
                    _ = SendRawAsync(definition.Command);
                }
            }

            _timerHandles.Add(definition.OneShot
                ? Scheduler.After(interval, Fire, definition.Name)
                : Scheduler.Every(interval, Fire, definition.Name));
        }
    }

    /// <summary>Cancels every schedule <see cref="StartTimers"/> created, leaving script timers alone.</summary>
    private void StopTimers()
    {
        foreach (var handle in _timerHandles)
        {
            handle.Dispose();
        }

        _timerHandles.Clear();
    }

    /// <summary>Splits a semicolon-separated command string, trimming and dropping blank segments.</summary>
    private static IEnumerable<string> SplitCommands(string? commands)
    {
        if (string.IsNullOrWhiteSpace(commands))
        {
            yield break;
        }

        foreach (var part in commands.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return part;
        }
    }

    /// <summary>Sends a command verbatim (no alias expansion, no echo).</summary>
    public async Task SendRawAsync(string command, CancellationToken cancellationToken = default)
    {
        var telnet = _telnet;
        if (telnet is null || !telnet.IsConnected)
        {
            return;
        }

        await telnet.SendLineAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves a key to a macro and sends its command. Returns the command sent, or null.</summary>
    public async Task<string?> HandleKeyAsync(string keyDescriptor, CancellationToken cancellationToken = default)
    {
        var macro = Macros.Resolve(keyDescriptor);
        if (macro is null)
        {
            return null;
        }

        if (macro.ScriptCallback is { Length: > 0 } callback)
        {
            ScriptRequested?.Invoke(this, ScriptRequest.Bare(ScriptSource.Macro, callback));
        }

        if (string.IsNullOrEmpty(macro.Command))
        {
            return null;
        }

        await SendCommandAsync(macro.Command, cancellationToken).ConfigureAwait(false);
        return macro.Command;
    }

    public ValueTask SetWindowSizeAsync(int width, int height) =>
        _telnet?.SetWindowSizeAsync(width, height) ?? ValueTask.CompletedTask;

    /// <summary>Appends a client-generated informational line.</summary>
    public void PrintSystem(string text)
    {
        var line = StyledLine.FromText(text, SystemStyle);
        Scrollback.Append(line);
        _log?.WriteSystem(text);
        LinePrinted?.Invoke(this, line);
    }

    private void Print(StyledLine line)
    {
        Scrollback.Append(line);
        _log?.WriteLine(line);
        LinePrinted?.Invoke(this, line);
    }

    /// <summary>
    /// A charset change gets one line in the client message log and nothing else.
    /// <para>
    /// Not <c>PrintSystem</c>: that writes into the character's transcript, which someone keeps, and a
    /// protocol detail is not part of what was said in the room. And not a status-row notice either —
    /// a notice displaces the row for something the user cannot act on, and the overwhelmingly common
    /// case is a successful negotiation at connect time, which is not news. The log is where you look
    /// when the text is wrong, and the status row already says what is in force.
    /// </para>
    /// </summary>
    private void OnEncodingChanged(object? sender, SessionEncodingEventArgs e)
    {
        Logger.LogInformation(
            "{World}: text encoding is now {Encoding} ({Source}).",
            World.Name,
            e.Encoding.Name,
            e.Encoding.Source);
        EncodingChanged?.Invoke(this, e);
    }

    private void OnDisconnected(object? sender, SessionDisconnectedEventArgs e)
    {
        CancelGmcpLogin();
        StopTimers();
        ClearPrompt();
        SetState(e.IsClean ? ConnectionState.Disconnected : ConnectionState.Faulted, e.Error);
        PrintSystem(e.IsClean ? "*** Disconnected." : $"*** Connection lost: {e.Error?.Message}");
    }

    /// <summary>
    /// Drops the prompt, because a prompt is a live connection's question and a dead one is not
    /// asking it.
    /// </summary>
    /// <remarks>
    /// Cleared before the state change rather than after, so anything that repaints on
    /// <see cref="StateChanged"/> already sees no prompt and the two never disagree for a frame. Only
    /// raises <see cref="PromptChanged"/> when there was something to clear: a session that
    /// disconnects without ever having been prompted must not report a change it did not make.
    /// <para>
    /// The disconnect path is the only place this is needed, because it is the only way out of a
    /// connection: <c>TelnetSession</c> raises <c>Disconnected</c> from the <c>finally</c> of its read
    /// loop, so a clean close and a faulted one both arrive here.
    /// </para>
    /// </remarks>
    private void ClearPrompt()
    {
        if (CurrentPrompt is null)
        {
            return;
        }

        CurrentPrompt = null;
        PromptChanged?.Invoke(this, null);
    }

    public async Task DisconnectAsync()
    {
        if (_telnet is not null)
        {
            await SendLeavingAsync().ConfigureAwait(false);
            await _telnet.DisconnectAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sends the character's <see cref="CharacterDefinition.OnDisconnect"/> commands (<c>QUIT</c>, say)
    /// before the client closes a connection itself — on Disconnect, Reconnect and quitting the client. A
    /// connection the server dropped gets none: there is nothing left to send them on.
    /// <para>
    /// Bounded by <see cref="LeavingTimeout"/> and never throws, because it runs on the way out of the client
    /// and a hung socket must not hold the exit.
    /// </para>
    /// </summary>
    private async Task SendLeavingAsync()
    {
        if (!IsConnected || Character is not { } character)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(LeavingTimeout);
        try
        {
            foreach (var command in SplitCommands(character.OnDisconnect))
            {
                await SendRawAsync(command, timeout.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            Logger.LogDebug(ex, "{World}: the on-disconnect commands were not all sent", World.Name);
        }
    }

    /// <summary>How long the on-disconnect commands may take before the connection is closed anyway.</summary>
    internal static readonly TimeSpan LeavingTimeout = TimeSpan.FromSeconds(1);

    private void SetState(ConnectionState state, Exception? error)
    {
        State = state;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(state, error));
    }

    /// <summary>
    /// The real transport + telnet stack for this world.
    /// <para>
    /// The world's <see cref="WorldDefinition.Encoding"/> is an <em>override</em> when it names one:
    /// stated at the head of the CHARSET preference order so a cooperative server agrees to it, and
    /// used for decoding whatever the server says. <c>auto</c> — the default — states the app-wide
    /// order instead and uses whatever negotiation settles on. Instance-level (not static) because the
    /// factory has to see the world to do either.
    /// </para>
    /// <para>
    /// The session's <see cref="Logger"/> goes in with it. It used to be left out, so every diagnostic
    /// TelnetNegotiationCore produces — negotiation traces, option state, keepalive and MCCP/GMCP/MSDP
    /// errors — went to <c>NullLogger</c> and was discarded. That is the detail you want when a MU*
    /// misbehaves, and it now reaches the client's diagnostics pipeline.
    /// </para>
    /// <para>
    /// <b>MSSP is waited for and never asked for, and that is the fix to a bug this factory caused.</b>
    /// It used to set <c>RequestOptions</c> so the session wrote <c>IAC DO MSSP</c> the moment it
    /// connected, on the reasoning that a server which supports MSSP but waits to be asked is otherwise
    /// never asked. The reasoning was sound and the cost was not: refusing an option means
    /// <em>consuming</em> its three bytes, and a server that does not implement it and does not consume
    /// them prepends them to the next line the client sends — which is always the auto-login. Measured
    /// against a live game: with the request, the login line was never evaluated and the connect screen
    /// came back instead; without it, the same line reached the game. The client is not in a position to
    /// know which servers parse telnet properly, and the one that does not is exactly the one whose
    /// login it breaks, silently. MSSP now arrives the way its own specification writes the handshake —
    /// the server offers <c>IAC WILL MSSP</c>, the library answers <c>DO</c> — and the INFO screen's
    /// "publishes none" covers the servers that never offer.
    /// </para>
    /// </summary>
    private ITelnetSession DefaultSessionFactory(ConnectionOptions options)
    {
        var (order, encodingOverride) =
            TelnetSessionOptions.ResolveWorldEncoding(World.Encoding, _charsetOrder);

        return new TelnetSession(
            new TcpTransport(options),
            Logger,
            options: new TelnetSessionOptions
            {
                GmcpModules = GmcpModulesFor(World, PlaysMedia),
                AnnouncePueblo = World.ContentFormat is ContentFormat.Ansi or ContentFormat.Pueblo,
                CharsetOrder = order,
                EncodingOverride = encodingOverride,
                KeepaliveInterval = TelnetSessionOptions.ResolveKeepalive(World.KeepaliveSeconds),
            });
    }

    public async ValueTask DisposeAsync()
    {
        StopTimers();
        Scheduler.Dispose();
        DetachLog(); // flushes what the session logged before closing the file behind it
        Scrollback.Dispose(); // deletes this session's scrollback spill cache; the log above is kept
        if (_telnet is not null)
        {
            await SendLeavingAsync().ConfigureAwait(false);
            await _telnet.DisposeAsync().ConfigureAwait(false);
            _telnet = null;
        }
    }
}
