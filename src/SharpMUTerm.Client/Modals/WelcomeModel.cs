namespace SharpMUTerm.Client.Modals;

/// <summary>What the welcome's menu offers, in the order it lists them.</summary>
public enum WelcomeChoice
{
    /// <summary>The small form: host, port, TLS, a character name.</summary>
    Connect,

    /// <summary>The world directory (mu*index), fetched only now.</summary>
    Browse,

    /// <summary>F5, to add a world by hand.</summary>
    Settings,

    /// <summary>Import worlds and automation from another client.</summary>
    Import,
}

/// <summary>
/// The starting layout a first world is given. <see cref="Suggested"/> sets nothing, so the client's own suggestion
/// still applies when it first connects; the other two are the player's choice outright and are written to
/// <c>WorldDefinition.Layout</c>.
/// </summary>
public enum WelcomeLayout
{
    Suggested,
    Roleplay,
    Combat,
}

/// <summary>The connect form's rows, top to bottom.</summary>
public enum WelcomeField
{
    Host,
    Port,
    Tls,
    Character,
    Password,
}

/// <summary>
/// The first-run welcome's model: the menu or the connect form, with everything either says. Drawn by the cell
/// surface (<c>WelcomeSurface.Render</c>) and by the window's native one. <c>IndexProblem</c> is why Browse worlds cannot
/// ask the world directory (null when it can); <c>WorldName</c> is the world Browse worlds filled the form in for.
/// </summary>
public sealed record WelcomeModel(
    bool InForm,
    WelcomeChoice Selected,
    WelcomeLayout Layout,
    WelcomeField Field,
    string Host,
    string Port,
    bool Tls,
    string Character,
    bool PasswordLater,
    string? Problem,
    string? IndexProblem,
    string? WorldName = null)
{
    /// <summary>The example the surface names, a host nobody owns (RFC 2606).</summary>
    public const string ExampleCommand = "/connect mud.example.org 4201 tls";

    /// <summary>The longest host the form takes: a DNS name's limit.</summary>
    public const int MaxHost = 253;

    /// <summary>A character name long enough for any game's.</summary>
    public const int MaxCharacter = 40;

    // The welcome's words, said once here so both hosts say the same thing in the same case. One noun, "world":
    // it is what F5, the rail and every other surface call a place you connect to.

    /// <summary>The welcome's heading.</summary>
    public const string Heading = "Welcome to SharpMUTerm";

    /// <summary>The line under the heading on the menu.</summary>
    public const string Intro = "No worlds yet. Pick a way in — you can add more at any time.";

    /// <summary>What leads the example command.</summary>
    public const string ExampleLead = "Or type it on the command line:";

    /// <summary>The line under the layout chips.</summary>
    public const string LayoutNote = "Saved per world · ⌥M changes it later";

    /// <summary>The form's heading.</summary>
    public const string FormHeading = "Connect to a world";

    /// <summary>The line under the form's heading.</summary>
    public const string FormIntro = "Saved as a world, so you can come back to it.";

    /// <summary>
    /// The line under the form's heading: <see cref="FormIntro"/>, or, when Browse worlds filled the form in, which world
    /// it is and that the address came from the directory.
    /// </summary>
    public string FormIntroLine => WorldName is null
        ? FormIntro
        : $"{WorldName}, as listed on mu*index. Check it and press ⏎ to save and connect.";

    /// <summary>The TLS row's words when on; the window's check box carries these words.</summary>
    public const string TlsOn = "On, encrypted";

    /// <summary>The TLS row's words when off.</summary>
    public const string TlsOff = "Off";

    /// <summary>The password row's words when on (no password now); the window's check box carries these words.</summary>
    public const string PasswordOn = "Set it on F5 after connecting";

    /// <summary>The password row's words when off.</summary>
    public const string PasswordOff = "Type it at the login prompt";

    /// <summary>The host box's placeholder.</summary>
    public const string HostPlaceholder = "mud.example.org";

    /// <summary>The character box's placeholder.</summary>
    public const string CharacterPlaceholder = "Optional, the name you log in as";

    /// <summary>The menu's keys: what moves (left) and what acts (right). The window joins the two.</summary>
    public static (string Left, string Right) MenuKeys => ("↑↓ pick · ←→ layout", "⏎ choose · Esc closes");

    /// <summary>The form's keys that act, in both hosts (how a host moves between fields is its own).</summary>
    public const string FormKeys = "⏎ connect · Esc back";

    /// <summary>A form row's label, as both hosts write it.</summary>
    public static string FieldLabel(WelcomeField field) => field switch
    {
        WelcomeField.Host => "Host",
        WelcomeField.Port => "Port",
        WelcomeField.Tls => "TLS",
        WelcomeField.Character => "Character",
        _ => "Password",
    };

    /// <summary>A choice's title and the words beside it.</summary>
    public (string Title, string Detail) Describe(WelcomeChoice choice) => choice switch
    {
        WelcomeChoice.Connect => ("Connect to a world", "Type a host and port"),
        WelcomeChoice.Browse => ("Browse worlds", IndexProblem is null ? "From the mu*index directory" : "No world directory set"),
        WelcomeChoice.Import => ("Import", "From MUSHclient, Mudlet or TinTin++"),
        _ => ("Open settings", "F5, add a world by hand"),
    };

    /// <summary>A layout's name as the chips say it.</summary>
    public static string LayoutName(WelcomeLayout layout) => layout == WelcomeLayout.Suggested ? "suggested" : layout.ToString();

    /// <summary>The line over the layout chips.</summary>
    public string LayoutHeading => Layout == WelcomeLayout.Suggested
        ? "Starting layout, picked from what the game sends:"
        : "Starting layout, whatever the game sends:";

    /// <summary>What the form says about the layout under its fields.</summary>
    public string FormLayoutLine => Layout == WelcomeLayout.Suggested
        ? "Layout: suggested from what the game sends."
        : $"Layout: {Layout}.";
}

