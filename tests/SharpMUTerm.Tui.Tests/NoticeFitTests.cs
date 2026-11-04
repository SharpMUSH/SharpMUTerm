using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The status row never wraps, whatever a notice says and however narrow the screen; a notice said again while it is
/// still on the row is not said twice; and a pane narrower than its server's wrap column is told about join wraps once.
/// </summary>
[NotInParallel]
public class NoticeFitTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App(int width, int height)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(DemoScene.Build(), ClientHost.Headless(new HeadlessConsoleDriver(width, height), Caps));
    }

    [Test]
    [Arguments(80, 24)]
    [Arguments(60, 20)]
    [Arguments(40, 12)]
    public async Task ALongNoticeIsElidedToTheRowAndTheRowStaysOneRow(int width, int height)
    {
        var app = App(width, height);
        app.RenderSnapshot();
        var before = app.LaidOutRows;

        app.SimulateNotice(
            string.Concat(Enumerable.Repeat("a notice far longer than any status row can hold ", 4)),
            MessageSeverity.Warning, "⌃B", null);
        app.RenderNextFrame();

        await Assert.That(SharpMUTermApp.MarkupWidth(app.StatusMarkup)).IsLessThanOrEqualTo(width);
        await Assert.That(app.LaidOutRows.Status).IsEqualTo(1);
        await Assert.That(app.LaidOutRows.Workspace).IsEqualTo(before.Workspace).Because("a wrapped row takes a row off the panes");
    }

    [Test]
    public async Task FitStatusRowLeavesARowThatFitsAlone()
    {
        await Assert.That(SharpMUTermApp.FitStatusRow("[dim]short[/]", 80)).IsEqualTo("[dim]short[/]");
        await Assert.That(SharpMUTermApp.MarkupWidth(SharpMUTermApp.FitStatusRow("[dim]" + new string('x', 100) + "[/]", 80)))
            .IsLessThanOrEqualTo(80);
    }

    [Test]
    public async Task TheSameNoticeTwiceIsSaidOnce()
    {
        var app = App(120, 34);
        var before = app.Messages.Entries.Count;

        app.SimulateNotice("Painting in Dark, output on screen included.", MessageSeverity.Info, "theme", null);
        app.SimulateNotice("Painting in Dark, output on screen included.", MessageSeverity.Info, "theme", null);

        await Assert.That(app.Messages.Entries.Count).IsEqualTo(before + 1);

        app.SimulateNotice("something else", MessageSeverity.Info, null, null);
        app.SimulateNotice("Painting in Dark, output on screen included.", MessageSeverity.Info, "theme", null);
        await Assert.That(app.Messages.Entries.Count).IsEqualTo(before + 3).Because("a notice after another one is news again");
    }

    [Test]
    public async Task APaneNarrowerThanTheServersWrapIsToldAboutJoinWrapsOnce()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);
        var app = new SharpMUTermApp(config, ClientHost.Headless(new HeadlessConsoleDriver(60, 24), Caps));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        await app.BindWorldWithoutConnecting(world).ConnectAsync();
        app.RenderNextFrame();

        for (var i = 0; i < 12; i++)
        {
            telnet.Receive("The plaza opens out under a white sky, its flagstones worn smooth by years of trade " + i);
        }

        app.RenderNextFrame();
        app.RenderNextFrame();

        var notices = app.Messages.Entries.Where(e => e.Text.Contains("join wraps", StringComparison.Ordinal)).ToList();
        await Assert.That(notices.Count).IsEqualTo(1);
        await Assert.That(world.JoinWrappedLines).IsFalse().Because("it is suggested, never switched on for the player");

        await Assert.That(app.DispatchCommand("world:join-wraps:Quiet")).IsTrue();
        await Assert.That(world.JoinWrappedLines).IsTrue();
    }

    [Test]
    public async Task AWidePaneIsToldNothing()
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "Quiet", Host = "quiet.example.org", Port = 4000 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann", Logging = new LoggingSettings() });
        config.Worlds.Add(world);
        var app = new SharpMUTermApp(config, ClientHost.Headless(new HeadlessConsoleDriver(160, 40), Caps));
        var telnet = new RecordingTelnetSession();
        app.TelnetFactory = _ => telnet;
        await app.BindWorldWithoutConnecting(world).ConnectAsync();

        for (var i = 0; i < 12; i++)
        {
            telnet.Receive("The plaza opens out under a white sky, its flagstones worn smooth by years of trade " + i);
        }

        app.RenderNextFrame();

        await Assert.That(app.Messages.Entries.Any(e => e.Text.Contains("join wraps", StringComparison.Ordinal))).IsFalse();
    }
}
