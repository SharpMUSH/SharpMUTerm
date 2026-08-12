using System.Globalization;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;

namespace SharpMUTerm.Tui;

/// <summary>
/// The chrome every full-screen settings screen (F2–F9) shares: the keyboard-hint and action-bar
/// fragments its renderer writes, and the band / rule / inset panels its view composes. The screens
/// differ in their body, not their frame, so the frame lives here — a header band, an action bar naming
/// what Esc and ⏎ do <em>right now</em>, and the hairlines between columns look and behave the same on
/// all of them.
/// </summary>
internal static class ScreenChrome
{
    /// <summary>
    /// The right-hand keyboard hints of a header band: the screen's verbs, then how to close it.
    /// <paramref name="fkey"/> is the F-key that also toggles the screen (<c>F6/Esc close</c>).
    /// <para>
    /// <paramref name="editable"/> comes from the screen's <see cref="ScreenModel"/>, never from the
    /// screen itself: a header may only claim ⏎ opens an editor when a row offers one. While an edit is
    /// open the hints change wholesale — Esc abandons the buffer rather than closing the screen.
    /// </para>
    /// </summary>
    internal static string Hints(
        string verbs,
        string fkey,
        bool editable = false,
        ScreenFocus? focus = null,
        bool removable = false,
        bool detailed = false)
    {
        if (focus?.Edit is { } edit)
        {
            // A capture answers to nothing else: every key but Esc is a candidate binding, so ⏎ does not
            // commit, ⇥ does not step, and the screen's own F-key does not close it — it is offered as a
            // binding and refused like any other claimed chord. Advertising any of the three would name
            // a key that means something different for as long as the prompt is up.
            if (edit.Capture)
            {
                return $"[{ScreenPalette.Label}]{CaptureHints}[/]";
            }

            var editing = EditingHints
                + (edit.VisibleChoices.Count > 0 ? ChoiceHint : string.Empty)
                + (edit.RowFields > 1 ? NextFieldHint : string.Empty);
            return $"[{ScreenPalette.Label}]{editing} · [/][{ScreenPalette.Accent}]{fkey}[/]"
                + $"[{ScreenPalette.Label}] close [/]";
        }

        // Order is a budget, not taste: a header narrower than its hints loses the *tail* of this string
        // (see the 80-column frames), so whatever is appended last is what a narrow terminal does not
        // get. `i info` goes after `Del remove` because Delete is the destructive key and must be the
        // one that survives — and because the INFO key's drawn row (`i  info on Aetherfall`) is on the
        // screen either way, while a key that removes a world has only this line and its own row.
        var all = (editable ? verbs + EditHint : verbs)
            + (removable ? DeleteHint : string.Empty)
            + (detailed ? DetailHint : string.Empty);
        return $"[{ScreenPalette.Label}]{all} · [/][{ScreenPalette.Accent}]{fkey}[/][{ScreenPalette.Label}]/[/]"
            + $"[{ScreenPalette.Accent}]Esc[/][{ScreenPalette.Label}] close [/]";
    }

    /// <summary>
    /// The keyboard hints every screen with a list and a checkbox pane shares, kept in one place so a
    /// screen cannot advertise a key its <see cref="ScreenModel"/> does not offer. It names ←→ as well as
    /// ⇥ because on a multi-pane screen both change pane; the single-pane form below does not, since
    /// there is nowhere sideways to go. <see cref="ScreenHintTests"/> pins the pair against every
    /// screen's real pane count.
    /// </summary>
    internal const string ListHints = "↑↓ select · ←→ ⇥ pane · Space toggle";

    /// <summary>The hints for a screen that is a single list with no second pane to ⇥ or ←→ into.</summary>
    internal const string SingleListHints = "↑↓ select · Space toggle";

    /// <summary>
    /// What a screen adds to its hints when — and only when — its model offers a row ⏎ can open.
    /// </summary>
    internal const string EditHint = " · ⏎ edit";

    /// <summary>
    /// What a screen adds to its hints when a pane offers a way to remove a row. Delete is the only way
    /// to run a removal — the drawn row is not a cursor stop (<see cref="ScreenModel.Sizes"/>) — so it
    /// acts on the row the cursor is already on, and this is where the screen says so.
    /// </summary>
    internal const string DeleteHint = " · Del remove";

    /// <summary>
    /// What a screen adds when a pane offers a read-only report on the selected row. Derived rather than
    /// written, because <c>i</c> is an ordinary letter: a screen answering it silently is a hidden
    /// feature, and one claiming it without answering looks broken.
    /// </summary>
    internal const string DetailHint = " · i info";

