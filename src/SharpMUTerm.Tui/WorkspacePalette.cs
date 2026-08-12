using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;

namespace SharpMUTerm.Tui;

/// <summary>
/// The three tones the main workspace is drawn on — the output <see cref="Surface"/> a pane paints,
/// the <see cref="Backdrop"/> everything that is not a pane sits on, and the <see cref="Rule"/>
/// hairline between panes.
/// <para>
/// They are <em>derived from the active theme</em> rather than written down as hexes, because the
/// theme is the user's (Dark / Light / Solarized Dark / a hand-written one) and a fixed pair would be
/// right for exactly one of them. What is fixed is the <em>relationship</em>, and it is the one the
/// settings screens already use: <see cref="ScreenPalette.PanelBg"/> sits a little over three quarters
/// of the way from black to <see cref="ScreenPalette.EditBg"/>, so a card reads as raised off its
/// backdrop without either tone leaving the family. The workspace uses that same step, which is why
/// F5 and the pane behind it look like two views of one application.
/// </para>
/// <para>
/// The surface is deliberately <em>not</em> the theme's plain text background: it is that background
/// nudged a quarter of the way toward the theme's own chrome tone, so the output area carries a hint
/// of the colour the header and input bands are already painted in. MU* text is unaffected — a span
/// with the default background emits no background at all (see <see cref="MarkupFormatter"/>), so it
/// takes whatever surface it is drawn on.
/// </para>
/// <para>
/// A pane's plane can also carry <em>whose</em> pane it is (<see cref="Tint"/>), and the two facts a
/// plane states are kept on separate channels so neither can be mistaken for the other: <b>identity is
/// hue and focus is luminance</b>. <see cref="Tint"/> moves the surface's colour, <see cref="Focus(Rgb)"/>
/// multiplies its brightness, and because the focus step is a <em>ratio</em> it lands the same relative
/// distance above every plane it is applied to.
/// </para>
/// <para>
/// The <b>command line wears the same hue</b> (<see cref="IdleBand(Theme, PaneTint)"/> /
/// <see cref="ArmedBand(Theme, PaneTint)"/>), so the bar under a pane says whose connection ⏎ is aimed
/// at. It takes the hue and <em>not</em> the <see cref="TintDepth"/> step the pane takes: on the input
/// band luminance is already spoken for — it is the whole armed-versus-idle cue — and a colour that
/// moved it too would put a second fact on a channel that already carries one. On the pane, luminance
/// carries focus as a ratio, which a step applied equally to all six tints leaves untouched.
/// </para>
/// </summary>
internal static class WorkspacePalette
{
    /// <summary>
    /// How far the surface moves from the theme's text background toward its chrome background. Small
    /// on purpose: this tints the plane the game's own colours are read against, and anything the eye
    /// can name as a colour would be competing with them.
    /// </summary>
    private const double ChromeTint = 0.25;

    /// <summary>
    /// The backdrop as a fraction of the surface, taken from <see cref="ScreenPalette"/>'s own pair —
    /// the mean of <c>PanelBg ÷ EditBg</c> across the three channels. Sharing the settings screens'
    /// step is the whole point: one application, one idea of how far a card floats.
    /// </summary>
    private const double BackdropScale = 0.757;

    /// <summary>
    /// How far the hairline between two panes moves from the <em>surface</em> toward the theme's border
    /// colour. Lifted off the surface rather than off the backdrop, because the rule has both planes
    /// beside it — the backdrop where it runs down the side of the rail, the surface where it separates
    /// two panes — and only the surface end guarantees a step away from each. (On a dark theme that
    /// lands it lighter than both, exactly where <see cref="ScreenPalette.Rule"/> sits on the settings
    /// screens; on a light one it lands between them, which is where a hairline belongs there.) Short of
    /// the border itself: a rule on <see cref="Theme.Border"/> reads fine once and shouts at four panes,
    /// and a divider's job is to be found, not noticed.
    /// </summary>
    private const double RuleLift = 0.45;

