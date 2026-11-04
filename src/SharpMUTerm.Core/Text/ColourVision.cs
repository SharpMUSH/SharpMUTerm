namespace SharpMUTerm.Core.Text;

/// <summary>A colour-vision deficiency <see cref="ColourVision"/> can simulate.</summary>
public enum ColourDeficiency
{
    /// <summary>Typical trichromatic vision: no simulation.</summary>
    None = 0,

    /// <summary>No working long-wavelength (red) cones.</summary>
    Protanopia,

    /// <summary>No working medium-wavelength (green) cones — the commonest of the three.</summary>
    Deuteranopia,

    /// <summary>No working short-wavelength (blue) cones.</summary>
    Tritanopia,
}

/// <summary>
/// How far apart two colours look, to typical vision and under a simulated colour-vision deficiency.
/// <para>
/// <b>Simulation:</b> Machado, Oliveira &amp; Fernandes, "A Physiologically-based Model for Simulation of
/// Color Vision Deficiency", IEEE TVCG 15(6), 2009 — the severity-1.0 (dichromat) matrices from the
/// paper's supplementary table, applied to <em>linear</em> sRGB and clipped to the unit cube. Chosen over
/// Brettel/Viénot because it is one 3×3 matrix per deficiency, covers tritanopia with the same model as
/// the other two, and is what browser devtools and most accessibility checkers ship.
/// </para>
/// <para>
/// <b>Distance:</b> CIEDE2000 (Sharma, Wu &amp; Dalal 2005's formulation) over CIELAB with a D65 white.
/// It is computed from the simulated colour <em>before</em> rounding to bytes: the pane planes this is used
/// for sit near L* 7, where one byte is a visible fraction of the whole distance being measured.
/// </para>
/// </summary>
public static class ColourVision
{
    private static readonly double[,] Protan =
    {
        { 0.152286, 1.052583, -0.204868 },
        { 0.114503, 0.786281, 0.099216 },
        { -0.003882, -0.048116, 1.051998 },
    };

    private static readonly double[,] Deutan =
    {
        { 0.367322, 0.860646, -0.227968 },
        { 0.280085, 0.672501, 0.047413 },
        { -0.011820, 0.042940, 0.968881 },
    };

    private static readonly double[,] Tritan =
    {
        { 1.255528, -0.076749, -0.178779 },
        { -0.078411, 0.930809, 0.147602 },
        { 0.004733, 0.691367, 0.303900 },
    };

    /// <summary>The CIEDE2000 distance between two colours as seen with <paramref name="deficiency"/>.</summary>
    public static double Distance(Rgb a, Rgb b, ColourDeficiency deficiency = ColourDeficiency.None) =>
        DeltaE2000(Lab(Linear(a, deficiency)), Lab(Linear(b, deficiency)));

    /// <summary>A colour as it appears with <paramref name="deficiency"/>, rounded to bytes for display.</summary>
    public static Rgb Simulate(Rgb rgb, ColourDeficiency deficiency)
    {
        var (r, g, b) = Linear(rgb, deficiency);
        return new Rgb(Encode(r), Encode(g), Encode(b));
    }

    /// <summary>Linear sRGB, put through the deficiency's matrix.</summary>
    private static (double R, double G, double B) Linear(Rgb rgb, ColourDeficiency deficiency)
    {
        var r = Decode(rgb.R);
        var g = Decode(rgb.G);
        var b = Decode(rgb.B);
        var m = deficiency switch
        {
            ColourDeficiency.Protanopia => Protan,
            ColourDeficiency.Deuteranopia => Deutan,
            ColourDeficiency.Tritanopia => Tritan,
            _ => null,
        };

        if (m is null)
        {
            return (r, g, b);
        }

        return (
            Math.Clamp((m[0, 0] * r) + (m[0, 1] * g) + (m[0, 2] * b), 0, 1),
            Math.Clamp((m[1, 0] * r) + (m[1, 1] * g) + (m[1, 2] * b), 0, 1),
            Math.Clamp((m[2, 0] * r) + (m[2, 1] * g) + (m[2, 2] * b), 0, 1));
    }