/// <summary>
/// One opening of the welcome in a host's own controls. The host shows the menu and the form; a choice, a layout and
/// the filled form come back through here, and the client runs each through the cell surface's own rules — the form
/// through <c>/connect</c>'s path, so the window can add nothing the command line cannot. A refusal comes back as a
/// republished model with <see cref="WelcomeModel.Problem"/> set, and the welcome stays open.
/// </summary>
public sealed class WelcomeSession : ModalSession<WelcomeModel>
{
    private readonly Action<WelcomeChoice> _choose;
    private readonly Action<WelcomeLayout> _layout;
    private readonly Action<WelcomeForm> _connect;
    private readonly Action _back;

    public WelcomeSession(
        WelcomeModel model,
        bool isDark,
        Action<WelcomeChoice> choose,
        Action<WelcomeLayout> layout,
        Action<WelcomeForm> connect,
        Action back,
        Action closed)
        : base(model, isDark, closed)
    {
        ArgumentNullException.ThrowIfNull(choose);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(connect);
        ArgumentNullException.ThrowIfNull(back);
        _back = back;
        _choose = choose;
        _layout = layout;
        _connect = connect;
    }

    public override string Surface => "welcome";

    /// <summary>The host's: a menu choice. Connect opens the form; the other two close the welcome and open what they name.</summary>
    public void Choose(WelcomeChoice choice)
    {
        if (!IsEnded)
        {
            _choose(choice);
        }
    }

    /// <summary>The host's: a layout chip.</summary>
    public void SetLayout(WelcomeLayout layout)
    {
        if (!IsEnded)
        {
            _layout(layout);
        }
    }

    /// <summary>The host's: Esc on the form, which goes back to the menu and keeps the welcome open.</summary>
    public void Back()
    {
        if (!IsEnded)
        {
            _back();
        }
    }

    /// <summary>The host's: the form's Connect, with what it holds. The client checks it; a refusal keeps the welcome open.</summary>
    public void Connect(WelcomeForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!IsEnded)
        {
            _connect(form);
        }
    }
}

/// <summary>What the connect form holds when Connect is pressed, as typed.</summary>
public sealed record WelcomeForm(string Host, string Port, bool Tls, string Character, bool PasswordLater);
