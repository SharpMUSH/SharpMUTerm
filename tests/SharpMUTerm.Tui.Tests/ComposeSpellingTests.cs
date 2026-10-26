using System.Text;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Telnet;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Spelling in the F1 composer: unknown words painted in the warning colour, ⌥W to add one, and a header that
/// says which dictionary is in use or that there is none.
/// </summary>
[NotInParallel]
public class ComposeSpellingTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo Key(ConsoleKey key, ConsoleModifiers modifiers = 0) =>
        new('\0', key, modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control));

    private static SpellChecker Dictionary() =>
        SpellChecker.FromStreams(
            new MemoryStream(Encoding.UTF8.GetBytes("3\nhello\nthe\nworld\n")),
            new MemoryStream(Encoding.UTF8.GetBytes("SET UTF-8\n")),
            "en_GB");

    private static SharpMUTermApp App(Func<SpellChecker?>? spelling)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(new AppConfiguration(), Headless, new HeadlessConsoleDriver(Width, Height), spelling: spelling);
    }

    private static Rgb? ForegroundOf(SharpMUTermApp app, string word)
    {
        var frame = app.RenderWholeFrame();
        var rows = FrameGrid.Decode(frame, Width, Height);
        var cells = FrameGrid.Cells(frame, Width, Height);
        for (var row = 0; row < rows.Count; row++)
        {
            var column = rows[row].IndexOf(word, StringComparison.Ordinal);
            if (column >= 0)
            {
                return cells[(row, column)].Foreground;
            }
        }

        throw new InvalidOperationException($"'{word}' is not on screen");
    }

    private static readonly Rgb Warn = new(0xff, 0x6b, 0x6b);

    [Test]
    public async Task AnUnknownWordIsPaintedInTheWarningColourAndAKnownOneIsNot()
    {
        var app = App(Dictionary);
        app.SimulateKey(Key(ConsoleKey.F1));
        app.Composer.SimulateTyping("hello wrold");

        await Assert.That(app.Composer.HeaderMarkup).Contains("en_GB");
        await Assert.That(ForegroundOf(app, "wrold")).IsEqualTo(Warn);
        await Assert.That(ForegroundOf(app, "hello")).IsNotEqualTo(Warn);
    }

    [Test]
    public async Task AltWAddsTheWordAtTheCaretAndItIsNoLongerMarked()
    {
        var checker = Dictionary();
        var app = App(() => checker);
        app.SimulateKey(Key(ConsoleKey.F1));
        app.Composer.SimulateTyping("Corvid");
        await Assert.That(ForegroundOf(app, "Corvid")).IsEqualTo(Warn);

        await Assert.That(app.Composer.SimulateKey(Key(ConsoleKey.W, ConsoleModifiers.Alt))).IsTrue();

        await Assert.That(checker.AddedWords).Contains("Corvid");
        await Assert.That(ForegroundOf(app, "Corvid")).IsNotEqualTo(Warn);
        await Assert.That(app.Composer.Body).IsEqualTo("Corvid");
    }

    [Test]
    public async Task WithNoDictionaryTheHeaderSaysSoAndAltWIsNotOffered()
    {
        var app = App(() => null);
        app.SimulateKey(Key(ConsoleKey.F1));
        app.Composer.SimulateTyping("wrold");

        await Assert.That(app.Composer.HeaderMarkup).Contains("no dictionary");
        await Assert.That(app.Composer.SimulateKey(Key(ConsoleKey.W, ConsoleModifiers.Alt))).IsFalse();
        var rows = FrameGrid.Decode(app.RenderWholeFrame(), Width, Height);
        await Assert.That(rows.Any(r => r.Contains("⌥W", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task TheDictionaryIsOpenedOnceAndOnlyWhenTheComposerIs()
    {
        var opened = 0;
        var app = App(() => { opened++; return Dictionary(); });
        await Assert.That(opened).IsEqualTo(0);

        app.SimulateKey(Key(ConsoleKey.F1));
        app.SimulateKey(Key(ConsoleKey.F1));
        app.SimulateKey(Key(ConsoleKey.F1));

        await Assert.That(opened).IsEqualTo(1);
    }

    [Test]
    public async Task AMooEditIsNotSpellChecked()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Moo", Host = "moo.example.org", Port = 7777 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(Width, Height), spelling: Dictionary);
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        var session = app.BindWorldWithoutConnecting(world);
        await session.ConnectAsync();
        app.RenderNextFrame();

        telnet.ReceiveMooEdit(new MooEdit("#98.look", "Test:look", "moo-code", ["player:tell(\"Hi.\");"]));

        await Assert.That(app.Composer.IsOpen).IsTrue();
        await Assert.That(app.Composer.Spelling).IsNull();
        await Assert.That(ForegroundOf(app, "player")).IsNotEqualTo(Warn);
    }
}
