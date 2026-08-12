namespace SharpMUTerm.Core.Text;

/// <summary>
/// The legibility floor: how far apart two colours are, and how to move a foreground the smallest
/// distance that makes it readable on the plane it is about to be painted on.
/// <para>
/// It exists because a colour is only legible <em>relative to something</em>, and this client had no
/// place that knew both halves at once. The freeze bar took its accent from ANSI 5 and painted it on
/// the pane surface: <c>#800080</c> on <c>#36363d</c> is <b>1.27:1</b>, which is very nearly the same
/// colour twice. The F2 highlight picker had the same shape one layer over — of its sixteen named
/// colours, only <c>grey</c> clears 3:1 on both the dark and the light theme, six fail on dark and
/// nine on light. A palette of fixed hexes cannot serve two themes, so the resolution has to happen
/// where the colour meets its plane rather than where it is chosen.
/// </para>
/// <para>
/// Pure and UI-agnostic on purpose: the same numbers decide a markup hex in the TUI, an ink on the
/// workspace palette, and anything a log renderer wants later.
/// </para>
/// </summary>
public static class Contrast
{
    /// <summary>
    /// The ratio a foreground must clear against its plane: <b>3.0:1</b>, WCAG AA for large text and
    /// user-interface components.
    /// <para>
    /// Deliberately not 4.5. A MU* server's own de-emphasis is spoken in exactly the colours a 4.5
    /// floor would erase — bright black for asides, <c>dim</c> for a status line nobody is meant to
    /// read twice — and a client that lifted those to body-text contrast would be flattening every
    /// deliberate act of de-emphasis a game makes. Three is the point where a colour stops being
    /// invisible, which is the complaint; four and a half is the point where it stops being quiet,
    /// which is not.
    /// </para>
    /// </summary>
    public const double Floor = 3.0;

    /// <summary>
    /// WCAG relative luminance — the sRGB channels linearised and weighted. This is the definition
    /// every contrast number in this codebase's design notes was produced with; it is <em>not</em> the
    /// BT.601 luma <see cref="Rgb"/>'s other consumers use (see <c>WorkspacePalette.Luma</c>), and the
    /// two are not interchangeable: that one is linear in the channels, which is a property the tint
    /// arithmetic leans on, and this one is not, which is why it can express a *ratio* a reader
    /// perceives.
    /// </summary>
    public static double RelativeLuminance(Rgb rgb) =>
        (0.2126 * Linearise(rgb.R)) + (0.7152 * Linearise(rgb.G)) + (0.0722 * Linearise(rgb.B));

    private static double Linearise(byte channel)
    {
        var v = channel / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// How far apart two colours read, from 1:1 (identical) to 21:1 (black on white). Symmetric —
    /// it says nothing about which one is the text.
    /// </summary>
    public static double Ratio(Rgb a, Rgb b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// <paramref name="foreground"/> moved the smallest distance that clears <paramref name="floor"/>
    /// against <paramref name="plane"/>, or returned unchanged when it already does.
    /// <para>
    /// <b>The direction is the plane's, not the colour's.</b> A dark plane always lifts and a light
    /// plane always darkens, which is what lets one function serve every theme: a pane plane is never
    /// mid-grey, so there is no theme in which "away from the background" is ambiguous. The pivot is
    /// the plane's own luminance against mid-scale.
    /// </para>
    /// <para>
    /// <b>Hue survives while there is headroom, and then it desaturates.</b> Blending toward white (or
    /// black) is monotone in luminance and can reach any target without clipping a channel, which
    /// scaling cannot — the same reasoning <c>WorkspacePalette.AtLuma</c> is written on. It has to give
    /// hue up eventually: pure <c>#0000ff</c> has a relative luminance of 0.0722 and so tops out at
    /// 1.88:1 on a dark pane <em>at full blue</em>, so a rule that held hue absolutely would leave the
    /// commonest unreadable colour in MU* output unreadable. What is kept is the channel order, so a
    /// lifted colour is still recognisably the one the server sent.
    /// </para>
    /// <para>
    /// <b>It is a projection and not a bijection.</b> Two foregrounds differing only below the floor
    /// come out closer together than they went in. That is the price of the floor, and the alternative
    /// is the present behaviour, in which they are equally invisible.
    /// </para>
    /// <para>
    /// <b>Best effort.</b> A floor that is arithmetically unreachable (21:1 against a mid-grey) yields
    /// the furthest colour in the chosen direction rather than an exception: the caller is a render
    /// path fed from the telnet read loop, and there is nothing useful it could do with a throw.
    /// </para>
    /// </summary>
    public static Rgb Legible(Rgb foreground, Rgb plane, double floor = Floor)
    {
        if (Ratio(foreground, plane) >= floor)
        {
            return foreground;
        }

        // Mid-scale in *luminance*, not in bytes: 0.18 is the relative luminance of the sRGB mid-grey
        // a reader would call "half way", and comparing bytes instead would call #808080 (0.216) dark.
        var target = RelativeLuminance(plane) < 0.18
            ? new Rgb(0xff, 0xff, 0xff)
            : new Rgb(0x00, 0x00, 0x00);

        // Binary search on the blend rather than a closed form: the luminance curve is piecewise and
        // the blend is in gamma space, so there is no inverse worth writing. Monotone in t, so twelve
        // steps land within a quarter of a byte — finer than the channel the answer is rounded to.
        var lo = 0.0;
        var hi = 1.0;
        for (var i = 0; i < 12; i++)
        {
            var mid = (lo + hi) / 2;
            if (Ratio(Mix(foreground, target, mid), plane) >= floor)
            {
                hi = mid;
            }
            else
            {
                lo = mid;
            }
        }

        var best = Mix(foreground, target, hi);

        // The search converges on the *threshold*; rounding to bytes can land a hair under it, and a
        // caller asserting the floor would then see it missed by a thousandth. Step to the end rather
        // than iterate: the remaining distance is at most a quarter of a byte, so this is the last
        // blend or the extreme itself.
        return Ratio(best, plane) >= floor ? best : target;
    }

    /// <summary>Linear blend, <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    private static Rgb Mix(Rgb from, Rgb to, double t) => new(
        Channel(from.R + ((to.R - from.R) * t)),
        Channel(from.G + ((to.G - from.G) * t)),
        Channel(from.B + ((to.B - from.B) * t)));

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
