using System.Text;
using SharpMUTerm.Core.WorldIndex;

namespace SharpMUTerm.Core.Tests.WorldIndex;

/// <summary>
/// mu*index's answers read as untrusted server text: rows bounded and cleaned, a bad row dropped and counted, an address
/// only what <c>/connect</c> would take. The fixtures are shaped from MUIndex's <c>GameListView</c>/<c>GameView</c>
/// (camelCase, enums as camelCase strings); the live service was not reachable when they were written.
/// </summary>
public class WorldDirectoryTests
{
    private const string Listing = """
        {
          "games": [
            { "id": "6f1c2a52-0000-4000-8000-0c6a1f3d2e01", "name": "Aetherfall", "tagline": "Sky islands",
              "state": "active", "archived": false, "playersNow": 64, "playersNowState": "measured",
              "codebase": "PennMUSH", "measuredProtocols": ["gmcp", "mssp"],
              "provenance": { "name": "crawler" }, "facets": { "genre": ["fantasy"] } },
            { "id": "6f1c2a52-0000-4000-8000-0c6a1f3d2e02", "name": "Hollow Crown", "tagline": "Courts",
              "playersNow": 33, "playersNowState": "declared", "codebase": "TinyMUX" },
            { "id": "6f1c2a52-0000-4000-8000-0c6a1f3d2e03", "name": "Copperline", "playersNow": 12, "playersNowState": "unknown" },
            { "id": "6f1c2a52-0000-4000-8000-0c6a1f3d2e04", "name": "Gone Away", "archived": true },
            { "id": "", "name": "No id" },
            { "id": "6f1c2a52-0000-4000-8000-0c6a1f3d2e05", "name": "   " },
            { "id": "6f1c2a52-0000-4000-8000-0c6a1f3d2e01", "name": "Aetherfall again" }
          ],
          "total": 412
        }
        """;

    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    [Test]
    public async Task AListingIsReadAndItsBadRowsAreDroppedAndCounted()
    {
        var (page, problem) = WorldDirectory.ParseList(Bytes(Listing));

        await Assert.That(problem).IsNull();
        await Assert.That(string.Join("|", page!.Rows.Select(r => r.Name))).IsEqualTo("Aetherfall|Hollow Crown|Copperline");
        await Assert.That(page.Dropped).IsEqualTo(4);
        await Assert.That(page.Total).IsEqualTo(412);
        await Assert.That(page.Rows[0].Players).IsEqualTo(64);
        await Assert.That(page.Rows[0].Count).IsEqualTo(WorldPlayerCount.Measured);
        await Assert.That(page.Rows[1].Count).IsEqualTo(WorldPlayerCount.Declared);
        await Assert.That(page.Rows[2].Players).IsNull().Because("a number the directory does not say it measured is not shown");
    }

    [Test]
    public async Task NamesAndTaglinesAreStrippedOfControlAndBidiCharactersAndBounded()
    {
        var json = "{\"games\":[{\"id\":\"6f1c2a52-0000-4000-8000-0c6a1f3d2e01\",\"name\":\"Evil\\u001b[31m\\u202eName\\n\",\"tagline\":\""
            + new string('x', 500) + "\"}]}";

        var row = WorldDirectory.ParseList(Bytes(json)).Page!.Rows.Single();

        await Assert.That(row.Name).IsEqualTo("Evil [31m Name");
        await Assert.That(row.Tagline.Length).IsEqualTo(WorldDirectory.MaxTagline);
        await Assert.That(row.Tagline).EndsWith("…");
    }

    [Test]
    public async Task NoMoreThanMaxRowsAreKept()
    {
        var rows = string.Join(',', Enumerable.Range(1, WorldDirectory.MaxRows + 5)
            .Select(i => $"{{\"id\":\"{new Guid(i, 0, 0, new byte[8])}\",\"name\":\"W{i}\"}}"));

        var page = WorldDirectory.ParseList(Bytes("{\"games\":[" + rows + "]}")).Page!;

        await Assert.That(page.Rows.Count).IsEqualTo(WorldDirectory.MaxRows);
        await Assert.That(page.Dropped).IsEqualTo(5);
    }

    [Test]
    public async Task AnAnswerThatIsNotAListSaysSo()
    {
        await Assert.That(WorldDirectory.ParseList(Bytes("not json")).Problem).IsEqualTo("the answer is not JSON the client can read");
        await Assert.That(WorldDirectory.ParseList(Bytes("[]")).Problem).IsEqualTo("the answer is not a list of worlds");
    }

