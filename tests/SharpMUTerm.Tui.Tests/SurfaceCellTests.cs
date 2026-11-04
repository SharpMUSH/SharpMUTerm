using System.Reflection;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Surfaces;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The terminal's half of the surface catalog (<see cref="SurfaceCatalog"/>): every surface drawn in cells is drawn
/// by a renderer that takes the surface's model, every command a surface raises is one the registry runs, and every
/// action ends in a client method that exists. The window's half is <c>SurfaceParityTests</c>.
/// </summary>
[NotInParallel]
public class SurfaceCellTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly Assembly Tui = typeof(SharpMUTermApp).Assembly;

    /// <summary>
    /// A cell renderer that stopped taking the model — reading the app's state itself — could say something the
    /// native view does not. The renderer is named in the catalog and one of its overloads must take the model.
    /// </summary>
    [Test]
    public async Task EverySurfaceDrawnInCellsHasACellRendererThatTakesItsModel()
    {
        foreach (var surface in SurfaceCatalog.All.Where(s => s.HasCells))
        {
            var name = surface.CellRenderer!;
            var dot = name.LastIndexOf('.');
            var type = Tui.GetType(name[..dot]);
            await Assert.That(type).IsNotNull().Because($"{surface.Id}: {name[..dot]} is not a type in the terminal client");
            var overloads = type!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                .Where(m => m.Name == name[(dot + 1)..])
                .ToList();
            await Assert.That(overloads).IsNotEmpty().Because($"{surface.Id}: {name} does not exist");
            await Assert.That(overloads.Any(m => m.GetParameters().Any(p => p.ParameterType == surface.Model)))
                .IsTrue().Because($"{surface.Id}: no overload of {name} takes a {surface.Model.Name}");
        }
    }

    /// <summary>Every action names the client method both views end in, and that method is there to end in.</summary>
    [Test]
    public async Task EveryActionReachesAClientMethodThatExists()
    {
        foreach (var surface in SurfaceCatalog.All)
        {
            foreach (var action in surface.Actions)
            {
                var dot = action.Reaches.LastIndexOf('.');
                var type = dot < 0 ? typeof(SharpMUTermApp) : Tui.GetType("SharpMUTerm.Tui." + action.Reaches[..dot]);
                var method = dot < 0 ? action.Reaches : action.Reaches[(dot + 1)..];
                var found = type?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
                    .Any(m => m.Name == method) ?? false;
                await Assert.That(found).IsTrue().Because($"{surface.Id}: {action.Reaches}");
            }
        }
    }

    /// <summary>A command a surface raises, or names as its keyboard path, is one the registry runs.</summary>
    [Test]
    public async Task EveryCommandASurfaceRaisesResolvesThroughTheRegistry()
    {
        Console.SetIn(TextReader.Null);
        using var driver = new HeadlessConsoleDriver(120, 34);
        await using var app = new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(driver, Caps));

        var ids = SurfaceCatalog.All.SelectMany(s => s.Actions.Where(a => a.Kind == SurfaceActionKind.Command).Select(a => a.Name))
            .Concat(SurfaceCatalog.All.SelectMany(s => s.Actions.Select(a => a.CellsVia)).OfType<string>().Where(v => SurfaceCatalog.Find(v) is null))
            .Concat(SurfaceCatalog.All.Select(s => s.KeyboardPath).OfType<string>())
            .Distinct(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            await Assert.That(app.Contributions.Commands.Resolve(id)).IsNotNull().Because(id);
        }
    }

    /// <summary>
    /// The terminal's verdicts, read through the app's own host: ⌃3 is the Escape byte here and never fires, ⌥⏎ is
    /// the newline, ⌃⇧←/→ are kitty's; and a macro on ⌃3 is dead on F4 and is not dispatched by the key path.
    /// </summary>
    [Test]
    public async Task F4TellsTheTerminalsTruth()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        await using var app = new SharpMUTermApp(config, ClientHost.Headless(new HeadlessConsoleDriver(120, 34), Caps));
        app.BindWorldWithoutConnecting(config.Worlds[0]);
        var keys = app.Host.Keys;

        await Assert.That(MacroKeys.Verdict(keys, "Ctrl+3").Delivery).IsEqualTo(MacroKeyDelivery.NeverArrives);
        await Assert.That(MacroKeys.Verdict(keys, "Ctrl+3").Reason).Contains("Escape");
        await Assert.That(MacroKeys.Verdict(keys, "Alt+Enter").Delivery).IsEqualTo(MacroKeyDelivery.Taken);
        await Assert.That(MacroKeys.Verdict(keys, "Alt+Enter").Reason).IsEqualTo("Alt+⏎ is the newline");
        await Assert.That(MacroKeys.Verdict(keys, "Ctrl+Shift+Left").Fires).IsFalse();
        await Assert.That(MacroKeys.Verdict(keys, "Ctrl+Shift+A").Fires).IsFalse();
        await Assert.That(MacroKeys.Verdict(keys, "Alt+O").Fires).IsFalse();

        var macro = config.TriggerSets.SelectMany(s => s.Macros).Single(m => m.Key == "Ctrl+F1");
        macro.Key = "Ctrl+3";
        await Assert.That(app.SimulateKey(new ConsoleKeyInfo('3', ConsoleKey.D3, false, false, true))).IsNull();

        var hotkeys = string.Join('\n', KeypadScreenRenderer.HotkeysColumn(keys, config.TriggerSets.SelectMany(s => s.Macros).ToList()));
        await Assert.That(hotkeys).Contains("▲ ⌃3");
    }
}
