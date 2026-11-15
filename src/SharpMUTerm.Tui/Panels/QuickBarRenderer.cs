using System.Globalization;
using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Automation;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui.Panels;

/// <summary>
/// The quick bar: one row of every key the character has bound — <c>F11 kick  ⌃F1 score │ ⌃E look │ Num7 northwest …</c> — with
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

    /// <summary>
    /// The <c>+N</c> count's link, followed by the index of the first binding it holds: a click lists the rest
    /// (<c>SharpMUTermApp.OpenQuickBarMore</c>), as the window's <c>+N</c> opens a menu of them.
    /// </summary>
    internal const string MoreLink = LinkPrefix + "more:";

    /// <summary>The longest a macro's name is drawn before it is cut.</summary>
    internal const int MaxNameWidth = 16;

    /// <summary>The right-hand hint naming where the keys are edited.</summary>
    internal const string Hint = QuickBarModel.EditHint;

    /// <summary>
    /// The bindings to show, from the bindings that resolve: one per key, every key a binding can have — function keys
    /// (plain before modified, then by number), then other chords (by modifiers, then key), then the numeric keypad in the
    /// pad's order (7 8 9, 4 5 6, 1 2 3, 0). A binding with neither a command nor a script is left out, as the key path
    /// leaves it, and so is one another binding on the same key shadows.
    /// </summary>
    internal static QuickBarModel Entries(HostKeys keys, IEnumerable<Macro> macros, Func<string, Macro?> resolve)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<(QuickBarItem Entry, int Modifiers, int Order, string Name)>();
        foreach (var candidate in macros)
        {
            if (!MacroKey.TryParse(candidate.Key, out var parts) || parts.Key.Length == 0)
            {
                continue;
            }

            var descriptor = MacroKey.Describe(parts.Key, parts.Ctrl, parts.Alt, parts.Shift);
            if (!seen.Add(descriptor)
                || resolve(descriptor) is not { } macro
                || (macro.Command.Trim().Length == 0 && string.IsNullOrEmpty(macro.ScriptCallback)))
            {
                continue;
            }

            var name = macro.Name.Trim().Length > 0 ? macro.Name.Trim() : macro.Command.Trim();
            if (name.Length == 0)
            {
                name = macro.ScriptCallback ?? descriptor;
            }

            var title = Single(name);
            var (group, order) = Place(parts.Key);
            var key = (parts.Ctrl ? "⌃" : string.Empty) + (parts.Alt ? "⌥" : string.Empty) + (parts.Shift ? "⇧" : string.Empty) + parts.Key;
            var modifiers = (parts.Ctrl ? 1 : 0) + (parts.Alt ? 2 : 0) + (parts.Shift ? 4 : 0);
            var item = new QuickBarItem(key, descriptor, title, MacroKeys.Verdict(keys, descriptor).Fires, macro.Danger, group,
                QuickBarItem.ShortFor(title) ?? QuickBarItem.ShortFor(macro.Command));
            entries.Add((item, modifiers, order, parts.Key));
        }

        return new QuickBarModel(entries
            .OrderBy(e => e.Entry.Group)
            .ThenBy(e => e.Entry.Group == QuickBarGroup.Keypad ? e.Order : e.Modifiers)
            .ThenBy(e => e.Entry.Group == QuickBarGroup.Keypad ? e.Modifiers : e.Order)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(e => e.Entry)
            .ToList());
    }

    /// <summary>The keypad's digits in the order the pad is laid out, top row first.</summary>
    private const string PadOrder = "7894561230";

    /// <summary>Which run a base key goes in, and its place there (a function key's number, a pad digit's position).</summary>
    private static (QuickBarGroup Group, int Order) Place(string key)
    {
        if (key.Length >= 2 && key[0] == 'F'
            && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return (QuickBarGroup.Function, number);
        }

        if (key.Length == 4 && key.StartsWith("Num", StringComparison.Ordinal) && PadOrder.IndexOf(key[3]) is >= 0 and var at)
        {
            return (QuickBarGroup.Keypad, at);
        }

        return (QuickBarGroup.Chord, 0);
    }

    /// <summary>The row for <paramref name="width"/> cells. Entries that do not fit are counted, never wrapped.</summary>
    /// <param name="model">What to draw.</param>
    /// <param name="width">The row's width in cells.</param>
    /// <param name="accent">
    /// The key's ink. The client passes a held muted grey: the terminal mockup draws <c>F1</c> quiet and <c>kick</c> in
    /// the body ink, so the names read first and the accent stays the client's voice for state.
    /// </param>
    /// <param name="danger">The ink a dangerous binding's key and name take; the accent when null.</param>
    internal static string Render(QuickBarModel model, int width, string accent, string? danger = null)
    {
        // A cell of margin at either end once the row can spare it, the terminal mockup's padded strip.
        width = Math.Max(1, width);
        return width >= MarginFromWidth ? " " + Row(model, width - 2, accent, danger) + " " : Row(model, width, accent, danger);
    }

    /// <summary>The narrowest row that keeps a cell of margin at either end.</summary>
    internal const int MarginFromWidth = 24;

    /// <summary>What sits between two runs (<see cref="QuickBarGroup"/>): a dim rule with a cell either side.</summary>
    private const string GroupRule = " [dim]│[/] ";

    /// <summary>What sits between two bindings of one run.</summary>
    private const string ItemGap = "  ";

    /// <summary>
    /// The row, laddered: every binding at full wording with the hint, then at the short wording (a direction as
    /// <c>nw</c>, the keypad's keys as <c>Num 7 nw  8 n</c>), then each of those without the hint, then as many as fit
    /// short with <c>+N</c> for the rest, without the hint. The bindings are what the bar is for, so the hint goes before
    /// any of them.
    /// </summary>
    private static string Row(QuickBarModel model, int width, string accent, string? danger)
    {
        var entries = model.Items;
        var hint = $"[link={EditLink}][{accent}]{QuickBarModel.EditKey}[/][dim]{QuickBarModel.EditHint[QuickBarModel.EditKey.Length..]}[/][/]";
        if (entries.Count == 0)
        {
            return ElideVisible($"[dim]{QuickBarModel.EmptyText} · [/]{hint}", width);
        }

        var hintWidth = VisibleLength(hint);
        foreach (var withHint in new[] { true, false })
        {
            foreach (var shortForm in new[] { false, true })
            {
                var row = Join(entries, entries.Count, shortForm, accent, danger ?? accent);
                if (VisibleLength(row) <= Room(withHint))
                {
                    return withHint ? SpreadLR(row, hint, width) : row;
                }
            }
        }

        // Past that a binding has to go, and in the cells a hidden one cannot be clicked (+N is a count, not a menu), so
        // the hint goes first: F4 is on ⌃P as well.
        foreach (var withHint in new[] { false, true })
        {
            for (var take = entries.Count - 1; take > 0; take--)
            {
                var row = Join(entries, take, true, accent, danger ?? accent)
                    + $"{ItemGap}[link={MoreLink}{take.ToString(CultureInfo.InvariantCulture)}][dim]+{(entries.Count - take).ToString(CultureInfo.InvariantCulture)}[/][/]";
                if (VisibleLength(row) <= Room(withHint))
                {
                    return withHint ? SpreadLR(row, hint, width) : row;
                }
            }
        }

        return ElideVisible(Join(entries, 1, true, accent, danger ?? accent), width);

        int Room(bool withHint) => withHint ? width - hintWidth - 2 : width;
    }

    /// <summary>The first <paramref name="take"/> bindings as one row, a rule between runs.</summary>
    private static string Join(IReadOnlyList<QuickBarItem> entries, int take, bool shortForm, string accent, string danger)
    {
        var row = new System.Text.StringBuilder();
        for (var i = 0; i < take; i++)
        {
            var entry = entries[i];
            var startsRun = i == 0 || entries[i - 1].Group != entry.Group;
            if (i > 0)
            {
                row.Append(startsRun ? GroupRule : ItemGap);
            }

            // Short, a keypad run says "Num" once and each key is its digit: `Num 7 nw  8 n  9 ne`.
            var keypadShort = shortForm && entry.Group == QuickBarGroup.Keypad && entry.ShortKey != entry.Key;
            if (keypadShort && startsRun)
            {
                row.Append("[dim]Num[/] ");
            }

            row.Append(Cell(entry, i, keypadShort ? entry.ShortKey : entry.Key, entry.Label(shortForm), accent, danger));
        }

        return row.ToString();
    }

    /// <summary>The first hidden binding's index a <c>+N</c> link names, or null for any other link.</summary>
    internal static int? ParseMoreLink(string url) =>
        url.StartsWith(MoreLink, StringComparison.Ordinal)
        && int.TryParse(url.AsSpan(MoreLink.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var first)
            ? first
            : null;

    /// <summary>
    /// The cell list a <c>+N</c> opens: a digit, the key and the binding's name per hidden binding, the selected one marked,
    /// and the keys. The same entries, in the same order, as the window's <c>+N</c> menu.
    /// </summary>
    internal static List<string> MoreLines(IReadOnlyList<QuickBarItem> hidden, int selected)
    {
        var lines = new List<string>();
        var keyWidth = hidden.Count == 0 ? 0 : hidden.Max(i => i.Key.Length);
        for (var i = 0; i < hidden.Count; i++)
        {
            var digit = i < 9 ? (i + 1).ToString(CultureInfo.InvariantCulture) : " ";
            var mark = i == selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            var weight = i == selected ? "bold " : string.Empty;
            var item = hidden[i];
            lines.Add($"{mark} [{ScreenPalette.Label}]{digit}  {Escape(item.Key.PadRight(keyWidth))}[/]  "
                + $"[{weight}{(item.Danger ? ScreenPalette.Warn : ScreenPalette.Value)}]{Escape(item.Title)}[/]");
        }

        lines.Add(string.Empty);
        lines.Add($"[{ScreenPalette.Label}]↑↓ select · ⏎ or a digit runs it · Esc cancels[/]");
        return lines;
    }

    /// <summary>The entry index a link names, or null for any other link.</summary>
    internal static int? ParseLink(string url) =>
        url.StartsWith(LinkPrefix, StringComparison.Ordinal)
        && int.TryParse(url.AsSpan(LinkPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : null;

    private static string Cell(QuickBarItem entry, int index, string key, string title, string accent, string danger)
    {
        var name = title.Length <= MaxNameWidth ? title : title[..(MaxNameWidth - 1)] + "…";
        var link = $"[link={LinkPrefix}{index.ToString(CultureInfo.InvariantCulture)}]";
        return entry.Fires && entry.Danger
            ? $"{link}[{danger}]{Escape(key)}[/] [{danger}]{Escape(name)}[/][/]"
            : entry.Fires
            ? $"{link}[{accent}]{Escape(key)}[/] {Escape(name)}[/]"
            // A key this host never delivers (the keypad in a terminal) is dimmed, but the binding is still a button:
            // the name keeps the body ink, because a click runs it all the same.
            : $"{link}[dim]{Escape(key)}[/] {Escape(name)}[/]";
    }

    /// <summary>A name on one line: a multi-command macro's line breaks become <c>; </c>.</summary>
    private static string Single(string name) =>
        string.Join("; ", name.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}
