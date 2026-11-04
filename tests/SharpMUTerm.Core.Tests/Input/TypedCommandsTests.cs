using SharpMUTerm.Core.Automation;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Input;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Tests.Session;

namespace SharpMUTerm.Core.Tests.Input;

/// <summary>Command stacking for typed lines: <c>n;e;e</c>, <c>#3 kill rat</c>, and nothing at all on a world that has none.</summary>
public class TypedCommandsTests
{
    [Test]
    public async Task WithNoSeparatorALineGoesAsTypedHashAndAll()
    {
        await Assert.That(TypedCommands.Expand("say a;b", null).Commands).IsEquivalentTo(["say a;b"]);
        await Assert.That(TypedCommands.Expand("#123 @set me=x", null).Commands).IsEquivalentTo(["#123 @set me=x"]);
    }

    [Test]
    public async Task ASeparatorSplitsTrimsAndDropsEmptyCommands()
    {
        var result = TypedCommands.Expand("n; e ;;e;", ";");

        await Assert.That(result.Refusal).IsNull();
        await Assert.That(result.Commands).IsEquivalentTo(["n", "e", "e"]);
    }

    [Test]
    public async Task ABackslashMakesTheSeparatorLiteral()
    {
        await Assert.That(TypedCommands.Expand(@"say a\;b;look", ";").Commands).IsEquivalentTo(["say a;b", "look"]);
        await Assert.That(TypedCommands.Expand(@"say c:\dir", ";").Commands).IsEquivalentTo([@"say c:\dir"]);
    }

    [Test]
    public async Task AMultiCharacterSeparatorLeavesASingleOneAlone()
    {
        await Assert.That(TypedCommands.Expand("say a;b;;look", ";;").Commands).IsEquivalentTo(["say a;b", "look"]);
    }

    [Test]
    public async Task HashNRepeatsACommandWithinEachPiece()
    {
        await Assert.That(TypedCommands.Expand("#3 kill rat;#2 e", ";").Commands)
            .IsEquivalentTo(["kill rat", "kill rat", "kill rat", "e", "e"]);
        await Assert.That(TypedCommands.Expand("#abc;#;#5", ";").Commands).IsEquivalentTo(["#abc", "#", "#5"]);
    }

    [Test]
    public async Task ARepeatOutOfBoundsSendsNothingAtAll()
    {
        foreach (var line in new[] { "n;#0 e", "n;#101 e", "#99999999999 e" })
        {
            var result = TypedCommands.Expand(line, ";");
            await Assert.That(result.Commands).IsEmpty();
            await Assert.That(result.Refusal).IsNotNull();
        }
    }

    [Test]
    public async Task ALineComingToMoreThanTheCapSendsNothing()
    {
        var result = TypedCommands.Expand("#100 n;e", ";");

        await Assert.That(result.Commands).IsEmpty();
        await Assert.That(result.Refusal!).Contains("100");
        await Assert.That(TypedCommands.Expand("#100 n", ";").Commands.Count).IsEqualTo(100);
    }

    private static (WorldSession Session, FakeTelnetSession Telnet) Connected(string? separator, params Alias[] aliases)
    {
        var telnet = new FakeTelnetSession();
        var world = new WorldDefinition { Name = "T", Host = "h", Port = 1, LocalEcho = true, CommandSeparator = separator };
        var set = new TriggerSet { Name = "s", Aliases = [.. aliases] };
        var session = new WorldSession(world, triggerSets: [set], sessionFactory: _ => telnet);
        return (session, telnet);
    }

    [Test]
    public async Task ATypedLineIsStackedThenEachCommandIsAliasExpandedAndEchoedOnce()
    {
        var (session, telnet) = Connected(";", new Alias { Name = "k", Pattern = "^k (.+)$", Substitution = "kill $1" });
        await session.ConnectAsync();
        var echoed = new List<string>();
        session.LinePrinted += (_, line) => echoed.Add(line.Text);

        await session.SendTypedAsync("#2 k rat;n");

        await Assert.That(telnet.SentLines).IsEquivalentTo(["kill rat", "kill rat", "n"]);
        await Assert.That(echoed).IsEquivalentTo(["#2 k rat;n"]);
    }

    [Test]
    public async Task APostAndAScriptAreNeverStacked()
    {
        var (session, telnet) = Connected(";");
        await session.ConnectAsync();

        await session.SendUserInputAsync("+bbpost 1=Title/one; two");
        await session.SendRawAsync("say x;y");

        await Assert.That(telnet.SentLines).IsEquivalentTo(["+bbpost 1=Title/one; two", "say x;y"]);
    }

    [Test]
    public async Task ARefusedLineSaysWhyAndSendsNothing()
    {
        var (session, telnet) = Connected(";");
        await session.ConnectAsync();
        var printed = new List<string>();
        session.LinePrinted += (_, line) => printed.Add(line.Text);

        await session.SendTypedAsync("#500 n");

        await Assert.That(telnet.SentLines).IsEmpty();
        await Assert.That(printed[^1]).Contains("nothing was sent");
    }
}
