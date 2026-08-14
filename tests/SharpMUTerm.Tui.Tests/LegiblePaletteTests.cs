using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The floor, asserted over the whole cross product it has to hold on: every colour this client can
/// paint × every theme × every plane a pane can wear.
/// <para>
/// <b>This is the test that would have caught the reported defect</b>, and the reason it is a table
/// rather than a case per bug is that the bugs were never individually interesting. The freeze bar's
/// purple was one cell of a grid in which six of sixteen picker names fail on the dark theme, nine of
/// sixteen fail on the light one, and only <c>grey</c> clears 3:1 on both — a palette of fixed hexes
/// cannot serve two themes, and nothing short of a table says so.
/// </para>
/// </summary>
public class LegiblePaletteTests
{
    private static IEnumerable<Theme> Themes() =>
        ThemeLibrary.Names.Select(ThemeLibrary.Get);

    /// <summary>
    /// Every plane a pane can be painted on: untinted and all six character tints, focused and not.
    /// Rebuilt here from the public surface rather than reached into, so this test measures what the
    /// workspace actually paints.
    /// </summary>
    private static IEnumerable<(string Name, Rgb Plane)> Planes(Theme theme)
    {
        foreach (var tint in Enum.GetValues<PaneTint>())
        {
            var plane = WorkspacePalette.Tint(theme, tint);
            yield return ($"{tint}", plane);
            yield return ($"{tint}+focus", WorkspacePalette.Focus(plane));
        }

        yield return ("backdrop", WorkspacePalette.Backdrop(theme));
    }

