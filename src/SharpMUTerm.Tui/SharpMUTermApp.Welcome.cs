using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Core.Session;
using static SharpMUTerm.Tui.MarkupText;

namespace SharpMUTerm.Tui;

/// <summary>
/// The first run: a welcome when the configuration has no worlds, and a short key tour after the first
/// connect. The welcome is a <see cref="ListOverlay"/> over <see cref="WelcomeSurface"/> where somebody sees
/// the screen, and the same choices said as lines where nobody does (<c>--plain</c>,
/// <see cref="ClientHost.ScreenIsSeen"/>). Its connect form goes through <see cref="ConnectCommand"/>, so it
/// can do nothing <c>/connect</c> cannot.
/// </summary>
internal sealed partial class SharpMUTermApp
{
    private readonly ListOverlay _welcomeView;

    /// <summary>The welcome's state while it is open, and what it was left at when it closed.</summary>
    private WelcomeSurface? _welcome;

    /// <summary>
    /// The starting layout the welcome was left at, for the run. Applied to the first world a configuration
    /// gains, however it is added — the form, or <c>/connect</c> typed under the welcome.
    /// </summary>
    private WelcomeLayout _welcomeLayout = WelcomeLayout.Suggested;

    /// <summary>Whether the configuration had no worlds when this app was made: the one state the tour is owed in.</summary>
    private readonly bool _startedEmpty;

    /// <summary>Sessions whose player asked, on the welcome's form, to be reminded to save a password.</summary>
    private readonly HashSet<string> _passwordLater = new(StringComparer.Ordinal);

    /// <summary>The words the key tour row starts with, so it can be found in a buffer again.</summary>
    internal const string KeyTourLead = "Keys:";

    /// <summary>The window holding the tour row, or null when none does.</summary>
    private string? _tourWindow;

    /// <summary>Whether the welcome surface is up, for the tests.</summary>
    internal bool WelcomeIsOpen => _welcomeView.IsOpen;

    /// <summary>What the welcome shows now, for the tests.</summary>
    internal IReadOnlyList<string> WelcomeLines => _welcomeView.Lines;

    /// <summary>The welcome's model, for a test that reads the form it drove.</summary>
    internal WelcomeSurface? Welcome => _welcome;

    /// <summary>Feeds a key to the welcome surface the way the window's own handler would.</summary>
    internal void SimulateWelcomeKey(ConsoleKeyInfo key) => _welcomeView.SimulateKey(key);

