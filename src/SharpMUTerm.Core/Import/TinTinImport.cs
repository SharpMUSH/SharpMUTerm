using System.Globalization;
using System.Text;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Import;

/// <summary>
/// Reads a TinTin++ script: <c>#session</c> (a world), <c>#action</c> and <c>#gag</c> (triggers), <c>#alias</c>, and
/// <c>#ticker</c> (a timer), each by its full name or the prefix TinTin accepts (<c>#act</c>, <c>#ali</c>, <c>#ses</c>).
/// Every other <c>#</c> command is counted as left out.
/// <para>
/// A pattern's <c>%1</c>–<c>%99</c> and <c>%*</c> become captures, <c>%d</c> digits, <c>%w</c> a word, and a
/// leading <c>^</c> anchors it as in TinTin; one with TinTin's own <c>{regex}</c> blocks is left out. A command list
/// is split on <c>;</c> into one command per line, and a list with a <c>#</c> command in it is left out whole: those
/// are TinTin's own statements, which this client does not run.
/// </para>
/// </summary>
public static class TinTinImport
{
    private static readonly (string Name, int Shortest)[] Commands =
    [
        ("session", 3),
        ("action", 2),
        ("alias", 3),
        ("ticker", 4),
        ("gag", 3),
    ];

    /// <summary>Whether <paramref name="text"/> looks like a TinTin++ script.</summary>
    public static bool Recognises(string text) =>
        text.Split('\n').Take(400).Any(line => line.TrimStart().StartsWith('#')
            && Command(Word(line.TrimStart()[1..])) is not null);

    /// <summary>Reads <paramref name="text"/>.</summary>
    public static (ImportResult? Result, string? Problem) Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new ImportResult(ImportFormat.TinTin, source);
        var others = new SortedSet<string>(StringComparer.Ordinal);
        var otherCount = 0;
        foreach (var statement in Statements(text).Take(ImportLimits.MaxItems * 4))
        {
            switch (Command(statement.Command))
            {
                case "session":
                    ReadSession(statement.Arguments, result);
                    break;
                case "action":
                    ReadAction(statement.Arguments, result, gag: false);
                    break;
                case "gag":
                    ReadAction(statement.Arguments, result, gag: true);
                    break;
                case "alias":
                    ReadAlias(statement.Arguments, result);
                    break;
                case "ticker":
                    ReadTicker(statement.Arguments, result);
                    break;
                default:
                    otherCount++;
                    if (others.Count < 8 && statement.Command.Length is > 0 and < 20)
                    {
                        others.Add("#" + statement.Command.ToLowerInvariant());
                    }

                    break;
            }
        }

        if (otherCount > 0)
        {
            result.Skip(
                "command",
                $"{otherCount} other TinTin command{(otherCount == 1 ? string.Empty : "s")}",
                string.Join(" ", others) + " — TinTin's own statements are not run here");
        }