    /// <summary>
    /// How far the <em>focused</em> plane is lifted off the unfocused one, as a fraction of it. Like
    /// <see cref="BackdropScale"/> this is not a number somebody liked: it is the mean of
    /// <c>CursorBg ÷ EditBg</c> across the three channels — the step the settings screens already take
    /// to say "the keyboard is here", measured off the card the cursor bar sits on
    /// (<see cref="ScreenPalette.CursorBg"/> is documented as exactly that). Reusing it means the
    /// workspace and F5 do not have two different ideas of what focus looks like, and it is a step of
    /// about three fifths rather than the thirteen points per channel the two input bands used to
    /// differ by — which is the whole complaint.
    /// <para>
    /// It is a <em>scale</em>, not a mix toward a hue, deliberately: multiplying keeps the theme's own
    /// colour and changes only its luminance, so the cue survives a monochrome terminal and a
    /// colour-blind reader, and a light theme lifts the same way a dark one does.
    /// </para>
    /// </summary>
    private const double FocusScale = 1.595;

    /// <summary>
    /// How far the idle input band is recessed from the theme's chrome tone. Like the other constants
    /// here it is measured off what the design already chose: the mean of the old hardcoded
    /// <c>#262b3a</c> over the default theme's <see cref="Theme.StatusBackground"/>, across the three
    /// channels. The idle band therefore lands where it always did — the tone was never the complaint —
    /// while <see cref="ArmedBand"/> moves away from it by a step the eye can actually find.
    /// </summary>
    private const double IdleBandScale = 0.814;

    /// <summary>
    /// How far the armed input band leans toward <see cref="Theme.Prompt"/>. Enough to read as a
    /// different <em>colour</em> and not merely a brighter one, which is the cue that survives a reader
    /// who sees luminance but not hue being given the opposite problem; small enough that the band is
    /// still the theme's chrome rather than a stripe of accent across the bottom of the window.
    /// </summary>
    private const double PromptTint = 0.28;

    /// <summary>The plane a pane's output is painted on — tab strip, scrollback and empty rows alike.</summary>
    internal static Rgb Surface(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Mix(theme.Background, theme.StatusBackground, ChromeTint);
    }

    /// <summary>
    /// The plane the <em>focused</em> pane's output is painted on, and the band behind the command line
    /// ⏎ sends from. One tone for both on purpose: "this is where you are" should be one thing to learn,
    /// not two, and the two questions a user has — which pane am I acting on, which line am I typing
    /// into — are then answered by the same colour in the two places it can appear.
    /// <para>
    /// It costs no cells. A focused pane is the same rectangle as an unfocused one, repainted; that
    /// matters because per-pane NAWS is derived from the pane rectangle, so a border or a marker column
    /// would re-announce a different terminal size to the server on every focus change and reflow the
    /// game's own output. See <c>SharpMUTermApp.PaneOutputRects</c>.
    /// </para>
    /// </summary>
    internal static Rgb Focus(Theme theme) => Focus(Surface(theme));

    /// <summary>
    /// The same focus step, taken off whatever plane a pane is actually painted on — which is
    /// <see cref="Surface"/> for a character with no tint and <see cref="Tint"/> for one that has chosen
    /// a colour. It is the <em>one</em> step either way: focus is a multiplication, so it lands the same
    /// distance above every plane it is applied to, and a tinted pane is therefore exactly as visibly
    /// focused as an untinted one.
    /// <para>
    /// This overload is what keeps the two cues from fighting. A tint changes only the plane's
    /// <em>hue</em> (see <see cref="Tint"/>); focus changes only its <em>luminance</em>. Neither can be
    /// read as the other, and no combination of the two produces a pane that is ambiguous about which
    /// question it is answering.
    /// </para>
    /// </summary>
    internal static Rgb Focus(Rgb plane) => Scale(plane, FocusScale);

