using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Import;

/// <summary>
/// Reads Mudlet's XML (<c>MudletPackage</c>): a profile save from a profile's <c>current/</c> folder, a package or
/// module export, or a zip (<c>.mpackage</c>) holding one — its triggers, aliases, timers and keys, folders flattened
/// (a rule inside a disabled folder comes in disabled). A profile folder adds its world from the <c>url</c>,
/// <c>port</c> and <c>login</c> files Mudlet keeps beside the saves.
/// <para>
/// <b>Lua is never imported.</b> Mudlet's rules carry two actions, a plain command and a Lua body; the command is kept
/// and the Lua is listed as left out. A rule whose only action is Lua is left out whole — keeping it as a rule that
/// does nothing would put a dead rule in the player's set, and translating Lua is not something an importer should
/// guess at. Scripts, buttons and chains (a trigger that gates the triggers inside it) are listed and left out too.
/// </para>
/// </summary>
public static class MudletImport
{
    // Mudlet's trigger pattern kinds (regexCodePropertyList).
    private const int Substring = 0;
    private const int Perl = 1;
    private const int BeginOfLine = 2;
    private const int Exact = 3;

    // Qt's key codes and modifiers, which Mudlet stores as numbers.
    private const int QtF1 = 0x01000030;
    private const int QtShift = 0x02000000;
    private const int QtControl = 0x04000000;
    private const int QtAlt = 0x08000000;
    private const int QtKeypad = 0x20000000;

    /// <summary>Whether <paramref name="text"/> looks like Mudlet's XML.</summary>
    public static bool Recognises(string text) => text.Contains("<MudletPackage", StringComparison.Ordinal);

    /// <summary>Reads Mudlet's XML <paramref name="text"/>, or says why it is not that.</summary>
    public static (ImportResult? Result, string? Problem) Parse(string text, string source)
    {
        ArgumentNullException.ThrowIfNull(text);
        var result = new ImportResult(ImportFormat.Mudlet, source);
        return Read(text, result) is { } problem ? (null, problem) : (result, null);
    }

    /// <summary>
    /// Reads a Mudlet profile folder: the newest save in <c>current/</c>, and the world from the <c>url</c>, <c>port</c>
    /// and <c>login</c> files. Its <c>password</c> file, if any, is offered on the review and never stored without a yes.
    /// </summary>
    public static (ImportResult? Result, string? Problem) ReadProfile(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var directory = new DirectoryInfo(folder);
        var result = new ImportResult(ImportFormat.Mudlet, directory.Name);
        var current = new DirectoryInfo(Path.Combine(directory.FullName, "current"));
        var save = current.Exists
            ? current.EnumerateFiles("*.xml").Take(ImportLimits.MaxZipEntries).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (save is not null)
        {
            if (save.Length > ImportLimits.MaxFileBytes)
            {
                return (null, "the profile's newest save is too large to read");
            }

            if (Read(File.ReadAllText(save.FullName), result) is { } problem)
            {
                return (null, problem);
            }
        }

        AddProfileWorld(directory, directory.Name, result);
        return result.IsEmpty && save is null
            ? (null, "that folder is not a Mudlet profile (it has no current/ saves and no url file)")
            : (result, null);
    }

