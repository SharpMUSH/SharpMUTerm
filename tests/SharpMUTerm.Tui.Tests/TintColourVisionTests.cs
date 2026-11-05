using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <b>The ten pane tints stay apart for colour-blind readers.</b> Every pair of tinted planes — and every
/// tinted plane against the untinted one, since "no colour" is a choice a neighbour can make — is held to a
/// CIEDE2000 floor under simulated protanopia, deuteranopia and tritanopia (Machado 2009, through
/// <see cref="ColourVision"/>), on all three themes.
/// <para>
/// <b>The floor is 1.9, and what it buys is stated rather than flattered.</b> A ΔE00 of about 1 is a
/// just-noticeable difference for two flat patches side by side; about 2 is where it is seen at a glance.
/// Panes are exactly that — flat planes side by side — so 1.9 says two characters' panes visibly differ
/// to a dichromat. It does not say a lone pane is recognised by its colour: the planes sit at one
/// luminance (an invariant: no character's pane is brighter than another's) near L* 7 on the dark themes,
/// where the whole gamut is a few units wide, and a dichromat sees hue on one axis only. The best set found
/// with the names ignored reached about 2.2. So the floor is close to the ceiling, and the name is said
/// where it costs no cell (the rail and tab tooltips).
/// </para>
/// <para>
/// Typical vision is held to 3.5 — the anchors were re-picked for colour-blind readers on condition that
/// nobody else's closest pair got closer (it was 3.71 on Dark, and is now 3.96).
/// </para>
/// </summary>
public class TintColourVisionTests
{
    private const double DichromatFloor = 1.9;

    private const double TypicalFloor = 3.5;

    public static IEnumerable<(string Theme, ColourDeficiency Deficiency)> Cases() =>
        from theme in ThemeLibrary.Names
        from deficiency in Enum.GetValues<ColourDeficiency>()
        select (theme, deficiency);

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task EveryPairOfPanePlanesStaysApart((string Theme, ColourDeficiency Deficiency) test)
    {
        var theme = ThemeLibrary.Get(test.Theme);
        var floor = test.Deficiency == ColourDeficiency.None ? TypicalFloor : DichromatFloor;

        // PaneTint.None is in the enumeration, so the untinted surface is one of the planes compared.
        var planes = Enum.GetValues<PaneTint>().Select(t => (Tint: t, Plane: WorkspacePalette.Tint(theme, t))).ToList();

        var worst = (Distance: double.MaxValue, A: PaneTint.None, B: PaneTint.None);
        for (var i = 0; i < planes.Count; i++)
        {
            for (var j = i + 1; j < planes.Count; j++)
            {
                var d = ColourVision.Distance(planes[i].Plane, planes[j].Plane, test.Deficiency);
                if (d < worst.Distance)
                {
                    worst = (d, planes[i].Tint, planes[j].Tint);
                }
            }
        }

        await Assert.That(worst.Distance).IsGreaterThanOrEqualTo(floor)
            .Because($"{worst.A} vs {worst.B} on {test.Theme} under {test.Deficiency}: {worst.Distance:0.00}");
    }
}
