using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// ⌃1–⌃9 and ⌃0 go to the main window of the first ten open characters, in the order the sidebar lists them, on a
/// host that reports Ctrl+digit whole (the window). A terminal sends those chords as a bare digit or as another
/// key's byte, so there nothing claims them and F4 says so.
/// </summary>
[NotInParallel]
public class CharacterJumpTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly HostKeys WindowKeys = HostKeys.Terminal with { ReportsChordsWhole = true, DeliversNumpad = true };

    private static readonly string[] Sessions = ["Alfa.Ann", "Bravo.Bob", "Cara.Cal"];

    [Test]
    public async Task EachDigitGoesToThatCharactersMainWindow()
    {
        await using var app = Scene(WindowKeys);

        app.SimulateKey(CtrlDigit(2));
        await Assert.That(app.ActiveSessionKey).IsEqualTo("Bravo.Bob");
        await Assert.That(app.ActiveWindowId()).IsEqualTo("char:Bravo.Bob");

        app.SimulateKey(CtrlDigit(3));
        await Assert.That(app.ActiveSessionKey).IsEqualTo("Cara.Cal");

        app.SimulateKey(CtrlDigit(1));
        await Assert.That(app.ActiveSessionKey).IsEqualTo("Alfa.Ann");
    }

    [Test]
    public async Task ADigitPastTheOpenCharactersSaysSoAndOpensNobody()
    {
        await using var app = Scene(WindowKeys, unopened: true);

        app.SimulateKey(CtrlDigit(4));
        await Assert.That(app.ActiveSessionKey).IsEqualTo("Alfa.Ann");
        await Assert.That(app.StatusMarkup).Contains("there is no character 4");
        await Assert.That(app.FindSession("Delta.Dee")).IsNull().Because("⌃N walks open characters and never dials one");

        app.SimulateKey(CtrlDigit(0));
        await Assert.That(app.StatusMarkup).Contains("there is no character 10").Because("⌃0 is the tenth");
    }

    [Test]
    public async Task TheSidebarAndThePaletteNameEachCharactersChord()
    {
        await using var app = Scene(WindowKeys);
        app.RenderNextFrame();

        var rail = app.RailLines.Select(FrameGrid.Visible).ToArray();
        foreach (var (name, chord) in new[] { ("Ann", "⌃1"), ("Bob", "⌃2"), ("Cal", "⌃3") })
        {
            await Assert.That(rail.Any(l => l.Contains(name, StringComparison.Ordinal) && l.Contains(chord, StringComparison.Ordinal)))
                .IsTrue().Because($"{name}'s row names {chord}");
        }

        var bob = app.BuildCatalog().Single(c => c.Id == CommandIds.Character("Bravo.Bob"));
        await Assert.That(bob.Key).IsEqualTo("⌃2");

        var help = app.HelpModelNow().Topic("keys")!;
        await Assert.That(help.Lines.Any(l => l.Contains("⌃1 to ⌃9 and ⌃0", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ATerminalClaimsNoCtrlDigit()
    {
        await using var app = Scene(HostKeys.Terminal);

        app.SimulateKey(CtrlDigit(2));
        await Assert.That(app.ActiveSessionKey).IsEqualTo("Alfa.Ann");

        var bob = app.BuildCatalog().Single(c => c.Id == CommandIds.Character("Bravo.Bob"));
        await Assert.That(bob.Key).IsEqualTo("⌥J").Because("the terminal keeps the cycle's neighbours");
        await Assert.That(app.HelpModelNow().Topic("keys")!.Lines.Any(l => l.Contains("⌃1 to ⌃9", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task F4SaysTakenInTheWindowAndNeverArrivesInATerminal()
    {
        for (var n = 0; n <= 9; n++)
        {
            var window = MacroKeys.Verdict(WindowKeys, $"Ctrl+{n}");
            await Assert.That(window.Delivery).IsEqualTo(MacroKeyDelivery.Taken);
            await Assert.That(window.Reason).Contains($"goes to character {(n == 0 ? 10 : n)}");
            await Assert.That(MacroKeys.Verdict(HostKeys.Terminal, $"Ctrl+{n}").Delivery).IsEqualTo(MacroKeyDelivery.NeverArrives);
        }

        await Assert.That(MacroKeys.Verdict(WindowKeys, "Ctrl+Shift+1").Fires).IsTrue().Because("only the bare Ctrl chord is taken");
    }

    [Test]
    public async Task HelpNamesTheChordOnlyWhereItArrives()
    {
        var window = new StringWriter();
        ClientUsage.Write(window, ClientUsage.Terminal, WindowKeys);
        await Assert.That(window.ToString()).Contains("Ctrl+1 to Ctrl+9 and Ctrl+0");

        var terminal = new StringWriter();
        ClientUsage.Write(terminal, ClientUsage.Terminal, HostKeys.Terminal);
        await Assert.That(terminal.ToString()).DoesNotContain("Ctrl+1 to Ctrl+9");
    }

    private static ConsoleKeyInfo CtrlDigit(int n) => new((char)('0' + n), ConsoleKey.D0 + n, false, false, true);

    private static SharpMUTermApp Scene(HostKeys keys, bool unopened = false)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var names = new List<(string World, string Character)> { ("Alfa", "Ann"), ("Bravo", "Bob"), ("Cara", "Cal") };
        if (unopened)
        {
            names.Add(("Delta", "Dee"));
        }

        foreach (var (world, character) in names)
        {
            var definition = new WorldDefinition { Name = world, Host = $"{world.ToLowerInvariant()}.example.org", Port = 4000 };
            definition.Characters.Add(new CharacterDefinition { Name = character, Logging = new LoggingSettings() });
            config.Worlds.Add(definition);
        }

        var host = ClientHost.Headless(new HeadlessConsoleDriver(160, 40), Caps) with { Keys = keys };
        var app = new SharpMUTermApp(config, host);
        app.TelnetFactory = _ => new RecordingTelnetSession();
        foreach (var key in Sessions)
        {
            if (!app.DispatchCommand(CommandIds.Character(key)))
            {
                throw new InvalidOperationException($"the app would not switch to {key}");
            }
        }

        app.DispatchCommand(CommandIds.Character(Sessions[0]));
        app.RenderNextFrame();
        return app;
    }
}
