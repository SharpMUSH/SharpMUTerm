using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Core.Commands;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Tests;

/// <summary>One name for a character on every surface, and never the dotted session key.</summary>
public class CharacterNamesTests
{
    [Test]
    public async Task ACharacterIsNamedCharacterThenWorld()
    {
        await Assert.That(CharacterNames.Display("Aetherfall", "Corvid")).IsEqualTo("Corvid · Aetherfall");
        await Assert.That(CharacterNames.Display("Aetherfall", null)).IsEqualTo("Aetherfall");
    }

    [Test]
    public async Task ASessionKeyIsResolvedAgainstTheConfigurationSoADottedWorldStillSplits()
    {
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "mud.example.org", Host = "mud.example.org", Port = 23 };
        world.Characters.Add(new CharacterDefinition { Name = "Ann" });
        config.Worlds.Add(world);

        await Assert.That(CharacterNames.ForKey("mud.example.org.Ann", config)).IsEqualTo("Ann · mud.example.org");
        await Assert.That(CharacterNames.ForKey("mud.example.org", config)).IsEqualTo("mud.example.org");
    }

    [Test]
    public async Task AKeyNothingNamesIsSplitWhereAKeyIsJoined()
    {
        await Assert.That(CharacterNames.ForKey("Quiet.Cal", (IEnumerable<CharacterRef>?)null)).IsEqualTo("Cal · Quiet");
        await Assert.That(CharacterNames.ForKey("Quiet", (IEnumerable<CharacterRef>?)null)).IsEqualTo("Quiet");
    }
}
