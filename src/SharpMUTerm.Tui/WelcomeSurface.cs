using SharpMUTerm.Core.Layouts;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>What the welcome's menu offers, in the order it lists them.</summary>
internal enum WelcomeChoice
{
    /// <summary>The small form: host, port, TLS, a character name.</summary>
    Connect,

    /// <summary>The package index browser, fetched only now.</summary>
    Browse,

    /// <summary>F5, to add a world by hand.</summary>
    Settings,
}

/// <summary>
/// The starting layout a first world is given. <see cref="Suggested"/> sets nothing, so the client's own
/// suggestion (<see cref="LayoutSuggester"/>) still applies when it first connects; the other two are the
/// player's choice outright and are written to <c>WorldDefinition.Layout</c>.
/// </summary>
internal enum WelcomeLayout
{
    Suggested,
    Roleplay,
    Combat,
}

/// <summary>The connect form's rows, top to bottom.</summary>
internal enum WelcomeField
{
    Host,
    Port,
    Tls,
    Character,
    Password,
}

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
    internal const string ExampleCommand = "/connect mud.example.org 4201 tls";

    /// <summary>The width the surface asks for; the overlay narrows it on a small terminal.</summary>
    internal const int PreferredWidth = 66;

    /// <summary>The longest host the form takes: a DNS name's limit.</summary>
    private const int MaxHost = 253;

    /// <summary>A character name long enough for any game's.</summary>
    private const int MaxCharacter = 40;

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
    public List<string> Render(int width)
    {
        if (!InForm)
        {
            return RenderMenu(width);
        }

        var form = RenderForm(width);
        var footer = form[^1];
        form.RemoveAt(form.Count - 1);
        var height = RenderMenu(width).Count;
        while (form.Count < height - 1)
        {
            form.Add(string.Empty);
        }

        form.Add(footer);
        return form;
    }

    private List<string> RenderMenu(int width)
    {
        var lines = new List<string>
        {
            Accent("Welcome to SharpMUTerm"),
            Label("No games yet. Pick one of these to start."),
            string.Empty,
        };

        foreach (var choice in Enum.GetValues<WelcomeChoice>())
        {
            var selected = choice == Selected;
            var (title, detail) = choice switch
            {
                WelcomeChoice.Connect => ("Connect to a host", "type a host and port"),
                WelcomeChoice.Browse => ("Browse games", _indexProblem is null ? "from the package index" : "no package index set"),
                _ => ("Open settings", "F5, add a world by hand"),
            };
            var marker = selected ? $"[bold {ScreenPalette.Accent}]▸[/]" : " ";
            lines.Add($"{marker} {Pad(Value(title, selected), 20)} {Label(detail)}");
        }

        lines.Add(Problem is null ? string.Empty : $"  [{ScreenPalette.Caution}]{Escape(Problem)}[/]");
        lines.Add(Label("Or type it on the command line below:"));
        lines.Add("  " + Value(ExampleCommand));
        lines.Add(string.Empty);
        lines.Add(Label(Layout == WelcomeLayout.Suggested
            ? "Starting layout, picked from what the game sends:"
            : "Starting layout, whatever the game sends:"));
        var chips = string.Join("  ", Enum.GetValues<WelcomeLayout>().Select(layout =>
        {
            var name = layout == WelcomeLayout.Suggested ? "suggested" : layout.ToString();
            return layout == Layout
                ? $"[bold {ScreenPalette.Ink} on {ScreenPalette.Accent}] {Escape(name)} [/]"
                : $"[{ScreenPalette.Value}] {Escape(name)} [/]";
        }));
        lines.Add("  " + chips);
        lines.Add("  " + Label("saved per world · ⌥M changes it later"));
        lines.Add(string.Empty);
        lines.Add(Footer(width, "↑↓ select · ←→ layout", "Esc close · ⏎ choose"));
        return lines;
    }

    private List<string> RenderForm(int width)
    {
        var lines = new List<string>
        {
            Accent("Connect to a host"),
            Label("Saved as a world, so you can come back to it."),
            string.Empty,
        };

        var boxWidth = Math.Clamp(width - 16, 12, 44);
        lines.Add(Row(WelcomeField.Host, "host", Box(Host, boxWidth, Field == WelcomeField.Host, "mud.example.org")));
        lines.Add(Row(WelcomeField.Port, "port", Box(Port, 7, Field == WelcomeField.Port, null)));
        lines.Add(Row(WelcomeField.Tls, "TLS", Toggle(Tls, "on, encrypted", "off")));
        lines.Add(Row(WelcomeField.Character, "character", Box(Character, boxWidth, Field == WelcomeField.Character, "optional, the name you log in as")));
        lines.Add(Row(WelcomeField.Password, "password", Toggle(PasswordLater, "save it on F5 after connecting", "type it at the login prompt")));
        lines.Add(string.Empty);
        lines.Add(Problem is null
            ? Label(Layout == WelcomeLayout.Suggested
                ? "Layout: suggested from what the game sends."
                : $"Layout: {Layout}.")
            : $"[{ScreenPalette.Caution}]{Escape(Problem)}[/]");
        lines.Add(string.Empty);
        lines.Add(Footer(width, "↑↓ field · Space toggle", "Esc back · ⏎ connect"));
        return lines;
    }

    private string Row(WelcomeField field, string label, string value)
    {
        var selected = Field == field;
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
        "Welcome to SharpMUTerm. No games yet.",
        "To connect, type /connect, a host and a port, and tls for an encrypted connection: " + ExampleCommand,
        indexProblem is null
            ? "To browse games from the package index, type /package browse"
            : "Browsing games is not available: " + indexProblem,
        "Starting layout: " + (layout == WelcomeLayout.Suggested ? "suggested from what the game sends" : layout.ToString())
            + ". Type /welcome layout roleplay, combat or suggested to change it.",
        "Type /welcome to hear this again.",
    ];
}
