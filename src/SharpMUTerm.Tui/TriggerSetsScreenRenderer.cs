using System.Globalization;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using static SharpMUTerm.Tui.MarkupText;
using static SharpMUTerm.Tui.ScreenPalette;

namespace SharpMUTerm.Tui;

/// <summary>
/// The trigger sets screen: the sets on the left (name, description, scripts, what each holds and who uses it), and on
/// the right the selected set's wells and its USED BY checklist. Opened with <c>s</c> from F2, F3, F4 and F6 over the
/// screen that asked, so a set is made, renamed and given its characters where its rules are written. F5 keeps its own
/// set pane, which is the same thing seen from one character.
/// <para>
/// What a set is, who uses it and how deleting one is described are <see cref="TriggerSetEditing"/>'s, the words the
/// window's set page uses too.
/// </para>
/// </summary>
internal static class TriggerSetsScreenRenderer
{
    /// <summary>The set list.</summary>
    internal const int SetsPane = 0;

    /// <summary>The selected set's USED BY checklist, one row a character.</summary>
    internal const int UsersPane = 1;

    /// <summary>The set row's fields, in ⇥ order.</summary>
    internal const int NameField = 0;

    internal const int DescriptionField = 1;

    internal const int ScriptsField = 2;

    internal const string Title = "Trigger sets";

    internal const string AddSetLabel = "+ set";

    internal const int ColumnWidth = 50;

    internal const int MinColumnWidth = 30;

    internal const int MinEditorWidth = 34;

    private const string NoDescription = "no description";

    private const string NoScripts = "no scripts";

    /// <summary>
    /// The screen's panes. <paramref name="usedBy"/> is the character a new set is used by (the one in front), or null.
    /// </summary>
    internal static ScreenModel Model(
        IReadOnlyList<WorldDefinition> worlds,
        IList<TriggerSet> sets,
        int selected,
        CharacterDefinition? usedBy)
    {
        ArgumentNullException.ThrowIfNull(worlds);
        ArgumentNullException.ThrowIfNull(sets);

        var rows = ScreenModel.Rows(sets.ToList(), set => ScreenRow.Of(
            ScreenField.UniqueName(
                "set name",
                () => set.Name,
                value => Rename(worlds, set, value),
                sets.Where(s => !ReferenceEquals(s, set)).Select(s => s.Name).ToArray()),
            ScreenField.Optional("description", () => set.Description, v => set.Description = v),
            ScreenField.Optional("scripts", () => WorldsScreenRenderer.JoinScripts(set), v => WorldsScreenRenderer.SplitScripts(set, v))))
            .Concat(Buttons(worlds, sets, selected, usedBy))
            .ToArray();

        if (selected < 0 || selected >= sets.Count)
        {
            return new ScreenModel(rows, Array.Empty<ScreenRow>());
        }

        var name = sets[selected].Name;
        var users = TriggerSetEditing.Users(worlds, name)
            .Select(u => ScreenRow.Of(new ScreenToggle(
                () => TriggerSetEditing.Uses(u.Character, name),
                () => TriggerSetEditing.Use(u.Character, name, !TriggerSetEditing.Uses(u.Character, name)))))
            .ToArray();

        return new ScreenModel(rows, users);
    }

    /// <summary>
    /// Renames the set and every character's reference to it, each kept where it was in that character's list —
    /// F5's rename, so undo replays it with the old name.
    /// </summary>
    private static void Rename(IReadOnlyList<WorldDefinition> worlds, TriggerSet set, string name)
    {
        var renamed = name.Trim();
        TriggerSetReferences.Rename(TriggerSetReferences.Find(worlds, set.Name), renamed);
        set.Name = renamed;
    }

    /// <summary>
    /// <c>[+ set]</c>, which makes an empty set named apart from the others and has the character in front use it — a
    /// set nobody uses does nothing, and the player making one here is about to write a rule into it — and Del, which
    /// takes the set and every character's use of it, both put back by the closing review's undo.
    /// </summary>
    private static List<ScreenRow> Buttons(
        IReadOnlyList<WorldDefinition> worlds, IList<TriggerSet> sets, int selected, CharacterDefinition? usedBy)
    {
        var rows = new List<ScreenRow>();
        if (sets.IsReadOnly)
        {
            return rows;
        }

        rows.Add(ScreenRow.Of(new ScreenButton(
            AddSetLabel,
            () =>
            {
                var set = new TriggerSet
                {
                    Name = ScreenLists.Available(sets.Select(s => s.Name), TriggerSetEditing.NewSetName),
                };
                sets.Add(set);
                if (usedBy is not null)
                {
                    TriggerSetEditing.Use(usedBy, set.Name, on: true);
                }

                return new ScreenPress(null, sets.Count - 1);
            },
            ScreenButtonKind.Add,
            usedBy is null ? null : "used by " + usedBy.Name)));

        if (selected >= 0 && selected < sets.Count)
        {
            var set = sets[selected];
            var index = selected;
            rows.Add(ScreenRow.Of(new ScreenButton(
                ScreenButton.RemoveKeyLabel,
                () =>
                {
                    var name = set.Name;
                    var references = TriggerSetReferences.Find(worlds, name);
                    sets.RemoveAt(index);
                    TriggerSetReferences.Detach(references);
                    return new ScreenPress(
                        () =>
                        {
                            sets.Insert(Math.Clamp(index, 0, sets.Count), set);
                            TriggerSetReferences.Reattach(references, name);
                        },
                        index);
                },
                ScreenButtonKind.Remove,
                set.Name,
                () => TriggerSetEditing.Deletion(worlds, set))));
        }

        return rows;
    }

