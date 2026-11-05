using System.Globalization;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Client.Settings;

/// <summary>
/// One installed package as F10 lists it: the package, and what the package index said about it the last time the
/// player opened it — null when the index has not been fetched this run, which is the usual case, because the index
/// is fetched only when the player asks.
/// </summary>
/// <param name="Package">The installed package.</param>
/// <param name="Listed">The index's entry for the same id, or null.</param>
public sealed record PackageItem(InstalledPackage Package, PackageIndexEntry? Listed)
{
    /// <summary>The package's id.</summary>
    public string Id => Package.Id;

    /// <summary>The newer version the index offers, or null when it offers none (or has not been read).</summary>
    public string? Update => Listed is { } entry && entry.Version != Package.Manifest.Version ? entry.Version : null;
}

/// <summary>Something a package row offers besides its fields: on/off, copy its sets, remove it, browse the index.</summary>
/// <param name="Id">A stable id (<see cref="PackageCollection.Toggle"/>…).</param>
/// <param name="Label">What it is called, after its key on the cell screen (<c>copy sets to yours</c>).</param>
/// <param name="Key">The cell screen's key for it (<c>Space</c>, <c>c</c>, <c>Del</c>, <c>b</c>).</param>
/// <param name="Help">One line saying what it does.</param>
public sealed record PackageAction(string Id, string Label, string Key, string Help);

/// <summary>
/// F10 declared: the installed packages, each package's fields — whether it is on, its version and games, what it
/// adds (sets, scripts, media), whether the index offers an update, who it applies to — and the actions a row offers.
/// The cell screen draws its rows and its key line from this, and the window draws its package manager page from it,
/// so the two show the same fields in the same order.
/// <para>
/// <b>The only thing a package field writes is <see cref="AppConfiguration.DisabledPackages"/></b>, and it replaces
/// that list rather than editing it, the reads-cross-threads rule every list section keeps. Installing and removing
/// change the package store, which is the client's; they are actions, not fields.
/// </para>
/// </summary>
public sealed class PackageCollection
{
    /// <summary>The on/off action.</summary>
    public const string Toggle = "toggle";

    /// <summary>The copy-its-sets-to-edit action.</summary>
    public const string CopySets = "copy";

    /// <summary>The remove action.</summary>
    public const string Remove = "remove";

    /// <summary>The browse-the-index action.</summary>
    public const string Browse = "browse";

    /// <summary>What one item is called.</summary>
    public required string Noun { get; init; }

    /// <summary>What several are called.</summary>
    public required string Plural { get; init; }

    /// <summary>Every field of a package, in the order both hosts show them.</summary>
    public required IReadOnlyList<SettingsField> Fields { get; init; }

    /// <summary>The actions a package row offers, in the order the cell screen's key line names them.</summary>
    public required IReadOnlyList<PackageAction> Actions { get; init; }

    /// <summary>The id of the field that switches a package on, which a list shows as each row's check box.</summary>
    public string EnabledField { get; init; } = "enabled";

    /// <summary>The fields a list row carries beside its check box: the name, then what is said after it.</summary>
    public IReadOnlyList<string> RowFields { get; init; } = ["name", "version", "games"];

    /// <summary>A field by id.</summary>
    public SettingsField Field(string id) =>
        Fields.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"The {Plural} declare no field '{id}'.");

    /// <summary>An action by id.</summary>
    public PackageAction Action(string id) =>
        Actions.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"The {Plural} declare no action '{id}'.");

    /// <summary>The fields a package's detail shows: every field but the check box and the row's own.</summary>
    public IEnumerable<SettingsField> DetailFields =>
        Fields.Where(f => f.Id != EnabledField && !RowFields.Contains(f.Id, StringComparer.Ordinal));

    /// <summary>The installed packages, by name, each with what <paramref name="index"/> lists for it.</summary>
    public static IReadOnlyList<PackageItem> Items(AppConfiguration config, PackageIndex? index)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.Packages
            .OrderBy(p => p.Manifest.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new PackageItem(p, index?.Entries.FirstOrDefault(e => e.Id == p.Id)))
            .ToList();
    }

    /// <summary>Whether the package <paramref name="id"/> is on.</summary>
    public static bool IsEnabled(AppConfiguration config, string id)
    {
        ArgumentNullException.ThrowIfNull(config);
        return !config.DisabledPackages.Contains(id, StringComparer.Ordinal);
    }

    /// <summary>
    /// Switches the package <paramref name="id"/> on or off by replacing <see cref="AppConfiguration.DisabledPackages"/>;
    /// false when it was already so.
    /// </summary>
    public static bool SetEnabled(AppConfiguration config, string id, bool on)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(id);
        if (IsEnabled(config, id) == on)
        {
            return false;
        }

        config.DisabledPackages = on
            ? config.DisabledPackages.Where(d => !string.Equals(d, id, StringComparison.Ordinal)).ToList()
            : [.. config.DisabledPackages, id];
        return true;
    }

    /// <summary>The characters a package applies to: everyone on a world it is for, as <c>Corvid · Aetherfall</c>.</summary>
    public static IReadOnlyList<string> CharactersFor(AppConfiguration config, PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(manifest);
        return config.Worlds.ToArray()
            .Where(w => manifest.IsFor(w.Host, w.Port))
            .SelectMany(w => w.Characters.ToArray().Select(c => Chrome.CharacterNames.Display(w.Name, c.Name)))
            .ToList();
    }

    /// <summary><c>1 trigger</c>, <c>3 aliases</c>.</summary>
    public static string Of(int n, string one, string? many = null) =>
        n == 1 ? $"1 {one}" : $"{n.ToString(CultureInfo.InvariantCulture)} {many ?? one + "s"}";

    /// <summary>A size as people read it.</summary>
    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes.ToString(CultureInfo.InvariantCulture)} B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
    };
}

