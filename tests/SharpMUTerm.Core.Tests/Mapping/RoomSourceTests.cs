using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Mapping;
using SharpMUTerm.Core.Session;
using SharpMUTerm.Core.Tests.Session;

namespace SharpMUTerm.Core.Tests.Mapping;

public class RoomSourceTests
{
    [Test]
    public async Task IronRealmsRoomInfo()
    {
        var room = GmcpRoomSource.Read("Room.Info", """
            {"num":12345,"name":"A shady lane","area":"Ashtan","environment":"Urban",
             "coords":"45,5,-4,1","map":"www.example.com/map.php?45","exits":{"n":12344,"se":12350},"details":["shop"]}
            """)!;

        await Assert.That(room.Id).IsEqualTo(12345L);
        await Assert.That((room.Name, room.Area, room.Environment)).IsEqualTo(("A shady lane", "Ashtan", "Urban"));
        await Assert.That(room.Coordinates).IsEqualTo((5, -4, 1));
        await Assert.That(room.Exits).IsEquivalentTo(new[] { new ObservedExit("north", 12344), new ObservedExit("southeast", 12350) });
    }

    [Test]
    public async Task AardwolfRoomInfo()
    {
        var room = GmcpRoomSource.Read("room.info", """
            {"num":32418,"name":"The Grand City of Aylor","zone":"aylor","terrain":"city",
             "details":"","exits":{"n":32416,"e":32419},"coord":{"id":0,"x":30,"y":19,"cont":0}}
            """)!;

        await Assert.That(room.Id).IsEqualTo(32418L);
        await Assert.That((room.Area, room.Environment)).IsEqualTo(("aylor", "city"));
        await Assert.That(room.Coordinates).IsEqualTo((30, 19, 0));
        await Assert.That(room.Exits.Count).IsEqualTo(2);
    }

    [Test]
    public async Task MumeRoomInfoAndUpdateExits()
    {
        var room = GmcpRoomSource.Read("Room.Info", """
            {"id":77,"name":"Ford","area":"Shire","terrain":"river",
             "exits":{"north":{"id":78,"name":"","flags":["door"]},"west":{"name":"","flags":[]}}}
            """)!;

        await Assert.That(room.Id).IsEqualTo(77L);
        await Assert.That(room.Environment).IsEqualTo("river");
        await Assert.That(room.Exits).IsEquivalentTo(new[] { new ObservedExit("north", 78), new ObservedExit("west", null) });

        var update = GmcpRoomSource.Read("Room.UpdateExits", """{"east":{"id":79}}""")!;
        await Assert.That(update.ExitsOnly).IsTrue();
        await Assert.That(update.Exits.Single()).IsEqualTo(new ObservedExit("east", 79));
    }

    [Test]
    [Arguments("Char.Vitals", """{"hp":1}""")]
    [Arguments("Room.Info", "not json")]
    [Arguments("Room.Info", """{"name":"no number"}""")]
    public async Task NotARoom(string package, string json) =>
        await Assert.That(GmcpRoomSource.Read(package, json)).IsNull();

    [Test]
    public async Task TheTextSourceReadsANameAndItsExits()
    {
        var session = new WorldSession(new WorldDefinition { Name = "T", Host = "h", Port = 1 }, sessionFactory: _ => new FakeTelnetSession());
        using var source = new TextRoomSource(session, new MapSettings
        {
            RoomNamePattern = @"^\*\* (?<name>.+) \*\*$",
            ExitsPattern = @"^\[Exits: (?<exits>.*)\]$",
        });
        var seen = new List<RoomObservation>();
        source.RoomEntered += (_, room) => seen.Add(room);

        source.Observe("** Town Square **");
        source.Observe("A fountain splashes.");
        source.Observe("[Exits: north, e and enter portal]");
        source.Observe("[Exits: none]");

        await Assert.That(seen.Count).IsEqualTo(2);
        await Assert.That(seen[0].Name).IsEqualTo("Town Square");
        await Assert.That(seen[0].Id).IsNull();
        await Assert.That(seen[0].Exits.Select(e => e.Key)).IsEquivalentTo(new[] { "north", "east", "enter", "portal" });
        await Assert.That(seen[1].Exits).IsEmpty();
    }

    [Test]
    public async Task ABadPatternSwitchesTheTextSourceOffAndSaysSo()
    {
        var session = new WorldSession(new WorldDefinition { Name = "T", Host = "h", Port = 1 }, sessionFactory: _ => new FakeTelnetSession());
        using var source = new TextRoomSource(session, new MapSettings { ExitsPattern = "([" });

        await Assert.That(source.IsActive).IsFalse();
        await Assert.That(source.Problem).Contains("exits pattern");
    }

    /// <summary>The mapper learns which way a room was left from what the player sent; the login line never reaches it.</summary>
    [Test]
    public async Task CommandSentCarriesWhatThePlayerSentAndNotTheLogin()
    {
        var telnet = new FakeTelnetSession();
        var character = new CharacterDefinition { Name = "Ann", Password = "swordfish", OnConnect = "look" };
        var session = new WorldSession(new WorldDefinition { Name = "T", Host = "h", Port = 1 }, character, sessionFactory: _ => telnet);
        var sent = new List<string>();
        session.CommandSent += (_, command) => sent.Add(command);
        await session.ConnectAsync();

        await session.SendUserInputAsync("n");

        await Assert.That(sent).IsEquivalentTo(new[] { "n" });
        await Assert.That(telnet.SentLines).Contains("connect Ann swordfish");
    }
}
