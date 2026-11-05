using System.Globalization;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Packages;

namespace SharpMUTerm.Client.Packages;

/// <summary>How a review row is drawn: as plain information, as a warning, or as a quiet aside.</summary>
public enum PackageReviewTone
{
    /// <summary>A plain row.</summary>
    Plain,

    /// <summary>Something that can act as the player: drawn in the warning ink.</summary>
    Warning,

    /// <summary>An aside, in the label ink.</summary>
    Quiet,
}

/// <summary>One row of a review: a label and its value, or — with no label — a sentence.</summary>
/// <param name="Label">The label (<c>scripts</c>), or empty for a sentence row.</param>
/// <param name="Value">What it says.</param>
/// <param name="Tone">How it is drawn.</param>
public sealed record PackageReviewRow(string Label, string Value, PackageReviewTone Tone = PackageReviewTone.Plain);

/// <summary>A heading and its rows.</summary>
public sealed record PackageReviewSection(string Heading, IReadOnlyList<PackageReviewRow> Rows);

/// <summary>
/// What the review says before a package is installed — the consent step — as one model both hosts draw: the cell
/// review's rows and the window's review card are this, in this order. It leads with what can act as the player (the
/// rules that send commands and the scripts that run), then what else it adds, then who it applies to.
/// </summary>
/// <param name="Title"><c>Install package</c> or <c>Update package</c>.</param>
/// <param name="Name">The package's name.</param>
/// <param name="Version">Its version.</param>
/// <param name="Author">Its author, or null.</param>
/// <param name="Replaces">The version it replaces, or null for a first install.</param>
/// <param name="Description">What it says it does, or null.</param>
/// <param name="Sections">The sections, in order.</param>
/// <param name="Acts">Whether anything in it can act as the player.</param>
public sealed record PackageReviewModel(
    string Title,
    string Name,
    string Version,
    string? Author,
    string? Replaces,
    string? Description,
    IReadOnlyList<PackageReviewSection> Sections,
    bool Acts)
{
    /// <summary>The first section's heading: what can act as the player.</summary>
    public const string ActsHeading = "WHAT CAN ACT AS YOU";

    /// <summary>The second: everything else it adds.</summary>
    public const string AddsHeading = "WHAT ELSE IT ADDS";

    /// <summary>The third: who it applies to.</summary>
    public const string WhoHeading = "WHO IT APPLIES TO";

    /// <summary>The sentence under the review: nothing is written until it is accepted.</summary>
    public const string Consent = "nothing is written until you install";

    /// <summary>The review of <paramref name="contents"/>.</summary>
    /// <param name="contents">The archive, read.</param>
    /// <param name="characters">The player's characters it would apply to, as <c>Corvid (Aetherfall)</c>.</param>
    /// <param name="existing">The installed version it would replace, or null.</param>
    public static PackageReviewModel From(PackageContents contents, IReadOnlyList<string> characters, InstalledPackage? existing)
    {
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentNullException.ThrowIfNull(characters);
        var manifest = contents.Manifest;
        var review = contents.Review();
        var rules = review.Triggers + review.Aliases + review.Macros + review.Timers;

        var acts = new List<PackageReviewRow>
        {
            review.Sending > 0
                ? new("sending", $"{(review.Sending == 1 ? "1 rule sends" : $"{Count(review.Sending)} rules send")} commands as you", PackageReviewTone.Warning)
                : new("sending", "no rule sends a command", PackageReviewTone.Quiet),
            new("scripts", review.Scripts.Count == 0
                ? "none"
                : string.Join(", ", review.Scripts.Select(s => $"{s.Name} ({PackageCollection.Size(s.Bytes)})"))),
        };
        if (review.Scripts.Count > 0)
        {
            acts.Add(new(string.Empty, "Lua runs in the sandbox, and can send commands as you", PackageReviewTone.Warning));
        }

        var adds = new List<PackageReviewRow>
        {
            new("rule sets", Count(review.Sets)),
            new("rules", rules == 0
                ? "none"
                : $"{PackageCollection.Of(review.Triggers, "trigger")} · {PackageCollection.Of(review.Aliases, "alias", "aliases")} · "
                    + $"{PackageCollection.Of(review.Macros, "key")} · {PackageCollection.Of(review.Timers, "timer")}"),
            new("media", review.MediaFiles == 0
                ? "none"
                : $"{PackageCollection.Of(review.MediaFiles, "file")}, {PackageCollection.Size(review.MediaBytes)}"),
        };
        if (review.Ignored.Count > 0)
        {
            adds.Add(new("left out", $"{PackageCollection.Of(review.Ignored.Count, "file")} outside sets/, scripts/ and media/", PackageReviewTone.Quiet));
        }

        var who = new List<PackageReviewRow>
        {
            new("games", string.Join(", ", manifest.Games)),
            new("characters", characters.Count == 0 ? "none of yours, yet" : string.Join(", ", characters)),
        };

        return new PackageReviewModel(
            existing is null ? "Install package" : "Update package",
            manifest.Name,
            manifest.Version,
            manifest.Author,
            existing?.Manifest.Version,
            string.IsNullOrWhiteSpace(manifest.Description) ? null : manifest.Description,
            [new(ActsHeading, acts), new(AddsHeading, adds), new(WhoHeading, who)],
            review.Sending > 0 || review.Scripts.Count > 0);
    }

    private static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Where an index entry stands against what is installed.</summary>
public enum PackageBrowseState
{
    /// <summary>Not installed.</summary>
    New,

    /// <summary>Installed at the version the index lists.</summary>
    Installed,

    /// <summary>Installed at another version: the index offers an update.</summary>
    Update,
}

/// <summary>One package the index lists, as both hosts' browse lists draw it.</summary>
/// <param name="Entry">The index's entry.</param>
/// <param name="ForWorld">Whether it is for the focused window's world (a <c>*</c> package does not count).</param>
/// <param name="State">Where it stands against what is installed.</param>
/// <param name="InstalledVersion">The version installed, or null.</param>
public sealed record PackageBrowseRow(PackageIndexEntry Entry, bool ForWorld, PackageBrowseState State, string? InstalledVersion)
{
    /// <summary>What a row says after its version: <c>installed</c>, <c>installed 1.1.0, update</c>, or nothing.</summary>
    public string StateText => State switch
    {
        PackageBrowseState.Installed => "installed",
        PackageBrowseState.Update => $"installed {InstalledVersion}, update",
        _ => string.Empty,
    };
}

/// <summary>
/// The package index as both hosts list it: the focused window's world's packages first, then the rest by name, each
/// with where it stands against what is installed; and how many entries the index broke its rules on.
/// </summary>
/// <param name="World">The focused window's world's name, or null.</param>
/// <param name="Dropped">Entries the index listed that were not kept.</param>
/// <param name="Rows">The rows, in order.</param>
public sealed record PackageBrowseModel(string? World, int Dropped, IReadOnlyList<PackageBrowseRow> Rows)
{
    /// <summary>The index over what is installed, sorted for <paramref name="world"/>.</summary>
    public static PackageBrowseModel From(PackageIndex index, IReadOnlyList<InstalledPackage> installed, WorldDefinition? world)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(installed);
        var rows = index.For(world?.Host, world?.Port ?? 0).Select(entry =>
        {
            var have = installed.FirstOrDefault(p => p.Id == entry.Id);
            var forWorld = world is not null && entry.Games.Any(g => g != "*" && PackageManifest.Matches(g, world.Host, world.Port));
            var state = have is null ? PackageBrowseState.New
                : have.Manifest.Version == entry.Version ? PackageBrowseState.Installed
                : PackageBrowseState.Update;
            return new PackageBrowseRow(entry, forWorld, state, have?.Manifest.Version);
        }).ToList();
        return new PackageBrowseModel(world?.Name, index.Dropped, rows);
    }
}
