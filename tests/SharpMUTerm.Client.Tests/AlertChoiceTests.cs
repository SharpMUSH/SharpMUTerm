using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Client.Tests;

/// <summary>F5's <c>alert on</c> row: one choice over the page and name fields, and a new world reads <c>pages</c>.</summary>
public class AlertChoiceTests
{
    [Test]
    public async Task ANewWorldReadsPages()
    {
        await Assert.That(WorldFieldValues.AlertName(new WorldDefinition())).IsEqualTo("pages");
    }

    [Test]
    [Arguments("off", false, false)]
    [Arguments("pages", true, false)]
    [Arguments("name & pages", true, true)]
    public async Task EachChoiceSetsBothFieldsAndReadsBack(string choice, bool page, bool name)
    {
        var world = new WorldDefinition { AlertOnName = !name, AlertOnPage = !page };

        WorldFieldValues.SetAlert(world, choice);

        await Assert.That(world.AlertOnPage).IsEqualTo(page);
        await Assert.That(world.AlertOnName).IsEqualTo(name);
        await Assert.That(WorldFieldValues.AlertName(world)).IsEqualTo(choice);
    }

    [Test]
    public async Task TheDeclaredFieldResetsToPages()
    {
        var config = new AppConfiguration();
        var world = new WorldDefinition { Name = "W", Host = "h", Port = 1, AlertOnPage = false };
        config.Worlds.Add(world);
        var field = BuiltInSettings.World.Fields.Single(f => f.Id == "alert-on");

        await Assert.That(WorldFieldValues.AlertName(world)).IsEqualTo("off");
        ((SettingBinding<string>)field.Binding!).Reset(new SettingTarget(config, world));
        await Assert.That(WorldFieldValues.AlertName(world)).IsEqualTo("pages");
    }
}
