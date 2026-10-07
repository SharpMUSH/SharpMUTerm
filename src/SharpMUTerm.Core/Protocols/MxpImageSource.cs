namespace SharpMUTerm.Core.Protocols;

/// <summary>
/// Where an MXP <c>&lt;IMAGE&gt;</c> lives. The spec: "The classname is appended to the URL, along with
/// the name of the graphics file itself" — so <c>URL=http://h/img/ T=maps map.png</c> is
/// <c>http://h/img/maps/map.png</c>.
/// </summary>
public static class MxpImageSource
{
    /// <summary>Longest name a placeholder will show before eliding.</summary>
    public const int MaxNameLength = 48;

    /// <summary>
    /// The absolute URL to fetch, or null when there is none this client may fetch. Only <c>http</c>,
    /// <c>https</c> and a <c>data:</c> file name are answered: an image with no <c>URL=</c> names a file
    /// in a local media directory, which this client does not have, and any other scheme is a world
    /// choosing what this machine opens.
    /// </summary>
    public static string? Resolve(string fileName, string? url, string? type)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var name = fileName.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        if (name.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(url) ? name : null;
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            return IsWeb(name, out var absolute) ? absolute : null;
        }

        var basePath = EnsureSlash(url.Trim());
        if (!string.IsNullOrWhiteSpace(type))
        {
            basePath = EnsureSlash(basePath + type.Trim().Trim('/'));
        }

        if (!Uri.TryCreate(basePath, UriKind.Absolute, out var baseUri) || !IsWebScheme(baseUri))
        {
            return null;
        }

        return Uri.TryCreate(baseUri, name, out var full) && IsWebScheme(full) ? full.AbsoluteUri : null;
    }

    /// <summary>What a placeholder calls the picture: the last segment of its file name, bounded.</summary>
    public static string DisplayName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var name = fileName.Trim();
        if (name.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || name.Length == 0)
        {
            return "image";
        }

        var query = name.IndexOfAny(['?', '#']);
        if (query >= 0)
        {
            name = name[..query];
        }

        var slash = name.TrimEnd('/').LastIndexOf('/');
        if (slash >= 0)
        {
            name = name[(slash + 1)..].TrimEnd('/');
        }

        if (name.Length == 0)
        {
            return "image";
        }

        return name.Length <= MaxNameLength ? name : name[..(MaxNameLength - 1)] + "…";
    }

    private static string EnsureSlash(string s) => s.EndsWith('/') ? s : s + "/";

    private static bool IsWeb(string text, out string absolute)
    {
        absolute = string.Empty;
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && IsWebScheme(uri))
        {
            absolute = uri.AbsoluteUri;
            return true;
        }

        return false;
    }

    private static bool IsWebScheme(Uri uri) => uri.Scheme is "http" or "https";
}
