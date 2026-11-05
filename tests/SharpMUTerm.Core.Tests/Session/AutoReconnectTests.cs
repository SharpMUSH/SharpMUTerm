using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Tests.Mapping;

namespace SharpMUTerm.Core.Tests.Session;

/// <summary>Dialling a dropped connection again: what counts as a drop, the waits, and when it stops.</summary>
public class AutoReconnectTests
{
    private sealed class Rig
    {
        public required WorldSession Session { get; init; }
        public required AutoReconnect Auto { get; init; }
        public required ManualTimeProvider Time { get; init; }
        public required List<FakeTelnetSession> Wires { get; init; }
        public List<ReconnectStep> Steps { get; } = [];
        public FakeTelnetSession Wire => Wires[^1];

        /// <summary>When set, the next sockets refuse to connect.</summary>
        public Exception? Refuse { get; set; }
    }

    private static async Task<Rig> Connected(bool autoReconnect = true)
    {
        var time = new ManualTimeProvider();
        var wires = new List<FakeTelnetSession>();
        Rig? rig = null;
        var world = new WorldDefinition { Name = "Aetherfall", Host = "h", Port = 1, AutoReconnect = autoReconnect };
        var session = new WorldSession(world, sessionFactory: _ =>
        {
            var wire = new FakeTelnetSession { ConnectFault = rig?.Refuse };
            wires.Add(wire);
            return wire;
        });
        var auto = new AutoReconnect(session, time, s => s.ConnectAsync());
        rig = new Rig { Session = session, Auto = auto, Time = time, Wires = wires };
        auto.Changed += (_, step) => rig.Steps.Add(step);
        await session.ConnectAsync();
        return rig;
    }

    [Test]
    public async Task ADropWaitsFiveSecondsThenDialsAndComesBack()
    {
        var rig = await Connected();

        rig.Wire.Drop(new IOException("Connection reset by peer"));

        await Assert.That(rig.Session.LastClose).IsEqualTo(SessionClose.Failed);
        await Assert.That(rig.Auto.SecondsLeft).IsEqualTo(5);
        await Assert.That(rig.Auto.Try).IsEqualTo(1);
        rig.Time.Advance(TimeSpan.FromSeconds(4));
        await Assert.That(rig.Wires.Count).IsEqualTo(1);

        rig.Time.Advance(TimeSpan.FromSeconds(1));

        await Assert.That(rig.Wires.Count).IsEqualTo(2);
        await Assert.That(rig.Session.State).IsEqualTo(ConnectionState.Connected);
        await Assert.That(rig.Steps).IsEquivalentTo([ReconnectStep.Scheduled, ReconnectStep.Dialling, ReconnectStep.Connected]);
        await Assert.That(rig.Auto.IsActive).IsFalse();
    }

    [Test]
    public async Task AServerClosingOnItsOwnIsADropToo()
    {
        var rig = await Connected();

        rig.Wire.Drop();

        await Assert.That(rig.Session.LastClose).IsEqualTo(SessionClose.Server);
        await Assert.That(rig.Auto.DueAt).IsNotNull();
    }

    [Test]
    public async Task AServerClosingRightAfterACommandIsALogout()
    {
        var rig = await Connected();
        await rig.Session.SendCommandAsync("QUIT");
        rig.Time.Advance(TimeSpan.FromSeconds(2));

        rig.Wire.Drop();

        await Assert.That(rig.Auto.IsActive).IsFalse();
        await Assert.That(rig.Steps).IsEmpty();
    }

    [Test]
    public async Task ThePlayersOwnDisconnectNeverReconnects()
    {
        var rig = await Connected();

        await rig.Session.DisconnectAsync();
        rig.Time.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(rig.Session.LastClose).IsEqualTo(SessionClose.Player);
        await Assert.That(rig.Wires.Count).IsEqualTo(1);
        await Assert.That(rig.Steps).IsEmpty();
    }

    [Test]
    public async Task AWorldThatDoesNotAskIsLeftAlone()
    {
        var rig = await Connected(autoReconnect: false);

        rig.Wire.Drop(new IOException("reset"));
        rig.Time.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(rig.Wires.Count).IsEqualTo(1);
        await Assert.That(rig.Steps).IsEmpty();
    }

    [Test]
    public async Task FailedTriesBackOffAndRunOut()
    {
        var rig = await Connected();
        rig.Refuse = new IOException("Connection refused");
        rig.Wire.Drop(new IOException("reset"));

        var waits = new List<int>();
        for (var i = 0; i < AutoReconnect.Delays.Count; i++)
        {
            waits.Add(rig.Auto.SecondsLeft ?? -1);
            rig.Time.Advance(AutoReconnect.Delays[i]);
        }

        await Assert.That(waits).IsEquivalentTo([5, 15, 30, 60, 60]);
        await Assert.That(rig.Steps[^1]).IsEqualTo(ReconnectStep.GaveUp);
        await Assert.That(rig.Auto.IsActive).IsFalse();
        rig.Time.Advance(TimeSpan.FromMinutes(10));
        await Assert.That(rig.Wires.Count).IsEqualTo(1 + AutoReconnect.Delays.Count);
    }

    [Test]
    public async Task AConnectionThatDropsAgainAtOnceKeepsCountingButOneThatLastedStartsOver()
    {
        var rig = await Connected();
        rig.Wire.Drop(new IOException("reset"));
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        rig.Wire.Drop(new IOException("reset")); // refused login, say: straight back out

        await Assert.That(rig.Auto.SecondsLeft).IsEqualTo(15);

        rig.Time.Advance(TimeSpan.FromSeconds(15));
        rig.Time.Advance(AutoReconnect.StableAfter);
        rig.Wire.Drop(new IOException("reset"));

        await Assert.That(rig.Auto.SecondsLeft).IsEqualTo(5);
    }

    [Test]
    public async Task CancelStopsTheWaitingTry()
    {
        var rig = await Connected();
        rig.Wire.Drop(new IOException("reset"));

        rig.Auto.Cancel();
        rig.Time.Advance(TimeSpan.FromMinutes(1));

        await Assert.That(rig.Wires.Count).IsEqualTo(1);
        await Assert.That(rig.Steps[^1]).IsEqualTo(ReconnectStep.Cancelled);
    }
}
