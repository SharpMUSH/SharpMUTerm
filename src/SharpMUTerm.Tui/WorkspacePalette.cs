using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;

namespace SharpMUTerm.Tui;

/// <summary>
/// The tones the main workspace is drawn on, derived from the active theme rather than written as
/// hexes so every theme gets the same <em>relationships</em>: <see cref="Surface"/> under a pane,
/// <see cref="Backdrop"/> under everything else, <see cref="Rule"/> between panes.
/// <para>
/// A plane says two things and they are kept on separate channels: <b>identity is hue</b>
/// (<see cref="Tint"/>) and <b>focus is luminance</b> (<see cref="Focus(Rgb)"/>). Focus is a
/// multiplication, so it lands the same ratio above every plane it is applied to and neither cue can be
/// read as the other.
/// </para>
/// </summary>
internal static class WorkspacePalette
{
    /// <summary>
    /// How far the surface moves from the theme's text background toward its chrome background. Small:
    /// this is the plane the game's own colours are read against.
    /// </summary>
    private const double ChromeTint = 0.25;

    /// <summary>
    /// The backdrop as a fraction of the surface — the mean of <c>PanelBg ÷ EditBg</c> from
    /// <see cref="ScreenPalette"/>, so the workspace and the settings screens agree on how far a card
    /// floats.
    /// </summary>
    private const double BackdropScale = 0.757;

    /// <summary>
    /// How far the hairline moves from the <em>surface</em> toward <see cref="Theme.Border"/>. Measured
    /// off the surface because the rule has both planes beside it and only that end guarantees a step
    /// away from each; short of the border itself, since a divider's job is to be found, not noticed.
    /// </summary>
    private const double RuleLift = 0.45;

    /// <summary>
    /// How far a <em>focused</em> plane is lifted off an unfocused one — the mean of
    /// <c>CursorBg ÷ EditBg</c>, the step the settings screens take to say "the keyboard is here".
    /// A scale rather than a mix toward a hue, so the cue is pure luminance and survives a monochrome
    /// terminal.
    /// </summary>
    private const double FocusScale = 1.595;

    /// <summary>How far the idle input band is recessed from the theme's chrome tone.</summary>
    private const double IdleBandScale = 0.814;

    /// <summary>
    /// How far the armed input band leans toward <see cref="Theme.Prompt"/> — enough to read as a
    /// different colour rather than only a brighter one, so the pair survives a reader who sees
    /// luminance but not hue.
    /// </summary>
    private const double PromptTint = 0.28;

    /// <summary>The plane a pane's output is painted on — tab strip, scrollback and empty rows alike.</summary>
    internal static Rgb Surface(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Mix(theme.Background, theme.StatusBackground, ChromeTint);
    }

    /// <summary>
    /// The plane the focused pane is painted on, and the band behind the command line ⏎ sends from. One
    /// tone for both, so "you are here" is one thing to learn.
    /// </summary>
    internal static Rgb Focus(Theme theme) => Focus(Surface(theme));

    /// <summary>
    /// The focus step taken off whatever plane a pane is actually painted on, tinted or not. It is a
    /// multiplication, so a tinted pane is exactly as visibly focused as an untinted one.
    /// </summary>
    internal static Rgb Focus(Rgb plane) => Scale(plane, FocusScale);