    /// <summary>Why the package index cannot be browsed, or null when it can be asked for.</summary>
    private string? PackageIndexProblem() =>
        Uri.TryCreate(_config.PackageIndexUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? null
            : string.IsNullOrWhiteSpace(_config.PackageIndexUrl)
                ? "no package index is set (packageIndexUrl in config.json)"
                : "the package index is not an https link (packageIndexUrl in config.json)";

    /// <summary>
    /// Shows the welcome: the surface where the screen is seen, the lines where it is not. Opened at start on
    /// an empty configuration and by <c>/welcome</c> or ⌃P ▸ <c>Welcome</c>; never again on its own once closed.
    /// </summary>
    internal void ShowWelcome()
    {
        var problem = PackageIndexProblem();
        if (Host.Announcer is { } announcer)
        {
            foreach (var line in WelcomeSurface.PlainLines(_welcomeLayout, problem))
            {
                announcer.Announce(line, Core.Accessibility.AnnouncementKind.Notice);
            }
        }

        if (!Host.ScreenIsSeen)
        {
            return;
        }

        // Already up: left as it is, form and all. Asking again (a second /welcome, or the startup pass a live
        // loop runs after a scene was laid out) must not throw away what the player has typed.
        if (_welcomeView.IsOpen && _welcome is not null)
        {
            return;
        }

        if (ComposerIsInTheWay("the welcome") || (AnyOverlayOpen && !_welcomeView.IsOpen && Refuse($"close {OpenOverlayName()} first")))
        {
            return;
        }

        _welcome = new WelcomeSurface(problem) { Layout = _welcomeLayout };
        OpenWelcomeView();
    }

    private void OpenWelcomeView()
    {
        var welcome = _welcome!;
        _welcomeView.Open(
            "welcome",
            "Welcome",
            welcome.Render,
            key => WelcomeKey(welcome, key),
            WelcomeSurface.PreferredWidth,
            escape: () =>
            {
                return welcome.Back();
            });
    }

    private bool WelcomeKey(WelcomeSurface welcome, ConsoleKeyInfo key)
    {
        var action = welcome.Interpret(key, words => ParseEndpoint(words));
        _welcomeLayout = welcome.Layout;
        switch (action)
        {
            case WelcomeAction.Browse:
                _welcomeView.Close();
                BrowsePackages();
                return false;

            case WelcomeAction.Settings:
                _welcomeView.Close();
                if (!ComposerIsInTheWay("a settings screen"))
                {
                    _settings.Toggle(ConsoleKey.F5, WorldsScreen);
                }

                return false;

            case WelcomeAction.Connect when welcome.Request is { } request:
                _welcomeView.Close();
                ConnectFromWelcome(request);
                return false;

            default:
                return true;
        }
    }

    /// <summary>The form's answer, through <c>/connect</c>'s own path, with the character the form named.</summary>
    private void ConnectFromWelcome(WelcomeConnect request)
    {
        var host = request.Host.Contains(':') ? $"[{request.Host}]" : request.Host;
        var arguments = $"{host} {request.Port}" + (request.Tls ? " tls" : string.Empty);
        ConnectCommand(ActiveWindowId(), arguments, world =>
        {
            ApplyWelcomeLayout(world);
            if (request.Character is { } name)
            {
                world.Characters.Add(new CharacterDefinition { Name = name });
                if (request.PasswordLater)
                {
                    _passwordLater.Add($"{world.Name}.{name}");
                }
            }
        });
    }

    /// <summary><c>/welcome</c>, and <c>/welcome layout roleplay|combat|suggested</c> for a reader with no arrows to press.</summary>
    private void WelcomeCommand(string arguments)
    {
        var words = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            ShowWelcome();
            return;
        }

        if (words.Length == 2 && words[0].Equals("layout", StringComparison.OrdinalIgnoreCase)
            && WelcomeSurface.ParseLayout(words[1]) is { } layout)
        {
            _welcomeLayout = layout;
            if (_welcome is not null)
            {
                _welcome.Layout = layout;
            }

            Notice(
                layout == WelcomeLayout.Suggested
                    ? "starting layout: suggested from what the game sends"
                    : $"starting layout: {layout}",
                MessageSeverity.Info);
            return;
        }

        RefuseCommand("/welcome, or /welcome layout roleplay, combat or suggested");
    }

    /// <summary>
    /// What a world the welcome adds starts with: the welcome's layout, when the player chose one. Left alone,
    /// nothing is set and the client's suggestion applies at the first connect.
    /// </summary>
    private void ApplyWelcomeLayout(WorldDefinition world)
    {
        if (WelcomeSurface.LayoutIdOf(_welcomeLayout) is { } layout)
        {
            world.Layout = layout;
        }
    }

    /// <summary>
    /// Marks the tour owed once a configuration that started this run empty has a world. Called on the way to
    /// every save, so a world added by the welcome, by <c>/connect</c> or on F5 all count.
    /// </summary>
    private void NoteFirstWorld()
    {
        if (_startedEmpty && !_tourOwedNoted && _config.Worlds.Count > 0)
        {
            _tourOwedNoted = true;
            _config.KeyTourPending = true;
        }
    }

    private bool _tourOwedNoted;

    /// <summary>The first connect of a new configuration: the key tour, and a reminder the form asked for.</summary>
    private void OnFirstRunConnected(WorldSession session, string windowId)
    {
        if (_config.KeyTourPending)
        {
            _config.KeyTourPending = false;
            ShowKeyTour(windowId);
            PersistConfiguration();
        }

        if (_passwordLater.Remove(session.SessionKey))
        {
            Notice(
                $"to log in by itself next time, save {Snippet(session.Character?.Name ?? session.World.Name)}'s password on F5",
                MessageSeverity.Info,
                "F5");
        }
    }

