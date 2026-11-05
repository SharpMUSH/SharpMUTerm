using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Import;

/// <summary>The other clients a setup can be imported from.</summary>
public enum ImportFormat
{
    /// <summary>A MUSHclient world file (<c>.mcl</c>, XML).</summary>
    MushClient,

    /// <summary>A Mudlet profile folder, a profile save, or a package or module export (XML, or a zip holding one).</summary>
    Mudlet,

    /// <summary>A TinTin++ script (<c>#session</c>, <c>#action</c>, <c>#alias</c>, <c>#ticker</c>, <c>#gag</c>).</summary>
    TinTin,
}

/// <summary>Names a format the way a player knows it.</summary>
public static class ImportFormats
{
    /// <summary>The client's name as its players write it.</summary>
    public static string Name(ImportFormat format) => format switch
    {
        ImportFormat.MushClient => "MUSHclient",
        ImportFormat.Mudlet => "Mudlet",
        _ => "TinTin++",
    };

    /// <summary>The formats in the order the surfaces name them.</summary>
    public static IReadOnlyList<ImportFormat> All { get; } = [ImportFormat.MushClient, ImportFormat.Mudlet, ImportFormat.TinTin];

    /// <summary>The formats as one phrase: <c>MUSHclient, Mudlet, TinTin++</c>.</summary>
    public static string Listed => string.Join(", ", All.Select(Name));
}

/// <summary>
/// The bounds every reader holds an import file to. An import file is untrusted input — written by another program,
/// often copied from a forum post — so every count and every length is capped while it is read, and an entry that
/// breaks a cap is skipped and counted rather than truncated (a truncated command is a different command).
/// </summary>
public static class ImportLimits
{
    /// <summary>The largest file read: a big Mudlet profile save is a few megabytes.</summary>
    public const long MaxFileBytes = 16L * 1024 * 1024;

    /// <summary>The most triggers, aliases, timers or keys taken from one file, each.</summary>
    public const int MaxItems = 5000;

    /// <summary>The most worlds taken from one file.</summary>
    public const int MaxWorlds = 100;

    /// <summary>The longest pattern or command taken.</summary>
    public const int MaxText = 4096;

    /// <summary>The longest name (of a world, character, rule or host) taken.</summary>
    public const int MaxName = 128;

    /// <summary>The most entries of a zip looked at, and the most of its XML read.</summary>
    public const int MaxZipEntries = 512;

    /// <summary>How many left-out entries are listed by name; the rest are only counted.</summary>
    public const int MaxListedSkips = 200;

    /// <summary>How deep a Mudlet folder may nest before the rest of it is left out.</summary>
    public const int MaxDepth = 32;
}

/// <summary>Something an import file held that was not imported, and why.</summary>
/// <param name="Kind">What it is: <c>trigger</c>, <c>alias</c>, <c>timer</c>, <c>key</c>, <c>script</c>, <c>world</c>…</param>
/// <param name="Name">What it is called in the file, or its pattern when it has no name.</param>
/// <param name="Reason">Why it was left out, in a few words a player understands.</param>
public sealed record ImportSkip(string Kind, string Name, string Reason);

/// <summary>A password the file held for a character. It is never stored unless the player says so on the review.</summary>
/// <param name="World">The index of the world in <see cref="ImportResult.Worlds"/>.</param>
/// <param name="Character">The character's name.</param>
/// <param name="Password">The password.</param>
public sealed record ImportedPassword(int World, string Character, string Password)
{
    /// <summary>Never the password: a record's own <c>ToString</c> would print it into a log.</summary>
    public override string ToString() => $"{nameof(ImportedPassword)} {{ World = {World}, Character = {Character} }}";
}

/// <summary>
/// What a reader made of one file: worlds with their characters, one set of automation, what was left out and why,
/// and any passwords — held apart, so nothing stores them by accident. Nothing here has touched the configuration;
/// <see cref="ImportPlan"/> decides names against it, and only the review's ⏎ writes.
/// </summary>
public sealed class ImportResult
{
    public ImportResult(ImportFormat format, string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Format = format;
        Source = source;
    }

    /// <summary>The client the file came from.</summary>
    public ImportFormat Format { get; }

    /// <summary>The file's name (not its whole path), for the review.</summary>
    public string Source { get; }

    /// <summary>The worlds the file names, each with the characters it names.</summary>
    public List<WorldDefinition> Worlds { get; } = new();

    /// <summary>The automation read. Its name is decided by the plan (<c>Imported from …</c>).</summary>
    public TriggerSet Set { get; } = new();

    /// <summary>Passwords the file held, never stored without the player's yes.</summary>
    public List<ImportedPassword> Passwords { get; } = new();

    /// <summary>What was left out, by name, up to <see cref="ImportLimits.MaxListedSkips"/>.</summary>
    public List<ImportSkip> Skipped { get; } = new();

    /// <summary>How many things were left out, including those past the listed ones.</summary>
    public int SkippedCount { get; private set; }

    /// <summary>Whether there is anything to import at all.</summary>
    public bool IsEmpty =>
        Worlds.Count == 0 && Set.Triggers.Count == 0 && Set.Aliases.Count == 0 && Set.Timers.Count == 0 && Set.Macros.Count == 0;

    /// <summary>How many rules the set holds.</summary>
    public int RuleCount => Set.Triggers.Count + Set.Aliases.Count + Set.Timers.Count + Set.Macros.Count;

    /// <summary>Records something left out.</summary>
    public void Skip(string kind, string? name, string reason)
    {
        SkippedCount++;
        if (Skipped.Count < ImportLimits.MaxListedSkips)
        {
            Skipped.Add(new ImportSkip(kind, ImportText.Name(name) is { Length: > 0 } n ? n : "(unnamed)", reason));
        }
    }

    /// <summary>Whether another <paramref name="kind"/> may be added; when not, it is recorded as left out.</summary>
    internal bool Room(string kind, int count, string? name)
    {
        if (count < ImportLimits.MaxItems)
        {
            return true;
        }

        Skip(kind, name, $"past the limit of {ImportLimits.MaxItems} {kind}s in one file");
        return false;
    }
}
