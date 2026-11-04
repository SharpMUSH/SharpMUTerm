using SharpMUTerm.Core.Layouts;
using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Tests.Workspaces;

/// <summary>
/// A strip panel (one row of meters, one row of buttons) is as tall as its content, whatever share its region was
/// given: <see cref="PaneNode.FixedRows"/> is honoured by the solver and the resizer, and the rest of a stacked
/// split goes to the panes that share.
/// </summary>
public class PaneExtentTests
{
    private static readonly PaneRect Screen = new(0, 0, 100, 30);

    private static PaneNode Pane(string id, int? fixedRows = null, int? minRows = null) =>
        new(id, [id]) { FixedRows = fixedRows, MinRows = minRows };

    [Test]
    public async Task AFixedStripGetsExactlyItsRowsAndTheRestGoesToTheSharingPane()
    {
        // The Combat shape: a one-row strip over the output, the strip's share a tenth of the window.
        var root = new SplitNode(SplitDirection.Column, [Pane("vitals", fixedRows: 1), Pane("output")], [0.1, 0.9]);

        var rects = LayoutSolver.Solve(root, Screen);

        await Assert.That(rects["vitals"]).IsEqualTo(new PaneRect(0, 0, 100, 1));
        await Assert.That(rects["output"]).IsEqualTo(new PaneRect(0, 2, 100, 28));
    }

    [Test]
    public async Task TheShareAFixedPaneWasGivenDoesNotMatter()
    {
        var narrow = new SplitNode(SplitDirection.Column, [Pane("output"), Pane("quick", fixedRows: 1)], [0.95, 0.05]);
        var wide = new SplitNode(SplitDirection.Column, [Pane("output"), Pane("quick", fixedRows: 1)], [0.2, 0.8]);

        await Assert.That(LayoutSolver.Solve(narrow, Screen)["quick"].Height).IsEqualTo(1);
        await Assert.That(LayoutSolver.Solve(wide, Screen)["quick"].Height).IsEqualTo(1);
        await Assert.That(LayoutSolver.Solve(wide, Screen)["output"].Height).IsEqualTo(28);
    }

    [Test]
    public async Task SharingPanesStillSplitWhatIsLeftByTheirFractions()
    {
        var root = new SplitNode(
            SplitDirection.Column,
            [Pane("strip", fixedRows: 2), Pane("a"), Pane("b")],
            [0.5, 0.25, 0.25]);

        var rects = LayoutSolver.Solve(root, Screen);

        // 30 − 2 dividers = 28; the strip takes 2, and a and b split the 26 left evenly.
        await Assert.That(rects["strip"].Height).IsEqualTo(2);
        await Assert.That(rects["a"].Height).IsEqualTo(13);
        await Assert.That(rects["b"].Height).IsEqualTo(13);
    }

    [Test]
    public async Task ColumnsAreNeverFixed()
    {
        var root = new SplitNode(SplitDirection.Row, [Pane("vitals", fixedRows: 1), Pane("output")]);

        var rects = LayoutSolver.Solve(root, Screen);

        await Assert.That(rects["vitals"].Height).IsEqualTo(30);
        await Assert.That(rects["vitals"].Width).IsEqualTo(50);
    }

    [Test]
    public async Task AFloorRaisesAPaneItsShareWouldSqueeze()
    {
        // The Roleplay shape: the composer's region is a fifth of the window, its floor six rows.
        var root = new SplitNode(SplitDirection.Column, [Pane("output"), Pane("composer", minRows: 6)], [0.9, 0.1]);

        var rects = LayoutSolver.Solve(root, Screen);

        await Assert.That(rects["composer"].Height).IsEqualTo(6);
        await Assert.That(rects["output"].Height).IsEqualTo(23);
    }

    [Test]
    public async Task WhenEveryPaneIsFixedTheLastTakesWhatIsLeftSoNoRowIsLost()
    {
        var root = new SplitNode(SplitDirection.Column, [Pane("a", fixedRows: 1), Pane("b", fixedRows: 1)]);

        var rects = LayoutSolver.Solve(root, Screen);

        await Assert.That(rects["a"].Height).IsEqualTo(1);
        await Assert.That(rects["b"].Height).IsEqualTo(28);
    }

    [Test]
    public async Task ASubtreeIsFixedOnlyWhenEveryPaneInItIs()
    {
        var strips = new SplitNode(SplitDirection.Row, [Pane("vitals", fixedRows: 1), Pane("quick", fixedRows: 2)]);
        var stack = new SplitNode(SplitDirection.Column, [Pane("vitals2", fixedRows: 1), Pane("quick2", fixedRows: 1)]);
        var mixed = new SplitNode(SplitDirection.Row, [Pane("vitals3", fixedRows: 1), Pane("output")]);

        await Assert.That(PaneExtent.FixedRows(strips)).IsEqualTo(2);
        await Assert.That(PaneExtent.FixedRows(stack)).IsEqualTo(3); // two rows and the divider between them
        await Assert.That(PaneExtent.FixedRows(mixed)).IsNull();
    }