    /// <summary>The tour row: one line naming four keys, chrome in the window, never the session's text.</summary>
    internal string KeyTourMarkup()
    {
        string Key(string key) => $"[bold {_ink.Accent}]{Escape(key)}[/]";
        string Say(string text) => $"[{_ink.Notice}]{Escape(text)}[/]";
        return Say(KeyTourLead + " ")
            + Key("⌃P") + Say(" every command · ")
            + Key("⌥M") + Say(" layouts · ")
            + Key("F1") + Say(" long posts · ")
            + Key("F5") + Say(" worlds · ")
            + Say("hide with ⌃P ▸ Key tour");
    }

    /// <summary>Puts the tour row at the end of <paramref name="windowId"/>, or moves it there.</summary>
    internal void ShowKeyTour(string windowId)
    {
        HideKeyTour();
        AppendWindowLine(windowId, KeyTourMarkup());
        _tourWindow = windowId;
    }

    /// <summary>Takes the tour row away. False when there was none.</summary>
    private bool HideKeyTour()
    {
        if (_tourWindow is not { } windowId)
        {
            return false;
        }

        _tourWindow = null;
        if (_lines.TryGetValue(windowId, out var buffer))
        {
            var markup = KeyTourMarkup();
            var at = buffer.FindLastIndex(line => string.Equals(line.Markup, markup, StringComparison.Ordinal));
            if (at >= 0)
            {
                RemoveChromeRow(windowId, at);
                RepaintPane(windowId);
                return true;
            }
        }

        return false;
    }

    /// <summary>⌃P ▸ <c>Key tour</c>: shows the row in the focused window, or hides it when it is up.</summary>
    private void ToggleKeyTour()
    {
        if (!HideKeyTour())
        {
            ShowKeyTour(ActiveWindowId());
        }
    }

    /// <summary>The snapshot views that start with this: <c>welcome</c> and <c>welcome-connect</c>.</summary>
    internal const string WelcomeView = "welcome";

    /// <summary>
    /// The welcome as a first run meets it: the configuration emptied, the startup path run with nothing to
    /// connect (which raises the notice and opens the surface), and for <c>welcome-connect</c> the form
    /// filled in by real keys — ⏎ on the first choice, a host, a port over the default, TLS on, a name.
    /// </summary>
    private string RenderWelcomeSnapshot(string view)
    {
        // And the workspace a first run has: the main window alone, owned by nobody. --demo-config resumes a
        // saved workspace with a character's windows in it, which an empty configuration could never have.
        _config.Worlds.Clear();
        foreach (var window in _workspace.Windows.Select(w => w.Id).Where(id => id != MainWindowId).ToList())
        {
            _workspace.CloseWindow(window);
        }

        if (_workspace.FindWindow(MainWindowId) is { } main)
        {
            main.Title = "Main";
            _workspace.SetWindowOwner(MainWindowId, null);
        }

        RebuildPaneArea();
        RefreshHeader();
        RefreshRail();
        UpdateStatus();
        LastCommand = StartAsync([]);

        if (string.Equals(view, WelcomeView + "-connect", StringComparison.OrdinalIgnoreCase))
        {
            SimulateWelcomeKey(Stroke('\r', ConsoleKey.Enter));
            Type("mud.example.org");
            SimulateWelcomeKey(Stroke('\0', ConsoleKey.DownArrow));
            for (var i = 0; i < 4; i++)
            {
                SimulateWelcomeKey(Stroke('\b', ConsoleKey.Backspace));
            }

            Type("4201");
            SimulateWelcomeKey(Stroke('\0', ConsoleKey.DownArrow));
            SimulateWelcomeKey(Stroke(' ', ConsoleKey.Spacebar));
            SimulateWelcomeKey(Stroke('\0', ConsoleKey.DownArrow));
            Type("Corvid");
        }

        SyncInputWidth();
        return RenderFrame();

        void Type(string text)
        {
            foreach (var c in text)
            {
                SimulateWelcomeKey(Stroke(c, ConsoleKey.NoName));
            }
        }
    }

    /// <summary>Whether a tour row is on screen, for the tests.</summary>
    internal bool KeyTourShown => _tourWindow is not null;
}