    /// <summary>The hints that replace a screen's own while a field edit is open.</summary>
    internal const string EditingHints = "⏎ commit · Esc revert";

    /// <summary>
    /// The hints that replace even those while a key capture is armed. They are the whole keyboard
    /// contract at that moment: one key gets you out, everything else is the value.
    /// </summary>
    internal const string CaptureHints = "press any key to bind it · Esc cancels";

    /// <summary>Added to <see cref="EditingHints"/> only when the row has another field to step to.</summary>
    internal const string NextFieldHint = " · ⇥ next field";

    /// <summary>
    /// Added to <see cref="EditingHints"/> only while the open field's dropdown has entries in it, since
    /// ↑↓ walk exactly those entries. Derived from <see cref="ScreenFieldEdit.VisibleChoices"/> rather
    /// than from whether the field has choices at all, so it disappears the moment a typed value narrows
    /// the list to nothing — the point at which the keys genuinely stop doing anything.
    /// </summary>
    internal const string ChoiceHint = " · ↑↓ pick from list";

    /// <summary>
    /// What the footer's Esc chip does while the screen is navigating. Closing keeps every committed
    /// edit, so it agrees with the header's <c>Esc close</c>; <see cref="ScreenFooterTests"/> pins the
    /// two against each other.
    /// </summary>
    internal const string CloseAction = "[[Esc]] Close";

    /// <summary>What the footer's ⏎ chip says on a row that has values to open.</summary>
    internal const string EditAction = "[[⏎]] Edit";

    /// <summary>And on one of a pane's building buttons, which is the other row ⏎ acts on.</summary>
    internal const string AddAction = "[[⏎]] Add";

    /// <summary>
    /// And on a row that is neither, where ⏎ leaves the screen. It says <c>Done</c> rather than
    /// <c>Save</c> because there is nothing left to save — every committed value is already on disk (see
    /// <see cref="ScreenEdits"/>) — and a chip promising a save would be promising work that is finished.
    /// </summary>
    internal const string DoneAction = "[[⏎]] Done";

    /// <summary>What the footer's Esc chip does while a field edit is open.</summary>
    internal const string RevertAction = "[[Esc]] Revert";

    /// <summary>What the footer's ⏎ chip does while a field edit is open.</summary>
    internal const string CommitAction = "[[⏎]] Commit";

    /// <summary>
    /// What the ⏎ chip becomes while a key capture is armed. ⏎ is not the key that commits there — it is
    /// merely one of the keys that could be bound — so the chip names what actually finishes the capture,
    /// which is any key at all.
    /// </summary>
    internal const string BindAction = "[[any key]] Bind";

    /// <summary>
    /// The right-hand actions of a footer bar. <paramref name="accent"/> lets a screen with a context
    /// colour (F5's per-world accent) tint the ⏎ chip; it defaults to the app accent.
    /// <para>
    /// <paramref name="focus"/> is read for <see cref="Hints"/>'s reason: while a field edit is open ⏎
    /// commits that field and Esc abandons its buffer, so a bar still offering <c>Save</c> and
    /// <c>Cancel</c> would name two keys that do something else.
    /// </para>
    /// </summary>
    internal static string Actions(string? accent = null, ScreenFocus? focus = null)
    {
        var editing = focus?.Edit is not null;
        var capturing = focus?.Edit is { Capture: true };
        var escape = editing ? RevertAction : CloseAction;
        var enter = capturing ? BindAction : editing ? CommitAction : Enter(focus);

        return $"[{ScreenPalette.Label}] {escape} [/]  "
            + $"[{ScreenPalette.Ink} on {accent ?? ScreenPalette.Accent}] {enter} [/] ";
    }

    /// <summary>
    /// What the ⏎ chip reads while the screen is navigating: whatever ⏎ does on the row the cursor is
    /// actually on (<see cref="ScreenEnter"/>). A caller with no focus at all — the width-agnostic
    /// <c>Render</c> the unit tests go through — gets the row-less answer, which is what ⏎ does when
    /// there is no row to act on.
    /// </summary>
    private static string Enter(ScreenFocus? focus) => focus?.Enter switch
    {
        ScreenEnter.Edit => EditAction,
        ScreenEnter.Add => AddAction,
        _ => DoneAction,
    };

    /// <summary>
    /// Draws a row as the keyboard cursor: the row's own markup on a cursor band padded out to
    /// <paramref name="width"/>, so the bar spans its pane instead of hugging the text. A row that
    /// isn't under the cursor comes back untouched.
    /// </summary>
    internal static string Cursor(string row, bool focused, int width) =>
        focused ? $"[on {ScreenPalette.CursorBg}]{MarkupText.PadVisible(row, width)}[/]" : row;