/// <summary>F10's declaration.</summary>
public static partial class BuiltInSettings
{
    /// <summary>The heading over what a package is.</summary>
    public const string PackageGroup = "Package";

    /// <summary>The heading over what a package adds.</summary>
    public const string ContributesGroup = "What it adds";

    /// <summary>The heading over who a package applies to and whether it is current.</summary>
    public const string ReachGroup = "Who it applies to";

    /// <summary>F10: the installed packages.</summary>
    public static SettingsSectionContribution Packages { get; } = new()
    {
        Id = "packages",
        Title = "Packages",
        Key = "F10",
        Order = 90,
        Scope = SettingScope.Item,
        Packages = new PackageCollection
        {
            Noun = "package",
            Plural = "packages",
            Fields =
            [
                new BoolField("enabled", "enabled", "whether its sets, scripts and sounds are used")
                {
                    Group = PackageGroup,
                    Keywords = "on off disable switch",
                    Binding = SettingBinding<bool>.ForItem<PackageItem>(
                        (c, p) => PackageCollection.IsEnabled(c, p.Id),
                        (c, p, v) => PackageCollection.SetEnabled(c, p.Id, v),
                        FreshPackage,
                        v => v ? "on" : "off"),
                },
                Info("name", "name", PackageGroup, p => p.Package.Manifest.Name, "what the package is called"),
                Info("version", "version", PackageGroup, p => p.Package.Manifest.Version, "the version installed"),
                Info("games", "games", PackageGroup, p => string.Join(", ", p.Package.Manifest.Games),
                    "the games it is for: host[:port], *.host, or * for any", "host world server"),
                Info("author", "by", PackageGroup, p => p.Package.Manifest.Author ?? "—", "who made it", "author"),
                Info("description", "about", PackageGroup, p => p.Package.Manifest.Description ?? "—", "what it says it does", "description"),
                Info("sets", "sets", ContributesGroup, Sets,
                    "its trigger sets, read-only on F2–F6; c copies them to yours to edit", "trigger alias timer rule"),
                Info("scripts", "scripts", ContributesGroup,
                    p => p.Package.Scripts.Count == 0 ? "none" : string.Join(", ", p.Package.Scripts),
                    "its Lua scripts, which run in the sandbox and can send commands as you", "lua"),
                Info("media", "media", ContributesGroup,
                    p => p.Package.Media.Count == 0 ? "none"
                        : $"{PackageCollection.Of(p.Package.Media.Count, "file")}, {PackageCollection.Size(p.Package.MediaBytes)}",
                    "its sounds", "sound music audio"),
                Info("update", "update", ReachGroup,
                    p => p.Update is { } newer ? $"{newer} in the index"
                        : p.Listed is not null ? "up to date" : "not checked — the index has not been opened",
                    "whether the package index offers a newer version", "upgrade new version index"),
                Info("characters", "characters", ReachGroup, Characters, "the characters it applies to", "who world"),
            ],
            Actions =
            [
                new(PackageCollection.Toggle, "on/off", "Space", "switch the package on or off"),
                new(PackageCollection.CopySets, "copy sets to yours", "c", "copy its sets into yours, where F2–F6 can edit them"),
                new(PackageCollection.Remove, "remove", "Del", "remove the package and its files"),
                new(PackageCollection.Browse, "browse", "b", "browse the package index"),
            ],
        },
    };

    private static PackageItem FreshPackage() =>
        new(new InstalledPackage(new PackageManifest { Id = "package", Name = "package", Version = "0" }, []), null);

    private static string Sets(AppConfiguration config, PackageItem item)
    {
        var sets = item.Package.Sets;
        if (sets.Count == 0)
        {
            return "none";
        }

        var rules = sets.Sum(s => s.Triggers.Count + s.Aliases.Count + s.Macros.Count + s.Timers.Count);
        return $"{string.Join(", ", sets.Select(s => s.Name))} · {PackageCollection.Of(rules, "rule")}";
    }

    private static string Characters(AppConfiguration config, PackageItem item)
    {
        var who = PackageCollection.CharactersFor(config, item.Package.Manifest);
        return who.Count == 0 ? "none" : string.Join(", ", who);
    }

    private static InfoField Info(string id, string label, string group, Func<PackageItem, string> read, string help, string? keywords = null) =>
        Info(id, label, group, (_, p) => read(p), help, keywords);

    private static InfoField Info(
        string id, string label, string group, Func<AppConfiguration, PackageItem, string> read, string help, string? keywords = null) =>
        new(id, label, help)
        {
            Group = group,
            Keywords = keywords,
            Read = t => t.Item is PackageItem item ? read(t.Config, item) : string.Empty,
        };
}
