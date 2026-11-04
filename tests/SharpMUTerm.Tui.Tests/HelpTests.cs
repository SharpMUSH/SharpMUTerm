using System.Security.Cryptography;
using System.Text;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Help;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// In-app help (<c>/help</c>, ⌃P ▸ Help), the spelling dictionary offered from F1 and fetched only when asked, and the
/// ⌃P / F5 suggestion to join a world's server wraps.
/// </summary>
[NotInParallel]
public class HelpTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0', ConsoleModifiers modifiers = 0) =>
        new(c, key, modifiers.HasFlag(ConsoleModifiers.Shift), modifiers.HasFlag(ConsoleModifiers.Alt), modifiers.HasFlag(ConsoleModifiers.Control));

    private static SharpMUTermApp App(string? dictionaries = null)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(new AppConfiguration(), Headless, new HeadlessConsoleDriver(120, 32), dictionaries: dictionaries);
    }

    private static void TypeCommand(SharpMUTermApp app, string text)
    {
        foreach (var c in text)
        {
            app.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        app.SimulateKey(Key(ConsoleKey.Enter, '\r'));
    }

    [Test]
    public async Task SlashHelpOpensHelpWithItsQueryTyped()
    {
        var app = App();
        TypeCommand(app, "/help connect");

        await Assert.That(app.HelpIsOpen).IsTrue();
        await Assert.That(app.CellHelp!.Query).IsEqualTo("connect");
        await Assert.That(app.PrimaryInputText).IsEmpty();
        await Assert.That(string.Join("\n", app.HelpLines)).Contains("/connect");
    }

    [Test]
    public async Task ThePaletteEntryOpensHelpAndEscClosesIt()
    {
        var app = App();
        await Assert.That(app.DispatchCommand(BuiltInCommands.HelpCommand)).IsTrue();
        await Assert.That(app.HelpIsOpen).IsTrue();

        app.SimulateHelpKey(Key(ConsoleKey.Escape, '\x1b'));
        await Assert.That(app.HelpIsOpen).IsFalse();
    }

    [Test]
    public async Task ChoosingASlashCommandPlacesItOnTheCommandLineAndSendsNothing()
    {
        var app = App();
        app.DispatchCommand(BuiltInCommands.HelpCommand);
        app.SimulateHelpTyping("/walk");
        var help = app.CellHelp!;
        while (help.Rows[help.Selected].Kind != HelpKind.Command)
        {
            app.SimulateHelpKey(Key(ConsoleKey.DownArrow));
        }

        app.SimulateHelpKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(app.HelpIsOpen).IsFalse();
        await Assert.That(app.PrimaryInputText).StartsWith("/walk");
    }

    [Test]
    public async Task AHowToOpensAsAPageAndEscGoesBackToTheList()
    {
        var app = App();
        app.DispatchCommand(BuiltInCommands.HelpCommand);
        app.SimulateHelpKey(Key(ConsoleKey.Enter, '\r'));

        await Assert.That(app.CellHelp!.Topic).IsEqualTo("getting-started");
        app.SimulateHelpKey(Key(ConsoleKey.Escape, '\x1b'));
        await Assert.That(app.HelpIsOpen).IsTrue();
        await Assert.That(app.CellHelp!.Topic).IsNull();
    }

    [Test]
    public async Task EveryRenderIsAsTallAsItWasAsked()
    {
        var model = App().HelpModelNow();
        foreach (var (query, topic) in new[] { ("", (string?)null), ("zzqx", null), ("", "keys"), ("connect", null) })
        {
            await Assert.That(HelpSurface.Render(model, query, 0, topic, 0, 80, 18).Count).IsEqualTo(18);
        }
    }

    [Test]
    public async Task EverySlashCommandTheCommandLineAnswersIsInTheTable()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "SharpMUTerm.slnx")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        var source = File.ReadAllText(Path.Combine(root, "src", "SharpMUTerm.Tui", "SharpMUTermApp.cs"));
        var handled = System.Text.RegularExpressions.Regex.Matches(source, "\"(/[a-z]+)[ \"]")
            .Select(m => m.Groups[1].Value)
            .Where(n => n is "/help" or "/connect" or "/welcome" or "/package" or "/walk" or "/map" or "/web" or "/graphics" or "/triggers")
            .Distinct();
        foreach (var name in handled)
        {
            await Assert.That(SlashCommands.Names).Contains(name);
        }

        await Assert.That(SlashCommands.Names).Contains("/help");
    }

    [Test]
    public async Task AltWInAComposerWithNoDictionaryOffersOne_AndF1ComesBackAfter()
    {
        var app = App();
        app.SimulateKey(Key(ConsoleKey.F1));
        app.Composer.SimulateTyping("a post");
        app.Composer.SimulateKey(Key(ConsoleKey.W, modifiers: ConsoleModifiers.Alt));

        await Assert.That(app.DictionaryOfferOpen).IsTrue();
        await Assert.That(app.ComposerOpen).IsFalse();
        await Assert.That(string.Join("\n", app.HelpLines)).Contains("nothing can be saved");

        app.SimulateHelpKey(Key(ConsoleKey.Escape, '\x1b'));
        await Assert.That(app.DictionaryOfferOpen).IsFalse();
        await Assert.That(app.ComposerOpen).IsTrue().Because("the offer closed F1 and gives it back");
    }

    [Test]
    public async Task WithNoDictionariesFolderNothingIsFetched()
    {
        var app = App();
        var asked = 0;
        app.DictionaryFetch = (_, _, _) =>
        {
            asked++;
            return Task.FromResult<byte[]?>(null);
        };
        app.InstallDictionary();

        await Assert.That(app.DictionaryTask is null).IsTrue();
        await Assert.That(app.LastNotice!).Contains("no dictionaries folder");
    }

    [Test]
    public async Task YesFetchesThePairIntoTheFolderAndSaysSo()
    {
        var folder = Path.Combine(Path.GetTempPath(), "smt-dict-" + Guid.NewGuid().ToString("N"));
        try
        {
            var aff = Encoding.UTF8.GetBytes("SET UTF-8\n");
            var dic = Encoding.UTF8.GetBytes("2\nhello\nworld\n");
            var app = App(folder);
            app.DictionaryToFetch = new DictionarySource(
                "en_TEST", "English (test)", "a test", "public domain",
                new Uri("https://example.org/en_TEST.aff"), Convert.ToHexStringLower(SHA256.HashData(aff)),
                new Uri("https://example.org/en_TEST.dic"), Convert.ToHexStringLower(SHA256.HashData(dic)),
                aff.Length + dic.Length);
            app.DictionaryFetch = (uri, _, _) => Task.FromResult<byte[]?>(uri.AbsolutePath.EndsWith(".aff", StringComparison.Ordinal) ? aff : dic);

            app.DispatchCommand(BuiltInCommands.DictionaryCommand);
            await Assert.That(app.DictionaryOfferOpen).IsTrue();
            app.SimulateHelpKey(Key(ConsoleKey.Enter, '\r'));
            await (app.DictionaryTask ?? Task.CompletedTask);

            await Assert.That(DictionaryDownload.Holds(folder)).IsTrue();
            await Assert.That(app.LastNotice!).Contains("en_TEST is installed");
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Test]
    public async Task ARaggedWorldIsSuggestedOnThePalette_AndRecommendedOnF5()
    {
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition { Name = "Aetherfall", Host = "aether.example.org", Port = 4000 });
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        app.NoteRaggedWraps("Aetherfall", 78, 60);

        await Assert.That(app.RaggedWraps()!.Select(r => r.World)).Contains("Aetherfall");
        await Assert.That(app.JoinWrapsAdvice(config.Worlds[0])).IsEqualTo("recommended · wraps at 78, pane is 60");

        await Assert.That(app.DispatchCommand(BuiltInCommands.JoinWraps("Aetherfall"))).IsTrue();
        await Assert.That(config.Worlds[0].JoinWrappedLines).IsTrue();
        await Assert.That(app.RaggedWraps() ?? []).IsEmpty().Because("joined, there is nothing left to suggest");
        await Assert.That(app.JoinWrapsAdvice(config.Worlds[0])).IsNull();
    }
}
