using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <em>Open log folder</em>: ⌃P, F5's <c>o</c> and the window's character page all show the folder a character's
/// transcripts go to — the character's own folder, else the log root — and make it first if it is missing. With no log
/// root nothing is offered; with no opener the client says where the folder is.
/// </summary>
[NotInParallel]
public class LogFolderTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class Presenter : INativeSettings
    {
        public List<SettingsForm> Shown { get; } = [];

        public void Show(SettingsForm form) => Shown.Add(form);

        public void Close()
        {
        }
    }

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "smt-logfolder-" + Guid.NewGuid().ToString("N"));

    private static (SharpMUTermApp App, List<string> Opened) App(
        string? logRoot, AppConfiguration? config = null, bool opener = true, INativeSettings? native = null)
    {
        Console.SetIn(TextReader.Null);
        var opened = new List<string>();
        var host = ClientHost.Headless(new HeadlessConsoleDriver(120, 34), Caps) with
        {
            OpenFolder = opener ? opened.Add : null,
            NativeSettings = native,
        };
        return (new SharpMUTermApp(config ?? DemoScene.Build(), host, logRoot: logRoot), opened);
    }

    private static CharacterDefinition Corvid(AppConfiguration config) =>
        config.Worlds.SelectMany(w => w.Characters).First(c => c.Name == DemoScene.MainCharacterName);

    [Test]
    public async Task ThePaletteOffersItOnlyWhereTheClientKeepsLogs()
    {
        var (withRoot, _) = App(TempRoot());
        await Assert.That(withRoot.BuildCatalog().Any(i => i.Id == BuiltInCommands.OpenLogFolderCommand)).IsTrue();

        var (without, _) = App(logRoot: null);
        await Assert.That(without.BuildCatalog().Any(i => i.Id == BuiltInCommands.OpenLogFolderCommand)).IsFalse();
    }

    [Test]
    public async Task ThePaletteOpensTheFocusedCharactersOwnFolderAndMakesItFirst()
    {
        var root = TempRoot();
        var config = DemoScene.Build();
        var own = Path.Combine(root, "corvid's logs");
        Corvid(config).Logging.Directory = own;
        try
        {
            var (app, opened) = App(root, config);
            await Assert.That(Directory.Exists(own)).IsFalse();

            app.DispatchCommand(BuiltInCommands.OpenLogFolderCommand);

            await Assert.That(opened).IsEquivalentTo(new[] { Path.GetFullPath(own) });
            await Assert.That(Directory.Exists(own)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task ACharacterWithNoFolderOfItsOwnOpensTheLogRoot()
    {
        var root = TempRoot();
        var config = DemoScene.Build();
        Corvid(config).Logging.Directory = null;
        try
        {
            var (app, opened) = App(root, config);
            app.DispatchCommand(BuiltInCommands.OpenLogFolderCommand);
            await Assert.That(opened).IsEquivalentTo(new[] { Path.GetFullPath(root) });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task WithNoOpenerTheClientSaysWhereTheFolderIs()
    {
        var root = TempRoot();
        try
        {
            var config = DemoScene.Build();
            Corvid(config).Logging.Directory = null;
            var (app, opened) = App(root, config, opener: false);
            app.DispatchCommand(BuiltInCommands.OpenLogFolderCommand);

            await Assert.That(opened).IsEmpty();
            await Assert.That(app.LastNotice).Contains(Path.GetFullPath(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task WithNoLogRootNothingIsOpenedAndTheClientSaysSo()
    {
        var (app, opened) = App(logRoot: null);
        app.DispatchCommand(BuiltInCommands.OpenLogFolderCommand);

        await Assert.That(opened).IsEmpty();
        await Assert.That(app.LastNotice).Contains("keeps no logs");
    }

    [Test]
    public async Task OOnACharacterInF5OpensThatCharactersFolder()
    {
        var root = TempRoot();
        var config = DemoScene.Build();
        var own = Path.Combine(root, "corvid");
        Corvid(config).Logging.Directory = own;
        try
        {
            var (app, opened) = App(root, config);

            // F9 is F5 with the cursor on the selected character.
            app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F9, false, false, false));
            await Assert.That(app.SettingsOpenKey).IsEqualTo(ConsoleKey.F9);
            app.SimulateSettingsKey(new ConsoleKeyInfo('o', ConsoleKey.O, false, false, false));

            await Assert.That(opened).IsEquivalentTo(new[] { Path.GetFullPath(own) });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task TheWindowsCharacterPageOpensTheCharactersFolderThroughTheClient()
    {
        var root = TempRoot();
        var config = DemoScene.Build();
        var own = Path.Combine(root, "corvid");
        var corvid = Corvid(config);
        corvid.Logging.Directory = own;
        try
        {
            var native = new Presenter();
            var (app, opened) = App(root, config, native: native);
            app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false));
            var form = native.Shown.Single();
            await Assert.That(form.CanOpenLogFolder).IsTrue();

            var world = config.Worlds.First(w => w.Characters.Contains(corvid));
            await Assert.That(form.OpenLogFolderOf(world, corvid)).IsTrue();
            await Assert.That(opened).IsEquivalentTo(new[] { Path.GetFullPath(own) });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task TheWindowsCharacterPageOffersNoButtonWhereTheClientKeepsNoLogs()
    {
        var native = new Presenter();
        var (app, _) = App(logRoot: null, native: native);
        app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F5, false, false, false));
        await Assert.That(native.Shown.Single().CanOpenLogFolder).IsFalse();
    }

    [Test]
    public async Task TheOpenerTakesOnlyAFolderThatExists()
    {
        await Assert.That(FolderOpener.TryParseFolder(Path.GetTempPath(), out _)).IsTrue();
        await Assert.That(FolderOpener.TryParseFolder("relative/logs", out _)).IsFalse();
        await Assert.That(FolderOpener.TryParseFolder(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), out _)).IsFalse();
        var file = Path.GetTempFileName();
        try
        {
            await Assert.That(FolderOpener.TryParseFolder(file, out _)).IsFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }
}
