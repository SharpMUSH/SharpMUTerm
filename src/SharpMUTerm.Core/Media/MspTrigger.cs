using System.Globalization;

namespace SharpMUTerm.Core.Media;

/// <summary>
/// MSP's in-band sound requests: a line that is exactly <c>!!SOUND(name V=… L=… P=… T=… U=…)</c> or
/// <c>!!MUSIC(name V=… L=… C=… T=… U=…)</c>, read into the same <see cref="MediaCommand"/>s MCMP produces, so
/// one queue applies one set of rules.
/// <para>
/// <b>Only on a connection that agreed MSP.</b> The caller checks that; this reads text and cannot tell a
/// server's trigger from one a player typed into a channel, which on any other connection is all it is.
/// </para>
/// <para>
/// MSP's own defaults differ from MCMP's and are filled in here: volume 100, priority 50, one play, and music
/// continuing. A sound is <see cref="MediaRequest.Exclusive"/> — MSP plays one at a time and lets a new one
/// in only at a higher priority — and music shares one key, so a different file replaces the one playing.
/// <c>Off</c> stops that kind; <c>!!SOUND(Off U=…)</c> also sets the folder later names are fetched from.
/// </para>
/// <para>
/// Not read: wildcards in a name (MSP's "pick one of these at random"), which the cache refuses as a name,
/// and a trigger with text before or after it on its line, which stays text.
/// </para>
/// </summary>
public static class MspTrigger
{
    /// <summary>The key every MSP music request shares: MSP plays one tune at a time.</summary>
    public const string MusicKey = "msp:music";

    /// <summary>The command <paramref name="line"/> is, or null when it is not a whole MSP trigger.</summary>
    public static MediaCommand? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = line.Trim();
        MediaKind kind;
        if (text.StartsWith("!!SOUND(", StringComparison.OrdinalIgnoreCase))
        {
            kind = MediaKind.Sound;
        }
        else if (text.StartsWith("!!MUSIC(", StringComparison.OrdinalIgnoreCase))
        {
            kind = MediaKind.Music;
        }
        else
        {
            return null;
        }

        if (!text.EndsWith(')') || text.IndexOf(')') != text.Length - 1)
        {
            return null;
        }

        var words = text[8..^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return null;
        }

        var name = words[0];
        var fields = new Dictionary<char, string>();
        foreach (var word in words.Skip(1))
        {
            if (word.Length < 3 || word[1] != '=')
            {
                return null;
            }

            fields[char.ToUpperInvariant(word[0])] = word[2..];
        }

        return FromFields(kind, name, fields);
    }

    /// <summary>
    /// A sound or music request from its parts: a name and MSP's one-letter fields (<c>V L P C T U</c>). MXP's
    /// <c>&lt;SOUND&gt;</c> and <c>&lt;MUSIC&gt;</c> carry the same fields as attributes and come through here, so
    /// both protocols follow one set of defaults.
    /// </summary>
    public static MediaCommand FromFields(MediaKind kind, string name, IReadOnlyDictionary<char, string> fields)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(fields);
        var url = fields.GetValueOrDefault('U');
        if (string.Equals(name, "Off", StringComparison.OrdinalIgnoreCase))
        {
            // The specification's way to set the download folder without playing anything.
            return url is not null && kind == MediaKind.Sound
                ? new MediaCommand.SetDefault(url)
                : new MediaCommand.Stop(new MediaFilter { Kind = kind });
        }

        var request = new MediaRequest(name)
        {
            Url = url,
            Kind = kind,
            Tag = fields.GetValueOrDefault('T'),
            Volume = Math.Clamp(Number(fields, 'V') ?? 100, 1, 100),
            Loops = Number(fields, 'L') is { } loops and (-1 or > 0) ? loops : 1,
        };

        return new MediaCommand.Play(kind == MediaKind.Sound
            ? request with { Priority = Math.Clamp(Number(fields, 'P') ?? 50, 1, 100), Exclusive = true }
            : request with { Continue = Number(fields, 'C') != 0, Key = MusicKey });
    }

    private static int? Number(IReadOnlyDictionary<char, string> fields, char key) =>
        fields.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
}