    /// <summary>
    /// The cursor band <see cref="Cursor"/> paints, which is what <see cref="Window"/> scrolls to. Found
    /// the way <see cref="Choices"/> finds the block caret: exactly one row of one pane carries it, so a
    /// column can locate its own focused row without every renderer handing back a line number.
    /// </summary>
    private static readonly string CursorMark = $"[on {ScreenPalette.CursorBg}]";

    /// <summary>The two cells a body column spends on the hairline and the gap beside it.</summary>
    internal const int ColumnDivider = 2;

    /// <summary>
    /// How wide a two-column screen's list column runs, given the width the screen was handed:
    /// <paramref name="desired"/> unless that would starve the column beside it, never below
    /// <paramref name="minimum"/>, past which both columns are unreadable rather than one. A caller with
    /// no width to spend gets the desired width unchanged.
    /// </summary>
    /// <param name="width">The whole screen's width, or 0 when the caller has none.</param>
    /// <param name="desired">What the list column takes when the screen can afford it.</param>
    /// <param name="minimum">The fewest cells the list column is still worth drawing in.</param>
    /// <param name="companion">The fewest cells the column beside it can be read in.</param>
    internal static int SplitWidth(int width, int desired, int minimum, int companion) =>
        width <= 0 ? desired : Math.Clamp(width - ColumnDivider - companion, minimum, desired);

    /// <summary>
    /// Drops a block's blank separator rows until it fits in <paramref name="height"/> rows. They are
    /// the first thing a short pane can spare — they carry no content, and every row they cost at the top
    /// is one the pane loses off the bottom, where the cursor's stops live. They go from the top down, so
    /// the section that compacts is the one already on screen.
    /// </summary>
    internal static List<string> Compact(List<string> block, int height)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (height <= 0)
        {
            return block;
        }

        for (var i = 0; i < block.Count && block.Count > height; i++)
        {
            if (block[i].Length == 0)
            {
                block.RemoveAt(i--);
            }
        }

