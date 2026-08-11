using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// The workspace's three planes. What is pinned here is the <em>relationship</em> between them —
/// which is the whole design: an output pane has to read as a raised surface on every theme, not
/// only on the one the hexes were picked against.
/// </summary>
public class WorkspacePaletteTests
{
    private static double Luma(SharpMUTerm.Core.Text.Rgb rgb) =>
        ((rgb.R * 299) + (rgb.G * 587) + (rgb.B * 114)) / 1000.0;

    /// <summary>A step the eye can find on a terminal's worth of flat colour.</summary>
    private const double Visible = 8;

    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task EveryThemeGetsARaisedSurfaceOverARecessedBackdrop(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var surface = Luma(WorkspacePalette.Surface(theme));
        var backdrop = Luma(WorkspacePalette.Backdrop(theme));

        // The pane is lighter than what surrounds it, or an empty pane goes on reading as a hole.
        await Assert.That(surface - backdrop).IsGreaterThan(Visible);
    }

    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task TheHairlineIsFoundAgainstBothPlanes(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var rule = Luma(WorkspacePalette.Rule(theme));

        // A divider has the backdrop beside it down the side of the rail and the surface beside it
        // between two panes; it has to be visible against each. The direction is the theme's business
        // (lighter than both on a dark theme, between them on a light one) — the distance is not.
        await Assert.That(Math.Abs(rule - Luma(WorkspacePalette.Surface(theme)))).IsGreaterThan(Visible);
        await Assert.That(Math.Abs(rule - Luma(WorkspacePalette.Backdrop(theme)))).IsGreaterThan(Visible);
    }

    [Test]
    public async Task TheSurfaceCarriesTheThemesOwnChromeTint()
    {
        var theme = ThemeLibrary.Get("Dark"); // background #1e1e1e, status background #2d2d3f

        var surface = WorkspacePalette.Surface(theme);

        // A quarter of the way from the text background toward the chrome the header and input bands
        // are already painted in — so the output plane belongs to this application, not to the terminal.
        await Assert.That(surface.R).IsEqualTo((byte)0x22);
        await Assert.That(surface.G).IsEqualTo((byte)0x22);
        await Assert.That(surface.B).IsEqualTo((byte)0x26);
        await Assert.That(surface.B).IsGreaterThan(surface.R); // cooler than the plain background, which is neutral
    }

    [Test]
    public async Task TheBackdropSitsTheSameStepBelowTheSurfaceThatTheSettingsScreensUse()
    {
        // ScreenPalette's own pair: PanelBg is the backdrop an EditBg card floats on. The workspace
        // reuses that step, which is why F5 and the pane behind it read as one application.
        var settingsStep = (Ratio(0x17, 0x1d) + Ratio(0x1b, 0x23) + Ratio(0x24, 0x33)) / 3.0;

        var theme = ThemeLibrary.Get("Dark");
        var surface = WorkspacePalette.Surface(theme);
        var backdrop = WorkspacePalette.Backdrop(theme);
        var workspaceStep = (Ratio(backdrop.R, surface.R) + Ratio(backdrop.G, surface.G) + Ratio(backdrop.B, surface.B)) / 3.0;

        await Assert.That(Math.Abs(workspaceStep - settingsStep)).IsLessThan(0.02);

        static double Ratio(int backdrop, int surface) => backdrop / (double)surface;
    }

    [Test]
    public async Task ADifferentThemeMovesAllThreeTones()
    {
        var dark = ThemeLibrary.Get("Dark");
        var solarized = ThemeLibrary.Get("Solarized Dark");

        await Assert.That(WorkspacePalette.Surface(solarized)).IsNotEqualTo(WorkspacePalette.Surface(dark));
        await Assert.That(WorkspacePalette.Backdrop(solarized)).IsNotEqualTo(WorkspacePalette.Backdrop(dark));
        await Assert.That(WorkspacePalette.Rule(solarized)).IsNotEqualTo(WorkspacePalette.Rule(dark));
    }