    /// <summary>The title and the keys, the keys derived from the model so the header offers nothing the screen lacks.</summary>
    internal static string HeaderLine(int width, ScreenModel? model = null, ScreenFocus? focus = null)
    {
        var title = $"[bold {Value}]{Title}[/]";
        var hints = ScreenChrome.Hints(
            ScreenChrome.ListHints, "Esc", model?.HasEditableRow ?? false, focus, model?.HasRemovableRow ?? false,
            room: ScreenChrome.HeaderRoom(" " + title, width), back: true);
        return SpreadTitle(" " + title, hints, width);
    }

    /// <summary>The action bar: which set is selected, and the screen's actions.</summary>
    internal static string FooterLine(IReadOnlyList<TriggerSet> sets, int selected, int width, ScreenFocus? focus = null)
    {
        var context = selected >= 0 && selected < sets.Count
            ? ScreenChrome.Context(ScreenChrome.Position("set", selected, sets.Count), Escape(sets[selected].Name))
            : string.Empty;
        return SpreadLR(" " + context, ScreenChrome.Actions(focus: focus), width);
    }

    /// <summary>The set list: what each holds and who uses it, a set nobody uses said in the warning ink.</summary>
    internal static List<string> ListColumn(
        IReadOnlyList<WorldDefinition> worlds,
        IList<TriggerSet> sets,
        int selected,
        CharacterDefinition? usedBy,
        ScreenFocus? focus = null,
        int width = ColumnWidth)
    {
        var cursor = focus ?? ScreenFocus.None;
        var lines = new List<string> { $"[{Label}]{Escape(TriggerSetEditing.Explainer)}[/]", string.Empty };
        if (sets.Count == 0)
        {
            lines.Add($"[{Label}]no trigger set yet — make one with [/][{Accent}][[{Escape(AddSetLabel)}]][/]");
        }

        for (var i = 0; i < sets.Count; i++)
        {
            var set = sets[i];
            var marker = i == selected ? $"[bold {Accent}]▸[/]" : " ";
            var name = ScreenChrome.Field(
                $"[{(i == selected ? "bold " : string.Empty)}{Value}]{Escape(set.Name)}[/]",
                cursor.EditOn(SetsPane, i, NameField));
            var usage = TriggerSetEditing.Usage(worlds, set.Name);
            var usageInk = TriggerSetEditing.IsUsed(worlds, set.Name) ? Label : Warn;
            lines.Add(ScreenChrome.Cursor($"{marker} {name}", cursor.IsOn(SetsPane, i), width));
            lines.Add(ScreenChrome.Cursor(
                FitVisible($"    [{usageInk}]{Escape(usage)}[/][{Label}] · {Escape(TriggerSetEditing.Holds(set))}[/]", width),
                cursor.IsOn(SetsPane, i),
                width));
        }

        lines.Add(string.Empty);
        lines.AddRange(ScreenChrome.Buttons(Buttons(worlds, sets, selected, usedBy), cursor, SetsPane, sets.Count, width));
        return lines;
    }

    /// <summary>The selected set's wells and its USED BY checklist, or nothing when no set is selected.</summary>
    internal static List<string> DetailColumn(
        IReadOnlyList<WorldDefinition> worlds,
        IReadOnlyList<TriggerSet> sets,
        int selected,
        ScreenFocus? focus = null,
        int width = ColumnWidth,
        int height = 0)
    {
        var cursor = focus ?? ScreenFocus.None;
        if (selected < 0 || selected >= sets.Count)
        {
            return new List<string>();
        }

        var set = sets[selected];
        var lines = new List<string>
        {
            FitVisible($"[bold {Value}]{Escape(set.Name)}[/]", width),
            FitVisible($"[{Label}]holds {Escape(TriggerSetEditing.Holds(set))}[/]", width),
            string.Empty,
            $"[{Label}]description[/]",
            "  " + ScreenChrome.Field(
                $"[{Label}]{Escape(set.Description ?? NoDescription)}[/]",
                cursor.EditOn(SetsPane, selected, DescriptionField)),
            $"[{Label}]scripts[/]",
            "  " + ScreenChrome.Field(
                $"[{Label}]{Escape(WorldsScreenRenderer.JoinScripts(set) ?? NoScripts)}[/]",
                cursor.EditOn(SetsPane, selected, ScriptsField)),
            string.Empty,
            $"[bold {Label}]USED BY[/]",
            FitVisible($"[{Label}]Space ticks · the first ticked set wins a clash[/]", width),
        };

        var users = TriggerSetEditing.Users(worlds, set.Name);
        if (users.Count == 0)
        {
            lines.Add($"[{Label}]no characters yet — add one on F5[/]");
        }

        for (var i = 0; i < users.Count; i++)
        {
            var user = users[i];
            var box = user.Uses
                ? $"[{Accent}][[x]][/] [{Value}]{Escape(user.Name)}[/]"
                : $"[{Label}][[ ]] {Escape(user.Name)}[/]";
            if (user.Uses && user.Character.TriggerSets.Count > 1)
            {
                var order = user.Character.TriggerSets.FindIndex(n => string.Equals(n, set.Name, StringComparison.OrdinalIgnoreCase));
                box += $" [{Label}]#{(order + 1).ToString(CultureInfo.InvariantCulture)} of {user.Character.TriggerSets.Count.ToString(CultureInfo.InvariantCulture)}[/]";
            }

            lines.Add(ScreenChrome.Cursor(box, cursor.IsOn(UsersPane, i), width));
        }

        if (users.Count > 0 && !users.Any(u => u.Uses))
        {
            lines.Add(string.Empty);
            lines.Add($"[{Warn}]No character uses {Escape(set.Name)}, so its rules do nothing yet.[/]");
        }

        return ScreenChrome.Compact(lines, height);
    }
}
