using System.Text.Json;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The arrangement holds across runs and across layout applies: a resumed workspace holding a docked composer builds,
/// split sizes come back as they were left, and a panel the player closed stays closed until they ask for it — in the
/// terminal and under the window's chrome alike.
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class LayoutRestartTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static AppConfiguration FreshConfig()
    {
        var config = DemoScene.Build();
        config.ScrollbackSpill.Enabled = false;
        config.LastSession = null;
        return config;
    }

    /// <summary>The live entry point's app: layouts applied on bind, under the terminal's or the window's chrome.</summary>
    private static SharpMUTermApp Live(AppConfiguration config, bool native)
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.Headless(new HeadlessConsoleDriver(120, 32), Headless) with
        {
            Chrome = native ? HostChrome.Native : HostChrome.None,
        };
        return new SharpMUTermApp(config, host, new ManualTimeProvider(), autoLayout: true);
    }

    /// <summary>The configuration as the next launch reads it: through the file's own serialiser.</summary>
    private static AppConfiguration Reloaded(AppConfiguration config) =>
        JsonSerializer.Deserialize<AppConfiguration>(
            JsonSerializer.Serialize(config, ConfigurationStore.SerializerOptions), ConfigurationStore.SerializerOptions)!;

    private static string ComposerOf(string key) => TerminalPanels.WindowId(PanelIds.Composer, key);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AWorkspaceSavedWithTheComposerDockedResumes(bool native)
    {
        var config = FreshConfig();
        string key;
        await using (var first = Live(config, native))
        {
            key = first.BindWorldWithoutConnecting(config.Worlds[0]).SessionKey;
            await Assert.That(first.PaneIdOf(ComposerOf(key))).IsNotNull();
            config.LastSession = first.CaptureSession();
        }

        // The composer was built in the constructor before the scheme it paints from existed, so this threw.
        await using var second = Live(Reloaded(config), native);
        await Assert.That(second.PaneIdOf(ComposerOf(key))).IsNotNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SplitSizesComeBackAsTheyWereLeft(bool native)
    {
        var config = FreshConfig();
        string before;
        await using (var first = Live(config, native))
        {
            first.BindWorldWithoutConnecting(config.Worlds[0]);
            first.RenderNextFrame();
            for (var i = 0; i < 6; i++)
            {
                first.DispatchCommand("layout:wider");
            }

            config.LastSession = first.CaptureSession();
            before = JsonSerializer.Serialize(config.LastSession.Root);
        }

        config = Reloaded(config);
        await using var second = Live(config, native);
        second.BindWorldWithoutConnecting(config.Worlds[0]);
        second.RenderNextFrame();
        await Assert.That(JsonSerializer.Serialize(second.CaptureSession().Root)).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AClosedComposerStaysClosedWhenTheLayoutIsAppliedAgain(bool native)
    {
        var config = FreshConfig();
        string key;
        await using (var first = Live(config, native))
        {
            key = first.BindWorldWithoutConnecting(config.Worlds[0]).SessionKey;
            first.CloseStripTab(ComposerOf(key));
            await Assert.That(first.PaneIdOf(ComposerOf(key))).IsNull();
        }

        var corvid = config.Worlds[0].Characters[0];
        await Assert.That(corvid.ClosedPanels).IsEquivalentTo(new[] { PanelIds.Composer });

        // A launch with no restore for this character applies its layout on bind, as a second character's connect does.
        config = Reloaded(config);
        config.LastSession = null;
        await using var second = Live(config, native);
        second.BindWorldWithoutConnecting(config.Worlds[0]);
        await Assert.That(second.LiveLayoutId).IsEqualTo(BuiltInLayouts.RoleplayId);
        await Assert.That(second.PaneIdOf(ComposerOf(key))).IsNull();
    }

    [Test]
    public async Task AskingForTheComposerOpensItAndLayoutsDrawItAgain()
    {
        var config = FreshConfig();
        await using var app = Live(config, native: false);
        var key = app.BindWorldWithoutConnecting(config.Worlds[0]).SessionKey;
        app.CloseStripTab(ComposerOf(key));

        await Assert.That(app.DispatchCommand("panel:composer")).IsTrue();
        await Assert.That(app.PaneIdOf(ComposerOf(key))).IsNotNull();
        await Assert.That(config.Worlds[0].Characters[0].ClosedPanels).IsNull();
    }

    [Test]
    public async Task ApplyingALayoutFromTheListDrawsItWhole()
    {
        var config = FreshConfig();
        await using var app = Live(config, native: false);
        var key = app.BindWorldWithoutConnecting(config.Worlds[0]).SessionKey;
        app.CloseStripTab(ComposerOf(key));
        await Assert.That(app.ClosedPanelsOf(key)).Contains(PanelIds.Composer);

        await Assert.That(app.DispatchCommand(LayoutCommands.ApplyPrefix + BuiltInLayouts.RoleplayId)).IsTrue();
        await Assert.That(app.PaneIdOf(ComposerOf(key))).IsNotNull();
        await Assert.That(app.ClosedPanelsOf(key)).IsEmpty();
    }

    [Test]
    public async Task ClosingAnOrdinaryWindowRecordsNothing()
    {
        var config = FreshConfig();
        await using var app = Live(config, native: false);
        var key = app.BindWorldWithoutConnecting(config.Worlds[0]).SessionKey;
        app.OpenUnownedWindowForTest("aside", "Aside");
        app.CloseStripTab("aside");

        await Assert.That(app.ClosedPanelsOf(key)).IsEmpty();
        var json = JsonSerializer.Serialize(config, ConfigurationStore.SerializerOptions);
        await Assert.That(json).DoesNotContain("closedPanels");
    }
}
