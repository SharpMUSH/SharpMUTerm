using System.Text;
using System.Text.Json;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Protocols;

/// <summary>What a world does with GMCP <c>Comm.Channel.Text</c> (F5 ▸ <c>capture channels</c>).</summary>
public enum ChannelCaptureMode
{
    /// <summary>Nothing: channel text reaches only the main window, as the server prints it. The default.</summary>
    Off,

    /// <summary>Each channel's text is also put in a window of its own; the main window keeps its copy.</summary>
    Copy,

    /// <summary>Each channel's text goes to its window, and the line the server prints for it is kept out of the main window.</summary>
    Move,
}

/// <summary>
/// GMCP channel capture (IRE's and Aardwolf's <c>Comm.Channel.Text</c>): the message is turned into a line for a spawn
/// window named after its channel, through the same <c>SpawnLine</c> a capture rule raises, so the restore log, ⌃F, the
/// unread badge and the activity bar treat it as any captured line. With <see cref="ChannelCaptureMode.Move"/> the line
/// the server prints for the same message is gagged from the main window by <see cref="TakeEcho"/>.
/// <para>
/// <b>The gag matches text, not position</b>: the server prints the channel line and sends the GMCP message separately,
/// and neither carries an id. A printed line whose visible text equals a message's within <see cref="EchoWindow"/> of it
/// is taken as that message's echo and is consumed once. IRE and Aardwolf send the GMCP first; a server that prints first
/// keeps its main-window copy, which is the safe way for this to be wrong.
/// </para>
/// </summary>
public sealed class ChannelCapture
{
    /// <summary>How long a message waits for its printed echo.</summary>
    public static readonly TimeSpan EchoWindow = TimeSpan.FromSeconds(3);

    /// <summary>The most messages waiting for an echo at once.</summary>
    public const int MaxPending = 32;

    /// <summary>The longest channel name kept for a window title.</summary>
    public const int MaxChannelLength = 24;

    /// <summary>The longest message text read.</summary>
    public const int MaxTextLength = 4096;

    private readonly object _gate = new();
    private readonly LinkedList<(string Text, DateTimeOffset At)> _pending = new();
    private readonly TimeProvider _clock;

    public ChannelCapture(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;

    /// <summary>
    /// The window a <c>Comm.Channel.Text</c> payload belongs to and the lines it shows there, or null for a payload that
    /// is not one (no channel, no text, not an object). The text may carry ANSI colour, which is kept.
    /// </summary>
    public static (string Window, IReadOnlyList<StyledLine> Lines)? Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Text(root, "channel") is not { } channel
                || Text(root, "text") is not { } text)
            {
                return null;
            }

            var window = WindowName(channel);
            if (window.Length == 0)
            {
                return null;
            }

            if (text.Length > MaxTextLength)
            {
                text = text[..MaxTextLength];
            }

            var parser = new AnsiParser();
            var lines = new List<StyledLine>(parser.Feed(text.Replace("\r", string.Empty, StringComparison.Ordinal)));
            if (parser.Flush() is { } last)
            {
                lines.Add(last);
            }

            while (lines.Count > 0 && lines[^1].Text.Trim().Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            return lines.Count == 0 ? null : (window, lines);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// A channel name as a window title: letters, digits, spaces, <c>-</c> and <c>_</c> only, capped, and with no
    /// leading or trailing space — it names a window, and is server text.
    /// </summary>
    public static string WindowName(string channel)
    {
        var name = new StringBuilder(Math.Min(channel.Length, MaxChannelLength));
        foreach (var c in channel)
        {
            if (name.Length >= MaxChannelLength)
            {
                break;
            }

            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_')
            {
                name.Append(c);
            }
        }

        return name.ToString().Trim();
    }

    /// <summary>Notes a message whose printed echo is to be kept out of the main window.</summary>
    public void ExpectEcho(IEnumerable<StyledLine> lines)
    {
        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            foreach (var line in lines)
            {
                var text = line.Text.Trim();
                if (text.Length > 0)
                {
                    _pending.AddLast((text, now));
                }
            }

            while (_pending.Count > MaxPending)
            {
                _pending.RemoveFirst();
            }
        }
    }

    /// <summary>Whether <paramref name="printed"/> is the echo of a message still waiting; if so it is consumed.</summary>
    public bool TakeEcho(string printed)
    {
        var text = printed.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            while (_pending.First is { } oldest && now - oldest.Value.At > EchoWindow)
            {
                _pending.RemoveFirst();
            }

            for (var node = _pending.First; node is not null; node = node.Next)
            {
                if (string.Equals(node.Value.Text, text, StringComparison.Ordinal))
                {
                    _pending.Remove(node);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether <paramref name="printed"/> is the echo of a message still waiting, without consuming it.</summary>
    public bool Expects(string printed)
    {
        var text = printed.Trim();
        if (text.Length == 0)
        {
            return false;
        }

        var now = _clock.GetUtcNow();
        lock (_gate)
        {
            foreach (var (pending, at) in _pending)
            {
                if (now - at <= EchoWindow && string.Equals(pending, text, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Forgets every message waiting for an echo (a new connection).</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _pending.Clear();
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
