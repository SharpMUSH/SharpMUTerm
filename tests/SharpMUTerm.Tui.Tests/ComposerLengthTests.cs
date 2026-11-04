using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The composer says how long the post is as it will be sent, and ⌥P shows it as sent: escapes and <c>%r</c> joins
/// included, because that is what a MUSH's buffer limit counts.
/// </summary>
[NotInParallel]
public class ComposerLengthTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo AltP => new('\0', ConsoleKey.P, false, true, false);

    private static async Task<SharpMUTermApp> Connected()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);
        var app = new SharpMUTermApp(config, ClientHost.Headless(new HeadlessConsoleDriver(120, 32), Caps));
        app.TelnetFactory = _ => new RecordingTelnetSession();
        await app.BindWorldWithoutConnecting(world).ConnectAsync();
        app.RenderNextFrame();
        return app;
    }

    [Test]
    public async Task TheCountIsOfWhatWillBeSent()
    {
        await Assert.That(ComposerModel.SentLength("+post a\nb", ComposeEscaping.AsTyped, edit: false))
            .IsEqualTo(ComposeMessage.Build("+post a\nb", ComposeEscaping.AsTyped)!.Length);
        await Assert.That(ComposerModel.SentLength("50% [x]", ComposeEscaping.Literal, edit: false))
            .IsGreaterThan("50% [x]".Length).Because("literal escapes are characters the game receives");
        await Assert.That(ComposerModel.SentLength("line\n", ComposeEscaping.Literal, edit: true)).IsEqualTo(4);
        await Assert.That(ComposerModel.CharactersLabel(1)).IsEqualTo("1 char");
        await Assert.That(ComposerModel.CharactersLabel(1234)).IsEqualTo("1,234 chars");
    }

    [Test]
    public async Task F1sHeaderCountsThePostAsItIsTyped()
    {
        var app = await Connected();
        app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
        app.Composer.SimulateTyping("+post hello");

        await Assert.That(app.Composer.HeaderMarkup).Contains(ComposerModel.CharactersLabel(app.Composer.Model.Characters));
        await Assert.That(app.Composer.Model.Characters).IsEqualTo("+post hello".Length);
    }

    [Test]
    public async Task AltPShowsThePostAsSentAndAgainHidesIt()
    {
        var app = await Connected();
        app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F1, false, false, false));
        app.Composer.SimulateTyping("+post one\n\ntwo");

        await Assert.That(app.Composer.SimulateKey(AltP)).IsTrue();
        await Assert.That(app.Composer.Model.Preview).IsTrue();
        await Assert.That(app.Composer.Model.Keys.Select(k => k.Label)).Contains("hide as sent");
        var preview = ComposeOverlay.PreviewLines(app.Composer.Model, 120).Select(MarkupText.Plain).ToList();
        await Assert.That(string.Join("\n", preview)).Contains("+post one%r%rtwo");
        await Assert.That(app.RenderWholeFrame()).Contains("AS SENT");

        app.Composer.SimulateKey(AltP);
        await Assert.That(app.Composer.Model.Preview).IsFalse();
    }

    [Test]
    public async Task AMooEditHasNoPreview()
    {
        var model = ComposerModel.Create(
            ComposerPlace.FullScreen, "Ann", "x", ComposeEscaping.AsTyped, new ComposerEdit("name", "moo-code"),
            spelling: null, canSend: true, armed: true, revision: 0, preview: true);

        await Assert.That(model.Preview).IsFalse();
    }
}
