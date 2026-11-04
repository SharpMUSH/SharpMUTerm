using System.Globalization;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Input;
using SharpMUTerm.Core.Protocols;
using static SharpMUTerm.Tui.MarkupText;
using static SharpMUTerm.Tui.ScreenPalette;

namespace SharpMUTerm.Tui;

/// <summary>
/// Produces the markup sub-blocks of the single-list option screens — F7 (Text &amp; display) and F8
/// (Input) — the header band, the options list (rows grouped under dim headings), and the footer action
/// bar. <see cref="OptionsScreenView"/> composes them into real panels; <see cref="Render(string, string,
/// IReadOnlyList{OptionRow})"/> merges the same blocks into one line list for the unit tests.
/// <para>
/// <b>Both screens are drawn from their declared settings sections</b> (<see cref="BuiltInSettings"/>, in
/// the Client): one row per field, a heading wherever the field's group changes, and the value read and
/// written through the field's <see cref="SettingBinding"/>. A field that differs from its default says
/// <c>● changed</c> and carries the reset <c>r</c> runs. Nothing about a row is written here any more except
/// how a kind of field is drawn, so a field added to a section appears on its screen, in the search and
/// under the reset key at once.
/// </para>
/// <para>
/// F7's <c>meters</c> field is the one drawn as more than a row: a table, one row per meter with its label,
/// value path and maximum path as three fields (⏎ opens the label, ⇥ steps on), the value now in a fourth
/// column read from the focused session's game data, <c>[[+ meter]]</c> under it, and — while the cursor is
/// in the table — the paths this connection has received, with their values, which a path field also offers
/// as its dropdown.
/// </para>
/// <para>
/// F9 was a third one, "Logging", and is gone: its rows edited one character's <see cref="LoggingSettings"/>
/// while presenting as a global preference. Logging sits in the character's own form on F5, and F9 opens
/// that screen there. See <see cref="WorldsScreenRenderer.FormColumn"/>.
/// </para>
/// </summary>
internal static class OptionsScreenRenderer
{
    /// <summary>Where a row's value starts, measured from the left edge of the list.</summary>
    private const int LabelWidth = 38;

    /// <summary>
    /// The checkbox column every row reserves, whether or not it has one: <c>"[[x]] "</c>. A value row
    /// indents past it rather than starting at the margin, so the labels of the two kinds of row line
    /// up in one column and the checkboxes read as a column of their own instead of as a ragged edge.
    /// </summary>
    private const int AffordanceWidth = 4;

    /// <summary>What a field that differs from its default says, after its value.</summary>
    internal const string ChangedMark = "● changed";

    /// <summary>
    /// A single options-list row: a toggle, a value row, both at once, a section header, a spacer, or a
    /// row drawn by its own <paramref name="Draw"/>. <paramref name="Bind"/> is the config the checkbox
    /// writes to; <paramref name="Edit"/> (or <paramref name="Fields"/>, for a row of several) is what ⏎
    /// opens. A row with neither still takes the cursor but nothing happens there.
    /// </summary>
    /// <param name="Label">What the row is called; <c>├ </c> marks a heading, empty a spacer.</param>
    /// <param name="Value">The value it shows, or null for a checkbox row.</param>
    /// <param name="Toggle">The checkbox's state, or null for none.</param>
    /// <param name="Hint">A dim note after the value.</param>
    /// <param name="Bind">What Space flips.</param>
    /// <param name="Edit">The one field ⏎ opens.</param>
    /// <param name="Fields">Several fields, ⏎ opening the first and ⇥ stepping on; wins over <paramref name="Edit"/>.</param>
    /// <param name="Button">What ⏎ runs on a button row.</param>
    /// <param name="Reset">
    /// Puts the row's setting back to its default; non-null exactly when it differs, which is also what
    /// draws <see cref="ChangedMark"/>.
    /// </param>
    /// <param name="Draw">
    /// Draws the row itself, given the open edit on it (null while navigating) and the width — the meter
    /// table's rows. Null for the ordinary checkbox and label/value shapes.
    /// </param>
    /// <param name="Static">Drawn but never a cursor stop: a table's column headings, the received-data box.</param>
    /// <param name="FieldId">The declared field the row draws, so the search can land on it.</param>
    /// <param name="Where">What the footer says while the cursor is on it, in place of <c>option n/m</c>.</param>
    public readonly record struct OptionRow(
        string Label,
        string? Value,
        bool? Toggle,
        string? Hint = null,
        ScreenToggle? Bind = null,
        ScreenField? Edit = null,
        IReadOnlyList<ScreenField>? Fields = null,
        ScreenButton? Button = null,
        Action? Reset = null,
        Func<ScreenFieldEdit?, int, string>? Draw = null,
        bool Static = false,
        string? FieldId = null,
        string? Where = null);

