using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.WorldIndex;

namespace SharpMUTerm.Client.Tests;

/// <summary>Browse worlds' one model: what both hosts list for a query and an order, and what they say in place of rows.</summary>
public class WorldBrowseModelTests
{
    private static WorldListing Row(int n, string name, string codebase, int? players, WorldPlayerCount count = WorldPlayerCount.Measured, string tagline = "") =>
        new(new Guid(n, 0, 0, new byte[8]), name, tagline, codebase, players, players is null ? WorldPlayerCount.Unknown : count);

    private static readonly WorldListing[] Rows =
    [
        Row(1, "Aetherfall", "PennMUSH", 64, tagline: "Sky islands"),
        Row(2, "Hollow Crown", "TinyMUX", 33, WorldPlayerCount.Declared),
        Row(3, "copperline", "Evennia", null),
    ];

    private static WorldBrowseModel Ready(int total = 3, string asked = "") => new(WorldBrowseState.Ready, Rows, total, asked, null, null);

    [Test]
    public async Task ByPlayersKeepsTheDirectorysOrderAndByNameIsAlphabetical()
    {
        await Assert.That(string.Join("|", Ready().View("", WorldDirectorySort.Players).Select(r => r.Name)))
            .IsEqualTo("Aetherfall|Hollow Crown|copperline");
        await Assert.That(string.Join("|", Ready().View(null, WorldDirectorySort.Name).Select(r => r.Name)))
            .IsEqualTo("Aetherfall|copperline|Hollow Crown");
    }

    [Test]
    public async Task EveryWordMustMatchANameCodebaseOrTagline()
    {
        await Assert.That(Ready().View("sky penn", WorldDirectorySort.Players).Single().Name).IsEqualTo("Aetherfall");
        await Assert.That(Ready().View("tinymux", WorldDirectorySort.Players).Single().Name).IsEqualTo("Hollow Crown");
        await Assert.That(Ready().View("sky mux", WorldDirectorySort.Players).Count).IsEqualTo(0);
    }

    [Test]
    public async Task PlayersMarkADeclaredCountAndDashAnUnknownOne()
    {
        await Assert.That(WorldBrowseModel.Players(Rows[0])).IsEqualTo("64");
        await Assert.That(WorldBrowseModel.Players(Rows[1])).IsEqualTo("~33");
        await Assert.That(WorldBrowseModel.Players(Rows[2])).IsEqualTo("–");
        await Assert.That(Ready().AnyDeclared).IsTrue();
    }

    [Test]
    public async Task EnterAsksTheDirectoryOnlyWhenTheRowsHeldCannotAnswer()
    {
        await Assert.That(Ready().AsksDirectory("aether")).IsFalse().Because("a row matches; ⏎ picks it");
        await Assert.That(Ready().AsksDirectory("zork")).IsTrue();
        await Assert.That(Ready(asked: "zork").AsksDirectory("zork")).IsFalse().Because("the directory was asked that already and sent all it holds");
        await Assert.That(Ready(total: 400, asked: "zork").AsksDirectory("zork")).IsTrue();
        await Assert.That(Ready().AsksDirectory("  ")).IsFalse();
        await Assert.That((Ready() with { State = WorldBrowseState.Loading }).AsksDirectory("zork")).IsFalse();
    }

    [Test]
    public async Task EachStateSaysWhatIsTrue()
    {
        var empty = new WorldBrowseModel(WorldBrowseState.Ready, [], 0, string.Empty, null, null);
        await Assert.That(new WorldBrowseModel(WorldBrowseState.Loading, [], 0, "", null, null).Empty("")).IsEqualTo(WorldBrowseModel.LoadingLine);
        await Assert.That(new WorldBrowseModel(WorldBrowseState.Failed, [], 0, "", "it timed out", null).Empty("")).IsEqualTo("mu*index did not answer: it timed out");
        await Assert.That(new WorldBrowseModel(WorldBrowseState.Unavailable, [], 0, "", "nothing was fetched", null).Empty("")).IsEqualTo("nothing was fetched");
        await Assert.That(empty.Empty("")).IsEqualTo("mu*index lists no worlds right now");
        await Assert.That((empty with { AskedFor = "zork" }).Empty("zork")).IsEqualTo("mu*index lists no world for “zork”");
        await Assert.That(Ready().Empty("zork")).Contains("⏎ asks mu*index for “zork”");
        await Assert.That(Ready(asked: "zork").Empty("zork")).IsEqualTo("nothing listed matches — ⌫ widens the search");
        await Assert.That(Ready().Empty("aether")).IsNull();
    }

    [Test]
    public async Task TheCountSaysHowManyOfHowMany()
    {
        await Assert.That(Ready().CountLine("")).IsEqualTo("3 worlds");
        await Assert.That(Ready(total: 412).CountLine("")).IsEqualTo("3 of 412 worlds");
        await Assert.That(Ready().CountLine("aether")).IsEqualTo("1 of 3");
    }

    [Test]
    public async Task TwoModelsOverEqualRowsAreEqual()
    {
        var copy = new WorldBrowseModel(WorldBrowseState.Ready, Rows.ToList(), 3, "", null, null);
        await Assert.That(copy).IsEqualTo(Ready());
        await Assert.That(copy.GetHashCode()).IsEqualTo(Ready().GetHashCode());
        await Assert.That(copy with { Opening = "Aetherfall" }).IsNotEqualTo(Ready());
    }

    [Test]
    public async Task ASessionPicksARowInRangeAndAsksOnlyWhatTheRowsCannotAnswer()
    {
        var picked = new List<int>();
        var asked = new List<string>();
        var session = new WorldBrowseSession(Ready(), true, picked.Add, asked.Add, () => { });

        session.Pick(1);
        session.Pick(9);
        session.Ask("aether");
        session.Ask(" zork ");

        await Assert.That(string.Join(",", picked)).IsEqualTo("1");
        await Assert.That(string.Join(",", asked)).IsEqualTo("zork");
    }
}
