using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Media;
using SharpMUTerm.Core.Session;

namespace SharpMUTerm.Core.Tests.Media;

public class MediaAdvertTests
{
    [Test]
    public async Task ClientMediaIsAskedForOnlyWhenTheClientPlaysIt()
    {
        var world = new WorldDefinition { GmcpModules = { "IRE.Rift 1" } };

        await Assert.That(WorldSession.GmcpModulesFor(world, playsMedia: true))
            .IsEquivalentTo(new[] { "IRE.Rift 1", MediaCommand.GmcpModule });
        await Assert.That(WorldSession.GmcpModulesFor(world, playsMedia: false))
            .IsEquivalentTo(new[] { "IRE.Rift 1" });
        await Assert.That(world.GmcpModules.Count).IsEqualTo(1);
    }
}
