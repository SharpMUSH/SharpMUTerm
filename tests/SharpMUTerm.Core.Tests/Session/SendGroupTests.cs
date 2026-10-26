using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Core.Tests.Session;

/// <summary>Who a group line and a script's <c>world.sendTo</c> reach.</summary>
public class SendGroupTests
{
    private static WorldSession Session(string world, string character) =>
        new(new WorldDefinition { Name = world, Host = "h", Port = 1 }, new CharacterDefinition { Name = character },
            sessionFactory: _ => new FakeTelnetSession());

    [Test]
    public async Task EachConnectedMemberOnceInGroupOrderAndTheRestNamed()
    {
        var ann = Session("Mush", "Ann");
        var bob = Session("Mush", "Bob");
        var cy = Session("Mush", "Cy");
        await ann.ConnectAsync();
        await bob.ConnectAsync();
        var group = new SendGroup();
        group.Toggle("Mush.Bob");
        group.Toggle("Mush.Cy");
        group.Toggle("Mush.Ann");
        group.Toggle("Mush.Gone");

        var (reachable, missed) = group.Resolve([ann, bob, cy, bob]);

        await Assert.That(reachable).IsEquivalentTo(new[] { bob, ann }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(missed).IsEquivalentTo(new[] { "Cy", "Mush.Gone" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(group.Toggle("Mush.Bob")).IsFalse();
        await Assert.That(group.Keys).DoesNotContain("Mush.Bob");
    }

    [Test]
    public async Task AScriptNamesACharacterByKeyOrByANameOnlyOneOfThemHas()
    {
        var ann = Session("Mush", "Ann");
        var ann2 = Session("Moo", "Ann");
        var bob = Session("Mush", "Bob");
        await ann.ConnectAsync();
        await ann2.ConnectAsync();
        WorldSession[] open = [ann, ann2, bob];

        await Assert.That(SendGroup.Find(open, "moo.ann").Session).IsSameReferenceAs(ann2);
        await Assert.That(SendGroup.Find(open, "Ann").Refusal).Contains("2 open characters");
        await Assert.That(SendGroup.Find(open, "Bob").Refusal).Contains("not connected");
        await Assert.That(SendGroup.Find(open, "Zed").Refusal).Contains("no open character");
    }
}