    /// <summary>
    /// One options screen: the title and F-key its chrome shows, the rows it lists, and — while the cursor is
    /// on a row Delete can take out (a meter) — that removal, which the model carries as its pane's hidden
    /// trailing button. <paramref name="Preview"/> is the theme under the cursor in the theme picker, or null while
    /// the cursor is elsewhere: the screen is painted in that theme while it is, which is the picker's live preview.
    /// </summary>
    internal readonly record struct OptionsScreen(
        string Title, string FKey, IReadOnlyList<OptionRow> Rows, ScreenButton? Remove = null, string? Preview = null);

    /// <summary>
    /// Merges every sub-block into one line list (header, options, footer). Used by the unit tests and
    /// as a width-agnostic fallback; the live view composes the same blocks into panels instead.
    /// </summary>
    public static List<string> Render(string title, string fkey, IReadOnlyList<OptionRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var screen = new OptionsScreen(title, fkey, rows);
        var lines = new List<string> { HeaderLine(title, fkey, 0, Model(screen)), string.Empty };
        lines.AddRange(BodyColumn(rows));
        lines.Add(string.Empty);
        lines.Add(FooterLine(rows, 0));
        return lines;
    }

    /// <summary>Merges a whole screen into one line list.</summary>
    internal static List<string> Render(OptionsScreen screen) => Render(screen.Title, screen.FKey, screen.Rows);

    /// <summary>
    /// The screen title on the left, the keyboard hints right-aligned to <paramref name="width"/>. The
    /// hints are derived from <paramref name="model"/> and <paramref name="focus"/> rather than written
    /// here, so the header cannot advertise an edit the screen doesn't offer — <c>r reset</c> appears only
    /// on a row that differs from its default, and <c>Del remove</c> only on a meter.
    /// <para>
    /// There is deliberately no <c>‹ back</c> affordance: there is no navigation stack behind a settings
    /// screen, Esc closes it, and the header already says so.
    /// </para>
    /// </summary>
    internal static string HeaderLine(
        string title, string fkey, int width, ScreenModel? model = null, ScreenFocus? focus = null)
    {
        var heading = $"[bold {Value}]{Escape(title)}[/]";
        var resettable = model is not null && focus is { Edit: null } at && model.ResetAt(at.Pane, at.Index) is not null;
        var hints = ScreenChrome.Hints(
            ScreenChrome.SingleListHints,
            Escape(fkey),
            model?.HasEditableRow ?? false,
            focus,
            removable: model?.HasRemovableRow ?? false,
            resettable: resettable);
        return SpreadTitle(" " + heading, hints, width);
    }

    /// <summary>
    /// The action bar: where the cursor is in the options list on the left — <c>option 4/15 · COLOUR &amp;
    /// LEGIBILITY</c>, or a row's own words (<c>meter 2/3</c>) — and the actions on the right.
    /// </summary>
    internal static string FooterLine(IReadOnlyList<OptionRow> rows, int width, ScreenFocus? focus = null)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var options = rows.Count(IsNavigable);
        var context = string.Empty;
        if (options > 0)
        {
            var selected = Math.Clamp(focus?.Pane == 0 ? focus.Value.Index : 0, 0, options - 1);
            var row = rows.Where(IsNavigable).ElementAt(selected);
            context = ScreenChrome.Context(
                row.Where is { } where ? Escape(where) : ScreenChrome.Position("option", selected, options),
                SectionOf(rows, selected));
        }

