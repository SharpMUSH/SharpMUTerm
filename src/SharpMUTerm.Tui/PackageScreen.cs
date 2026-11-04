using System.Globalization;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Packages;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// What the package surfaces say, as markup rows: the review a player reads before anything is installed, the list
/// of what is installed and the index. Each is drawn from a Client model or F10's declaration — the same ones the
/// window draws — and is pure, so a test reads the words the player would.
/// </summary>
internal static class PackageScreen
{
    /// <summary>
    /// The review, drawn from <see cref="PackageReviewModel"/> — the model the window's review card draws, so the two
    /// say the same facts in the same order. It is the consent step, so it leads with what can act as the player: the
    /// rules that send commands and the scripts that run, then what else it adds and who it applies to.
    /// </summary>
    internal static List<string> Review(PackageReviewModel model, int width)
    {
        var lines = new List<string>
        {
            $"[bold {ScreenPalette.Accent}]{Escape(Elide(model.Name, width))}[/] [{ScreenPalette.Label}]{Escape(model.Version)}"
                + (model.Author is { } author ? $" · by {Escape(Elide(author, 40))}" : string.Empty) + "[/]",
        };

        if (model.Replaces is { } replaces)
        {
            lines.Add($"[{ScreenPalette.Label}]replaces version {Escape(replaces)}, and its sets with it[/]");
        }

        if (model.Description is { } description)
        {
            lines.Add($"[{ScreenPalette.Value}]{Escape(Elide(description, width))}[/]");
        }

        foreach (var section in model.Sections)
        {
            lines.Add(string.Empty);
            lines.Add($"[bold {ScreenPalette.Accent}]{Escape(section.Heading)}[/]");
            foreach (var row in section.Rows)
            {
                lines.Add(ReviewRow(row, width));
            }
        }

        return lines;
    }

    /// <summary>
    /// The rows pinned under the review, outside its scroll (the import review's shape): a rule, then the keys that answer
    /// it and that nothing is written until ⏎. Pinned because this is a consent screen — at 80×24 the body filled the
    /// screen down to the command line, and a longer package would have scrolled its way to accept out of sight.
    /// </summary>
    internal static List<string> ReviewFooter(int width)
    {
        width = Math.Max(30, width);
        var keys = new List<LadderKey>
        {
            new("⏎", "install", Drop: LadderKey.Keep),
            new("↑↓", "scroll", Drop: 1),
            new("Esc", "cancel", Drop: LadderKey.Keep),
        };
        var ladder = KeyLadder.Fit(keys, width - 1, k => $"[{ScreenPalette.Accent}]{Escape(k)}[/]", Label, "   ");
        var note = $"[{ScreenPalette.Label}]— nothing is written until you press ⏎[/]";
        return
        [
            $"[{ScreenPalette.Rule}]{new string('─', width - 1)}[/]",
            VisibleLength(ladder) + 3 + VisibleLength(note) <= width - 1 ? $"{ladder}   {note}" : ladder,
        ];
    }

    private static string Label(string text) => $"[{ScreenPalette.Label}]{Escape(text)}[/]";

    private static string ReviewRow(PackageReviewRow row, int width)
    {
        var ink = row.Tone switch
        {
            PackageReviewTone.Warning => $"bold {ScreenPalette.Warn}",
            PackageReviewTone.Quiet => ScreenPalette.Label,
            _ => ScreenPalette.Value,
        };
        return row.Label.Length == 0
            ? $"  [{ink}]{Escape(Elide(row.Value, Math.Max(10, width - 4)))}[/]"
            : $"  [{ScreenPalette.Label}]{row.Label.PadRight(11)}[/][{ink}]{Escape(Elide(row.Value, Math.Max(10, width - 14)))}[/]";
    }

