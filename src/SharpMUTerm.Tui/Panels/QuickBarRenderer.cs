using System.Globalization;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Automation;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui.Panels;

/// <summary>
/// The quick bar: one row of the character's function-key macros — <c>F1 kick  F2 bash  F3 cure light</c> — with
/// <c>F4 edits keys</c> at the right. Each entry is a link (<see cref="LinkPrefix"/> and its index) that the
/// panel's own handler runs through the macro path the key itself takes. Elided to the width, never wrapped. Drawn
/// from a <see cref="QuickBarModel"/>, the one the window's native buttons are drawn from, so entry N is the same
/// binding in both.
/// </summary>
internal static class QuickBarRenderer
{
    /// <summary>The scheme an entry's link carries, followed by its index in the list drawn.</summary>
    internal const string LinkPrefix = "mux:quick:";

    /// <summary>The hint's link: a click opens the F4 screen (<see cref="QuickBarModel.EditCommand"/>), as the window's does.</summary>
    internal const string EditLink = LinkPrefix + "edit";

    /// <summary>The longest a macro's name is drawn before it is cut.</summary>
    internal const int MaxNameWidth = 16;

    /// <summary>The right-hand hint naming where the keys are edited.</summary>
    internal const string Hint = QuickBarModel.EditHint;

    /// <summary>
    /// The function-key bindings to show, from the bindings that resolve: one per key, plain keys before modified
    /// ones, then by number. A binding with neither a command nor a script is left out, as the key path leaves it.
    /// </summary>
    internal static QuickBarModel Entries(HostKeys keys, IEnumerable<Macro> macros, Func<string, Macro?> resolve)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(QuickBarItem Entry, int Modifiers, int Number)>();
        foreach (var candidate in macros)
        {
            if (!MacroKey.TryParse(candidate.Key, out var parts)
                || parts.Key.Length < 2 || parts.Key[0] != 'F'
                || !int.TryParse(parts.Key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                continue;
            }

            var descriptor = MacroKey.Describe(parts.Key, parts.Ctrl, parts.Alt, parts.Shift);
            if (!seen.Add(descriptor)
                || resolve(descriptor) is not { } macro
                || (macro.Command.Length == 0 && string.IsNullOrEmpty(macro.ScriptCallback)))
            {
                continue;
            }

            var name = macro.Name.Trim().Length > 0 ? macro.Name.Trim() : macro.Command.Trim();
            if (name.Length == 0)
            {
                name = macro.ScriptCallback ?? descriptor;
            }

            var key = (parts.Ctrl ? "⌃" : string.Empty) + (parts.Alt ? "⌥" : string.Empty) + (parts.Shift ? "⇧" : string.Empty) + parts.Key;
            var modifiers = (parts.Ctrl ? 1 : 0) + (parts.Alt ? 2 : 0) + (parts.Shift ? 4 : 0);
            entries.Add((new QuickBarItem(key, descriptor, Single(name), MacroKeys.Verdict(keys, descriptor).Fires), modifiers, number));
        }

        return new QuickBarModel(entries.OrderBy(e => e.Modifiers).ThenBy(e => e.Number).Select(e => e.Entry).ToList());
    }

    /// <summary>The row for <paramref name="width"/> cells. Entries that do not fit are counted, never wrapped.</summary>
    internal static string Render(QuickBarModel model, int width, string accent)
    {
        var entries = model.Items;
        width = Math.Max(1, width);
        var hint = $"[link={EditLink}][bold {accent}]{QuickBarModel.EditKey}[/][dim]{QuickBarModel.EditHint[QuickBarModel.EditKey.Length..]}[/][/]";
        if (entries.Count == 0)
        {
            return ElideVisible($"[dim]{QuickBarModel.EmptyText} · [/]{hint}", width);
        }

        var cells = entries.Select((e, i) => Cell(e, i, accent)).ToList();
        var hintWidth = VisibleLength(hint);
        foreach (var withHint in new[] { true, false })
        {
            var room = withHint ? width - hintWidth - 2 : width;
            var row = Fit(cells, room);
            if (row is not null)
            {
                return withHint ? SpreadLR(row, hint, width) : row;
            }
        }

        return ElideVisible(cells[0], width);
    }

    /// <summary>The entry index a link names, or null for any other link.</summary>
    internal static int? ParseLink(string url) =>
        url.StartsWith(LinkPrefix, StringComparison.Ordinal)
        && int.TryParse(url.AsSpan(LinkPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : null;

    /// <summary>The most entries that fit <paramref name="room"/> with a <c>+N</c> for the rest, or null for none.</summary>
    private static string? Fit(List<string> cells, int room)
    {
        for (var take = cells.Count; take > 0; take--)
        {
            var row = string.Join("  ", cells.Take(take));
            if (take < cells.Count)
            {
                row += $"  [dim]+{(cells.Count - take).ToString(CultureInfo.InvariantCulture)}[/]";
            }

            if (VisibleLength(row) <= room)
            {
                return row;
            }
        }

        return null;
    }

    private static string Cell(QuickBarItem entry, int index, string accent)
    {
        var name = entry.Title.Length <= MaxNameWidth ? entry.Title : entry.Title[..(MaxNameWidth - 1)] + "…";
        var link = $"[link={LinkPrefix}{index.ToString(CultureInfo.InvariantCulture)}]";
        return entry.Fires
            ? $"{link}[bold {accent}]{Escape(entry.Key)}[/] {Escape(name)}[/]"
            : $"{link}[dim]{Escape(entry.Key)} {Escape(name)}[/][/]";
    }

    /// <summary>A name on one line: a multi-command macro's line breaks become <c>; </c>.</summary>
    private static string Single(string name) =>
        string.Join("; ", name.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}
