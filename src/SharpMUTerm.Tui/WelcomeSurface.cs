using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Layouts;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>What a key on the welcome asks the app to do.</summary>
internal enum WelcomeAction
{
    /// <summary>Nothing beyond a repaint.</summary>
    Redraw,

    /// <summary>Dial <see cref="WelcomeSurface.Request"/>.</summary>
    Connect,

    /// <summary>Close, and open Browse worlds.</summary>
    Browse,

    /// <summary>Close, and open F5.</summary>
    Settings,

    /// <summary>Close, and start an import from another client.</summary>
    Import,
}

/// <summary>What the connect form asks for, read once ⏎ has checked it.</summary>
internal sealed record WelcomeConnect(string Host, int Port, bool Tls, string? Character, bool PasswordLater, string? WorldName = null, string? Encoding = null);

/// <summary>
/// The first-run welcome: what it says and what a key on it means, with no window in it — the
/// <see cref="QuitPrompt"/> split, so a test reads it without a terminal. <see cref="SharpMUTermApp"/> hosts it
/// in a <see cref="ListOverlay"/>, and <see cref="PlainLines"/> is the same surface said as lines for
/// <c>--plain</c>, where nothing reads the cells.
/// <para>
/// The form goes nowhere the command line cannot: ⏎ hands back a <see cref="WelcomeConnect"/> and the app runs
/// it through <c>/connect</c>'s own path, so a world the welcome adds is the world <c>/connect host port</c>
/// would have added, character and layout aside.
/// </para>
/// </summary>
internal sealed class WelcomeSurface
{
    /// <summary>The example the surface names, a host nobody owns (RFC 2606).</summary>
    internal const string ExampleCommand = WelcomeModel.ExampleCommand;

    /// <summary>The width the surface asks for; the overlay narrows it on a small terminal.</summary>
    internal const int PreferredWidth = 78;

    private const int MaxHost = WelcomeModel.MaxHost;

    private const int MaxCharacter = WelcomeModel.MaxCharacter;

    private readonly string? _indexProblem;

    /// <param name="indexProblem">Why the world directory cannot be browsed, or null when it can be asked for.</param>
    public WelcomeSurface(string? indexProblem) => _indexProblem = indexProblem;

    public WelcomeChoice Selected { get; private set; } = WelcomeChoice.Connect;

    public WelcomeLayout Layout { get; set; } = WelcomeLayout.Suggested;

    /// <summary>Whether the connect form is showing rather than the menu.</summary>
    public bool InForm { get; private set; }

    public WelcomeField Field { get; private set; } = WelcomeField.Host;

    public string Host { get; private set; } = string.Empty;

    public string Port { get; private set; } = SharpMUTermApp.DefaultConnectPort.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool Tls { get; private set; }

    public string Character { get; private set; } = string.Empty;

    public bool PasswordLater { get; private set; }

    /// <summary>What the surface says about the last thing it refused, shown in place; null when nothing.</summary>
    public string? Problem { get; private set; }

    /// <summary>The checked form, once ⏎ has accepted it.</summary>
    public WelcomeConnect? Request { get; private set; }

    /// <summary>The world Browse worlds filled the form in for, or null; dropped once the host is changed.</summary>
    public string? WorldName { get; private set; }

    /// <summary>
    /// The encoding the world will be saved with, from the charset mu*index names for the world Browse worlds picked
    /// (null: auto); dropped with <see cref="WorldName"/> once the host is changed.
    /// </summary>
    public string? Encoding { get; private set; }

    /// <summary>The host Browse worlds filled in, so a changed host drops the name that went with it.</summary>
    private string? _prefilledHost;

    /// <summary>Everything the surface says now: what the cells draw and the window's native welcome draws.</summary>
    public WelcomeModel Model =>
        new(InForm, Selected, Layout, Field, Host, Port, Tls, Character, PasswordLater, Problem, _indexProblem, WorldName, Encoding);

