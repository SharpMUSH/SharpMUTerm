using System.Globalization;
using SharpMUTerm.Core.Packages;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// What the two package surfaces say, as markup rows: the review a player reads before anything is installed,
/// and the list of what is installed. Pure, so a test reads the words the player would.
/// </summary>
internal static class PackageScreen
{
    /// <summary>
    /// The review. It is the consent step, so it leads with what can act as the player: the rules that send
    /// commands and the scripts that run, then who it applies to and how much it writes.
    /// </summary>
    internal static List<string> Review(PackageContents contents, IReadOnlyList<string> characters, InstalledPackage? existing, int width)
    {
        var manifest = contents.Manifest;
        var review = contents.Review();
        var lines = new List<string>
        {
            $"[bold {ScreenPalette.Accent}]{Escape(Elide(manifest.Name, width))}[/] [{ScreenPalette.Label}]{Escape(manifest.Version)}"
                + (manifest.Author is { } author ? $" · by {Escape(Elide(author, 40))}" : string.Empty) + "[/]",
        };

        if (existing is not null)
        {
            lines.Add($"[{ScreenPalette.Label}]replaces version {Escape(existing.Manifest.Version)}, and its sets with it[/]");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Description))
        {
            lines.Add($"[{ScreenPalette.Value}]{Escape(Elide(manifest.Description, width))}[/]");
        }

        lines.Add(string.Empty);
        lines.Add($"[bold {ScreenPalette.Accent}]WHAT IT ADDS[/]");
        lines.Add(Row("rule sets", Count(review.Sets)));
        lines.Add(Row("rules", $"{Of(review.Triggers, "trigger")} · {Of(review.Aliases, "alias", "aliases")} · {Of(review.Macros, "key")} · {Of(review.Timers, "timer")}"));
        lines.Add(review.Sending > 0
            ? $"  [bold {ScreenPalette.Warn}]{(review.Sending == 1 ? "1 of them sends" : $"{Count(review.Sending)} of them send")} commands as you[/]"
            : $"  [{ScreenPalette.Label}]none of them sends a command[/]");
        lines.Add(Row("scripts", review.Scripts.Count == 0
            ? "none"
            : string.Join(", ", review.Scripts.Select(s => $"{s.Name} ({Size(s.Bytes)})"))));
        if (review.Scripts.Count > 0)
        {
            lines.Add($"  [bold {ScreenPalette.Warn}]Lua runs in the sandbox, and can send commands as you[/]");
        }

        lines.Add(Row("media", review.MediaFiles == 0 ? "none" : $"{Of(review.MediaFiles, "file")}, {Size(review.MediaBytes)}"));
        if (review.Ignored.Count > 0)
        {
            lines.Add(Row("left out", $"{Of(review.Ignored.Count, "file")} outside sets/, scripts/ and media/"));
        }

        lines.Add(string.Empty);
        lines.Add($"[bold {ScreenPalette.Accent}]WHO IT APPLIES TO[/]");
        lines.Add(Row("games", string.Join(", ", manifest.Games)));
        lines.Add(Row("characters", characters.Count == 0 ? "none of yours, yet" : string.Join(", ", characters)));
        lines.Add(string.Empty);
        lines.Add($"[{ScreenPalette.Label}]⏎ install · Esc cancel — nothing is written until you press ⏎[/]");
        return lines;

