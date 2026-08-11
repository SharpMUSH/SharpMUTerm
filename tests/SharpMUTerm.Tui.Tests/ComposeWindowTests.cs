using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The composer, driven the way a user drives it: F1 opens it on the focused character's draft, ⌃S
/// sends one command to that character, Esc keeps the post, and ⌥L changes what "send" means.
/// <para>
/// The sessions are <strong>connected</strong> for the same reason
/// <see cref="LinkSchemeSecurityTests"/>'s are: <c>SendUserInputAsync</c> returns without writing when
/// there is no live transport, so "the post reached the world" asserted against an unconnected session
/// would be true whatever the code did.
/// </para>
/// </summary>
/// <remarks>
/// Serialised with the other end-to-end suites: constructing the app and rendering a frame both touch
/// the process-global console streams.
/// </remarks>
[NotInParallel]
public class ComposeWindowTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo Key(ConsoleKey key, ConsoleModifiers modifiers = 0) =>
        new('\0', key, modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control));

    private static ConsoleKeyInfo CtrlS => Key(ConsoleKey.S, ConsoleModifiers.Control);

    private static ConsoleKeyInfo AltL => Key(ConsoleKey.L, ConsoleModifiers.Alt);

    private static ConsoleKeyInfo Esc => Key(ConsoleKey.Escape);

    // ---- Opening and closing ----------------------------------------------------------------

    [Test]
    public async Task F1OpensTheComposerAndF1ClosesIt()
    {
        var world = await Connected();

        world.App.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(world.App.Composer.IsOpen).IsTrue();

        world.App.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(world.App.Composer.IsOpen).IsFalse();
    }

    [Test]
    public async Task TheCommandSurfaceOpensItToo()
    {
        var world = await Connected();

        await Assert.That(world.App.DispatchCommand("term:compose")).IsTrue();

        await Assert.That(world.App.Composer.IsOpen).IsTrue();
    }

    /// <summary>
    /// The header names the character the post will reach — resolved from the focused window, which is
    /// the same rule ⏎ follows. It is asserted against <c>SessionTitle</c>'s own output rather than the
    /// string "Ann", because the snapshot demo has to fake this value and the two must not drift.
    /// </summary>
    [Test]
    public async Task TheHeaderNamesTheCharacterItWillSendTo()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(world.App.Composer.HeaderMarkup).Contains("Ann");
        await Assert.That(world.App.Composer.HeaderMarkup).DoesNotContain("no connection");
    }

    // ---- Sending -----------------------------------------------------------------------------

    [Test]
    public async Task CtrlSSendsTheWholeBufferAsOneCommand()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F1));
        world.App.Composer.SimulateTyping("+bbpost 12=Title\nfirst\nsecond");

        await Assert.That(world.App.Composer.SimulateKey(CtrlS)).IsTrue();

        await Assert.That(world.Telnet.Lines).IsEquivalentTo(new[] { "+bbpost 12=Title%rfirst%rsecond" });
        await Assert.That(world.App.Composer.IsOpen).IsFalse();
    }

    [Test]
    public async Task AltLFlipsTheEscapingAndChangesWhatIsSent()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F1));
        world.App.Composer.SimulateTyping("100% [sure]");

        await Assert.That(world.App.Composer.SimulateKey(AltL)).IsTrue();
        await Assert.That(world.App.Composer.Escaping).IsEqualTo(ComposeEscaping.Literal);
        await Assert.That(world.App.Composer.HeaderMarkup).Contains("literal");

        world.App.Composer.SimulateKey(CtrlS);

        await Assert.That(world.Telnet.Lines).IsEquivalentTo(new[] { "100%% \\[sure\\]" });
    }

    [Test]
    public async Task SendingAnEmptyComposerSaysSoAndSendsNothing()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F1));

        world.App.Composer.SimulateKey(CtrlS);

        await Assert.That(world.Telnet.Lines).IsEmpty();
        await Assert.That(world.App.Composer.IsOpen).IsTrue(); // the window stays; nothing was posted
        await Assert.That(world.App.StatusMarkup).Contains("composer is empty");
    }

    // ---- Drafts ------------------------------------------------------------------------------

    [Test]
    public async Task EscKeepsThePostAndReopeningBringsItBack()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F1));
        world.App.Composer.SimulateTyping("half a post");

        await Assert.That(world.App.Composer.SimulateKey(Esc)).IsTrue();
        await Assert.That(world.App.Composer.IsOpen).IsFalse();

        world.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(world.App.Composer.Body).IsEqualTo("half a post");
    }

    /// <summary>The mode travels with the draft: reopening in the other one would send different text.</summary>
    [Test]
    public async Task TheEscapingModeIsKeptWithTheDraft()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F1));
        world.App.Composer.SimulateTyping("100%");
        world.App.Composer.SimulateKey(AltL);
        world.App.Composer.SimulateKey(Esc);

        world.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(world.App.Composer.Escaping).IsEqualTo(ComposeEscaping.Literal);
    }

    [Test]
    public async Task ASentPostIsNotStillThereNextTime()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F1));
        world.App.Composer.SimulateTyping("posted and gone");
        world.App.Composer.SimulateKey(CtrlS);

        world.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(world.App.Composer.Body).IsEmpty();
    }

    /// <summary>
    /// A draft belongs to a character, not to the client. Two open characters keep two posts, and
    /// switching between them brings the right one back — the failure this guards is the one a single
    /// shared buffer would produce, where a post written to one game reappears addressed to another.
    /// </summary>
    [Test]
    public async Task EachCharacterKeepsItsOwnPost()
    {
        var two = await TwoConnectedWorlds();

        two.App.SimulateKey(Key(ConsoleKey.F1));
        two.App.Composer.SimulateTyping("Bob's post");
        two.App.Composer.SimulateKey(Esc);

        await Assert.That(two.App.DispatchCommand("char:Quiet.Ann")).IsTrue();
        two.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(two.App.Composer.Body).IsEmpty();

        two.App.Composer.SimulateTyping("Ann's post");
        two.App.Composer.SimulateKey(Esc);
        await Assert.That(two.App.DispatchCommand("char:Loud.Bob")).IsTrue();
        two.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(two.App.Composer.Body).IsEqualTo("Bob's post");
    }

    /// <summary>
    /// The post goes to the character whose window is focused, not to whichever was active last — the
    /// same rule ⏎ follows, and the misdelivery this codebase has had before.
    /// </summary>
    [Test]
    public async Task ThePostGoesToTheFocusedWindowsCharacter()
    {
        var two = await TwoConnectedWorlds();
        await Assert.That(two.App.DispatchCommand("char:Quiet.Ann")).IsTrue();

        two.App.SimulateKey(Key(ConsoleKey.F1));
        two.App.Composer.SimulateTyping("for Ann");
        two.App.Composer.SimulateKey(CtrlS);

        await Assert.That(two.Quiet.Lines).IsEquivalentTo(new[] { "for Ann" });
        await Assert.That(two.Loud.Lines).IsEmpty();
    }

    // ---- Refusals ----------------------------------------------------------------------------

    /// <summary>
    /// With nothing connected the window still opens — writing a post while the world is down is a real
    /// thing to be doing — and refuses at the moment of sending, keeping the post.
    /// </summary>
    [Test]
    public async Task WithNothingConnectedItOpensAndRefusesToSend()
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(new AppConfiguration(), Headless, new HeadlessConsoleDriver(Width, Height));

        app.SimulateKey(Key(ConsoleKey.F1));
        await Assert.That(app.Composer.IsOpen).IsTrue();
        await Assert.That(app.Composer.HeaderMarkup).Contains("no connection");

        app.Composer.SimulateTyping("a post with nowhere to go");
        app.Composer.SimulateKey(CtrlS);

        await Assert.That(app.Composer.IsOpen).IsTrue();
        await Assert.That(app.Composer.Body).IsEqualTo("a post with nowhere to go");
    }

    /// <summary>
    /// It will not open over a settings screen. Two modal windows with two <c>PreviewKeyPressed</c>
    /// handlers cannot be driven headlessly — and, worse, <c>SettingsOverlay</c> listens for paste at the
    /// driver precisely because its screens have no focusable target, so a paste with both open would be
    /// delivered twice.
    /// </summary>
    [Test]
    public async Task ItRefusesToOpenOverASettingsScreen()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F7));

        world.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(world.App.Composer.IsOpen).IsFalse();
        await Assert.That(world.App.StatusMarkup).Contains("close the settings screen first");
    }

    /// <summary>
    /// The same rule for every surface, not only the settings screens. A modal surface owns the screen
    /// in this client, and the composer is one — so it may not be stacked on top of another, and the
    /// refusal names the one that is in the way rather than saying "a surface".
    /// </summary>
    [Test]
    public async Task ItRefusesToOpenOverTheCommandSurface()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.P, ConsoleModifiers.Control));
        await Assert.That(world.App.MenuIsOpen).IsTrue();

        world.App.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(world.App.Composer.IsOpen).IsFalse();
        await Assert.That(world.App.StatusMarkup).Contains("close the command surface first");
    }

    /// <summary>
    /// And the other direction, which is what putting the composer into <c>AnyOverlayOpen</c> buys: the
    /// surfaces that already decline to act while a screen is up decline while a post is being written
    /// too. ⌃R is the one that reads that flag directly.
    /// </summary>
    [Test]
    public async Task TheComposerCountsAsAnOpenSurface()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F1));

        world.App.SimulateKey(Key(ConsoleKey.R, ConsoleModifiers.Control));

        await Assert.That(world.App.HistorySearchOpen).IsFalse();
        await Assert.That(world.App.Composer.IsOpen).IsTrue();
    }

    /// <summary>
    /// And nothing opens over the composer either — the pair is mutually exclusive rather than
    /// one-sided. Driven through the real chords and the real ⌃P entry, because a global shortcut runs
    /// before any window sees the key, which is exactly how a second modal got on top of this one.
    /// </summary>
    [Test]
    public async Task NoOtherSurfaceOpensOverTheComposer()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F1));

        world.App.SimulateKey(Key(ConsoleKey.P, ConsoleModifiers.Control));
        await Assert.That(world.App.MenuIsOpen).IsFalse();

        world.App.SimulateKey(Key(ConsoleKey.F7));
        await Assert.That(world.App.OpenSettingsKey).IsNull();

        world.App.DispatchCommand("term:messages");
        await Assert.That(world.App.MessageLogIsOpen).IsFalse();

        await Assert.That(world.App.Composer.IsOpen).IsTrue();
        await Assert.That(world.App.StatusMarkup).Contains("close the composer first");
    }

    /// <summary>
    /// ⌃Q is the exception, and deliberately: a client you cannot leave from a modal would be worse than
    /// one that stacks a prompt. What it does instead is <em>count</em> the unsent post, so the question
    /// says what leaving would cost — the composer keeps drafts in memory only, so quitting is the one
    /// thing that loses them.
    /// </summary>
    [Test]
    public async Task QuittingStillWorksAndCountsTheUnsentPost()
    {
        var world = await Connected();
        world.App.SimulateKey(Key(ConsoleKey.F1));
        world.App.Composer.SimulateTyping("a post worth keeping");

        world.App.SimulateKey(Key(ConsoleKey.Q, ConsoleModifiers.Control));

        await Assert.That(world.App.QuitPromptOpen).IsTrue();
        await Assert.That(string.Join("\n", world.App.QuitPromptLines)).Contains("unsent draft");
    }

    // ---- Harness -----------------------------------------------------------------------------

    private sealed record Wired(SharpMUTermApp App, RecordingTelnetSession Telnet);

    private static async Task<Wired> Connected()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        await app.BindWorldWithoutConnecting(world).ConnectAsync();
        app.RenderNextFrame();
        return new Wired(app, telnet);
    }

    private sealed record TwoWorlds(
        SharpMUTermApp App, RecordingTelnetSession Quiet, RecordingTelnetSession Loud);

    private static async Task<TwoWorlds> TwoConnectedWorlds()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();

        var quiet = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        quiet.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        var loud = new WorldDefinition { Name = "Loud", Host = "loud.example.org", Port = 4000 };
        loud.Characters.Add(new CharacterDefinition { Name = "Bob", Logging = new LoggingSettings() });
        config.Worlds.Add(quiet);
        config.Worlds.Add(loud);

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height));
        var quietTelnet = new RecordingTelnetSession();
        var loudTelnet = new RecordingTelnetSession();
        app.TelnetFactory = options =>
            options.Host.StartsWith("quiet", StringComparison.Ordinal) ? quietTelnet : loudTelnet;

        await Open(app, "Quiet.Ann");
        await Open(app, "Loud.Bob");
        app.RenderNextFrame();
        return new TwoWorlds(app, quietTelnet, loudTelnet);
    }

    private static async Task Open(SharpMUTermApp app, string sessionKey)
    {
        if (!app.DispatchCommand(Core.Commands.CommandIds.Character(sessionKey)))
        {
            throw new InvalidOperationException($"the app would not switch to {sessionKey}");
        }

        await app.FindSession(sessionKey)!.ConnectAsync();
    }
}
