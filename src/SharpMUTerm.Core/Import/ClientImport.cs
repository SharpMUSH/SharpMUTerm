using System.Text;

namespace SharpMUTerm.Core.Import;

/// <summary>
/// The one way in to the readers: a path (a file, or a Mudlet profile folder) or text already in memory, with the
/// format told apart by what the content is rather than by the file's name. Nothing here writes anything.
/// </summary>
public static class ClientImport
{
    /// <summary>
    /// Reads whatever <paramref name="path"/> names: a Mudlet profile folder, a zip (a Mudlet <c>.mpackage</c>), or a
    /// MUSHclient, Mudlet or TinTin++ file. The size is checked before a byte is read.
    /// </summary>
    public static (ImportResult? Result, string? Problem) Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            var full = Path.GetFullPath(path);
            if (Directory.Exists(full))
            {
                return MudletImport.ReadProfile(full);
            }

            var file = new FileInfo(full);
            if (!file.Exists)
            {
                return (null, "there is no file or folder there");
            }

            if (file.Length > ImportLimits.MaxFileBytes)
            {
                return (null, $"that file is larger than {ImportLimits.MaxFileBytes / (1024 * 1024)} MB, too large to be a client's setup");
            }

            var bytes = File.ReadAllBytes(file.FullName);
            if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4)
            {
                return MudletImport.ReadZip(bytes, file.Name);
            }

            var (result, problem) = Parse(Decode(bytes), file.Name);
            if (result is { Format: ImportFormat.Mudlet })
            {
                MudletImport.AddProfileWorldFor(file.FullName, result);
            }

            return (result, problem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return (null, "it could not be read: " + ex.Message);
        }
    }

    /// <summary>Reads text whose format is told by its content.</summary>
    public static (ImportResult? Result, string? Problem) Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(source);
        if (MudletImport.Recognises(text))
        {
            return MudletImport.Parse(text, source);
        }

        if (MushclientImport.Recognises(text))
        {
            return MushclientImport.Parse(text, source);
        }

        if (TinTinImport.Recognises(text))
        {
            return TinTinImport.Parse(text, source);
        }

        return (null, $"not a setup this client can import (it reads {ImportFormats.Listed})");
    }

    /// <summary>UTF-8 unless it says otherwise: a byte-order mark for UTF-16, else Latin-1 when it is not valid UTF-8.</summary>
    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
