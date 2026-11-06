using System.Text.Json;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// <c>--emulated</c> is the terminal in a window: every surface in cells, nothing native laid over them. And the
/// shortcuts that start each mode side by side.
/// </summary>
public class EmulatedModeTests
{
    [Test]
    public async Task EmulatedDrawsNoChromeAndGuiIsTheDefault()
    {
        var emulated = WindowOptions.Parse(["--emulated", "example.org"]);
        await Assert.That(emulated.Emulated).IsTrue();
        await Assert.That(emulated.Chrome).IsEqualTo(HostChrome.None);
        await Assert.That(emulated.ClientArgs).IsEquivalentTo(new[] { "example.org" });

        var gui = WindowOptions.Parse(["--gui"]);
        await Assert.That(gui.Emulated).IsFalse();
        await Assert.That(gui.Chrome).IsEqualTo(HostChrome.Desktop);
        await Assert.That(gui.ClientArgs).IsEmpty();
    }

    [Test]
    public async Task AnEmulatedHostOffersTheClientNoNativeSurface()
    {
        var driver = new AvaloniaConsoleDriver(80, 24);
        var host = WindowHost.Create(driver, clipboard: null, HostChrome.Desktop, emulated: true);

        await Assert.That(host.Chrome).IsEqualTo(HostChrome.None);
        await Assert.That(host.NativeSettings).IsNull();
        await Assert.That(host.NativePalette).IsNull();
        await Assert.That(host.NativeComposer).IsNull();
        await Assert.That(host.NativeModals).IsNull();
        await Assert.That(host.FloatingPanels).IsNull();
        await Assert.That(host.FilePicker).IsNull();
        await Assert.That(driver.Pointer.CellsOnly).IsTrue();

        var gui = WindowHost.Create(new AvaloniaConsoleDriver(80, 24), clipboard: null);
        await Assert.That(gui.NativePalette).IsNotNull();
        await Assert.That(gui.NativeModals).IsNotNull();
    }

    [Test]
    public async Task AnEmulatedWindowLaysNoPanelOverTheCells()
    {
        var gui = Panels("--gui");
        var emulated = Panels("--emulated");

        // Combat's vitals, map and quick bar are native views in the window, and only cells in the emulated one.
        await Assert.That(gui).IsGreaterThan(0);
        await Assert.That(emulated).IsEqualTo(0);
    }

    [Test]
    public async Task ThePresenterDropsNativeViewsInCellsOnlyMode()
    {
        IPointerPresenter pointer = new WindowPointer { CellsOnly = true };
        pointer.Dividers([new PaneDividerOverlay(new PaneRect(0, 0, 1, 10), Vertical: true, Lit: false)]);
        await Assert.That(((WindowPointer)pointer).Dividers).IsEmpty();
        await Assert.That(pointer.VitalsCardRows(3, target: false)).IsNull();
    }

    [Test]
    public async Task TheInstallAddsEmulatedAndNativeBesideVelopacksOwnShortcut()
    {
        await Assert.That(LaunchShortcuts.Added.Select(l => l.Name))
            .IsEquivalentTo(new[] { "SharpMUTerm (Emulated)", "SharpMUTerm (Native)" });
        await Assert.That(LaunchShortcuts.Added[0].Executable).IsEqualTo("sharpmuterm-window.exe");
        await Assert.That(LaunchShortcuts.Added[0].Arguments).IsEqualTo("--emulated");
        await Assert.That(LaunchShortcuts.Added[1].Executable).IsEqualTo("sharpmuterm.exe");
    }

    [Test]
    public async Task TheTerminalProfileRunsTheNativeClientFromTheInstall()
    {
        var current = Path.Combine(Path.GetTempPath(), "Shar\"p", "current");
        using var json = JsonDocument.Parse(LaunchShortcuts.TerminalFragment(current));
        var profile = json.RootElement.GetProperty("profiles")[0];

        await Assert.That(profile.GetProperty("name").GetString()).IsEqualTo("SharpMUTerm");
        await Assert.That(profile.GetProperty("commandline").GetString())
            .IsEqualTo("\"" + Path.Combine(current, "sharpmuterm.exe") + "\"");
    }

    private static int Panels(string mode)
    {
        var options = WindowOptions.Parse(["--demo-config", mode, "--view", "layout-combat", "--size", "120x32"]);
        var config = ClientLaunch.LoadConfiguration(options, []);
        var driver = new AvaloniaConsoleDriver(120, 32);
        var app = ClientLaunch.Demo(options, config, driver, clipboard: null);
        new Thread(() => app.Run([])) { IsBackground = true }.Start();
        LiveLoop.Settle(driver);
        return driver.Pointer.Panels.Count;
    }
}