    /// <summary>
    /// The plane a character's pane is painted on, so a workspace holding several characters says whose
    /// pane is whose. <see cref="PaneTint.None"/> returns the surface byte for byte.
    /// <para>
    /// All six tints sit at one luminance, by construction: the plane is re-lit to
    /// <see cref="TintDepth"/> before the anchor is mixed in, and luma is linear in the channels, so both
    /// ends of the blend share it and so does every point between. No character's pane is brighter than
    /// another's. That luminance sits <em>below</em> the untinted surface because MU* servers are written
    /// for black terminals and their bright ANSI is what is read here.
    /// </para>
    /// <para>
    /// The cue is truecolor and says nothing on a monochrome terminal, deliberately: what has to survive
    /// a lost hue is focus, and the rail and the tab title still name the character in words.
    /// </para>
    /// </summary>
    internal static Rgb Tint(Theme theme, PaneTint tint)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Tinted(Surface(theme), tint, TintDepth);
    }

    /// <summary>
    /// One plane wearing one character's colour: re-lit to <paramref name="depth"/> of its own
    /// luminance, then mixed toward the tint's hue at that same luminance. The two callers pass
    /// different depths, which is the whole composition rule — a pane takes the step down, the command
    /// line takes hue only.
    /// </summary>
    private static Rgb Tinted(Rgb plane, PaneTint tint, double depth)
    {
        if (Anchor(tint) is not { } anchor)
        {
            return plane;
        }

        var lit = AtLuma(plane, Luma(plane) * depth);
        return Mix(lit, AtLuma(anchor, Luma(lit)), TintStrength);
    }

    /// <summary>
    /// How far below the untinted surface a tinted pane sits. <b>Bounded from below by the focus step,
    /// arithmetically:</b> a client may hold tinted and untinted characters at once, and a depth reaching
    /// <c>1 ÷ <see cref="FocusScale"/></c> would leave a focused tinted pane no brighter than an
    /// unfocused untinted one. The geometric mean of that floor and no darkening puts the untinted
    /// surface midway in ratio, so every focused pane outshines every unfocused one whatever the colours.
    /// </summary>
    private static readonly double TintDepth = 1.0 / Math.Sqrt(FocusScale);

    /// <summary>
    /// How far a tinted plane travels toward its hue — pure chroma, since <see cref="AtLuma"/> has
    /// already settled the brightness. Short of the whole way: the thing being identified is a character,
    /// not an alarm, and this is the plane the game's colours are read against.
    /// </summary>
    private const double TintStrength = 0.75;

    /// <summary>
    /// The hue each named tint stands for. Never painted — <see cref="Tinted"/> re-lights each to the
    /// plane it is going onto, so what is fixed here is hue and saturation. Six, spread around the wheel,
    /// so two characters' colours are recognised rather than compared; saturated, because chroma is
    /// bounded by luminance and these are re-lit onto a dark plane.
    /// </summary>
    private static Rgb? Anchor(PaneTint tint) => tint switch
    {
        PaneTint.Slate => new Rgb(0x1e, 0x5c, 0xe0),
        PaneTint.Teal => new Rgb(0x00, 0xa0, 0x94),
        PaneTint.Moss => new Rgb(0x3f, 0xa8, 0x18),
        PaneTint.Ochre => new Rgb(0xd4, 0x96, 0x00),
        PaneTint.Ember => new Rgb(0xd8, 0x33, 0x1c),
        PaneTint.Plum => new Rgb(0xb0, 0x2c, 0xd4),
        _ => null,
    };

    /// <summary>
    /// The same colour at <paramref name="target"/> luma, keeping the hue. The two directions are taken
    /// separately because only one is safe in each: darkening is a scale (which cannot leave the byte
    /// range), brightening is a blend toward white. Scaling upward would clip the strongest channel and
    /// silently change the hue.
    /// </summary>
    private static Rgb AtLuma(Rgb rgb, double target)
    {
        var luma = Luma(rgb);
        if (luma <= 0)
        {
            return rgb;
        }

        return luma >= target
            ? Scale(rgb, target / luma)
            : Mix(rgb, new Rgb(255, 255, 255), (target - luma) / (255 - luma));
    }

    /// <summary>
    /// Perceived brightness (ITU-R BT.601). Being <em>linear</em> in the channels is the property
    /// <see cref="Tint"/> leans on, not the particular weights.
    /// </summary>
    private static double Luma(Rgb rgb) => ((rgb.R * 299.0) + (rgb.G * 587.0) + (rgb.B * 114.0)) / 1000.0;

    /// <summary>
    /// The band a command line is drawn on when ⏎ will <em>not</em> send from it. Measured off
    /// <see cref="Theme.StatusBackground"/> rather than <see cref="Surface"/>, because the input area
    /// belongs to the chrome family.
    /// </summary>
    internal static Rgb IdleBand(Theme theme) => IdleBand(theme, PaneTint.None);

    /// <summary>
    /// The same band wearing a character's colour, so a glance at the command line says whose connection
    /// ⏎ is aimed at.
    /// <para>
    /// <b>Hue only — the band keeps its luminance exactly.</b> On this row luminance is already the
    /// armed-versus-idle cue, so a colour that moved it too would put a second fact on a channel that
    /// carries one, and <see cref="IdleInk"/> would lose the contrast it was picked with.
    /// </para>
    /// </summary>
    internal static Rgb IdleBand(Theme theme, PaneTint tint)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Tinted(Scale(theme.StatusBackground, IdleBandScale), tint, depth: 1.0);
    }

    /// <summary>
    /// The band behind the command line ⏎ <em>does</em> send from: the idle band lifted by
    /// <see cref="FocusScale"/> and leaned toward <see cref="Theme.Prompt"/>. The hue is affordable here
    /// and not on a pane, because a pane's plane is what the game's own colours are read against.
    /// </summary>
    internal static Rgb ArmedBand(Theme theme) => ArmedBand(theme, PaneTint.None);

    /// <summary>
    /// The armed band over a tinted idle one. Derived from <see cref="IdleBand(Theme, PaneTint)"/> rather
    /// than tinted itself, so the lift and the prompt lean land <em>after</em> the character's hue and
    /// both cues survive a tint.
    /// </summary>
    internal static Rgb ArmedBand(Theme theme, PaneTint tint)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Mix(Scale(IdleBand(theme, tint), FocusScale), theme.Prompt, PromptTint);
    }

    /// <summary>
    /// Text on an idle band, dimmer than the armed bar's ink so the pair reads apart if a terminal
    /// flattens both backgrounds. Measured against the untinted band — safely, since a tint moves that
    /// band's hue and not its luminance — which also keeps the tab chips that share it from acquiring a
    /// per-character text colour.
    /// </summary>
    internal static Rgb IdleInk(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Mix(theme.Foreground, IdleBand(theme), 0.55);
    }

    /// <summary>
    /// The plane everything that is not a pane sits on: the rail, the status line, and the gaps a split
    /// leaves between panes.
    /// </summary>
    internal static Rgb Backdrop(Theme theme) => Recessed(Surface(theme));

    /// <summary>
    /// One step behind a plane — the backdrop's step, reused for the chips of a tab strip's unselected
    /// tabs. A chip states one fact relative to its own strip: the selected tab is painted the plane its
    /// page is painted on, its siblings are recessed from it. Pane focus is not a term, because it is
    /// already in the plane.
    /// </summary>
    internal static Rgb Recessed(Rgb plane) => Scale(plane, BackdropScale);

    /// <summary>The one-cell hairline a split draws between two panes, and beside the rail.</summary>
    internal static Rgb Rule(Theme theme) => Mix(Surface(theme), theme.Border, RuleLift);

    /// <summary>
    /// The chrome the header ribbon's character segment sits on — the chrome band lifted toward its own
    /// foreground, so the segment reads as a chip against the band it ends on.
    /// </summary>
    internal static Rgb HeaderChip(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Mix(theme.StatusBackground, theme.Foreground, ChipLift);
    }

    /// <summary>
    /// How far the header chip is lifted toward the theme's ink: enough that the wedge between the two
    /// reads as a shape, no further, since this is a background for a name and not a highlight.
    /// </summary>
    private const double ChipLift = 0.22;

    /// <summary>
    /// Every plane a pane's output can be painted on — the untinted surface and all six tints, each
    /// focused and not. They form a band: every one is a theme background put through a darkening and a
    /// brightening.
    /// </summary>
    private static IEnumerable<Rgb> PanePlanes(Theme theme)
    {
        foreach (var tint in Enum.GetValues<PaneTint>())
        {
            var plane = Tint(theme, tint);
            yield return plane;
            yield return Focus(plane);
        }
    }

    /// <summary>
    /// The one plane a foreground must clear to be legible on <em>every</em> pane: the extreme of
    /// <see cref="PanePlanes"/> in the direction a foreground on this theme travels. It is the true worst
    /// case rather than an approximation — past the band the contrast ratio is monotone in the
    /// background's luminance, so clearing the furthest clears them all.
    /// <para>
    /// Per theme rather than per pane, because resolving against a pane's actual plane would mean
    /// re-formatting a whole buffer on every focus move.
    /// </para>
    /// </summary>
    internal static Rgb ReadingPlane(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Extreme(PanePlanes(theme), Surface(theme));
    }

    /// <summary>
    /// The same worst case for the colours the <em>client</em> paints in its own voice, which land on the
    /// backdrop and on tab chips as well as on panes — so one ink is legible wherever the chrome puts it.
    /// </summary>
    internal static Rgb ChromePlane(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        // The recessed chips belong in the set: an unread tint is chrome ink and lands on one of them.
        var planes = PanePlanes(theme).ToList();
        return Extreme(planes.Concat(planes.Select(Recessed)).Append(Backdrop(theme)), Surface(theme));
    }

    /// <summary>
    /// The member of <paramref name="planes"/> hardest for a foreground to clear: brightest when
    /// <paramref name="reference"/> is dark, darkest when it is light.
    /// </summary>
    private static Rgb Extreme(IEnumerable<Rgb> planes, Rgb reference) =>
        Contrast.RelativeLuminance(reference) < LightPlaneLuminance
            ? planes.MaxBy(Contrast.RelativeLuminance)
            : planes.MinBy(Contrast.RelativeLuminance);

    /// <summary>
    /// Mid-scale in relative luminance. Measured in luminance and not in bytes: <c>#808080</c> reads as
    /// half way when written down but is 0.216, so a byte pivot would call it dark and push text
    /// <em>toward</em> it.
    /// </summary>
    private const double LightPlaneLuminance = 0.18;

    /// <summary>
    /// The colours this client paints in its own voice, resolved against the theme and held to
    /// <see cref="Contrast.Floor"/> on the plane they land on.
    /// </summary>
    internal static ChromeInk Chrome(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var plane = ChromePlane(theme);

        // The marker's hue is the theme's own index 5, so a theme overriding the base palette contributes
        // its violet. Whether that colour can be *read* is not the theme's to decide: on the default dark
        // theme index 5 is #800080 against a #36363d pane, 1.27:1.
        return new ChromeInk(
            Contrast.Legible(ChromeInk.BaseAccent, plane).ToHex(),
            Contrast.Legible(ChromeInk.BaseNotice, plane).ToHex(),
            Contrast.Legible(ChromeInk.BaseDraft, plane).ToHex(),
            Contrast.Legible(theme.ResolveIndex(5), plane).ToHex(),
            plane);
    }

    /// <summary>Linear blend, <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static Rgb Mix(Rgb from, Rgb to, double t) => new(
        Channel(from.R + ((to.R - from.R) * t)),
        Channel(from.G + ((to.G - from.G) * t)),
        Channel(from.B + ((to.B - from.B) * t)));

    /// <summary>Scales a colour toward black, keeping its hue.</summary>
    private static Rgb Scale(Rgb rgb, double factor) =>
        new(Channel(rgb.R * factor), Channel(rgb.G * factor), Channel(rgb.B * factor));

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
