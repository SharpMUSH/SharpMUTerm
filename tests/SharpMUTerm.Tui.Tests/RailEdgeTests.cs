using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Why the cell layout keeps a rule between the rail and the panes but no spacer beside it (wave 24). The Terminal-Main
/// mockup draws the rail straight against the output, the edge carried by the planes alone (#121216 against #1e1e24,
/// about 6 L*). Here the rail is the backdrop, and the planes beside it are not that far apart: an unfocused pane sits
/// one backdrop step above it and a tinted one sits lower still (<c>TintDepth</c>), so on every theme some pane meets
/// the rail at a step the eye cannot find on its own. The rule is what carries the edge there, and it is well clear of
/// the backdrop. The rail-coloured spacer that used to follow the rule separated nothing the rule had not, so that
/// column went to the panes (<c>RailWindowRowTests.TheRailSpendsAtMostOneBlankColumnPastItsWidestRow</c>).
/// </summary>
public class RailEdgeTests
{
    /// <summary>A step the eye finds on a terminal's worth of flat colour (<c>WorkspacePaletteTests.Visible</c>'s figure, in L*).</summary>
    private const double Visible = 8;

    private static double Lightness(SharpMUTerm.Core.Text.Rgb rgb)
    {
        var y = SharpMUTerm.Core.Text.Contrast.RelativeLuminance(rgb);
        return y > 0.008856 ? (116 * Math.Cbrt(y)) - 16 : 903.3 * y;
    }

    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task SomePaneMeetsTheRailAtAStepOnlyTheRuleCarries(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var rail = Lightness(WorkspacePalette.Backdrop(theme));
        var planes = Enum.GetValues<PaneTint>()
            .Select(tint => tint == PaneTint.None ? WorkspacePalette.Surface(theme) : WorkspacePalette.Tint(theme, tint))
            .ToList();
        var nearest = planes.Min(plane => Math.Abs(Lightness(plane) - rail));
        var rule = Math.Abs(Lightness(WorkspacePalette.Rule(theme)) - rail);

        await Assert.That(nearest).IsLessThan(Visible).Because("the planes alone would not show where the rail ends");
        await Assert.That(rule).IsGreaterThan(nearest).Because("the rule carries the edge the planes do not");
    }
}
