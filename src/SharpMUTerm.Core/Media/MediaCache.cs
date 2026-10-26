using System.Security.Cryptography;
using System.Text;

namespace SharpMUTerm.Core.Media;

/// <summary>
/// Where a world's sound files are kept: fetched once from the URL the server names, then read from disk.
/// <para>
/// <b>What may be fetched is narrow on purpose</b>, because the name and the URL are server text: http(s)
/// only (<c>WebImageLoader</c>'s rule); a name that is relative, has no <c>..</c> segment, no drive and no
/// backslash; one of four audio extensions; and a size cap per file and per world, enforced while reading, so
/// a server cannot fill the disk. A file is cached by a hash of its full URL, so a name never becomes a path.
/// </para>
/// <para>
/// A cache with no root is memory-only by construction and fetches nothing at all, which is what a snapshot
/// and a test get: the <c>mssp:</c> rule.
/// </para>
/// </summary>
public sealed class MediaCache
{
    /// <summary>The audio types this client fetches and plays.</summary>
    public static readonly IReadOnlyList<string> Extensions = ["wav", "mp3", "ogg", "flac"];

    private readonly string? _root;
    private readonly Func<Uri, long, CancellationToken, Task<byte[]?>> _fetch;

    public MediaCache(string? root = null, Func<Uri, long, CancellationToken, Task<byte[]?>>? fetch = null)
    {
        _root = string.IsNullOrWhiteSpace(root) ? null : root;
        _fetch = fetch ?? FetchAsync;
    }

    /// <summary>The largest file fetched.</summary>
    public long MaxFileBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>The most one world's cache holds; the oldest files go first past it.</summary>
    public long MaxWorldBytes { get; init; } = 512L * 1024 * 1024;

    /// <summary>
    /// Folders searched by name before anything is fetched — a soundpack the player installed. Their files are
    /// the player's, so they are read as they are and never fetched into.
    /// </summary>
    public IList<string> LocalFolders { get; } = new List<string>();

    /// <summary>Whether this cache can hold anything at all.</summary>
    public bool HasRoot => _root is not null;

    /// <summary>The cache folder beside a configuration file: <c>media/</c>.</summary>
    public static string PathFor(string configPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".", "media");

    /// <summary>Why a server's file name is refused, or null when it is acceptable.</summary>
    public static string? CheckName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            return "no file name";
        }

        if (name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || Path.IsPathRooted(name)
            || name.Split('/').Any(segment => segment is ".." or "." || segment.Length == 0)
            || name.Any(char.IsControl))
        {
            return "not a relative file name";
        }

        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        return Extensions.Contains(extension) ? null : $"not a sound file (.{extension})";
    }

    /// <summary>The full URL of <paramref name="name"/> under the folder <paramref name="folder"/>, or null.</summary>
    public static Uri? UrlFor(string name, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)
            || !Uri.TryCreate(folder.TrimEnd('/') + "/", UriKind.Absolute, out var root)
            || root.Scheme is not ("http" or "https"))
        {
            return null;
        }

        var path = string.Join('/', name.Split('/').Select(Uri.EscapeDataString));
        return Uri.TryCreate(root, path, out var full) && full.Scheme is "http" or "https" ? full : null;
    }

    /// <summary>
    /// The local path of <paramref name="name"/> for <paramref name="world"/>: a soundpack's copy, else the cached
    /// one, else fetched from <paramref name="folder"/> when <paramref name="download"/> allows. Null when there is
    /// no file to play; <paramref name="problem"/> says why, for the player.
    /// </summary>
    public async Task<(string? Path, string? Problem)> ResolveAsync(
        string world, string name, string? folder, bool download = true, CancellationToken cancellationToken = default)
    {
        if (CheckName(name) is { } refused)
        {
            return (null, refused);
        }

        foreach (var local in LocalFolders)
        {
            var candidate = Path.GetFullPath(Path.Combine(local, name));
            if (candidate.StartsWith(Path.GetFullPath(local), StringComparison.Ordinal) && File.Exists(candidate))
            {
                return (candidate, null);
            }
        }

        if (_root is null)
        {
            return (null, "sounds are not kept here");
        }

        if (UrlFor(name, folder) is not { } url)
        {
            return (null, folder is null ? "the game named no place to fetch it from" : "only http and https are fetched");
        }

        var directory = Path.Combine(_root, Sanitise(world));
        var path = Path.Combine(directory, Hash(url.AbsoluteUri) + Path.GetExtension(name).ToLowerInvariant());
        if (File.Exists(path))
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return (path, null);
        }

        if (!download)
        {
            return (null, "downloading is off for this world");
        }

        byte[]? bytes;
        try
        {
            bytes = await _fetch(url, MaxFileBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            return (null, $"could not fetch it: {ex.Message}");
        }

        if (bytes is null || bytes.Length == 0)
        {
            return (null, "could not fetch it");
        }

        if (bytes.Length > MaxFileBytes)
        {
            return (null, "the file is too large");
        }

        try
        {
            Directory.CreateDirectory(directory);
            MakeRoom(directory, bytes.Length);
            var temporary = path + ".part";
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
            return (path, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"could not keep it: {ex.Message}");
        }
    }

    private void MakeRoom(string directory, long incoming)
    {
        var files = new DirectoryInfo(directory).GetFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total + incoming <= MaxWorldBytes)
            {
                break;
            }

            total -= file.Length;
            file.Delete();
        }
    }

    private static string Sanitise(string world)
    {
        var builder = new StringBuilder();
        foreach (var c in world)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_');
        }

        var text = builder.ToString().Trim('.');
        return text.Length == 0 ? "_" : text;
    }

    private static string Hash(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Reads at most <paramref name="max"/> + 1 bytes, so an oversized file is refused without being held.</summary>
    private static async Task<byte[]?> FetchAsync(Uri url, long max, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > max)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }
}
