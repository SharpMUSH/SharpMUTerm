using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Client.Composer;

/// <summary>Where a composer is drawn: F1's full screen, or a pane of a layout.</summary>
public enum ComposerPlace
{
    /// <summary>F1: a modal over the workspace.</summary>
    FullScreen,

    /// <summary>The docked composer panel, in a pane.</summary>
    Docked,
}

/// <summary>A word the dictionary does not know, as a range of the whole buffer (lines broken by <c>\n</c>).</summary>
/// <param name="Start">Offset into the buffer.</param>
/// <param name="Length">How many characters.</param>
public readonly record struct SpellRange(int Start, int Length);

/// <summary>
/// One entry of a composer's key row: a chord in the cell glyphs (<c>⌃S</c>, <c>⌥L</c>, <c>Esc</c>) and what it does.
/// A host that draws keys in words (the window) spells the chord with <c>KeyGlyphs.Words</c>.
/// </summary>
/// <param name="Key">The chord; empty for a hint that is no key (<c>click here to write</c>).</param>
/// <param name="Label">What it does.</param>
/// <param name="Short">A shorter label for a narrow pane; null when <paramref name="Label"/> is already short.</param>
public sealed record ComposerKey(string Key, string Label, string? Short = null);

/// <summary>
/// A buffer being edited rather than a post: a MOO's (MCP simpleedit) or a script file's. Saved as its lines — no
/// <c>%r</c> join, no escaping, no spelling.
/// </summary>
/// <param name="Name">What it is called: the MOO's name for it, or the script's name under the scripts folder.</param>
/// <param name="Type">The MOO's type (<c>moo-code</c>), or <c>lua</c> for a script.</param>
/// <param name="Script">Whether this is a script file (⌃S saves it to disk and reloads the character's scripts).</param>
/// <param name="ReadOnly">Whether it can only be read: a package's script.</param>
/// <param name="Status">What the last save said (<c>saved · 2 files loaded</c>, or the first load error), or null.</param>
/// <param name="Line">The line the caret opens on, from 1; 0 for the end.</param>
/// <param name="ErrorLine">
/// The line of this script the last load error named (<c>corvid.lua:(14,…)</c>), from 1; 0 for none. Both hosts mark it
/// from this one field — the cells on a warning band behind the line, the window in its gutter and behind the line
/// (<see cref="ScriptErrors.LineIn(string?, string)"/>).
/// </param>
public sealed record ComposerEdit(string Name, string Type, bool Script = false, bool ReadOnly = false, string? Status = null, int Line = 0, int ErrorLine = 0);

