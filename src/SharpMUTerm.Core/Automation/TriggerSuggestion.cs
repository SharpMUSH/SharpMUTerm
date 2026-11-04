using System.Text;

namespace SharpMUTerm.Core.Automation;

/// <summary>
/// A trigger pattern proposed from a line of output somebody selected: the text, in
/// <see cref="TriggerMatchType.Wildcard"/> form, with its obviously variable parts turned into <c>*</c>.
/// </summary>
/// <param name="Name">A short name for the rule, from the first literal words of the pattern.</param>
/// <param name="Pattern">The pattern, read as <paramref name="MatchType"/>.</param>
/// <param name="MatchType">How <paramref name="Pattern"/> is read.</param>
public readonly record struct TriggerSuggestion(string Name, string Pattern, TriggerMatchType MatchType)
{
    /// <summary>The regex the pattern compiles to — what F2 shows behind the plain-language form.</summary>
    public string Regex => TriggerPatterns.ToRegex(MatchType, Pattern);

    /// <summary>The longest selection read; past it the rest is ignored rather than becoming a pattern.</summary>
    public const int MaxLength = 200;

    /// <summary>The longest name proposed.</summary>
    public const int MaxNameLength = 24;

    /// <summary>
    /// Proposes a pattern for <paramref name="selected"/>. Three kinds of run become a wildcard, because
    /// they are the parts of a line that are different next time:
    /// <list type="bullet">
    /// <item>numbers (<c>125</c>, <c>1,024</c>, <c>3.5</c>) — damage, gold, experience, counts;</item>
    /// <item>the inside of quoted speech — what was said, while the quotes and the verb stay;</item>
    /// <item>any of <paramref name="names"/> as a whole word — the players the game has told the client
    /// about (GMCP), so <c>Rivane tells you</c> becomes <c>* tells you</c>.</item>
    /// </list>
    /// Nothing else is guessed at. A pattern that would be nothing but wildcards is no pattern at all, so
    /// that selection is matched as literal text instead (<see cref="TriggerMatchType.Contains"/>).
    /// </summary>
    /// <param name="selected">The selected text. Only its first non-blank line is used.</param>
    /// <param name="names">Names to read as variable, or null for none.</param>
    public static TriggerSuggestion Suggest(string selected, IEnumerable<string>? names = null)
    {
        ArgumentNullException.ThrowIfNull(selected);

        var line = selected.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? string.Empty;
        if (line.Length > MaxLength)
        {
            line = line[..MaxLength];
        }

        var variable = new bool[line.Length];
        MarkQuoted(line, variable);
        MarkNumbers(line, variable);
        MarkNames(line, variable, names);

        var pattern = new StringBuilder();
        var literal = new StringBuilder();
        var wild = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (variable[i])
            {
                if (!wild)
                {
                    pattern.Append('*');
                    wild = true;
                }

                continue;
            }

            wild = false;

            if (line[i] == '*')
            {
                pattern.Append("\\*");
            }
            else
            {
                pattern.Append(line[i]);
            }

            literal.Append(line[i]);
        }

        if (literal.ToString().Trim().Length == 0)
        {
            return new TriggerSuggestion(NameFrom(line), line, TriggerMatchType.Contains);
        }

        return variable.Any(v => v)
            ? new TriggerSuggestion(NameFrom(literal.ToString()), pattern.ToString(), TriggerMatchType.Wildcard)
            : new TriggerSuggestion(NameFrom(line), line, TriggerMatchType.Contains);
    }

    /// <summary>The text between each pair of double quotes (straight or curly), not the quotes themselves.</summary>
    private static void MarkQuoted(string line, bool[] variable)
    {
        for (var i = 0; i < line.Length; i++)
        {
            var close = line[i] switch
            {
                '"' => '"',
                '“' => '”',
                _ => '\0',
            };
            if (close == '\0')
            {
                continue;
            }

            var end = line.IndexOf(close, i + 1);
            if (end < 0)
            {
                return;
            }

            for (var j = i + 1; j < end; j++)
            {
                variable[j] = true;
            }

            i = end;
        }
    }

    /// <summary>Runs of digits, with the separators inside a number (<c>1,024</c>, <c>3.5</c>) taken with them.</summary>
    private static void MarkNumbers(string line, bool[] variable)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (!char.IsAsciiDigit(line[i]))
            {
                continue;
            }

            var end = i;
            while (end + 1 < line.Length
                   && (char.IsAsciiDigit(line[end + 1])
                       || (line[end + 1] is ',' or '.' && end + 2 < line.Length && char.IsAsciiDigit(line[end + 2]))))
            {
                end++;
            }

            for (var j = i; j <= end; j++)
            {
                variable[j] = true;
            }

            i = end;
        }
    }

    /// <summary>Each name as a whole word, case-insensitively. Names shorter than two letters are ignored.</summary>
    private static void MarkNames(string line, bool[] variable, IEnumerable<string>? names)
    {
        if (names is null)
        {
            return;
        }

        foreach (var name in names.Where(n => n is { Length: >= 2 }).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var at = 0;
            while ((at = line.IndexOf(name, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = at + name.Length;
                var whole = (at == 0 || !char.IsLetterOrDigit(line[at - 1]))
                    && (end == line.Length || !char.IsLetterOrDigit(line[end]));
                if (whole)
                {
                    for (var j = at; j < end; j++)
                    {
                        variable[j] = true;
                    }
                }

                at = end;
            }
        }
    }

    /// <summary>The first few words of <paramref name="text"/>, as a rule name.</summary>
    private static string NameFrom(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(',', '.', ':', ';', '!', '?', '"', '\'', '“', '”', '(', ')', '[', ']'))
            .Where(w => w.Length > 0);
        var name = new StringBuilder();
        foreach (var word in words)
        {
            if (name.Length > 0 && name.Length + 1 + word.Length > MaxNameLength)
            {
                break;
            }

            if (name.Length > 0)
            {
                name.Append(' ');
            }

            name.Append(word.Length > MaxNameLength ? word[..MaxNameLength] : word);
        }

        return name.Length == 0 ? "New Trigger" : name.ToString();
    }
}
