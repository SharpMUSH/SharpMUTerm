using SharpConsoleUI.Drivers;
using SharpConsoleUI.Parsing;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Core.Workspaces;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Which tab a pane is showing, and how the strip says so.
/// <para>
/// A chip states one fact — is this the tab you are viewing — relative to its own strip. Pane focus is
/// not a term in the arithmetic; it arrives folded into the plane, so the cue is one ratio in every
/// strip, on every theme, under every tint. Deriving the selected chip from focus instead is what left
/// an unfocused pane painting every chip its own surface tone, ink included.
/// </para>
/// <para>
/// <see cref="AnUnfocusedPanesSelectedTabIsDistinguishableFromItsSiblings"/> is a frame test rather than
/// an arithmetic one deliberately: the old arithmetic was internally consistent while the screen was
/// wrong, so anything agreeing with the expression would have passed.
/// </para>
/// </summary>
/// <remarks>Serialised: rendering redirects the process-global <c>Console.Out</c>.</remarks>
[NotInParallel]
public class TabSelectionTests
{
    private const int Width = 120;
    private const int Height = 32;

    private static readonly TerminalCapabilities Headless =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private static SharpMUTermApp Demo()
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(DemoScene.Build(), Headless, new HeadlessConsoleDriver(Width, Height));
    }

    /// <summary>
    /// The row carrying both tab labels of the <c>tabs</c> view's right-hand pane, with the column each
    /// starts at. Read off the decoded rows, since the strip sits above the pane rectangles.
    /// </summary>
    private static (int Row, int Chat, int Scenes) Strip(string frame)
    {
        var rows = FrameGrid.Decode(frame, Width, Height);
        for (var y = 0; y < rows.Count; y++)
        {
            var chat = rows[y].IndexOf("Chat", StringComparison.Ordinal);
            var scenes = rows[y].IndexOf("Scenes", StringComparison.Ordinal);
            if (chat >= 0 && scenes >= 0)
            {
                return (y, chat, scenes);
            }
        }

        throw new InvalidOperationException("No row carried both of the right pane's tab labels.");
    }

    /// <summary>
    /// The reported bug as a frame: the right pane holds no focus and two tabs, and the chip under the
    /// one in front has to differ from the chip under its sibling.
    /// </summary>
    [Test]
    public async Task AnUnfocusedPanesSelectedTabIsDistinguishableFromItsSiblings()
    {
        var frame = Demo().RenderSnapshot("tabs");
        var (row, chat, scenes) = Strip(frame);
        var cells = FrameGrid.Cells(frame, Width, Height);

        // A cell inside each label, not the padding between chips.
        await Assert.That(cells[(row, chat)].Background)
            .IsNotEqualTo(cells[(row, scenes)].Background);
    }

    /// <summary>
    /// Within a strip the selected chip is the brighter of the two. Across panes that ordering belongs to
    /// focus, which is why the step is taken inside the strip and not against a fixed tone.
    /// </summary>
    [Test]
    public async Task ASelectedChipOutshinesItsSiblingsOnEveryPlane()
    {
        foreach (var theme in ThemeLibrary.Names.Select(ThemeLibrary.Get))
        {
            foreach (var tint in Enum.GetValues<PaneTint>())
            {
                foreach (var lit in new[] { false, true })
                {
                    var plane = WorkspacePalette.Tint(theme, tint);
                    var surface = lit ? WorkspacePalette.Focus(plane) : plane;

                    await Assert.That(Contrast.RelativeLuminance(WorkspacePalette.Recessed(surface)))
                        .IsLessThan(Contrast.RelativeLuminance(surface));
                }
            }
        }
    }

    /// <summary>
    /// Focus still orders the panes: both of a focused strip's chips clear their unfocused counterparts,
    /// so the two steps cannot be read for each other.
    /// </summary>
    [Test]
    public async Task FocusStillOrdersTheStripsAcrossPanes()
    {
        foreach (var theme in ThemeLibrary.Names.Select(ThemeLibrary.Get))
        {
            foreach (var tint in Enum.GetValues<PaneTint>())
            {
                var plane = WorkspacePalette.Tint(theme, tint);
                var lit = WorkspacePalette.Focus(plane);

                await Assert.That(Contrast.RelativeLuminance(plane))
                    .IsLessThan(Contrast.RelativeLuminance(lit));
                await Assert.That(Contrast.RelativeLuminance(WorkspacePalette.Recessed(plane)))
                    .IsLessThan(Contrast.RelativeLuminance(WorkspacePalette.Recessed(lit)));
            }
        }
    }

    /// <summary>
    /// Selection is said in weight as well as colour, and costs no cells: every width the strip is
    /// measured by is <c>MarkupParser.StripLength</c>.
    /// </summary>
    [Test]
    public async Task SelectionIsBoldAndCostsNoCells()
    {
        var window = new WorkspaceWindow("w", "Chat", WindowKind.Spawn);
        var selected = TabTitles.For(window, selected: true);

        await Assert.That(selected).Contains("[bold]");
        await Assert.That(MarkupParser.StripLength(selected))
            .IsEqualTo(MarkupParser.StripLength(TabTitles.For(window)));
    }

    /// <summary>
    /// A background pane's front tab collects lines like any other, so the two cues have to compose into
    /// one tag rather than nest.
    /// </summary>
    [Test]
    public async Task ASelectedTabThatIsAlsoUnreadKeepsBothCues()
    {
        var workspace = new Workspace();
        var window = workspace.OpenWindow("w", "Chat", WindowKind.Spawn);
        workspace.ActivateWindow("main");
        for (var i = 0; i < 4; i++)
        {
            workspace.NoteActivity("w");
        }

        var markup = TabTitles.For(window, selected: true);

        await Assert.That(markup).Contains($"[bold {UnreadBadge.TintFor(null)}]");
        await Assert.That(MarkupParser.StripLength(markup))
            .IsEqualTo(MarkupParser.StripLength(TabTitles.For(window)));
    }

    /// <summary>
    /// Per-pane NAWS is derived from the pane rectangles, so a selection cue that cost a cell would
    /// re-announce a terminal size on every tab change. Counterpart of
    /// <c>TabActivityIndicatorTests.ActivityMovesNoPaneRectangle</c>.
    /// </summary>
    [Test]
    public async Task SelectingATabMovesNoPaneRectangle()
    {
        var app = Demo();
        app.RenderSnapshot("tabs");
        var before = app.PaneOutputRects().ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        app.SimulateWindowChange(DemoScene.ScenesWindowId);
        app.RenderNextFrame();

        var after = app.PaneOutputRects();
        await Assert.That(after.Count).IsEqualTo(before.Count);
        foreach (var (paneId, rect) in after)
        {
            await Assert.That(rect).IsEqualTo(before[paneId]);
        }
    }
}
