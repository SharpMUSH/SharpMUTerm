using System.Text;

namespace SharpMUTerm.Core.Text;

/// <summary>
/// Makes a piece of a file name out of text a player or a server chose — a world's name, a character's — so that the
/// name is legal on Windows, Linux and macOS alike.
/// <para>
/// A character on a MUSH whose name has a space is connected as <c>"first last"</c>, quotes included, and that is what
/// ends up in <see cref="Configuration.CharacterDefinition.Name"/>. Windows refuses a file name holding a quote, so the
/// session log for that character could not be made at all. Linux would take the quote, but the same configuration is
/// read on both, and a name should mean the same file on each.
/// </para>
/// <para>
/// The rule, in order: quotes and control characters are dropped (a quote is punctuation around the name, not part of
/// it); <c>&lt; &gt; : / \ | ? *</c> become <c>_</c>, so <c>a/b</c> and <c>ab</c> stay apart; leading spaces and
/// trailing spaces and dots go (Windows strips the trailing ones itself and then cannot find the file); the result is
/// cut to <see cref="MaxLength"/>; a Windows device name (<c>CON</c>, <c>NUL</c>, <c>COM1</c>, …, matched on the part
/// before the first dot, as Windows does) gets a <c>_</c> after it; and an empty result is the fallback.
/// <b>A name that needs none of this comes back unchanged</b>, so every log name a world could already make is the
/// same name it was.
/// </para>
/// </summary>
public static class PortableFileName
{
    /// <summary>The most characters a piece keeps; a name is a label, and a path has a length limit on Windows.</summary>
    public const int MaxLength = 64;

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// <paramref name="text"/> as a piece of a file name that every one of the three systems accepts, or
    /// <paramref name="fallback"/> when nothing is left of it.
    /// </summary>
    public static string Of(string? text, string fallback = "_")
    {
        ArgumentException.ThrowIfNullOrEmpty(fallback);
        if (string.IsNullOrEmpty(text))
        {
            return fallback;
        }

        var name = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '"' || char.IsControl(c))
            {
                continue;
            }

            name.Append(c is '<' or '>' or ':' or '/' or '\\' or '|' or '?' or '*' ? '_' : c);
        }

        var safe = name.ToString().TrimStart(' ');
        if (safe.Length > MaxLength)
        {
            // Never cut a surrogate pair in half: half a pair is not a character any file system will store.
            var cut = char.IsHighSurrogate(safe[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
            safe = safe[..cut];
        }

        safe = safe.TrimEnd(' ', '.');
        if (safe.Length == 0)
        {
            return fallback;
        }

        var dot = safe.IndexOf('.', StringComparison.Ordinal);
        var stem = (dot < 0 ? safe : safe[..dot]).TrimEnd(' ');
        return Reserved.Contains(stem) ? stem + "_" + safe[stem.Length..] : safe;
    }
}
