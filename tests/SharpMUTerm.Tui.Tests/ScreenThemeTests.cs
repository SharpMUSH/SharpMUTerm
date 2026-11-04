using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The settings screens follow the theme (defect D6). <see cref="ScreenPalette"/>'s constants are the Dark
/// theme's; <see cref="ScreenTheme"/> works the same roles out for the active theme, and the views resolve
/// their renderers' markup through it. Pinned here: Dark is untouched, every theme's inks clear the floor
/// on every plane they are drawn on, the resolver rewrites only colours, and a Light frame is light.
/// <see cref="FrameContrastTests"/> holds the painted pairs of every settings view to the floor on all three.
/// </summary>
[NotInParallel]
public class ScreenThemeTests
{
    private static readonly string[] Roles =
    [
        ScreenPalette.Accent, ScreenPalette.PanelBg, ScreenPalette.PanelFg, ScreenPalette.HeaderBg,
        ScreenPalette.FooterBg, ScreenPalette.EditBg, ScreenPalette.Label, ScreenPalette.Value, ScreenPalette.Muted,
        ScreenPalette.Rule, ScreenPalette.CursorBg, ScreenPalette.Ink, ScreenPalette.MenuBg,
        ScreenPalette.MenuSelectedBg, ScreenPalette.MenuShadow, ScreenPalette.FieldBg, ScreenPalette.Warn,
    ];

    private static readonly string[] TextPlanes =
    [
        ScreenPalette.PanelBg, ScreenPalette.HeaderBg, ScreenPalette.FooterBg, ScreenPalette.EditBg,
        ScreenPalette.CursorBg, ScreenPalette.MenuBg, ScreenPalette.MenuSelectedBg, ScreenPalette.FieldBg,
    ];

    private static readonly string[] Planes = [.. TextPlanes, ScreenPalette.Rule, ScreenPalette.MenuShadow];

