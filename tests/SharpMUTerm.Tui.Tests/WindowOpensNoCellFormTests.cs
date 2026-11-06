using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Composer;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Client.Palette;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The audit behind "nothing in the window opens a terminal form": with every native host attached (palette, settings,
/// composer, modals), every ⌃P command and every application chord is run on a fresh client, and none may leave a
/// cell-drawn form open (<see cref="SharpMUTermApp.CellSurfaceOpen"/>). A surface added later that forgets its native
/// side fails here by name.
/// </summary>
[NotInParallel]
public class WindowOpensNoCellFormTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class Modals : INativeModals
    {
        public bool Available => true;

        public List<ModalSession> Shown { get; } = [];

        public void Show(ModalSession session) => Shown.Add(session);

        public void Close(ModalSession session)
        {
        }
    }

    private sealed class Palette : INativePalette
    {
        public void Show(PaletteSession session)
        {
        }

        public void Close()
        {
        }
    }

    private sealed class Settings : INativeSettings
    {
        public void Show(SettingsForm form)
        {
        }

        public void Close()
        {
        }
    }

    private sealed class Composer : INativeComposer
    {
        public bool Available => true;

        public void Open(ComposerSession session, PanelInk ink)
        {
        }

        public void Close()
        {
        }
    }

    private static ClientHost NativeHost() =>
        ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null) with
        {
            NativeModals = new Modals(),
            NativePalette = new Palette(),
            NativeSettings = new Settings(),
            NativeComposer = new Composer(),
        };

    /// <summary>The demo scene in a window, with <paramref name="view"/>'s state: maps, panels, scenes, captures.</summary>
    private static SharpMUTermApp Demo(string? view)
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(DemoScene.Build(), NativeHost());
        app.RenderSnapshot(view);
        return app;
    }

    internal static async Task<SharpMUTermApp> Window(ManualTimeProvider? clock = null)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        world.Characters.Add(new CharacterDefinition { Name = "Bea", Logging = new LoggingSettings() });
        config.Worlds.Add(world);
        var app = new SharpMUTermApp(config, NativeHost(), clock);
        app.TelnetFactory = _ => new RecordingTelnetSession();
        await app.BindWorldWithoutConnecting(world).ConnectAsync();
        app.RenderNextFrame();
        return app;
    }

    [Test]
    public async Task NoCommandOpensACellForm()
    {
        List<string> ids;
        await using (var probe = await Window())
        {
            ids = probe.BuildCatalog().Select(i => i.Id).Distinct().ToList();
        }

        var opened = new List<string>();
        foreach (var id in ids)
        {
            var app = await Window();
            await using var _ = app;
            app.DispatchCommand(id);
            app.RenderNextFrame();
            if (app.CellSurfaceOpen is { } open)
            {
                opened.Add($"{id} → {open}");
            }
        }

        await Assert.That(opened).IsEmpty().Because(string.Join("\n", opened));
    }

    [Test]
    [Arguments(null)]
    [Arguments("layout-combat")]
    [Arguments("layout-roleplay")]
    [Arguments("characters")]
    public async Task NoCommandInTheDemoOpensACellForm(string? view)
    {
        List<string> ids;
        await using (var probe = Demo(view))
        {
            ids = probe.BuildCatalog().Select(i => i.Id).Distinct().ToList();
        }

        var opened = new List<string>();
        foreach (var id in ids.Where(i => i != "term:quit"))
        {
            var app = Demo(view);
            await using var _ = app;
            if (app.CellSurfaceOpen is not null)
            {
                continue; // the view itself is a form; nothing to learn
            }

            app.DispatchCommand(id);
            app.RenderNextFrame();
            if (app.CellSurfaceOpen is { } open)
            {
                opened.Add($"{id} → {open}");
            }
        }

        await Assert.That(opened).IsEmpty().Because(string.Join("\n", opened));
    }

    [Test]
    public async Task NoChordOpensACellForm()
    {
        var keys = MacroKeys.AppShortcuts.Select(s => new ConsoleKeyInfo(
                '\0', s.Key, s.Modifiers.HasFlag(ConsoleModifiers.Shift), s.Modifiers.HasFlag(ConsoleModifiers.Alt),
                s.Modifiers.HasFlag(ConsoleModifiers.Control)))
            .Concat(Enumerable.Range(0, 12).Select(n => new ConsoleKeyInfo('\0', ConsoleKey.F1 + n, false, false, false)))
            .ToList();

        var opened = new List<string>();
        foreach (var key in keys)
        {
            var clock = new ManualTimeProvider();
            var app = await Window(clock);
            await using var _ = app;
            app.SimulateKey(key);
            clock.Advance(TimeSpan.FromSeconds(2));
            app.RenderNextFrame();
            if (app.CellSurfaceOpen is { } open)
            {
                opened.Add($"{key.Modifiers}+{key.Key} → {open}");
            }
        }

        await Assert.That(opened).IsEmpty().Because(string.Join("\n", opened));
    }
}
