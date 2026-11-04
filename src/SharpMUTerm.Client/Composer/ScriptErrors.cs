using System.Globalization;
using System.Text.RegularExpressions;

namespace SharpMUTerm.Client.Composer;

/// <summary>
/// Reads the line a Lua load or run error names in a script file: MoonSharp says <c>corvid.lua:(14,22-30): …</c> (a chunk is
/// loaded under its file's name), and a plain <c>corvid.lua:14:</c> is read too. One reader, so ⌃P's list of scripts, the
/// line an opened script lands on and the line the editor marks (<see cref="ComposerEdit.ErrorLine"/>) are the same number.
/// </summary>
public static class ScriptErrors
{
    private static readonly Regex ErrorAt = new(
        @"(?<file>[A-Za-z0-9_.\-]+\.lua):\(?(?<line>\d+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// The line of script <paramref name="name"/> (folders and a <c>package:</c> prefix allowed; its file name is what is
    /// matched) that <paramref name="text"/> names, or null.
    /// </summary>
    public static int? LineIn(string? text, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var file = name[(name.LastIndexOf('/') + 1)..];
        try
        {
            foreach (Match match in ErrorAt.Matches(text))
            {
                if (string.Equals(match.Groups["file"].Value, file, StringComparison.Ordinal)
                    && int.TryParse(match.Groups["line"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var line)
                    && line > 0)
                {
                    return line;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        return null;
    }

    /// <summary>The line the newest of <paramref name="errors"/> (oldest first) names in <paramref name="name"/>, or null.</summary>
    public static int? LineIn(IReadOnlyList<string> errors, string name)
    {
        ArgumentNullException.ThrowIfNull(errors);
        for (var i = errors.Count - 1; i >= 0; i--)
        {
            if (LineIn(errors[i], name) is { } line)
            {
                return line;
            }
        }

        return null;
    }
}
