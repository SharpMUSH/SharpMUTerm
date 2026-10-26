using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SharpMUTerm.Core.Presence;

/// <summary>
/// Shows a <see cref="GamePresence"/> through the Discord desktop app's local IPC: the <c>discord-ipc-N</c> socket
/// (a named pipe on Windows), a handshake naming the game's application, then <c>SET_ACTIVITY</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing leaves the machine from here.</b> The socket is the Discord app's own, on this computer; with no
/// Discord running there is nothing to connect to and nothing happens. The application id is the game's, from
/// its <c>External.Discord.Info</c>: this client registers none of its own.
/// </para>
/// <para>
/// <b><see cref="Show"/> never blocks.</b> It records the latest presence and wakes a worker, which sends only the
/// newest one, so a game that updates its status every second costs one write per second at most and the
/// client's own thread never waits on a socket. A failed connect is not retried until the next update.
/// </para>
/// </remarks>
public sealed class DiscordIpc : IPresence
{
    private const int Handshake = 0;
    private const int Frame = 1;
    private const int MaxReply = 64 * 1024;

    private readonly Func<CancellationToken, Task<Stream?>> _connect;
    private readonly ILogger _logger;
    private readonly int _pid;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Task _worker;
    private (GamePresence? Presence, bool Set) _pending;
    private Stream? _stream;
    private string? _connectedFor;

    /// <param name="connect">Opens the IPC stream, or returns null when there is none. Defaults to the Discord app's.</param>
    /// <param name="logger">Where a failure is noted; never the presence itself.</param>
    /// <param name="pid">The process Discord attributes the activity to.</param>
    public DiscordIpc(Func<CancellationToken, Task<Stream?>>? connect = null, ILogger? logger = null, int? pid = null)
    {
        _connect = connect ?? ConnectToDiscordAsync;
        _logger = logger ?? NullLogger.Instance;
        _pid = pid ?? Environment.ProcessId;
        _worker = Task.Run(RunAsync);
    }

    public void Show(GamePresence? presence)
    {
        lock (_gate)
        {
            _pending = (presence, true);
        }

        _wake.Release();
    }

    private async Task RunAsync()
    {
        var token = _stop.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await _wake.WaitAsync(token).ConfigureAwait(false);
                GamePresence? presence;
                lock (_gate)
                {
                    if (!_pending.Set)
                    {
                        continue;
                    }

                    presence = _pending.Presence;
                    _pending = default;
                }

                await SendAsync(presence, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SendAsync(GamePresence? presence, CancellationToken token)
    {
        try
        {
            if (presence is null)
            {
                if (_stream is not null)
                {
                    await WriteAsync(Frame, Activity(null), token).ConfigureAwait(false);
                    await ReadReplyAsync(token).ConfigureAwait(false);
                }

                return;
            }

            if (_stream is null || _connectedFor != presence.ApplicationId)
            {
                Close();
                _stream = await _connect(token).ConfigureAwait(false);
                if (_stream is null)
                {
                    return;
                }

                _connectedFor = presence.ApplicationId;
                await WriteAsync(Handshake, new JsonObject { ["v"] = 1, ["client_id"] = presence.ApplicationId }, token).ConfigureAwait(false);
                await ReadReplyAsync(token).ConfigureAwait(false);
            }

            await WriteAsync(Frame, Activity(presence), token).ConfigureAwait(false);
            await ReadReplyAsync(token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException or TimeoutException or InvalidDataException or ObjectDisposedException)
        {
            _logger.LogDebug(ex, "Discord presence was not updated.");
            Close();
        }
    }

    /// <summary>The <c>SET_ACTIVITY</c> command for a presence, or the one that clears it.</summary>
    internal JsonObject Activity(GamePresence? presence)
    {
        JsonObject? activity = null;
        if (presence is not null)
        {
            activity = new JsonObject();
            if (presence.Details is { } details)
            {
                activity["details"] = details;
            }

            if (presence.State is { } state)
            {
                activity["state"] = state;
            }

            if (presence.StartTime is { } start)
            {
                activity["timestamps"] = new JsonObject { ["start"] = start.ToUnixTimeSeconds() };
            }

            var assets = new JsonObject();
            if (presence.Game is { } game)
            {
                assets["large_text"] = game;
            }

            if (presence.SmallImage is { } small)
            {
                assets["small_image"] = small;
                if (presence.SmallImageText is { } smallText)
                {
                    assets["small_text"] = smallText;
                }
            }

            if (assets.Count > 0)
            {
                activity["assets"] = assets;
            }

            if (presence is { PartySize: { } size and > 0, PartyMax: { } max } && max >= size)
            {
                activity["party"] = new JsonObject { ["size"] = new JsonArray(size, max) };
            }
        }

        return new JsonObject
        {
            ["cmd"] = "SET_ACTIVITY",
            ["args"] = new JsonObject { ["pid"] = _pid, ["activity"] = activity },
            ["nonce"] = Guid.NewGuid().ToString("N"),
        };
    }

    private async Task WriteAsync(int op, JsonObject body, CancellationToken token)
    {
        var payload = Encoding.UTF8.GetBytes(body.ToJsonString());
        var frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, 8);
        await _stream!.WriteAsync(frame, token).ConfigureAwait(false);
        await _stream.FlushAsync(token).ConfigureAwait(false);
    }

    /// <summary>Reads and discards one reply, so Discord's side never fills; bounded in size and time.</summary>
    private async Task ReadReplyAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        var header = new byte[8];
        try
        {
            await _stream!.ReadExactlyAsync(header, timeout.Token).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            if (length is < 0 or > MaxReply)
            {
                throw new InvalidDataException("Discord sent a reply this client will not read.");
            }

            await _stream.ReadExactlyAsync(new byte[length], timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("Discord did not answer.");
        }
        catch (EndOfStreamException ex)
        {
            throw new IOException("Discord closed the connection.", ex);
        }
    }

    private void Close()
    {
        _stream?.Dispose();
        _stream = null;
        _connectedFor = null;
    }

    /// <summary>The Discord app's IPC endpoint on this machine, or null when none answers.</summary>
    private static async Task<Stream?> ConnectToDiscordAsync(CancellationToken token)
    {
        for (var i = 0; i < 10; i++)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.ConnectAsync(200, token).ConfigureAwait(false);
                        return pipe;
                    }
                    catch
                    {
                        await pipe.DisposeAsync().ConfigureAwait(false);
                        throw;
                    }
                }

                foreach (var folder in SocketFolders())
                {
                    var path = Path.Combine(folder, $"discord-ipc-{i}");
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), token).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or SocketException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    /// <summary>Where Discord puts its socket on Linux and macOS, flatpak and snap installs included.</summary>
    private static IEnumerable<string> SocketFolders()
    {
        var roots = new[] { "XDG_RUNTIME_DIR", "TMPDIR", "TMP", "TEMP" }
            .Select(Environment.GetEnvironmentVariable)
            .Where(v => !string.IsNullOrEmpty(v))
            .Cast<string>()
            .Append("/tmp")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var root in roots)
        {
            yield return root;
            yield return Path.Combine(root, "app", "com.discordapp.Discord");
            yield return Path.Combine(root, "snap.discord");
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _worker.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
        }

        Close();
        _stop.Dispose();
        _wake.Dispose();
    }
}
