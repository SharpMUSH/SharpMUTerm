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
    /// <summary>The frame the audit renders. Shared with <see cref="Pairs"/>, which decodes a grid of
    /// exactly this size — a walker told the wrong dimensions drops cells off the edge silently.</summary>
    private const int Width = 140;

    private const int Height = 36;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    /// <summary>
    /// The views worth walking: one of every kind of surface the client paints — panes, the rail, the
    /// header, the command line, an overlay, a settings screen, and each of the boundary bars.
    /// </summary>
    private static readonly string[] Views =
    [
        "", "freeze", "away", "highlight", "scrollback", "links", "mxp-image", "mxp-figure", "connections", "tint", "tint-input",
        "characters", "compose", "moo-edit", "mssp", "web", "spawn", "split", "menu", "quit", "worlds", "triggers",
        "logging", "startup", "history", "prefix-panel", "keypad",

        // The selection band is the one plane this client invents rather than derives from a pane, and
        // its ink replaces the world's own colour on every selected cell — so it is the pair with the
        // most to lose from a theme it was not measured against.
        "selection", "input-selection", "rail-keys", "menu-fuzzy", "menu-recent",

        // The split-scrollback band (the notice ink on a pane) and the reflowed room, whose two blocks carry
        // dim labels and the world's own text.
        "scrollback-split", "reflow",

        // The first run: the welcome over an empty client, its connect form mid-entry (a field on FieldBg, the
        // caret, the toggles), and the key tour row on a pane.
        "welcome", "welcome-connect", "tour",

        // The four panels: who is here and the quick bar carry the accent on links, the vitals strip paints bars
        // and the warning in a server-driven colour, and the docked composer is an editor on its own plane.
        "panel-who", "panel-vitals", "panel-quickbar", "panel-composer",

        // The meters are the status row's only cells whose colour depends on what a server sent (a low
        // value wears the notice ink), and the inspector is a modal of its own.
        "meters", "gamedata", "map", "map-walking", "map-split",

        // The package surfaces are a modal of their own, with a warning ink on the review.
        "packages", "package-review", "package-browse", "send-menu",

        // The settings screens follow the theme (ScreenTheme), so each kind of screen is walked on all
        // three: a list-and-editor screen, an options card, a field mid-edit with its caret and its
        // candidate list, a password, and the deletion review that follows a screen out.
        "aliases", "timers", "textansi", "input", "set", "password", "deletions", "route", "triggers-edit",
        "keypad-edit", "worlds-edit", "mssp-none", "compose-literal",

        // The settings search marks its matches in the accent and the changed marks wear it too; the meter
        // editor's dropdown carries the received values as muted notes beside each path.
        "settings-search", "settings-meters", "settings-meters-pick", "settings-changed", "settings-undo",
        "settings-themes", "settings-themes-solarized", "settings-themes-used",

        // The prompt row is the only thing this client paints outside a pane that carries the
        // *game's* colours rather than its own, so the floor is applied against the idle input band
        // instead of the reading plane. A band nothing renders in a test is a band nobody checks.
        "prompt",
    ];

    public static IEnumerable<(string Theme, string View)> Cases() =>
        from theme in ThemeLibrary.Names
        from view in Views
        select (theme, view);

    /// <summary>
    /// The views walked again at 80×24, where the layout is a different one: F2, F4 and the settings search draw one
    /// column at a time, the rail folds, and the status row is short. The 80-column settings path kept Dark's colours on
    /// Light (UX audit wave 11, #1: wells at 1.34:1, <c>F11</c> at 1.05:1) while every 140-column frame here was green.
    /// </summary>
    private static readonly string[] NarrowViews =
    [
        "", "triggers", "triggers-edit", "keypad", "keypad-edit", "settings-search", "aliases", "timers", "worlds",
        "menu", "menu-fuzzy", "notices", "compose", "panel-who", "layout-roleplay", "layout-combat",
    ];

    public static IEnumerable<(string Theme, string View)> NarrowCases() =>
        from theme in ThemeLibrary.Names
        from view in NarrowViews
        select (theme, view);

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task NoPaintedTextIsBelowTheLegibilityFloor((string Theme, string View) test) =>
        await AssertFloor(test.Theme, test.View, Width, Height);

    [Test]
    [MethodDataSource(nameof(NarrowCases))]
    public async Task NoPaintedTextIsBelowTheLegibilityFloorAt80Columns((string Theme, string View) test) =>
        await AssertFloor(test.Theme, test.View, 80, 24);

    /// <summary>
    /// <b>Small text the reader must act on is held to 4.5:1, not 3.</b> The status row (a notice, and an error above
    /// all) and the selected row of ⌃P: an error notice was the raw warning red on Light's status plane, 1.39:1, and ⌃P's
    /// selected row was grey on deep teal at exactly 3.00:1 — which the 3:1 floor passes. Every theme, both sizes.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(BodyTextCases))]
    public async Task TheStatusRowAndThePalettesSelectedRowAreBodyText((string Theme, string View, int Width) test)
    {
        var height = test.Width == Width ? Height : 24;
        var frame = Render(test.Theme, test.View, test.Width, height);
        var cells = FrameGrid.Cells(frame, test.Width, height);

        // The cells to hold: the last row (the status row), and ⌃P's selected row — the cells on the band its ▸ marker
        // is drawn on, in the marker's row (a pane beside the palette is not the palette).
        var band = test.View.StartsWith("menu", StringComparison.Ordinal)
            ? cells.Where(c => c.Value.Glyph == '▸').Select(c => (c.Key.Row, c.Value.Background)).FirstOrDefault()
            : default;
        var failures = cells
            .Where(c => c.Key.Row == height - 1 || (c.Key.Row == band.Row && band.Background is { } bg && c.Value.Background == bg))
            .Select(c => c.Value)
            .Where(c => c.Glyph is not (' ' or '\n' or '\r') && !IsFill(c.Glyph) && c.Glyph is not ('▌' or '▐' or '●' or '○' or '◆' or '◇'))
            .Where(c => c.Foreground is { } && c.Background is { })
            .Select(c => (Fg: c.Foreground!.Value, Bg: c.Background!.Value))
            .Distinct()
            .Select(p => (p.Fg, p.Bg, Ratio: Contrast.Ratio(p.Fg, p.Bg)))
            .Where(t => t.Ratio < 4.5)
            .Select(t => $"{t.Ratio:0.00} {t.Fg.ToHex()} on {t.Bg.ToHex()}")
            .ToList();

        await Assert.That(failures).IsEmpty()
            .Because($"--theme \"{test.Theme}\" --view {(test.View.Length == 0 ? "(default)" : test.View)} at {test.Width} columns");
    }

    public static IEnumerable<(string Theme, string View, int Width)> BodyTextCases() =>
        from theme in ThemeLibrary.Names
        from view in new[] { "notices", "menu", "menu-fuzzy" }
        from width in new[] { 80, Width }
        select (theme, view, width);

    private static async Task AssertFloor(string theme, string view, int width, int height)
    {
        var frame = Render(theme, view, width, height);
        var pairs = Pairs(frame, width, height);
        var test = (Theme: theme, View: view);

        // A walker handed the wrong dimensions, or one that stopped recognising an escape, decodes
        // nothing — and an audit over nothing passes every assertion below it. That is the failure
        // FrameGrid's own remarks warn about ("quietly green on a frame it has misread"), and it is
        // precisely what a floor assertion cannot notice, so it is asserted separately.
        //
        // Counted in *cells* rather than in distinct pairs: a settings screen is painted from the theme's
        // ScreenTheme and is legitimately down to four colour pairs (F1's composer), while the
        // thinnest real frame here still paints 201 glyphs. A pair-count floor would be policing how
        // colourful a view is, which is not what this is for.
        var painted = pairs.Values.Sum();
        await Assert.That(painted).IsGreaterThanOrEqualTo(100)
            .Because($"the frame decoded to {painted} painted cells, too few to be a real one");

        var failures = pairs
            .Select(pair => (pair.Key.Fg, pair.Key.Bg, pair.Value, Ratio: Contrast.Ratio(pair.Key.Fg, pair.Key.Bg)))
            .Where(t => t.Ratio < Contrast.Floor)
            .OrderBy(t => t.Ratio)
            .Select(t => $"{t.Ratio:0.00} {t.Fg.ToHex()} on {t.Bg.ToHex()} ({t.Value} cells)")
            .ToList();

        await Assert.That(failures).IsEmpty()
            .Because($"--theme \"{test.Theme}\" --view {(test.View.Length == 0 ? "(default)" : test.View)}");
    }

    // There is no exemption for the framework's [dim] any more. It resolves to a fixed #808080 nothing can retheme, and
    // was exempted while it measured 2.52:1 on Solarized Dark's focused pane; the panes have since been deepened, the
    // settings screens repaint it off Dark (ScreenTheme), and the status row swaps it (StatusInk), so every [dim] cell
    // in every view here clears the floor on all three themes, and a new one that does not fails like any other ink.

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

    private static string Render(string themeName, string view, int width = Width, int height = Height)
    {
        Console.SetIn(TextReader.Null);
        var config = DemoScene.Build();
        config.ThemeName = themeName;
        config.Theme = ThemeLibrary.Get(themeName);

        var app = new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(width, height));
        return app.RenderSnapshot(view.Length == 0 ? null : view);
    }

    /// <summary>
    /// Every (foreground, background) pair the frame paints a glyph in, with a cell count — read off the
    /// <em>grid</em> (<see cref="FrameGrid.Cells"/>) rather than the escape stream, because the frame is
    /// cursor-addressed and a cell that was painted and then overwritten is not on screen. The two agree
    /// exactly on all 72 frames here; asking the grid is what makes that a fact rather than a hope.
    /// <para>
    /// Spaces are skipped — a space has no foreground to read — and so is everything <see cref="IsFill"/>
    /// names.
    /// </para>
    /// </summary>
    private static Dictionary<(Rgb Fg, Rgb Bg), int> Pairs(string frame, int width, int height)
    {
        var pairs = new Dictionary<(Rgb, Rgb), int>();

        foreach (var cell in FrameGrid.Cells(frame, width, height).Values)
        {
            if (cell.Glyph is ' ' or '\n' or '\r' || IsFill(cell.Glyph)
                || cell.Foreground is not { } ink || cell.Background is not { } plane)
            {
                continue;
            }

            pairs[(ink, plane)] = pairs.GetValueOrDefault((ink, plane)) + 1;
        }

        return pairs;
    }
}
