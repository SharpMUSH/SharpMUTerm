namespace SharpMUTerm.Client.Modals;

/// <summary>What the welcome's menu offers, in the order it lists them.</summary>
public enum WelcomeChoice
{
    /// <summary>The small form: host, port, TLS, a character name.</summary>
    Connect,

    /// <summary>The package index browser, fetched only now.</summary>
    Browse,

    /// <summary>F5, to add a world by hand.</summary>
    Settings,
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
/// surface (<c>WelcomeSurface.Render</c>) and by the window's native one.
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
    string? IndexProblem)
{
    /// <summary>The example the surface names, a host nobody owns (RFC 2606).</summary>
    public const string ExampleCommand = "/connect mud.example.org 4201 tls";

    /// <summary>The longest host the form takes: a DNS name's limit.</summary>
    public const int MaxHost = 253;

    /// <summary>A character name long enough for any game's.</summary>
    public const int MaxCharacter = 40;

    /// <summary>A choice's title and the words beside it.</summary>
    public (string Title, string Detail) Describe(WelcomeChoice choice) => choice switch
    {
        WelcomeChoice.Connect => ("Connect to a host", "type a host and port"),
        WelcomeChoice.Browse => ("Browse games", IndexProblem is null ? "from the package index" : "no package index set"),
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
