using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Telnet;

namespace SharpMUTerm.Core.Tests.Session;

/// <summary>GMCP <c>Char.Login</c>: opt-in per world, credentials only when the server offers them, the connect line otherwise.</summary>
public class GmcpLoginTests
{
    private static (WorldSession Session, FakeTelnetSession Telnet) Create(bool gmcpLogin, TimeSpan? wait = null)
    {
        var telnet = new FakeTelnetSession();
        var world = new WorldDefinition { Name = "Achaea", Host = "h", Port = 23, GmcpLogin = gmcpLogin };
        var character = new CharacterDefinition { Name = "Ann", Password = "s3cret", OnConnect = "look" };
        world.Characters.Add(character);
        var session = new WorldSession(world, character, sessionFactory: _ => telnet);
        if (wait is { } w)
        {
            session.GmcpLoginWait = w;
        }

        return (session, telnet);
    }

    private static string Text(WorldSession session) =>
        string.Join("\n", session.Scrollback.Snapshot().Select(l => l.Text));

    [Test]
    public async Task OffTheConnectLineGoesOutAtOnceAndNoModuleIsAskedFor()
    {
        var (session, telnet) = Create(gmcpLogin: false);
        await session.ConnectAsync();

        await Assert.That(telnet.SentLines).IsEquivalentTo(new[] { "connect Ann s3cret", "look" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(WorldSession.GmcpModulesFor(session.World, playsMedia: false)).DoesNotContain(GmcpLogin.Module);
    }

    [Test]
    public async Task AServerOfferingCredentialsGetsThemOverGmcpAndNoConnectLine()
    {
        var (session, telnet) = Create(gmcpLogin: true);
        await Assert.That(WorldSession.GmcpModulesFor(session.World, playsMedia: false)).Contains(GmcpLogin.Module);
        await session.ConnectAsync();
        await Assert.That(telnet.SentLines).IsEmpty();

        telnet.EmitGmcp("Char.Login.Default", """{"type":["password-credentials"]}""");
        await WaitFor(() => telnet.SentLines.Count > 0);

        await Assert.That(telnet.SentGmcp).Contains(("Char.Login.Credentials", """{"account":"Ann","password":"s3cret"}"""));
        await Assert.That(telnet.SentLines).IsEquivalentTo(new[] { "look" });
        await Assert.That(Text(session)).DoesNotContain("s3cret");

        telnet.EmitGmcp("Char.Login.Result", """{"success":false,"message":"Bad\npassword."}""");
        await Assert.That(Text(session)).Contains("The GMCP login for Ann was refused: Bad password.");
    }

    [Test]
    public async Task AServerOfferingSomethingElseGetsTheConnectLine()
    {
        var (session, telnet) = Create(gmcpLogin: true);
        await session.ConnectAsync();

        telnet.EmitGmcp("Char.Login.Default", """{"type":"oauth"}""");
        await WaitFor(() => telnet.SentLines.Count >= 2);

        await Assert.That(telnet.SentLines).IsEquivalentTo(new[] { "connect Ann s3cret", "look" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(telnet.SentGmcp.Any(m => m.Package == GmcpLogin.CredentialsPackage)).IsFalse();
    }

    [Test]
    public async Task AServerThatSaysNothingGetsTheConnectLineAfterTheWaitAndOnlyOnce()
    {
        var (session, telnet) = Create(gmcpLogin: true, wait: TimeSpan.FromMilliseconds(50));
        await session.ConnectAsync();

        await WaitFor(() => telnet.SentLines.Count >= 2);
        telnet.EmitGmcp("Char.Login.Default", """{"type":["password-credentials"]}""");
        await Task.Delay(100);

        await Assert.That(telnet.SentLines).IsEquivalentTo(new[] { "connect Ann s3cret", "look" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(telnet.SentGmcp.Any(m => m.Package == GmcpLogin.CredentialsPackage)).IsFalse();
    }

    [Test]
    public async Task TheDefaultsTypeAndTheResultAreReadDefensively()
    {
        await Assert.That(GmcpLogin.OffersCredentials("""{"type":"password-credentials"}""")).IsTrue();
        await Assert.That(GmcpLogin.OffersCredentials("""{"type":[1,"PASSWORD-CREDENTIALS"]}""")).IsTrue();
        await Assert.That(GmcpLogin.OffersCredentials("not json")).IsFalse();
        await Assert.That(GmcpLogin.OffersCredentials("{}")).IsFalse();
        await Assert.That(GmcpLogin.ReadResult("""{"success":true}""")).IsEqualTo(new LoginResult(true, null));
        await Assert.That(GmcpLogin.ReadResult("""{"message":"x"}""")).IsNull();
        var body = System.Text.Json.Nodes.JsonNode.Parse(GmcpLogin.Credentials("A\"n", "p\\w"))!;
        await Assert.That(body["account"]!.GetValue<string>()).IsEqualTo("A\"n");
        await Assert.That(body["password"]!.GetValue<string>()).IsEqualTo("p\\w");
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }
}
