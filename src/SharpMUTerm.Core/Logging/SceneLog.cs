using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Logging;

/// <summary>Which lines a scene log keeps.</summary>
public enum SceneLogContent
{
    /// <summary>Poses: lines that start with the name of someone present (<see cref="ScenePoses"/>). The default.</summary>
    Poses,

    /// <summary>Every line the window shows.</summary>
    Everything,
}

/// <summary>
/// A roleplay scene being recorded (⌃P ▸ <em>Start scene log</em>): one window's lines from now until it is stopped,
/// written through <see cref="HtmlLogSink"/> or <see cref="PlainTextLogSink"/> to a file of its own, under a header naming
/// the scene, when it started and who was present. It is not the session log, which is per character and kept running;
/// a scene is something a player starts and stops, and keeps.
/// </summary>
public sealed class SceneLog : IDisposable
{
    private readonly ILogSink _sink;
    private readonly IReadOnlyCollection<string> _names;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _closed;

    private SceneLog(ILogSink sink, string path, string windowId, string title, SceneLogContent content, IReadOnlyCollection<string> names, DateTimeOffset started)
    {
        _sink = sink;
        _names = names;
        Path = path;
        WindowId = windowId;
        Title = title;
        Content = content;
        Started = started;
    }

    /// <summary>The file being written.</summary>
    public string Path { get; }

    /// <summary>The window whose lines are recorded.</summary>
    public string WindowId { get; }

    /// <summary>The scene's title (the window's, unless one was given).</summary>
    public string Title { get; }

    public SceneLogContent Content { get; }

    public DateTimeOffset Started { get; }

    /// <summary>How many lines have been written.</summary>
    public int Lines { get; private set; }

    /// <summary>The names that started a pose so far, in no order.</summary>
    public IReadOnlyCollection<string> Posers
    {
        get
        {
            lock (_gate)
            {
                return _seen.ToArray();
            }
        }
    }

    /// <summary>
    /// Starts a scene log in <paramref name="folder"/>: <c>scene-&lt;title&gt;-&lt;yyyyMMdd-HHmmss&gt;.html</c> (or
    /// <c>.txt</c>), never overwriting a file that is there. Writes the header at once. Throws an
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> when the file cannot be made.
    /// </summary>
    /// <param name="present">Who is present: the room's roster and the player's own character. Also the names a pose starts with.</param>
    public static SceneLog Start(
        string folder,
        string windowId,
        string title,
        bool html,
        SceneLogContent content,
        IReadOnlyCollection<string> present,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Directory.CreateDirectory(folder);
        var stem = $"scene-{FileStem(title)}-{now.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        var extension = html ? ".html" : ".txt";
        var path = System.IO.Path.Combine(folder, stem + extension);
        for (var n = 2; File.Exists(path); n++)
        {
            path = System.IO.Path.Combine(folder, $"{stem}-{n}{extension}");
        }

        var heading = $"Scene: {title}";
        ILogSink sink = html ? HtmlLogSink.CreateFile(path, heading) : PlainTextLogSink.CreateFile(path, append: false);
        var log = new SceneLog(sink, path, windowId, title, content, present, now);
        sink.WriteSystem(heading);
        sink.WriteSystem($"Started {now.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        sink.WriteSystem(present.Count == 0 ? "Present: nobody the client knows of" : $"Present: {string.Join(", ", present)}");
        sink.WriteSystem(content == SceneLogContent.Poses ? "Poses only" : "Every line");
        sink.Flush();
        return log;
    }

    /// <summary>Offers a line the window showed; it is written when the scene keeps lines of its kind. Any thread.</summary>
    public bool Offer(StyledLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.IsPrompt || line.Text.Trim().Length == 0)
        {
            return false;
        }

        string? poser = ScenePoses.Poser(line.Text, _names);
        if (Content == SceneLogContent.Poses && poser is null)
        {
            return false;
        }

        lock (_gate)
        {
            if (_closed)
            {
                return false;
            }

            if (poser is not null)
            {
                _seen.Add(poser);
            }

            _sink.WriteLine(line);
            _sink.Flush();
            Lines++;
            return true;
        }
    }

    /// <summary>Writes the closing lines and closes the file.</summary>
    public void Stop(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _sink.WriteSystem($"Ended {now.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} · {Lines} line{(Lines == 1 ? string.Empty : "s")}"
                + (_seen.Count == 0 ? string.Empty : $" · posed: {string.Join(", ", _seen.Order(StringComparer.OrdinalIgnoreCase))}"));
            _sink.Dispose();
        }
    }

    public void Dispose() => Stop(DateTimeOffset.Now);

    /// <summary>A title as part of a file name: letters, digits and dashes, at most 40.</summary>
    internal static string FileStem(string title)
    {
        var stem = new StringBuilder();
        foreach (var c in title)
        {
            if (stem.Length >= 40)
            {
                break;
            }

            stem.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        var text = Regex.Replace(stem.ToString(), "-{2,}", "-").Trim('-');
        return text.Length == 0 ? "scene" : text;
    }
}

/// <summary>
/// What counts as a pose in a scene log: a line that starts with the name of someone present (<c>Ashlyn smiles.</c>,
/// <c>Ashlyn says, "…"</c>, <c>Ashlyn's eyes narrow.</c>), or a line in the shape a say takes (<c>Name says, "…"</c>).
/// Room descriptions, channel lines, system messages and the client's own lines start with no name and are left out.
/// </summary>
public static class ScenePoses
{
    private static readonly Regex SayShape = new(
        @"^([A-Z][\w'-]{1,30})\s+(says|asks|exclaims|whispers|shouts|murmurs|replies)\b.*""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    /// <summary>The name a pose starts with, or null when <paramref name="text"/> is not a pose.</summary>
    public static string? Poser(string text, IReadOnlyCollection<string> names)
    {
        var line = text.TrimStart();
        if (line.Length == 0 || line.StartsWith("***", StringComparison.Ordinal))
        {
            return null;
        }

        foreach (var name in names.OrderByDescending(n => n.Length))
        {
            if (name.Length > 0
                && line.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && (line.Length == name.Length || !char.IsLetterOrDigit(line[name.Length])))
            {
                return name;
            }
        }

        try
        {
            return SayShape.Match(line) is { Success: true } say ? say.Groups[1].Value : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }
}