    /// <summary>Reads the first Mudlet XML in a zip (a <c>.mpackage</c>), extracting nothing to disk.</summary>
    public static (ImportResult? Result, string? Problem) ReadZip(byte[] bytes, string source)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries.Take(ImportLimits.MaxZipEntries))
            {
                if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || entry.Length > ImportLimits.MaxFileBytes)
                {
                    continue;
                }

                var text = ReadBounded(entry);
                if (text is not null && Recognises(text))
                {
                    return Parse(text, source);
                }
            }
        }
        catch (InvalidDataException)
        {
            return (null, "that zip cannot be read");
        }

        return (null, "that zip holds no Mudlet package XML");
    }

    /// <summary>
    /// When <paramref name="file"/> is a save inside a profile's <c>current/</c> folder, the profile's world is added
    /// from the files beside it.
    /// </summary>
    public static void AddProfileWorldFor(string file, ImportResult result)
    {
        var current = new FileInfo(file).Directory;
        if (current is { Name: "current", Parent: { } profile } && File.Exists(Path.Combine(profile.FullName, "url")))
        {
            AddProfileWorld(profile, profile.Name, result);
        }
    }

    private static string? ReadBounded(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > ImportLimits.MaxFileBytes)
            {
                return null;
            }
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string? ReadSmallFile(DirectoryInfo folder, string name)
    {
        var file = new FileInfo(Path.Combine(folder.FullName, name));
        return file.Exists && file.Length <= 1024 ? File.ReadAllText(file.FullName).Trim() : null;
    }

    private static void AddProfileWorld(DirectoryInfo profile, string name, ImportResult result)
    {
        if (result.Worlds.Count > 0)
        {
            return;
        }

        var host = ImportText.Host(ReadSmallFile(profile, "url"));
        if (host is null)
        {
            return;
        }

        var world = new WorldDefinition
        {
            Name = ImportText.Name(name) is { Length: > 0 } n ? n : host,
            Host = host,
            Port = ImportText.Port(ReadSmallFile(profile, "port")) ?? 23,
            UseTls = ReadSmallFile(profile, "ssl_tls_enabled") is "true" or "1",
        };
        var login = ImportText.Name(ReadSmallFile(profile, "login"));
        if (login.Length > 0)
        {
            world.Characters.Add(new CharacterDefinition { Name = login });
            if (ReadSmallFile(profile, "password") is { Length: > 0 and <= ImportLimits.MaxName } password)
            {
                result.Passwords.Add(new ImportedPassword(0, login, password));
            }
        }

        result.Worlds.Add(world);
    }

    private static string? Read(string text, ImportResult result)
    {
        if (ImportText.Xml(text) is not { Root: { } root } || root.Name.LocalName != "MudletPackage")
        {
            return "not a Mudlet package or profile save";
        }

        foreach (var host in root.Elements("HostPackage").Elements("Host").Take(1))
        {
            ReadHost(host, result);
        }

        foreach (var package in root.Elements("TriggerPackage"))
        {
            Walk(package, "Trigger", "TriggerGroup", 0, true, result, ReadTrigger);
        }

        foreach (var package in root.Elements("AliasPackage"))
        {
            Walk(package, "Alias", "AliasGroup", 0, true, result, ReadAlias);
        }

        foreach (var package in root.Elements("TimerPackage"))
        {
            Walk(package, "Timer", "TimerGroup", 0, true, result, ReadTimer);
        }

        foreach (var package in root.Elements("KeyPackage"))
        {
            Walk(package, "Key", "KeyGroup", 0, true, result, ReadKey);
        }

        foreach (var script in root.Elements("ScriptPackage").Descendants("Script").Take(ImportLimits.MaxItems))
        {
            if (!string.IsNullOrWhiteSpace(script.Element("script")?.Value))
            {
                result.Skip("script", script.Element("name")?.Value, "Lua scripts are never imported");
            }
        }

        foreach (var button in root.Elements("ActionPackage").Descendants("Action").Take(ImportLimits.MaxItems))
        {
            result.Skip("button", button.Element("name")?.Value, "buttons are not imported");
        }

        return null;
    }

    private static void ReadHost(XElement host, ImportResult result)
    {
        var url = ImportText.Host(ImportText.Child(host, "url"));
        if (url is null)
        {
            return;
        }

        var name = ImportText.Name(ImportText.Child(host, "name"));
        var world = new WorldDefinition
        {
            Name = name.Length > 0 ? name : url,
            Host = url,
            Port = ImportText.Port(ImportText.Child(host, "port")) ?? 23,
        };
        var login = ImportText.Name(ImportText.Child(host, "login"));
        if (login.Length > 0)
        {
            world.Characters.Add(new CharacterDefinition { Name = login });
        }

        result.Worlds.Add(world);
    }

    /// <summary>
    /// Walks a Mudlet package's folders, handing each rule that is not a folder to <paramref name="read"/> with whether
    /// it and every folder above it are active. A rule that has rules inside it and patterns of its own is a chain,
    /// which this client has no equivalent for, so it and everything in it are left out.
    /// </summary>
    private static void Walk(
        XElement parent,
        string item,
        string group,
        int depth,
        bool active,
        ImportResult result,
        Action<XElement, bool, ImportResult> read)
    {
        foreach (var element in parent.Elements())
        {
            var local = element.Name.LocalName;
            if (local != item && local != group)
            {
                continue;
            }

            var name = element.Element("name")?.Value;
            var on = active && ImportText.Flag(element, "isActive");
            var children = element.Elements().Where(e => e.Name.LocalName == item || e.Name.LocalName == group).ToList();
            var isFolder = ImportText.Flag(element, "isFolder") || local == group;
            if (depth >= ImportLimits.MaxDepth)
            {
                result.Skip(item.ToLowerInvariant(), name, "nested too deeply");
                continue;
            }

            if (isFolder && !HasOwnPattern(element))
            {
                if (!string.IsNullOrWhiteSpace(element.Element("script")?.Value))
                {
                    result.Skip("script", name, "a folder's Lua is never imported");
                }

                Walk(element, item, group, depth + 1, on, result, read);
                continue;
            }

            if (children.Count > 0)
            {
                var count = element.Descendants().Count(e => e.Name.LocalName == item || e.Name.LocalName == group);
                result.Skip(item.ToLowerInvariant(), name, $"it is a chain gating {count} rule{(count == 1 ? string.Empty : "s")} inside it, which this client has no equivalent for");
                continue;
            }

            read(element, on, result);
        }
    }

    private static bool HasOwnPattern(XElement element) =>
        element.Element("regexCodeList")?.Elements("string").Any(s => s.Value.Length > 0) == true
        || !string.IsNullOrEmpty(element.Element("regex")?.Value)
        || !string.IsNullOrEmpty(element.Element("time")?.Value?.Trim('0', ':', '.'));

    /// <summary>The rule's command, its Lua listed as left out; null (and the rule left out) when Lua is all it does.</summary>
    private static string? Command(XElement element, string kind, string name, string commandElement, ImportResult result)
    {
        var command = ImportText.Body(element.Element(commandElement)?.Value);
        var lua = !string.IsNullOrWhiteSpace(element.Element("script")?.Value);
        if (command is null)
        {
            result.Skip(kind, name, "its command is too long");
            return null;
        }

        if (command.Length == 0)
        {
            result.Skip(kind, name, lua ? "its action is Lua, which is never imported" : "it does nothing this client can import");
            return null;
        }

        if (lua)
        {
            result.Skip("part", name, "its Lua was left out; its command was kept");
        }

        return command;
    }

    private static void ReadTrigger(XElement element, bool active, ImportResult result)
    {
        var name = ImportText.Name(element.Element("name")?.Value);
        if (!result.Room("trigger", result.Set.Triggers.Count, name))
        {
            return;
        }

        if (ImportText.Flag(element, "isMultiline"))
        {
            result.Skip("trigger", name, "it matches across several lines");
            return;
        }

        var patterns = element.Element("regexCodeList")?.Elements("string").Select(s => s.Value).ToList() ?? [];
        var kinds = element.Element("regexCodePropertyList")?.Elements("integer").Select(i => int.TryParse(i.Value, out var k) ? k : -1).ToList() ?? [];
        if (patterns.Count == 0)
        {
            result.Skip("trigger", name, "it has no pattern");
            return;
        }

        if (Command(element, "trigger", name, "mCommand", result) is not { } command)
        {
            return;
        }

        var stop = ImportText.Flag(element, "isFilterTrigger");
        for (var i = 0; i < patterns.Count && i < 64; i++)
        {
            var label = patterns.Count == 1 ? name : $"{name} ({i + 1})";
            var kind = i < kinds.Count ? kinds[i] : Substring;
            if (ImportText.Body(patterns[i]) is not { Length: > 0 } raw || raw.Contains('\n'))
            {
                result.Skip("trigger", label, "its pattern is empty, too long or more than one line");
                continue;
            }

            (string Pattern, TriggerMatchType Type)? mapped = kind switch
            {
                Substring => (raw, TriggerMatchType.Contains),
                BeginOfLine => (raw, TriggerMatchType.StartsWith),
                Exact => (ImportText.ExactLine(raw), TriggerMatchType.Regex),
                Perl when ImportText.Compiles(raw) => (raw, TriggerMatchType.Regex),
                _ => null,
            };
            if (mapped is not { } m)
            {
                result.Skip("trigger", label, kind == Perl
                    ? "its regular expression is not one this client can read"
                    : "its pattern kind (Lua function, colour, prompt or line spacer) has no equivalent here");
                continue;
            }

            if (!result.Room("trigger", result.Set.Triggers.Count, label))
            {
                return;
            }

            result.Set.Triggers.Add(new Trigger
            {
                Name = label,
                Pattern = m.Pattern,
                MatchType = m.Type,
                Enabled = active,
                CaseSensitive = true,
                StopProcessing = stop,
                Actions = { SendResponse = ImportText.Literal(command) },
            });
        }
    }

    private static void ReadAlias(XElement element, bool active, ImportResult result)
    {
        var name = ImportText.Name(element.Element("name")?.Value);
        if (!result.Room("alias", result.Set.Aliases.Count, name))
        {
            return;
        }

        var pattern = ImportText.Body(element.Element("regex")?.Value);
        if (string.IsNullOrEmpty(pattern) || pattern.Contains('\n') || !ImportText.Compiles(pattern))
        {
            result.Skip("alias", name, string.IsNullOrEmpty(pattern) ? "it has no pattern" : "its regular expression is not one this client can read");
            return;
        }

        if (Command(element, "alias", name, "command", result) is not { } command)
        {
            return;
        }

        result.Set.Aliases.Add(new Alias
        {
            Name = name,
            Pattern = pattern,
            Enabled = active,
            CaseSensitive = true,
            Substitution = ImportText.Literal(command),
        });
    }

    private static void ReadTimer(XElement element, bool active, ImportResult result)
    {
        var name = ImportText.Name(element.Element("name")?.Value);
        if (!result.Room("timer", result.Set.Timers.Count, name))
        {
            return;
        }

        if (ImportText.Flag(element, "isTempTimer") || ImportText.Flag(element, "isOffsetTimer"))
        {
            result.Skip("timer", name, "it is a temporary or offset timer");
            return;
        }

        var time = element.Element("time")?.Value?.Trim() ?? string.Empty;
        if (!TimeSpan.TryParseExact(time, [@"hh\:mm\:ss\.fff", @"hh\:mm\:ss", @"h\:mm\:ss\.fff", @"hh\:mm\:ss\.ff", @"hh\:mm\:ss\.f"], System.Globalization.CultureInfo.InvariantCulture, out var interval)
            || interval.TotalSeconds is < 0.1 or > 86400)
        {
            result.Skip("timer", name, "its interval is not between a tenth of a second and a day");
            return;
        }

        if (Command(element, "timer", name, "command", result) is not { } command)
        {
            return;
        }

        result.Set.Timers.Add(new TimerDefinition
        {
            Name = name,
            IntervalSeconds = Math.Round(interval.TotalSeconds, 2),
            Command = command,
            Enabled = active,
        });
    }

    private static void ReadKey(XElement element, bool active, ImportResult result)
    {
        var name = ImportText.Name(element.Element("name")?.Value);
        if (!result.Room("key", result.Set.Macros.Count, name))
        {
            return;
        }

        var code = int.TryParse(element.Element("keyCode")?.Value, out var c) ? c : 0;
        var modifiers = int.TryParse(element.Element("keyModifier")?.Value, out var m) ? m : 0;
        if (KeyName(code, modifiers) is not { } descriptor || MacroKey.Canonicalise(descriptor) is not { } key)
        {
            result.Skip("key", name, "its key has no name here (only function keys, letters, digits and the keypad do)");
            return;
        }

        if (Command(element, "key", name, "command", result) is not { } command)
        {
            return;
        }

        if (result.Set.Macros.Any(existing => string.Equals(existing.Key, key, StringComparison.Ordinal)))
        {
            result.Skip("key", name, $"{key} is already bound by another key in this file");
            return;
        }

        result.Set.Macros.Add(new Macro { Name = name.Length > 0 ? name : key, Key = key, Command = command, Enabled = active });
    }

    /// <summary>A Qt key code and modifier mask as a descriptor (<c>Ctrl+F2</c>, <c>Num8</c>), or null.</summary>
    internal static string? KeyName(int code, int modifiers)
    {
        string? key = code switch
        {
            >= QtF1 and < QtF1 + 24 => "F" + (code - QtF1 + 1),
            >= 'A' and <= 'Z' => ((char)code).ToString(),
            >= '0' and <= '9' when (modifiers & QtKeypad) != 0 => "Num" + (char)code,
            >= '0' and <= '9' => ((char)code).ToString(),
            _ => null,
        };
        if (key is null)
        {
            return null;
        }

        var ctrl = (modifiers & QtControl) != 0;
        var alt = (modifiers & QtAlt) != 0;
        var shift = (modifiers & QtShift) != 0;

        // A bare letter or digit is typing, not a key to bind.
        if (key.Length == 1 && !ctrl && !alt)
        {
            return null;
        }

        return MacroKey.Describe(key, ctrl, alt, shift);
    }
}
