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

    /// <summary>How far a hovered divider leans from the hairline toward the prompt colour.</summary>
    private const double RuleHoverLean = 0.55;

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

    /// <summary>
    /// The plane a pane's output is painted on — tab strip, scrollback and empty rows alike.
    /// <para>
    /// On a dark theme it is then taken <see cref="DarkSurfaceDepth"/> of the way down, because every
    /// other plane is measured off this one and the focus step is a <em>lift</em>: a base that sat level
    /// with the theme's text background left the focused pane, and the tinted panes' focused planes,
    /// reading as grey rather than as the dark terminal MU* output is written for. A light theme is left
    /// where it is — darkening it would make it a grey theme, and its focus step already clamps at white.
    /// </para>
    /// </summary>
    internal static Rgb Surface(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var plane = Mix(theme.Background, theme.StatusBackground, ChromeTint);
        return Contrast.RelativeLuminance(plane) < LightPlaneLuminance ? Scale(plane, DarkSurfaceDepth) : plane;
    }

    /// <summary>
    /// How far a dark theme's surface is scaled toward black. Chosen so the <em>focused</em> plane lands
    /// near where the unfocused one used to be (<c>#26262b</c> against <c>#222226</c> on Dark): the lift
    /// still reads, and it no longer reads as the pane having gone grey. Scaling rather than mixing toward
    /// black keeps the theme's hue, so Solarized Dark stays blue-green.
    /// </summary>
    private const double DarkSurfaceDepth = 0.7;

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
    /// All ten tints sit at one luminance, by construction: the plane is re-lit to
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
        return Tinted(Surface(theme), TintGround(theme), tint, TintDepth);
    }

    /// <summary>
    /// One plane wearing one character's colour: re-lit to <paramref name="depth"/> of its own
    /// luminance, then mixed toward the tint's hue at that same luminance. The two callers pass
    /// different depths, which is the whole composition rule — a pane takes the step down, the command
    /// line takes hue only. <paramref name="ground"/> is the hue the tint is laid over
    /// (<see cref="TintGround"/>); only its hue counts, since it is re-lit to the plane.
    /// </summary>
    private static Rgb Tinted(Rgb plane, Rgb? ground, PaneTint tint, double depth)
    {
        if (Anchor(tint) is not { } anchor)
        {
            return plane;
        }

        var lit = AtLuma(ground ?? plane, Luma(plane) * depth);
        return Mix(lit, AtLuma(anchor, Luma(lit)), TintStrength);
    }

    /// <summary>
    /// The hue a tint is laid over, when it is not the plane's own (null). The built-in Dark theme's planes are the
    /// mockups' navy, and the ten anchors were picked against the near-neutral grey its surface used to be: laid over
    /// navy, the residual blue drew the tints together for a dichromat (Moss and Ochre to 1.6 under protanopia, Teal
    /// and Jade to 1.7 under deuteranopia, against a floor of 1.9). So on Dark a tinted plane keeps that grey's hue
    /// (<see cref="DarkTintGround"/>), at its own luminance as before — every tinted plane, and the step between them,
    /// is what it was; only the untinted plane is navy. Every other theme lays its tints over its own plane, and so does
    /// the command line on every theme: it takes hue only, and its band is not one of the ten-plus-one a reader compares.
    /// </summary>
    private static Rgb? TintGround(Theme theme) =>
        theme.Background == DarkBackground ? DarkTintGround : null;

    private static readonly Rgb DarkBackground = ThemeLibrary.Dark().Background;

    /// <summary>The Dark theme's surface before it was navy, whose hue its tints are laid over.</summary>
    private static readonly Rgb DarkTintGround = new(0x18, 0x18, 0x1b);

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
    /// not an alarm, and this is the plane the game's colours are read against. Higher than it was before
    /// <see cref="DarkSurfaceDepth"/>: chroma is bounded by luminance, so a darker plane needs more of the
    /// hue to stay as far from its neighbours.
    /// </summary>
    private const double TintStrength = 0.85;

    /// <summary>
    /// The hue each named tint stands for. Never painted — <see cref="Tinted"/> re-lights each to the
    /// plane it is going onto, so what is fixed here is hue and saturation. Ten, spread around the wheel,
    /// so two characters' colours are recognised rather than compared; saturated, because chroma is
    /// bounded by luminance and these are re-lit onto a dark plane.
    /// <para>
    /// <b>Picked for colour-blind readers as well</b> (<c>TintColourVisionTests</c>): searched, within
    /// 40° of each name's hue, for the set whose closest pair — among the ten and the untinted plane, on
    /// all three themes — is furthest apart under simulated protanopia, deuteranopia and tritanopia,
    /// without the closest pair for typical vision getting any closer and without the light theme's planes
    /// leaving pastel. Worst pair, CIEDE2000: 2.05 protan, 2.22 deutan, 2.00 tritan on Dark (were 1.74,
    /// 0.28, 0.83), 1.99 deutan on Solarized Dark; typical vision 3.96 (was 3.71). That is "two panes side
    /// by side differ", not "a pane is recognised alone": ten planes at one luminance near L* 7 cannot be,
    /// for a dichromat — the best found with the names ignored was about 2.2 — which is why a tinted
    /// character's rail row and tab say the colour by name.
    /// </para>
    /// </summary>
    private static Rgb? Anchor(PaneTint tint) => tint switch
    {
        PaneTint.Slate => new Rgb(0x17, 0x57, 0xe0),
        PaneTint.Teal => new Rgb(0x2e, 0xa0, 0x7f),
        PaneTint.Moss => new Rgb(0x48, 0xa8, 0x30),
        PaneTint.Ochre => new Rgb(0xa1, 0x32, 0x00),
        PaneTint.Ember => new Rgb(0xd8, 0x00, 0x18),
        PaneTint.Plum => new Rgb(0x60, 0x2f, 0xa1),
        PaneTint.Indigo => new Rgb(0x40, 0x40, 0xff),
        PaneTint.Azure => new Rgb(0x00, 0x89, 0xce),
        PaneTint.Jade => new Rgb(0x07, 0xb0, 0x6e),
        PaneTint.Rose => new Rgb(0xc6, 0x17, 0x84),
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
        return Tinted(Scale(theme.StatusBackground, IdleBandScale), ground: null, tint, depth: 1.0);
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
    /// <para>
    /// A light theme takes a shallower step (<see cref="LightBackdropScale"/>): the backdrop's ratio taken off a
    /// near-white surface landed at mid-grey (<c>#b7b7b9</c>, L* 75, 21 L* under the panes), and a rail and status
    /// row that grey read as disabled. The step on Light is now about 4 L*, Solarized Dark's size.
    /// </para>
    /// </summary>
    internal static Rgb Backdrop(Theme theme)
    {
        var surface = Surface(theme);
        return Contrast.RelativeLuminance(surface) < LightPlaneLuminance ? Recessed(surface) : Scale(surface, LightBackdropScale);
    }

    /// <summary>The backdrop as a fraction of a light theme's surface: about L* 91 under Light's L* 96 panes.</summary>
    private const double LightBackdropScale = 0.95;

    /// <summary>
    /// One step behind a plane — the backdrop's step, reused for the chips of a tab strip's unselected
    /// tabs. A chip states one fact relative to its own strip: the selected tab is painted the plane its
    /// page is painted on, its siblings are recessed from it. Pane focus is not a term, because it is
    /// already in the plane.
    /// <para>
    /// A light plane takes a shallower step (<see cref="LightRecessScale"/>), for the backdrop's reason: the dark
    /// themes' step off a near-white pane landed unselected chips and resting strip panels on a mid-grey (L* 75) that
    /// read as disabled. Twice the light backdrop's step (about 9 L*), so a recessed chip or strip is still a step below
    /// the pane and a step apart from the backdrop beside it.
    /// </para>
    /// </summary>
    internal static Rgb Recessed(Rgb plane) =>
        Scale(plane, Contrast.RelativeLuminance(plane) < LightPlaneLuminance ? BackdropScale : LightRecessScale);

    /// <summary>
    /// The plane the history half of a pane split by scrolling back is drawn on (the terminal mockup's <c>#1a1a20</c> over a
    /// <c>#1e1e24</c> live tail): one small step <em>away from the text</em> off the pane's own plane — deeper on a dark
    /// plane, toward white on a light one — so the rows being read and the rows still arriving are two planes, and the
    /// step can only widen the contrast <see cref="ReadingPlane"/> already guarantees.
    /// </summary>
    internal static Rgb History(Rgb plane) =>
        Contrast.RelativeLuminance(plane) < LightPlaneLuminance
            ? Scale(plane, HistoryScale)
            : Mix(plane, new Rgb(0xff, 0xff, 0xff), HistoryLift);

    /// <summary>The history's step on a dark plane: the terminal mockup's ratio.</summary>
    private const double HistoryScale = 0.87;

    /// <summary>The history's step toward white on a light plane.</summary>
    private const double HistoryLift = 0.6;

    /// <summary>The recess as a fraction of a light plane: about L* 87 under Light's L* 96 panes.</summary>
    private const double LightRecessScale = 0.90;

    /// <summary>The one-cell hairline a split draws between two panes, and beside the rail.</summary>
    internal static Rgb Rule(Theme theme) => Mix(Surface(theme), theme.Border, RuleLift);

    /// <summary>
    /// The band a docked panel's heading is drawn on (<c>HERE</c>, <c>COMPOSE</c>, <c>VITALS</c>), the terminal mockups'
    /// heading bar, and the scrolled-back band between a split pane's two viewports: the rule leaned toward
    /// <see cref="Theme.Prompt"/>, so it is past every pane plane, the focused one included (a step above on a dark theme,
    /// below on a light one), and a hue apart from the divider rule it may sit beside.
    /// </summary>
    internal static Rgb HeadingBand(Theme theme) => Mix(Rule(theme), theme.Prompt, HeadingLean);

    /// <summary>How far <see cref="HeadingBand"/> leans from the rule toward the prompt colour.</summary>
    private const double HeadingLean = 0.2;

    /// <summary>A heading's words: the theme's text, held to body-text contrast (4.5:1) on <see cref="HeadingBand"/>.</summary>
    internal static Rgb HeadingInk(Theme theme) => Contrast.Legible(theme.Foreground, HeadingBand(theme), HeadingFloor);

    /// <summary>The floor a heading's words and accent are held to: small text, the status row's 4.5:1.</summary>
    internal const double HeadingFloor = 4.5;

    /// <summary>
    /// A divider under the pointer, or being dragged: the hairline leaned toward the prompt colour, the anchor
    /// the armed command line leans to. Enough to say "this moves" without reading as a second focus cue.
    /// </summary>
    internal static Rgb RuleHover(Theme theme) => Mix(Rule(theme), theme.Prompt, RuleHoverLean);

    /// <summary>
    /// The floor the header's text is held to — WCAG AA for body text. The header is the one row every frame carries,
    /// and its segment labels are names a reader looks for, not de-emphasis; <see cref="Contrast.Floor"/> (3:1) is for
    /// the workspace's own chrome and left Light's ribbon at exactly 3.01:1.
    /// </summary>
    internal const double HeaderTextFloor = 4.5;

    /// <summary>
    /// A header ribbon segment's fill for <paramref name="theme"/>. A dark theme keeps the segment's own neon (the
    /// identity the ribbon exists to show, inked in the dark background). A light theme gets a <em>deep</em> fill of the
    /// same hue — darkened until the theme's own light background reads on it at <see cref="HeaderTextFloor"/> — and is
    /// inked light, because Dark's neon with ink driven down to the floor read washed out. Both hosts read it: the cell
    /// header and the native one (<c>ChromePalette.Brand</c>).
    /// </summary>
    internal static Rgb HeaderFill(Theme theme, Rgb fill)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Contrast.RelativeLuminance(theme.Background) < LightPlaneLuminance
            ? fill
            : Contrast.Legible(fill, theme.Background, HeaderTextFloor);
    }

    /// <summary>
    /// The ink on a header segment's fill: the theme's own background (dark on a dark theme's neon, light on a light
    /// theme's deep fill), held to <see cref="HeaderTextFloor"/> against the fill it lands on.
    /// </summary>
    internal static Rgb HeaderInk(Theme theme, Rgb fill)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Contrast.Legible(theme.Background, fill, HeaderTextFloor);
    }

    /// <summary>
    /// A notice's quiet text on the status row: the framework's <c>[dim]</c> grey, which is fixed and fell to 2.4:1 on
    /// Light, replaced by the same grey held to <see cref="HeaderTextFloor"/> on <paramref name="plane"/>.
    /// </summary>
    internal static Rgb NoticeMuted(Theme theme, Rgb plane)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Contrast.Legible(new Rgb(0x80, 0x80, 0x80), plane, HeaderTextFloor);
    }

    /// <summary>The header's quiet text (the counts, the log state), held to <see cref="HeaderTextFloor"/> on its band.</summary>
    internal static Rgb HeaderMuted(Theme theme) => HeaderMuted(theme, theme?.StatusBackground ?? throw new ArgumentNullException(nameof(theme)));

    /// <summary>The header's quiet text held to <see cref="HeaderTextFloor"/> on <paramref name="plane"/>.</summary>
    internal static Rgb HeaderMuted(Theme theme, Rgb plane)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Contrast.Legible(Mix(plane, theme.StatusForeground, MutedLean), plane, HeaderTextFloor);
    }

    /// <summary>
    /// The plane a host draws its native header on. A dark theme keeps the status band (a step above the backdrop, the
    /// mockup's header over its navigation). A light theme's status band is a tinted grey (Light's lavender
    /// <c>#dcdce6</c>) that sat beside the neutral rail and footer as a third, unrelated grey; there the header shares the
    /// backdrop with the rail and the footer, and the rule under it is the boundary. Native only: the cell header keeps
    /// its band.
    /// </summary>
    internal static Rgb NativeHeaderPlane(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Contrast.RelativeLuminance(theme.Background) < LightPlaneLuminance ? theme.StatusBackground : Backdrop(theme);
    }

    /// <summary>
    /// The low cue on a meter (a vital below <c>StatusMeterRenderer.LowShare</c>): its bar, its value and its <c>low</c>
    /// mark, in both hosts — the settings screens' warning red (<see cref="ScreenPalette.Warn"/>, the mockups' low mana)
    /// held to <see cref="HeaderTextFloor"/> on <paramref name="plane"/>. It was the notice amber on a dark theme until
    /// meters had colours of their own: movement is gold, and an amber low cue said nothing beside it. A red pushed down
    /// to a light plane's floor stays red, where the amber turned olive (UX audit #29).
    /// </summary>
    internal static Rgb Low(Theme theme, Rgb plane)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Contrast.Legible(LowBase, plane, HeaderTextFloor);
    }

    /// <summary>The low cue before the plane has said how deep it must be.</summary>
    private static readonly Rgb LowBase = new(0xff, 0x6b, 0x6b);

    /// <summary>How far the header's quiet text leans from its band toward its ink before the floor is applied.</summary>
    private const double MutedLean = 0.55;

    /// <summary>
    /// Every plane a pane's output can be painted on — the untinted surface and all ten tints, each
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
    /// Whether a theme's panes are dark — a foreground on them is lifted rather than darkened. The map's area colours
    /// take their dark or light set by this (<c>MapAreaPalette.For</c>).
    /// </summary>
    internal static bool DarkPanes(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Contrast.RelativeLuminance(Surface(theme)) < LightPlaneLuminance;
    }

    /// <summary>
    /// The band a selected run of a pane's output is painted on.
    /// <para>
    /// <b>One pair per theme, not per pane</b>, and for a reason of its own rather than
    /// <see cref="ReadingPlane"/>'s. There it is cost; here it is meaning. A pane's plane says two things
    /// already — whose connection this is, in hue, and where the keyboard is, in luminance — and a
    /// selection is neither. It is the client answering a gesture the user is making *now*, it exists in
    /// exactly one pane at a time, and a highlight that changed colour depending on whose pane it landed in
    /// would be reporting a fact nobody asked it about.
    /// </para>
    /// <para>
    /// Built from <see cref="ReadingPlane"/> — the extreme of the twenty-two in the direction of travel —
    /// pushed <em>further</em> that way, so it clears the whole band rather than the one plane it was
    /// derived from, and then leaned toward <see cref="Theme.Prompt"/>. The lean is what makes it read as
    /// the client's own mark instead of a pane that has gone brighter, and it is the same anchor
    /// <see cref="ArmedBand(Theme)"/> uses, which is the other place this client says "your keystrokes are
    /// about this".
    /// </para>
    /// </summary>
    internal static Rgb SelectionBand(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        // Away from the panes, whichever way that is. On a dark theme the reading plane is the brightest of
        // the twenty-two and the selection goes brighter still; on a light one it is the darkest and the
        // selection goes darker. Scaling the wrong way would land the band *inside* the band it has to
        // stand clear of — the exact failure Extreme exists to describe, one step further out.
        var plane = ReadingPlane(theme);
        var dark = Contrast.RelativeLuminance(Surface(theme)) < LightPlaneLuminance;
        return Mix(Scale(plane, dark ? SelectionScale : 1.0 / SelectionScale), theme.Prompt, SelectionTint);
    }

    /// <summary>
    /// The band a selection in the command line is painted on: <see cref="SelectionBand"/>'s recipe applied
    /// to the armed input band instead of the reading plane. The pane band is measured against the panes
    /// and the armed bar is the brightest plane the client paints on a dark theme, so on the bar the pane
    /// band sat only about 1.7:1 off its background — legible, and barely there. Same direction of travel,
    /// same step and the same lean toward <see cref="Theme.Prompt"/>, so the two still read as one mark.
    /// </summary>
    internal static Rgb InputSelectionBand(Theme theme, PaneTint tint)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var dark = Contrast.RelativeLuminance(Surface(theme)) < LightPlaneLuminance;
        var armed = ArmedBand(theme, tint);
        return Mix(Scale(armed, dark ? SelectionScale : 1.0 / SelectionScale), theme.Prompt, SelectionTint);
    }

    /// <summary>The ink of selected command-line text, held to the floor on <see cref="InputSelectionBand"/>.</summary>
    internal static Rgb InputSelectionInk(Theme theme, PaneTint tint)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Contrast.Legible(theme.Foreground, InputSelectionBand(theme, tint));
    }

    /// <summary>
    /// The one colour every selected cell's text is painted in, held to <see cref="Contrast.Floor"/> on
    /// <see cref="SelectionBand"/>.
    /// <para>
    /// The framework's highlight replaces the foreground as well as the background on every selected cell,
    /// so a world's own colours are gone for the duration and this single ink is what all selected output
    /// is read in. That makes it the one place in this client where failing the floor would make text
    /// unreadable rather than merely quiet.
    /// </para>
    /// </summary>
    internal static Rgb SelectionInk(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Contrast.Legible(theme.Foreground, SelectionBand(theme));
    }

    /// <summary>
    /// How far past the reading plane the selection band sits. Larger than <see cref="FocusScale"/> on
    /// purpose: the focus step separates a pane from its neighbours, and a selection has to separate itself
    /// from a pane that may already have taken that step.
    /// </summary>
    private const double SelectionScale = 1.9;

    /// <summary>How much of the prompt hue the band carries — enough to be recognised, not a wash.</summary>
    private const double SelectionTint = 0.35;

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
