using System.Globalization;
using System.Text;
using SharpConsoleUI;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;

namespace SharpMUTerm.Tui;

/// <summary>
/// The settings screens' palette resolved for one theme. <see cref="ScreenPalette"/> names the roles —
/// panel, header, label, value, well, accent — and its constants are the Dark theme's values for them;
/// this is the same set of roles worked out for whatever theme is active, so a Light client gets light
/// screens rather than a dark island in the middle of a light workspace (defect D6).
/// <para>
/// <b>The renderers do not know about it, deliberately.</b> They write markup in the role colours
/// (<c>[{Label}]…</c>, the <see cref="ScreenPalette"/> constants), and the view resolves that markup
/// through an instance of this class at the one place it becomes a control (<see cref="Paint(string,
/// string)"/>, <see cref="Lines"/>, <see cref="Colour"/>). Threading a palette parameter through every
/// renderer would have touched every function on every screen and every test that calls one, for a
/// value only the last step needs; and a mutable <see cref="ScreenPalette"/> would be global state the
/// parallel test suites race on. An instance is immutable, made once per app from the app's theme, and
/// handed to the views.
/// </para>
/// <para>
/// <b>The Dark theme resolves to the constants byte for byte</b>, so every Dark frame is exactly what
/// it was. Other themes keep the same <em>relationships</em>: a dark theme takes its background's hue
/// on the planes; a light one mirrors each role's lightness (CIE L*, so a step that read as one step
/// still does) and keeps its hue. Then every ink is held to <see cref="Contrast.Floor"/> on each plane
/// it is drawn on, the floor the workspace's chrome is held to.
/// </para>
/// </summary>
internal sealed class ScreenTheme
{
    /// <summary>The palette the constants describe: the Dark theme's.</summary>
    internal static readonly ScreenTheme Dark = new(Canonical());

    private readonly IReadOnlyDictionary<string, string> _roles;

