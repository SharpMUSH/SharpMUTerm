using SharpMUTerm.Client.Panels;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The map's area colours (<see cref="MapAreaPalette"/>) against the planes they are drawn on: every hex of the set a
/// theme takes clears the legibility floor on the worst pane plane of that theme, so the terminal's
/// <see cref="Contrast.Legible"/> leaves them exactly as the model says and the window draws the same hex.
/// </summary>
public class MapAreaPaletteTests
{
    [Test]
    public async Task EveryAreaColourClearsTheFloorOnEveryBuiltInTheme()
    {
        var failures = new List<string>();
        foreach (var name in ThemeLibrary.Names)
        {
            var theme = ThemeLibrary.Get(name);
            var palette = MapAreaPalette.For(WorkspacePalette.DarkPanes(theme));
            var plane = WorkspacePalette.ReadingPlane(theme);
            foreach (var hex in palette.Colours)
            {
                var colour = Parse(hex);
                var ratio = Contrast.Ratio(colour, plane);
                if (ratio < Contrast.Floor)
                {
                    failures.Add($"{name}: {hex} is {ratio:F2}:1 on {plane}");
                }

                if (Contrast.Legible(colour, plane) != colour)
                {
                    failures.Add($"{name}: {hex} is moved by the floor");
                }
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task TheDarkAndLightThemesTakeDifferentSetsOfTheSameHues()
    {
        await Assert.That(WorkspacePalette.DarkPanes(ThemeLibrary.Get("Dark"))).IsTrue();
        await Assert.That(WorkspacePalette.DarkPanes(ThemeLibrary.Get("Solarized Dark"))).IsTrue();
        await Assert.That(WorkspacePalette.DarkPanes(ThemeLibrary.Get("Light"))).IsFalse();
        await Assert.That(MapAreaPalette.Dark.Colours.Count).IsEqualTo(MapAreaPalette.Light.Colours.Count);
    }

    [Test]
    public async Task AnAreasColourIsStableForItsName()
    {
        await Assert.That(MapAreaPalette.Dark.Colour("Aetherfall")).IsEqualTo(MapAreaPalette.Dark.Colour("Aetherfall"));
        await Assert.That(MapAreaPalette.IndexOf("Aetherfall")).IsEqualTo(MapAreaPalette.IndexOf("Aetherfall"));

        // Pinned, so a change to the hash (which would recolour every player's areas) is a decision, not an accident.
        await Assert.That(MapAreaPalette.IndexOf("Aetherfall")).IsEqualTo(Fnv("Aetherfall"));
        var spread = Enumerable.Range(0, 64).Select(i => MapAreaPalette.IndexOf($"Area {i}")).Distinct().Count();
        await Assert.That(spread).IsEqualTo(8);
    }

    private static int Fnv(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return (int)(hash % 8u);
    }

    private static Rgb Parse(string hex) => new(
        Convert.ToByte(hex.Substring(1, 2), 16),
        Convert.ToByte(hex.Substring(3, 2), 16),
        Convert.ToByte(hex.Substring(5, 2), 16));
}