        string Row(string label, string value) =>
            $"  [{ScreenPalette.Label}]{label.PadRight(11)}[/][{ScreenPalette.Value}]{Escape(Elide(value, Math.Max(10, width - 14)))}[/]";
    }

    /// <summary>The installed packages, the selected one's detail, and the keys.</summary>
    internal static List<string> List(
        IReadOnlyList<InstalledPackage> packages,
        IReadOnlyCollection<string> disabled,
        int selected,
        bool confirmingRemove,
        Func<InstalledPackage, IReadOnlyList<string>> characters,
        int width)
    {
        var lines = new List<string>
        {
            $"[{ScreenPalette.Label}]{Count(packages.Count)} installed · ↑↓ select · Space on/off · c copy sets to yours · Del remove · b browse · Esc close[/]",
            string.Empty,
        };

        if (packages.Count == 0)
        {
            lines.Add($"[{ScreenPalette.Label}]none — b browses the package index; ⌃P ▸ Install package… takes a file or an https link[/]");
            return lines;
        }

        for (var i = 0; i < packages.Count; i++)
        {
            var package = packages[i];
            var on = !disabled.Contains(package.Id);
            var marker = i == selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            var box = on ? "[[x]]" : "[[ ]]";
            lines.Add($"{marker} {box} [{(i == selected ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(Elide(package.Manifest.Name, 30))}[/]"
                + $" [{ScreenPalette.Label}]{Escape(package.Manifest.Version)} · {Escape(Elide(string.Join(", ", package.Manifest.Games), Math.Max(10, width - 50)))}[/]");
        }

        var current = packages[Math.Clamp(selected, 0, packages.Count - 1)];
        lines.Add(string.Empty);
        lines.Add($"[bold {ScreenPalette.Accent}]{Escape(Elide(current.Manifest.Name, width - 4))}[/]");
        if (!string.IsNullOrWhiteSpace(current.Manifest.Description))
        {
            lines.Add($"  [{ScreenPalette.Value}]{Escape(Elide(current.Manifest.Description, width - 4))}[/]");
        }

        lines.Add($"  [{ScreenPalette.Label}]sets[/]       [{ScreenPalette.Value}]{Escape(Elide(string.Join(", ", current.Sets.Select(s => s.Name)), width - 14))}[/]");
        var who = characters(current);
        lines.Add($"  [{ScreenPalette.Label}]characters[/] [{ScreenPalette.Value}]{Escape(Elide(who.Count == 0 ? "none" : string.Join(", ", who), width - 14))}[/]");
        if (confirmingRemove)
        {
            lines.Add(string.Empty);
            lines.Add($"[bold {ScreenPalette.Warn}]Remove {Escape(current.Manifest.Name)} and its files? Del again removes it, any other key keeps it.[/]");
        }

        return lines;
    }

    /// <summary>The index's packages, the focused world's first, with what is installed beside each.</summary>
    internal static List<string> Browse(
        IReadOnlyList<PackageIndexEntry> entries,
        IReadOnlyList<InstalledPackage> installed,
        int selected,
        string? world,
        int dropped,
        int width)
    {
        var lines = new List<string>
        {
            $"[{ScreenPalette.Label}]{Of(entries.Count, "package")}"
                + (world is null ? string.Empty : $" · {Escape(Elide(world, 30))}'s first")
                + " · ↑↓ select · ⏎ review · Esc close[/]",
        };

        if (dropped > 0)
        {
            lines.Add($"[{ScreenPalette.Warn}]{(dropped == 1 ? "1 entry in the index breaks its rules and is" : $"{Count(dropped)} entries in the index break its rules and are")} not listed[/]");
        }

        lines.Add(string.Empty);
        if (entries.Count == 0)
        {
            lines.Add($"[{ScreenPalette.Label}]the index lists nothing[/]");
            return lines;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var have = installed.FirstOrDefault(p => p.Id == entry.Id);
            var state = have is null ? string.Empty
                : have.Manifest.Version == entry.Version ? " · installed"
                : $" · installed {Escape(have.Manifest.Version)}, update";
            var marker = i == selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            lines.Add($"{marker} [{(i == selected ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(Elide(entry.Name, 30))}[/]"
                + $" [{ScreenPalette.Label}]{Escape(entry.Version)} · {Escape(Elide(string.Join(", ", entry.Games), Math.Max(10, width - 60)))}{state}[/]");
        }

        var current = entries[Math.Clamp(selected, 0, entries.Count - 1)];
        lines.Add(string.Empty);
        lines.Add($"[bold {ScreenPalette.Accent}]{Escape(Elide(current.Name, width - 4))}[/]"
            + (current.Author is { } author ? $" [{ScreenPalette.Label}]by {Escape(Elide(author, 40))}[/]" : string.Empty));
        if (!string.IsNullOrWhiteSpace(current.Description))
        {
            lines.Add($"  [{ScreenPalette.Value}]{Escape(Elide(current.Description, width - 4))}[/]");
        }

        lines.Add($"  [{ScreenPalette.Label}]from[/] [{ScreenPalette.Value}]{Escape(Elide(current.Url, width - 9))}[/]");
        return lines;
    }

    internal static string Of(int n, string one, string? many = null) =>
        n == 1 ? $"1 {one}" : $"{Count(n)} {many ?? one + "s"}";

    internal static string Count(int n) => n.ToString(CultureInfo.InvariantCulture);

    internal static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };

    private static string Elide(string text, int width) =>
        text.Length <= width ? text : width <= 1 ? "…" : text[..(width - 1)] + "…";
}
