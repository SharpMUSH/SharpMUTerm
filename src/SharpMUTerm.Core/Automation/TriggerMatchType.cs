using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUTerm.Core.Automation;

/// <summary>
/// How a <see cref="Trigger.Pattern"/> is read. Every kind compiles to one .NET regex
/// (<see cref="TriggerPatterns.ToRegex"/>), so the engine, captures and the match timeout are the same
/// for all of them; what differs is only what the player has to type.
/// <para>
/// <see cref="Regex"/> is first, and so the default, because it is what every rule written before this
/// existed means: a configuration with no <c>matchType</c> reads exactly as it always did, which is why
/// the schema version did not move for it.
/// </para>
/// </summary>
public enum TriggerMatchType
{
    /// <summary>The pattern is a .NET regular expression, as it always was.</summary>
    Regex,

    /// <summary>The line contains the pattern as literal text, anywhere.</summary>
    Contains,

    /// <summary>The line starts with the pattern as literal text.</summary>
    StartsWith,

    /// <summary>
    /// Literal text in which each <c>*</c> stands for any run of characters (and is a capture, so
    /// <c>$1</c> names the first). It matches anywhere in the line, like <see cref="Contains"/>; <c>\*</c>
    /// is a literal asterisk.
    /// </summary>
    Wildcard,
}

/// <summary>
/// The one place a <see cref="TriggerMatchType"/> becomes a regex, and the plain words each is offered
/// under. Pure, so the engine, the F2 editor's live test and the suggestion from a selection all read
/// a pattern the same way.
/// </summary>
public static class TriggerPatterns
{
    /// <summary>
    /// The match kinds in the order the F2 editor offers them: the plain-language ones first, the raw
    /// regex last, because a player who has never written one should meet the three they can use before
    /// the one they cannot.
    /// </summary>
    public static IReadOnlyList<TriggerMatchType> Offered { get; } =
    [
        TriggerMatchType.Contains,
        TriggerMatchType.StartsWith,
        TriggerMatchType.Wildcard,
        TriggerMatchType.Regex,
    ];

    /// <summary>What a match kind is called on screen.</summary>
    public static string Describe(TriggerMatchType type) => type switch
    {
        TriggerMatchType.Contains => "contains the text",
        TriggerMatchType.StartsWith => "starts with",
        TriggerMatchType.Wildcard => "matches with * wildcards",
        _ => "regular expression",
    };

    /// <summary>The match kind a description names, case-insensitively, or null when it names none.</summary>
    public static TriggerMatchType? Parse(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        foreach (var type in Offered)
        {
            if (string.Equals(Describe(type), description.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>The regex <paramref name="pattern"/> means when read as <paramref name="type"/>.</summary>
    public static string ToRegex(TriggerMatchType type, string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return type switch
        {
            TriggerMatchType.Contains => Literal(pattern),
            TriggerMatchType.StartsWith => "^" + Literal(pattern),
            TriggerMatchType.Wildcard => WildcardToRegex(pattern),
            _ => pattern,
        };
    }

    /// <summary>
    /// Why <paramref name="pattern"/> cannot be used as <paramref name="type"/>, or null when it can. Only a
    /// regex can fail to parse; the literal kinds refuse only the empty pattern, which would match every
    /// line.
    /// </summary>
    public static string? Validate(TriggerMatchType type, string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length == 0)
        {
            return "pattern cannot be empty";
        }

        if (type == TriggerMatchType.Wildcard && pattern.All(c => c == '*' || char.IsWhiteSpace(c)))
        {
            return "a pattern of only * matches every line";
        }

        if (type != TriggerMatchType.Regex)
        {
            return null;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.None, AutomationDefaults.RegexMatchTimeout);
            return null;
        }
        catch (ArgumentException error)
        {
            return "not a valid regex: " + error.Message.Split('\n')[0].Trim();
        }
    }

    /// <summary>
    /// <paramref name="text"/> escaped to match itself. A space is left a space: <c>Regex.Escape</c> writes
    /// it as a backslash and a space, which is legal and is also the first thing a reader of the regex shown
    /// behind a plain pattern would stumble on — and without <c>IgnorePatternWhitespace</c> a bare space is
    /// literal anyway.
    /// </summary>
    public static string Literal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return System.Text.RegularExpressions.Regex.Escape(text).Replace("\\ ", " ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Literal text with each unescaped <c>*</c> turned into a capture. Every wildcard is lazy except a
    /// trailing one, which is greedy: a lazy capture at the very end of a pattern would match nothing at
    /// all, and <c>$1</c> of <c>* says *</c> would always be empty.
    /// </summary>
    internal static string WildcardToRegex(string pattern)
    {
        var parts = new List<string>();
        var literal = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                literal.Append('*');
                i++;
            }
            else if (c == '*')
            {
                parts.Add(literal.ToString());
                literal.Clear();
            }
            else
            {
                literal.Append(c);
            }
        }

        parts.Add(literal.ToString());

        var regex = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            regex.Append(Literal(parts[i]));
            if (i < parts.Count - 1)
            {
                regex.Append(i == parts.Count - 2 && parts[^1].Length == 0 ? "(.*)" : "(.*?)");
            }
        }

        return regex.ToString();
    }
}