/// <summary>
/// <b>A composer, as both hosts draw it.</b> The terminal draws F1 and the docked panel in cells; the window draws them
/// with a native editor. Both read this: who the post goes to, the draft, the escaping mode, the MOO edit it is (if it
/// is one), which dictionary marks it and the words it marks, and the keys the footer offers.
/// <para>
/// <b>The draft is the client's.</b> <see cref="Body"/> is the character's draft as the client holds it, and
/// <see cref="Revision"/> moves only when the client replaced it — F1 closing over a docked copy, a send clearing it,
/// a key the cells took. A native editor that typed the change itself sees the revision unmoved and keeps its own
/// text (and its caret and undo); one that sees it move takes the client's.
/// </para>
/// <para>
/// <b>Equal by value</b>, lists included, because the window is told about a panel only when it changed and a
/// model rebuilt on every frame must compare equal to the last one.
/// </para>
/// </summary>
/// <param name="Place">F1 or the docked panel.</param>
/// <param name="Target">Who it sends to, as the window title names the connection; empty when nothing is connected.</param>
/// <param name="Body">The draft, lines broken by <c>\n</c>.</param>
/// <param name="Escaping">⌥L's mode.</param>
/// <param name="Edit">The MOO buffer this is an edit of, or null for a post.</param>
/// <param name="Dictionary">The dictionary's name (<c>en_GB</c>), or null for none — and none for a MOO edit.</param>
/// <param name="CanSend">Whether there is a connection for ⌃S to reach.</param>
/// <param name="Armed">Whether typing goes here: always for F1; for the docked panel, the client's one fact.</param>
/// <param name="Revision">Moves when the client replaced <paramref name="Body"/>.</param>
/// <param name="Marks">The words the dictionary does not know, over <paramref name="Body"/>.</param>
/// <param name="Keys">The key row.</param>
public sealed record ComposerModel(
    ComposerPlace Place,
    string Target,
    string Body,
    ComposeEscaping Escaping,
    ComposerEdit? Edit,
    string? Dictionary,
    bool CanSend,
    bool Armed,
    long Revision,
    IReadOnlyList<SpellRange> Marks,
    IReadOnlyList<ComposerKey> Keys)
{
    /// <summary>What ⌃S sends for this draft: <see cref="ComposeMessage.Build"/>, or null for nothing (or an edit).</summary>
    public string? Command => Edit is null ? ComposeMessage.Build(Body, Escaping) : null;

    /// <summary>
    /// Whether F1 shows the post as it will be sent (⌥P) — the <c>%r</c>-joined, escaped command — under the editor. The
    /// client's toggle for the run, never saved; always false for the docked panel and a MOO edit.
    /// </summary>
    public bool Preview { get; init; }

    /// <summary>The header's length cell for this draft: <see cref="SentLength"/>.</summary>
    public int Characters => SentLength(Body, Escaping, Edit is not null);

    /// <summary>
    /// How long a draft is as the game will get it: the length of <see cref="ComposeMessage.Build"/>'s command, with its
    /// <c>%r</c> breaks and its escapes, because that is what a board's length limit counts. A MOO edit is saved as its
    /// lines, so it counts its text. A host editor that holds text the model has not caught up with asks this directly.
    /// </summary>
    public static int SentLength(string body, ComposeEscaping escaping, bool edit)
    {
        ArgumentNullException.ThrowIfNull(body);
        body = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        return edit ? body.TrimEnd('\n').Length : ComposeMessage.Build(body, escaping)?.Length ?? 0;
    }

    /// <summary>The length cell's words: <c>1 char</c>, <c>240 chars</c>.</summary>
    public static string CharactersLabel(int count) => count == 1 ? "1 char" : $"{count:N0} chars";

    /// <summary>
    /// The docked panel's footer for a draft: <c>2 paragraphs · 31 words</c>. A paragraph is a run of non-blank lines, as
    /// <see cref="ComposeMessage.Build"/> joins them with <c>%r%r</c> between; a word is a run of non-space characters.
    /// Empty for an empty draft.
    /// </summary>
    public static string Summary(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var paragraphs = 0;
        var inParagraph = false;
        foreach (var line in lines)
        {
            var blank = string.IsNullOrWhiteSpace(line);
            if (!blank && !inParagraph)
            {
                paragraphs++;
            }

            inParagraph = !blank;
        }

        var words = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words == 0)
        {
            return string.Empty;
        }

        static string Count(int n, string one) => n == 1 ? $"1 {one}" : $"{n:N0} {one}s";
        return $"{Count(paragraphs, "paragraph")} · {Count(words, "word")}";
    }

    /// <summary>
    /// The verb the post starts with — <c>+pose</c>, <c>say</c> — for the docked header, so a reader can see what the
    /// buffer will be sent as without reading it. Empty for an empty buffer.
    /// </summary>
    public string Verb => VerbOf(Body);

    /// <summary>
    /// The model for a composer's state. The keys are worked out here, so the cells and the window offer the same row:
    /// F1 names ⌃S, ⌥L (a post only), ⌥W (while a dictionary marks it) and Esc; the docked panel adds F1 and says what
    /// Esc does there.
    /// </summary>
    /// <param name="spelling">The dictionary, or null; ignored for a MOO edit, whose buffer is as often code as prose.</param>
    public static ComposerModel Create(
        ComposerPlace place,
        string target,
        string body,
        ComposeEscaping escaping,
        ComposerEdit? edit,
        SpellChecker? spelling,
        bool canSend,
        bool armed,
        long revision,
        bool preview = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(body);
        body = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        var checker = edit is null ? spelling : null;
        return new ComposerModel(
            place,
            target,
            body,
            escaping,
            edit,
            checker?.Name,
            canSend,
            armed,
            revision,
            ComposerMarks.For(body, checker),
            KeysFor(place, edit is not null, checker is not null, canSend, armed, preview, edit))
        {
            Preview = preview && place == ComposerPlace.FullScreen && edit is null,
        };
    }

    /// <summary>
    /// The window's docked editor placeholder while it does not hold the keyboard. It says what <see cref="ClickHint"/>
    /// says, so the window leaves that hint out of its key row (the cells draw no placeholder on an idle editor, and
    /// keep it).
    /// </summary>
    public const string ClickToWrite = "Click to write a post";

    /// <summary>The docked key row's keyless hint while the editor does not hold the keyboard.</summary>
    public const string ClickHint = "click here to write";

    /// <summary>The key row's keyless hint over a package's script, which can be read and never saved.</summary>
    public const string ReadOnlyHint = "read only — a package's script";

    /// <summary>What F1's key row calls ⌥W while no dictionary marks the post.</summary>
    public const string GetDictionary = "get a dictionary";

    /// <summary>The key row for a composer in this state.</summary>
    /// <param name="preview">Whether F1's as-sent preview is showing, which names what ⌥P does next.</param>
    /// <param name="editing">The edit it is, when <paramref name="edit"/>: a script saves to disk, a package's script only reads.</param>
    public static IReadOnlyList<ComposerKey> KeysFor(ComposerPlace place, bool edit, bool dictionary, bool canSend, bool armed, bool preview = false, ComposerEdit? editing = null)
    {
        if (place == ComposerPlace.Docked)
        {
            if (!armed)
            {
                return [new(string.Empty, ClickHint, "click to write"), new("F1", "full screen", "full")];
            }

            var docked = new List<ComposerKey> { new("⌃S", "send"), new("⌥L", "literal") };
            if (dictionary)
            {
                docked.Add(new("⌥W", "add word", "word"));
            }

            docked.Add(new("F1", "full screen", "full"));
            docked.Add(new("Esc", "command line", "back"));
            return docked;
        }

        if (editing is { Script: true, ReadOnly: true })
        {
            return [new(string.Empty, ReadOnlyHint, "read only"), new("Esc", "close")];
        }

        if (editing is { Script: true })
        {
            return [new("⌃S", "save and reload", "save"), new("Esc", "close (keeps unsaved text)", "close")];
        }

        if (edit)
        {
            return
            [
                new("⌃S", canSend ? "save" : "save (nothing connected)"),
                new("Esc", "close (keeps the edit)"),
            ];
        }

        var keys = new List<ComposerKey>
        {
            new("⌃S", canSend ? "send" : "send (nothing connected)"),
            new("⌥L", "escaping"),
        };
        // With no dictionary, ⌥W is the way to one: the offer, never a download on its own.
        keys.Add(dictionary ? new("⌥W", "add word") : new("⌥W", GetDictionary));

        keys.Add(new("⌥P", preview ? "hide as sent" : "as sent"));
        keys.Add(new("Esc", "close (keeps the draft)"));
        return keys;
    }

    /// <summary>The first word of the first non-blank line, at most sixteen characters.</summary>
    public static string VerbOf(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var first = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
        var space = first.IndexOfAny([' ', '\t']);
        var verb = space < 0 ? first : first[..space];
        return verb.Length <= 16 ? verb : verb[..15] + "…";
    }

    /// <inheritdoc />
    public bool Equals(ComposerModel? other) =>
        other is not null
        && Place == other.Place
        && Target == other.Target
        && Body == other.Body
        && Escaping == other.Escaping
        && Equals(Edit, other.Edit)
        && Dictionary == other.Dictionary
        && CanSend == other.CanSend
        && Armed == other.Armed
        && Revision == other.Revision
        && Preview == other.Preview
        && Marks.SequenceEqual(other.Marks)
        && Keys.SequenceEqual(other.Keys);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Place, Body, Escaping, Armed, Revision, Marks.Count, Keys.Count);
}

