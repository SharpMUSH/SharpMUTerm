using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The script editing loop from the client's side: ⌃P lists the character's scripts, one opens in the composer's script
/// mode (lines verbatim, no escaping, no spelling), ⌃S writes it under the scripts folder and reloads that character's
/// scripts, and a reload's error is said in the editor's header with its file and line and kept in the console. A
/// package's script is only read.
/// </summary>
[NotInParallel]
public class ScriptEditLoopTests
{
    private const string Ann = "Convergence.Ann";

    private static readonly ConsoleKeyInfo CtrlS = new('\u0013', ConsoleKey.S, false, false, true);
    private static readonly ConsoleKeyInfo Esc = new('\u001b', ConsoleKey.Escape, false, false, false);

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class World : IAsyncDisposable
    {
        private World(string root, SharpMUTermApp app, RecordingTelnetSession wire)
        {
            Root = root;
            App = app;
            Wire = wire;
        }

        public string Root { get; }

        public SharpMUTermApp App { get; }

        public RecordingTelnetSession Wire { get; }

        public static async Task<World> Open(string lua)
        {
            var root = Directory.CreateTempSubdirectory("sharpmuterm-scriptedit-").FullName;
            File.WriteAllText(Path.Combine(root, "main.lua"), lua);
            var set = new TriggerSet { Name = "Lua", ScriptFiles = { "main.lua" } };
            set.Triggers.Add(new Trigger { Name = "ping", Pattern = "^ping$", Actions = new TriggerActions { ScriptCallback = "on_ping" } });
            var config = new AppConfiguration();
            config.TriggerSets.Add(set);
            config.Worlds.Add(new WorldDefinition
            {
                Name = "Convergence",
                Host = "convergence.example",
                Port = 4201,
                Characters = { new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings(), TriggerSets = { "Lua" } } },
            });

            Console.SetIn(TextReader.Null);
            var wire = new RecordingTelnetSession();
            var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32), scriptsRoot: root);
            app.TelnetFactory = _ => wire;
            app.DispatchCommand(CommandIds.Character(Ann));
            await app.FindSession(Ann)!.ConnectAsync();
            app.RenderNextFrame();
            return new World(root, app, wire);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            Directory.Delete(Root, recursive: true);
        }
    }

    [Test]
    public async Task EditScriptListsTheCharactersScriptsOnThePalette()
    {
        await using var world = await World.Open("function on_ping() world.send('v1') end");
        File.WriteAllText(Path.Combine(world.Root, "extra.lua"), "-- not loaded");

        await Assert.That(world.App.DispatchCommand(BuiltInCommands.ScriptEditCommand)).IsTrue();

        var ids = world.App.BuildCatalog().Select(i => i.Id).ToList();
        await Assert.That(ids).Contains(BuiltInCommands.ScriptOpen("main.lua"));
        await Assert.That(ids).Contains(BuiltInCommands.ScriptOpen("extra.lua"));
        var main = world.App.BuildCatalog().Single(i => i.Id == BuiltInCommands.ScriptOpen("main.lua"));
        await Assert.That(main.Detail).Contains("loaded by this character");
    }

    [Test]
    public async Task AScriptOpensVerbatimWithNoEscapingOrSpelling()
    {
        await using var world = await World.Open("x = '%r [y]'\n");

        await Assert.That(world.App.DispatchCommand(BuiltInCommands.ScriptOpen("main.lua"))).IsTrue();

        await Assert.That(world.App.Composer.IsOpen).IsTrue();
        await Assert.That(world.App.Composer.Script!.Name).IsEqualTo("main.lua");
        await Assert.That(world.App.Composer.Body).StartsWith("x = '%r [y]'");
        await Assert.That(world.App.Composer.Spelling).IsNull();
        await Assert.That(world.App.Composer.Model.Edit!.Script).IsTrue();
        await Assert.That(world.App.Composer.Model.Keys.Select(k => k.Key)).Contains("⌃S");
        await Assert.That(world.App.Composer.Model.Keys.Select(k => k.Key)).DoesNotContain("⌥L");
    }

    [Test]
    public async Task CtrlSSavesTheFileAndReloadsTheCharactersScripts()
    {
        await using var world = await World.Open("function on_ping() world.send('v1') end");
        world.App.DispatchCommand(BuiltInCommands.ScriptOpen("main.lua"));
        world.App.Composer.SimulateTyping("\nfunction on_ping() world.send('v2') end\n");

        await Assert.That(world.App.Composer.SimulateKey(CtrlS)).IsTrue();

        // Saved verbatim, nothing sent to the game, the editor still open on the file, and the new function live.
        await Assert.That(File.ReadAllText(Path.Combine(world.Root, "main.lua"))).Contains("world.send('v2')");
        await Assert.That(world.Wire.Lines.Any(l => l.Contains("on_ping", StringComparison.Ordinal))).IsFalse();
        await Assert.That(world.App.Composer.IsOpen).IsTrue();
        await Assert.That(world.App.Composer.Script!.Status).Contains("reloaded");
        world.Wire.Receive("ping");
        await Assert.That(world.Wire.Lines).Contains("v2");
        await Assert.That(world.App.ConsoleTexts(Ann, ScriptConsoleKind.Note).Any(t => t.Contains("saved main.lua", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task AReloadErrorIsSaidWithItsFileAndLineAndKeptInTheConsole()
    {
        await using var world = await World.Open("function on_ping() world.send('v1') end");
        world.App.DispatchCommand(BuiltInCommands.ScriptOpen("main.lua"));
        world.App.Composer.SimulateTyping("\n\nfunction broken(\n");

        world.App.Composer.SimulateKey(CtrlS);

        var status = world.App.Composer.Script!.Status!;
        await Assert.That(status).StartsWith("!");
        await Assert.That(status).Contains("main.lua");
        await Assert.That(world.App.Composer.HeaderMarkup).Contains("main.lua:");
        await Assert.That(world.App.ConsoleTexts(Ann, ScriptConsoleKind.Error).Any(t => t.Contains("main.lua", StringComparison.Ordinal))).IsTrue();

        // Opened again from ⌃P, the editor lands on the line the error named.
        world.App.Composer.SimulateKey(Esc);
        world.App.DispatchCommand(BuiltInCommands.ScriptOpen("main.lua"));
        await Assert.That(world.App.Composer.Script!.Line).IsGreaterThan(0);
    }

    [Test]
    public async Task EscKeepsUnsavedTextForTheFileUntilItIsSaved()
    {
        await using var world = await World.Open("x = 1\n");
        world.App.DispatchCommand(BuiltInCommands.ScriptOpen("main.lua"));
        world.App.Composer.SimulateTyping("y = 2\n");

        world.App.Composer.SimulateKey(Esc);

        await Assert.That(world.App.Composer.IsOpen).IsFalse();
        await Assert.That(world.App.KeptScriptText("main.lua")).Contains("y = 2");
        await Assert.That(File.ReadAllText(Path.Combine(world.Root, "main.lua"))).IsEqualTo("x = 1\n");
        world.App.DispatchCommand(BuiltInCommands.ScriptOpen("main.lua"));
        await Assert.That(world.App.Composer.Body).Contains("y = 2");
    }

    [Test]
    public async Task ANewScriptIsCreatedByNameAndARefusedNameWritesNothing()
    {
        await using var world = await World.Open("x = 1\n");

        world.App.NewScript("../outside");
        await Assert.That(world.App.Composer.IsOpen).IsFalse();
        await Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(world.Root)!, "outside.lua"))).IsFalse();

        world.App.NewScript("lib/helpers");
        await Assert.That(world.App.Composer.IsOpen).IsTrue();
        await Assert.That(world.App.Composer.Script!.Name).IsEqualTo("lib/helpers.lua");
        world.App.Composer.SimulateKey(CtrlS);
        await Assert.That(File.Exists(Path.Combine(world.Root, "lib", "helpers.lua"))).IsTrue();
        await Assert.That(world.App.Composer.Script!.Status).Contains("not loaded");
    }

    [Test]
    public async Task APackagesScriptIsOnlyRead()
    {
        Console.SetIn(TextReader.Null);
        await using var demo = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(120, 32));
        demo.RenderSnapshot();

        demo.OpenScript("package:aetherfall-pack/combat.lua");

        await Assert.That(demo.Composer.IsOpen).IsTrue();
        await Assert.That(demo.Composer.Script!.ReadOnly).IsTrue();
        await Assert.That(demo.Composer.Model.Keys.Select(k => k.Key)).DoesNotContain("⌃S");
        await Assert.That(demo.Composer.HeaderMarkup).Contains("READ ONLY");
        await Assert.That(demo.Composer.SimulateKey(CtrlS)).IsTrue();
        await Assert.That(demo.Composer.IsOpen).IsTrue();
    }
}
