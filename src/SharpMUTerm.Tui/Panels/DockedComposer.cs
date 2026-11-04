using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Text;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui.Panels;

/// <summary>
/// The composer as a pane: a header naming what is being written and how it will be escaped
/// (<c>COMPOSE  +pose  literal</c>), the F1 composer's editor, and a key row
/// (<c>⌃S send · ⌥L literal · F1 full screen · Esc command line</c>).
/// <para>
/// <b>The editor is the F1 composer's</b> — a <see cref="MultilineEditControl"/> that inserts on ⏎, wraps words
/// and keeps undo — and the post it builds is <see cref="ComposeMessage.Build"/>'s, so a docked post and a
/// full-screen one are the same command. Its buffer <em>is</em> the character's draft: the host writes every
/// change into the store F1 opens from, so F1 here carries on the same text.
/// </para>
/// <para>
/// <b>It does not route its own keys.</b> The host decides, from its pane, whether it is armed — the place typing,
/// paste and the caret go — and hands each key to <see cref="Editor"/> itself. That is the rule the command
/// line follows, and it is what keeps one fact behind "which control gets a keystroke".
/// </para>
/// <para>
/// <b>It draws from a <see cref="ComposerModel"/></b>, the one the window's native editor draws: the host describes the
/// composer (<c>describe</c>) and <see cref="Render(ComposerModel)"/> turns the model into the header and key rows, so
/// the two hosts offer the same keys and say the same mode. <see cref="Revision"/> moves whenever the buffer changes by
/// any hand but a native editor's (<see cref="SetFromHost"/>), which is how that editor knows to take the text.
/// </para>
/// <para>
/// The grid is kept and re-parented across pane rebuilds. A disposed <c>GridControl</c> disposes its children and
/// nothing else, and none of these three keeps state that a dispose clears, so the same instance serves for the
/// life of the panel (pinned by <c>DockedComposerTests</c>).
/// </para>
/// </summary>
internal sealed class DockedComposer
{
    private readonly MarkupControl _header;
    private readonly MarkupControl _footer;
    private readonly string _accent;
    private readonly Func<DockedComposer, ComposerModel> _describe;
    private bool _armed;
    private bool _quiet;
    private bool _fromHost;

    /// <param name="describe">
    /// The model for this composer's state, which the host also records as drawn and hands a native editor.
    /// </param>
    internal DockedComposer(string windowId, string sessionKey, ScreenTheme scheme, string accent, Func<DockedComposer, ComposerModel> describe)
    {
        WindowId = windowId;
        SessionKey = sessionKey;
        _accent = accent;
        _describe = describe;
        Editor = new MultilineEditControl
        {
            WrapMode = WrapMode.WrapWords,
            ShowLineNumbers = false,
            IsEditing = true,
            VerticalAlignment = VerticalAlignment.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,

            // Esc is the host's — it hands the keyboard back to the command line — so the editor must not
            // spend it dropping out of edit mode first.
            EscapeExitsEditMode = false,
            PlaceholderText = "Write a post. ⏎ starts a new line; ⌃S sends it as one command.",

            // Both pairs, as the F1 composer sets them: the control paints from the focused pair whenever it has
            // focus, and from the other while the command line has it.
            BackgroundColor = scheme.Colour(ScreenPalette.EditBg),
            FocusedBackgroundColor = scheme.Colour(ScreenPalette.EditBg),
            ForegroundColor = scheme.Colour(ScreenPalette.Value),
            FocusedForegroundColor = scheme.Colour(ScreenPalette.Value),
            SelectionBackgroundColor = scheme.Colour(ScreenPalette.CursorBg),
            SelectionForegroundColor = scheme.Colour(ScreenPalette.Value),
        };
        Editor.ContentChanged += (_, _) =>
        {
            if (!_fromHost)
            {
                Revision++;
            }

            if (!_quiet)
            {
                Redraw();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };

        _header = new MarkupControl(new List<string>()) { HorizontalAlignment = HorizontalAlignment.Stretch };
        _footer = new MarkupControl(new List<string>()) { HorizontalAlignment = HorizontalAlignment.Stretch };

        var grid = Controls.Grid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill);
        grid.Rows(GridLength.Cells(1), GridLength.Star(1), GridLength.Cells(1)).Columns(GridLength.Star(1));
        grid.Place(_header, 0, 0, 1, 1);
        grid.Place(Editor, 1, 0, 1, 1);
        grid.Place(_footer, 2, 0, 1, 1);
        Control = grid.Build();
        Redraw();
    }

    /// <summary>The panel window this is drawn in.</summary>
    internal string WindowId { get; }

    /// <summary>Whose draft this is: the <c>World.Character</c> key the panel belongs to.</summary>
    internal string SessionKey { get; }

    /// <summary>What the pane hosts.</summary>
    internal IWindowControl Control { get; }

    /// <summary>The editor, which the host focuses and hands keys to while this is armed.</summary>
    internal MultilineEditControl Editor { get; }

    /// <summary>The escaping mode, toggled by ⌥L, as F1's is.</summary>
    internal ComposeEscaping Escaping { get; private set; } = ComposeEscaping.AsTyped;

    /// <summary>The buffer, with its lines broken by <c>\n</c> whatever the platform's newline.</summary>
    internal string Body => Editor.GetContent().Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>Whether typing goes here. Only the host sets it.</summary>
    internal bool Armed
    {
        get => _armed;
        set
        {
            if (_armed != value)
            {
                _armed = value;
                Redraw();
            }
        }
    }