/// <summary>The words a dictionary marks in a whole buffer, as one list of ranges.</summary>
public static class ComposerMarks
{
    /// <summary>
    /// The words <paramref name="spelling"/> does not know in <paramref name="body"/>, each as a range of the whole
    /// buffer. Line by line, through <see cref="SpellChecker.Misspellings"/> — the very call the cell editor's
    /// highlighter makes per line — so the two hosts mark the same words. Empty with no dictionary.
    /// </summary>
    public static IReadOnlyList<SpellRange> For(string body, SpellChecker? spelling)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (spelling is null || body.Length == 0)
        {
            return [];
        }

        var marks = new List<SpellRange>();
        var offset = 0;
        foreach (var line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            foreach (var miss in spelling.Misspellings(line))
            {
                marks.Add(new SpellRange(offset + miss.Start, miss.Length));
            }

            offset += line.Length + 1;
        }

        return marks;
    }

    /// <summary>
    /// The word at <paramref name="caret"/> in <paramref name="body"/> (in it, or just after it), or null: the word ⌥W
    /// adds. <see cref="SpellChecker.WordAt"/> on the caret's line, the cell composer's rule.
    /// </summary>
    public static string? WordAt(string body, int caret)
    {
        ArgumentNullException.ThrowIfNull(body);
        body = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        caret = Math.Clamp(caret, 0, body.Length);
        var start = caret == 0 ? 0 : body.LastIndexOf('\n', caret - 1) + 1;

        var end = body.IndexOf('\n', caret);
        var line = body[start..(end < 0 ? body.Length : end)];
        return SpellChecker.WordAt(line, caret - start);
    }
}
