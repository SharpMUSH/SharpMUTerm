using System.Net.Sockets;
using System.Text;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Links;

namespace SharpMUTerm.Tui;

/// <summary>
/// Hands a <c>telnet://</c> link to a client that is already running, so a click in a browser opens a window in the
/// client the player has open rather than a second one beside it. A launch that finds nobody listening runs as a
/// client of its own and listens from then on, so the second instance always works; this only saves it.
/// <para>
/// The channel is a Unix domain socket (Windows 10 and later have them too) in a folder only the user can reach:
/// <c>$XDG_RUNTIME_DIR/sharpmuterm</c> where there is one, else the folder <c>config.json</c> is in. The socket file
/// is made owner-only as well. What crosses it is one line holding the link, at most
/// <see cref="TelnetLink.MaxLength"/> bytes, and the receiver reads it as the untrusted text it is
/// (<see cref="SharpMUTermApp.OpenLink(string)"/> runs it through <see cref="TelnetLink"/> again): whoever writes to
/// the socket can do what clicking a link does, and nothing more.
/// </para>
/// <para>
/// A socket file nobody answers on is a client that ended without removing it; it is deleted and taken over. The
/// running client answers <c>ok</c> once it has the link, so a sender never exits on a link nobody took.
/// </para>
/// </summary>
internal sealed class LinkHandoff : IDisposable
{
    private const string Acknowledgement = "ok";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    private readonly Socket _listener;
    private readonly string _path;
    private readonly CancellationTokenSource _stop = new();

    private LinkHandoff(Socket listener, string path)
    {
        _listener = listener;
        _path = path;
    }

    /// <summary>Where the socket is: in the user's runtime folder, else beside the configuration.</summary>
    public static string SocketPath()
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var folder = !OperatingSystem.IsWindows() && !string.IsNullOrEmpty(runtime) && Directory.Exists(runtime)
            ? Path.Combine(runtime, "sharpmuterm")
            : Path.GetDirectoryName(ConfigurationStore.DefaultPath)!;
        return Path.Combine(folder, "links.sock");
    }

    /// <summary>
    /// Gives <paramref name="link"/> to a running client. True once that client has said it has it; false when no
    /// client is listening, or it did not answer, and this launch should run the link itself.
    /// </summary>
    public static bool TrySend(string link, string? path = null)
    {
        path ??= SocketPath();
        if (!File.Exists(path) || link.Length > TelnetLink.MaxLength || link.Contains('\n'))
        {
            return false;
        }

        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.ReceiveTimeout = (int)Patience.TotalMilliseconds;
            socket.SendTimeout = (int)Patience.TotalMilliseconds;
            socket.Connect(new UnixDomainSocketEndPoint(path));
            socket.Send(Encoding.ASCII.GetBytes(link + "\n"));
            var reply = new byte[8];
            var read = socket.Receive(reply);
            return Encoding.ASCII.GetString(reply, 0, read).StartsWith(Acknowledgement, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is SocketException or IOException or ObjectDisposedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Starts listening for links from later launches, raising <paramref name="received"/> on a pool thread with each
    /// line as it arrived (unparsed). Null when another client is already listening, or the socket cannot be made —
    /// later launches then run their links themselves.
    /// </summary>
    public static LinkHandoff? Listen(Action<string> received, string? path = null)
    {
        path ??= SocketPath();
        try
        {
            var folder = Path.GetDirectoryName(path)!;
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            if (File.Exists(path))
            {
                if (Answers(path))
                {
                    return null;
                }

                File.Delete(path);
            }

            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            listener.Listen(4);
            var handoff = new LinkHandoff(listener, path);
            _ = Task.Run(() => handoff.AcceptAsync(received));
            return handoff;
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException or PlatformNotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static bool Answers(string path)
    {
        try
        {
            using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            probe.Connect(new UnixDomainSocketEndPoint(path));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task AcceptAsync(Action<string> received)
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket connection;
            try
            {
                connection = await _listener.AcceptAsync(_stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                using (connection)
                {
                    if (await ReadLineAsync(connection) is { } line)
                    {
                        received(line);
                        try
                        {
                            await connection.SendAsync(Encoding.ASCII.GetBytes(Acknowledgement + "\n"));
                        }
                        catch (Exception e) when (e is SocketException or ObjectDisposedException)
                        {
                            // The sender gave up waiting; the link is taken all the same.
                        }
                    }
                }
            });
        }
    }

    /// <summary>One line, bounded in length and in time; null for anything else.</summary>
    private async Task<string?> ReadLineAsync(Socket connection)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(Patience);
        var buffer = new byte[TelnetLink.MaxLength + 2];
        var filled = 0;
        try
        {
            while (filled < buffer.Length)
            {
                var read = await connection.ReceiveAsync(buffer.AsMemory(filled), timeout.Token);
                if (read == 0)
                {
                    break;
                }

                var newline = Array.IndexOf(buffer, (byte)'\n', filled, read);
                filled += read;
                if (newline >= 0)
                {
                    return Encoding.ASCII.GetString(buffer, 0, newline);
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        return null;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        _stop.Dispose();
    }
}