    /// <summary>
    /// The installed packages, the selected one's detail, and the keys — all drawn from F10's declaration: the key line
    /// is its actions, a row is its check box and row fields, the detail is every other field under its label.
    /// </summary>
    /// <param name="drawn">When given, receives the ids of the fields drawn, in order — what a test compares with the window's.</param>
    internal static List<string> List(
        PackageCollection declaration,
        AppConfiguration config,
        IReadOnlyList<PackageItem> packages,
        int selected,
        bool confirmingRemove,
        int width,
        List<string>? drawn = null)
    {
        // One row, never wrapped: a ladder after the count (↑↓ goes first, then the copy and browse keys).
        var count = $"{Count(packages.Count)} installed · ";
        var ladder = new List<LadderKey> { new("↑↓", "select", Drop: 1) };
        ladder.AddRange(declaration.Actions.Select(a => new LadderKey(
            a.Key,
            a.Label,
            a.Label.Contains(' ', StringComparison.Ordinal) ? a.Label[..a.Label.IndexOf(' ', StringComparison.Ordinal)] : null,
            a.Key switch { "c" => 2, "b" => 3, _ => LadderKey.Keep })));
        ladder.Add(new("Esc", "close"));
        var keys = KeyLadder.Fit(ladder, Math.Max(20, width - count.Length - 1), Escape, Escape, " · ");
        var lines = new List<string>
        {
            $"[{ScreenPalette.Label}]{count}{keys}[/]",
            string.Empty,
        };

        if (packages.Count == 0)
        {
            lines.Add($"[{ScreenPalette.Label}]none — b browses the package index; ⌃P ▸ Install package… takes a file or an https link[/]");
            return lines;
        }

        string Read(string id, PackageItem item) =>
            declaration.Field(id) is InfoField info ? info.Read(new SettingTarget(config, Item: item)) : string.Empty;

        var enabled = (SettingBinding<bool>)declaration.Field(declaration.EnabledField).Binding!;
        var name = declaration.RowFields[0];
        var after = declaration.RowFields.Skip(1).ToList();
        for (var i = 0; i < packages.Count; i++)
        {
            var package = packages[i];
            var on = enabled.Get(new SettingTarget(config, Item: package));
            var marker = i == selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            var box = on ? "[[x]]" : "[[ ]]";
            var tail = string.Join(" · ", after.Select(id => Read(id, package)));
            lines.Add($"{marker} {box} [{(i == selected ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(Elide(Read(name, package), 30))}[/]"
                + $" [{ScreenPalette.Label}]{Escape(Elide(tail, Math.Max(10, width - 40)))}[/]");
        }

        drawn?.Add(declaration.EnabledField);
        drawn?.AddRange(declaration.RowFields);
        var current = packages[Math.Clamp(selected, 0, packages.Count - 1)];
        lines.Add(string.Empty);
        lines.Add($"[bold {ScreenPalette.Accent}]{Escape(Elide(Read(name, current), width - 4))}[/]");
        foreach (var field in declaration.DetailFields)
        {
            drawn?.Add(field.Id);
            lines.Add($"  [{ScreenPalette.Label}]{field.Label.PadRight(11)}[/]"
                + $"[{ScreenPalette.Value}]{Escape(Elide(Read(field.Id, current), Math.Max(10, width - 14)))}[/]");
        }

        // The question's rows are always there, blank until Del asks it: the modal is sized once, when it opens, and a
        // question added later would land under its bottom edge. Elided to one row for the same reason.
        lines.Add(string.Empty);
        lines.Add(confirmingRemove
            ? $"[bold {ScreenPalette.Warn}]{Escape(Elide($"Remove {Read(name, current)} and its files? Del again removes it, any other key keeps it.", width))}[/]"
            : string.Empty);

        return lines;
    }

    /// <summary>The index's packages, the focused world's first, with what is installed beside each: the browse model drawn.</summary>
    internal static List<string> Browse(PackageBrowseModel model, int selected, int width)
    {
        var entries = model.Rows;
        var lines = new List<string>
        {
            $"[{ScreenPalette.Label}]{Of(entries.Count, "package")}"
                + (model.World is null ? string.Empty : $" · {Escape(Elide(model.World, 30))}'s first")
                + " · ↑↓ select · ⏎ review · Esc close[/]",
        };

        if (model.Dropped > 0)
        {
            lines.Add($"[{ScreenPalette.Warn}]{(model.Dropped == 1 ? "1 entry in the index breaks its rules and is" : $"{Count(model.Dropped)} entries in the index break its rules and are")} not listed[/]");
        }

        lines.Add(string.Empty);
        if (entries.Count == 0)
        {
            lines.Add($"[{ScreenPalette.Label}]the index lists nothing[/]");
            return lines;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var row = entries[i];
            var entry = row.Entry;
            var state = row.StateText.Length == 0 ? string.Empty : " · " + Escape(row.StateText);
            var marker = i == selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            lines.Add($"{marker} [{(i == selected ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(Elide(entry.Name, 30))}[/]"
                + $" [{ScreenPalette.Label}]{Escape(entry.Version)} · {Escape(Elide(string.Join(", ", entry.Games), Math.Max(10, width - 60)))}{state}[/]");
        }

        var current = entries[Math.Clamp(selected, 0, entries.Count - 1)].Entry;
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

    private static string Elide(string text, int width) =>
        text.Length <= width ? text : width <= 1 ? "…" : text[..(width - 1)] + "…";
}
