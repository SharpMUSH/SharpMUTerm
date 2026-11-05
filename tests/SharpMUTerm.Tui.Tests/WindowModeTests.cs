using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Full screen and windowed full screen reach the host's window from ⌃P, the right-click menu and the Window settings
/// page; a host with no window (the terminal, a test) offers none of them and refuses them out loud.
/// </summary>
[NotInParallel]
public class WindowModeTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class FakeModes : IWindowModes
    {
        public List<WindowMode> Toggled { get; } = [];

        public bool Available => true;

        public WindowMode Mode { get; set; }

        public event Action? Changed;

        public void Toggle(WindowMode mode)
        {
            Toggled.Add(mode);
            Mode = Mode == mode ? WindowMode.Windowed : mode;
            Changed?.Invoke();
        }
    }

    private static SharpMUTermApp App(IWindowModes? modes)
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 40), null, null, null) with { WindowModes = modes };
        return new SharpMUTermApp(new AppConfiguration(), host);
    }

    [Test]
    public async Task AHostWithNoWindowListsNoWindowPageAndNoMode()
    {
        var app = App(null);
        var ids = app.BuildCatalog().Select(c => c.Id).ToArray();

        await Assert.That(ids).DoesNotContain(BuiltInCommands.FullScreenCommand);
        await Assert.That(ids).DoesNotContain(BuiltInCommands.WindowedFullScreenCommand);
        await Assert.That(app.Contributions.SettingsSections.Find(BuiltInSettings.Window.Id)).IsNull();
    }

    [Test]
    public async Task AHostWithNoWindowRefusesTheCommandOutLoud()
    {
        var app = App(null);
        await Assert.That(app.ToggleWindowMode(WindowMode.FullScreen)).IsFalse();
        await Assert.That(app.LastNoticeModel?.Text ?? "").Contains("full screen");
    }

    [Test]
    public async Task AWindowListsThePageAndBothModes_AndTheEntriesToggleTheWindow()
    {
        var modes = new FakeModes();
        var app = App(modes);

        await Assert.That(app.Contributions.SettingsSections.Find(BuiltInSettings.Window.Id)).IsNotNull();
        var ids = app.BuildCatalog().Select(c => c.Id).ToArray();
        await Assert.That(ids).Contains(BuiltInCommands.FullScreenCommand);
        await Assert.That(ids).Contains(BuiltInCommands.WindowedFullScreenCommand);

        await Assert.That(app.DispatchCommand(BuiltInCommands.WindowedFullScreenCommand)).IsTrue();
        await Assert.That(app.BuildCatalog().Single(c => c.Id == BuiltInCommands.WindowedFullScreenCommand).Title)
            .IsEqualTo("Leave windowed full screen");
        await Assert.That(app.DispatchCommand(BuiltInCommands.FullScreenCommand)).IsTrue();

        await Assert.That(modes.Toggled).IsEquivalentTo(new[] { WindowMode.WindowedFullScreen, WindowMode.FullScreen });
    }
}