    /// <summary>
    /// The form, filled in from a world Browse worlds picked: its host, port and TLS, and its name for the world it will
    /// be saved as. Only fills it in — the player's own ⏎ there is what saves and dials, through <c>/connect</c>'s path.
    /// </summary>
    public void Prefill(string worldName, string host, int port, bool tls, string? encoding = null)
    {
        InForm = true;
        Field = WelcomeField.Character;
        Host = new string(host.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).Take(MaxHost).ToArray());
        Port = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Tls = tls;
        WorldName = worldName;
        Encoding = encoding;
        _prefilledHost = Host;
        Problem = null;
        Request = null;
    }

    /// <summary>
    /// A menu choice made outright — the native welcome's click, or ⏎ on the cell menu once the pointer is on it. The
    /// cell menu's own rules: Connect opens the form, Browse with no index says so in place.
    /// </summary>
    public WelcomeAction Choose(WelcomeChoice choice)
    {
        Selected = choice;
        Problem = null;
        return MenuKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    /// <summary>
    /// The native form's Connect: the fields as typed, held to the cell form's limits, then checked as ⏎ checks them.
    /// </summary>
    public WelcomeAction Submit(WelcomeForm form, Func<IReadOnlyList<string>, (string Host, int Port)?> parse)
    {
        ArgumentNullException.ThrowIfNull(form);
        InForm = true;
        Host = new string(form.Host.Trim().Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c)).Take(MaxHost).ToArray());
        Port = new string(form.Port.Trim().Where(char.IsAsciiDigit).Take(5).ToArray());
        Tls = form.Tls;
        Character = new string(form.Character.Where(c => !char.IsControl(c)).Take(MaxCharacter).ToArray()).TrimStart();
        PasswordLater = form.PasswordLater;
        Problem = null;
        return Submit(parse);
    }

    /// <summary>The built-in layout id <see cref="Layout"/> names, or null for the suggestion.</summary>
    public string? LayoutId => LayoutIdOf(Layout);

    internal static string? LayoutIdOf(WelcomeLayout layout) => layout switch
    {
        WelcomeLayout.Roleplay => BuiltInLayouts.RoleplayId,
        WelcomeLayout.Combat => BuiltInLayouts.CombatId,
        _ => null,
    };

    /// <summary>Reads a layout word as <c>/welcome layout</c> takes it.</summary>
    internal static WelcomeLayout? ParseLayout(string word) => word.Trim().ToLowerInvariant() switch
    {
        "suggested" or "suggest" or "auto" => WelcomeLayout.Suggested,
        "roleplay" or "rp" => WelcomeLayout.Roleplay,
        "combat" => WelcomeLayout.Combat,
        _ => null,
    };

    /// <summary>Puts the menu's cursor on <paramref name="choice"/>: the row the player is coming back from.</summary>
    public void Highlight(WelcomeChoice choice)
    {
        Selected = choice;
        Problem = null;
    }

    /// <summary>Back to the menu, as it opens — for <c>/welcome</c> reopening a surface that was left in the form.</summary>
    public void ShowMenu()
    {
        InForm = false;
        Problem = null;
    }

    /// <summary>
    /// Esc on a form a pick in the window's own find card filled in: the pick is given back — host, port, TLS, the name and
    /// encoding that came with it, and what was typed since — and the page is the list beside it again, on its Browse row.
    /// The form keeps nothing, as the cells' Esc back to Browse worlds keeps nothing. False when no pick filled it in.
    /// </summary>
    public bool ForgetPick()
    {
        if (WorldName is null)
        {
            return false;
        }

        InForm = false;
        Selected = WelcomeChoice.Browse;
        Field = WelcomeField.Host;
        Host = string.Empty;
        Port = SharpMUTermApp.DefaultConnectPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Tls = false;
        Character = string.Empty;
        PasswordLater = false;
        WorldName = null;
        Encoding = null;
        _prefilledHost = null;
        Problem = null;
        Request = null;
        return true;
    }

    /// <summary>Esc: the form goes back to the menu and stays open; on the menu Esc closes, which is the overlay's.</summary>
    /// <returns>Whether Esc was taken here.</returns>
    public bool Back()
    {
        if (!InForm)
        {
            return false;
        }

        ShowMenu();
        return true;
    }

    /// <summary>What a key does. <paramref name="parse"/> reads a host and port the way <c>/connect</c> does.</summary>
    public WelcomeAction Interpret(ConsoleKeyInfo key, Func<IReadOnlyList<string>, (string Host, int Port)?> parse)
    {
        ArgumentNullException.ThrowIfNull(parse);
        return InForm ? FormKey(key, parse) : MenuKey(key);
    }

    private WelcomeAction MenuKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Selected = (WelcomeChoice)Math.Max(0, (int)Selected - 1);
                Problem = null;
                return WelcomeAction.Redraw;
            case ConsoleKey.DownArrow:
                Selected = (WelcomeChoice)Math.Min((int)WelcomeChoice.Import, (int)Selected + 1);
                Problem = null;
                return WelcomeAction.Redraw;
            case ConsoleKey.LeftArrow:
                Layout = (WelcomeLayout)(((int)Layout + 2) % 3);
                return WelcomeAction.Redraw;
            case ConsoleKey.RightArrow:
                Layout = (WelcomeLayout)(((int)Layout + 1) % 3);
                return WelcomeAction.Redraw;
            case ConsoleKey.Enter:
                switch (Selected)
                {
                    case WelcomeChoice.Connect:
                        InForm = true;
                        Field = WelcomeField.Host;
                        Problem = null;
                        return WelcomeAction.Redraw;
                    case WelcomeChoice.Browse when _indexProblem is not null:
                        Problem = _indexProblem;
                        return WelcomeAction.Redraw;
                    case WelcomeChoice.Browse:
                        return WelcomeAction.Browse;
                    case WelcomeChoice.Import:
                        return WelcomeAction.Import;
                    default:
                        return WelcomeAction.Settings;
                }

            default:
                return WelcomeAction.Redraw;
        }
    }

    private WelcomeAction FormKey(ConsoleKeyInfo key, Func<IReadOnlyList<string>, (string Host, int Port)?> parse)
    {
        var shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
            case ConsoleKey.Tab when shift:
                Field = (WelcomeField)Math.Max(0, (int)Field - 1);
                return WelcomeAction.Redraw;
            case ConsoleKey.DownArrow:
            case ConsoleKey.Tab:
                Field = (WelcomeField)Math.Min((int)WelcomeField.Password, (int)Field + 1);
                return WelcomeAction.Redraw;
            case ConsoleKey.Enter:
                return Submit(parse);
            case ConsoleKey.Backspace:
                Edit(text => text.Length == 0 ? text : text[..^1]);
                return WelcomeAction.Redraw;
            case ConsoleKey.Spacebar when Field is WelcomeField.Tls or WelcomeField.Password:
            case ConsoleKey.LeftArrow when Field is WelcomeField.Tls or WelcomeField.Password:
            case ConsoleKey.RightArrow when Field is WelcomeField.Tls or WelcomeField.Password:
                if (Field == WelcomeField.Tls)
                {
                    Tls = !Tls;
                }
                else
                {
                    PasswordLater = !PasswordLater;
                }

                return WelcomeAction.Redraw;
        }

        // Typing, with the modifiers a printable key can carry and no others: a chord is not text.
        var c = key.KeyChar;
        if (char.IsControl(c) || (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
        {
            return WelcomeAction.Redraw;
        }

        switch (Field)
        {
            case WelcomeField.Host when !char.IsWhiteSpace(c) && Host.Length < MaxHost:
                Host += c;
                break;
            case WelcomeField.Port when char.IsAsciiDigit(c) && Port.Length < 5:
                Port += c;
                break;
            case WelcomeField.Character when Character.Length < MaxCharacter && !(c == ' ' && Character.Length == 0):
                Character += c;
                break;
        }

        Problem = null;
        return WelcomeAction.Redraw;
    }

    private void Edit(Func<string, string> change)
    {
        switch (Field)
        {
            case WelcomeField.Host:
                Host = change(Host);
                break;
            case WelcomeField.Port:
                Port = change(Port);
                break;
            case WelcomeField.Character:
                Character = change(Character);
                break;
        }

        Problem = null;
    }

    private WelcomeAction Submit(Func<IReadOnlyList<string>, (string Host, int Port)?> parse)
    {
        if (Host.Length == 0)
        {
            Field = WelcomeField.Host;
            Problem = "type the game's host first, for example mud.example.org";
            return WelcomeAction.Redraw;
        }

        if (parse(Port.Length == 0 ? [Host] : [Host, Port]) is not { } endpoint)
        {
            Problem = Port.Length > 0 && !(int.TryParse(Port, out var port) && port is > 0 and <= 65535)
                ? "the port is a number from 1 to 65535"
                : "that is not a host name or address";
            Field = Problem.StartsWith("the port", StringComparison.Ordinal) ? WelcomeField.Port : WelcomeField.Host;
            return WelcomeAction.Redraw;
        }

        var character = Character.Trim();
        if (WorldName is not null && !string.Equals(Host, _prefilledHost, StringComparison.OrdinalIgnoreCase))
        {
            WorldName = null;
            Encoding = null;
        }

        Request = new WelcomeConnect(endpoint.Host, endpoint.Port, Tls, character.Length == 0 ? null : character, PasswordLater, WorldName, Encoding);
        return WelcomeAction.Connect;
    }

    // ---- What it says ---------------------------------------------------------------------------

    /// <summary>The plane the welcome is painted on: the settings screens' panel, the mockup's card.</summary>
    internal const string Plane = ScreenPalette.PanelBg;

    /// <summary>The body's indent inside the frame; the heading and key bands run edge to edge.</summary>
    private const int Indent = 2;

    private static string Label(string text) => $"[{ScreenPalette.Label}]{Escape(text)}[/]";

    private static string Muted(string text) => $"[{ScreenPalette.Muted}]{Escape(text)}[/]";

    private static string Value(string text, bool bold = false) =>
        $"[{(bold ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(text)}[/]";

    private static string Pad(string markup, int width) =>
        markup + new string(' ', Math.Max(0, width - VisibleLength(markup)));

    /// <summary>A body line, indented from the frame.</summary>
    private static string Body(string markup) => markup.Length == 0 ? markup : new string(' ', Indent) + markup;

    /// <summary>
    /// The most lines the surface may take, or <see cref="int.MaxValue"/> for no limit. The app sets it from the desktop's
    /// height so the window, centred, leaves the command line and the status row in view (at 60×20 it covered both).
    /// </summary>
    public int MaxRows { get; set; } = int.MaxValue;

    /// <summary>
    /// The surface at a width: the menu, or the form. Both are the same number of lines, so the one window holds
    /// either — the form's footer sits where the menu's does, and nothing has to be closed and reopened to switch,
    /// which a window running its own loop does not do in the order a headless one does.
    /// </summary>
    public List<string> Render(int width) => Render(Model, width, MaxRows);

    /// <summary>
    /// The surface for <paramref name="model"/> at a width — the cells of the model the window's native welcome draws. The
    /// mockup's shape: the heading on a band across the top, the body indented under it, the keys on a band across the
    /// foot, all on the settings screens' panel (<see cref="Plane"/>). <paramref name="width"/> is the frame's whole inside.
    /// Over <paramref name="rows"/> lines the spacers go first, then the <c>/connect</c> example (the status row names
    /// <c>/connect</c> too); nothing is wrapped by the frame, because every sentence is broken to the body's width here.
    /// </summary>
    public static List<string> Render(WelcomeModel model, int width, int rows = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(model);
        var menu = Fit(RenderMenu(model, width), rows);
        if (!model.InForm)
        {
            return menu;
        }

        var form = Fit(RenderForm(model, width), menu.Count);
        var footer = form[^1];
        form.RemoveAt(form.Count - 1);
        while (form.Count < menu.Count - 1)
        {
            form.Add(string.Empty);
        }

        form.Add(footer);
        return form;
    }

    /// <summary>A line of the surface and when it is given up: 0 never, else highest first.</summary>
    private readonly record struct Line(string Markup, int Drop = 0);

    /// <summary>Spacer rows, given up first, in this order (highest first).</summary>
    private const int DropLayoutSpacer = 6, DropExample = 6, DropHeadingSpacer = 5, DropIntroSpacer = 4, DropKeysSpacer = 3, DropProblemSpacer = 2;

    /// <summary><paramref name="lines"/> with the highest <see cref="Line.Drop"/> given up first until at most <paramref name="rows"/> remain.</summary>
    private static List<string> Fit(List<Line> lines, int rows)
    {
        var kept = lines.ToList();
        foreach (var drop in kept.Select(l => l.Drop).Where(d => d > 0).Distinct().OrderDescending().ToList())
        {
            if (kept.Count <= rows)
            {
                break;
            }

            kept.RemoveAll(l => l.Drop == drop);
        }

        return kept.Select(l => l.Markup).ToList();
    }

    /// <summary><paramref name="text"/> broken to <paramref name="width"/>, each row a body line in <paramref name="style"/>.</summary>
    private static IEnumerable<Line> Sentence(string text, int width, Func<string, string> style) =>
        HelpSurface.Wrap(text, Math.Max(10, width)).Select(row => new Line(Body(style(row))));

    private static List<Line> RenderMenu(WelcomeModel model, int width)
    {
        var inner = width - (2 * Indent);
        var lines = new List<Line> { new(Band(WelcomeModel.Heading, width)), new(string.Empty, DropHeadingSpacer) };
        lines.AddRange(Sentence(WelcomeModel.Intro, inner, t => Value(t)));
        lines.Add(new(string.Empty, DropIntroSpacer));

        foreach (var choice in Enum.GetValues<WelcomeChoice>())
        {
            var selected = choice == model.Selected;
            var (title, detail) = model.Describe(choice);
            var marker = selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";

            // The detail is elided to the row rather than wrapped by the frame (60 columns): it says where a choice goes,
            // and the title already does.
            var room = inner - 23;
            var shown = detail.Length <= room ? detail : room > 1 ? detail[..(room - 1)].TrimEnd() + "…" : string.Empty;
            var row = $"{marker} {Pad(Value(title, selected), 20)} {Muted(shown)}";

            // The choice ⏎ takes is a band across the list, the settings screens' cursor row (the mockup's).
            lines.Add(new(Body(selected ? $"[on {ScreenPalette.CursorBg}]{Pad(row, inner)}[/]" : row)));
        }

        if (model.Problem is null)
        {
            lines.Add(new(string.Empty, DropProblemSpacer));
        }
        else
        {
            lines.AddRange(HelpSurface.Wrap(model.Problem, Math.Max(10, inner - 2))
                .Select(row => new Line(Body($"  [{ScreenPalette.Caution}]{Escape(row)}[/]"))));
        }

        lines.Add(new(Body(Muted(WelcomeModel.ExampleLead)), DropExample));
        lines.Add(new(Body($"  [{ScreenPalette.Accent}]{Escape(ExampleCommand)}[/]"), DropExample));
        lines.Add(new(string.Empty, DropLayoutSpacer));
        lines.AddRange(Sentence(model.LayoutHeading, inner, Label));
        var chips = string.Join("  ", Enum.GetValues<WelcomeLayout>().Select(layout =>
        {
            var name = WelcomeModel.LayoutName(layout);
            return layout == model.Layout
                ? $"[bold {ScreenPalette.Ink} on {ScreenPalette.Accent}] {Escape(name)} [/]"
                : $"[{ScreenPalette.Value}] {Escape(name)} [/]";
        }));

        // The note goes beside the chips where the row has room for both (the mockup's), else on the row under them.
        var note = Muted(WelcomeModel.LayoutNote);
        var beside = "  " + chips + "   " + note;
        if (VisibleLength(beside) <= inner)
        {
            lines.Add(new(Body(beside)));
        }
        else
        {
            lines.Add(new(Body("  " + chips)));
            lines.AddRange(HelpSurface.Wrap(WelcomeModel.LayoutNote, Math.Max(10, inner - 2)).Select(row => new Line(Body("  " + Muted(row)))));
        }

        lines.Add(new(string.Empty, DropKeysSpacer));
        lines.Add(new(Keys(width, WelcomeModel.MenuKeys.Left, WelcomeModel.MenuKeys.Right)));
        return lines;
    }

    private static List<Line> RenderForm(WelcomeModel model, int width)
    {
        // The intro is broken to the body's width: a world Browse worlds named makes it a long sentence, which ran past
        // the frame's right edge at 80 columns.
        var lines = new List<Line> { new(Band(WelcomeModel.FormHeading, width)), new(string.Empty, DropHeadingSpacer) };
        lines.AddRange(Sentence(model.FormIntroLine, width - (2 * Indent), t => Value(t)));
        lines.Add(new(string.Empty, DropIntroSpacer));

        var boxWidth = Math.Clamp(width - (2 * Indent) - 16, 12, 44);
        var field = model.Field;
        lines.Add(new(Body(Row(field, WelcomeField.Host, Box(model.Host, boxWidth, field == WelcomeField.Host, WelcomeModel.HostPlaceholder)))));
        lines.Add(new(Body(Row(field, WelcomeField.Port, Box(model.Port, 7, field == WelcomeField.Port, null)))));
        lines.Add(new(Body(Row(field, WelcomeField.Tls, Toggle(model.Tls, WelcomeModel.TlsOn, WelcomeModel.TlsOff)))));
        lines.Add(new(Body(Row(field, WelcomeField.Character, Box(model.Character, boxWidth, field == WelcomeField.Character, WelcomeModel.CharacterPlaceholder)))));
        lines.Add(new(Body(Row(field, WelcomeField.Password, Toggle(model.PasswordLater, WelcomeModel.PasswordOn, WelcomeModel.PasswordOff)))));
        lines.Add(new(string.Empty, DropProblemSpacer));
        if (model.Problem is null)
        {
            lines.AddRange(Sentence(model.FormLayoutLine, width - (2 * Indent), Muted));
        }
        else
        {
            lines.AddRange(Sentence(model.Problem, width - (2 * Indent), t => $"[{ScreenPalette.Caution}]{Escape(t)}[/]"));
        }

        lines.Add(new(string.Empty, DropKeysSpacer));
        lines.Add(new(Keys(width, "↑↓ field · Space toggle", WelcomeModel.FormKeys)));
        return lines;
    }

    private static string Row(WelcomeField current, WelcomeField field, string value)
    {
        var label = WelcomeModel.FieldLabel(field);
        var selected = current == field;
        var marker = selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
        return $"{marker} {Pad(selected ? Value(label, bold: true) : Label(label), 11)} {value}";
    }

    private static string Box(string text, int width, bool editing, string? placeholder)
    {
        var shown = text.Length > width - 1 ? "…" + text[^(width - 2)..] : text;
        var body = shown.Length == 0 && !editing && placeholder is not null
            ? $"[{ScreenPalette.Muted} on {ScreenPalette.FieldBg}]{Escape(placeholder.Length > width ? placeholder[..width] : placeholder)}[/]"
            : $"[{ScreenPalette.Value} on {ScreenPalette.FieldBg}]{Escape(shown)}[/]";
        var caret = editing ? $"[{ScreenPalette.Ink} on {ScreenPalette.Accent}] [/]" : string.Empty;
        var used = VisibleLength(body) + (editing ? 1 : 0);
        return body + caret + $"[on {ScreenPalette.FieldBg}]{new string(' ', Math.Max(0, width - used))}[/]";
    }

    private static string Toggle(bool on, string yes, string no) =>
        on ? $"[{ScreenPalette.Accent}]●[/] {Value(yes)}" : $"[{ScreenPalette.Label}]○[/] {Value(no)}";

    /// <summary>The heading on a band across the whole inside, in bold, a cell in from the frame.</summary>
    private static string Band(string text, int width) =>
        $"[on {ScreenPalette.HeaderBg}]{Pad($" {Value(text, bold: true)}", width)}[/]";

    /// <summary>
    /// The keys on a band across the foot: what moves on the left, what acts on the right, each key in bold and its word
    /// quieter, a cell in from either edge. Below the width for both, the left side goes: the right holds the way out.
    /// </summary>
    private static string Keys(int width, string left, string right)
    {
        var l = KeyRun(left);
        var r = KeyRun(right);
        var room = width - VisibleLength(l) - VisibleLength(r) - 2;
        var row = room >= 2 ? " " + l + new string(' ', room) + r : " " + r;
        return $"[on {ScreenPalette.FooterBg}]{Pad(row, width)}[/]";
    }

    /// <summary><c>⏎ choose · Esc closes</c> with each key (the first word of a part) bold and the rest of the part quieter.</summary>
    private static string KeyRun(string keys) => string.Join(Muted(" · "), keys.Split(" · ").Select(part =>
    {
        var space = part.IndexOf(' ', StringComparison.Ordinal);
        return space < 0
            ? Value(part, bold: true)
            : Value(part[..space], bold: true) + Muted(part[space..]);
    }));

    /// <summary>
    /// The welcome said as lines, for <c>--plain</c>: every choice is a command a reader can type, because
    /// there is no menu to arrow through.
    /// </summary>
    public static IReadOnlyList<string> PlainLines(WelcomeLayout layout, string? indexProblem) =>
    [
        "Welcome to SharpMUTerm. No worlds yet.",
        "To connect, type /connect, a host and a port, and tls for an encrypted connection: " + ExampleCommand,
        indexProblem is null
            ? "To browse worlds listed on mu*index, type /browse, or /browse and words to search for"
            : "Browsing worlds is not available: " + indexProblem,
        "To import worlds and automation from MUSHclient, Mudlet or TinTin++, type /import and the path to the file",
        "Starting layout: " + (layout == WelcomeLayout.Suggested ? "suggested from what the game sends" : layout.ToString())
            + ". Type /welcome layout roleplay, combat or suggested to change it.",
        "Type /welcome to hear this again.",
    ];
}