    private ScreenTheme(IReadOnlyDictionary<string, string> roles)
    {
        _roles = roles;
        IsIdentity = roles.All(r => string.Equals(r.Key, r.Value, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether every role resolves to its own constant (the Dark theme).</summary>
    internal bool IsIdentity { get; }

    internal string Accent => Role(ScreenPalette.Accent);

    internal string PanelBg => Role(ScreenPalette.PanelBg);

    internal string PanelFg => Role(ScreenPalette.PanelFg);

    internal string HeaderBg => Role(ScreenPalette.HeaderBg);

    internal string FooterBg => Role(ScreenPalette.FooterBg);

    internal string EditBg => Role(ScreenPalette.EditBg);

    internal string Label => Role(ScreenPalette.Label);

    internal string Value => Role(ScreenPalette.Value);

    internal string Muted => Role(ScreenPalette.Muted);

    internal string Rule => Role(ScreenPalette.Rule);

    internal string CursorBg => Role(ScreenPalette.CursorBg);

    internal string Ink => Role(ScreenPalette.Ink);

    internal string MenuBg => Role(ScreenPalette.MenuBg);

    internal string MenuSelectedBg => Role(ScreenPalette.MenuSelectedBg);

    internal string MenuShadow => Role(ScreenPalette.MenuShadow);

    internal string FieldBg => Role(ScreenPalette.FieldBg);

    internal string Warn => Role(ScreenPalette.Warn);

    /// <summary>The planes text is drawn on — what an ink is measured against.</summary>
    private static readonly string[] TextPlanes =
    [
        ScreenPalette.PanelBg, ScreenPalette.HeaderBg, ScreenPalette.FooterBg, ScreenPalette.EditBg,
        ScreenPalette.CursorBg, ScreenPalette.MenuBg, ScreenPalette.MenuSelectedBg, ScreenPalette.FieldBg,
    ];

    /// <summary>
    /// Every plane role. <see cref="ScreenPalette.MenuShadow"/> is not among them: it is placed relative
    /// to the others rather than mirrored, because a shadow is darker than what casts it on any theme.
    /// </summary>
    private static readonly string[] Planes = [.. TextPlanes, ScreenPalette.Rule];

    /// <summary>The text roles, mirrored with the planes on a light theme and then held to the floor.</summary>
    private static readonly string[] Inks =
        [ScreenPalette.Label, ScreenPalette.Muted, ScreenPalette.PanelFg, ScreenPalette.Value];

    /// <summary>The reference backgrounds the hue shift is measured from, one per side of mid-grey.</summary>
    private static readonly Rgb DarkReference = new(0x1e, 0x1e, 0x1e);

    private static readonly Rgb LightReference = new(0xfa, 0xfa, 0xfa);

    /// <summary>The palette for <paramref name="theme"/>.</summary>
    internal static ScreenTheme For(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var light = Contrast.RelativeLuminance(theme.Background) >= LightPlaneLuminance;
        var shift = Delta(theme.Background, light ? LightReference : DarkReference);
        var roles = new Dictionary<string, Rgb>(StringComparer.OrdinalIgnoreCase);

        foreach (var plane in Planes)
        {
            var rgb = Parse(plane);
            roles[plane] = Add(light ? Mirror(rgb) : rgb, shift);
        }

        // Darker than every plane on either kind of theme: the list's far edge reads as "under".
        var lowest = Planes.Select(p => Lightness(roles[p])).Min();
        roles[ScreenPalette.MenuShadow] = light
            ? Add(AtLightness(Parse(ScreenPalette.MenuShadow), lowest - ShadowStep), shift)
            : Add(Parse(ScreenPalette.MenuShadow), shift);

        foreach (var ink in Inks)
        {
            var rgb = Parse(ink);
            roles[ink] = Legible(light ? Mirror(rgb) : rgb, roles, PlanesUnder(ink));
        }

        // The accent and the warning keep their hue and are moved only as far as the floor asks; on a
        // light theme that is a long way, which is the point.
        roles[ScreenPalette.Accent] = Legible(Parse(ScreenPalette.Accent), roles);
        roles[ScreenPalette.Warn] = Legible(Parse(ScreenPalette.Warn), roles);

        // Text printed on the accent: near-black on a bright accent, the panel's own tone on a dark one.
        var accent = roles[ScreenPalette.Accent];
        var ink0 = Contrast.RelativeLuminance(accent) < LightPlaneLuminance ? roles[ScreenPalette.PanelBg] : Parse(ScreenPalette.Ink);
        roles[ScreenPalette.Ink] = Contrast.Legible(ink0, accent);

        return new ScreenTheme(roles.ToDictionary(r => r.Key, r => r.Value.ToHex(), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>What a role constant resolves to here. A colour that is not a role is returned as given.</summary>
    internal string Role(string constant) => _roles.TryGetValue(constant, out var hex) ? hex : constant;

    /// <summary>A role constant as a control colour.</summary>
    internal Color Colour(string constant) => new(Role(constant));

    /// <summary>
    /// Colours a bordered modal's frame, title and unstyled text. The framework's defaults (a cyan title
    /// and border, <c>#eeeeee</c> text) were chosen for a dark backdrop and fall to 1.4:1 on a light one,
    /// so a theme that is not the constants' own draws them in its label and value inks; the Dark theme
    /// keeps the framework's, unchanged.
    /// </summary>
    internal void Frame(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (IsIdentity)
        {
            return;
        }

        window.ForegroundColor = Colour(ScreenPalette.Value);
        window.ActiveTitleForegroundColor = Colour(ScreenPalette.Value);
        window.InactiveTitleForegroundColor = Colour(ScreenPalette.Label);
        window.ActiveBorderForegroundColor = Colour(ScreenPalette.Label);
        window.InactiveBorderForegroundColor = Colour(ScreenPalette.Label);
    }

    /// <summary>Every line of a renderer's markup resolved for this theme, on <paramref name="plane"/>.</summary>
    internal List<string> Lines(IEnumerable<string> lines, string plane = ScreenPalette.PanelBg)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Select(line => Paint(line, plane)).ToList();
    }

    /// <summary>
    /// One line of a renderer's markup resolved for this theme. Role colours are swapped for this
    /// theme's; any other foreground — a world's own accent, a highlight a rule names — is held to the
    /// floor against the plane it lands on, which is the tag's own background, else the one it is nested
    /// in, else <paramref name="plane"/> (the control's). Escaped brackets and link targets are text and
    /// are never read as colours. A run of blocks (<c>█░▒▓</c>) is a colour sample and keeps its colour,
    /// even one that shares a role's hex, because what it shows is that colour.
    /// </summary>
    internal string Paint(string markup, string plane = ScreenPalette.PanelBg)
    {
        ArgumentNullException.ThrowIfNull(markup);
        if (markup.IndexOf('[') < 0)
        {
            return markup;
        }

        var output = new StringBuilder(markup.Length + 16);
        var planes = new Stack<string>();
        planes.Push(Role(plane));

        var i = 0;
        while (i < markup.Length)
        {
            var ch = markup[i];
            if ((ch == '[' || ch == ']') && i + 1 < markup.Length && markup[i + 1] == ch)
            {
                output.Append(ch).Append(ch);
                i += 2;
                continue;
            }

            if (ch != '[')
            {
                output.Append(ch);
                i++;
                continue;
            }

            var close = markup.IndexOf(']', i + 1);
            if (close < 0)
            {
                output.Append(markup, i, markup.Length - i);
                break;
            }

            var tag = markup.Substring(i + 1, close - i - 1);
            if (tag == "/")
            {
                if (planes.Count > 1)
                {
                    planes.Pop();
                }

                output.Append("[/]");
            }
            else
            {
                output.Append('[').Append(Tag(tag, planes, Sample(markup, close + 1))).Append(']');
            }

            i = close + 1;
        }

        return output.ToString();
    }

    /// <summary>One opening tag, resolved; pushes the plane its content is drawn on.</summary>
    private string Tag(string tag, Stack<string> planes, bool sample)
    {
        if (tag.StartsWith("link=", StringComparison.OrdinalIgnoreCase))
        {
            planes.Push(planes.Peek());
            return tag;
        }

        var words = tag.Split(' ');
        string? background = null;
        var foreground = -1;
        for (var w = 0; w < words.Length; w++)
        {
            if (words[w] == "on" && w + 1 < words.Length && IsHex(words[w + 1]))
            {
                words[w + 1] = Role(words[w + 1]);
                background = words[w + 1];
                w++;
            }
            else if (IsHex(words[w]) && foreground < 0)
            {
                foreground = w;
            }
        }

        var plane = background ?? planes.Peek();
        if (foreground >= 0)
        {
            var given = words[foreground];
            if (sample)
            {
                // A colour sample is the colour it samples — F2's swatch of a highlight that happens to be
                // the accent's hex must not turn into this theme's accent. The one role a sample is drawn
                // in is the hairline (an unset swatch's hatching), and that follows the theme.
                words[foreground] = string.Equals(given, ScreenPalette.Rule, StringComparison.OrdinalIgnoreCase)
                    ? Role(given)
                    : given;
            }
            else if (_roles.ContainsKey(given))
            {
                words[foreground] = Role(given);
            }
            else if (!IsIdentity)
            {
                // Not a role: a colour the screen was handed. Hold it to the floor where it lands.
                words[foreground] = Contrast.Legible(Parse(given), Parse(plane)).ToHex();
            }
        }

        planes.Push(plane);
        return string.Join(' ', words);
    }

    /// <summary>Whether the text a tag opens on is a colour sample — blocks only, up to the next tag.</summary>
    private static bool Sample(string markup, int start)
    {
        var any = false;
        for (var i = start; i < markup.Length && markup[i] != '['; i++)
        {
            if (markup[i] is not ('█' or '░' or '▒' or '▓'))
            {
                return false;
            }

            any = true;
        }

        return any;
    }

    private static bool IsHex(string word) =>
        word.Length == 7 && word[0] == '#' && word.Skip(1).All(Uri.IsHexDigit);

    private static Rgb Parse(string hex) => new(
        byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static IReadOnlyDictionary<string, string> Canonical() =>
        new[]
        {
            ScreenPalette.Accent, ScreenPalette.PanelBg, ScreenPalette.PanelFg, ScreenPalette.HeaderBg,
            ScreenPalette.FooterBg, ScreenPalette.EditBg, ScreenPalette.Label, ScreenPalette.Value,
            ScreenPalette.Muted, ScreenPalette.Rule, ScreenPalette.CursorBg, ScreenPalette.Ink,
            ScreenPalette.MenuBg, ScreenPalette.MenuSelectedBg, ScreenPalette.MenuShadow, ScreenPalette.FieldBg,
            ScreenPalette.Warn,
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(h => h, h => h, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The planes an ink is drawn on. Every plane text is drawn on, except that a label never sits on the
    /// menu's selected candidate — the candidate the buffer names is drawn in the value ink, and the rest of
    /// the list in labels on the menu's own plane — so a label is not dragged toward a plane it never meets.
    /// </summary>
    internal static IReadOnlyList<string> PlanesUnder(string ink) =>
        string.Equals(ink, ScreenPalette.Label, StringComparison.OrdinalIgnoreCase)
            ? TextPlanes.Where(p => p != ScreenPalette.MenuSelectedBg).ToArray()
            : TextPlanes;

    /// <summary>An ink moved, if it must be, until it clears the floor on every plane it is drawn on.</summary>
    private static Rgb Legible(Rgb ink, IReadOnlyDictionary<string, Rgb> roles, IReadOnlyList<string>? planes = null)
    {
        // Every plane is on one side of mid-grey, so the plane nearest the ink is the hardest and clearing
        // it clears the rest; two passes absorb the rounding of the first.
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var plane in planes ?? TextPlanes)
            {
                ink = Contrast.Legible(ink, roles[plane]);
            }
        }

        return ink;
    }

    /// <summary>How far below the darkest plane a light theme's menu shadow sits, in L*.</summary>
    private const double ShadowStep = 16;

    private const double LightPlaneLuminance = 0.18;

    /// <summary>
    /// A dark role on a light theme: the same colour at the mirrored lightness (100 − L*), keeping the
    /// tint it carried (its distance from the grey of its own luminance).
    /// </summary>
    private static Rgb Mirror(Rgb rgb) => AtLightness(rgb, 100 - Lightness(rgb));

    /// <summary>The colour's tint laid over the grey at <paramref name="lightness"/> (L*).</summary>
    private static Rgb AtLightness(Rgb rgb, double lightness)
    {
        var own = Grey(Contrast.RelativeLuminance(rgb));
        var grey = Grey(FromLightness(Math.Clamp(lightness, 0, 100)));
        return new Rgb(
            Channel(grey + (rgb.R - own)),
            Channel(grey + (rgb.G - own)),
            Channel(grey + (rgb.B - own)));
    }

    /// <summary>CIE L* of a colour's relative luminance.</summary>
    private static double Lightness(Rgb rgb)
    {
        var y = Contrast.RelativeLuminance(rgb);
        return y > 216.0 / 24389 ? (116 * Math.Cbrt(y)) - 16 : y * 24389 / 27;
    }

    private static double FromLightness(double lightness) =>
        lightness > 8 ? Math.Pow((lightness + 16) / 116, 3) : lightness * 27 / 24389;

    /// <summary>The sRGB byte of the neutral grey with relative luminance <paramref name="y"/>.</summary>
    private static double Grey(double y)
    {
        var v = y <= 0.0031308 ? 12.92 * y : (1.055 * Math.Pow(y, 1 / 2.4)) - 0.055;
        return v * 255;
    }

    private static (int R, int G, int B) Delta(Rgb a, Rgb b) => (a.R - b.R, a.G - b.G, a.B - b.B);

    private static Rgb Add(Rgb rgb, (int R, int G, int B) d) =>
        new(Channel(rgb.R + d.R), Channel(rgb.G + d.G), Channel(rgb.B + d.B));

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