    [Test]
    public async Task TheRuleStopsShortOfTheThemesBorderColour()
    {
        var theme = ThemeLibrary.Get("Dark");

        // A hairline that landed on Theme.Border reads fine once and shouts at four panes.
        await Assert.That(Luma(WorkspacePalette.Rule(theme))).IsLessThan(Luma(theme.Border));
    }

    [Test]
    public async Task ThePlanesAreThemesNotConstants()
    {
        // A hand-written theme's background is what the panes are built from, not a hex in this file.
        var custom = new Theme
        {
            Background = new SharpMUTerm.Core.Text.Rgb(0x10, 0x00, 0x20),
            StatusBackground = new SharpMUTerm.Core.Text.Rgb(0x30, 0x00, 0x60),
        };

        var surface = WorkspacePalette.Surface(custom);
        await Assert.That(surface.B).IsGreaterThan(surface.R);
        await Assert.That(surface.G).IsEqualTo((byte)0);
    }

    // --- The focus planes ------------------------------------------------------------------------

    /// <summary>
    /// How far apart two tones have to be before this suite will call them distinguishable. Well above
    /// <see cref="Visible"/>, because the complaint these tests exist for was about a pair that <em>did</em>
    /// technically differ: the two input bands were thirteen points per channel apart, which is a luma step
    /// of about fifteen, and the report was that the difference is "almost invisible". So the bar for
    /// <em>focus</em> is deliberately set above the step that was reported as not good enough.
    /// </summary>
    private const double Obvious = 24;

    /// <summary>
    /// The measured distance between the two input bands, on every shipped theme. This is the assertion
    /// that speaks the language of the bug report — not "a colour was set" but "the two colours are this
    /// far apart" — and the number it is held to is above the one that was reported as too close.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task TheArmedBandIsObviouslyBrighterThanTheIdleOne(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var armed = Luma(WorkspacePalette.ArmedBand(theme));
        var idle = Luma(WorkspacePalette.IdleBand(theme));

