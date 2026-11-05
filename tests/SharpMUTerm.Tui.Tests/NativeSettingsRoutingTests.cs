using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A host with its own settings window (<see cref="ClientHost.NativeSettings"/>) gets the declared sections there and
/// keeps the cell screens for the rest; a host without one sees no change. What the window commits goes through the
/// cell screens' own funnel, so it is saved, and a reset puts back the default the cell screen's <c>r</c> does.
/// </summary>
[NotInParallel]
public class NativeSettingsRoutingTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class Presenter : INativeSettings
    {
        public List<SettingsForm> Shown { get; } = [];

        public int Closed { get; private set; }

        public void Show(SettingsForm form) => Shown.Add(form);

        public void Close() => Closed++;
    }

    private static (SharpMUTermApp App, Presenter Native, List<AppConfiguration> Saves) App(bool native = true)
    {
        Console.SetIn(TextReader.Null);
        var presenter = new Presenter();
        var saves = new List<AppConfiguration>();
        var host = ClientHost.Headless(new HeadlessConsoleDriver(120, 34), Caps) with
        {
            NativeSettings = native ? presenter : null,
        };
        return (new SharpMUTermApp(DemoScene.Build(), host, save: saves.Add), presenter, saves);
    }

    private static ConsoleKeyInfo F(ConsoleKey key) => new('\0', key, false, false, false);

    [Test]
    public async Task F7AndF8OpenTheHostsWindowAndNoCellScreen()
    {
        var (app, native, _) = App();

        app.SimulateKey(F(ConsoleKey.F7));
        await Assert.That(native.Shown.Count).IsEqualTo(1);
        await Assert.That(native.Shown[0].Initial).IsEqualTo("text");
        await Assert.That(app.SettingsOpenKey).IsNull();

        app.DispatchCommand("screen:input");
        await Assert.That(native.Shown.Count).IsEqualTo(2);
        await Assert.That(native.Shown[1].Initial).IsEqualTo("input");
        await Assert.That(app.SettingsOpenKey).IsNull();
    }

    [Test]
    public async Task AListScreenOpensTheHostsWindowToo()
    {
        var (app, native, _) = App();

        app.SimulateKey(F(ConsoleKey.F2));

        await Assert.That(native.Shown.Single().Initial).IsEqualTo("triggers");
        await Assert.That(app.SettingsOpenKey).IsNull();
    }

    [Test]
    public async Task AHostWithNoSettingsWindowOpensTheCellScreen()
    {
        var (app, native, _) = App(native: false);

        app.SimulateKey(F(ConsoleKey.F7));

        await Assert.That(native.Shown).IsEmpty();
        await Assert.That(app.SettingsOpenKey).IsEqualTo(ConsoleKey.F7);
    }

    [Test]
    public async Task AChangeFromTheWindowIsSavedAndAResetPutsTheDefaultBack()
    {
        var (app, native, saves) = App();
        app.SimulateKey(F(ConsoleKey.F7));
        var form = native.Shown.Single();
        var tab = (NumberField)form.Section("text")!.Fields.Single(f => f.Id == "tab-width");
        var changes = 0;
        form.Changed += () => changes++;

        form.SetNumber(tab, 99);

        await Assert.That(app.Configuration.Text.TabWidth).IsEqualTo(TextSettings.MaxTabWidth).Because("held to the field's bounds");
        await Assert.That(saves.Count).IsEqualTo(1);
        await Assert.That(form.IsDefault(tab)).IsFalse();
        await Assert.That(changes).IsEqualTo(1);

        form.Reset(tab);

        await Assert.That(app.Configuration.Text.TabWidth).IsEqualTo(new AppConfiguration().Text.TabWidth);
        await Assert.That(form.IsDefault(tab)).IsTrue();
        await Assert.That(saves.Count).IsEqualTo(2);
    }

    /// <summary>
    /// The window never sends a declared section to its cell screen: F5 asked for from the window comes back as the
    /// window's own Worlds &amp; Characters page, so the cell F5 is the terminal's alone.
    /// </summary>
    [Test]
    public async Task TheWindowNeverOpensTheCellF5()
    {
        var (app, native, _) = App();
        app.SimulateKey(F(ConsoleKey.F5));
        await Assert.That(native.Shown.Single().Initial).IsEqualTo("worlds");

        native.Shown.Single().OpenScreen("worlds");
        app.SimulateKey(F(ConsoleKey.F9));

        await Assert.That(app.SettingsOpenKey).IsNull();
        await Assert.That(native.Shown.Select(f => f.Initial)).IsEquivalentTo(new[] { "worlds", "worlds", "logging" });
    }

    /// <summary>The worlds page's two commands go to the client, which closes the window first; nothing else is run.</summary>
    [Test]
    public async Task TheWorldsPageRunsOnlyBrowseAndImport()
    {
        var (app, native, _) = App();
        app.SimulateKey(F(ConsoleKey.F5));
        var form = native.Shown.Single();

        await Assert.That(form.Run("term:quit")).IsFalse();
        await Assert.That(native.Closed).IsEqualTo(0);
        await Assert.That(form.Run(SettingsForm.BrowseWorlds)).IsTrue();
        await Assert.That(native.Closed).IsEqualTo(1);
    }

    [Test]
    public async Task TheSearchIsTheCellSurfacesOwn()
    {
        var (app, native, _) = App();
        app.SimulateKey(F(ConsoleKey.F7));
        var form = native.Shown.Single();

        var sections = app.Contributions.SettingsSections.Ordered;
        await Assert.That(form.Search("meter").Select(m => m.Text))
            .IsEquivalentTo(SettingsSearch.Find(sections, "meter").Select(m => m.Text));
    }
}