        return block;
    }

    /// <summary>What a windowed block says stands above it, and below it, in place of a drawn row.</summary>
    private const string MoreAbove = "⌃";

    private const string MoreBelow = "⌄";

    /// <summary>
    /// Slices a block down to <paramref name="height"/> rows around the row carrying the cursor band, so
    /// a pane taller than the screen still shows the row the keyboard is on — otherwise the cursor walks
    /// through rows that were never drawn.
    /// <para>
    /// Centred on the focused row rather than scrolled minimally into view: these blocks are rebuilt from
    /// scratch on every keystroke, so a stateless rule has to be a function of the cursor alone. A block
    /// with no cursor in it shows its top, where its heading is, and the edges say what they are hiding.
    /// </para>
    /// </summary>
    internal static List<string> Window(List<string> block, int height)
    {
        ArgumentNullException.ThrowIfNull(block);

        if (height <= 0 || block.Count <= height)
        {
            return block;
        }

        var focused = block.FindIndex(l => l.Contains(CursorMark, StringComparison.Ordinal));
        var start = focused < 0
            ? 0
            : Math.Clamp(focused - (height / 2), 0, block.Count - height);

        var window = block.GetRange(start, height);
        if (start > 0)
        {
            window[0] = More(MoreAbove, start);
        }

        var below = block.Count - start - height;
        if (below > 0)
        {
            window[^1] = More(MoreBelow, below);
        }

        return window;
    }

    /// <summary>How a windowed block names the rows it is not drawing, on the edge they are past.</summary>
    private static string More(string arrow, int count) =>
        $"  [{ScreenPalette.Muted}]{arrow} {count.ToString(CultureInfo.InvariantCulture)} more[/]";

    /// <summary>
    /// Draws a row's editable value: its committed text in a field well, or — when
    /// <paramref name="edit"/> is the open edit for that field — the buffer in that same well with a
    /// block caret in it and the reason the last commit was refused.
    /// <para>
    /// The resting well is why <see cref="ReadOnly"/> exists: a well means "the keyboard can change this
    /// here" and its absence means it cannot, so a row may not advertise an editor it has not got.
    /// </para>
    /// <para>
    /// <paramref name="display"/> is already markup, since a screen decides how a committed value reads;
    /// the buffer is escaped here, being raw text. A <see cref="ScreenFieldEdit.Masked"/> buffer is
    /// replaced by <see cref="Mask"/> first, so a secret has no route into markup at all — not even the
    /// character under the caret. The mask is per-character <em>while typing</em> so the caret lands where
    /// the keys say; a resting secret is fixed-width (<see cref="RestingMask"/>) so an unedited screen
    /// does not publish its length.
    /// </para>
    /// </summary>
    internal static string Field(string display, ScreenFieldEdit? edit)
    {
        if (edit is not { } open)
        {
            return Well(display);
        }

        if (open.Capture)
        {
            return Capture(open);
        }

        var text = open.Masked ? Mask(open.Text.Length) : open.Text;
        var caret = Math.Clamp(open.Caret, 0, text.Length);
        var before = MarkupText.Escape(text[..caret]);
        var under = caret < text.Length ? MarkupText.Escape(text[caret].ToString()) : " ";
        var after = caret < text.Length ? MarkupText.Escape(text[(caret + 1)..]) : string.Empty;

        var buffer = $"[{ScreenPalette.Value} on {ScreenPalette.FieldBg}]{before}[/]"
            + $"[{ScreenPalette.Ink} on {ScreenPalette.Accent}]{under}[/]"
            + $"[{ScreenPalette.Value} on {ScreenPalette.FieldBg}]{after} [/]";

        return open.Error is { } error
            ? $"{buffer}  [{ScreenPalette.Warn}]▲ {MarkupText.Escape(error)}[/]"
            : buffer;
    }

    /// <summary>
    /// The resting field well: a value's own markup on the recessed input background, with a trailing
    /// cell so the well is a visible box rather than a tint hugging the glyphs. The trailing cell is
    /// where the caret goes the moment ⏎ opens the field, so the well doesn't jump sideways under it.
    /// </summary>
    private static string Well(string display) => $"[on {ScreenPalette.FieldBg}]{display} [/]";

    /// <summary>What an armed key capture puts where the value was — and the only way out of it.</summary>
    internal const string CapturePrompt = "press a key · Esc cancels";

    /// <summary>
    /// An armed key capture: the value is replaced outright by the prompt, in the accent block the caret
    /// is drawn in, because there is no buffer to put a caret inside — the next keystroke <em>is</em> the
    /// value. A refused key keeps the capture armed and says why beside it.
    /// </summary>
    private static string Capture(ScreenFieldEdit open)
    {
        var prompt = $"[{ScreenPalette.Ink} on {ScreenPalette.Accent}] {CapturePrompt} [/]";
        return open.Error is { } error
            ? $"{prompt}  [{ScreenPalette.Warn}]▲ {MarkupText.Escape(error)}[/]"
            : prompt;
    }

    /// <summary>
    /// The block caret <see cref="Field"/> paints, which is what <see cref="Choices"/> hangs the dropdown
    /// off. One field of one row can be open at a time and only the column drawing it paints this, so
    /// finding it is how a column knows the open edit is its own.
    /// </summary>
    private static readonly string CaretMark = $"[{ScreenPalette.Ink} on {ScreenPalette.Accent}]";

    /// <summary>
    /// The most candidates a dropdown lists at once. Seventeen colour names is more rows than F2's editor
    /// pane can spare, so the list is capped and the caption says what it is capped to (<c>6 of 17</c>) —
    /// a list silently showing a third of itself would be worse than no list.
    /// </summary>
    internal const int MaxChoiceRows = 6;

    /// <summary>What the dropdown calls itself when the field will take values outside the list.</summary>
    internal const string OpenChoicesCaption = "suggestions";

    /// <summary>What it calls itself when the list is the permitted set and nothing else will commit.</summary>
    internal const string ClosedChoicesCaption = "these values only";

    /// <summary>
    /// What an open field's dropdown says when the buffer matches none of its entries. It names the state
    /// as legal, because on these fields it is: spawn windows are defined by what routes to them, so a
    /// name matching nothing is how the next one is created.
    /// </summary>
    internal const string NoMatchOpen = "nothing matches — a new value is allowed";

    /// <summary>
    /// What a closed field's dropdown says instead. It states the fact and stops: the value is refused at
    /// ⏎ by the field's validator, and a second warning before the user has finished typing would spend
    /// that colour on a value they may be halfway through.
    /// </summary>
    internal const string NoMatchClosed = "nothing matches";

    /// <summary>How far the dropdown is inset from the column's edge, so it hangs under the field.</summary>
    private const string ChoiceIndent = "  ";

    /// <summary>
    /// Draws an open field's candidate list into <paramref name="column"/> and hands the column back. A
    /// block not drawing the open edit has no caret in it and comes back untouched, so the wiring is one
    /// line per column and cannot be pointed at the wrong field.
    /// <para>
    /// The list is an <b>overlay</b>, replacing the rows beside the field rather than pushing them down.
    /// F5's character form is a grid row sized to its own line count, so a list that grew it would resize
    /// the screen on ⏎; F2's editor pane is long enough that pushing would shove reachable checkboxes off
    /// a short terminal. An overlay changes no geometry.
    /// </para>
    /// <para>
    /// It opens downward, and upward when there are not enough rows below — F5's log format sits second
    /// from the end of its form. The caption keeps its edge against the field either way (<c>▾</c> below,
    /// <c>▴</c> above), so the block reads as attached to the well.
    /// </para>
    /// </summary>
    /// <param name="column">The block's lines, as the renderer has just built them.</param>
    /// <param name="edit">The screen's open edit, or null while it is navigating.</param>
    /// <param name="width">The column's width; the list is content-sized and never drawn wider.</param>
    internal static List<string> Choices(List<string> column, ScreenFieldEdit? edit, int width)
    {
        ArgumentNullException.ThrowIfNull(column);

        if (edit is not { HasChoices: true } open)
        {
            return column;
        }

        var anchor = column.FindIndex(l => l.Contains(CaretMark, StringComparison.Ordinal));
        if (anchor < 0)
        {
            return column;
        }

        var (caption, entries) = ChoiceContent(open);
        var height = entries.Count + 2; // the caption, and the shadow closing the far edge

        // Below when the rows are there, above when they aren't, and — for a block shorter than the
        // list itself — below anyway, extending it, because a list drawn nowhere helps nobody.
        var above = anchor + 1 + height > column.Count && anchor - height >= 0;
        var start = above ? anchor - height : anchor + 1;

        // The caption keeps the edge nearest the field and names the direction; the shadow takes the
        // far one. Reading order follows the block either way, which is what makes a list drawn upward
        // legible at all.
        var block = new List<(string Content, string Background)>(height)
        {
            ($"[{ScreenPalette.Label}]{(above ? "▴" : "▾")} {caption}[/]", ScreenPalette.MenuBg),
        };
        block.InsertRange(above ? 0 : 1, entries);

        var inner = block.Max(row => MarkupText.VisibleLength(row.Content));
        if (width > ChoiceIndent.Length + 2)
        {
            inner = Math.Min(inner, width - ChoiceIndent.Length - 2);
        }

        var lines = block.ConvertAll(row => MenuLine(row.Content, row.Background, inner));
        lines.Insert(above ? 0 : lines.Count, Shadow(inner));

        for (var i = 0; i < lines.Count; i++)
        {
            var at = start + i;
            if (at < column.Count)
            {
                column[at] = lines[i];
            }
            else
            {
                column.Add(lines[i]);
            }
        }

        return column;
    }

    /// <summary>
    /// The dropdown's caption and its drawn entries: the choices the buffer narrows to
    /// (<see cref="ScreenField.Matching"/>), windowed to <see cref="MaxChoiceRows"/> around the one the
    /// buffer names, so the marked entry is always on screen.
    /// </summary>
    private static (string Caption, List<(string Content, string Background)> Entries) ChoiceContent(
        ScreenFieldEdit open)
    {
        var all = open.Choices!;
        var visible = open.VisibleChoices;
        var caption = open.ClosedChoices ? ClosedChoicesCaption : OpenChoicesCaption;
        var entries = new List<(string, string)>();

        if (visible.Count == 0)
        {
            return ($"{caption}  {(open.ClosedChoices ? NoMatchClosed : NoMatchOpen)}", entries);
        }

        var marked = ScreenField.IndexOf(visible, open.Text);
        var take = Math.Min(MaxChoiceRows, visible.Count);
        var first = visible.Count <= take
            ? 0
            : Math.Clamp(Math.Max(marked, 0) - ((take - 1) / 2), 0, visible.Count - take);

        for (var i = first; i < first + take; i++)
        {
            var name = MarkupText.Escape(visible[i]);
            entries.Add(i == marked
                ? ($"[{ScreenPalette.Accent}]▸[/] [{ScreenPalette.Value}]{name}[/]", ScreenPalette.MenuSelectedBg)
                : ($"  [{ScreenPalette.Label}]{name}[/]", ScreenPalette.MenuBg));
        }

        // The count is only worth a caption when it is news: the list is capped, or the buffer has
        // narrowed it. "4 of 4" would just be noise on every route field ever opened.
        return (take < all.Count ? $"{caption}  {take} of {all.Count}" : caption, entries);
    }

    /// <summary>
    /// One row of the floating block: its markup, inset from the column's edge, padded to the block's
    /// inner width on a raised background. It hugs its content rather than spanning the pane, because a
    /// full-width band is what the pane's own rows look like and this must not be mistaken for one.
    /// </summary>
    private static string MenuLine(string content, string bg, int inner) =>
        $"{ChoiceIndent}[on {bg}] {MarkupText.PadVisible(content, Math.Max(0, inner))} [/]";

    /// <summary>
    /// The block's far edge, offset a cell the way a dropped shadow is. It costs one row and buys the
    /// thing a cell grid cannot otherwise say: that the pane's own rows continuing below (or above) the
    /// list are *behind* it and not part of it.
    /// </summary>
    private static string Shadow(int inner) =>
        $"{ChoiceIndent} [on {ScreenPalette.MenuShadow}]{new string(' ', Math.Max(0, inner + 1))}[/]";

    /// <summary>
    /// Draws a value the keyboard cannot change where it is drawn. It gets the muted ink and, decisively,
    /// <em>no</em> field well: a well means "you can change this here", its absence means "you cannot".
    /// <para>
    /// Scoped to rows reading <c>label   value</c>, where the ambiguity lives. A checkbox and a radio
    /// group carry an affordance of their own and are left alone.
    /// </para>
    /// </summary>
    internal static string ReadOnly(string text) => $"[{ScreenPalette.Muted}]{MarkupText.Escape(text)}[/]";

    /// <summary>The glyph a masked value is drawn in — one per character of the buffer.</summary>
    internal const char MaskGlyph = '•';

    /// <summary>
    /// How wide a masked value is drawn when nothing is being typed into it. Fixed, and deliberately not
    /// the value's own length: a resting screen is the one a screenshot or a snapshot catches, and a mask
    /// that grew with the secret would publish its length to anyone looking at the picture.
    /// </summary>
    internal const int RestingMaskWidth = 8;

    /// <summary><paramref name="length"/> mask glyphs — what a secret looks like in markup.</summary>
    internal static string Mask(int length) => new(MaskGlyph, Math.Max(0, length));

    /// <summary>
    /// A set secret at rest: <see cref="RestingMaskWidth"/> glyphs in the ordinary value ink, so the row
    /// reads as holding something. Drawn in a well like any other editable value, because it is one.
    /// </summary>
    internal static string RestingMask() => $"[{ScreenPalette.Value}]{Mask(RestingMaskWidth)}[/]";

    /// <summary>How far a legend's continuation rows are indented, to clear its label.</summary>
    internal const int LegendLabel = 7;

    /// <summary>
    /// Spells out the glyphs a list's rows are written in, at the foot of the column that draws them.
    /// These screens compress a rule into single cells, and a compressed value cannot say what its own
    /// marks mean — <c>H</c> could as easily be "hidden" as "highlight".
    /// <para>
    /// At the foot rather than beside the header: the header names the row's columns and these are the
    /// marks inside them, and the slack in a list column is at the bottom. Entries wrap to
    /// <paramref name="width"/>, since the column is a function of the screen's width
    /// (<see cref="SplitWidth"/>).
    /// </para>
    /// </summary>
    /// <param name="label">What the block is called, drawn on its first row only.</param>
    /// <param name="cells">The entries, already coloured — lit when they describe the selected row.</param>
    /// <param name="width">The column's width, which the block wraps to.</param>
    internal static IEnumerable<string> Legend(string label, IEnumerable<string> cells, int width)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(cells);

        var line = $"[{ScreenPalette.Label}]{MarkupText.Escape(label)}[/]"
            + new string(' ', Math.Max(1, LegendLabel - label.Length));
        var used = LegendLabel;

        foreach (var cell in cells)
        {
            var cellWidth = MarkupText.VisibleLength(cell) + 2;
            if (used > LegendLabel && width > 0 && used + cellWidth > width)
            {
                yield return line.TrimEnd();
                line = new string(' ', LegendLabel);
                used = LegendLabel;
            }

            line += cell + "  ";
            used += cellWidth;
        }

        yield return line.TrimEnd();
    }

    /// <summary>
    /// One entry of a <see cref="Legend"/>: its glyph and what the glyph means, lit when the selected
    /// row carries it and muted when it doesn't. Lighting them is what turns a key into a reading of the
    /// row the cursor is on, which is the same trick F2's attribute legend plays on the open buffer.
    /// </summary>
    internal static string LegendEntry(string glyph, string meaning, bool lit) => lit
        ? $"[{ScreenPalette.Accent}]{glyph}[/] [{ScreenPalette.Value}]{MarkupText.Escape(meaning)}[/]"
        : $"[{ScreenPalette.Label}]{glyph} {MarkupText.Escape(meaning)}[/]";

    /// <summary>
    /// The one row an <em>empty</em> trigger set gets in a flattened pane. F2, F3, F4 and F6 each draw one
    /// column of every set's rules, so a set holding none of that kind would be drawn nowhere at all.
    /// <para>
    /// A readout and not a row: the cursor cannot reach it, because it stands for a set rather than an
    /// item, and a cursor stop here would be a row that <c>[[- del]]</c>, Space and ⏎ all had to except.
    /// Moving an item into the set is what replaces it with real rows.
    /// </para>
    /// </summary>
    /// <param name="set">The set with nothing in it.</param>
    /// <param name="noun">What the screen's rows are called, plural — <c>triggers</c>, <c>timers</c>.</param>
    internal static string EmptySet(string set, string noun) =>
        $"  [{ScreenPalette.Muted}]▪ {MarkupText.Escape(set)} — no {MarkupText.Escape(noun)}[/]";

    /// <summary>
    /// Draws a pane's button rows, appended after its list. They come from the pane's own
    /// <see cref="ScreenButton"/>s rather than being written out per screen, so the label the cursor lands
    /// on and the command ⏎ runs cannot drift apart.
    /// <para>
    /// The two kinds are drawn differently because they are different things. A button that <b>builds</b>
    /// is a chip in the accent — somewhere the cursor goes and ⏎ presses. A <b>removal</b> is not a cursor
    /// stop (<see cref="ScreenModel.Sizes"/>), so a chip would be an affordance for something the keyboard
    /// cannot reach; it is drawn as a reading of what Delete would take — <c>Del  removes Aetherfall</c> —
    /// because the target is the one thing a destructive key must not leave off-screen.
    /// </para>
    /// </summary>
    /// <param name="buttons">The pane's button rows, in the order the model appends them.</param>
    /// <param name="cursor">Where the keyboard is, so the focused button gets the cursor bar.</param>
    /// <param name="pane">Which pane these belong to.</param>
    /// <param name="firstIndex">The pane row index of the first button — i.e. the list's length.</param>
    /// <param name="width">How wide the cursor bar runs, matching the pane's other rows.</param>
    internal static List<string> Buttons(
        IReadOnlyList<ScreenRow> buttons, ScreenFocus cursor, int pane, int firstIndex, int width)
    {
        ArgumentNullException.ThrowIfNull(buttons);

        var lines = new List<string>(buttons.Count);
        for (var i = 0; i < buttons.Count; i++)
        {
            if (buttons[i].Button is not { } button)
            {
                continue;
            }

            lines.Add(button.Kind switch
            {
                ScreenButtonKind.Remove => KeyHintRow(button, RemovesWord),
                ScreenButtonKind.Detail => KeyHintRow(button, InfoWords),
                _ => Cursor(AddRow(button), cursor.IsOn(pane, firstIndex + i), width),
            });
        }

        return lines;
    }

    /// <summary>A building button: a pressable chip in the accent, naming its source when it has one.</summary>
    private static string AddRow(ScreenButton button)
    {
        var row = $"[{ScreenPalette.Accent}][[{MarkupText.Escape(button.Label)}]][/]";
        return button.Target is { } target
            ? row + $" [{ScreenPalette.Value}]{MarkupText.Escape(target)}[/]"
            : row;
    }

    /// <summary>
    /// What a targeted key would act on, as a row: the key, the verb, the victim or subject. Never drawn
    /// with a cursor bar, because the cursor cannot get there — that is the whole fix for "only the last
    /// world can be deleted", and the reason the INFO key is drawn this way too rather than as a chip.
    /// </summary>
    private static string KeyHintRow(ScreenButton button, string verb) =>
        $"[{ScreenPalette.Accent}]{MarkupText.Escape(button.Label)}[/]"
        + $"  [{ScreenPalette.Label}]{verb}[/] "
        + $"[{ScreenPalette.Value}]{MarkupText.Escape(button.Target ?? string.Empty)}[/]";

    /// <summary>The verb on a removal row, between the key and what it would take.</summary>
    internal const string RemovesWord = "removes";

    /// <summary>The verb on a report row, between the key and what it would report on.</summary>
    internal const string InfoWords = "info on";

    /// <summary>
    /// Where the cursor is within one of a screen's lists — <c>trigger 1/4</c>, <c>world 2/2</c>. Every
    /// footer's context line opens with one, so the eight screens answer the same question in the same
    /// words.
    /// </summary>
    internal static string Position(string noun, int index, int count) =>
        $"{noun} {(index + 1).ToString(CultureInfo.InvariantCulture)}"
        + $"/{count.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// A footer's context line: a <see cref="Position"/>, then whatever identifies the thing it points at.
    /// Null and empty parts are dropped, so a screen with nothing selected renders an empty context rather
    /// than a stranded separator.
    /// </summary>
    internal static string Context(params string?[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var present = parts.Where(p => !string.IsNullOrEmpty(p));
        var joined = string.Join("  ·  ", present);
        return joined.Length == 0 ? string.Empty : $"[{ScreenPalette.Label}]{joined}[/]";
    }

    /// <summary>A full-width one-row band — the header or the footer.</summary>
    internal static MarkupControl Band(string line, string bg) => new(new List<string> { line })
    {
        BackgroundColor = new Color(bg),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    /// <summary>
    /// How many rows a two-column screen's body has: everything but the header band and the action bar.
    /// Zero when the caller has no height, which is how every block below reads "draw it all".
    /// </summary>
    internal static int Rows(int height) => height <= 0 ? 0 : Math.Max(1, height - 2);

    /// <summary>
    /// The frame every two-column settings screen shares: a header band on the first row, an action bar on
    /// the last, and between them two columns divided by a hairline.
    /// <para>
    /// The body is <em>sized to its content</em> rather than stretched, so a screen with four rules does
    /// not draw a thirty-row empty pane under them. The hairline stops where the columns stop and the
    /// slack below belongs to the backdrop. A caller with no height falls back to filling.
    /// </para>
    /// </summary>
    /// <param name="header">The header band.</param>
    /// <param name="footer">The action bar.</param>
    /// <param name="left">The list column.</param>
    /// <param name="right">The column beside it.</param>
    /// <param name="leftWidth">How wide the list column runs (see <see cref="SplitWidth"/>).</param>
    /// <param name="content">How many rows the taller of the two columns holds.</param>
    /// <param name="rows">How many rows the body has to spend, or 0 when the caller has no height.</param>
    internal static IWindowControl Split(
        MarkupControl header,
        MarkupControl footer,
        MarkupControl left,
        MarkupControl right,
        int leftWidth,
        int content,
        int rows)
    {
        var body = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Column(c => c.Width(leftWidth).Add(left))
            .Column(c => c.Width(1).Add(VerticalRule()))
            .Column(c => c.Width(1).Add(Filler()))
            .Column(c => c.Flex(1).Add(right))
            .Build();

        var root = Controls.Grid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill);

        if (rows <= 0)
        {
            root.Rows(GridLength.Cells(1), GridLength.Star(1), GridLength.Cells(1)).Columns(GridLength.Star(1));
            root.Place(header, 0, 0, 1, 1);
            root.Place(body, 1, 0, 1, 1);
            root.Place(footer, 2, 0, 1, 1);
            return root.Build();
        }

        root.Rows(
                GridLength.Cells(1),
                GridLength.Cells(Math.Clamp(content, 1, rows)),
                GridLength.Star(1),
                GridLength.Cells(1))
            .Columns(GridLength.Star(1));
        root.Place(header, 0, 0, 1, 1);
        root.Place(body, 1, 0, 1, 1);
        root.Place(footer, 3, 0, 1, 1);
        return root.Build();
    }

    /// <summary>Widens a column panel to its arranged width so its content isn't hugged.</summary>
    internal static MarkupControl Stretch(MarkupControl control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        return control;
    }

    /// <summary>
    /// The one-cell rule between two body columns. A <see cref="MarkupControl"/> with no lines measures
    /// to nothing and never paints its background, so the rule is an empty grid instead — a grid's
    /// background covers its whole arranged area, giving a full-height hairline.
    /// </summary>
    internal static IWindowControl VerticalRule()
    {
        var rule = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Column(c => c.Flex(1).Add(Filler()))
            .Build();
        rule.BackgroundColor = new Color(ScreenPalette.Rule);
        return rule;
    }

    /// <summary>An empty panel, used to hold a spacer or flex column open.</summary>
    internal static MarkupControl Filler() => new(new List<string>());

    /// <summary>Prefixes each line with a space so a column doesn't sit flush against the rule.</summary>
    internal static List<string> Indent(IEnumerable<string> lines) => lines.Select(l => " " + l).ToList();
}
