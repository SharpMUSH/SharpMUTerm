using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Tests.Text;

/// <summary>
/// The colour-vision simulation the pane tints are measured with. What is pinned is that it is the model it
/// says it is: the confusions each deficiency is known for collapse, the ones it is not known for do not,
/// and the distance is a distance.
/// </summary>
public class ColourVisionTests
{
    private static readonly Rgb Red = new(0xd0, 0x30, 0x30);
    private static readonly Rgb Green = new(0x50, 0x90, 0x20);
    private static readonly Rgb Blue = new(0x20, 0x40, 0xd0);

    [Test]
    public async Task BlackAndWhiteAreAHundredApart()
    {
        await Assert.That(ColourVision.Distance(new Rgb(0, 0, 0), new Rgb(0xff, 0xff, 0xff))).IsEqualTo(100.0).Within(0.01);
    }

    [Test]
    [Arguments(ColourDeficiency.None)]
    [Arguments(ColourDeficiency.Protanopia)]
    [Arguments(ColourDeficiency.Deuteranopia)]
    [Arguments(ColourDeficiency.Tritanopia)]
    public async Task ADistanceIsZeroToItselfAndTheSameBothWays(ColourDeficiency deficiency)
    {
        await Assert.That(ColourVision.Distance(Red, Red, deficiency)).IsEqualTo(0.0);
        await Assert.That(ColourVision.Distance(Red, Blue, deficiency))
            .IsEqualTo(ColourVision.Distance(Blue, Red, deficiency)).Within(1e-9);
    }

    /// <summary>
    /// Greys are untouched by every matrix — each row of Machado's matrices sums to one — so the simulation
    /// moves hue and never lightness of a neutral.
    /// </summary>
    [Test]
    [Arguments(ColourDeficiency.Protanopia)]
    [Arguments(ColourDeficiency.Deuteranopia)]
    [Arguments(ColourDeficiency.Tritanopia)]
    public async Task AGreyLooksTheSameToEveryone(ColourDeficiency deficiency)
    {
        var grey = new Rgb(0x77, 0x77, 0x77);
        await Assert.That(ColourVision.Distance(grey, ColourVision.Simulate(grey, deficiency))).IsLessThan(0.5);
    }

    /// <summary>Red against green is the confusion protanopia and deuteranopia are named for.</summary>
    [Test]
    [Arguments(ColourDeficiency.Protanopia)]
    [Arguments(ColourDeficiency.Deuteranopia)]
    public async Task RedAndGreenCloseUpForTheRedGreenDeficiencies(ColourDeficiency deficiency)
    {
        var typical = ColourVision.Distance(Red, Green);
        await Assert.That(ColourVision.Distance(Red, Green, deficiency)).IsLessThan(typical / 2);
    }

    /// <summary>And tritanopia keeps it — its confusion is blue against green, not red against green.</summary>
    [Test]
    public async Task TritanopiaKeepsRedAndGreenApartAndClosesBlueOnGreen()
    {
        await Assert.That(ColourVision.Distance(Red, Green, ColourDeficiency.Tritanopia))
            .IsGreaterThan(ColourVision.Distance(Red, Green) / 2);

        var cyan = new Rgb(0x20, 0x90, 0xa0);
        var green = new Rgb(0x30, 0xa0, 0x40);
        await Assert.That(ColourVision.Distance(cyan, green, ColourDeficiency.Tritanopia))
            .IsLessThan(ColourVision.Distance(cyan, green) / 2);
    }
}
