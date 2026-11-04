using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpMUTerm.Core.Commands;
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
    private bool _armed;
    private bool _quiet;

    internal DockedComposer(string windowId, string sessionKey, ScreenTheme scheme, string accent)
    {
        WindowId = windowId;
        SessionKey = sessionKey;
        _accent = accent;
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
    internal static string Verb(string body)
    {
        var first = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
        var space = first.IndexOfAny([' ', '\t']);
        var verb = space < 0 ? first : first[..space];
        return verb.Length <= 16 ? verb : verb[..15] + "…";
    }

    private void Redraw()
    {
        var verb = Verb(Body);
        var mode = Escaping == ComposeEscaping.Literal ? $"[{_accent}]literal[/]" : "[dim]as typed[/]";
        var marker = _armed ? $"[{_accent}]▌[/]" : " ";
        _header.SetContent(new List<string>
        {
            $"{marker}[bold {_accent}]COMPOSE[/]" + (verb.Length > 0 ? $"  {Escape(verb)}" : string.Empty) + $"  {mode}",
        });

        // The long form names what each key does; the short one, for a narrow column, keeps every key and the
        // two words that matter. Both list the same keys, so a narrow pane hides no gesture.
        var footer = _armed
            ? $" {Key("⌃S")}[dim] send · [/]{Key("⌥L")}[dim] literal · [/]{Key("F1")}[dim] full screen · [/]{Key("Esc")}[dim] command line[/]"
            : $" [dim]click here to write · [/]{Key("F1")}[dim] full screen[/]";
        if (_width > 0 && VisibleLength(footer) > _width)
        {
            footer = _armed
                ? $" {Key("⌃S")}[dim] send · [/]{Key("⌥L")}[dim] literal · [/]{Key("F1")}[dim] full · [/]{Key("Esc")}[dim] back[/]"
                : $" [dim]click to write · [/]{Key("F1")}[dim] full[/]";
        }

        _footer.SetContent(new List<string> { _width > 0 ? ElideVisible(footer, _width) : footer });
    }

    private string Key(string key) => $"[{_accent}]{key}[/]";
}
