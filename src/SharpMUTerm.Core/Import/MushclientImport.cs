using System.Text.RegularExpressions;
using System.Xml.Linq;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Import;

/// <summary>
/// Reads a MUSHclient world file (<c>.mcl</c>, the XML format MUSHclient has written since 3.x): the world's name,
/// host and port, its player, and its triggers, aliases, timers, function-key macros and keypad keys.
/// <para>
/// <b>Only what sends text is imported.</b> A rule whose <c>send_to</c> is the world (0) or the command processor (10)
/// becomes a rule here; one that sends to a script, a variable, the notepad or the output is left out and listed.
/// A <c>script="…"</c> function name, the file's <c>&lt;script&gt;</c> and its plugins are never imported and never
/// run: they are VBScript, Lua, JScript or Python written for MUSHclient's own object model.
/// </para>
/// <para>
/// MUSHclient matches a non-regex pattern against the <em>whole</em> line, each <c>*</c> a wildcard, so such a pattern
/// becomes an anchored regex — or, when it already starts and ends with <c>*</c>, this client's own wildcard kind,
/// which means the same thing.
/// </para>
/// </summary>
public static class MushclientImport
{
    private static readonly Regex FunctionKey = new(@"^((Ctrl|Shift|Alt)\+)*F([1-9]|1[0-9]|2[0-4])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether <paramref name="text"/> looks like a MUSHclient world file.</summary>
    public static bool Recognises(string text) => text.Contains("<muclient", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads <paramref name="text"/>, or says why it is not a MUSHclient world file.</summary>
    public static (ImportResult? Result, string? Problem) Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (ImportText.Xml(text) is not { Root: { } root } || !root.Name.LocalName.Equals("muclient", StringComparison.OrdinalIgnoreCase))
        {
            return (null, "not a MUSHclient world file (worlds saved before MUSHclient 3 are not XML and cannot be read)");
        }

        var result = new ImportResult(ImportFormat.MushClient, source);
        foreach (var world in root.Elements("world"))
        {
            ReadWorld(world, result);
        }

        foreach (var trigger in root.Elements("triggers").Elements("trigger").OrderBy(t => ImportText.Int(t, "sequence", 100)))
        {
            ReadTrigger(trigger, result);
        }

        foreach (var alias in root.Elements("aliases").Elements("alias").OrderBy(a => ImportText.Int(a, "sequence", 100)))
        {
            ReadAlias(alias, result);
        }

        foreach (var timer in root.Elements("timers").Elements("timer"))
        {
            ReadTimer(timer, result);
        }

        foreach (var macro in root.Elements("macros").Elements("macro"))
        {
            ReadMacro(macro, result);
        }

        foreach (var key in root.Elements("keypad").Elements("key"))
        {
            ReadKeypad(key, result);
        }

        foreach (var script in root.Elements("script"))
        {
            if (!string.IsNullOrWhiteSpace(script.Value))
            {
                result.Skip("script", "world script", "script code is never imported");
            }
        }

        foreach (var plugin in root.Elements("include").Concat(root.Elements("plugins").Elements()))
        {
            result.Skip("plugin", ImportText.Attr(plugin, "name") ?? ImportText.Attr(plugin, "plugin"), "plugins are never imported");
        }

        return (result, null);
    }

    private static void ReadWorld(XElement element, ImportResult result)
    {
        var name = ImportText.Name(ImportText.Attr(element, "name"));
        if (result.Worlds.Count >= ImportLimits.MaxWorlds)
        {
            result.Skip("world", name, "past the limit of worlds in one file");
            return;
        }

        var host = ImportText.Host(ImportText.Attr(element, "site"));
        var port = ImportText.Port(ImportText.Attr(element, "port")) ?? 4000;
        if (host is null)
        {
            result.Skip("world", name, "it names no host");
            return;
        }

        var world = new WorldDefinition { Name = name.Length > 0 ? name : host, Host = host, Port = port };
        var player = ImportText.Name(ImportText.Attr(element, "player"));
        if (player.Length > 0)
        {
            world.Characters.Add(new CharacterDefinition { Name = player });
            var password = ImportText.Attr(element, "password");
            if (!string.IsNullOrEmpty(password) && password.Length <= ImportLimits.MaxName)
            {
                result.Passwords.Add(new ImportedPassword(result.Worlds.Count, player, password));
            }
        }

        result.Worlds.Add(world);
    }

    /// <summary>The command a rule sends, or null (and the rule listed as left out) when it sends somewhere else.</summary>
    private static string? Send(XElement element, string kind, string name, ImportResult result)
    {
        var sendTo = ImportText.Int(element, "send_to", 0);
        var body = ImportText.Body(ImportText.Child(element, "send"));
        if (body is null)
        {
            result.Skip(kind, name, "its command is too long");
            return null;
        }

        if (sendTo is not (0 or 10) && body.Length > 0)
        {
            result.Skip(kind, name, sendTo is 12 or 14 ? "it sends to a script, which is never imported" : "it sends somewhere other than the world");
            return null;
        }

        return ImportText.PercentReferences(body);
    }

    private static string? Pattern(XElement element, string kind, string name, ImportResult result, out TriggerMatchType type)
    {
        type = TriggerMatchType.Regex;
        var match = ImportText.Body(ImportText.Attr(element, "match"));
        if (string.IsNullOrEmpty(match))
        {
            result.Skip(kind, name, match is null ? "its pattern is too long" : "it has no pattern");
            return null;
        }

        if (match.Contains('\n'))
        {
            result.Skip(kind, name, "it matches more than one line");
            return null;
        }

        if (ImportText.Flag(element, "regexp"))
        {
            if (!ImportText.Compiles(match))
            {
                result.Skip(kind, name, "its regular expression is not one this client can read");
                return null;
            }

            return match;
        }

        // A whole-line wildcard. `*text*` already means "anywhere", which is this client's own wildcard kind.
        if (kind == "trigger" && match.Length > 2 && match[0] == '*' && match[^1] == '*' && match.Trim('*').Length > 0)
        {
            type = TriggerMatchType.Wildcard;
            return match;
        }

        return ImportText.AnchoredWildcard(match);
    }

    private static void ReadTrigger(XElement element, ImportResult result)
    {
        var name = ImportText.Name(ImportText.Attr(element, "name") is { Length: > 0 } n ? n : ImportText.Attr(element, "match"));
        if (!result.Room("trigger", result.Set.Triggers.Count, name))
        {
            return;
        }

        if (ImportText.Flag(element, "multi_line"))
        {
            result.Skip("trigger", name, "it matches more than one line");
            return;
        }

        if (Pattern(element, "trigger", name, result, out var type) is not { } pattern || Send(element, "trigger", name, result) is not { } send)
        {
            return;
        }

        var gag = ImportText.Flag(element, "omit_from_output");
        if (send.Length == 0 && !gag)
        {
            result.Skip("trigger", name, ImportText.Attr(element, "script") is { Length: > 0 }
                ? "it only calls a script function, which is never imported"
                : "it does nothing this client can import (colours or sounds only)");
            return;
        }

        if (ImportText.Attr(element, "script") is { Length: > 0 } function)
        {
            result.Skip("part", name, $"its script function {ImportText.Name(function)} was left out");
        }

        result.Set.Triggers.Add(new Trigger
        {
            Name = name,
            Pattern = pattern,
            MatchType = type,
            Enabled = ImportText.Flag(element, "enabled", fallback: true),
            CaseSensitive = !ImportText.Flag(element, "ignore_case"),
            StopProcessing = !ImportText.Flag(element, "keep_evaluating"),
            Actions = { Gag = gag, SendResponse = send.Length > 0 ? send : null },
        });
    }

    private static void ReadAlias(XElement element, ImportResult result)
    {
        var name = ImportText.Name(ImportText.Attr(element, "name") is { Length: > 0 } n ? n : ImportText.Attr(element, "match"));
        if (!result.Room("alias", result.Set.Aliases.Count, name)
            || Pattern(element, "alias", name, result, out _) is not { } pattern
            || Send(element, "alias", name, result) is not { } send)
        {
            return;
        }

        if (send.Length == 0)
        {
            result.Skip("alias", name, "it sends nothing (a script function or nothing at all)");
            return;
        }

        result.Set.Aliases.Add(new Alias
        {
            Name = name,
            Pattern = pattern,
            Enabled = ImportText.Flag(element, "enabled", fallback: true),
            CaseSensitive = !ImportText.Flag(element, "ignore_case"),
            Substitution = send,
        });
    }

    private static void ReadTimer(XElement element, ImportResult result)
    {
        var name = ImportText.Name(ImportText.Attr(element, "name") is { Length: > 0 } n ? n : "timer");
        if (!result.Room("timer", result.Set.Timers.Count, name))
        {
            return;
        }

        if (ImportText.Flag(element, "at_time"))
        {
            result.Skip("timer", name, "it fires at a time of day, and timers here repeat on an interval");
            return;
        }

        var seconds = (ImportText.Number(element, "hour") * 3600) + (ImportText.Number(element, "minute") * 60) + ImportText.Number(element, "second");
        if (seconds is < 0.1 or > 86400 || double.IsNaN(seconds))
        {
            result.Skip("timer", name, "its interval is not between a tenth of a second and a day");
            return;
        }

        if (Send(element, "timer", name, result) is not { } send)
        {
            return;
        }

        if (send.Length == 0)
        {
            result.Skip("timer", name, "it sends nothing (a script function or nothing at all)");
            return;
        }

        result.Set.Timers.Add(new TimerDefinition
        {
            Name = name,
            IntervalSeconds = Math.Round(seconds, 2),
            Command = send.Replace("$$", "$", StringComparison.Ordinal),
            OneShot = ImportText.Flag(element, "one_shot"),
            Enabled = ImportText.Flag(element, "enabled", fallback: true),
        });
    }

    private static void ReadMacro(XElement element, ImportResult result)
    {
        var name = ImportText.Name(ImportText.Attr(element, "name"));
        var body = ImportText.Body(ImportText.Child(element, "send"));
        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        if (!FunctionKey.IsMatch(name) || MacroKey.Canonicalise(name) is not { } key)
        {
            result.Skip("key", name, "only function-key macros have a key here");
            return;
        }

        if (!string.Equals(ImportText.Attr(element, "type"), "send_now", StringComparison.OrdinalIgnoreCase))
        {
            result.Skip("key", name, "it puts text on the command line rather than sending it");
            return;
        }

        AddMacro(result, name, key, body);
    }

    private static void ReadKeypad(XElement element, ImportResult result)
    {
        var name = ImportText.Name(ImportText.Attr(element, "name"));
        var body = ImportText.Body(ImportText.Child(element, "send"));
        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        var ctrl = name.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase);
        var digit = ctrl ? name[5..] : name;
        if (digit.Length != 1 || !char.IsAsciiDigit(digit[0]) || MacroKey.Canonicalise((ctrl ? "Ctrl+" : string.Empty) + "Num" + digit) is not { } key)
        {
            result.Skip("key", "keypad " + name, "only the keypad's digits have a key here");
            return;
        }

        AddMacro(result, "keypad " + name, key, body);
    }

    private static void AddMacro(ImportResult result, string name, string key, string body)
    {
        if (!result.Room("key", result.Set.Macros.Count, name))
        {
            return;
        }

        if (result.Set.Macros.Any(m => string.Equals(m.Key, key, StringComparison.Ordinal)))
        {
            result.Skip("key", name, $"{key} is already bound by another key in this file");
            return;
        }

        result.Set.Macros.Add(new Macro { Name = name, Key = key, Command = body });
    }
}
