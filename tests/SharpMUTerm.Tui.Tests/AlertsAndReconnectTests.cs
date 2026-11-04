using System.Runtime.CompilerServices;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Wave 12's three: a dropped connection dialled again with a countdown on the status row, the desktop told when a
/// line wants the player while they are elsewhere, and typed command stacking reaching the wire per piece.
/// </summary>
[NotInParallel]
public class AlertsAndReconnectTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class FakeAttention : IAttention
    {
        public bool FiltersByFocus { get; init; }

        public bool? Focused { get; set; }

        public event Action<bool>? FocusChanged;

        public List<AttentionRequest> Requests { get; } = [];

        public void Request(AttentionRequest request) => Requests.Add(request);

        public void Focus(bool focused)
        {
            Focused = focused;
            FocusChanged?.Invoke(focused);
        }
    }

    private sealed record Rig(
        SharpMUTermApp App,
        WorldSession Session,
        ManualTimeProvider Time,
        List<RecordingTelnetSession> Wires,
        FakeAttention Attention,
        StrongBox<Exception?> Refuse)
    {
        public RecordingTelnetSession Wire => Wires[^1];
    }

    private static async Task<Rig> Open(
        bool autoReconnect = true,
        string? separator = null,
        bool alertOnName = false,
        Trigger? trigger = null,
        bool? focused = false)
    {
        Console.SetIn(TextReader.Null);
        var config = new AppConfiguration();
        var world = new WorldDefinition
        {
            Name = "Aetherfall",
            Host = "aetherfall.example",
            Port = 4201,
            AutoReconnect = autoReconnect,
            CommandSeparator = separator,
            AlertOnName = alertOnName,
        };
        world.Characters.Add(new CharacterDefinition { Name = "Corvid", Logging = new() });
        config.Worlds.Add(world);
        if (trigger is not null)
        {
            var set = new TriggerSet { Name = "alerts" };
            set.Triggers.Add(trigger);
            config.TriggerSets.Add(set);
            world.Characters[0].TriggerSets.Add(set.Name);
        }

        var time = new ManualTimeProvider();
        var attention = new FakeAttention { Focused = focused };
        var host = ClientHost.ForHarness(Headless, new HeadlessConsoleDriver(120, 32), null, null, null) with
        {
            Attention = attention,
        };
        var app = new SharpMUTermApp(config, host, time);
        var wires = new List<RecordingTelnetSession>();
        var refuse = new StrongBox<Exception?>();
        app.TelnetFactory = _ =>
        {
            var wire = new RecordingTelnetSession { ConnectFault = refuse.Value };
            wires.Add(wire);
            return wire;
        };
        app.DispatchCommand(CommandIds.Character("Aetherfall.Corvid"));
        var session = app.FindSession("Aetherfall.Corvid")!;
        await session.ConnectAsync();
        app.RenderNextFrame();
        return new Rig(app, session, time, wires, attention, refuse);
    }

    private static string Plain(string markup) => MarkupText.Plain(markup);

    [Test]
    public async Task ADropCountsDownOnTheStatusRowAndDialsAgain()
    {
        var rig = await Open();
        await using var _ = rig.App;

        // The first wait is said by the notice, which carries Stop and covers the row for longer than the wait.
        rig.Refuse.Value = new IOException("Connection refused");
        rig.Wire.Drop(new IOException("Connection reset by peer"));
        await Assert.That(rig.App.LastNotice).Contains("reconnecting in 5 s");
        await Assert.That(rig.App.BuildCatalog().Select(i => i.Title)).Contains("Stop reconnecting");

        // That try is refused; the second waits fifteen seconds, and once its notice has gone the row counts it down.
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        await Assert.That(rig.Wires.Count).IsEqualTo(2);
        rig.Time.Advance(SharpMUTermApp.NoticeDuration);
        rig.App.RenderNextFrame();
        await Assert.That(Plain(rig.App.StatusMarkup)).Contains(SharpMUTermApp.ReconnectState(9));
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        rig.App.RenderNextFrame();
        await Assert.That(Plain(rig.App.StatusMarkup)).Contains(SharpMUTermApp.ReconnectState(8));

        rig.Refuse.Value = null;
        rig.Time.Advance(TimeSpan.FromSeconds(8));
        rig.App.RenderNextFrame();

        await Assert.That(rig.Wires.Count).IsEqualTo(3);
        await Assert.That(rig.Session.State).IsEqualTo(ConnectionState.Connected);
        await Assert.That(Plain(rig.App.StatusMarkup)).DoesNotContain("reconnecting");
        await Assert.That(rig.App.BuildCatalog().Select(i => i.Title)).DoesNotContain("Stop reconnecting");
    }

    /// <summary>
    /// What a reconnect says in the session's window is a client row drawn like the bars, not a <c>***</c> session line:
    /// it is never in the session's scrollback (so never in a transcript, restore or scene log) and ⌃F does not find it.
    /// </summary>
    [Test]
    public async Task AReconnectSaysSoInAClientRowAndNotInTheSessionsLines()
    {
        var rig = await Open();
        await using var _ = rig.App;
        rig.Wire.Drop(new IOException("Connection reset by peer"));

        var window = rig.App.ActiveWindowId();
        var row = rig.App.PaneLines(window).Last(l => MarkupText.Plain(l).Contains(SharpMUTermApp.ReconnectLabel, StringComparison.Ordinal));
        await Assert.That(MarkupText.Plain(row)).Contains("in 5 s · try 1 of");
        await Assert.That(rig.Session.Scrollback.Snapshot().Any(l => l.Text.Contains("econnect", StringComparison.Ordinal))).IsFalse();
        await Assert.That(rig.App.SearchModelFor("try 1 of", regex: false, all: true).Rows).IsEmpty();
    }

    [Test]
    public async Task StopReconnectingCancelsTheWait()
    {
        var rig = await Open();
        await using var _ = rig.App;
        rig.Wire.Drop(new IOException("reset"));

        rig.App.DispatchCommand(BuiltInCommands.StopReconnectCommand);
        await Assert.That(rig.App.LastNotice).Contains("stopped reconnecting");
        rig.Time.Advance(TimeSpan.FromMinutes(2));
        rig.App.RenderNextFrame();

        await Assert.That(rig.Wires.Count).IsEqualTo(1);
        await Assert.That(Plain(rig.App.StatusMarkup)).DoesNotContain("reconnecting");
    }

    [Test]
    public async Task ReconnectWhileWaitingDialsAtOnceAndLeavesNoSecondTry()
    {
        var rig = await Open();
        await using var _ = rig.App;
        rig.Wire.Drop(new IOException("reset"));

        rig.App.DispatchCommand("world:reconnect");
        await Task.Delay(50);
        rig.Time.Advance(TimeSpan.FromMinutes(2));

        await Assert.That(rig.Wires.Count).IsEqualTo(2);
        await Assert.That(rig.Session.State).IsEqualTo(ConnectionState.Connected);
    }

    [Test]
    public async Task DisconnectWhileWaitingCancelsIt()
    {
        var rig = await Open();
        await using var _ = rig.App;
        rig.Wire.Drop(new IOException("reset"));

        rig.App.DispatchCommand("world:disconnect");
        rig.Time.Advance(TimeSpan.FromMinutes(2));

        await Assert.That(rig.Wires.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ThePlayersOwnDisconnectIsNeverDialledAgain()
    {
        var rig = await Open();
        await using var _ = rig.App;

        rig.App.DispatchCommand("world:disconnect");
        rig.Time.Advance(TimeSpan.FromMinutes(2));

        await Assert.That(rig.Wires.Count).IsEqualTo(1);
        await Assert.That(rig.Session.LastClose).IsEqualTo(SessionClose.Player);
    }

    [Test]
    public async Task AWorldWithReconnectOffIsLeftDown()
    {
        var rig = await Open(autoReconnect: false);
        await using var _ = rig.App;

        rig.Wire.Drop(new IOException("reset"));
        rig.Time.Advance(TimeSpan.FromMinutes(2));

        await Assert.That(rig.Wires.Count).IsEqualTo(1);
        await Assert.That(rig.App.BuildCatalog().Select(i => i.Title)).DoesNotContain("Stop reconnecting");
    }

    [Test]
    public async Task ThePageReachesTheHostOnlyWhileItIsBehindAnotherWindow()
    {
        var rig = await Open(alertOnName: true, focused: true);
        await using var _ = rig.App;

        rig.Wire.Receive("Rivane pages: are you there?\r\n");
        await Assert.That(rig.Attention.Requests).IsEmpty();

        rig.Attention.Focus(false);
        rig.Wire.Receive("Rivane pages: hello?\r\n");

        await Assert.That(rig.Attention.Requests.Count).IsEqualTo(1);
        await Assert.That(rig.Attention.Requests[0].Title).IsEqualTo("Corvid · Aetherfall — page");
        await Assert.That(rig.Attention.Requests[0].Body).IsEqualTo("Rivane pages: hello?");

        // One per interval: the next page inside it is not said again.
        rig.Wire.Receive("Rivane pages: hello??\r\n");
        await Assert.That(rig.Attention.Requests.Count).IsEqualTo(1);
        rig.Time.Advance(AttentionThrottle.Interval);
        rig.Wire.Receive("Rivane pages: hello???\r\n");
        await Assert.That(rig.Attention.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TheTitleCountsWhatArrivedWhileAwayAndClearsOnReturn()
    {
        var rig = await Open(focused: true);
        await using var _ = rig.App;
        rig.App.RenderNextFrame();
        var resting = rig.App.ShownTitle!;

        rig.Attention.Focus(false);
        rig.Wire.Receive("The fountain bubbles.\r\nA bird lands.\r\n");

        await Assert.That(rig.App.ShownTitle).IsEqualTo("(2) " + resting);

        rig.Attention.Focus(true);
        await Assert.That(rig.App.ShownTitle).IsEqualTo(resting);
    }

    [Test]
    public async Task AnAlertTriggerAsksAndF7CanTurnItOff()
    {
        var trigger = new Trigger { Name = "attack", Pattern = "attacks you", Actions = { Alert = true } };
        var rig = await Open(trigger: trigger);
        await using var _ = rig.App;

        rig.Wire.Receive("A rat attacks you!\r\n");
        await Assert.That(rig.Attention.Requests.Select(r => r.Title)).IsEquivalentTo(["Corvid · Aetherfall — alert"]);

        rig.App.Configuration.Text.AlertWhenAway = false;
        rig.Time.Advance(AttentionThrottle.Interval);
        rig.Wire.Receive("A rat attacks you again!\r\n");
        await Assert.That(rig.Attention.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AHostThatCannotTellItsFocusWaitsForTheReaderToBeIdle()
    {
        var trigger = new Trigger { Name = "attack", Pattern = "attacks you", Actions = { Alert = true } };
        var rig = await Open(trigger: trigger, focused: null);
        await using var _ = rig.App;

        rig.App.SimulateKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));
        rig.Wire.Receive("A rat attacks you!\r\n");
        await Assert.That(rig.Attention.Requests).IsEmpty();

        rig.Time.Advance(SharpMUTermApp.AwayAfter);
        rig.Wire.Receive("A rat attacks you!\r\n");
        await Assert.That(rig.Attention.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TheCommandLineStacksOnAWorldWithASeparator()
    {
        var rig = await Open(separator: ";");
        await using var _ = rig.App;

        foreach (var c in "#2 n;look")
        {
            rig.App.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        rig.App.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        await Task.Delay(50);

        await Assert.That(rig.Wire.Lines).IsEquivalentTo(["n", "n", "look"]);
    }

    [Test]
    public async Task WithNoSeparatorTheCommandLineSendsTheLineAsTyped()
    {
        var rig = await Open(separator: null);
        await using var _ = rig.App;

        foreach (var c in "say a;b")
        {
            rig.App.SimulateKey(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
        }

        rig.App.SimulateKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
        await Task.Delay(50);

        await Assert.That(rig.Wire.Lines).IsEquivalentTo(["say a;b"]);
    }

    [Test]
    public void TheTerminalEscapesCannotBeEndedEarlyByAGamesText()
    {
        var request = new AttentionRequest("Corvid · Aetherfall — page", "x\u001b\\;\u0007y");

        var kitty = TerminalAttention.Sequence(TerminalNotify.Osc99, request, 1);
        var foot = TerminalAttention.Sequence(TerminalNotify.Osc777, request, 1);

        // The only ESC and BEL bytes are the escapes' own.
        if (kitty.Count(c => c == '\u001b') != 4 || kitty.Contains('\u0007') || !kitty.Contains("o=unfocused"))
        {
            throw new InvalidOperationException(kitty);
        }

        if (foot.Count(c => c == '\u001b') != 2 || foot.Contains('\u0007') || foot.Split(';').Length != 4)
        {
            throw new InvalidOperationException(foot);
        }

        if (TerminalAttention.Sequence(TerminalNotify.Bell, request, 1) != "\a")
        {
            throw new InvalidOperationException("bell");
        }
    }

    [Test]
    public async Task TheDemoViewWritesWithTheLiveWriters()
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        await using var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(120, 32));
        app.RenderSnapshot("reconnect");

        await Assert.That(Plain(app.StatusMarkup)).Contains(SharpMUTermApp.ReconnectState(12));
    }
}
