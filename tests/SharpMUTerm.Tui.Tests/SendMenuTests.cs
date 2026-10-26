using System.Drawing;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>An MXP <c>&lt;SEND&gt;</c> menu: the payload round trip, the window's menu, and the terminal's chooser.</summary>
[NotInParallel]
public class SendMenuTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static readonly SpanInteraction Menu = SpanInteraction.Command("buy lantern") with
    {
        Choices = [new SendChoice("Buy it", "buy lantern"), new SendChoice("Haggle, | , %", "haggle over a, b | c %20")],
    };

    [Test]
    public async Task AMenuSurvivesTheLinkRoundTripAndItsSeparatorsCannotBeForged()
    {
        var emitted = LinkPayload.For(Menu)!;
        var delivered = LinkPayload.Delivered(emitted);

        await Assert.That(delivered.Action).IsEqualTo(LinkAction.Send);
        await Assert.That(delivered.Target).IsEqualTo("buy lantern");
        await Assert.That(delivered.Choices!.Select(c => (c.Label, c.Command))).IsEquivalentTo(
            Menu.Choices!.Select(c => (c.Label, c.Command)), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(LinkPayload.Delivered(LinkPayload.For(Menu with { PromptOnly = true })!).Action).IsEqualTo(LinkAction.Prompt);
    }

    [Test]
    public async Task AMalformedMenuIsNoPayloadAtAll()
    {
        await Assert.That(LinkPayload.Parse(LinkPayload.MenuScheme + "no-comma").Action).IsEqualTo(LinkAction.Unknown);
    }

    private static SharpMUTermApp Snapshot()
    {
        Console.SetIn(TextReader.Null);
        var app = new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(120, 32));
        app.RenderSnapshot("send-menu");
        return app;
    }

    [Test]
    public async Task ARightClickInATerminalOpensTheChooserAndADigitSendsThatCommand()
    {
        await using var app = Snapshot();

        await Assert.That(app.SendMenu.IsOpen).IsTrue();
        var text = string.Join("\n", app.SendMenu.Lines.Select(MarkupText.Plain));
        await Assert.That(text).Contains("Buy it");
        await Assert.That(text).Contains("haggle over a brass lantern");
    }

    [Test]
    public async Task TheWindowsMenuListsEveryCommandByItsLabel()
    {
        Console.SetIn(TextReader.Null);
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, _ => { }) with { Pointer = new MenuPresenter() };
        await using var app = new SharpMUTermApp(DemoScene.Build(), host);
        app.RenderSnapshot("send-menu");

        await Assert.That(app.SendMenu.IsOpen).IsFalse();
        var cell = app.MenuLinkCell(SharpMUTermApp.MainWindowId)!.Value;
        var labels = app.PointerMenu(cell).Select(i => i.Label).ToArray();
        await Assert.That(labels).Contains("Buy it");
        await Assert.That(labels).Contains("Look at it");
        await Assert.That(labels).Contains("Haggle");
        await Assert.That(app.TargetAt(cell).Tooltip).Contains("right-click for 2 more");
    }

    [Test]
    public async Task TheChooserSendsTheChosenCommandToTheWindowsOwnWorld()
    {
        Console.SetIn(TextReader.Null);
        var config = new SharpMUTerm.Core.Configuration.AppConfiguration();
        config.Worlds.Add(new SharpMUTerm.Core.Configuration.WorldDefinition
        {
            Name = "Market",
            Host = "market.example",
            Port = 4000,
            ContentFormat = SharpMUTerm.Core.Configuration.ContentFormat.Mxp,
            Characters = { new SharpMUTerm.Core.Configuration.CharacterDefinition { Name = "Ann", Logging = new() } },
        });
        await using var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        var wire = new RecordingTelnetSession();
        app.TelnetFactory = _ => wire;
        app.DispatchCommand(SharpMUTerm.Core.Commands.CommandIds.Character("Market.Ann"));
        await app.FindSession("Market.Ann")!.ConnectAsync();
        wire.Receive("\x1b[1zOn the cloth: <SEND \"buy &text;|look &text;\">a lantern</SEND>.");
        app.RenderNextFrame();
        var window = app.ActiveWindowId();

        app.SimulatePointer(app.MenuLinkCell(window)!.Value, MouseFlags.Button3Pressed);
        await Assert.That(app.SendMenu.IsOpen).IsTrue();
        app.SendMenu.SimulateKey(new ConsoleKeyInfo('2', ConsoleKey.D2, false, false, false));

        await Assert.That(app.SendMenu.IsOpen).IsFalse();
        await Assert.That(wire.Lines).Contains("look a lantern");
    }

    private sealed class MenuPresenter : IPointerPresenter
    {
        public List<IReadOnlyList<PointerMenuItem>> Menus { get; } = [];

        public void Hover(PointerTarget target)
        {
        }

        public void ShowMenu(Point cell, IReadOnlyList<PointerMenuItem> items) => Menus.Add(items);

        public bool DrawsMenus => true;

        public void Scrollbars(IReadOnlyList<PaneScrollbar> bars)
        {
        }
    }
}