    private static double Decode(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static byte Encode(double linear)
    {
        var c = linear <= 0.0031308 ? 12.92 * linear : (1.055 * Math.Pow(linear, 1 / 2.4)) - 0.055;
        return (byte)Math.Clamp(Math.Round(c * 255), 0, 255);
    }

    /// <summary>CIELAB (D65) of a linear sRGB triple.</summary>
    private static (double L, double A, double B) Lab((double R, double G, double B) rgb)
    {
        var x = (0.4124564 * rgb.R) + (0.3575761 * rgb.G) + (0.1804375 * rgb.B);
        var y = (0.2126729 * rgb.R) + (0.7151522 * rgb.G) + (0.0721750 * rgb.B);
        var z = (0.0193339 * rgb.R) + (0.1191920 * rgb.G) + (0.9503041 * rgb.B);

        var fx = F(x / 0.95047);
        var fy = F(y);
        var fz = F(z / 1.08883);
        return ((116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz));

        static double F(double t) =>
            t > 216.0 / 24389 ? Math.Cbrt(t) : (t * 24389 / 27 + 16) / 116;
    }

    private static double DeltaE2000((double L, double A, double B) p, (double L, double A, double B) q)
    {
        const double Pow25To7 = 6103515625.0; // 25^7

        var c1 = Math.Sqrt((p.A * p.A) + (p.B * p.B));
        var c2 = Math.Sqrt((q.A * q.A) + (q.B * q.B));
        var cBar7 = Math.Pow((c1 + c2) / 2, 7);
        var g = 0.5 * (1 - Math.Sqrt(cBar7 / (cBar7 + Pow25To7)));

        var a1 = (1 + g) * p.A;
        var a2 = (1 + g) * q.A;
        var c1p = Math.Sqrt((a1 * a1) + (p.B * p.B));
        var c2p = Math.Sqrt((a2 * a2) + (q.B * q.B));
        var h1p = Hue(p.B, a1);
        var h2p = Hue(q.B, a2);

        var dL = q.L - p.L;
        var dC = c2p - c1p;
        double dh;
        if (c1p * c2p == 0)
        {
            dh = 0;
        }
        else
        {
            dh = h2p - h1p;
            if (dh > 180)
            {
                dh -= 360;
            }
            else if (dh < -180)
            {
                dh += 360;
            }
        }

        var dH = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(Radians(dh / 2));

        var lBar = (p.L + q.L) / 2;
        var cBarP = (c1p + c2p) / 2;
        double hBar;
        if (c1p * c2p == 0)
        {
            hBar = h1p + h2p;
        }
        else if (Math.Abs(h1p - h2p) <= 180)
        {
            hBar = (h1p + h2p) / 2;
        }
        else
        {
            hBar = h1p + h2p < 360 ? (h1p + h2p + 360) / 2 : (h1p + h2p - 360) / 2;
        }

        var t = 1
            - (0.17 * Math.Cos(Radians(hBar - 30)))
            + (0.24 * Math.Cos(Radians(2 * hBar)))
            + (0.32 * Math.Cos(Radians((3 * hBar) + 6)))
            - (0.20 * Math.Cos(Radians((4 * hBar) - 63)));
        var dTheta = 30 * Math.Exp(-Math.Pow((hBar - 275) / 25, 2));
        var cBarP7 = Math.Pow(cBarP, 7);
        var rC = 2 * Math.Sqrt(cBarP7 / (cBarP7 + Pow25To7));
        var sL = 1 + (0.015 * Math.Pow(lBar - 50, 2) / Math.Sqrt(20 + Math.Pow(lBar - 50, 2)));
        var sC = 1 + (0.045 * cBarP);
        var sH = 1 + (0.015 * cBarP * t);
        var rT = -Math.Sin(Radians(2 * dTheta)) * rC;

        var l = dL / sL;
        var c = dC / sC;
        var h = dH / sH;
        return Math.Sqrt((l * l) + (c * c) + (h * h) + (rT * c * h));

        static double Hue(double b, double a)
        {
            var degrees = Math.Atan2(b, a) * 180 / Math.PI;
            return degrees < 0 ? degrees + 360 : degrees;
        }

        static double Radians(double degrees) => degrees * Math.PI / 180;
    }
}