    [Test]
    public async Task TheAddressIsAnActiveTelnetOrTlsEndpointSeenMostRecently()
    {
        const string game = """
            { "id": "6f1c2a52-0000-4000-8000-0c6a1f3d2e01", "name": "Aetherfall",
              "endpoints": [
                { "host": "old.example", "port": 4000, "kind": "telnet", "state": "gone", "lastSeenAt": "2026-10-30T00:00:00Z" },
                { "host": "ws.example", "port": 443, "kind": "websocket", "state": "active" },
                { "host": "stale.example", "port": 4000, "kind": "telnet", "state": "stale", "lastSeenAt": "2026-11-01T00:00:00Z" },
                { "host": "a.example", "port": 4201, "kind": "telnet", "state": "active", "lastSeenAt": "2026-10-01T00:00:00Z" },
                { "host": "b.example", "port": 4202, "kind": "tls", "state": "active", "lastSeenAt": "2026-11-02T00:00:00Z" }
              ] }
            """;

        var (address, problem) = WorldDirectory.ParseGame(Bytes(game));

        await Assert.That(problem).IsNull();
        await Assert.That(address).IsEqualTo(new WorldAddress("Aetherfall", "b.example", 4202, true));
    }

    [Test]
    public async Task AStaleEndpointIsOfferedWhenNoneIsActiveAndTlsMeasuredCounts()
    {
        const string game = """
            { "name": "Copperline", "endpoints": [
                { "host": "c.example", "port": 23, "kind": "telnet", "state": "stale", "tlsMeasured": true } ] }
            """;

        await Assert.That(WorldDirectory.ParseGame(Bytes(game)).Address).IsEqualTo(new WorldAddress("Copperline", "c.example", 23, true));
    }

    [Test]
    public async Task AHostOrPortConnectWouldRefuseIsNeverOffered()
    {
        const string game = """
            { "name": "Bad", "endpoints": [
                { "host": "evil host", "port": 4000, "kind": "telnet", "state": "active" },
                { "host": "ok.example", "port": 0, "kind": "telnet", "state": "active" },
                { "host": "ok.example", "port": 70000, "kind": "telnet", "state": "active" },
                { "host": "x.example", "port": 4000, "kind": "telnet", "state": "gone" } ] }
            """;

        var (address, problem) = WorldDirectory.ParseGame(Bytes(game));

        await Assert.That(address).IsNull();
        await Assert.That(problem).IsEqualTo("the directory lists no telnet address for it that answers");
        await Assert.That(WorldDirectory.ParseGame(Bytes("{\"name\":\"N\"}")).Problem).IsEqualTo("the directory lists no address for it");
    }

    [Test]
    public async Task TheListIsAskedOverHttpsOnlyWithTheQueryEscaped()
    {
        var list = WorldDirectory.ListUri("https://mu-index.com/api/", "red & blue")!.AbsoluteUri;
        await Assert.That(list).StartsWith("https://mu-index.com/api/games?limit=200&sort=players&q=red");
        await Assert.That(list).Contains("%26");
        await Assert.That(WorldDirectory.ListUri("http://mu-index.com/api")).IsNull();
        await Assert.That(WorldDirectory.ListUri("https://user@mu-index.com/api")).IsNull();
        await Assert.That(WorldDirectory.GameUri("https://mu-index.com/api", Guid.Parse("6f1c2a52-0000-4000-8000-0c6a1f3d2e01"))!.AbsoluteUri)
            .IsEqualTo("https://mu-index.com/api/games/6f1c2a52-0000-4000-8000-0c6a1f3d2e01");
    }

    [Test]
    public async Task ProblemNamesTheSettingWhenThereIsNoUsableDirectory()
    {
        await Assert.That(WorldDirectory.Problem(SharpMUTerm.Core.Configuration.AppConfiguration.DefaultWorldDirectoryUrl)).IsNull();
        await Assert.That(WorldDirectory.Problem("")).Contains("no world directory is set");
        await Assert.That(WorldDirectory.Problem("http://mu-index.com/api")).Contains("not an https link");
    }

    [Test]
    public async Task ADownloadOfAPlainHttpLinkFetchesNothing()
    {
        await Assert.That(await WorldDirectory.DownloadAsync(new Uri("http://mu-index.com/api/games"), CancellationToken.None)).IsNull();
    }
}