        await Assert.That(armed - idle).IsGreaterThan(Obvious);
    }

    /// <summary>
    /// What the old pair actually measured, kept as the floor the new one has to beat. Without it
    /// "obvious" is a number in a constant with nothing behind it; with it, the suite records the step
    /// that was rejected and holds the replacement to more than double it.
    /// </summary>
    [Test]
    public async Task TheNewStepIsMoreThanTwiceTheOneThatWasReportedAsInvisible()
    {
        var theme = ThemeLibrary.Get("Dark");
        var reported = Luma(new SharpMUTerm.Core.Text.Rgb(0x33, 0x39, 0x4c))
            - Luma(new SharpMUTerm.Core.Text.Rgb(0x26, 0x2b, 0x3a));
        var now = Luma(WorkspacePalette.ArmedBand(theme)) - Luma(WorkspacePalette.IdleBand(theme));

        await Assert.That(now).IsGreaterThan(reported * 2);
    }

    /// <summary>
    /// The armed band differs in hue as well as in brightness, so the pair still reads for someone who
    /// cannot use the luma step — the second cue colour is not asked to carry alone.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Solarized Dark")]
    public async Task TheArmedBandLeansTowardThePromptColour(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var armed = WorkspacePalette.ArmedBand(theme);
        var idle = WorkspacePalette.IdleBand(theme);

        // The prompt colour is bluer than it is red on every shipped theme, so the band it leans toward
        // gains more blue than red. Asserting the *direction* rather than a hex keeps this a claim about
        // the relationship, which is the only thing that survives a theme change.
        await Assert.That(armed.B - idle.B).IsGreaterThan(armed.R - idle.R);
    }

    /// <summary>
    /// A focused pane's plane is a step above an unfocused one, on every theme. Smaller than the input
    /// bands' step on purpose — the pane's plane is what the game's own colours are read against — which
    /// is exactly why the tab chip and the ▌ marker carry the rest of the signal.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task TheFocusedPaneIsLiftedOffTheUnfocusedOne(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var focused = Luma(WorkspacePalette.Focus(theme));
        var unfocused = Luma(WorkspacePalette.Surface(theme));

        await Assert.That(focused - unfocused).IsGreaterThan(Visible);
    }

    /// <summary>
    /// The four planes of the workspace are four <em>different</em> colours. This is not pedantry: the
    /// first attempt at this made the idle input band equal to the backdrop, which reads as the bar
    /// vanishing into the chrome — and made a painted-cell test unable to tell one from the other, which
    /// is the same defect stated in the language of the frame.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task EveryPlaneIsItsOwnTone(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var planes = new[]
        {
            WorkspacePalette.Surface(theme),
            WorkspacePalette.Backdrop(theme),
            WorkspacePalette.Focus(theme),
            WorkspacePalette.IdleBand(theme),
            WorkspacePalette.ArmedBand(theme),
        };

        await Assert.That(planes.Distinct().Count()).IsEqualTo(planes.Length);
    }

    /// <summary>
    /// <b>Truecolor is not guaranteed.</b> On a 256-colour terminal every one of these tones is
    /// approximated to the nearest xterm-256 entry, and two colours a few points apart collapse onto the
    /// <em>same</em> entry — which is very likely part of why the old pair of bands was reported as
    /// indistinguishable rather than merely close — two tones a few points apart can round to one entry,
    /// and nothing in the old design guaranteed they would not. The distinction has to survive that
    /// quantisation, so the pairs that carry focus are checked against the palette a 256-colour terminal
    /// actually has, on every shipped theme.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task FocusSurvivesA256ColourTerminal(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);

        await Assert.That(Xterm256(WorkspacePalette.ArmedBand(theme)))
            .IsNotEqualTo(Xterm256(WorkspacePalette.IdleBand(theme)));
        await Assert.That(Xterm256(WorkspacePalette.Focus(theme)))
            .IsNotEqualTo(Xterm256(WorkspacePalette.Surface(theme)));
    }

    // --- The per-character tints -----------------------------------------------------------------

    /// <summary>Every colour a character's pane can be given, without the untinted default.</summary>
    private static IEnumerable<PaneTint> Colours() =>
        Enum.GetValues<PaneTint>().Where(t => t != PaneTint.None);

    /// <summary>
    /// <b>All six tints sit at exactly one luminance — and it is one step below the untinted surface,
    /// not level with it.</b> Two claims, and the first is the one everything else rests on: no
    /// character's pane may be brighter than another's, or the colour would be answering the question
    /// focus answers. It holds by construction (the plane is re-lit before the anchor is mixed in, and
    /// luma is linear in the channels), so what is left is rounding to bytes and nothing else — two of
    /// them, the re-light and the mix, which is worth about a point of luma at any brightness.
    /// <para>
    /// The second claim is a deliberate change from the first cut, which pinned the tinted plane to the
    /// surface's <em>own</em> luma. MU* servers are written for black terminals and their bright ANSI is
    /// what is read on this plane, so it wants to be darker than the client's chrome. The step is the
    /// same for every colour, which is what leaves the focus cue — a ratio — untouched.
    /// </para>
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task EveryTintedPaneSitsAtOneLuminanceAStepBelowTheSurface(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var surface = Luma(WorkspacePalette.Surface(theme));
        var planes = Colours().Select(t => Luma(WorkspacePalette.Tint(theme, t))).ToList();

        // One luminance, whatever the colour.
        await Assert.That(planes.Max() - planes.Min()).IsLessThanOrEqualTo(1.5).Because(themeName);

        // And below the untinted plane. As a *fraction* of it, not by a fixed number of luma: on a dark
        // theme the whole workspace lives in the low thirties, where a step of Visible would be a third
        // of the surface's entire brightness, and the eye reads a dark plane's depth in ratio anyway.
        await Assert.That(planes.Max()).IsLessThan(surface * 0.9).Because(themeName);
    }

    /// <summary>
    /// <b>Every focused pane on the screen is brighter than every unfocused one, whatever colours are in
    /// play.</b> This is what bounds the darkening, and it is the reason the depth is derived rather than
    /// chosen: a client may hold tinted and untinted characters at once, so the planes on screen are the
    /// tinted one, the surface, and each of those lifted — and a darkening deep enough to drop a
    /// <em>focused</em> tinted pane to an <em>unfocused</em> untinted one would have the focus cue
    /// reporting the wrong fact, which is exactly what the two-channel design exists to prevent.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task EveryFocusedPaneOutshinesEveryUnfocusedOneAcrossTheWholePalette(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var planes = Colours().Select(t => WorkspacePalette.Tint(theme, t))
            .Append(WorkspacePalette.Surface(theme))
            .ToList();

        var dimmestFocused = planes.Min(p => Luma(WorkspacePalette.Focus(p)));
        var brightestUnfocused = planes.Max(Luma);

        await Assert.That(dimmestFocused).IsGreaterThan(brightestUnfocused).Because(themeName);
    }

    /// <summary>
    /// And it is a <em>tint</em>: the plane really does move. Distance measured across the channels
    /// rather than in luma, because luma is precisely the axis the test above pins to zero — a tint that
    /// did nothing at all would pass that one.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task EveryTintIsItsOwnPlaneAndNoneOfThemIsTheSurface(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var planes = Colours().Select(t => WorkspacePalette.Tint(theme, t)).ToList();

        await Assert.That(planes.Distinct().Count()).IsEqualTo(planes.Count);
        await Assert.That(planes).DoesNotContain(WorkspacePalette.Surface(theme));
    }

    /// <summary>
    /// <see cref="PaneTint.None"/> is the surface itself, byte for byte. A client whose characters have
    /// chosen no colour is painted exactly as it was before this existed — which is what makes the
    /// default safe to ship without a migration.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task NoTintIsTheSurfaceUnchanged(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);

        await Assert.That(WorkspacePalette.Tint(theme, PaneTint.None))
            .IsEqualTo(WorkspacePalette.Surface(theme));
    }

    /// <summary>
    /// <b>Focus survives every tint.</b> The two cues are on separate channels — identity in hue, focus
    /// in luminance — so a tinted pane is still visibly the focused one, on every theme and in every
    /// colour. This is the composition claim: the tint replaces the plane, and the focus step is taken
    /// off whatever plane that is.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task AFocusedTintedPaneIsStillLiftedOffItsUnfocusedSelf(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);

        foreach (var tint in Colours())
        {
            var plane = WorkspacePalette.Tint(theme, tint);
            await Assert.That(Luma(WorkspacePalette.Focus(plane)) - Luma(plane))
                .IsGreaterThan(Visible)
                .Because($"{tint} on {themeName}");
        }
    }

    /// <summary>
    /// And it is the <em>same</em> lift, measured the way the lift is actually applied: focus is a
    /// multiplication, so what has to be equal above every plane is the <em>ratio</em>, and it is equal
    /// above all six tints and above the plain surface alike. Without this a colour could quietly make
    /// one character's pane look more focused than another's, which would be the cue reporting the wrong
    /// fact. (The absolute step is smaller above a tinted plane than above the untinted one, and
    /// necessarily so — the plane is darker. That is the trade the depth makes, and the ordering it may
    /// not break is pinned by
    /// <see cref="EveryFocusedPaneOutshinesEveryUnfocusedOneAcrossTheWholePalette"/>.)
    /// <para>
    /// The light theme is left out on purpose and not by oversight: its surface is already so bright that
    /// the focus step clamps at white (this is true of the untinted plane too, and predates tints), so
    /// there is no step there for a tint to preserve or to break.
    /// </para>
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Solarized Dark")]
    public async Task TheFocusStepIsTheSameRatioAboveATintedPlaneAsAboveThePlainOne(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var plain = Luma(WorkspacePalette.Focus(theme)) / Luma(WorkspacePalette.Surface(theme));

        foreach (var tint in Colours())
        {
            var plane = WorkspacePalette.Tint(theme, tint);
            var step = Luma(WorkspacePalette.Focus(plane)) / Luma(plane);
            await Assert.That(Math.Abs(step - plain)).IsLessThan(0.05).Because($"{tint} on {themeName}");
        }
    }

    // --- the tints on the command line -------------------------------------------------------------

    /// <summary>
    /// <b>A tinted command line keeps the whole armed-versus-idle step.</b> The bar wears the character's
    /// hue, and only the hue: luminance on this row is already the one cue that says which line ⏎ sends
    /// from, so the tint leaves it alone. Held to the same <see cref="Obvious"/> bar the untinted pair is
    /// held to — a colour must never make the two bars harder to tell apart than no colour at all.
    /// <para>
    /// The light theme is measured against <see cref="Visible"/> instead, and the reason is arithmetic
    /// rather than tolerance: its idle band lifted by the focus step clamps against white before the lean
    /// toward the prompt colour is applied, so how much of the step survives depends on which channels a
    /// given hue has left to give. It predates tints — the untinted light band is already the narrowest
    /// of the three — and every other cue on the row is untouched.
    /// </para>
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task ATintedCommandLineIsStillObviouslyArmedOrIdle(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var floor = themeName == "Light" ? Visible : Obvious;

        foreach (var tint in Colours())
        {
            var armed = Luma(WorkspacePalette.ArmedBand(theme, tint));
            var idle = Luma(WorkspacePalette.IdleBand(theme, tint));

            await Assert.That(armed - idle).IsGreaterThan(floor).Because($"{tint} on {themeName}");
        }
    }

    /// <summary>
    /// The band moves in colour and not in brightness — the other half of the composition rule, and what
    /// makes the claim above hold by construction rather than by luck. It also keeps
    /// <see cref="WorkspacePalette.IdleInk"/>, which is measured against the untinted band and shared with
    /// the tab chips, at the contrast it was picked with.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task ATintedBandKeepsTheUntintedBandsBrightness(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var plain = Luma(WorkspacePalette.IdleBand(theme));

        foreach (var tint in Colours())
        {
            var band = WorkspacePalette.IdleBand(theme, tint);
            await Assert.That(Math.Abs(Luma(band) - plain)).IsLessThanOrEqualTo(1.0).Because($"{tint} on {themeName}");
            await Assert.That(band).IsNotEqualTo(WorkspacePalette.IdleBand(theme)).Because($"{tint} on {themeName}");
        }
    }

    /// <summary>
    /// And the armed band still leans toward the theme's prompt colour on top of the character's hue, so
    /// a tinted pair differs in brightness <em>and</em> in hue exactly as the untinted pair does. This is
    /// the reason the armed band is derived from the tinted idle one rather than tinted in its own right:
    /// tinting it directly would have overwritten the lean with the character's colour.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Solarized Dark")]
    public async Task ATintedArmedBandStillLeansTowardThePromptColour(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);

        foreach (var tint in Colours())
        {
            var armed = WorkspacePalette.ArmedBand(theme, tint);
            var idle = WorkspacePalette.IdleBand(theme, tint);

            await Assert.That(armed.B - idle.B).IsGreaterThan(armed.R - idle.R).Because($"{tint} on {themeName}");
        }
    }

    /// <summary>
    /// <see cref="PaneTint.None"/> leaves both bands byte for byte where they were. The command line of a
    /// client whose characters have chosen no colour — and of one whose focused window belongs to nobody
    /// at all — is painted exactly as it was before any of this existed.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    [Arguments("Solarized Dark")]
    public async Task NoTintLeavesBothBandsUnchanged(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);

        await Assert.That(WorkspacePalette.IdleBand(theme, PaneTint.None)).IsEqualTo(WorkspacePalette.IdleBand(theme));
        await Assert.That(WorkspacePalette.ArmedBand(theme, PaneTint.None)).IsEqualTo(WorkspacePalette.ArmedBand(theme));
    }

    /// <summary>
    /// Two characters' command lines are told apart by colour, the same way their panes are. Measured
    /// with brightness discounted, because these bands are all at one luminance by construction.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Solarized Dark")]
    public async Task TwoCharactersCommandLinesDifferInHue(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var bands = Colours().Select(t => (Tint: t, Band: WorkspacePalette.IdleBand(theme, t))).ToList();

        foreach (var a in bands)
        {
            foreach (var b in bands.Where(p => p.Tint != a.Tint))
            {
                await Assert.That(ChromaDistance(a.Band, b.Band))
                    .IsGreaterThan(6.0)
                    .Because($"{a.Tint} vs {b.Tint} on {themeName}");
            }
        }
    }

    /// <summary>
    /// The tints are told apart by <em>colour</em>, which is the cue that survives sitting beside a pane
    /// of the same brightness. Measured as the distance between two planes with their (identical)
    /// brightness taken out — a pair that differed only in luma would score zero here, and would be two
    /// characters a reader has to compare rather than recognise.
    /// </summary>
    [Test]
    [Arguments("Dark")]
    [Arguments("Solarized Dark")]
    public async Task TwoCharactersPanesDifferInHueAndNotInBrightness(string themeName)
    {
        var theme = ThemeLibrary.Get(themeName);
        var planes = Colours().Select(t => (Tint: t, Plane: WorkspacePalette.Tint(theme, t))).ToList();

        foreach (var a in planes)
        {
            foreach (var b in planes.Where(p => p.Tint != a.Tint))
            {
                await Assert.That(ChromaDistance(a.Plane, b.Plane))
                    .IsGreaterThan(6.0)
                    .Because($"{a.Tint} vs {b.Tint} on {themeName}");
            }
        }
    }

    /// <summary>
    /// How far apart two colours are once their brightness is discounted — the channel distance between
    /// them after each has been shifted to a common luma. It is a coarse measure and deliberately so: the
    /// claim being pinned is "these are different colours", not a perceptual model.
    /// </summary>
    private static double ChromaDistance(SharpMUTerm.Core.Text.Rgb a, SharpMUTerm.Core.Text.Rgb b)
    {
        var shift = Luma(a) - Luma(b);
        var dr = a.R - b.R - shift;
        var dg = a.G - b.G - shift;
        var db = a.B - b.B - shift;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    /// <summary>
    /// The nearest xterm-256 palette entry to a colour — the 6×6×6 cube (16–231) and the 24-step grey
    /// ramp (232–255), which is what a 256-colour terminal has to choose from. Nearest by squared
    /// distance, which is what every terminal and library that does this uses.
    /// </summary>
    private static int Xterm256(SharpMUTerm.Core.Text.Rgb rgb)
    {
        var levels = new[] { 0, 95, 135, 175, 215, 255 };
        var best = 0;
        var bestDistance = int.MaxValue;

        for (var i = 0; i < 216; i++)
        {
            var r = levels[i / 36];
            var g = levels[i / 6 % 6];
            var b = levels[i % 6];
            Consider(16 + i, r, g, b);
        }

        for (var i = 0; i < 24; i++)
        {
            var grey = 8 + (i * 10);
            Consider(232 + i, grey, grey, grey);
        }

        return best;

        void Consider(int index, int r, int g, int b)
        {
            var distance = ((rgb.R - r) * (rgb.R - r)) + ((rgb.G - g) * (rgb.G - g)) + ((rgb.B - b) * (rgb.B - b));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = index;
            }
        }
    }
}
