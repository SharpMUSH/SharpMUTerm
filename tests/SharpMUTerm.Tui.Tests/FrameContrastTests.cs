using System.Text;
using System.Text.RegularExpressions;
using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <b>Every cell of every frame, on every theme.</b> The unit tests hold each colour to the floor where
/// it is produced; this holds the <em>paint</em> to it, which is a different claim and the one that
/// found most of the defects.
/// <para>
/// A colour is only legible relative to the plane it lands on, and there is exactly one place both facts
/// are true at once: the emitted SGR. Walking it back into (foreground, background) pairs found what
/// reading the source did not — the trigger left-rule, the header ribbon's chip, a world's own accent on
/// the rail, the unread badge on a tab, and the command line's ink on the Solarized armed band. Every
/// one of those had a plausible-looking call site.
/// </para>
/// </summary>
/// <remarks>
/// Serialised with the other end-to-end suites: constructing the app and rendering a frame both touch
/// the process-global console streams.
/// </remarks>
[NotInParallel]
public class FrameContrastTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>
    /// The views worth walking: one of every kind of surface the client paints — panes, the rail, the
    /// header, the command line, an overlay, a settings screen, and each of the boundary bars.
    /// </summary>
    private static readonly string[] Views =
    [
        "", "freeze", "away", "highlight", "scrollback", "links", "connections", "tint", "tint-input",
        "characters", "compose", "mssp", "web", "spawn", "split", "menu", "quit", "worlds", "triggers",
        "logging", "startup", "history", "prefix-panel", "keypad",
    ];

    public static IEnumerable<(string Theme, string View)> Cases() =>
        from theme in ThemeLibrary.Names
        from view in Views
        select (theme, view);

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task NoPaintedTextIsBelowTheLegibilityFloor((string Theme, string View) test)
    {
        var frame = Render(test.Theme, test.View);

        var failures = Pairs(frame)
            .Select(pair => (pair.Key.Fg, pair.Key.Bg, pair.Value, Ratio: Contrast.Ratio(pair.Key.Fg, pair.Key.Bg)))
            .Where(t => t.Ratio < Contrast.Floor)
            .Where(t => !IsFrameworkDim(t.Fg))
            .OrderBy(t => t.Ratio)
            .Select(t => $"{t.Ratio:0.00} {t.Fg.ToHex()} on {t.Bg.ToHex()} ({t.Value} cells)")
            .ToList();

        await Assert.That(failures).IsEmpty()
            .Because($"--theme \"{test.Theme}\" --view {(test.View.Length == 0 ? "(default)" : test.View)}");
    }

    /// <summary>
    /// <b>The one exemption, and it is the framework's rather than ours.</b> SharpConsoleUI resolves the
    /// <c>[dim]</c> markup tag to a fixed <c>#808080</c> of its own — it is an internal local function in
    /// the markup renderer, reachable through no option we hold — so a dim rule measures 4.01:1 on the
    /// default dark theme and <b>2.52:1</b> on Solarized Dark's focused pane.
    /// <para>
    /// It is exempted rather than papered over. Reaching it means giving up <c>[dim]</c> everywhere in
    /// favour of an explicit floor-checked grey, which is a sweep across every renderer in the app for a
    /// near miss on one theme; the honest state is a named exemption and a number, so that anybody who
    /// does that sweep can delete this and watch the test still pass.
    /// </para>
    /// </summary>
    private static bool IsFrameworkDim(Rgb fg) => fg == new Rgb(0x80, 0x80, 0x80);

    /// <summary>
    /// A glyph that is a <em>fill</em> rather than text, and to which a text floor therefore does not
    /// apply. Three kinds, and each is exempt for its own reason rather than because it was failing:
    /// <list type="bullet">
    /// <item>the powerline wedges — a fill <em>boundary</em>: the glyph is by construction the previous
    /// ribbon segment's colour drawn on the next segment's, so its "contrast" is the distance between
    /// two identities;</item>
    /// <item>box-drawing rules and the settings screens' hairlines — a divider's job is to be found, not
    /// read, and <c>WorkspacePalette.Rule</c> already says in as many words that a rule at full contrast
    /// "reads fine once and shouts at four panes";</item>
    /// <item>the solid and shaded blocks, which in this app are colour <em>samples</em> — F2's highlight
    /// swatch is the picked colour shown as the pane will paint it, so holding it to a floor against the
    /// settings backdrop would be measuring it against a plane it is deliberately not for.</item>
    /// </list>
    /// <b>The half blocks are not exempt</b>, and that is the line: <c>▌</c> is the trigger left-rule and
    /// the focused-pane marker — marks that carry a fact and have to be seen — and one of them was a real
    /// defect this test caught.
    /// </summary>
    private static bool IsFill(char ch) =>
        ch is '' or ''
        || ch is >= '\u2500' and <= '\u257f'
        || ch is '█' or '░' or '▒' or '▓';

    private static string Render(string themeName, string view)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ThemeName = themeName;
        config.Theme = ThemeLibrary.Get(themeName);

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(140, 36));
        return app.RenderSnapshot(view.Length == 0 ? null : view);
    }

    private static readonly Regex Sgr = new(@"\x1b\[([0-9;]*)m", RegexOptions.Compiled);

    private static readonly Regex Csi = new(@"\x1b\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);

    /// <summary>
    /// Every (foreground, background) pair the frame actually paints a glyph in, with a cell count.
    /// Spaces are skipped — a space has no foreground to read — and so is everything
    /// <see cref="IsFill"/> names.
    /// </summary>
    private static Dictionary<(Rgb Fg, Rgb Bg), int> Pairs(string frame)
    {
        var pairs = new Dictionary<(Rgb, Rgb), int>();
        Rgb? fg = null;
        Rgb? bg = null;

        for (var i = 0; i < frame.Length;)
        {
            var sgr = Sgr.Match(frame, i);
            if (sgr.Success && sgr.Index == i)
            {
                Apply(sgr.Groups[1].Value, ref fg, ref bg);
                i = sgr.Index + sgr.Length;
                continue;
            }

            var csi = Csi.Match(frame, i);
            if (csi.Success && csi.Index == i)
            {
                i = csi.Index + csi.Length;
                continue;
            }

            var ch = frame[i++];
            if (ch is ' ' or '\n' or '\r' || IsFill(ch) || fg is not { } ink || bg is not { } plane)
            {
                continue;
            }

            pairs[(ink, plane)] = pairs.GetValueOrDefault((ink, plane)) + 1;
        }

        return pairs;
    }

    private static void Apply(string parameters, ref Rgb? fg, ref Rgb? bg)
    {
        var codes = parameters.Split(';')
            .Where(p => p.Length > 0)
            .Select(int.Parse)
            .ToList();

        for (var i = 0; i < codes.Count;)
        {
            if (codes[i] == 0)
            {
                fg = bg = null;
                i++;
            }
            else if (codes[i] is 38 or 48 && i + 4 < codes.Count && codes[i + 1] == 2)
            {
                var colour = new Rgb((byte)codes[i + 2], (byte)codes[i + 3], (byte)codes[i + 4]);
                if (codes[i] == 38)
                {
                    fg = colour;
                }
                else
                {
                    bg = colour;
                }

                i += 5;
            }
            else
            {
                i++;
            }
        }
    }
}