        return result.IsEmpty && result.SkippedCount == 0 ? (null, "no TinTin++ #session, #action, #alias or #ticker lines in that file") : (result, null);
    }

    /// <summary>The full command a typed word names (TinTin accepts any prefix down to a minimum), or null.</summary>
    private static string? Command(string word)
    {
        var lower = word.ToLowerInvariant();
        foreach (var (name, shortest) in Commands)
        {
            if (lower.Length >= shortest && name.StartsWith(lower, StringComparison.Ordinal))
            {
                return name;
            }
        }

        return null;
    }

    private static string Word(string text)
    {
        var end = 0;
        while (end < text.Length && char.IsAsciiLetter(text[end]))
        {
            end++;
        }

        return text[..end];
    }

    private sealed record Statement(string Command, IReadOnlyList<string> Arguments);

    /// <summary>
    /// The script's statements: a <c>#word</c> and its arguments, each a <c>{…}</c> group (nested, across lines) or a
    /// bare word, up to a newline or <c>;</c> outside any group.
    /// </summary>
    private static IEnumerable<Statement> Statements(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            // To the start of the next statement: a # at the start of a line or after a ;.
            while (i < text.Length && (char.IsWhiteSpace(text[i]) || text[i] == ';'))
            {
                i++;
            }

            if (i >= text.Length)
            {
                yield break;
            }

            if (text[i] != '#')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            i++;
            var start = i;
            while (i < text.Length && char.IsAsciiLetter(text[i]))
            {
                i++;
            }

            var command = text[start..i];
            var arguments = new List<string>();
            while (i < text.Length && text[i] != '\n' && text[i] != ';' && arguments.Count < 8)
            {
                if (text[i] is ' ' or '\t' or '\r')
                {
                    i++;
                }
                else if (text[i] == '{')
                {
                    var depth = 0;
                    var open = i;
                    for (; i < text.Length; i++)
                    {
                        if (text[i] == '{')
                        {
                            depth++;
                        }
                        else if (text[i] == '}' && --depth == 0)
                        {
                            break;
                        }
                    }

                    arguments.Add(text[(open + 1)..Math.Min(i, text.Length)]);
                    i++;
                }
                else
                {
                    var word = i;
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != ';')
                    {
                        i++;
                    }

                    arguments.Add(text[word..i]);
                }
            }

            yield return new Statement(command, arguments);
        }
    }

    private static void ReadSession(IReadOnlyList<string> arguments, ImportResult result)
    {
        var name = ImportText.Name(arguments.ElementAtOrDefault(0));
        if (result.Worlds.Count >= ImportLimits.MaxWorlds)
        {
            result.Skip("world", name, "past the limit of worlds in one file");
            return;
        }

        var host = ImportText.Host(arguments.ElementAtOrDefault(1));
        var port = ImportText.Port(arguments.ElementAtOrDefault(2));
        if (host is null || port is null)
        {
            result.Skip("world", name, "it does not name a host and a port");
            return;
        }

        if (result.Worlds.Any(w => w.Host == host && w.Port == port))
        {
            return;
        }

        result.Worlds.Add(new WorldDefinition { Name = name.Length > 0 ? name : host, Host = host, Port = port.Value });
    }

    /// <summary>
    /// A TinTin pattern as a regex: <c>%1</c>–<c>%99</c> become named captures (so a command's <c>%2</c> finds the right
    /// one whatever order they appear in), <c>%*</c> an unnamed one, <c>%d</c>/<c>%w</c> typed ones. Null when it uses
    /// TinTin's own regex blocks.
    /// </summary>
    internal static string? PatternToRegex(string pattern, bool anchorEnd = false)
    {
        if (pattern.Contains('{') || pattern.Contains('}'))
        {
            return null;
        }

        var builder = new StringBuilder();
        var i = 0;
        if (pattern.StartsWith('^'))
        {
            builder.Append('^');
            i = 1;
        }

        var end = pattern.Length;
        var anchored = anchorEnd;
        if (end > i && pattern[end - 1] == '$' && (end < 2 || pattern[end - 2] != '\\'))
        {
            end--;
            anchored = true;
        }

        var literal = new StringBuilder();
        var seen = new HashSet<int>();
        void Flush()
        {
            builder.Append(TriggerPatterns.Literal(literal.ToString()));
            literal.Clear();
        }

        for (; i < end; i++)
        {
            var c = pattern[i];
            if (c != '%' || i + 1 >= end)
            {
                literal.Append(c);
                continue;
            }

            var next = pattern[i + 1];
            var last = i + 2 >= end;
            if (char.IsAsciiDigit(next))
            {
                var j = i + 1;
                while (j < end && j < i + 3 && char.IsAsciiDigit(pattern[j]))
                {
                    j++;
                }

                var number = int.Parse(pattern[(i + 1)..j], CultureInfo.InvariantCulture);
                Flush();
                last = j >= end;
                var body = last ? ".*" : ".*?";
                builder.Append(seen.Add(number) ? $"(?<t{number}>{body})" : $"\\k<t{number}>");
                i = j - 1;
            }
            else if (next is '*' or 'd' or 'w' or 's' or 'D' or 'W' or 'S')
            {
                Flush();
                builder.Append(next switch
                {
                    '*' => last ? "(.*)" : "(.*?)",
                    'd' => "([0-9]+)",
                    'w' => "([A-Za-z]+)",
                    's' => "(\\s+)",
                    'D' => "([^0-9]+)",
                    'W' => "([^A-Za-z]+)",
                    _ => "(\\S+)",
                });
                i++;
            }
            else if (next == '%')
            {
                literal.Append('%');
                i++;
            }
            else
            {
                literal.Append(c);
            }
        }

        Flush();
        if (anchored)
        {
            builder.Append('$');
        }

        var regex = builder.ToString();
        return ImportText.Compiles(regex) ? regex : null;
    }

    /// <summary>
    /// A TinTin command list as this client's: split on <c>;</c> outside braces, one per line, <c>%N</c> becoming the
    /// matching capture. Null when it holds a <c>#</c> command.
    /// </summary>
    internal static string? CommandsToSubstitution(string commands, Func<int, string> reference)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var depth = 0;
        foreach (var c in commands)
        {
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth = Math.Max(0, depth - 1);
            }

            if (c == ';' && depth == 0)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else if (c != '\n' && c != '\r')
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString());
        var lines = new List<string>();
        foreach (var part in parts.Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            if (part.StartsWith('#'))
            {
                return null;
            }

            var line = new StringBuilder();
            for (var i = 0; i < part.Length; i++)
            {
                var c = part[i];
                if (c == '%' && i + 1 < part.Length && char.IsAsciiDigit(part[i + 1]))
                {
                    var j = i + 1;
                    while (j < part.Length && j < i + 3 && char.IsAsciiDigit(part[j]))
                    {
                        j++;
                    }

                    line.Append(reference(int.Parse(part[(i + 1)..j], CultureInfo.InvariantCulture)));
                    i = j - 1;
                }
                else if (c == '$')
                {
                    line.Append("$$");
                }
                else if (c is '{' or '}')
                {
                    // Braces only group in TinTin; they are not text.
                }
                else
                {
                    line.Append(c);
                }
            }

            lines.Add(line.ToString());
        }

        return string.Join('\n', lines);
    }

    private static void ReadAction(IReadOnlyList<string> arguments, ImportResult result, bool gag)
    {
        var raw = arguments.ElementAtOrDefault(0) ?? string.Empty;
        var name = ImportText.Name(raw);
        if (!result.Room("trigger", result.Set.Triggers.Count, name))
        {
            return;
        }

        if (ImportText.Body(raw) is not { Length: > 0 } pattern || pattern.Contains('\n'))
        {
            result.Skip("trigger", name, "its pattern is empty, too long or more than one line");
            return;
        }

        if (PatternToRegex(pattern) is not { } regex)
        {
            result.Skip("trigger", name, "it uses TinTin's own regex syntax");
            return;
        }

        string? send = null;
        if (!gag)
        {
            var commands = ImportText.Body(arguments.ElementAtOrDefault(1));
            send = commands is null ? null : CommandsToSubstitution(commands, n => n == 0 ? "$0" : $"${{t{n}}}");
            if (send is null)
            {
                result.Skip("trigger", name, commands is null ? "its commands are too long" : "it runs TinTin commands (#…), which are not run here");
                return;
            }

            if (send.Length == 0)
            {
                result.Skip("trigger", name, "it sends nothing");
                return;
            }

            // A reference to a capture the pattern does not have would be sent as the literal text.
            for (var n = 1; n < 100; n++)
            {
                if (send.Contains($"${{t{n}}}", StringComparison.Ordinal) && !regex.Contains($"<t{n}>", StringComparison.Ordinal))
                {
                    send = send.Replace($"${{t{n}}}", string.Empty, StringComparison.Ordinal);
                }
            }
        }

        result.Set.Triggers.Add(new Trigger
        {
            Name = (gag ? "gag " : string.Empty) + name,
            Pattern = regex,
            Enabled = true,
            CaseSensitive = true,
            StopProcessing = false,
            Actions = { Gag = gag, SendResponse = send },
        });
    }

    private static void ReadAlias(IReadOnlyList<string> arguments, ImportResult result)
    {
        var raw = arguments.ElementAtOrDefault(0) ?? string.Empty;
        var name = ImportText.Name(raw);
        if (!result.Room("alias", result.Set.Aliases.Count, name))
        {
            return;
        }

        var commands = ImportText.Body(arguments.ElementAtOrDefault(1));
        if (name.Length == 0 || commands is null)
        {
            result.Skip("alias", name, name.Length == 0 ? "it has no name" : "its commands are too long");
            return;
        }

        string? pattern;
        string? substitution;
        if (raw.Contains('%'))
        {
            // The pattern names its own arguments.
            pattern = PatternToRegex(raw.StartsWith('^') ? raw : "^" + raw, anchorEnd: true);
            substitution = pattern is null ? null : CommandsToSubstitution(commands, n => n == 0 ? "$0" : $"${{t{n}}}");
        }
        else if (raw.Any(char.IsWhiteSpace))
        {
            pattern = ImportText.ExactLine(raw);
            substitution = CommandsToSubstitution(commands, _ => string.Empty);
        }
        else
        {
            // A bare word: %0 is everything after it, %1–%3 its first three words.
            pattern = "^" + TriggerPatterns.Literal(raw)
                + @"(?:\s+(?<a0>(?<a1>\S+)(?:\s+(?<a2>\S+))?(?:\s+(?<a3>\S+))?.*))?$";
            substitution = CommandsToSubstitution(commands, n => n <= 3 ? $"${{a{n}}}" : "\u0000");
            if (substitution?.Contains('\u0000') == true)
            {
                result.Skip("alias", name, "it uses more than three arguments");
                return;
            }
        }

        if (pattern is null || substitution is null || substitution.Length == 0)
        {
            result.Skip("alias", name, pattern is null
                ? "it uses TinTin's own regex syntax"
                : substitution is null ? "it runs TinTin commands (#…), which are not run here" : "it sends nothing");
            return;
        }

        result.Set.Aliases.Add(new Alias { Name = name, Pattern = pattern, CaseSensitive = true, Substitution = substitution });
    }

    private static void ReadTicker(IReadOnlyList<string> arguments, ImportResult result)
    {
        var name = ImportText.Name(arguments.ElementAtOrDefault(0));
        if (!result.Room("timer", result.Set.Timers.Count, name))
        {
            return;
        }

        var commands = ImportText.Body(arguments.ElementAtOrDefault(1));
        var send = commands is null ? null : CommandsToSubstitution(commands, _ => string.Empty);
        if (!double.TryParse(arguments.ElementAtOrDefault(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || seconds is < 0.1 or > 86400)
        {
            result.Skip("timer", name, "its interval is not between a tenth of a second and a day");
            return;
        }

        if (string.IsNullOrEmpty(send))
        {
            result.Skip("timer", name, send is null ? "it runs TinTin commands (#…), which are not run here" : "it sends nothing");
            return;
        }

        result.Set.Timers.Add(new TimerDefinition
        {
            Name = name,
            IntervalSeconds = seconds,
            Command = send.Replace("$$", "$", StringComparison.Ordinal),
        });
    }
}
