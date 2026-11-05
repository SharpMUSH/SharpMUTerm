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
/// (<c>COMPOSE  +pose  literal</c>), the command field (<see cref="ComposerCommandRow"/>), the F1 composer's editor for
/// the text, and a key row
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
    private readonly HeadingBand.Ink? _heading;
    private readonly Func<DockedComposer, ComposerModel> _describe;
    private bool _armed;
    private bool _quiet;
    private bool _fromHost;

    /// <param name="describe">
    /// The model for this composer's state, which the host also records as drawn and hands a native editor.
    /// </param>
    /// <param name="heading">The heading's band (<see cref="HeadingBand"/>); null draws the bare accent label.</param>
    internal DockedComposer(string windowId, string sessionKey, ScreenTheme scheme, string accent, Func<DockedComposer, ComposerModel> describe, HeadingBand.Ink? heading = null)
    {
        WindowId = windowId;
        SessionKey = sessionKey;
        _accent = accent;
        _heading = heading;
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
            // focus, and from the other while the command line has it. The well (FieldBg), as the terminal mockup draws
            // the text: the ground an input sits in, a step below the side column rather than raised above it.
            BackgroundColor = scheme.Colour(ScreenPalette.FieldBg),
            FocusedBackgroundColor = scheme.Colour(ScreenPalette.FieldBg),
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

        CommandRow = new ComposerCommandRow(scheme, plane: null);
        CommandRow.Field.ContentChanged += (_, _) =>
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
        grid.Rows(GridLength.Cells(1), GridLength.Cells(1), GridLength.Star(1), GridLength.Cells(1)).Columns(GridLength.Star(1));
        grid.Place(_header, 0, 0, 1, 1);
        grid.Place(CommandRow.Control, 1, 0, 1, 1);
        // A cell of the well before the text, the terminal mockup's padded body: a column of the well's own colour,
        // so the text sits in the well rather than against the side column's rule.
        var well = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Column(c => c.Width(1).Add(new MarkupControl(new List<string>())))
            .Column(c => c.Flex(1).Add(Editor))
            .Build();
        well.BackgroundColor = scheme.Colour(ScreenPalette.FieldBg);
        grid.Place(well, 2, 0, 1, 1);
        grid.Place(_footer, 3, 0, 1, 1);
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

    /// <summary>The command field above the text.</summary>
    internal ComposerCommandRow CommandRow { get; }

    /// <summary>The command field's text, one line (<see cref="ComposerModel.Verb"/>).</summary>
    internal string Verb => CommandRow.Text;

    /// <summary>
    /// Whether the keyboard is in the command field rather than the text: ⇥ moves it (<see cref="ToggleField"/>), and a
    /// click in either field puts it there. The composer's own state, never the model's: where a caret is is not a fact
    /// the other host draws.
    /// </summary>
    internal bool InCommand { get; set; }

    /// <summary>The field the keyboard is in: the one the host focuses and hands keys to while this is armed.</summary>
    internal MultilineEditControl Focused => InCommand ? CommandRow.Field : Editor;

    /// <summary>Whether <paramref name="control"/> is one of this composer's two fields.</summary>
    internal bool Owns(object? control) => ReferenceEquals(control, Editor) || ReferenceEquals(control, CommandRow.Field);

    /// <summary>⇥: the keyboard to the other field.</summary>
    internal void ToggleField() => InCommand = !InCommand;

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

    /// <summary>The command a native editor's command field now holds, written in as <see cref="SetFromHost"/> writes the text.</summary>
    internal void SetCommandFromHost(string command)
    {
        if (string.Equals(Verb, ComposeMessage.CleanCommand(command), StringComparison.Ordinal))
        {
            return;
        }

        _fromHost = true;
        try
        {
            CommandRow.Set(command);
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
    internal void Load(string body, ComposeEscaping escaping, string? verb = null)
    {
        Escaping = escaping;
        Revision++;
        _quiet = true;
        try
        {
            if (!string.Equals(Body, body, StringComparison.Ordinal))
            {
                Editor.SetContent(body);
            }

            if (verb is not null)
            {
                CommandRow.Set(verb);
            }
        }
        finally
        {
            _quiet = false;
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
        var rows = Math.Max(1, height - 3);
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
        var verb = model.HeadingVerb;
        if (_heading is { } band)
        {
            var bandMode = model.Escaping == ComposeEscaping.Literal ? $"[{band.Accent}]literal[/]" : "as typed";
            var rest = (verb.Length > 0 ? $"  {Escape(verb)}" : string.Empty) + $"  {bandMode}";
            var lead = model.Armed ? $"[{band.Accent}]▌[/]" : null;
            var chars = ComposerModel.CharactersLabel(model.Characters);
            _header.SetContent(new List<string> { HeadingBand.Row("COMPOSE", rest, chars, _width > 0 ? _width : VisibleLength(rest) + 9, band, lead) });
            RenderFooter(model);
            return;
        }

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

        RenderFooter(model);
    }

    /// <summary>
    /// The key row under the editor, laddered (<see cref="KeyLadder"/>) to the pane: long labels, then short ones, then
    /// ⌥W, ⇥, ⌥L and F1 given up in that order; ⌃S and Esc stay. It was cut at the edge (<c>Esc back ·…</c>) at 80 columns.
    /// </summary>
    private void RenderFooter(ComposerModel model)
    {
        var keys = model.Keys.Select(k => new LadderKey(k.Key, k.Label, k.Short, k.Key switch
        {
            "⌥W" => 1,
            "⇥" => 2,
            "⌥L" => 3,
            "F1" => 4,
            _ => LadderKey.Keep,
        })).ToList();
        var footer = " " + KeyLadder.Fit(keys, _width > 0 ? _width - 1 : 0, Key, label => $"[dim]{Escape(label)}[/]", "[dim] · [/]");
        _footer.SetContent(new List<string> { _width > 0 ? ElideVisible(footer, _width) : footer });
    }

    private string Key(string key) => $"[{_accent}]{key}[/]";
}