        return SpreadLR(" " + context, ScreenChrome.Actions(focus: focus), width);
    }

    /// <summary>
    /// The section heading the nth navigable row sits under, without the branch glyph that marks it as
    /// one — or null on a screen whose rows aren't grouped.
    /// </summary>
    private static string? SectionOf(IReadOnlyList<OptionRow> rows, int selected)
    {
        string? section = null;
        var navigable = 0;
        foreach (var row in rows)
        {
            if (IsSection(row))
            {
                section = Escape(row.Label[2..]);
                continue;
            }

            if (!IsNavigable(row))
            {
                continue;
            }

            if (navigable++ == selected)
            {
                return section;
            }
        }

        return null;
    }

    /// <summary>
    /// The options list itself — one line per row, in order. The row under the keyboard cursor is
    /// drawn on a cursor bar padded to <paramref name="width"/>; spacers, headings and static rows are
    /// skipped when counting, so the cursor index matches <see cref="Model"/>'s row order.
    /// </summary>
    internal static List<string> BodyColumn(
        IReadOnlyList<OptionRow> rows, ScreenFocus? focus = null, int width = 0)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var cursor = focus ?? ScreenFocus.None;
        var lines = new List<string>(rows.Count);
        var navigable = 0;
        foreach (var row in rows)
        {
            if (!IsNavigable(row))
            {
                lines.Add(row.Draw is { } draw ? draw(null, width) : RenderRow(row, null));
                continue;
            }

            var on = cursor.IsOn(0, navigable);
            var line = row.Draw is { } drawn
                ? drawn(on ? cursor.Edit : null, width)
                : RenderRow(row, cursor.EditOn(0, navigable, 0));
            lines.Add(ScreenChrome.Cursor(line, on, width));
            navigable++;
        }

        return ScreenChrome.Choices(lines, cursor.Edit, width > 0 ? width : ChoiceListWidth);
    }

    /// <summary>
    /// How wide a dropdown may run on a screen rendered without a width — the merged
    /// <see cref="Render(OptionsScreen)"/> path the unit tests use.
    /// </summary>
    private const int ChoiceListWidth = 60;

    /// <summary>
    /// The screen's one navigable pane: every row that is a cursor stop, in display order, each carrying
    /// whatever it was built with — the checkbox Space flips, the values ⏎ opens, the button ⏎ runs, the
    /// reset <c>r</c> runs — and, when there is one, the removal Delete runs as a hidden trailing button.
    /// </summary>
    internal static ScreenModel Model(OptionsScreen screen)
    {
        var rows = screen.Rows
            .Where(IsNavigable)
            .Select(r => new ScreenRow(
                r.Bind,
                r.Fields ?? (r.Edit is { } field ? new[] { field } : null),
                r.Button,
                r.Reset))
            .ToList();
        if (screen.Remove is { } remove)
        {
            rows.Add(ScreenRow.Of(remove));
        }

        return new ScreenModel(rows);
    }

    /// <summary>Where the row drawing a declared field sits among the cursor stops, or -1.</summary>
    internal static int IndexOf(OptionsScreen screen, string fieldId)
    {
        var navigable = 0;
        foreach (var row in screen.Rows)
        {
            if (!IsNavigable(row))
            {
                continue;
            }

            if (string.Equals(row.FieldId, fieldId, StringComparison.Ordinal))
            {
                return navigable;
            }

            navigable++;
        }

        return -1;
    }

    /// <summary>
    /// One row: its checkbox column (a box, or the blank that keeps the labels in one column), its
    /// label, the value it holds if it holds one, its hint, and <see cref="ChangedMark"/> when it differs
    /// from its default.
    /// </summary>
    private static string RenderRow(OptionRow row, ScreenFieldEdit? edit)
    {
        if (IsSpacer(row))
        {
            return string.Empty;
        }

        if (IsSection(row))
        {
            return $"[dim]{Escape(row.Label)}[/]";
        }

        var box = row.Toggle switch
        {
            true => $"[{Accent}][[x]][/] ",
            false => "[dim][[ ]][/] ",
            null => new string(' ', AffordanceWidth),
        };

        var hint = row.Hint is null ? string.Empty : $"[dim] — {Escape(row.Hint)}[/]";
        var changed = row.Reset is null ? string.Empty : Changed();
        var hasValue = row.Value is not null || edit is not null;
        var label = Escape(row.Label);

        // A checkbox row's label *is* its content, so it keeps the primary ink; a label/value row's
        // label is the secondary half of a pair, so it dims and lets the value carry the weight.
        if (!hasValue)
        {
            return $"{box}{label}{hint}{changed}";
        }

        var value = ScreenChrome.Field(Escape(row.Value ?? string.Empty), edit);
        return $"{box}[dim]{label.PadRight(LabelWidth - AffordanceWidth)}[/] {value}{hint}{changed}";
    }

    private static string Changed() => $"  [{Accent}]{ChangedMark}[/]";

    /// <summary>A blank separator carrying nothing at all.</summary>
    private static bool IsSpacer(OptionRow row) =>
        row.Label.Length == 0 && row.Value is null && row.Toggle is null && row.Draw is null
        && row.Button is null && row.Fields is null;

    /// <summary>A dim group heading, marked by the branch glyph the screens prefix them with.</summary>
    private static bool IsSection(OptionRow row) => row.Label.StartsWith("├ ", StringComparison.Ordinal);

    /// <summary>Whether a row is a cursor stop.</summary>
    private static bool IsNavigable(OptionRow row) => !row.Static && !IsSpacer(row) && !IsSection(row);

    /// <summary>
    /// The F7 screen over the defaults — or over the given settings, written back to as the rows are used.
    /// <paramref name="sound"/> null leaves the SOUND group out, which is how a caller that has no sound
    /// settings to hand gets the rest of the screen.
    /// </summary>
    internal static OptionsScreen TextAnsiScreen(TextSettings? text = null, SoundSettings? sound = null) =>
        TextAnsiScreen(
            new AppConfiguration { Text = text ?? new TextSettings(), Sound = sound ?? new SoundSettings() },
            includeSound: sound is not null);

    /// <summary>
    /// The F7 screen, drawn from <paramref name="section"/> (the declared <see cref="BuiltInSettings.Text"/>
    /// unless a host passes the registry's) over <paramref name="config"/>.
    /// </summary>
    /// <param name="config">Where the values are read from and written to.</param>
    /// <param name="includeSound">Whether the SOUND group is drawn.</param>
    /// <param name="data">The focused session's game data, which the meter table reads its <c>now</c> column from.</param>
    /// <param name="cursor">Which cursor stop the keyboard is on, which decides whether the received-data box shows.</param>
    /// <param name="section">The declared section.</param>
    internal static OptionsScreen TextAnsiScreen(
        AppConfiguration config,
        bool includeSound = true,
        GameDataStore? data = null,
        int cursor = -1,
        SettingsSectionContribution? section = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        return FromSection(
            section ?? BuiltInSettings.Text,
            config,
            data,
            cursor,
            includeSound ? null : BuiltInSettings.SoundGroup);
    }

    /// <summary>The F8 screen over the defaults, or over the given settings.</summary>
    internal static OptionsScreen InputScreen(InputSettings? input = null) =>
        InputScreen(new AppConfiguration { Input = input ?? new InputSettings() });

    /// <summary>
    /// The F8 "Input" screen, drawn from its declared section.
    /// <para>
    /// <c>keep logins out of history</c> is the one row here that is about a secret rather than a
    /// convenience: with history browsable and searchable (⌃R), a hand-typed
    /// <c>connect &lt;name&gt; &lt;password&gt;</c> recorded in it is a password on display. It is bash's
    /// <c>HISTIGNORE</c>, on by default, and the verbs it recognises are <see cref="HistorySecrets.Verbs"/>.
    /// </para>
    /// <para>
    /// There is no spellcheck section and no <c>newline key</c> row: a setting with nothing behind it is the
    /// empty promise these screens are held against, and the newline chord is fixed per host and named where
    /// the keys are named.
    /// </para>
    /// </summary>
    internal static OptionsScreen InputScreen(AppConfiguration config, SettingsSectionContribution? section = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        return FromSection(section ?? BuiltInSettings.Input, config, null, -1, null);
    }

    /// <summary>
    /// A declared section as an options screen: a heading wherever the group changes, then one row per
    /// field, drawn by kind. <paramref name="skipGroup"/> leaves one group out.
    /// </summary>
    internal static OptionsScreen FromSection(
        SettingsSectionContribution section,
        AppConfiguration config,
        GameDataStore? data,
        int cursor,
        string? skipGroup)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(config);

        var rows = new List<OptionRow>();
        ScreenButton? remove = null;
        string? preview = null;
        string? group = null;
        var navigable = 0;
        foreach (var field in section.Fields)
        {
            if (skipGroup is not null && string.Equals(field.Group, skipGroup, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(field.Group, group, StringComparison.Ordinal))
            {
                if (rows.Count > 0)
                {
                    rows.Add(new OptionRow(string.Empty, null, null));
                }

                group = field.Group;
                if (group is not null)
                {
                    rows.Add(new OptionRow("├ " + group.ToUpperInvariant(), null, null));
                }
            }

            if (field is ListField { Id: BuiltInSettings.MetersField, Binding: SettingBinding<List<StatusMeter>> meters })
            {
                remove = MeterRows(rows, config, meters, data, cursor, ref navigable) ?? remove;
                continue;
            }

            if (field is ThemeField { Binding: SettingBinding<string> theme })
            {
                preview = ThemeRows(rows, config, field, theme, cursor, ref navigable) ?? preview;
                continue;
            }

            rows.Add(Row(field, config));
            navigable++;
        }

        return new OptionsScreen(section.Title, section.Key ?? string.Empty, rows, remove, preview);
    }

    /// <summary>What the theme picker's caption says of the theme in use.</summary>
    internal const string ThemeInUse = "in use";

    /// <summary>The width a preview is drawn at when the screen is rendered without one (the unit tests' merged path).</summary>
    private const int PreviewFallbackWidth = 76;

    /// <summary>
    /// F7's theme picker, appended to <paramref name="rows"/>: one cursor stop per theme (⏎ uses it, through the
    /// screen's edits and so the save funnel), then the preview of the theme under the cursor — or of the theme in use,
    /// while the cursor is elsewhere. Returns the theme under the cursor, which the screen is painted in while it is
    /// there; Esc closes the screen with nothing committed, which is the revert.
    /// </summary>
    private static string? ThemeRows(
        List<OptionRow> rows,
        AppConfiguration config,
        SettingsField field,
        SettingBinding<string> binding,
        int cursor,
        ref int navigable)
    {
        var cards = ThemePreviews.Cards(config);
        var current = binding.Get(config);
        var reset = binding.IsDefault(config) ? null : (Action)(() => binding.Reset(config));
        string? under = null;
        for (var i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            rows.Add(new OptionRow(
                card.Name, null, null,
                Button: UseTheme(binding, config, card.Name),
                Reset: reset,
                FieldId: i == 0 ? field.Id : null,
                Where: ScreenChrome.Position("theme", i, cards.Count),
                Draw: (_, width) => ThemeRow(card, current, reset is not null && i == 0)));
            if (navigable == cursor)
            {
                under = card.Name;
            }

            navigable++;
        }

        var shown = cards.FirstOrDefault(c => string.Equals(c.Name, under ?? current, StringComparison.OrdinalIgnoreCase))
            ?? cards[0];
        var caption = under is null || string.Equals(under, current, StringComparison.OrdinalIgnoreCase)
            ? $"[dim]preview · {Escape(shown.Name)} · {ThemeInUse}[/]"
            : $"[dim]preview · {Escape(shown.Name)} · ⏎ or Space uses it, Esc keeps {Escape(current)}[/]";
        rows.Add(new OptionRow(string.Empty, null, null, Static: true, Draw: (_, _) => new string(' ', AffordanceWidth) + caption));
        for (var line = 0; line < PreviewRows; line++)
        {
            var at = line;
            rows.Add(new OptionRow(string.Empty, null, null, Static: true,
                Draw: (_, width) => PreviewLines(shown.Preview, width > 0 ? width : PreviewFallbackWidth)[at]));
        }

        return under;
    }

    /// <summary>⏎ on a theme: make it the theme, with the old one as the undo.</summary>
    private static ScreenButton UseTheme(SettingBinding<string> binding, AppConfiguration config, string name) => new(
        "use",
        () =>
        {
            var before = binding.Get(config);
            binding.Set(config, name);
            return new ScreenPress(() => binding.Set(config, before));
        },
        ScreenButtonKind.Apply,
        name);

    /// <summary>One theme's row: the in-use mark, its name, a strip of its planes, and what kind of theme it is.</summary>
    private static string ThemeRow(ThemeCard card, string current, bool changed)
    {
        var inUse = string.Equals(card.Name, current, StringComparison.OrdinalIgnoreCase);
        var mark = inUse ? $"[{Accent}]●[/]   " : new string(' ', AffordanceWidth);
        var preview = card.Preview;
        var strip = string.Concat(
            new[] { preview.Backdrop, preview.Panes[0].Plane, preview.Panes[1].Plane, preview.Panes[2].Plane, preview.CommandLine.Band }
                .Select(p => $"[{p.ToHex()}]██[/]"));
        var kind = $"{(card.IsDark ? "dark" : "light")} · {(card.IsCustom ? "your own" : "built in")}";
        var name = Escape(card.Name).PadRight(LabelWidth - AffordanceWidth - 12);
        var label = inUse ? $"[{Value}]{name}[/]" : name;
        return $"{mark}{label} {strip}  [dim]{kind}[/]{(inUse ? $"  [{Accent}]{ThemeInUse}[/]" : string.Empty)}"
            + (changed ? Changed() : string.Empty);
    }

    /// <summary>How many rows a preview takes under its caption: the tab strips, five lines, the command line.</summary>
    internal const int PreviewRows = 7;

    /// <summary>
    /// A preview as <see cref="PreviewRows"/> rows of markup, <paramref name="width"/> wide: three panes on the
    /// backdrop, each with its tab strip, and the command line across them. Every colour is the model's own, so the
    /// window's cards and these rows say the same thing.
    /// </summary>
    internal static IReadOnlyList<string> PreviewLines(ThemePreview preview, int width)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var indent = new string(' ', AffordanceWidth);
        var room = Math.Max(3 * 8 + 4, width - AffordanceWidth - 1);
        var paneWidth = (room - 4) / 3;
        var full = (paneWidth * 3) + 4;
        var backdrop = preview.Backdrop.ToHex();
        var gap = $"[on {backdrop}] [/]";

        var lines = new List<string>(PreviewRows);
        var strips = new List<string>();
        foreach (var pane in preview.Panes)
        {
            strips.Add(TabStrip(pane, paneWidth, backdrop));
        }

        lines.Add(indent + gap + string.Join(gap, strips) + gap);
        var depth = preview.Panes.Max(p => p.Lines.Count);
        for (var row = 0; row < Math.Min(depth, PreviewRows - 2); row++)
        {
            var cells = preview.Panes.Select(p => PaneLine(p, row, paneWidth));
            lines.Add(indent + gap + string.Join(gap, cells) + gap);
        }

        while (lines.Count < PreviewRows - 1)
        {
            lines.Add(indent + gap + string.Join(gap, preview.Panes.Select(p => $"[on {p.Plane.ToHex()}]{new string(' ', paneWidth)}[/]")) + gap);
        }

        var line = preview.CommandLine;
        var band = line.Band.ToHex();
        var prompt = Clip(line.Prompt, full - 2);
        var typed = Clip(line.Text, Math.Max(0, full - 3 - prompt.Length));
        var used = prompt.Length + 1 + typed.Length;
        lines.Add(indent + gap
            + $"[{line.PromptInk.ToHex()} on {band}]{Escape(prompt)}[/][on {band}] [/]"
            + $"[{line.Ink.ToHex()} on {band}]{Escape(typed)}[/]"
            + $"[on {band}]{new string(' ', Math.Max(0, full - 2 - used))}[/]" + gap);
        return lines;
    }

    private static string TabStrip(PreviewPane pane, int width, string backdrop)
    {
        var markup = new System.Text.StringBuilder();
        var used = 0;
        foreach (var tab in pane.Tabs)
        {
            var title = $" {tab.Title}";
            var badge = tab.Badge is null ? string.Empty : $" {tab.Badge}";
            var cost = title.Length + badge.Length + 1;
            if (used + cost > width)
            {
                break;
            }

            var chip = tab.Chip.ToHex();
            var weight = tab.Selected ? "bold " : string.Empty;
            markup.Append($"[{weight}{tab.Ink.ToHex()} on {chip}]{Escape(title)}[/]");
            if (tab.Badge is { } count && tab.BadgeInk is { } ink)
            {
                markup.Append($"[{ink.ToHex()} on {chip}]{Escape(" " + count)}[/]");
            }

            markup.Append($"[on {chip}] [/]");
            used += cost;
        }

        if (used < width)
        {
            markup.Append($"[on {backdrop}]{new string(' ', width - used)}[/]");
        }

        return markup.ToString();
    }

    private static string PaneLine(PreviewPane pane, int row, int width)
    {
        var plane = pane.Plane.ToHex();
        var markup = new System.Text.StringBuilder($"[on {plane}] [/]");
        var used = 1;
        if (row < pane.Lines.Count)
        {
            foreach (var run in pane.Lines[row])
            {
                var text = Clip(run.Text, width - 1 - used);
                if (text.Length == 0)
                {
                    break;
                }

                var weight = run.Bold ? "bold " : string.Empty;
                markup.Append($"[{weight}{run.Ink.ToHex()} on {plane}]{Escape(text)}[/]");
                used += text.Length;
            }
        }

        markup.Append($"[on {plane}]{new string(' ', Math.Max(0, width - used))}[/]");
        return markup.ToString();
    }

    private static string Clip(string text, int width) =>
        width <= 0 ? string.Empty : text.Length <= width ? text : width == 1 ? "…" : text[..(width - 1)] + "…";

    /// <summary>One declared field as a row, by kind. A field with no binding the row understands is shown read-only.</summary>
    private static OptionRow Row(SettingsField field, AppConfiguration config)
    {
        var reset = field.Binding is { } binding && !binding.IsDefault(config)
            ? () => binding.Reset(config)
            : (Action?)null;

        switch (field)
        {
            case BoolField when field.Binding is SettingBinding<bool> flag:
                return new OptionRow(
                    field.Label, null, flag.Get(config), null,
                    ScreenToggle.Bind(() => flag.Get(config), v => flag.Set(config, v)),
                    Reset: reset, FieldId: field.Id);

            case NumberField number when field.Binding is SettingBinding<int> count:
                return new OptionRow(
                    field.Label, count.Get(config).ToString(CultureInfo.InvariantCulture), null, null, null,
                    ScreenField.Integer(
                        field.Label, () => count.Get(config), v => count.Set(config, v), number.Minimum, number.Maximum),
                    Reset: reset, FieldId: field.Id);

            case ChoiceField choice when field.Binding is SettingBinding<string> named:
                return new OptionRow(
                    field.Label, named.Get(config), null, null, null,
                    ScreenField.Choice(field.Label, () => named.Get(config), v => named.Set(config, v), choice.ChoicesIn(config)),
                    Reset: reset, FieldId: field.Id);

            case TextField when field.Binding is SettingBinding<string?> text:
                return new OptionRow(
                    field.Label, text.Get(config) ?? string.Empty, null, null, null,
                    ScreenField.Optional(field.Label, () => text.Get(config), v => text.Set(config, v)),
                    Reset: reset, FieldId: field.Id);

            default:
                return new OptionRow(
                    field.Label, field.Binding?.Text(config) ?? string.Empty, null, Reset: reset, FieldId: field.Id);
        }
    }

    /// <summary>The cells the meter table's label column takes (a label is at most eight).</summary>
    private const int MeterLabelWidth = StatusMeter.MaxLabelLength + 1;

    /// <summary>The cells its <c>now</c> column takes: <c>1000/1000</c> and a cell.</summary>
    private const int MeterNowWidth = 11;

    /// <summary>The gap between two of its columns.</summary>
    private const int MeterGap = 2;

    /// <summary>How many received paths the box under the table lists before it says how many more there are.</summary>
    internal const int ReceivedRows = 6;

    /// <summary>What the received-data box is called, and the path fields' dropdown with it.</summary>
    internal const string ReceivedCaption = "received on this connection";

    /// <summary>What the <c>now</c> column says of a meter whose value has not arrived.</summary>
    internal const string Waiting = "waiting";

    /// <summary>
    /// F7's meter table, appended to <paramref name="rows"/>: the column headings, one row per meter, the
    /// add button, and — while the cursor is on one of those — the box of paths this connection has
    /// received. Returns the removal Delete runs, when the cursor is on a meter.
    /// </summary>
    private static ScreenButton? MeterRows(
        List<OptionRow> rows,
        AppConfiguration config,
        SettingBinding<List<StatusMeter>> binding,
        GameDataStore? data,
        int cursor,
        ref int navigable)
    {
        var meters = binding.Get(config);
        var reset = binding.IsDefault(config) ? null : (Action)(() => binding.Reset(config));
        var received = data is null ? Array.Empty<(string Path, string Value)>() : ReceivedPaths(data);
        var first = navigable;

        rows.Add(new OptionRow(string.Empty, null, null, Static: true, FieldId: BuiltInSettings.MetersField,
            Draw: (_, width) => MeterHeading(width, reset is not null)));

        ScreenButton? remove = null;
        for (var i = 0; i < meters.Count; i++)
        {
            var meter = meters[i];
            var index = i;
            var fields = new[]
            {
                ScreenField.Text("label", () => meter.Label, v => meter.Label = StatusMeter.Clip(v)),
                PathField("value path", () => meter.Value, v => meter.Value = v ?? string.Empty, received, required: true),
                PathField("maximum path", () => meter.Max, v => meter.Max = v, received, required: false),
            };
            rows.Add(new OptionRow(
                meter.Label, null, null,
                Fields: fields,
                Reset: reset,
                FieldId: i == 0 ? BuiltInSettings.MetersField : null,
                Where: ScreenChrome.Position("meter", index, meters.Count),
                Draw: (edit, width) => MeterRow(meter, data, edit, width)));
            if (navigable == cursor)
            {
                remove = ScreenButton.Remove(meters, index, first, $"meter {meter.Label}");
            }

            navigable++;
        }

        var addAt = navigable;
        rows.Add(new OptionRow(
            "[+ meter]", null, null,
            Button: ScreenButton.Add("+ meter", meters, () => new StatusMeter { Label = "new" }, first),
            Reset: reset,
            FieldId: meters.Count == 0 ? BuiltInSettings.MetersField : null,
            Draw: (_, _) => new string(' ', AffordanceWidth) + $"[{Accent}][[+ meter]][/]"));
        navigable++;

        if (cursor >= first && cursor <= addAt)
        {
            rows.AddRange(ReceivedBox(received));
        }

        return remove;
    }

    /// <summary>
    /// A meter's value or maximum path: free text, with every path this connection has received offered
    /// in the dropdown beside what it holds now. The current value heads the list, so opening the field
    /// shows the whole list with it marked rather than narrowing to nothing.
    /// </summary>
    private static ScreenField PathField(
        string label,
        Func<string?> get,
        Action<string?> set,
        IReadOnlyList<(string Path, string Value)> received,
        bool required)
    {
        var current = get();
        var choices = new List<string>();
        if (!string.IsNullOrWhiteSpace(current))
        {
            choices.Add(current);
        }

        choices.AddRange(received.Select(r => r.Path).Where(p => !string.Equals(p, current, StringComparison.Ordinal)));
        var notes = received.ToDictionary(r => r.Path, r => r.Value, StringComparer.Ordinal);

        return new ScreenField(
            label,
            () => get() ?? string.Empty,
            value => required && string.IsNullOrWhiteSpace(value) ? $"{label} cannot be empty" : null,
            value => set(string.IsNullOrWhiteSpace(value) ? null : value.Trim()),
            choices.Count > 0 ? choices : null,
            ChoiceNotes: notes,
            ChoiceCaption: ReceivedCaption);
    }

    /// <summary>How wide the table's two path columns are at <paramref name="width"/>.</summary>
    private static (int Value, int Max) PathWidths(int width)
    {
        var room = (width > 0 ? width : 100) - AffordanceWidth - MeterLabelWidth - MeterNowWidth - (3 * MeterGap);
        room = Math.Max(16, room);
        return (room / 2, room - (room / 2));
    }

    private static string MeterHeading(int width, bool changed)
    {
        var (value, max) = PathWidths(width);
        var gap = new string(' ', MeterGap);
        var line = new string(' ', AffordanceWidth)
            + "label".PadRight(MeterLabelWidth) + gap
            + "value path".PadRight(value) + gap
            + "maximum path".PadRight(max) + gap
            + "now";
        return $"[dim]{line}[/]" + (changed ? Changed() : string.Empty);
    }

    /// <summary>
    /// One meter: its three fields in their columns and what it reads now. While one of its fields is open
    /// that field takes the rest of the row, so a long path is typed where it can be seen.
    /// </summary>
    private static string MeterRow(StatusMeter meter, GameDataStore? data, ScreenFieldEdit? edit, int width)
    {
        var (valueWidth, maxWidth) = PathWidths(width);
        var gap = new string(' ', MeterGap);
        var line = new System.Text.StringBuilder(new string(' ', AffordanceWidth));
        var cells = new[] { (meter.Label, MeterLabelWidth), (meter.Value, valueWidth), (meter.Max ?? string.Empty, maxWidth) };
        for (var i = 0; i < cells.Length; i++)
        {
            var (text, cell) = cells[i];
            if (edit is { } open && open.Field == i)
            {
                var room = Math.Max(8, (width > 0 ? width : 100) - VisibleLength(line.ToString()) - 2);
                line.Append(ScreenChrome.Field(string.Empty, Scrolled(open, room)));
                return line.ToString();
            }

            var shown = string.IsNullOrEmpty(text) ? "—" : text;
            var display = Escape(ElidePlain(shown, cell - 1).PadRight(cell - 1));
            line.Append(PadVisible(ScreenChrome.Field($"[{ScreenPalette.Value}]{display}[/]", null), cell)).Append(gap);
        }

        line.Append(Now(meter, data));
        return line.ToString();
    }

    /// <summary>The <c>now</c> column: <c>412/500</c>, <c>412</c> with no maximum, or <see cref="Waiting"/>.</summary>
    internal static string Now(StatusMeter meter, GameDataStore? data) =>
        NowText(meter, data) is { } text
            ? $"[bold {ScreenPalette.Value}]{Escape(text)}[/]"
            : $"[{Muted}]{Waiting}[/]";

    /// <summary>The <c>now</c> column as plain text, or null while the meter's value has not arrived.</summary>
    internal static string? NowText(StatusMeter meter, GameDataStore? data)
    {
        if (data is null || StatusMeter.Read(data, meter.Value) is not { } value)
        {
            return null;
        }

        var max = string.IsNullOrWhiteSpace(meter.Max) ? null : StatusMeter.Read(data, meter.Max);
        return StatusMeter.Fit(value, 5) + (max is null ? string.Empty : "/" + StatusMeter.Fit(max, 5));
    }

    /// <summary>
    /// An open edit whose buffer is longer than <paramref name="room"/>, cut to the stretch around the caret
    /// so the caret is always drawn — the buffer itself is untouched.
    /// </summary>
    private static ScreenFieldEdit Scrolled(ScreenFieldEdit edit, int room)
    {
        if (edit.Text.Length < room)
        {
            return edit;
        }

        var start = Math.Clamp(edit.Caret - room + 2, 0, edit.Text.Length);
        var length = Math.Min(room - 1, edit.Text.Length - start);
        return edit with { Text = edit.Text.Substring(start, length), Caret = edit.Caret - start };
    }

    private static string ElidePlain(string text, int width) =>
        text.Length <= width ? text : width <= 1 ? "…" : text[..(width - 1)] + "…";

    /// <summary>The box of received paths under the table, drawn while the cursor is in it.</summary>
    private static IEnumerable<OptionRow> ReceivedBox(IReadOnlyList<(string Path, string Value)> received)
    {
        var indent = new string(' ', AffordanceWidth);
        var widest = received.Count == 0 ? 0 : Math.Min(40, received.Max(r => r.Path.Length));
        yield return Line($"{indent}[{Label}]┌ {ReceivedCaption}[/]");
        if (received.Count == 0)
        {
            yield return Line($"{indent}[{Label}]│[/] [{Muted}]nothing yet — a meter shows once its value arrives[/]");
        }

        foreach (var (path, value) in received.Take(ReceivedRows))
        {
            yield return Line(
                $"{indent}[{Label}]│[/] [{ScreenPalette.Value}]{Escape(ElidePlain(path, widest).PadRight(widest))}[/]"
                + $"  [{Muted}]{Escape(ElidePlain(value, 20))}[/]");
        }

        var more = received.Count - ReceivedRows;
        yield return Line(more > 0
            ? $"{indent}[{Label}]└ {more.ToString(CultureInfo.InvariantCulture)} more · open a path and ↑↓ to pick one[/]"
            : $"{indent}[{Label}]└ open a path and ↑↓ to pick one[/]");

        static OptionRow Line(string markup) => new(string.Empty, null, null, Static: true, Draw: (_, _) => markup);
    }

    /// <summary>
    /// Every value this connection's game data holds, as a path a meter can name and its text now: the
    /// scalar fields of each GMCP package (and MSDP and MXP variables), objects walked a few levels deep and
    /// arrays left out — a list of channels is not something a meter shows. Sorted by path.
    /// </summary>
    internal static IReadOnlyList<(string Path, string Value)> ReceivedPaths(GameDataStore data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var found = new List<(string, string)>();
        foreach (var entry in data.Entries)
        {
            Walk(entry.Package, entry.Data, 0);
        }

        return found.OrderBy(f => f.Item1, StringComparer.OrdinalIgnoreCase).ToList();

        void Walk(string path, System.Text.Json.Nodes.JsonNode? node, int depth)
        {
            if (found.Count >= 512)
            {
                return;
            }

            switch (node)
            {
                case System.Text.Json.Nodes.JsonObject obj when depth < 4:
                    foreach (var (key, child) in obj)
                    {
                        Walk(path + "." + key, child, depth + 1);
                    }

                    break;
                case System.Text.Json.Nodes.JsonValue:
                    if (data.GetText(path) is { } text)
                    {
                        found.Add((path, text));
                    }

                    break;
            }
        }
    }

    /// <summary>The F7 screen body.</summary>
    public static List<string> TextAnsi() => Render(TextAnsiScreen());

    /// <summary>The F8 "Input" screen body.</summary>
    public static List<string> Input() => Render(InputScreen());
}