    [Test]
    public async Task DraggingTheDividerOfAFixedStripMovesNothing()
    {
        var root = new SplitNode(SplitDirection.Column, [Pane("vitals", fixedRows: 1), Pane("output")], [0.1, 0.9]);
        var before = root.Sizes.ToList();
        var rects = LayoutSolver.Solve(root, Screen);

        var result = PaneResize.DragBorder(root, 0, 8, rects);

        await Assert.That(result.Outcome).IsEqualTo(PaneResizeOutcome.FixedSize);
        await Assert.That(root.Sizes).IsEquivalentTo(before);
    }

    [Test]
    public async Task ResizingFromBesideAStripTakesTheRowsFromThePaneBeyondIt()
    {
        // output between a fixed strip above and a sharing pane below: ⌥⇧↑ grows output from below, not the strip.
        var layout = new WorkspaceLayout(
            new SplitNode(SplitDirection.Column, [Pane("strip", fixedRows: 1), Pane("output"), Pane("below")], [0.1, 0.45, 0.45]),
            "output");
        var rects = LayoutSolver.Solve(layout.Root, Screen);

        var result = PaneResize.Apply(layout, PaneDirection.Up, rects);
        var after = LayoutSolver.Solve(layout.Root, Screen);

        await Assert.That(result.Outcome).IsEqualTo(PaneResizeOutcome.Resized);
        await Assert.That(after["strip"].Height).IsEqualTo(1);
        await Assert.That(after["output"].Height).IsEqualTo(rects["output"].Height + 1);
    }

    [Test]
    public async Task AResizeWithOnlyAStripToPayIsRefusedAndSaysWhy()
    {
        var layout = new WorkspaceLayout(
            new SplitNode(SplitDirection.Column, [Pane("vitals", fixedRows: 1), Pane("output")]),
            "output");
        var rects = LayoutSolver.Solve(layout.Root, Screen);

        var result = PaneResize.Apply(layout, PaneDirection.Up, rects);

        await Assert.That(result.Outcome).IsEqualTo(PaneResizeOutcome.FixedSize);
        await Assert.That(PaneResize.Describe(result.Outcome, PaneDirection.Up)).IsNotEmpty();
    }

    [Test]
    public async Task AStripsFloorIsItsOwnHeightNotTheOrdinaryFloor()
    {
        await Assert.That(PaneResize.Minimum(Pane("vitals", fixedRows: 1), horizontal: false)).IsEqualTo(1);
        await Assert.That(PaneResize.Minimum(Pane("composer", minRows: 6), horizontal: false)).IsEqualTo(6);
        await Assert.That(PaneResize.Minimum(Pane("output"), horizontal: false)).IsEqualTo(PaneResize.MinimumPaneRows);
    }

    [Test]
    public async Task APaneHoldingOneStripPanelIsAsTallAsItAndDrawsNoTabStrip()
    {
        var pane = new PaneNode("p", ["panel:vitals:W.C"]);

        PanelSizing.Apply(pane, PanelOf, tabStripRows: 1);

        await Assert.That(pane.FixedRows).IsEqualTo(1);
        await Assert.That(PanelSizing.ShowsTabStrip(pane.Tabs.Select(PanelOf).ToList())).IsFalse();
    }

    [Test]
    public async Task TwoStripPanelsInOnePaneAddTheTabStripTheyThenNeed()
    {
        var pane = new PaneNode("p", ["panel:vitals:W.C", "panel:quick-bar:W.C"]);

        PanelSizing.Apply(pane, PanelOf, tabStripRows: 1);

        await Assert.That(pane.FixedRows).IsEqualTo(2);
        await Assert.That(PanelSizing.ShowsTabStrip(pane.Tabs.Select(PanelOf).ToList())).IsTrue();
    }

    [Test]
    public async Task AStripHandedAnOrdinaryWindowStopsBeingAStrip()
    {
        var pane = new PaneNode("p", ["panel:vitals:W.C"]) { FixedRows = 1 };
        pane.Tabs.Add("main");

        PanelSizing.Apply(pane, PanelOf, tabStripRows: 1);

        await Assert.That(pane.FixedRows).IsNull();
    }

    [Test]
    public async Task AComposerPaneHasAFloorOfItsEditorAndItsTabStrip()
    {
        var pane = new PaneNode("p", ["panel:composer:W.C"]);

        PanelSizing.Apply(pane, PanelOf, tabStripRows: 1);

        await Assert.That(pane.FixedRows).IsNull();
        await Assert.That(pane.MinRows).IsEqualTo(PanelSizing.ComposerMinRows + 1);
    }

    private static string? PanelOf(string windowId)
    {
        if (!windowId.StartsWith("panel:", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = windowId["panel:".Length..];
        return rest[..rest.IndexOf(':')];
    }
}
