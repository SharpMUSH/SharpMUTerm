using System.Text;
using SharpMUTerm.Core.WorldIndex;

namespace SharpMUTerm.Core.Tests.WorldIndex;

/// <summary>
/// mu*index's answers read as untrusted server text and otherwise as the directory states them: rows kept in its order
/// with its counts and labels, text bounded and cleaned, sizes capped, and an address put in the connect form only when
/// <c>/connect</c> would take it. The fixtures are shaped from MUIndex's <c>GameListView</c>/<c>GameView</c>
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
    public async Task AListingIsReadAsTheDirectoryStatesItEveryRowInItsOrder()
    {
        var (page, problem) = WorldDirectory.ParseList(Bytes(Listing));

        await Assert.That(problem).IsNull();
        await Assert.That(string.Join("|", page!.Rows.Select(r => r.Name)))
            .IsEqualTo("Aetherfall|Hollow Crown|Copperline|Gone Away|No id||Aetherfall again")
            .Because("the client drops no archived, repeated, unnamed or id-less row: the directory decides what it lists");
        await Assert.That(page.Dropped).IsEqualTo(0);
        await Assert.That(page.Total).IsEqualTo(412);
        await Assert.That(page.Rows[0].Players).IsEqualTo(64);
        await Assert.That(page.Rows[0].Count).IsEqualTo(WorldPlayerCount.Measured);
        await Assert.That(page.Rows[1].Count).IsEqualTo(WorldPlayerCount.Declared);
        await Assert.That(page.Rows[2].Players).IsEqualTo(12).Because("a count is shown with the directory's own label");
        await Assert.That(page.Rows[2].Count).IsEqualTo(WorldPlayerCount.Unknown);
        await Assert.That(page.Rows[4].Id).IsEqualTo(Guid.Empty);
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
        await Assert.That(page.Dropped).IsEqualTo(5).Because("the row cap is the one thing that leaves rows unread");
    }

    [Test]
    public async Task AnAnswerThatIsNotAListSaysSo()
    {
        await Assert.That(WorldDirectory.ParseList(Bytes("not json")).Problem).IsEqualTo("the answer is not JSON the client can read");
        await Assert.That(WorldDirectory.ParseList(Bytes("[]")).Problem).IsEqualTo("the answer is not a list of worlds");
    }

    [Test]
    public async Task AddressesAreListedInTheDirectorysOrderLeavingOutOnlyGoneAndUndialableKinds()
    {
        const string game = """
            { "id": "6f1c2a52-0000-4000-8000-0c6a1f3d2e01", "name": "Aetherfall",
              "endpoints": [
                { "host": "old.example", "port": 4000, "kind": "telnet", "state": "gone", "lastSeenAt": "2026-10-30T00:00:00Z" },
                { "host": "ws.example", "port": 443, "kind": "websocket", "state": "active" },
                { "host": "stale.example", "port": 4000, "kind": "telnet", "state": "stale", "lastSeenAt": "2026-11-01T00:00:00Z" },
                { "host": "a.example", "port": 4201, "kind": "telnet", "state": "active", "lastSeenAt": "2026-10-01T00:00:00Z" },
                { "host": "b.example", "port": 4202, "kind": "tls", "state": "active", "lastSeenAt": "2026-11-02T00:00:00Z" },
                { "host": "a.example", "port": 4201, "kind": "telnet", "state": "active" }
              ] }
            """;

        var (parsed, problem) = WorldDirectory.ParseGame(Bytes(game));

        await Assert.That(problem).IsNull();
        await Assert.That(string.Join(",", parsed!.Endpoints.Select(e => $"{e.Address}:{(e.Tls ? "tls" : "plain")}:{(e.Stale ? "stale" : "active")}")))
            .IsEqualTo("stale.example:4000:plain:stale,a.example:4201:plain:active,b.example:4202:tls:active,a.example:4201:plain:active")
            .Because("the client neither re-ranks nor de-duplicates what the directory lists");
        await Assert.That(parsed.FirstConnectable).IsEqualTo(0);
        await Assert.That(parsed.Address(2)).IsEqualTo(new WorldAddress("Aetherfall", "b.example", 4202, true));
        await Assert.That(parsed.Charset).IsNull();
    }

    [Test]
    public async Task NoMoreThanMaxEndpointsAreOffered()
    {
        var endpoints = string.Join(',', Enumerable.Range(1, 20).Select(i => $"{{\"host\":\"h{i}.example\",\"port\":{i},\"kind\":\"telnet\",\"state\":\"active\"}}"));

        var parsed = WorldDirectory.ParseGame(Bytes("{\"name\":\"W\",\"endpoints\":[" + endpoints + "]}")).Game!;

        await Assert.That(parsed.Endpoints.Count).IsEqualTo(WorldDirectory.MaxEndpoints);
        await Assert.That(parsed.Endpoints[0].Host).IsEqualTo("h1.example");
    }

    [Test]
    public async Task TheGamesCharsetIsReadFromItsFieldsAndANonUtf8OneSetsTheEncoding()
    {
        static WorldGame Game(string charset) => WorldDirectory.ParseGame(Bytes(
            "{\"name\":\"W\",\"endpoints\":[{\"host\":\"w.example\",\"port\":4000,\"kind\":\"telnet\",\"state\":\"active\"}],"
            + "\"fields\":{\"CHARSET\":{\"value\":" + charset + ",\"source\":\"handshake\",\"measured\":true}}}")).Game!;

        await Assert.That(Game("\"ISO-8859-1\"").Charset).IsEqualTo("ISO-8859-1");
        await Assert.That(Game("\"ISO-8859-1\"").Address(0).Encoding).IsEqualTo("ISO-8859-1");
        await Assert.That(Game("\"latin1\"").Encoding).IsEqualTo("ISO-8859-1");
        await Assert.That(Game("\"windows-1252\"").Encoding).IsEqualTo("CP1252");
        await Assert.That(Game("\"UTF-8\"").Charset).IsEqualTo("UTF-8");
        await Assert.That(Game("\"UTF-8\"").Encoding).IsNull().Because("UTF-8 leaves the world's encoding auto");
        await Assert.That(Game("\"US-ASCII\"").Encoding).IsNull().Because("UTF-8 reads ASCII");
        await Assert.That(Game("\"KOI8-R\"").Encoding).IsNull().Because("a charset F5 does not offer is not guessed at");
        await Assert.That(Game("\"\\u001b[31mevil\\u202echarset\"").Charset).IsEqualTo("[31mevil charset").Because("control and bidi characters are stripped");
        await Assert.That(Game("\"\\u001b[31mevil\\u202echarset\"").Encoding).IsNull();
        await Assert.That(Game("\"" + new string('a', 200) + "\"").Charset!.Length).IsEqualTo(WorldDirectory.MaxCharset).Because("a charset name is bounded");
        await Assert.That(Game("12").Charset).IsNull();
    }

    [Test]
    public async Task AStaleEndpointIsOfferedWhenNoneIsActiveAndTlsMeasuredCounts()
    {
        const string game = """
            { "name": "Copperline", "endpoints": [
                { "host": "c.example", "port": 23, "kind": "telnet", "state": "stale", "tlsMeasured": true } ] }
            """;

        var parsed = WorldDirectory.ParseGame(Bytes(game)).Game!;
        await Assert.That(parsed.Endpoints.Count).IsEqualTo(1);
        await Assert.That(parsed.Address(0)).IsEqualTo(new WorldAddress("Copperline", "c.example", 23, true));
    }

    [Test]
    public async Task AnAddressConnectWouldRefuseIsListedAsNotConnectableAndNeverFillsTheForm()
    {
        const string game = """
            { "name": "Bad", "endpoints": [
                { "host": "evil host", "port": 4000, "kind": "telnet", "state": "active" },
                { "host": "ok.example", "port": 0, "kind": "telnet", "state": "active" },
                { "host": "ok.example", "port": 70000, "kind": "telnet", "state": "active" },
                { "host": "x.example", "port": 4000, "kind": "telnet", "state": "gone" } ] }
            """;

        var (parsed, problem) = WorldDirectory.ParseGame(Bytes(game));

        await Assert.That(problem).IsNull();
        await Assert.That(parsed!.Endpoints.Count).IsEqualTo(3).Because("the gone one is left out, the three it cannot dial are listed");
        await Assert.That(parsed.Endpoints.All(e => !e.Connectable)).IsTrue();
        await Assert.That(parsed.FirstConnectable).IsEqualTo(-1);
        await Assert.That(parsed.Endpoints[0].Host).IsEqualTo("evil host");
        await Assert.That(WorldDirectory.ParseGame(Bytes("{\"name\":\"N\",\"endpoints\":[]}")).Problem).IsEqualTo("the directory lists no telnet address for it");
        await Assert.That(WorldDirectory.ParseGame(Bytes("{\"name\":\"N\"}")).Problem).IsEqualTo("the directory lists no address for it");
    }

    [Test]
    public async Task TheListIsAskedOverHttpsOnlyWithTheQueryEscaped()
    {
        var list = WorldDirectory.ListUri("https://mu-index.com/api/", "red & blue")!.AbsoluteUri;
        await Assert.That(list).StartsWith("https://mu-index.com/api/games?limit=200&sort=players&unnamed=!yes&q=red");
        await Assert.That(WorldDirectory.ListUri("https://mu-index.com/api")!.AbsoluteUri)
            .IsEqualTo("https://mu-index.com/api/games?limit=200&sort=players&unnamed=!yes")
            .Because("every list asks the directory to leave out games listed only by host:port");
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
