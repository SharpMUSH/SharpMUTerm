using System.Text.Json;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Core.Tests.Configuration;

/// <summary>The zoom's levels and the text sizes the configuration keeps.</summary>
public class DisplaySettingsTests
{
    [Test]
    public async Task TheZoomStepsThroughTheBrowsersLevelsAndStopsAtTheEnds()
    {
        await Assert.That(ZoomLevels.Next(100)).IsEqualTo(110);
        await Assert.That(ZoomLevels.Previous(100)).IsEqualTo(90);
        await Assert.That(ZoomLevels.Next(ZoomLevels.Maximum)).IsEqualTo(ZoomLevels.Maximum);
        await Assert.That(ZoomLevels.Previous(ZoomLevels.Minimum)).IsEqualTo(ZoomLevels.Minimum);
        await Assert.That(ZoomLevels.Stepped(175, 0)).IsEqualTo(100);
    }

    [Test]
    public async Task ALevelBetweenStepsStepsToTheNextOneAcross()
    {
        await Assert.That(ZoomLevels.Next(117)).IsEqualTo(125);
        await Assert.That(ZoomLevels.Previous(117)).IsEqualTo(110);
    }

    [Test]
    public async Task AHandEditedFileIsHeldToTheRanges()
    {
        var held = new DisplaySettings { Zoom = 5000, OutputPoints = 1, ComposerText = 10, PanelText = 999 }.Clamped();

        await Assert.That(held.Zoom).IsEqualTo(ZoomLevels.Maximum);
        await Assert.That(held.OutputPoints).IsEqualTo(DisplaySettings.MinOutputPoints);
        await Assert.That(held.ComposerText).IsEqualTo(ZoomLevels.AreaSteps[0]);
        await Assert.That(held.PanelText).IsEqualTo(ZoomLevels.AreaSteps[^1]);
    }

    [Test]
    public async Task APercentReadsWithOrWithoutItsSign()
    {
        await Assert.That(ZoomLevels.Parse("125%")).IsEqualTo(125);
        await Assert.That(ZoomLevels.Parse(" 90 ")).IsEqualTo(90);
        await Assert.That(ZoomLevels.Parse("big")).IsNull();
        await Assert.That(ZoomLevels.Label(125)).IsEqualTo("125%");
    }

    [Test]
    public async Task AFileFromBeforeTheSettingReadsAsEverythingAtItsDesignedSize()
    {
        var config = JsonSerializer.Deserialize<AppConfiguration>("{}", ConfigurationStore.SerializerOptions)!;

        await Assert.That(config.Display.Zoom).IsEqualTo(100);
        await Assert.That(config.Display.OutputPoints).IsEqualTo(DisplaySettings.DefaultOutputPoints);
        await Assert.That(config.Display.DialogText).IsEqualTo(100);
    }
}