    /// <summary>
    /// Moves whenever the buffer is replaced or edited by anything but <see cref="SetFromHost"/>: a key the cells took,
    /// F1 closing over this copy, a send clearing it. A native editor takes the text only when this has moved.
    /// </summary>
    internal long Revision { get; private set; }

    /// <summary>The model last drawn.</summary>
    internal ComposerModel? Model { get; private set; }

    /// <summary>The dictionary marking this buffer, once one is opened for it.</summary>
    internal SpellChecker? Spelling { get; private set; }

    /// <summary>Marks the words <paramref name="checker"/> does not know, in <paramref name="marked"/>, as F1 does.</summary>
    internal void UseSpelling(SpellChecker checker, Color marked)
    {
        Spelling = checker;
        Editor.SyntaxHighlighter = new SpellingHighlighter(checker, marked);
        Redraw();
    }

    /// <summary>
    /// The text a native editor over this composer now holds: written into the buffer without moving
    /// <see cref="Revision"/>, so the editor that sent it keeps its own caret and undo, and raising
    /// <see cref="Changed"/> like a keystroke, so the draft store follows.
    /// </summary>
    internal void SetFromHost(string body)
    {
        body = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (string.Equals(Body, body, StringComparison.Ordinal))
        {
            return;
        }

        _fromHost = true;
        try
        {
            Editor.SetContent(body);
        }
        finally
        {
            _fromHost = false;
        }
    }

    /// <summary>Raised after the buffer or the mode changed by the writer's hand (never by <see cref="Load"/>).</summary>
    internal event EventHandler? Changed;

    /// <summary>The header row's markup, for the tests.</summary>
    internal string HeaderMarkup => _header.Text;

    /// <summary>The key row's markup, for the tests.</summary>
    internal string FooterMarkup => _footer.Text;

    /// <summary>Replaces the buffer and mode with a draft kept elsewhere (F1 closing), raising nothing.</summary>
    internal void Load(string body, ComposeEscaping escaping)
    {
        Escaping = escaping;
        Revision++;
        if (!string.Equals(Body, body, StringComparison.Ordinal))
        {
            _quiet = true;
            try
            {
                Editor.SetContent(body);
            }
            finally
            {
                _quiet = false;
            }
        }

        Redraw();
    }

    /// <summary>⌥L: flips the escaping and says so in the header.</summary>
    internal void ToggleEscaping()
    {
        Escaping = Escaping == ComposeEscaping.Literal ? ComposeEscaping.AsTyped : ComposeEscaping.Literal;
        Redraw();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Sizes the editor to the pane's box, less the header and key rows.</summary>
    internal void Fit(int width, int height)
    {
        var rows = Math.Max(1, height - 2);
        if (Editor.ViewportHeight != rows)
        {
            Editor.ViewportHeight = rows;
        }

        if (_width != width)
        {
            _width = width;
            Redraw();
        }
    }

    /// <summary>The pane's width, which picks the key row's long or short form.</summary>
    private int _width;

    /// <summary>
    /// The verb the post starts with — <c>+pose</c>, <c>say</c> — for the header, so a reader can see what the
    /// buffer will be sent as without reading it. Empty for an empty buffer.
    /// </summary>
    internal static string Verb(string body) => ComposerModel.VerbOf(body);

    /// <summary>Re-describes the composer and draws the model.</summary>
    internal void Redraw()
    {
        var model = _describe(this);
        Model = model;
        Render(model);
    }

    /// <summary>
    /// The cell header and key row for <paramref name="model"/>: <c>▌COMPOSE  +pose  literal</c>, and the model's keys
    /// in the long form, or the short one when the pane is too narrow for it. Both forms list the same keys, so a
    /// narrow pane hides no gesture.
    /// </summary>
    internal void Render(ComposerModel model)
    {
        var verb = model.Verb;
        var mode = model.Escaping == ComposeEscaping.Literal ? $"[{_accent}]literal[/]" : "[dim]as typed[/]";
        var marker = model.Armed ? $"[{_accent}]▌[/]" : " ";
        var title = $"{marker}[bold {_accent}]COMPOSE[/]" + (verb.Length > 0 ? $"  {Escape(verb)}" : string.Empty) + $"  {mode}";

        // The length cell at the right edge, as F1 has it; the title gives way first, and a pane too narrow for both
        // keeps the title.
        var length = $"[dim]{ComposerModel.CharactersLabel(model.Characters)}[/] ";
        _header.SetContent(new List<string>
        {
            _width <= 0 ? $"{title}  {length}"
                : _width >= VisibleLength(title) + VisibleLength(length) + 2 ? SpreadLR(title, length, _width)
                : ElideVisible(title, _width),
        });

        var footer = KeyRow(model.Keys, shortForm: false);
        if (_width > 0 && VisibleLength(footer) > _width)
        {
            footer = KeyRow(model.Keys, shortForm: true);
        }

        _footer.SetContent(new List<string> { _width > 0 ? ElideVisible(footer, _width) : footer });
    }

    /// <summary>The key row: <c> ⌃S send · ⌥L literal · …</c>, a key in the accent and its word dimmed.</summary>
    private string KeyRow(IReadOnlyList<ComposerKey> keys, bool shortForm)
    {
        var row = new System.Text.StringBuilder(" ");
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var label = Escape(shortForm ? key.Short ?? key.Label : key.Label);
            row.Append(key.Key.Length > 0 ? $"{Key(key.Key)}[dim] {label}" : $"[dim]{label}");
            row.Append(i < keys.Count - 1 ? " · [/]" : "[/]");
        }

        return row.ToString();
    }

    private string Key(string key) => $"[{_accent}]{key}[/]";
}
