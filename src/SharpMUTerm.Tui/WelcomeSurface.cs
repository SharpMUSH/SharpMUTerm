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

    /// <summary>Close, and open the package index.</summary>
    Browse,

    /// <summary>Close, and open F5.</summary>
    Settings,
}

/// <summary>What the connect form asks for, read once ⏎ has checked it.</summary>
internal sealed record WelcomeConnect(string Host, int Port, bool Tls, string? Character, bool PasswordLater);

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
    internal const int PreferredWidth = 66;

    private const int MaxHost = WelcomeModel.MaxHost;

    private const int MaxCharacter = WelcomeModel.MaxCharacter;

    private readonly string? _indexProblem;

    /// <param name="indexProblem">Why the package index cannot be browsed, or null when it can be asked for.</param>
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

    /// <summary>Everything the surface says now: what the cells draw and the window's native welcome draws.</summary>
    public WelcomeModel Model =>
        new(InForm, Selected, Layout, Field, Host, Port, Tls, Character, PasswordLater, Problem, _indexProblem);

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

    /// <summary>Back to the menu, as it opens — for <c>/welcome</c> reopening a surface that was left in the form.</summary>
    public void ShowMenu()
    {
        InForm = false;
        Problem = null;
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
                Selected = (WelcomeChoice)Math.Min((int)WelcomeChoice.Settings, (int)Selected + 1);
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
        Request = new WelcomeConnect(endpoint.Host, endpoint.Port, Tls, character.Length == 0 ? null : character, PasswordLater);
        return WelcomeAction.Connect;
    }

    // ---- What it says ---------------------------------------------------------------------------

    private static string Accent(string text) => $"[bold {ScreenPalette.Accent}]{Escape(text)}[/]";

    private static string Label(string text) => $"[{ScreenPalette.Label}]{Escape(text)}[/]";

    private static string Value(string text, bool bold = false) =>
        $"[{(bold ? "bold " : string.Empty)}{ScreenPalette.Value}]{Escape(text)}[/]";

    private static string Pad(string markup, int width) =>
        markup + new string(' ', Math.Max(0, width - VisibleLength(markup)));

    /// <summary>
    /// The surface at a width: the menu, or the form. Both are the same number of lines, so the one window holds
    /// either — the form's footer sits where the menu's does, and nothing has to be closed and reopened to switch,
    /// which a window running its own loop does not do in the order a headless one does.
    /// </summary>
    public List<string> Render(int width) => Render(Model, width);

    /// <summary>The surface for <paramref name="model"/> at a width — the cells of the model the window's native welcome draws.</summary>
    public static List<string> Render(WelcomeModel model, int width)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.InForm)
        {
            return RenderMenu(model, width);
        }

        var form = RenderForm(model, width);
        var footer = form[^1];
        form.RemoveAt(form.Count - 1);
        var height = RenderMenu(model, width).Count;
        while (form.Count < height - 1)
        {
            form.Add(string.Empty);
        }

        form.Add(footer);
        return form;
    }

    private static List<string> RenderMenu(WelcomeModel model, int width)
    {
        var lines = new List<string>
        {
            Accent(WelcomeModel.Heading),
            Label(WelcomeModel.Intro),
            string.Empty,
        };

        foreach (var choice in Enum.GetValues<WelcomeChoice>())
        {
            var selected = choice == model.Selected;
            var (title, detail) = model.Describe(choice);
            var marker = selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            lines.Add($"{marker} {Pad(Value(title, selected), 20)} {Label(detail)}");
        }

        lines.Add(model.Problem is null ? string.Empty : $"  [{ScreenPalette.Caution}]{Escape(model.Problem)}[/]");
        lines.Add(Label(WelcomeModel.ExampleLead));
        lines.Add("  " + Value(ExampleCommand));
        lines.Add(string.Empty);
        lines.Add(Label(model.LayoutHeading));
        var chips = string.Join("  ", Enum.GetValues<WelcomeLayout>().Select(layout =>
        {
            var name = WelcomeModel.LayoutName(layout);
            return layout == model.Layout
                ? $"[bold {ScreenPalette.Ink} on {ScreenPalette.Accent}] {Escape(name)} [/]"
                : $"[{ScreenPalette.Value}] {Escape(name)} [/]";
        }));
        lines.Add("  " + chips);
        lines.Add("  " + Label(WelcomeModel.LayoutNote));
        lines.Add(string.Empty);
        lines.Add(Footer(width, WelcomeModel.MenuKeys.Left, WelcomeModel.MenuKeys.Right));
        return lines;
    }

    private static List<string> RenderForm(WelcomeModel model, int width)
    {
        var lines = new List<string>
        {
            Accent(WelcomeModel.FormHeading),
            Label(WelcomeModel.FormIntro),
            string.Empty,
        };

        var boxWidth = Math.Clamp(width - 16, 12, 44);
        var field = model.Field;
        lines.Add(Row(field, WelcomeField.Host, Box(model.Host, boxWidth, field == WelcomeField.Host, WelcomeModel.HostPlaceholder)));
        lines.Add(Row(field, WelcomeField.Port, Box(model.Port, 7, field == WelcomeField.Port, null)));
        lines.Add(Row(field, WelcomeField.Tls, Toggle(model.Tls, WelcomeModel.TlsOn, WelcomeModel.TlsOff)));
        lines.Add(Row(field, WelcomeField.Character, Box(model.Character, boxWidth, field == WelcomeField.Character, WelcomeModel.CharacterPlaceholder)));
        lines.Add(Row(field, WelcomeField.Password, Toggle(model.PasswordLater, WelcomeModel.PasswordOn, WelcomeModel.PasswordOff)));
        lines.Add(string.Empty);
        lines.Add(model.Problem is null
            ? Label(model.FormLayoutLine)
            : $"[{ScreenPalette.Caution}]{Escape(model.Problem)}[/]");
        lines.Add(string.Empty);
        lines.Add(Footer(width, "↑↓ field · Space toggle", WelcomeModel.FormKeys));
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

    private static string Footer(int width, string left, string right)
    {
        var gap = Math.Max(2, width - left.Length - right.Length);
        return Label(left) + new string(' ', gap) + Label(right);
    }

    /// <summary>
    /// The welcome said as lines, for <c>--plain</c>: every choice is a command a reader can type, because
    /// there is no menu to arrow through.
    /// </summary>
    public static IReadOnlyList<string> PlainLines(WelcomeLayout layout, string? indexProblem) =>
    [
        "Welcome to SharpMUTerm. No worlds yet.",
        "To connect, type /connect, a host and a port, and tls for an encrypted connection: " + ExampleCommand,
        indexProblem is null
            ? "To browse worlds from the package index, type /package browse"
            : "Browsing worlds is not available: " + indexProblem,
        "Starting layout: " + (layout == WelcomeLayout.Suggested ? "suggested from what the game sends" : layout.ToString())
            + ". Type /welcome layout roleplay, combat or suggested to change it.",
        "Type /welcome to hear this again.",
    ];
}
