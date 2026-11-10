using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Help;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Zoom and per-area text sizes are the client's settings, offered only where the host draws its own text: the window
/// lists the Text size page and ⌃P's zoom entries, steps and saves the zoom, and is told every change; a terminal lists
/// none of it and says to use the terminal's own zoom.
/// </summary>
[NotInParallel]
public class TextSizeTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class FakeTextSize(bool areas = true) : IWindowTextSize
    {
        public List<DisplaySettings> Shown { get; } = [];

        public bool SizesAreas => areas;

        public event Action<int>? ZoomRequested;

        public void Show(DisplaySettings sizes) => Shown.Add(sizes);

        public void Press(int step) => ZoomRequested?.Invoke(step);
    }

    private sealed class Settings : INativeSettings
    {
        public List<SettingsForm> Shown { get; } = [];

        public void Show(SettingsForm form) => Shown.Add(form);

        public void Close()
        {
        }
    }

    private static SharpMUTermApp App(IWindowTextSize? sizes, INativeSettings? settings = null, AppConfiguration? config = null, List<AppConfiguration>? saves = null)
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 40), null, null, null)
            with { TextSize = sizes, NativeSettings = settings };
        return new SharpMUTermApp(config ?? new AppConfiguration(), host, save: saves is null ? null : saves.Add);
    }

    [Test]
    public async Task ATerminalListsNoTextSizePageAndNoZoom_AndSaysToUseItsOwn()
    {
        var app = App(null);
        var ids = app.BuildCatalog().Select(c => c.Id).ToArray();

        await Assert.That(app.Contributions.SettingsSections.Find(BuiltInSettings.TextSizeSectionId)).IsNull();
        await Assert.That(ids).DoesNotContain(BuiltInCommands.ZoomInCommand);
        await Assert.That(ids).DoesNotContain(BuiltInCommands.ZoomOutCommand);
        await Assert.That(ids).DoesNotContain(BuiltInCommands.ZoomResetCommand);
        await Assert.That(ids).DoesNotContain(BuiltInCommands.TextSizeCommand);

        await Assert.That(app.StepZoom(1)).IsFalse();
        await Assert.That(app.LastNoticeModel?.Text ?? "").Contains("terminal's own");
        await Assert.That(app.Configuration.Display.Zoom).IsEqualTo(100);
    }

    [Test]
    public async Task TheWindowListsThePageAndZoomEntries_AndIsToldTheSizesOnce()
    {
        var sizes = new FakeTextSize();
        var app = App(sizes, new Settings());

        await Assert.That(app.Contributions.SettingsSections.Find(BuiltInSettings.TextSizeSectionId)).IsNotNull();
        var catalog = app.BuildCatalog();
        await Assert.That(catalog.Single(c => c.Id == BuiltInCommands.ZoomInCommand).Title).IsEqualTo("Zoom in to 110%");
        await Assert.That(catalog.Single(c => c.Id == BuiltInCommands.ZoomOutCommand).Title).IsEqualTo("Zoom out to 90%");
        await Assert.That(catalog.Select(c => c.Id)).DoesNotContain(BuiltInCommands.ZoomResetCommand);
        await Assert.That(catalog.Select(c => c.Id)).Contains(BuiltInCommands.TextSizeCommand);

        app.RenderWholeFrame();
        app.RenderWholeFrame();
        await Assert.That(sizes.Shown.Count).IsEqualTo(1);
        await Assert.That(sizes.Shown[0].Zoom).IsEqualTo(100);
    }

    [Test]
    public async Task TheWindowsZoomKeysStepTheZoom_SaveIt_AndTellTheWindow()
    {
        var sizes = new FakeTextSize();
        var saves = new List<AppConfiguration>();
        var app = App(sizes, new Settings(), saves: saves);

        sizes.Press(1);
        sizes.Press(1);

        await Assert.That(app.Configuration.Display.Zoom).IsEqualTo(125);
        await Assert.That(saves.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(sizes.Shown[^1].Zoom).IsEqualTo(125);
        await Assert.That(app.LastNoticeModel?.Text ?? "").Contains("125%");

        await Assert.That(app.BuildCatalog().Single(c => c.Id == BuiltInCommands.ZoomResetCommand).Title).IsEqualTo("Reset zoom (125%)");
        await Assert.That(app.DispatchCommand(BuiltInCommands.ZoomResetCommand)).IsTrue();
        await Assert.That(app.Configuration.Display.Zoom).IsEqualTo(100);
        await Assert.That(sizes.Shown[^1].Zoom).IsEqualTo(100);
    }

    [Test]
    public async Task ZoomStopsAtItsEnds()
    {
        var config = new AppConfiguration();
        config.Display.Zoom = ZoomLevels.Maximum;
        var app = App(new FakeTextSize(), new Settings(), config);

        await Assert.That(app.BuildCatalog().Select(c => c.Id)).DoesNotContain(BuiltInCommands.ZoomInCommand);
        await Assert.That(app.StepZoom(1)).IsTrue();
        await Assert.That(app.Configuration.Display.Zoom).IsEqualTo(ZoomLevels.Maximum);
        await Assert.That(app.LastNoticeModel?.Text ?? "").Contains("largest");
    }

    [Test]
    public async Task AnEditOnThePageReachesTheWindowThroughTheSaveFunnel()
    {
        var sizes = new FakeTextSize();
        var app = App(sizes, new Settings());
        app.RenderWholeFrame();
        var field = (ChoiceField)BuiltInSettings.TextSize.Fields.Single(f => f.Id == "panel-text");

        ((SettingBinding<string>)field.Binding!).Set(app.Configuration, "150%");
        app.SaveConfiguration();

        await Assert.That(sizes.Shown[^1].PanelText).IsEqualTo(150);
    }

    [Test]
    public async Task ThePalettesEntryOpensThePageInTheWindowsSettings()
    {
        var settings = new Settings();
        var app = App(new FakeTextSize(), settings);

        await Assert.That(app.DispatchCommand(BuiltInCommands.TextSizeCommand)).IsTrue();
        await Assert.That(settings.Shown.Count).IsEqualTo(1);
        await Assert.That(settings.Shown[0].Initial).IsEqualTo(BuiltInSettings.TextSizeSectionId);
    }

    [Test]
    public async Task EmulatedModeKeepsTheZoomButHasNoPage()
    {
        var sizes = new FakeTextSize(areas: false);
        var app = App(sizes, null);

        await Assert.That(app.Contributions.SettingsSections.Find(BuiltInSettings.TextSizeSectionId)).IsNull();
        var ids = app.BuildCatalog().Select(c => c.Id).ToArray();
        await Assert.That(ids).Contains(BuiltInCommands.ZoomInCommand);
        await Assert.That(ids).DoesNotContain(BuiltInCommands.TextSizeCommand);
        await Assert.That(app.StepZoom(-1)).IsTrue();
        await Assert.That(app.Configuration.Display.Zoom).IsEqualTo(90);
    }

    [Test]
    public async Task HelpSaysTheTerminalsTextIsItsOwn()
    {
        var topic = HelpTopics.For("⌥⏎").Single(t => t.Id == "text-size");
        var text = string.Join(" ", topic.Lines);

        await Assert.That(text).Contains("terminal's own");
        await Assert.That(text).Contains("⌃=");
    }
}
