using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Lua from the client's side: a character whose trigger set names a file gets that file run, and a rule's
/// <c>script</c> field calls into it. The changelog said "sandboxed Lua scripting" from the first release and
/// nothing in the app ever built a script host, so these are asserted over a connected session rather than
/// against the host alone.
/// </summary>
[NotInParallel]
public class ScriptsEndToEndTests
{
    private const string Ann = "Convergence.Ann";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static AppConfiguration Config()
    {
        var set = new TriggerSet { Name = "Lua", ScriptFiles = { "main.lua" } };
        set.Triggers.Add(new Trigger
        {
            Name = "ping",
            Pattern = "^ping$",
            Actions = new TriggerActions { ScriptCallback = "on_ping" },
        });
        set.Macros.Add(new Macro { Name = "scripted", Key = "F12", ScriptCallback = "on_key" });

        var config = new AppConfiguration();
        config.TriggerSets.Add(set);
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Convergence",
            Host = "convergence.example",
            Port = 4201,
            Characters = { new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings(), TriggerSets = { "Lua" } } },
        });
        return config;
    }

    private static async Task<(SharpMUTermApp App, RecordingTelnetSession Wire)> Open(string? root)
    {
        Console.SetIn(TextReader.Null);
        var wire = new RecordingTelnetSession();
        var app = new SharpMUTermApp(Config(), Headless, new HeadlessConsoleDriver(120, 32), scriptsRoot: root);
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
        var root = Directory.CreateTempSubdirectory("sharpmuterm-scripts-").FullName;
        File.WriteAllText(Path.Combine(root, "main.lua"), lua);
        return root;
    }

    [Test]
    public async Task AConfiguredTriggerRunsTheFunctionItNames()
    {
        var root = Root("function on_ping() world.send('pong') end");
        try
        {
            var (app, wire) = await Open(root);
            await using var _ = app;

            wire.Receive("ping");

            await Assert.That(wire.Lines).Contains("pong");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>An app nobody gave a scripts folder reads no file and runs nothing, whatever the sets say.</summary>
    [Test]
    public async Task AnAppWithNoScriptsRootRunsNothing()
    {
        var (app, wire) = await Open(root: null);
        await using var _ = app;

        wire.Receive("ping");

        await Assert.That(wire.Lines).DoesNotContain("pong");
        await Assert.That(app.BuildCatalog().Select(i => i.Id)).DoesNotContain("world:reload-scripts");
    }

    [Test]
    public async Task ReloadScriptsReadsTheFileAgain()
    {
        var root = Root("function on_ping() world.send('v1') end");
        try
        {
            var (app, wire) = await Open(root);
            await using var _ = app;
            await Assert.That(app.BuildCatalog().Select(i => i.Id)).Contains("world:reload-scripts");

            File.WriteAllText(Path.Combine(root, "main.lua"), "function on_ping() world.send('v2') end");
            await Assert.That(app.DispatchCommand("world:reload-scripts")).IsTrue();
            wire.Receive("ping");

            await Assert.That(wire.Lines).Contains("v2");
            await Assert.That(wire.Lines).DoesNotContain("v1");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A script that fails says so in the character's own window, where the reader is looking.</summary>
    [Test]
    public async Task AScriptErrorIsPrintedInTheSessionsWindow()
    {
        var root = Root("function on_ping() error('boom') end");
        try
        {
            var (app, wire) = await Open(root);
            await using var _ = app;

            wire.Receive("ping");
            app.RenderNextFrame();

            var lines = app.FindSession(Ann)!.Scrollback.Snapshot().Select(l => l.ToString());
            await Assert.That(string.Join("\n", lines)).Contains("boom");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A macro that only names a function runs it from a real key press, through the key chain.</summary>
    [Test]
    public async Task AScriptOnlyMacroRunsItsFunctionFromAKeyPress()
    {
        var root = Root("function on_key() world.send('keyed') end");
        try
        {
            var (app, wire) = await Open(root);
            await using var _ = app;

            app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.F12, shift: false, alt: false, control: false));

            await Assert.That(wire.Lines).Contains("keyed");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>A reload that could not read its file says so, rather than reporting the file as loaded.</summary>
    [Test]
    public async Task AReloadThatLoadsNothingDoesNotSayItLoaded()
    {
        var root = Root("function on_ping() world.send('pong') end");
        try
        {
            var (app, _) = await Open(root);
            await using var __ = app;

            File.Delete(Path.Combine(root, "main.lua"));
            await Assert.That(app.DispatchCommand("world:reload-scripts")).IsTrue();

            var texts = app.Messages.Entries.Select(m => m.Text).ToArray();
            await Assert.That(texts.Any(t => t.Contains("0 of 1 scripts loaded"))).IsTrue();
            await Assert.That(texts.Any(t => t.EndsWith(": 1 script loaded"))).IsFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