    /// <summary>
    /// The plane a character's pane is painted on: <see cref="Surface"/> taken one <see cref="TintDepth"/>
    /// step down and pushed toward the tint's hue at <em>that</em> luminance, so a workspace holding
    /// several characters says whose pane is whose. <see cref="PaneTint.None"/> returns the surface
    /// itself, byte for byte — an untinted client is painted exactly as it was before this existed.
    /// <para>
    /// <b>All six tints sit at exactly one luminance, and it is not the surface's.</b> The first half is
    /// the legibility guarantee and is by construction: the plane is re-lit to the target
    /// (<see cref="AtLuma"/>) <em>before</em> the anchor is mixed into it, and luma is linear in the
    /// channels, so both ends of the blend share that luma and so does every point between them — for
    /// any <see cref="TintStrength"/> and on any theme. No character's pane is brighter than another's,
    /// and the focus step therefore lands the same <em>ratio</em> above each.
    /// </para>
    /// <para>
    /// The second half is a deliberate departure from the first cut of this feature, which held the
    /// tinted plane at the untinted surface's exact luma. <b>MU* servers are written for black
    /// terminals</b> and their own bright ANSI is what is read on this plane, so the pane wants to be
    /// darker than the client's chrome rather than level with it. What is given up is the claim that a
    /// tint changes no contrast ratio the theme was designed around; what is kept is the reason that
    /// claim was made — the game's text sits on a plane one step <em>away</em> from it on a dark theme,
    /// which raises contrast rather than lowering it. The step is bounded and the bound is not a
    /// preference: see <see cref="TintDepth"/>.
    /// </para>
    /// <para>
    /// Two consequences worth stating rather than discovering. <b>The tint carries no information a
    /// monochrome terminal can show</b>, and it must not: the cue that has to survive a lost hue is
    /// focus, and focus is luminance. A reader who cannot use the tint loses nothing they did not
    /// already have — the sidebar and the tab title still name the character in words. <b>And it is a
    /// truecolor cue</b>: a 256-colour terminal quantises these planes onto a handful of entries, which
    /// degrades to a pane that is merely dark rather than to a wrong answer. That is why
    /// <c>WorkspacePaletteTests.FocusSurvivesA256ColourTerminal</c> exists and has no tint counterpart.
    /// </para>
    /// </summary>
    internal static Rgb Tint(Theme theme, PaneTint tint)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Tinted(Surface(theme), tint, TintDepth);
    }

    /// <summary>
    /// One plane wearing one character's colour: <paramref name="plane"/> re-lit to
    /// <paramref name="depth"/> of its own luminance, then mixed toward the tint's hue at that same
    /// luminance. The two callers hand it two different depths and the difference is the whole
    /// composition rule — see the type's own summary.
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
    /// How far below the untinted surface a tinted pane sits, as a fraction of its luminance. <b>It is
    /// bounded from below by the focus step and the bound is arithmetic, not taste.</b> A client may
    /// hold tinted and untinted characters at once, so the four planes on screen are
    /// <c>tinted, surface, tinted·<see cref="FocusScale"/>, surface·FocusScale</c> — and if the depth
    /// ever reached <c>1 ÷ FocusScale</c> a <em>focused</em> tinted pane would be no brighter than an
    /// <em>unfocused</em> untinted one, which is the focus cue reporting the wrong fact for the reason
    /// the tint work exists to prevent. The value is the geometric mean of that floor and no darkening
    /// at all, so the untinted surface sits exactly midway — in ratio, √FocusScale ≈ 1.26 either way —
    /// between a tinted pane and a focused tinted one. Every focused pane on the screen is then brighter
    /// than every unfocused one, whatever colours are in play.
    /// </summary>
    private static readonly double TintDepth = 1.0 / Math.Sqrt(FocusScale);

    /// <summary>
    /// How far a tinted plane travels from its own tone toward the tint's hue. It is a pure chroma
    /// control — <see cref="AtLuma"/> has already taken the brightness question away — so this is the
    /// answer to "how coloured", and nothing else. It went up a little with <see cref="TintDepth"/>, and
    /// only a little: chroma is bounded by luminance, so the same fraction of a darker plane is a fainter
    /// colour, but the work of keeping the six apart is the <see cref="Anchor"/>s' and not this constant's.
    /// Still well short of the whole way, because this is the plane the game's own colours are read
    /// against and the thing being identified is a character, not an alarm — pushed to the anchor itself
    /// the pane reads as a coloured panel, which is a client shouting a fact nobody asked it to repeat.
    /// </summary>
    private const double TintStrength = 0.75;

    /// <summary>
    /// The hue each named tint stands for, as a reference colour. These are never painted:
    /// <see cref="Tinted"/> re-lights each one to the plane it is going onto, so what is fixed here is
    /// the <em>hue</em> and the saturation, and the brightness is the theme's.
    /// <para>
    /// Six, spread around the wheel at roughly even spacing (blue → blue-green → green → amber →
    /// red-orange → violet), because the failure this feature has is two characters whose colours a
    /// reader has to compare rather than recognise. They are saturated, and that is what the darker
    /// target bought: re-lit down to a dark theme's pane a muted anchor has almost no chroma left, and
    /// the first set — muted, at the surface's own brightness — left the two closest of the six ΔE 8.2
    /// apart on the default theme, with the nearest only ΔE 7.7 from the untinted plane. Measured the
    /// same way, these are ΔE 14.4 from each other and ΔE 12.1 from the untinted plane, on a plane that
    /// is also a fifth darker.
    /// </para>
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
    /// The same colour at a different brightness — <paramref name="target"/> luma, keeping the hue. It
    /// takes the two directions separately because only one of them is safe in each: a colour brighter
    /// than the target is <em>scaled</em> down (a multiplication can never leave the byte range), and one
    /// darker is blended toward white, which reaches any luminance up to 255 without clipping. Scaling
    /// upward would clip the strongest channel first and so would silently change the hue — on a light
    /// theme, where every anchor has to travel up, it would change it beyond recognition.
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
    /// Perceived brightness, on the same weights the rest of this codebase measures a colour by (ITU-R
    /// BT.601, which is what <c>WorkspacePaletteTests</c> and <c>FocusIndicationTests</c> already use).
    /// Being <em>linear</em> in the channels is the property <see cref="Tint"/> leans on, not the
    /// particular weights.
    /// </summary>
    private static double Luma(Rgb rgb) => ((rgb.R * 299.0) + (rgb.G * 587.0) + (rgb.B * 114.0)) / 1000.0;

    /// <summary>
    /// The chrome band a command line is drawn on when ⏎ will <em>not</em> send from it. It is the theme's
    /// status/chrome tone recessed by <see cref="IdleBandScale"/> — the input area belongs to the chrome
    /// family, not to the pane surface, which is why it is measured off
    /// <see cref="Theme.StatusBackground"/> and not off <see cref="Surface"/>.
    /// <para>
    /// It sits where the design's own idle band sat; the tone was never the complaint. What was wrong was
    /// the <em>distance</em> to the armed one — the two hardcoded hexes were a ratio of about 1.33 apart,
    /// thirteen points per channel, which is genuinely close to invisible. <see cref="ArmedBand"/> now
    /// takes the same focus step everything else does, and picks up the theme's prompt hue on the way.
    /// </para>
    /// </summary>
    internal static Rgb IdleBand(Theme theme) => IdleBand(theme, PaneTint.None);

    /// <summary>
    /// The same band wearing a character's colour — the bar under a tinted pane, so a glance at the
    /// command line says whose connection ⏎ is aimed at without reading the prompt.
    /// <para>
    /// <b>Hue only: the band keeps its luminance exactly.</b> On a pane the tint takes a
    /// <see cref="TintDepth"/> step down as well, and it must not here, because luminance on this row is
    /// already the armed-versus-idle cue — the one thing the input area says with brightness. Leaving it
    /// alone means the step between the two bands is the step it has always been, in every colour and on
    /// every theme, and that the ink chosen to be read on these bands (<see cref="IdleInk"/>) keeps the
    /// contrast it was picked with.
    /// </para>
    /// </summary>
    internal static Rgb IdleBand(Theme theme, PaneTint tint)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Tinted(Scale(theme.StatusBackground, IdleBandScale), tint, depth: 1.0);
    }

    /// <summary>
    /// The band behind the command line ⏎ <em>does</em> send from: the idle band lifted by the same
    /// <see cref="FocusScale"/> a focused pane is lifted by, and then pushed a little toward
    /// <see cref="Theme.Prompt"/> — the theme's own colour for a prompt, which is what this band is.
    /// <para>
    /// The hue is affordable here and not on a pane: a pane's plane is what the game's own colours are
    /// read against, so <see cref="Focus"/> stays a pure luminance lift, while the input band is chrome
    /// and was already tinted. Between them the armed and idle bands now differ in luminance <em>and</em>
    /// hue, on top of the bold-versus-dim prompt and the bright-versus-dim ink — four cues, of which
    /// three survive a terminal that cannot render the fourth.
    /// </para>
    /// </summary>
    internal static Rgb ArmedBand(Theme theme) => ArmedBand(theme, PaneTint.None);

    /// <summary>
    /// The armed band over a tinted idle one. Derived from <see cref="IdleBand(Theme, PaneTint)"/> rather
    /// than tinted in its own right, and that ordering is the point: the lift and the lean toward
    /// <see cref="Theme.Prompt"/> are applied <em>after</em> the character's hue, so both cues survive a
    /// tint — the armed bar is brighter than the idle one by the step it always was, and still bluer than
    /// it by the theme's own prompt colour. Tinting the armed band directly would have overwritten that
    /// lean with the character's hue and left the pair differing in brightness alone.
    /// </summary>
    internal static Rgb ArmedBand(Theme theme, PaneTint tint)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Mix(Scale(IdleBand(theme, tint), FocusScale), theme.Prompt, PromptTint);
    }

    /// <summary>
    /// Text on an idle band: the theme's foreground pulled most of the way down to that band. Dimmer
    /// than the armed bar's ink, so the pair still reads apart if a terminal flattens both backgrounds.
    /// <para>
    /// Measured against the <em>untinted</em> band, and safely so: a tint moves that band's hue and not
    /// its luminance (<see cref="IdleBand(Theme, PaneTint)"/>), so this ink keeps the contrast it was
    /// picked with whatever colour the bar is wearing. One ink for every tint also keeps the tab chips,
    /// which share it, from acquiring a per-character text colour nobody asked for.
    /// </para>
    /// </summary>
    internal static Rgb IdleInk(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Mix(theme.Foreground, IdleBand(theme), 0.55);
    }

    /// <summary>
    /// The plane everything that is not a pane sits on: the connection rail, the status line, and the
    /// gaps a split leaves between panes. Recessed relative to <see cref="Surface"/>, so an empty pane
    /// is still a visible rectangle and a workspace of many panes reads as cards on a desk.
    /// </summary>
    internal static Rgb Backdrop(Theme theme) => Recessed(Surface(theme));

    /// <summary>
    /// One step behind a plane — the backdrop's own step, reused for the chips of a tab strip's
    /// <em>unselected</em> tabs.
    /// <para>
    /// A chip states one fact, and states it relative to its own strip: the selected tab is painted the
    /// plane its page is painted on, its siblings are recessed from it. Pane focus is not a term, because
    /// it is already in the plane — so the selection cue is one ratio in every strip, on every theme,
    /// under every tint, and the two questions a strip answers stay on separate channels.
    /// </para>
    /// </summary>
    internal static Rgb Recessed(Rgb plane) => Scale(plane, BackdropScale);

    /// <summary>The one-cell hairline a split draws between two panes, and beside the rail.</summary>
    internal static Rgb Rule(Theme theme) => Mix(Surface(theme), theme.Border, RuleLift);

    /// <summary>
    /// The dim chrome the header ribbon's character segment sits on — the theme's chrome band lifted
    /// toward its own foreground, so the segment reads as a distinct chip against the band it ends on.
    /// <para>
    /// Derived rather than the fixed <c>#3f4859</c> it was. That literal was picked against a dark theme
    /// and is a <em>dark</em> chip whatever the theme, so under Light the world's accent — text on this
    /// chip — was drawn at 1.53:1 while everything around it had been resolved for a light plane.
    /// </para>
    /// </summary>
    internal static Rgb HeaderChip(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Mix(theme.StatusBackground, theme.Foreground, ChipLift);
    }

    /// <summary>
    /// How far the header chip is lifted off the chrome band toward the theme's ink. Enough that the
    /// wedge between the two is visible as a shape — the segment boundary is the only thing that says
    /// where one part of the ribbon ends — and no further, because this is a background for a name and
    /// not a highlight.
    /// </summary>
    private const double ChipLift = 0.22;

    /// <summary>
    /// Every plane a pane's output can be painted on: the untinted surface and all six tints, each
    /// focused and not. Fourteen colours, and what matters about them is that they form a <em>band</em>
    /// — the whole set sits on one side of mid-scale, because every one of them is one theme background
    /// put through a darkening and a brightening.
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
    /// The one plane a foreground has to clear for it to be legible on <em>every</em> pane — the extreme
    /// of <see cref="PanePlanes"/> in the direction a foreground on this theme is moved.
    /// <para>
    /// <b>It is the worst case rather than an approximation of one.</b> A lift pushes a foreground away
    /// from the band; once it is past the band the contrast ratio is monotone in the background's
    /// luminance, so the plane hardest to clear is the one furthest in the direction of travel — the
    /// brightest on a dark theme, the darkest on a light one. Clearing it clears all fourteen.
    /// </para>
    /// <para>
    /// <b>It is per theme, and that is the whole reason it exists.</b> A pane's actual plane depends on
    /// its character's tint and on whether it holds focus, and resolving a colour against <em>that</em>
    /// would mean re-formatting a whole buffer on every focus move — the expensive whole-buffer path
    /// this codebase reserves for one deliberate keystroke. One plane per theme means a lifted colour is
    /// decided once, when the line is formatted, and never revisited.
    /// </para>
    /// </summary>
    internal static Rgb ReadingPlane(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Extreme(PanePlanes(theme), Surface(theme));
    }

    /// <summary>
    /// The same worst case for the colours the <em>client</em> paints in its own voice, which land on
    /// the <see cref="Backdrop"/> — the status line, the rail — as well as on panes. It is
    /// <see cref="ReadingPlane"/>'s band with the backdrop added, so one ink is legible wherever the
    /// chrome puts it: a status-line segment and a pane overlay must not need two different teals.
    /// </summary>
    internal static Rgb ChromePlane(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        // The recessed chips belong in the set: an unread tint is chrome ink and lands on one of them.
        var planes = PanePlanes(theme).ToList();
        return Extreme(planes.Concat(planes.Select(Recessed)).Append(Backdrop(theme)), Surface(theme));
    }

    /// <summary>
    /// The member of <paramref name="planes"/> hardest for a foreground to clear, given that the
    /// direction of travel is decided by <paramref name="reference"/>: brightest when the theme is dark,
    /// darkest when it is light.
    /// </summary>
    private static Rgb Extreme(IEnumerable<Rgb> planes, Rgb reference) =>
        Contrast.RelativeLuminance(reference) < LightPlaneLuminance
            ? planes.MaxBy(Contrast.RelativeLuminance)
            : planes.MinBy(Contrast.RelativeLuminance);

    /// <summary>
    /// Mid-scale in relative luminance — 0.18, the sRGB middle grey. A theme whose planes sit below it
    /// is dark and its text is lifted; above it and the text is darkened. Measured in luminance and not
    /// in bytes because <c>#808080</c> looks like half way when written down and is not: its relative
    /// luminance is 0.216, and a byte pivot would call it dark and push text <em>toward</em> it.
    /// </summary>
    private const double LightPlaneLuminance = 0.18;

    /// <summary>
    /// The colours this client paints in its own voice, resolved against the theme and held to
    /// <see cref="Contrast.Floor"/> on the plane they land on. See <see cref="ChromeInk"/> for what each
    /// one is for, and what it measured before it was measured against anything.
    /// </summary>
    internal static ChromeInk Chrome(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var plane = ChromePlane(theme);

        // The marker's hue is the theme's own index 5, so a theme that overrides the base palette
        // (Solarized does) contributes its violet rather than xterm's. What is *not* the theme's to
        // decide is whether that colour can be read: on the default dark theme index 5 is #800080
        // against a #36363d pane, which is 1.27:1 — the reported "freeze is purple on a blue
        // background", and very nearly the same colour twice.
        return new ChromeInk(
            Contrast.Legible(ChromeInk.BaseAccent, plane).ToHex(),
            Contrast.Legible(ChromeInk.BaseNotice, plane).ToHex(),
            Contrast.Legible(ChromeInk.BaseDraft, plane).ToHex(),
            Contrast.Legible(theme.ResolveIndex(5), plane).ToHex(),
            plane);
    }

    /// <summary>Linear blend of two colours, <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static Rgb Mix(Rgb from, Rgb to, double t) => new(
        Channel(from.R + ((to.R - from.R) * t)),
        Channel(from.G + ((to.G - from.G) * t)),
        Channel(from.B + ((to.B - from.B) * t)));

    /// <summary>Scales a colour toward black, keeping its hue.</summary>
    private static Rgb Scale(Rgb rgb, double factor) =>
        new(Channel(rgb.R * factor), Channel(rgb.G * factor), Channel(rgb.B * factor));

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
