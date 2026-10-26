using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Core.Tests.Session;

/// <summary>
/// Every rule kind with a <c>script</c> field asks for it through one event, <see cref="WorldSession.ScriptRequested"/>.
/// Before it, a trigger raised an event nothing subscribed to, and aliases, timers and macros raised nothing at
/// all: the F2 <c>script</c> field could be filled in and saved, and nothing ever ran.
/// </summary>
public class WorldSessionScriptRequestTests
{
    private static WorldDefinition World() => new() { Name = "T", Host = "h", Port = 1 };

    private static (WorldSession Session, FakeTelnetSession Telnet, List<ScriptRequest> Requests) Create(TriggerSet set)
    {
        var telnet = new FakeTelnetSession();
        var session = new WorldSession(World(), triggerSets: new[] { set }, sessionFactory: _ => telnet);
        var requests = new List<ScriptRequest>();
        session.ScriptRequested += (_, e) =>
        {
            lock (requests)
            {
                requests.Add(e);
            }
        };
        return (session, telnet, requests);
    }

    [Test]
    public async Task ATriggerAsksForItsScriptWithTheWholeMatchAndItsGroups()
    {
        var set = new TriggerSet { Name = "s" };
        set.Triggers.Add(new Trigger
        {
            Name = "tells",
            Pattern = @"^(\w+) tells you '(.*)'$",
            Actions = new TriggerActions { ScriptCallback = "on_tell" },
        });
        var (session, telnet, requests) = Create(set);
        await session.ConnectAsync();

        telnet.EmitLine("Ann tells you 'hi'");

        await Assert.That(requests).HasSingleItem();
        var request = requests[0];
        await Assert.That(request.Source).IsEqualTo(ScriptSource.Trigger);
        await Assert.That(request.Callback).IsEqualTo("on_tell");
        await Assert.That(request.WholeMatch).IsEqualTo("Ann tells you 'hi'");
        await Assert.That(request.Groups).IsEquivalentTo(new[] { "Ann", "hi" });
    }

    [Test]
    public async Task AnAliasAsksForItsScriptAndStillSendsItsSubstitution()
    {
        var set = new TriggerSet { Name = "s" };
        set.Aliases.Add(new Alias { Pattern = @"^k (\w+)$", Substitution = "kill $1", ScriptCallback = "on_kill" });
        var (session, telnet, requests) = Create(set);
        await session.ConnectAsync();

        await session.SendUserInputAsync("k orc");

        await Assert.That(requests).HasSingleItem();
        await Assert.That(requests[0].Source).IsEqualTo(ScriptSource.Alias);
        await Assert.That(requests[0].Groups).IsEquivalentTo(new[] { "orc" });
        await Assert.That(telnet.SentLines).Contains("kill orc");
    }

    /// <summary>A script-only alias swallows the line: it matched, so the raw input is not sent as well.</summary>
    [Test]
    public async Task AScriptOnlyAliasSendsNothingItself()
    {
        var set = new TriggerSet { Name = "s" };
        set.Aliases.Add(new Alias { Pattern = "^go$", ScriptCallback = "walk" });
        var (session, telnet, requests) = Create(set);
        await session.ConnectAsync();

        await session.SendUserInputAsync("go");

        await Assert.That(requests).HasSingleItem();
        await Assert.That(telnet.SentLines).IsEmpty();
    }

    [Test]
    public async Task AMacroAsksForItsScriptAndSendsNoEmptyCommand()
    {
        var set = new TriggerSet { Name = "s" };
        set.Macros.Add(new Macro { Key = "F12", ScriptCallback = "panic" });
        var (session, telnet, requests) = Create(set);
        await session.ConnectAsync();

        var sent = await session.HandleKeyAsync("F12");

        await Assert.That(sent).IsNull();
        await Assert.That(requests).HasSingleItem();
        await Assert.That(requests[0].Source).IsEqualTo(ScriptSource.Macro);
        await Assert.That(requests[0].Callback).IsEqualTo("panic");
        await Assert.That(telnet.SentLines).IsEmpty();
    }

    [Test]
    public async Task ATimerAsksForItsScriptEachTimeItFires()
    {
        var set = new TriggerSet { Name = "s" };
        set.Timers.Add(new TimerDefinition { Name = "tick", IntervalSeconds = 0.02, ScriptCallback = "on_tick" });
        var (session, telnet, requests) = Create(set);
        await session.ConnectAsync();

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && Count(requests) < 2)
        {
            await Task.Delay(10);
        }

        await session.DisconnectAsync();
        await Assert.That(Count(requests)).IsGreaterThanOrEqualTo(2);
        await Assert.That(requests[0].Source).IsEqualTo(ScriptSource.Timer);
        await Assert.That(telnet.SentLines).IsEmpty();
    }

    private static int Count(List<ScriptRequest> requests)
    {
        lock (requests)
        {
            return requests.Count;
        }
    }
}