    private static Rgb Rgb(string hex) => new(
        Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));

    /// <summary>Every Dark frame is what it was: the Dark theme resolves each role to its own constant.</summary>
    [Test]
    public async Task TheDarkThemeIsTheConstantsByteForByte()
    {
        var scheme = ScreenTheme.For(ThemeLibrary.Get("Dark"));

        foreach (var role in Roles)
        {
            await Assert.That(scheme.Role(role)).IsEqualTo(role);
        }

        await Assert.That(scheme.IsIdentity).IsTrue();
    }

    /// <summary>
    /// The text inks — label, muted readout, value, the panel's own text, the accent and the warning — clear
    /// <see cref="Contrast.Floor"/> on every plane text is drawn on, on every theme; and the ink printed on the
    /// accent clears it on the accent.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task EveryInkClearsTheFloorOnEveryPlane(string themeName)
    {
        var scheme = ScreenTheme.For(ThemeLibrary.Get(themeName));
        string[] inks = [ScreenPalette.Label, ScreenPalette.Muted, ScreenPalette.Value, ScreenPalette.PanelFg, ScreenPalette.Warn];

        foreach (var ink in inks)
        {
            foreach (var plane in ScreenTheme.PlanesUnder(ink))
            {
                await Assert.That(Contrast.Ratio(Rgb(scheme.Role(ink)), Rgb(scheme.Role(plane))))
                    .IsGreaterThanOrEqualTo(Contrast.Floor)
                    .Because($"{ink} on {plane} on {themeName}");
            }
        }

        // The accent is text too — hints, checkmarks, the selected candidate's marker — on any of them.
        foreach (var plane in TextPlanes)
        {
            await Assert.That(Contrast.Ratio(Rgb(scheme.Accent), Rgb(scheme.Role(plane))))
                .IsGreaterThanOrEqualTo(Contrast.Floor).Because($"accent on {plane} on {themeName}");
        }

        await Assert.That(Contrast.Ratio(Rgb(scheme.Ink), Rgb(scheme.Accent)))
            .IsGreaterThanOrEqualTo(Contrast.Floor).Because($"ink on the accent on {themeName}");
    }

    /// <summary>
    /// A light theme's screens are light: every plane is on the light side of mid-grey, and the text that
    /// was light on the dark screens is dark. And the relationships survive — the label is quieter than the
    /// value, and the menu's shadow is darker than every plane it falls on.
    /// </summary>
    [Test]
    public async Task ALightThemeGetsLightScreensWithTheSameRelationships()
    {
        var scheme = ScreenTheme.For(ThemeLibrary.Get("Light"));

        foreach (var plane in TextPlanes)
        {
            await Assert.That(Contrast.RelativeLuminance(Rgb(scheme.Role(plane)))).IsGreaterThan(0.18).Because(plane);
        }

        await Assert.That(Contrast.RelativeLuminance(Rgb(scheme.Value))).IsLessThan(0.05);
        await Assert.That(Contrast.Ratio(Rgb(scheme.Value), Rgb(scheme.PanelBg)))
            .IsGreaterThan(Contrast.Ratio(Rgb(scheme.Label), Rgb(scheme.PanelBg)));

        var shadow = Contrast.RelativeLuminance(Rgb(scheme.MenuShadow));
        foreach (var plane in Planes.Where(p => p != ScreenPalette.MenuShadow))
        {
            await Assert.That(shadow).IsLessThan(Contrast.RelativeLuminance(Rgb(scheme.Role(plane)))).Because(plane);
        }
    }

    /// <summary>
    /// The resolver swaps colours and nothing else: escaped brackets stay text, a link's target is never read
    /// as a colour, a colour sample keeps the colour it samples even when it shares a role's hex, and a colour
    /// that is not a role is held to the floor against the plane it lands on.
    /// </summary>
    [Test]
    public async Task PaintingRewritesColoursAndLeavesTextAlone()
    {
        var scheme = ScreenTheme.For(ThemeLibrary.Get("Light"));

        await Assert.That(scheme.Paint($"[[{ScreenPalette.Label}]] [{ScreenPalette.Label}]x[/]"))
            .IsEqualTo($"[[{ScreenPalette.Label}]] [{scheme.Label}]x[/]");
        await Assert.That(scheme.Paint($"[link=mux:send:{ScreenPalette.Value}]go[/]"))
            .IsEqualTo($"[link=mux:send:{ScreenPalette.Value}]go[/]");
        await Assert.That(scheme.Paint($"[{ScreenPalette.Accent}]████[/]"))
            .IsEqualTo($"[{ScreenPalette.Accent}]████[/]");
        await Assert.That(scheme.Paint($"[{ScreenPalette.Ink} on {ScreenPalette.Accent}] ⏎ [/]"))
            .IsEqualTo($"[{scheme.Ink} on {scheme.Accent}] ⏎ [/]");

        // A world's own bright accent, which was legible on the dark backdrop, is darkened onto the light one.
        var painted = scheme.Paint("[bold #66ffcc]Aetherfall[/]");
        var hex = painted.Substring("[bold ".Length, 7);
        await Assert.That(Contrast.Ratio(Rgb(hex), Rgb(scheme.PanelBg))).IsGreaterThanOrEqualTo(Contrast.Floor);
    }

    /// <summary>
    /// The frames say so too: on Light, no settings screen paints a single cell on a plane of the dark
    /// palette. This is what catches a view that builds a control from a renderer's markup without resolving
    /// it — the dark island D6 was — which a contrast audit cannot, because a dark band with its own light
    /// text on it is perfectly legible.
    /// </summary>
    [Test]
    [Arguments("triggers")]
    [Arguments("worlds")]
    [Arguments("textansi")]
    [Arguments("aliases")]
    [Arguments("timers")]
    [Arguments("keypad-edit")]
    [Arguments("mssp")]
    [Arguments("compose")]
    [Arguments("deletions")]
    [Arguments("packages")]
    public async Task NoLightSettingsFramePaintsADarkPlane(string view)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ThemeName = "Light";
        config.Theme = ThemeLibrary.Get("Light");
        var caps = new TerminalCapabilities(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);
        var app = new SharpMUTermApp(config, caps, new HeadlessConsoleDriver(120, 32));
        var frame = app.RenderSnapshot(view);

        var dark = Planes.Select(Rgb).ToHashSet();
        var cells = FrameGrid.Cells(frame, 120, 32).Values.ToList();
        var offending = cells.Count(c => c.Background is { } bg && dark.Contains(bg));

        await Assert.That(cells.Count).IsGreaterThan(1000);
        await Assert.That(offending).IsEqualTo(0).Because($"--theme Light --view {view}");
    }
}
