using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Protocols;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// GMCP and MSDP from the client's side: the status row's meters and the ⌃P inspector. The client used to
/// keep nine fixed <c>Char.*</c> keys from GMCP and draw none of them.
/// </summary>
[NotInParallel]
public class GameDataTests
{
    private const string Ann = "Convergence.Ann";

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static AppConfiguration Config()
    {
        var config = new AppConfiguration();
        config.Worlds.Add(new WorldDefinition
        {
            Name = "Convergence",
            Host = "convergence.example",
            Port = 4201,
            Characters = { new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() } },
        });
        return config;
    }

    private static async Task<(SharpMUTermApp App, RecordingTelnetSession Wire)> Open(AppConfiguration? config = null)
    {
        Console.SetIn(TextReader.Null);
        var wire = new RecordingTelnetSession();
        var clock = new ManualTimeProvider();
        var app = new SharpMUTermApp(config ?? Config(), Headless, new HeadlessConsoleDriver(160, 32), time: clock);
        app.TelnetFactory = _ => wire;
        if (!app.DispatchCommand(CommandIds.Character(Ann)))
        {
            throw new InvalidOperationException("the app would not switch to Ann");
        }

        await app.FindSession(Ann)!.ConnectAsync();

        // The switch's notice holds the row; the meters are on the resting line under it.
        clock.Advance(SharpMUTermApp.NoticeDuration + TimeSpan.FromSeconds(1));
        app.RenderNextFrame();
        return (app, wire);
    }

    [Test]
    public async Task AWorldWithNoGameDataShowsNoMeters()
    {
        var (app, _) = await Open();
        await using var _app = app;

        await Assert.That(MarkupText.Plain(app.StatusMarkup)).DoesNotContain("hp");
    }

    [Test]
    public async Task GmcpVitalsAppearOnTheStatusRow()
    {
        var (app, wire) = await Open();
        await using var _app = app;

        wire.ReceiveGmcp("Char.Vitals", """{"hp":"412","maxhp":"500"}""");
        app.RenderNextFrame();

        var row = MarkupText.Plain(app.StatusMarkup);
        await Assert.That(row).Contains("hp   412/500");
        await Assert.That(row).DoesNotContain("mp"); // no value, no meter
    }

    [Test]
    public async Task AnMsdpGameFillsTheSameMeterAndIsAskedForIt()
    {
        var (app, wire) = await Open();
        await using var _app = app;

        wire.RaiseMsdpEnabled();
        wire.ReceiveMsdp("""{"HEALTH":"90","HEALTH_MAX":"100"}""");
        app.RenderNextFrame();

        await Assert.That(MarkupText.Plain(app.StatusMarkup)).Contains("hp    90/100");
        await Assert.That(wire.SentMsdp).Contains(("REPORT", "HEALTH"));
        await Assert.That(wire.SentMsdp).Contains(("REPORT", "HEALTH_MAX"));
    }

    /// <summary>The 99 → 100 lesson: a value from the wire may not change how wide the row is.</summary>
    [Test]
    [Arguments("9")]
    [Arguments("10")]
    [Arguments("100")]
    [Arguments("99999")]
    [Arguments("123456789")]
    public async Task AMetersWidthDoesNotDependOnItsValue(string hp)
    {
        var meters = new List<StatusMeter> { new() { Label = "hp", Value = "Char.Vitals.hp", Max = "Char.Vitals.maxhp" } };
        var baseline = new GameDataStore();
        baseline.SetGmcp("Char.Vitals", """{"hp":"1","maxhp":"1"}""");
        var store = new GameDataStore();
        store.SetGmcp("Char.Vitals", $$"""{"hp":"{{hp}}","maxhp":"{{hp}}"}""");

        var width = MarkupText.VisibleLength(StatusMeterRenderer.Render(meters, store, "#ffff00"));

        await Assert.That(width).IsEqualTo(MarkupText.VisibleLength(StatusMeterRenderer.Render(meters, baseline, "#ffff00")));
    }

    [Test]
    public async Task ALowValueWearsTheWarningInk()
    {
        var meters = new List<StatusMeter> { new() { Label = "hp", Value = "Char.Vitals.hp", Max = "Char.Vitals.maxhp" } };
        var store = new GameDataStore();
        store.SetGmcp("Char.Vitals", """{"hp":"20","maxhp":"100"}""");

        await Assert.That(StatusMeterRenderer.Render(meters, store, "#abcdef")).Contains("#abcdef");

        store.SetGmcp("Char.Vitals", """{"hp":"80"}""");
        await Assert.That(StatusMeterRenderer.Render(meters, store, "#abcdef")).DoesNotContain("#abcdef");
    }

    [Test]
    public async Task TheInspectorOpensOnTheFocusedConnection()
    {
        var (app, wire) = await Open();
        await using var _app = app;
        wire.ReceiveGmcp("Room.Info", """{"num":1,"name":"Hall"}""");

        await Assert.That(app.BuildCatalog().Select(i => i.Id)).Contains("term:gamedata");
        await Assert.That(app.DispatchCommand("term:gamedata")).IsTrue();
        await Assert.That(app.GameDataIsOpen).IsTrue();

        app.DispatchCommand("term:gamedata");
        await Assert.That(app.GameDataIsOpen).IsFalse();
    }

    [Test]
    public async Task F7EditsTheMetersAsOneLine()
    {
        var text = new TextSettings();
        var screen = OptionsScreenRenderer.TextAnsiScreen(text);
        var model = OptionsScreenRenderer.Model(screen);
        var field = Enumerable.Range(0, model.PaneCount)
            .SelectMany(p => Enumerable.Range(0, model.RowCount(p)).Select(r => model.RowAt(p, r)))
            .SelectMany(r => Enumerable.Range(0, r.FieldCount).Select(i => r.FieldAt(i)))
            .Where(f => f is not null)
            .Select(f => f!.Value)
            .Single(f => f.Label == "meters");

        await Assert.That(new ScreenEdits().Apply(field, "hp=Char.Vitals.hp/Char.Vitals.maxhp; gold=Char.Status.gold; broken")).IsNull();

        await Assert.That(text.StatusMeters.Select(m => m.Label)).IsEquivalentTo(new[] { "hp", "gold" });
        await Assert.That(text.StatusMeters[1].Max).IsNull();
    }

    [Test]
    public async Task F5KeepsOnlyModulesThatParse()
    {
        var world = new WorldDefinition { Name = "W", Host = "h", Port = 1 };

        WorldsScreenRenderer.SplitModules(world, "IRE.Rift 1, nonsense here, Room 2, ire.rift 3");

        await Assert.That(world.GmcpModules).IsEquivalentTo(new[] { "IRE.Rift 1", "Room 2" });
        await Assert.That(WorldsScreenRenderer.JoinModules(world)).IsEqualTo("IRE.Rift 1, Room 2");
    }
}
