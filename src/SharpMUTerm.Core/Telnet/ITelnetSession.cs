namespace SharpMUTerm.Core.Telnet;

/// <summary>
/// A telnet session over a transport: option negotiation, charset decoding, prompt
/// detection, and out-of-band protocols (GMCP/MSSP/MSDP/MCCP). Emits decoded server
/// output as text; ANSI styling and scrollback live in higher layers.
/// </summary>
public interface ITelnetSession : IAsyncDisposable
{
    bool IsConnected { get; }

    /// <summary>
    /// The encoding this session is decoding and encoding with, and whether CHARSET negotiated it,
    /// the world pinned it, or it is merely what was assumed because nothing negotiated.
    /// </summary>
    SessionEncoding CurrentEncoding { get; }

    /// <summary>Server output: completed lines and prompts.</summary>
    event EventHandler<TelnetOutputEventArgs>? OutputReceived;

    /// <summary>Raised when <see cref="CurrentEncoding"/> changes.</summary>
    event EventHandler<SessionEncodingEventArgs>? EncodingChanged;

    /// <summary>GMCP messages routed from the server.</summary>
    event EventHandler<GmcpMessageEventArgs>? GmcpReceived;

    /// <summary>MSDP messages routed from the server.</summary>
    event EventHandler<MsdpMessageEventArgs>? MsdpReceived;

    /// <summary>MSSP server-status data.</summary>
    event EventHandler<MsspReceivedEventArgs>? MsspReceived;

    /// <summary>Raised when the connection ends.</summary>
    event EventHandler<SessionDisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// Raised once the peer has negotiated MXP (RFC-less option 91). A consumer uses this to decide
    /// how to parse the stream: MXP is a property of the connection, not of the user's configuration.
    /// </summary>
    event EventHandler? MxpEnabled;

    /// <summary>
    /// Raised when MSP (option 90) is agreed (<c>true</c>) or withdrawn (<c>false</c>). While it is agreed, a
    /// line that is an MSP trigger is a sound request rather than text. A session that cannot negotiate MSP
    /// never raises it, which is what the default does.
    /// </summary>
    event EventHandler<bool>? MspChanged
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Raised once the server has switched this connection to Pueblo (its start sequence, after our
    /// <c>PUEBLOCLIENT</c>). The <see cref="MxpEnabled"/> of Pueblo.
    /// </summary>
    event EventHandler? PuebloEnabled;

    /// <summary>
    /// Raised once the client has agreed to the server's MSDP, so a consumer can ask it to <c>REPORT</c> the
    /// variables it wants. MSDP servers commonly send nothing until asked.
    /// </summary>
    event EventHandler? MsdpEnabled;

    /// <summary>
    /// Raised when a MOO hands this client a buffer to edit (MCP <c>dns-org-mud-moo-simpleedit</c>). Only an
    /// authenticated MCP message raises it, so a player cannot.
    /// </summary>
    event EventHandler<MooEdit>? MooEditReceived
    {
        add { }
        remove { }
    }

    /// <summary>Raised with a MOO's status text (MCP <c>dns-com-awns-status</c>).</summary>
    event EventHandler<string>? McpStatusReceived
    {
        add { }
        remove { }
    }

    /// <summary>Sends an edited buffer back to the MOO that handed it out. Throws when MCP editing is not agreed.</summary>
    ValueTask SendMooEditAsync(MooEdit edit, IReadOnlyList<string> lines) =>
        ValueTask.FromException(new InvalidOperationException("This session cannot send MOO edits."));

    /// <summary>Connects the transport and begins negotiation and the receive loop.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a command line to the server (a trailing CR/LF is appended).</summary>
    ValueTask SendLineAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Sends raw bytes to the server (IAC bytes are escaped by the telnet layer).</summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);

    /// <summary>Sends a GMCP message.</summary>
    ValueTask SendGmcpAsync(string package, string json, CancellationToken cancellationToken = default);

    /// <summary>Sends one MSDP variable and value, e.g. <c>REPORT</c> <c>HEALTH</c>.</summary>
    ValueTask SendMsdpAsync(string variable, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports the terminal window size via NAWS. Before the server enables NAWS, retains the latest
    /// size and reports it automatically when negotiation completes; unchanged sizes are not resent.
    /// </summary>
    ValueTask SetWindowSizeAsync(int width, int height);

    /// <summary>Closes the session.</summary>
    Task DisconnectAsync();
}
