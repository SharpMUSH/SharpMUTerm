namespace SharpMUTerm.Core.Avatars;

/// <summary>
/// Where a character's or a world's picture comes from (<see cref="Configuration.CharacterDefinition.Avatar"/>,
/// <see cref="Configuration.WorldDefinition.Avatar"/>): an absolute path to an image file on this machine, a path under
/// the home folder written <c>~/…</c>, or an <c>https://</c> URL. Nothing else is read.
/// <para>
/// <b>The player typed it, and a shared configuration or an import may carry it</b>, so the rules are narrow: no relative
/// path (it would mean something different from every working folder), no <c>file:</c>, <c>http:</c> or <c>data:</c>, no
/// user information in a URL, no control characters, a picture's own extension on a path, and a bounded length. Reading
/// is bounded too (<see cref="MaxBytes"/>, counted while streaming) and times out (<see cref="Timeout"/>).
/// </para>
/// </summary>
public static class AvatarSource
{
    /// <summary>The longest source accepted, in characters.</summary>
    public const int MaxLength = 2048;

    /// <summary>The most bytes a picture may be, read or downloaded.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>How long a download may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>The extensions a file must have (what the client can decode).</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    /// <summary>
    /// Why <paramref name="source"/> is refused, or null when it is accepted. Blank is accepted: it means no picture. A
    /// file is checked to exist, so a mistyped path is said at once rather than shown as a letter for ever.
    /// </summary>
    public static string? Problem(string? source)
    {
        var text = source?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length > MaxLength)
        {
            return $"a picture's address is at most {MaxLength} characters";
        }

        if (text.Any(char.IsControl))
        {
            return "a picture's address cannot contain control characters";
        }

        if (text.Contains("://", StringComparison.Ordinal) || text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return "a picture on the web needs an https:// address";
            }

            return uri.UserInfo.Length > 0 ? "a picture's address cannot carry a user name or password" : null;
        }

        if (ExpandPath(text) is not { } path)
        {
            return "use a full path to an image file, or an https:// address";
        }

        if (!Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            return "the file needs to be a picture: " + string.Join(", ", Extensions);
        }

        return File.Exists(path) ? null : "there is no file there";
    }

    /// <summary>Whether <paramref name="source"/> names a picture on the web rather than a file.</summary>
    public static bool IsWeb(string source) =>
        source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The absolute path <paramref name="source"/> names (<c>~/</c> expanded), or null when it is not a full path. A
    /// path with <c>..</c> in it is resolved; one that is relative is refused rather than read against the working folder.
    /// </summary>
    public static string? ExpandPath(string source)
    {
        var text = source.Trim();
        if (text.StartsWith("~/", StringComparison.Ordinal) || text.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(home))
            {
                return null;
            }

            text = Path.Combine(home, text[2..]);
        }

        if (!Path.IsPathFullyQualified(text))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static readonly Lazy<HttpClient> Shared = new(() => new HttpClient(new SocketsHttpHandler
    {
        MaxAutomaticRedirections = 5,
        ConnectTimeout = Timeout,
    })
    {
        Timeout = Timeout,
    });

    /// <summary>
    /// The live reader: <see cref="LoadAsync(string, HttpClient?, CancellationToken)"/> with one shared client, made the
    /// first time a picture on the web is asked for.
    /// </summary>
    public static Task<byte[]?> LoadAsync(string source, CancellationToken cancellationToken) =>
        LoadAsync(source, IsWeb(source.Trim()) ? Shared.Value : null, cancellationToken);

    /// <summary>
    /// Reads the picture's bytes: the file, or a download over https. Null when it is refused, missing, too large, not
    /// answered in time, or not an image. Never throws for any of those.
    /// </summary>
    public static async Task<byte[]?> LoadAsync(string source, HttpClient? http, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (Problem(source) is not null)
        {
            return null;
        }

        try
        {
            if (IsWeb(source.Trim()))
            {
                return http is null ? null : await DownloadAsync(new Uri(source.Trim()), http, cancellationToken).ConfigureAwait(false);
            }

            var path = ExpandPath(source)!;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes)
            {
                return null;
            }

            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException
                                       or OperationCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<byte[]?> DownloadAsync(Uri uri, HttpClient http, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode
            || response.RequestMessage?.RequestUri is { } landed && landed.Scheme != Uri.UriSchemeHttps
            || response.Content.Headers.ContentLength > MaxBytes
            || response.Content.Headers.ContentType?.MediaType is { } type && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
