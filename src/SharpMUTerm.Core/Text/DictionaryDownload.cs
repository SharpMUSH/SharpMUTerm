using System.Security.Cryptography;

namespace SharpMUTerm.Core.Text;

/// <summary>
/// A Hunspell dictionary the client knows how to fetch: two files at pinned https addresses, each with the SHA-256 the
/// bytes must have. The address names a release tag, so what it serves cannot move; the hash is what makes that true.
/// </summary>
/// <param name="Name">The file name the pair is saved under (<c>en_US</c>), which is also what the composer calls it.</param>
/// <param name="Language">What a reader calls it (<c>English (US)</c>).</param>
/// <param name="Origin">Where it comes from, said in the consent text.</param>
/// <param name="Licence">Its licence, said in the consent text.</param>
/// <param name="Aff">The affix file.</param>
/// <param name="AffSha256">The affix file's SHA-256, lower-case hex.</param>
/// <param name="Dic">The word list.</param>
/// <param name="DicSha256">The word list's SHA-256, lower-case hex.</param>
/// <param name="Bytes">About how big the two are together, for the consent text.</param>
public sealed record DictionarySource(
    string Name,
    string Language,
    string Origin,
    string Licence,
    Uri Aff,
    string AffSha256,
    Uri Dic,
    string DicSha256,
    long Bytes);

/// <summary>
/// Fetches a spelling dictionary into the client's own <c>dictionaries/</c> folder, only when the player asks
/// (F1's ⌥W with no dictionary, ⌃P ▸ <em>Get a spelling dictionary</em>). Nothing is bundled and nothing is fetched at
/// start. Windows has no system Hunspell folder, so without this a roleplayer there never has spelling.
/// <para>
/// <b>Server text never meets the disk here, but the network does</b>, so the rules are the package index's: https
/// only, every file capped while it streams (<see cref="MaxFileBytes"/>), and the bytes checked against the pinned
/// SHA-256 before anything is written. The pair is written as <c>.new</c> files and renamed, the affix file first,
/// because <see cref="SpellChecker.Find"/> looks for a <c>.dic</c> with an <c>.aff</c> beside it: a pair half written
/// is never one it opens.
/// </para>
/// </summary>
public static class DictionaryDownload
{
    /// <summary>The most one file may be. The largest English word list is about 1.2 MB.</summary>
    public const long MaxFileBytes = 8L * 1024 * 1024;

    private const string Release = "https://raw.githubusercontent.com/LibreOffice/dictionaries/libreoffice-24.8.4.2/en/";

    private const string LibreOffice = "LibreOffice's dictionaries (release 24.8.4.2)";

    /// <summary>English (US): the default.</summary>
    public static DictionarySource EnglishUs { get; } = new(
        "en_US",
        "English (US)",
        LibreOffice,
        "SCOWL / Hunspell en_US: free to use, copy and change",
        new Uri(Release + "en_US.aff"),
        "e746c882dd6f303c2c46e7452804b9201115a6942cfeb15f18f8edf774d2e24e",
        new Uri(Release + "en_US.dic"),
        "f0b1a234bd178bdd01875b2a392a9647f888b8fe879f79c52aae62c2759b3647",
        555_000);

    /// <summary>English (GB), for a locale that asks for it.</summary>
    public static DictionarySource EnglishGb { get; } = new(
        "en_GB",
        "English (GB)",
        LibreOffice,
        "Marco A.G. Pinto's en_GB: LGPL / MPL",
        new Uri(Release + "en_GB.aff"),
        "04755f317851defd8b9f77fd7e5d60f495221fad926d0df346cd9b61b646bfc2",
        new Uri(Release + "en_GB.dic"),
        "e070c64007d0f6a3423d028a53da099934b5f967dc0ef6d01b631ec2119659d4",
        1_262_000);

    /// <summary>Every dictionary this client can fetch.</summary>
    public static IReadOnlyList<DictionarySource> Catalog { get; } = [EnglishUs, EnglishGb];

    /// <summary>
    /// The one to offer for <paramref name="language"/> (<c>en_GB</c>, <c>en-AU</c>): its own if there is one, else the
    /// US list — the only languages offered are English for now, and a reader of any other gets the US one named as
    /// such rather than nothing.
    /// </summary>
    public static DictionarySource For(string? language)
    {
        var wanted = (language ?? string.Empty).Replace('-', '_').Split('.')[0].ToUpperInvariant();
        return wanted is "EN_GB" or "EN_IE" or "EN_NZ" or "EN_AU" or "EN_ZA" or "EN_IN" ? EnglishGb : EnglishUs;
    }

    /// <summary>Whether <paramref name="folder"/> already holds a dictionary pair, read without opening it.</summary>
    public static bool Holds(string? folder)
    {
        if (folder is null)
        {
            return false;
        }

        try
        {
            return Directory.Exists(folder)
                && Directory.GetFiles(folder, "*.dic").Any(d => File.Exists(Path.ChangeExtension(d, ".aff")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Fetches <paramref name="source"/> with <paramref name="fetch"/> and writes it into <paramref name="folder"/>.
    /// Returns null when it is installed, or why not. <paramref name="fetch"/> is handed the address and the cap and
    /// returns the bytes, or null for a failed or over-long download; <see cref="DownloadAsync"/> is the real one.
    /// </summary>
    public static async Task<string?> InstallAsync(
        string folder,
        DictionarySource source,
        Func<Uri, long, CancellationToken, Task<byte[]?>> fetch,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fetch);

        var files = new List<(string Extension, byte[] Bytes)>();
        foreach (var (uri, sha, extension) in new[] { (source.Aff, source.AffSha256, ".aff"), (source.Dic, source.DicSha256, ".dic") })
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                return $"{uri} is not https";
            }

            byte[]? bytes;
            try
            {
                bytes = await fetch(uri, MaxFileBytes, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                return $"{source.Name}{extension} was not fetched: {ex.Message}";
            }

            if (bytes is null || bytes.Length > MaxFileBytes)
            {
                return $"{source.Name}{extension} was not fetched, or was too large";
            }

            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), sha, StringComparison.Ordinal))
            {
                return $"{source.Name}{extension} is not the file this client expects (its SHA-256 differs); nothing was written";
            }

            files.Add((extension, bytes));
        }

        try
        {
            Directory.CreateDirectory(folder);
            foreach (var (extension, bytes) in files)
            {
                var path = Path.Combine(folder, source.Name + extension);
                var staging = path + ".new";
                await File.WriteAllBytesAsync(staging, bytes, token).ConfigureAwait(false);
                File.Move(staging, path, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{source.Name} could not be written to {folder}: {ex.Message}";
        }

        return null;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Downloads <paramref name="uri"/> over https, stopping at <paramref name="cap"/> bytes.</summary>
    public static async Task<byte[]?> DownloadAsync(Uri uri, long cap, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > cap)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > cap)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
