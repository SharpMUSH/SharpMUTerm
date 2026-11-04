using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Import;

namespace SharpMUTerm.Client.Tests;

/// <summary>
/// The import review (<see cref="ImportReviewModel"/>): what it says about an import before anything is written, built
/// from the plan both hosts draw.
/// </summary>
public class ImportReviewModelTests
{
    private const string World = """
        <muclient>
        <world name="Northwind" site="northwind.example.org" port="4201" player="Wren" password="pw" />
        <triggers>
          <trigger enabled="y" match="* pages: *"><send>page %1=away</send></trigger>
          <trigger enabled="y" match="*has arrived*" omit_from_output="y"></trigger>
          <trigger enabled="y" match="x *" send_to="12"><send>f()</send></trigger>
        </triggers>
        <aliases><alias match="gg *" enabled="y"><send>get %1</send></alias></aliases>
        </muclient>
        """;

    private static ImportPlan Plan(AppConfiguration? config = null) =>
        ImportPlan.For(config ?? new AppConfiguration(), MushclientImport.Parse(World, "Northwind.mcl").Result!);

    [Test]
    public async Task TheReviewLeadsWithWhatCanActAsThePlayer_AndSaysWhatItAddsAndLeavesOut()
    {
        var model = ImportReviewModel.From(Plan());

        await Assert.That(model.Title).IsEqualTo("Import from MUSHclient");
        await Assert.That(model.Intro).Contains("Northwind.mcl");
        await Assert.That(model.Intro).Contains("Nothing is written until you press Enter");
        await Assert.That(model.ActsAsYou.ToArray()).IsEquivalentTo(new[]
        {
            "1 trigger sends a command when a line matches",
            "1 alias turns what you type into other commands",
        });
        await Assert.That(model.Adds.Select(r => r.Kind + ":" + r.Name).ToArray()).IsEquivalentTo(new[] { "world:Northwind", "set:Imported from MUSHclient" });
        await Assert.That(model.Adds[0].Detail).IsEqualTo("new world, northwind.example.org:4201, character Wren");
        await Assert.That(model.Adds[1].Detail).IsEqualTo("2 triggers, 1 alias · new set, listed on Wren");
        await Assert.That(model.LeftOutCount).IsEqualTo(1);
        await Assert.That(model.LeftOut.Single().Detail).Contains("script");
    }

    [Test]
    public async Task PasswordsAreNamedAndNotStoredUntilSwitchedOn()
    {
        var plan = Plan();
        var off = ImportReviewModel.From(plan);
        var on = ImportReviewModel.From(plan, storePasswords: true);

        await Assert.That(off.Passwords).IsEqualTo(1);
        await Assert.That(off.StorePasswords).IsFalse();
        await Assert.That(off.PasswordLine).Contains("will not be stored");
        await Assert.That(on.PasswordLine).Contains("will be stored");
        await Assert.That(off.Keys).Contains("p passwords");
        await Assert.That(off).IsNotEqualTo(on);
        await Assert.That(ImportReviewModel.From(plan)).IsEqualTo(off).Because("two reviews of one plan are the same review");
    }

    [Test]
    public async Task AKnownWorldSaysItOnlyGainsCharacters()
    {
        var config = new AppConfiguration
        {
            Worlds = { new WorldDefinition { Name = "Mine", Host = "northwind.example.org", Port = 4201, Characters = { new CharacterDefinition { Name = "Wren" } } } },
        };
        var model = ImportReviewModel.From(Plan(config));

        await Assert.That(model.Adds[0].Detail).Contains("already configured");
        await Assert.That(model.Adds[0].Detail).Contains("keeps your Wren as is");
        await Assert.That(model.Adds[1].Detail).Contains("listed on no character yet");
        await Assert.That(model.Passwords).IsEqualTo(0).Because("a password is only ever stored for a character the import adds");
        await Assert.That(model.Keys).DoesNotContain("passwords");
    }

    [Test]
    public async Task TheSessionPassesGesturesOnUntilItEnds()
    {
        var accepted = 0;
        var toggled = 0;
        var closed = 0;
        var session = new ImportReviewSession(ImportReviewModel.From(Plan()), true, () => accepted++, () => toggled++, () => closed++);

        session.TogglePasswords();
        session.Accept();
        session.Accept();
        session.TogglePasswords();

        await Assert.That((accepted, toggled, closed)).IsEqualTo((1, 1, 1));
        await Assert.That(session.Surface).IsEqualTo(Surfaces.SurfaceCatalog.Import);
    }
}
