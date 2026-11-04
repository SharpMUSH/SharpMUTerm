using SharpMUTerm.Core.Protocols;

namespace SharpMUTerm.Core.Tests.Protocols;

/// <summary>
/// Who is in the room, from GMCP: the whole list, a player arriving and leaving, and creatures from the room's item
/// list. The roster is server text, so it is bounded in count and name length and refuses names a click would turn
/// into a different command.
/// </summary>
public class RoomOccupantsTests
{
    [Test]
    public async Task RoomPlayersReplacesTheListInTheGamesOrder()
    {
        var roster = new RoomOccupants();
        roster.Apply("Room.Players", """[{"name":"Ann","fullname":"Ann the Bold"},{"name":"Bo"}]""");
        roster.Apply("Room.Players", """[{"name":"Cy"},{"name":"Ann"}]""");

        await Assert.That(roster.Known).IsTrue();
        await Assert.That(roster.Snapshot.Select(o => o.Name)).IsEquivalentTo(new[] { "Cy", "Ann" });
    }

    [Test]
    public async Task AFullNameIsShownAndTheShortNameIsKept()
    {
        var roster = new RoomOccupants();
        roster.Apply("Room.Players", """[{"name":"Ann","fullname":"Ann the Bold"}]""");

        var ann = roster.Snapshot.Single();
        await Assert.That(ann.Name).IsEqualTo("Ann");
        await Assert.That(ann.Display).IsEqualTo("Ann the Bold");
        await Assert.That(ann.IsPlayer).IsTrue();
    }

    [Test]
    public async Task AddAndRemoveChangeOneRowWhateverTheCase()
    {
        var roster = new RoomOccupants();
        roster.Apply("Room.Players", """[{"name":"Ann"}]""");
        roster.Apply("Room.AddPlayer", """{"name":"Bo","idle":"240"}""");
        roster.Apply("Room.AddPlayer", """{"name":"bo","idle":5}""");

        await Assert.That(roster.Snapshot.Count).IsEqualTo(2);
        await Assert.That(roster.Snapshot[1].IdleSeconds).IsEqualTo(5);

        await Assert.That(roster.Apply("Room.RemovePlayer", "\"BO\"")).IsTrue();
        await Assert.That(roster.Snapshot.Select(o => o.Name)).IsEquivalentTo(new[] { "Ann" });
    }

    [Test]
    public async Task CreaturesComeFromTheRoomsItemListAndOnlyMonsters()
    {
        var roster = new RoomOccupants();
        roster.Apply("Room.Players", """[{"name":"Ann"}]""");
        roster.Apply("Char.Items.List", """{"location":"room","items":[{"id":"1","name":"a rat","attrib":"m"},{"id":"2","name":"a chest","attrib":"c"}]}""");
        roster.Apply("Char.Items.List", """{"location":"inv","items":[{"id":"3","name":"a sword","attrib":"m"}]}""");

        await Assert.That(roster.Snapshot.Select(o => o.Display)).IsEquivalentTo(new[] { "Ann", "a rat" });
        await Assert.That(roster.Snapshot[1].IsPlayer).IsFalse();

        roster.Apply("Char.Items.Remove", """{"location":"room","item":{"id":"1"}}""");
        await Assert.That(roster.Snapshot.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ANameWithASpaceOrAControlCharacterIsRefused()
    {
        var roster = new RoomOccupants();
        roster.Apply("Room.Players", """[{"name":"Ann; quit"},{"name":"Bo\u001b[2J"},{"name":"Cy"}]""");

        await Assert.That(roster.Snapshot.Select(o => o.Name)).IsEquivalentTo(new[] { "Cy" });
    }

    [Test]
    public async Task TheRosterIsCapped()
    {
        var roster = new RoomOccupants();
        var many = string.Join(",", Enumerable.Range(0, RoomOccupants.MaxOccupants + 50).Select(i => $"{{\"name\":\"P{i}\"}}"));
        roster.Apply("Room.Players", $"[{many}]");

        await Assert.That(roster.Snapshot.Count).IsEqualTo(RoomOccupants.MaxOccupants);
    }

    [Test]
    public async Task OtherPackagesAndMalformedBodiesChangeNothing()
    {
        var roster = new RoomOccupants();
        var raised = 0;
        roster.Changed += (_, _) => raised++;

        await Assert.That(roster.Apply("Char.Vitals", """{"hp":"1"}""")).IsFalse();
        await Assert.That(roster.Apply("Room.Players", "[{")).IsFalse();
        await Assert.That(roster.Known).IsFalse();
        await Assert.That(raised).IsEqualTo(0);
    }

    [Test]
    public async Task AnEmptyRoomTheGameReportedIsKnown()
    {
        var roster = new RoomOccupants();
        roster.Apply("Room.Players", "[]");

        await Assert.That(roster.Known).IsTrue();
        await Assert.That(roster.Snapshot).IsEmpty();

        roster.Clear();
        await Assert.That(roster.Known).IsFalse();
    }
}

/// <summary>What the character is fighting, read from the three shapes games send it in.</summary>
public class CombatTargetTests
{
    [Test]
    public async Task AardwolfsEnemyAndPercentage()
    {
        var data = new GameDataStore();
        data.SetGmcp("Char.Status", """{"enemy":"a rotting ghoul","enemypct":"40"}""");

        var target = CombatTarget.Read(data);
        await Assert.That(target).IsNotNull();
        await Assert.That(target!.Name).IsEqualTo("a rotting ghoul");
        await Assert.That(target.Share).IsEqualTo(0.4);
    }

    [Test]
    public async Task AnEmptyEnemyIsNoTarget()
    {
        var data = new GameDataStore();
        data.SetGmcp("Char.Status", """{"enemy":"","enemypct":"0"}""");

        await Assert.That(CombatTarget.Read(data)).IsNull();
    }

    [Test]
    public async Task IresTargetInfo()
    {
        var data = new GameDataStore();
        data.SetGmcp("IRE.Target.Info", """{"id":"1","short_desc":"a goblin","hpperc":"75%"}""");

        var target = CombatTarget.Read(data)!;
        await Assert.That(target.Name).IsEqualTo("a goblin");
        await Assert.That(target.Share).IsEqualTo(0.75);
    }

    [Test]
    public async Task MsdpHealthIsAShareOfItsMaximumWhenOneIsSent()
    {
        var data = new GameDataStore();
        data.SetMsdp("""{"OPPONENT_NAME":"a troll","OPPONENT_HEALTH":"30","OPPONENT_HEALTH_MAX":"120"}""");

        var target = CombatTarget.Read(data)!;
        await Assert.That(target.Name).IsEqualTo("a troll");
        await Assert.That(target.Share).IsEqualTo(0.25);
    }

    [Test]
    public async Task ATargetWithNoHealthHasNoShare()
    {
        var data = new GameDataStore();
        data.SetGmcp("Char.Status", """{"enemy":"a shade"}""");

        await Assert.That(CombatTarget.Read(data)!.Share).IsNull();
    }
}
