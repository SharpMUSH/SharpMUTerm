using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Logging;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The script console, GMCP channel capture, highlighted names and the scene log, each driven through a connected
/// session the way a player reaches it.
/// </summary>
[NotInParallel]
public class ScriptingWaveTests
{
    private const string Ann = "Convergence.Ann";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static AppConfiguration Config(Action<WorldDefinition>? world = null)
    {
        var set = new TriggerSet { Name = "Lua", ScriptFiles = { "main.lua" } };
        var config = new AppConfiguration();
        config.TriggerSets.Add(set);
        var definition = new WorldDefinition
        {
            Name = "Convergence",
            Host = "convergence.example",
            Port = 4201,
            Characters = { new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings(), TriggerSets = { "Lua" } } },
        };
        world?.Invoke(definition);
        config.Worlds.Add(definition);
        return config;
    }

    private static async Task<(SharpMUTermApp App, RecordingTelnetSession Wire)> Open(
        AppConfiguration config, string? scriptsRoot = null, string? logRoot = null, Action<string>? clipboard = null)
    {
        Console.SetIn(TextReader.Null);
        var wire = new RecordingTelnetSession();
        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32), logRoot: logRoot, scriptsRoot: scriptsRoot, clipboard: clipboard);
        app.TelnetFactory = _ => wire;
        if (!app.DispatchCommand(CommandIds.Character(Ann)))
        {
            throw new InvalidOperationException("the app would not switch to Ann");
        }

        await app.FindSession(Ann)!.ConnectAsync();
        app.RenderNextFrame();
        return (app, wire);
    }

    private static string Root(string lua)
    {
        var root = Directory.CreateTempSubdirectory("sharpmuterm-console-").FullName;
        File.WriteAllText(Path.Combine(root, "main.lua"), lua);
        return root;
    }

    private static void Enter(SharpMUTermApp app, string line)
    {
        app.SimulateConsoleTyping(line);
        app.SimulateConsoleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
    }

    [Test]
    public async Task TheConsoleRunsALineInTheCharactersSandboxAndShowsWhatItPrinted()
    {
        var root = Root("function greet(n) print('hello ' .. n) end\nstore.set('kills', 3)");
        try
        {
            var (app, _) = await Open(Config(), scriptsRoot: root);
            await using var _app = app;

            await Assert.That(app.DispatchCommand(BuiltInCommands.ScriptConsoleCommand)).IsTrue();
            await Assert.That(app.ScriptConsoleIsOpen).IsTrue();
            await Assert.That(app.CellConsole!.Model.CanRun).IsTrue();
            await Assert.That(app.CellConsole!.Model.Store.Select(e => e.Key)).Contains("kills");

            Enter(app, "1 + 1");
            Enter(app, "greet('Ann')");
            Enter(app, "error('boom')");

            var lines = app.CellConsole!.Model.Lines;
            await Assert.That(lines).Contains(new ScriptConsoleLine(ScriptConsoleKind.Result, "2"));
            await Assert.That(lines).Contains(new ScriptConsoleLine(ScriptConsoleKind.Output, "hello Ann"));
            await Assert.That(lines.Any(l => l.Kind == ScriptConsoleKind.Error && l.Text.Contains("boom"))).IsTrue();
            await Assert.That(app.CellConsole!.Model.History).IsEquivalentTo(new[] { "1 + 1", "greet('Ann')", "error('boom')" });
            await Assert.That(string.Join("\n", app.ConsoleLines)).Contains("hello Ann");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ALoopTypedAtThePromptIsStoppedAndTheConsoleStaysUsable()
    {
        var root = Root("-- nothing");
        try
        {
            var (app, _) = await Open(Config(), scriptsRoot: root);
            await using var _app = app;
            app.DispatchCommand(BuiltInCommands.ScriptConsoleCommand);

            Enter(app, "while true do end");
            Enter(app, "40 + 2");

            var lines = app.CellConsole!.Model.Lines;
            await Assert.That(lines.Any(l => l.Kind == ScriptConsoleKind.Error)).IsTrue();
            await Assert.That(lines).Contains(new ScriptConsoleLine(ScriptConsoleKind.Result, "42"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DeletingAStoreKeyAndReloadingGoThroughTheConsole()
    {
        var root = Root("store.set('a', 1)\nstore.set('b', 2)");
        try
        {
            var (app, _) = await Open(Config(), scriptsRoot: root);
            await using var _app = app;
            app.DispatchCommand(BuiltInCommands.ScriptConsoleCommand);

            app.DeleteStoreKey("a");
            await Assert.That(app.CellConsole!.Model.Store.Select(e => e.Key)).IsEquivalentTo(new[] { "b" });

            File.WriteAllText(Path.Combine(root, "main.lua"), "print('v2 loaded')");
            app.SimulateConsoleKey(new ConsoleKeyInfo('\f', ConsoleKey.L, false, false, true));

            var lines = app.CellConsole!.Model.Lines;
            await Assert.That(lines.Any(l => l.Kind == ScriptConsoleKind.Note && l.Text.StartsWith("reloaded", StringComparison.Ordinal))).IsTrue();
            await Assert.That(lines).Contains(new ScriptConsoleLine(ScriptConsoleKind.Output, "v2 loaded"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task AScriptErrorReachesTheConsoleWithItsFileAndLine()
    {
        var root = Root("local x = 1\n\nnil_call()");
        try
        {
            var (app, _) = await Open(Config(), scriptsRoot: root);
            await using var _app = app;
            app.DispatchCommand(BuiltInCommands.ScriptConsoleCommand);

            var errors = app.CellConsole!.Model.Lines.Where(l => l.Kind == ScriptConsoleKind.Error).ToArray();
            await Assert.That(errors.Any(e => e.Text.Contains("main.lua:(3", StringComparison.Ordinal) || e.Text.Contains("main.lua:3", StringComparison.Ordinal))).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A client that runs no scripts still opens the console, and it says why there is no prompt.</summary>
    [Test]
    public async Task WithNoScriptsRootTheConsoleSaysWhyAndRunsNothing()
    {
        var (app, _) = await Open(Config(), scriptsRoot: null);
        await using var _app = app;

        await Assert.That(app.DispatchCommand(BuiltInCommands.ScriptConsoleCommand)).IsTrue();
        await Assert.That(app.CellConsole!.Model.CanRun).IsFalse();
        await Assert.That(app.CellConsole!.Model.Unavailable!).Contains("runs no scripts");

        Enter(app, "1 + 1");
        await Assert.That(app.CellConsole!.Model.Lines).IsEmpty();
    }

    [Test]
    public async Task AChannelMessageOpensAWindowForItsChannelAndTheMainCopyStays()
    {
        var (app, wire) = await Open(Config(w => w.CaptureChannels = ChannelCaptureMode.Copy));
        await using var _app = app;

        wire.ReceiveGmcp("Comm.Channel.Text", """{"channel":"ooc","talker":"Rivane","text":"[OOC] Rivane: anyone around?"}""");
        wire.Receive("[OOC] Rivane: anyone around?\n");

        var spawn = app.WindowIds().Single(id => id.StartsWith("spawn:", StringComparison.Ordinal));
        await Assert.That(spawn).Contains("ooc");
        await Assert.That(app.PaneLines(spawn).Any(l => l.Contains("anyone around?", StringComparison.Ordinal))).IsTrue();
        await Assert.That(app.PaneLines(app.ActiveWindowId()).Any(l => l.Contains("anyone around?", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task MoveModeTakesTheEchoOutOfTheMainWindow()
    {
        var (app, wire) = await Open(Config(w => w.CaptureChannels = ChannelCaptureMode.Move));
        await using var _app = app;

        wire.ReceiveGmcp("Comm.Channel.Text", """{"channel":"ooc","talker":"Rivane","text":"[OOC] Rivane: hello"}""");
        wire.Receive("[OOC] Rivane: hello\n");
        wire.Receive("The fountain splashes.\n");

        var main = app.PaneLines(app.ActiveWindowId());
        await Assert.That(main.Any(l => l.Contains("Rivane: hello", StringComparison.Ordinal))).IsFalse();
        await Assert.That(main.Any(l => l.Contains("fountain", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task WithCaptureOffAChannelMessageOpensNoWindow()
    {
        var (app, wire) = await Open(Config());
        await using var _app = app;

        wire.ReceiveGmcp("Comm.Channel.Text", """{"channel":"ooc","talker":"Rivane","text":"hi"}""");

        await Assert.That(app.WindowIds().Any(id => id.StartsWith("spawn:", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task AHighlightedNameIsColouredInTheMainWindowAsAWholeWord()
    {
        var (app, wire) = await Open(Config(w => w.HighlightNames = [new NameHighlight("Ashlyn", "red")]));
        await using var _app = app;

        wire.Receive("Ashlyn waves to Ashlynn.\n");

        var line = app.PaneLines(app.ActiveWindowId()).Single(l => l.Contains("waves", StringComparison.Ordinal));
        await Assert.That(line).Contains("Ashlyn[/]");
        await Assert.That(line).DoesNotContain("Ashlynn[/]");
    }

    [Test]
    public async Task ASceneLogRecordsTheWindowWithAHeaderAndRefusesASecondStart()
    {
        var logs = Directory.CreateTempSubdirectory("sharpmuterm-scene-").FullName;
        try
        {
            var config = Config();
            config.SceneLog = new SceneLogSettings { Html = false, Content = SceneLogContent.Everything };
            var (app, wire) = await Open(config, logRoot: logs);
            await using var _app = app;

            await Assert.That(app.DispatchCommand(BuiltInCommands.SceneStartCommand)).IsTrue();
            var log = app.SceneLogOf(app.ActiveWindowId());
            await Assert.That(log).IsNotNull();
            await Assert.That(app.BuildCatalog().Select(i => i.Id)).Contains(BuiltInCommands.SceneStopCommand);

            wire.Receive("Rivane says, \"Evening.\"\n");
            wire.Receive("The fountain splashes.\n");
            await Assert.That(app.DispatchCommand(BuiltInCommands.SceneStopCommand)).IsTrue();
            await Assert.That(app.SceneLogOf(app.ActiveWindowId())).IsNull();

            var text = File.ReadAllText(log!.Path);
            await Assert.That(text).Contains("Present: Ann");
            await Assert.That(text).Contains("Evening.");
            await Assert.That(text).Contains("The fountain splashes.");
            await Assert.That(text).Contains("Ended");
            await Assert.That(Path.GetDirectoryName(log.Path)!).IsEqualTo(Path.Combine(logs, "scenes"));
        }
        finally
        {
            Directory.Delete(logs, recursive: true);
        }
    }

    [Test]
    public async Task PosesOnlyLeavesOutTheRoomsOwnLines()
    {
        var logs = Directory.CreateTempSubdirectory("sharpmuterm-scene-").FullName;
        try
        {
            var config = Config();
            config.SceneLog = new SceneLogSettings { Html = true, Content = SceneLogContent.Poses };
            var (app, wire) = await Open(config, logRoot: logs);
            await using var _app = app;

            app.DispatchCommand(BuiltInCommands.SceneStartCommand);
            var log = app.SceneLogOf(app.ActiveWindowId())!;
            wire.Receive("Ann says, \"Evening.\"\n");
            wire.Receive("The fountain splashes.\n");
            app.DispatchCommand(BuiltInCommands.SceneStopCommand);

            var text = File.ReadAllText(log.Path);
            await Assert.That(log.Path).EndsWith(".html");
            await Assert.That(text).Contains("Evening.");
            await Assert.That(text).DoesNotContain("fountain");
        }
        finally
        {
            Directory.Delete(logs, recursive: true);
        }
    }

    /// <summary>
    /// While a scene records, the status row and the header model say so; the saved notice offers Open and Copy path,
    /// both client commands naming the scene by index, and Open refuses out loud in a host that opens no files.
    /// </summary>
    [Test]
    public async Task ARecordingSceneIsMarkedAndItsSavedNoticeOffersOpenAndCopyPath()
    {
        var logs = Directory.CreateTempSubdirectory("sharpmuterm-scene-").FullName;
        try
        {
            var copied = new List<string>();
            var (app, wire) = await Open(Config(), logRoot: logs, clipboard: copied.Add);
            await using var _app = app;
            await Assert.That(MarkupText.Plain(app.StatusMarkup)).DoesNotContain(SharpMUTermApp.RecordingCell);

            app.DispatchCommand(BuiltInCommands.SceneStartCommand);
            var log = app.SceneLogOf(app.ActiveWindowId())!;
            await Assert.That(app.BuildHeaderModel().Recording).IsTrue();
            await Assert.That(app.LastNoticeModel!.Source).IsEqualTo(SharpMUTerm.Client.Chrome.NoticeModel.Sources.Scene);
            await Assert.That(app.LastNoticeModel!.Key).IsNull().Because("no bare chip");

            // The notice covers the row for a while; once it goes, the resting row carries the mark.
            app.ClearNoticeForTest();
            await Assert.That(MarkupText.Plain(app.StatusMarkup)).Contains(SharpMUTermApp.RecordingCell);

            wire.Receive("Ann says, \"Evening.\"\n");
            app.DispatchCommand(BuiltInCommands.SceneStopCommand);
            await Assert.That(app.BuildHeaderModel().Recording).IsFalse();
            var saved = app.LastNoticeModel!;
            await Assert.That(saved.Action!.Label).IsEqualTo("Open");
            await Assert.That(saved.Second!.Label).IsEqualTo("Copy path");
            await Assert.That(saved.Action.Command).DoesNotContain(logs).Because("a command id names a scene by index, never by path");

            await Assert.That(app.DispatchCommand(saved.Second.Command)).IsTrue();
            await Assert.That(copied).IsEquivalentTo(new[] { log.Path });

            await Assert.That(app.DispatchCommand(saved.Action.Command)).IsTrue();
            await Assert.That(app.StatusMarkup).Contains("opens no files");

            await Assert.That(app.DispatchCommand(BuiltInCommands.SceneOpenPrefix + "7")).IsTrue();
            await Assert.That(app.StatusMarkup).Contains("no such saved scene");
        }
        finally
        {
            Directory.Delete(logs, recursive: true);
        }
    }

    /// <summary>The file opener's own gate: only an existing log file, never a script or a relative path.</summary>
    [Test]
    public async Task TheFileOpenerTakesOnlyALogFile()
    {
        var dir = Directory.CreateTempSubdirectory("sharpmuterm-open-").FullName;
        try
        {
            var html = Path.Combine(dir, "scene.html");
            var script = Path.Combine(dir, "launch.desktop");
            File.WriteAllText(html, "<p>x</p>");
            File.WriteAllText(script, "[Desktop Entry]");

            await Assert.That(ExternalBrowser.TryParseOpenableFile(html, out var full)).IsTrue();
            await Assert.That(full).IsEqualTo(html);
            await Assert.That(ExternalBrowser.TryParseOpenableFile(script, out _)).IsFalse();
            await Assert.That(ExternalBrowser.TryParseOpenableFile("scene.html", out _)).IsFalse();
            await Assert.That(ExternalBrowser.TryParseOpenableFile(Path.Combine(dir, "gone.txt"), out _)).IsFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>An app with no log root — every test, every snapshot — refuses out loud and writes nothing.</summary>
    [Test]
    public async Task WithNoLogRootTheSceneLogRefuses()
    {
        var (app, _) = await Open(Config(), logRoot: null);
        await using var _app = app;

        app.DispatchCommand(BuiltInCommands.SceneStartCommand);

        await Assert.That(app.SceneLogOf(app.ActiveWindowId())).IsNull();
        await Assert.That(app.StatusMarkup).Contains("no scene log");
    }
}