    [Test]
    public async Task EveryPickerColourIsLegibleOnEveryPlaneOfEveryTheme()
    {
        var failures = new List<string>();

        foreach (var theme in Themes())
        {
            var plane = WorkspacePalette.ReadingPlane(theme);
            foreach (var name in ScreenColours.Palette.Where(n => n != ScreenColours.None))
            {
                if (!WebColors.TryParse(name, out var colour))
                {
                    failures.Add($"{name} does not resolve");
                    continue;
                }

                var painted = Contrast.Legible(new Rgb(colour.R, colour.G, colour.B), plane);

                // Panes only. A highlight colours a line of a world's output and a world's output is
                // never drawn on the backdrop, so measuring it there would hold the picker to a plane it
                // cannot land on — and on the Light theme the backdrop is darker than every pane, so it
                // is the one that would fail.
                foreach (var (planeName, actual) in Planes(theme).Where(p => p.Name != "backdrop"))
                {
                    var ratio = Contrast.Ratio(painted, actual);
                    if (ratio < Contrast.Floor)
                    {
                        failures.Add($"{theme.Name}/{name} on {planeName}: {ratio:0.00}");
                    }
                }
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task EveryBasePaletteIndexIsLegibleOnEveryPlaneOfEveryTheme()
    {
        // 0–15: what a MU* server actually sends. The six-cube and the greyscale ramp go through the
        // same lift and are not enumerated — 256 × 3 × 15 assertions would buy one more decimal place
        // of the same fact, and these sixteen are the ones a game reaches for.
        var failures = new List<string>();

        foreach (var theme in Themes())
        {
            var reading = WorkspacePalette.ReadingPlane(theme);
            for (var index = 0; index < 16; index++)
            {
                var painted = Contrast.Legible(theme.ResolveIndex(index), reading);
                foreach (var (planeName, plane) in Planes(theme).Where(p => p.Name != "backdrop"))
                {
                    var ratio = Contrast.Ratio(painted, plane);
                    if (ratio < Contrast.Floor)
                    {
                        failures.Add($"{theme.Name}/idx:{index} on {planeName}: {ratio:0.00}");
                    }
                }
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task EveryChromeInkIsLegibleWhereverTheChromePutsIt()
    {
        // Including the backdrop, which is what separates ChromePlane from ReadingPlane: these four are
        // painted on the status line and the rail as well as into panes, and one ink has to be readable
        // in both places or the client needs two teals.
        var failures = new List<string>();

        foreach (var theme in Themes())
        {
            var ink = WorkspacePalette.Chrome(theme);
            foreach (var (label, hex) in new[]
                     {
                         ("accent", ink.Accent), ("notice", ink.Notice),
                         ("draft", ink.Draft), ("marker", ink.Marker),
                     })
            {
                var colour = ParseHex(hex);
                foreach (var (planeName, plane) in Planes(theme))
                {
                    var ratio = Contrast.Ratio(colour, plane);
                    if (ratio < Contrast.Floor)
                    {
                        failures.Add($"{theme.Name}/{label} on {planeName}: {ratio:0.00}");
                    }
                }
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task TheReportedFreezeBarColourFailedThisTestBeforeItWasFixed()
    {
        // The defect as it was reported, kept as its own case so the number stays in the repository: the
        // bar took its accent straight from the theme's index 5, and on the default dark theme's focused
        // pane that is #800080 on #36363d.
        var theme = ThemeLibrary.Dark();
        var focusedPane = WorkspacePalette.Focus(WorkspacePalette.Surface(theme));

        await Assert.That(Contrast.Ratio(theme.ResolveIndex(5), focusedPane)).IsLessThan(1.3);
        await Assert.That(Contrast.Ratio(ParseHex(WorkspacePalette.Chrome(theme).Marker), focusedPane))
            .IsGreaterThanOrEqualTo(Contrast.Floor);
    }

    [Test]
    public async Task TheLightThemesChromeWasUnreadableToo()
    {
        // Not a rider on the freeze fix but the same defect on the other side: every one of the client's
        // own hexes was picked against a dark theme, and no snapshot showed it because every frame in
        // the gallery renders Dark.
        var theme = ThemeLibrary.Get("Light");
        var plane = WorkspacePalette.ChromePlane(theme);

        await Assert.That(Contrast.Ratio(ChromeInk.BaseAccent, plane)).IsLessThan(Contrast.Floor);
        await Assert.That(Contrast.Ratio(ChromeInk.BaseDraft, plane)).IsLessThan(Contrast.Floor);
        await Assert.That(Contrast.Ratio(ChromeInk.BaseNotice, plane)).IsLessThan(Contrast.Floor);

        var ink = WorkspacePalette.Chrome(theme);
        await Assert.That(Contrast.Ratio(ParseHex(ink.Accent), plane)).IsGreaterThanOrEqualTo(Contrast.Floor);
        await Assert.That(Contrast.Ratio(ParseHex(ink.Draft), plane)).IsGreaterThanOrEqualTo(Contrast.Floor);
        await Assert.That(Contrast.Ratio(ParseHex(ink.Notice), plane)).IsGreaterThanOrEqualTo(Contrast.Floor);
    }

    [Test]
    public async Task TheReadingPlaneIsTheWorstOfTheBandAndNotAMemberPickedByName()
    {
        // The whole argument for one plane per theme is that clearing *it* clears all fourteen. If the
        // extreme were mis-chosen the table tests above would still pass on most cells and fail on the
        // one that matters, so the property is asserted directly.
        foreach (var theme in Themes())
        {
            var reading = WorkspacePalette.ReadingPlane(theme);
            var luminance = Contrast.RelativeLuminance(reading);
            var band = Planes(theme).Where(p => p.Name != "backdrop")
                .Select(p => Contrast.RelativeLuminance(p.Plane)).ToList();

            // Dark theme: the brightest plane. Light theme: the darkest. Either way, an extreme.
            await Assert.That(luminance == band.Max() || luminance == band.Min()).IsTrue();
        }
    }

    /// <summary>
    /// A selection has to be seen against the pane it is drawn in, and every pane is a different plane.
    /// The band is one pair per theme rather than one per pane for <see cref="WorkspacePalette.ReadingPlane"/>'s
    /// reason, so it has to stand clear of the whole band, not of the plane it happened to be derived from.
    /// <para>
    /// The floor here is a <em>fill against a fill</em> and is deliberately not <see cref="Contrast.Floor"/>:
    /// three to one is where text stops being invisible, and two backgrounds that differed that hard would
    /// make a selected line shout. What is held to the text floor is the ink on it, below.
    /// </para>
    /// </summary>
    [Test]
    public async Task TheSelectionBandStandsClearOfEveryPlaneAPaneCanWear()
    {
        var failures = new List<string>();

        foreach (var theme in Themes())
        {
            var band = WorkspacePalette.SelectionBand(theme);
            foreach (var (name, plane) in Planes(theme))
            {
                var ratio = Contrast.Ratio(band, plane);
                if (ratio < SelectionSeparation)
                {
                    failures.Add($"{theme.Name}/{name}: {ratio:0.00}:1");
                }
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    /// <summary>
    /// The ink painted <em>on</em> that band, held to the text floor — the rule the whole file exists for,
    /// applied to the one plane the client invents rather than inherits. It matters more here than
    /// elsewhere: the highlight replaces the game's own foreground on every selected cell, so this single
    /// colour is what all selected output is read in.
    /// </summary>
    [Test]
    public async Task TheSelectionInkIsLegibleOnItsOwnBand()
    {
        foreach (var theme in Themes())
        {
            var band = WorkspacePalette.SelectionBand(theme);
            await Assert.That(Contrast.Ratio(WorkspacePalette.SelectionInk(theme), band))
                .IsGreaterThanOrEqualTo(Contrast.Floor)
                .Because($"{theme.Name}'s selection ink must be readable on its own band");
        }
    }

    /// <summary>
    /// How far a selection's fill must sit from a pane's own fill to be seen as a band. Below the text
    /// floor deliberately (see <see cref="TheSelectionBandStandsClearOfEveryPlaneAPaneCanWear"/>) and far
    /// enough above 1:1 that it cannot be a rounding artefact. The band as built clears it with room —
    /// the tightest cell measured across the three themes is 2.70:1, on Dark's focused untinted pane —
    /// so this is the property being asserted rather than the number that happens to hold today.
    /// </summary>
    private const double SelectionSeparation = 1.5;

    private static Rgb ParseHex(string hex) => new(
        Convert.ToByte(hex.Substring(1, 2), 16),
        Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16));
}
