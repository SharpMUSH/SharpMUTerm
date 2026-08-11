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
/// hue and focus is luminance</b>. <see cref="Tint"/> moves the surface's colour without moving its
/// brightness, <see cref="Focus(Rgb)"/> moves its brightness without moving its colour, and the focus
/// step lands the same distance above a tinted plane as above an untinted one.
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
    /// The plane a character's pane is painted on: <see cref="Surface"/> pushed toward the tint's hue at
    /// the surface's <em>own luminance</em>, so a workspace holding several characters says whose pane is
    /// whose. <see cref="PaneTint.None"/> returns the surface itself, byte for byte — an untinted client
    /// is painted exactly as it was before this existed.
    /// <para>
    /// <b>The luminance is preserved deliberately, and that is the whole legibility argument.</b> A MU*
    /// server chooses its own text colours and this is the plane they are read against, so a tint that
    /// darkened or lightened the pane would change every contrast ratio the theme was designed around —
    /// on a palette the client does not control and cannot test. Matching the anchor to the surface's
    /// luminance <em>before</em> mixing (<see cref="AtLuma"/>) makes the mix luminance-neutral by
    /// construction rather than by a constant somebody tuned: luma is linear in the channels, so a blend
    /// of two colours of equal luma has that luma too, for any <see cref="TintStrength"/> and on any
    /// theme.
    /// </para>
    /// <para>
    /// Two consequences worth stating rather than discovering. <b>The tint carries no information a
    /// monochrome terminal can show</b>, and it must not: the cue that has to survive a lost hue is
    /// focus, and focus is luminance. A reader who cannot use the tint loses nothing they did not
    /// already have — the sidebar and the tab title still name the character in words. <b>And it is a
    /// truecolor cue</b>: at the surface's luminance the tints are a few points apart per channel, which
    /// a 256-colour terminal will quantise onto the same entry as the untinted plane. That degrades to
    /// <em>exactly the pane there would otherwise be</em>, which is why it is acceptable here and would
    /// not be for focus (see <c>WorkspacePaletteTests.FocusSurvivesA256ColourTerminal</c>).
    /// </para>
    /// </summary>
    internal static Rgb Tint(Theme theme, PaneTint tint)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var surface = Surface(theme);
        return Anchor(tint) is { } anchor
            ? Mix(surface, AtLuma(anchor, Luma(surface)), TintStrength)
            : surface;
    }

    /// <summary>
    /// How far a tinted plane travels from the theme's surface toward its hue. It is a pure chroma
    /// control — <see cref="AtLuma"/> has already taken the brightness question away — so this is the
    /// answer to "how coloured", and nothing else. Short of the whole way, because the anchor at the
    /// surface's luminance is as saturated as that luminance allows and a pane painted in it reads as a
    /// coloured panel rather than as a client with a quiet mark on it. The thing being identified is a
    /// character, not an alarm.
    /// </summary>
    private const double TintStrength = 0.7;

    /// <summary>
    /// The hue each named tint stands for, as a mid-luminance reference colour. These are never painted:
    /// <see cref="Tint"/> re-lights each one to the active theme's surface before it is used, so what is
    /// fixed here is the <em>hue</em> and the modest saturation, and the brightness is the theme's.
    /// <para>
    /// Six, spread around the wheel at roughly even spacing (blue → blue-green → green → amber →
    /// red-orange → violet), because the failure this feature has is two characters whose colours a
    /// reader has to compare rather than recognise. They are muted rather than primary for the same
    /// reason the anchors exist at all: re-lit to a dark theme's surface a primary would come out as
    /// nearly the maximum chroma that luminance can hold, and the pane would shout.
    /// </para>
    /// </summary>
    private static Rgb? Anchor(PaneTint tint) => tint switch
    {
        PaneTint.Slate => new Rgb(0x4a, 0x6f, 0xa5),
        PaneTint.Teal => new Rgb(0x2a, 0x8c, 0x84),
        PaneTint.Moss => new Rgb(0x5a, 0x8a, 0x42),
        PaneTint.Ochre => new Rgb(0xa8, 0x7c, 0x2e),
        PaneTint.Ember => new Rgb(0xa8, 0x54, 0x40),
        PaneTint.Plum => new Rgb(0x84, 0x54, 0xa0),
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
    internal static Rgb IdleBand(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Scale(theme.StatusBackground, IdleBandScale);
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
    internal static Rgb ArmedBand(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return Mix(Scale(IdleBand(theme), FocusScale), theme.Prompt, PromptTint);
    }

    /// <summary>
    /// Text on an idle band: the theme's foreground pulled most of the way down to that band. Dimmer
    /// than the armed bar's ink, so the pair still reads apart if a terminal flattens both backgrounds.
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
    internal static Rgb Backdrop(Theme theme) => Scale(Surface(theme), BackdropScale);

    /// <summary>The one-cell hairline a split draws between two panes, and beside the rail.</summary>
    internal static Rgb Rule(Theme theme) => Mix(Surface(theme), theme.Border, RuleLift);

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
