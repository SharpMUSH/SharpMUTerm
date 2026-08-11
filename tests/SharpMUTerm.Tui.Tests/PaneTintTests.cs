using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Per-character pane tints, as the frame says them. A character can be given a colour and its panes are
/// painted in it, so a workspace holding several characters says whose pane is whose without being read.
/// <para>
/// The assertions are about <em>painted cells</em> for the reason the focus indicator's are
/// (<see cref="FocusIndicationTests"/>): a colour can be assigned to a control that is arranged at zero
/// rows, or to a plane something else paints over, and every such bug is invisible to a test that reads
/// the property back.
/// </para>
/// <para>
/// The one test to keep above the others is <see cref="TintingACharacterMovesNoPaneRectangle"/>, and it
/// is the same claim as <c>FocusIndicationTests.MovingFocusDoesNotMoveAnyPaneRectangle</c>: per-pane NAWS
/// is derived from the pane rectangle, so an identity cue that cost a cell — a coloured gutter, a border,
/// a marker column — would announce a new terminal size to every connected server the moment somebody
/// picked a colour on F5. Recolouring what is already drawn is the whole design, here as there.
/// </para>
/// </summary>
/// <remarks>
/// Serialised for the same reason the other end-to-end suites are: rendering a frame redirects the
/// process-global <c>Console.Out</c>, and the harness redirects <c>Console.In</c>.
/// </remarks>
[NotInParallel]
public class PaneTintTests
{
    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp App(AppConfiguration config, int width = 120, int height = 34)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(config, Headless, new HeadlessConsoleDriver(width, height));
    }

    private static SharpConsoleUI.Color Colour(Rgb rgb) => new(rgb.R, rgb.G, rgb.B, 255);

    /// <summary>Aetherfall's Corvid — the character the demo scene resumes with in its main window.</summary>
    private static CharacterDefinition Corvid(AppConfiguration config) => config.Worlds[0].Characters[0];

    // --- the tint reaches the pane ----------------------------------------------------------------

    /// <summary>
    /// A character with a colour has its pane painted in that colour, and the cells are on the frame
    /// inside that pane's own rectangle. Read against <see cref="WorkspacePalette"/> rather than a hex,
    /// because the plane is derived from the active theme and a literal would pin the wrong thing.
    /// </summary>
    [Test]
    public async Task ATintedCharactersPaneIsPaintedInItsColour()
    {
        var config = DemoScene.Build();
        Corvid(config).Tint = PaneTint.Slate;
        var app = App(config);

        var ansi = app.RenderSnapshot();
        var pane = app.PaneIdOf("main")!;
        var plane = WorkspacePalette.Focus(WorkspacePalette.Tint(config.Theme, PaneTint.Slate));

        await Assert.That(app.FocusedPaneId).IsEqualTo(pane); // the demo resumes focused on it
        await Assert.That(app.PaneSurfaceColors[pane]).IsEqualTo(Colour(plane));
        await Assert.That(FrameGrid.CellsPaintedIn(ansi, app.PaneOutputRects()[pane], Colour(plane)))
            .IsGreaterThan(0);
    }

    /// <summary>
    /// <b>An untinted client is painted exactly as it was before this existed.</b>
    /// <see cref="PaneTint.None"/> is the default and there is no migration marking anybody, so the frame
    /// a user who has chosen nothing sees has to be byte-identical to the one the palette produces on its
    /// own. This is the pin that makes the default safe.
    /// </summary>
    [Test]
    public async Task AnUntintedWorkspaceIsThePlainSurface()
    {
        var config = DemoScene.Build();
        var app = App(config);

        app.RenderSnapshot("split");
        var (focused, unfocused) = app.PaneBandColors;

        foreach (var (paneId, colour) in app.PaneSurfaceColors)
        {
            await Assert.That(colour).IsEqualTo(paneId == app.FocusedPaneId ? focused : unfocused);
        }
    }

    /// <summary>
    /// Two characters, two colours, one frame — the state the feature exists for. Each pane wears
    /// <em>its own</em> character's colour, which is the property that makes a glance worth anything: a
    /// background pane that took the focused character's colour would say the opposite of what it means.
    /// It is the same rule that stops a link clicked in a background pane sending to whoever is in front
    /// (<c>WindowSession</c>) — ownership is the workspace's record, never <c>_active</c>.
    /// </summary>
    [Test]
    public async Task TwoCharactersPanesWearTheirOwnColours()
    {
        var config = DemoScene.Build();
        var app = App(config);

        // The `tint` view is the shipping path: it writes the colours onto the real characters and then
        // opens and splits through the same code a user's keys reach.
        var ansi = app.RenderSnapshot("tint");
        var slate = WorkspacePalette.Tint(config.Theme, PaneTint.Slate);
        var moss = WorkspacePalette.Tint(config.Theme, PaneTint.Moss);
        var rects = app.PaneOutputRects();

        // The view colours the first configured world's character Slate and the second's Moss; each pane
        // is checked against the character the *workspace* says owns the window it is showing, so the
        // assertion follows ownership rather than restating the view's own arithmetic.
        var second = config.Worlds[1].Name + ".";

        await Assert.That(app.PaneSurfaceColors.Count).IsEqualTo(2);
        foreach (var (paneId, colour) in app.PaneSurfaceColors)
        {
            var owned = app.PaneActiveTab(paneId) is { } window && app.WindowOwnerOf(window) is { } key
                && key.StartsWith(second, StringComparison.Ordinal)
                    ? moss
                    : slate;
            var expected = paneId == app.FocusedPaneId ? WorkspacePalette.Focus(owned) : owned;

            await Assert.That(colour).IsEqualTo(Colour(expected)).Because(paneId);
            await Assert.That(FrameGrid.CellsPaintedIn(ansi, rects[paneId], Colour(expected)))
                .IsGreaterThan(0)
                .Because(paneId);
        }

        // And the two are genuinely different colours, or the frame proves nothing at all.
        await Assert.That(app.PaneSurfaceColors.Values.Distinct().Count()).IsEqualTo(2);
    }

    // --- composing with focus ---------------------------------------------------------------------

    /// <summary>
    /// <b>The tint replaces the plane and the focus step is taken off it.</b> Moving the focus onto a
    /// tinted pane lifts that pane the same way it lifts an untinted one, and the pane the focus left
    /// drops back to its own colour rather than to the plain surface — so at no point does a pane state
    /// one fact by losing the other. Driven with the real ⌃→ handler.
    /// </summary>
    [Test]
    public async Task FocusLiftsATintedPaneAndLeavesItsColourBehind()
    {
        var config = DemoScene.Build();
        var app = App(config);
        app.RenderSnapshot("tint");

        var first = app.FocusedPaneId;
        var second = app.PaneIds.Single(id => id != first);
        var planeOfFirst = app.PaneSurfaceColors[first];
        var planeOfSecond = app.PaneSurfaceColors[second];

        app.SimulateKey(new ConsoleKeyInfo('\0', ConsoleKey.RightArrow, false, false, true));
        var ansi = app.RenderWholeFrame();
        var rects = app.PaneOutputRects();

        await Assert.That(app.FocusedPaneId).IsEqualTo(second); // the move really happened

        // The pane that gained the focus is its own colour lifted; the one that lost it is its own
        // colour unlifted. Both are on the frame, inside their own rectangles.
        await Assert.That(app.PaneSurfaceColors[second]).IsNotEqualTo(planeOfSecond);
        await Assert.That(app.PaneSurfaceColors[first]).IsNotEqualTo(planeOfFirst);
        await Assert.That(FrameGrid.CellsPaintedIn(ansi, rects[second], app.PaneSurfaceColors[second]))
            .IsGreaterThan(0);
        await Assert.That(FrameGrid.CellsPaintedIn(ansi, rects[first], app.PaneSurfaceColors[first]))
            .IsGreaterThan(0);

        // Still two colours, and still one per character: the focus step moved, the identities did not.
        await Assert.That(app.PaneSurfaceColors.Values.Distinct().Count()).IsEqualTo(2);
    }

    /// <summary>
    /// The pane wears the colour of the window <em>in front of it</em>, which is the only honest answer
    /// for a pane hosting two characters' windows as tabs: one rectangle is painted, and the output on it
    /// belongs to one of them. Bringing the other tab forward repaints the pane, through the same
    /// activation path a tab click takes.
    /// </summary>
    [Test]
    public async Task ThePaneWearsTheColourOfTheWindowInFront()
    {
        var config = DemoScene.Build();
        Corvid(config).Tint = PaneTint.Ember;
        var app = App(config);
        app.RenderSnapshot(); // main and Chat share one pane, main in front

        var pane = app.PaneIdOf("main")!;
        var ember = Colour(WorkspacePalette.Focus(WorkspacePalette.Tint(config.Theme, PaneTint.Ember)));
        await Assert.That(app.PaneSurfaceColors[pane]).IsEqualTo(ember);

        // The Chat window belongs to the same character, so the colour survives the switch — the claim
        // being pinned is that the plane follows the front window at all, and this is the arm where the
        // answer stays put.
        app.SimulateWindowChange(DemoScene.ChatWindowId);
        app.RenderNextFrame();
        await Assert.That(app.PaneIdOf(DemoScene.ChatWindowId)).IsEqualTo(pane);
        await Assert.That(app.PaneSurfaceColors[pane]).IsEqualTo(ember);
    }

    /// <summary>
    /// A window nobody owns is untinted, and so is one whose owner this configuration no longer holds. It
    /// falls back to the plain surface rather than to the focused character's colour: an unowned pane
    /// wearing somebody's identity is worse than an unowned pane wearing none.
    /// </summary>
    [Test]
    public async Task AWindowWithNoOwnerIsUntinted()
    {
        var config = DemoScene.Build();
        Corvid(config).Tint = PaneTint.Plum;
        var app = App(config);
        app.RenderSnapshot("web"); // the web view belongs to no character

        var pane = app.PaneIdOf(app.WindowIds().Single(id => id.StartsWith("web", StringComparison.Ordinal)));
        var (focused, unfocused) = app.PaneBandColors;

        await Assert.That(pane).IsNotNull();
        await Assert.That(app.PaneSurfaceColors[pane!])
            .IsEqualTo(pane == app.FocusedPaneId ? focused : unfocused);
    }

    // --- the NAWS trap ----------------------------------------------------------------------------

    /// <summary>
    /// <b>Giving a character a colour moves no pane rectangle.</b> The counterpart of
    /// <c>FocusIndicationTests.MovingFocusDoesNotMoveAnyPaneRectangle</c>, and the reason this cue is a
    /// repaint rather than a border or a gutter: per-pane NAWS is derived from the pane rectangle, so a
    /// cue that cost a cell would re-announce a different terminal size to every connected server and
    /// reflow the game's own output — the moment somebody chose a colour on F5, and again every time they
    /// changed their mind. Checked narrow as well as roomy, because the roomy default is exactly where a
    /// one-cell change hides.
    /// <para>
    /// It commits through <c>SaveConfiguration</c> — the funnel every settings screen goes through — so
    /// this pins the other half too: a colour chosen on F5 arrives on the panes <em>now</em>, and not
    /// whenever the user next happens to move the focus.
    /// </para>
    /// </summary>
    [Test]
    [Arguments(160, 48)]
    [Arguments(120, 34)]
    [Arguments(100, 24)]
    [Arguments(80, 20)]
    public async Task TintingACharacterMovesNoPaneRectangle(int width, int height)
    {
        var config = DemoScene.Build();
        var app = App(config, width, height);
        app.RenderSnapshot("split");

        var before = app.PaneOutputRects().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var planesBefore = app.PaneSurfaceColors.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var railBefore = app.RailColumnWidth;

        Corvid(config).Tint = PaneTint.Ochre;
        app.SaveConfiguration();
        app.RenderNextFrame();

        // The colour really did arrive, without anything else being touched…
        await Assert.That(app.PaneSurfaceColors.Values.Distinct())
            .IsNotEquivalentTo(planesBefore.Values.Distinct());

        // …and it cost nothing: same sidebar, same rectangles, so the same NAWS size.
        await Assert.That(app.RailColumnWidth).IsEqualTo(railBefore);
        var after = app.PaneOutputRects();
        await Assert.That(after.Count).IsEqualTo(before.Count);
        foreach (var (paneId, rect) in before)
        {
            await Assert.That(after[paneId]).IsEqualTo(rect).Because(paneId);
        }
    }
}
