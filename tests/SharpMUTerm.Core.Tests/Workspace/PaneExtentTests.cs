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

        // Flush: a strip sits against its neighbour with no divider row, so the output has the row a rule took.
        await Assert.That(rects["vitals"]).IsEqualTo(new PaneRect(0, 0, 100, 1));
        await Assert.That(rects["output"]).IsEqualTo(new PaneRect(0, 1, 100, 29));
    }

    [Test]
    public async Task TheShareAFixedPaneWasGivenDoesNotMatter()
    {
        var narrow = new SplitNode(SplitDirection.Column, [Pane("output"), Pane("quick", fixedRows: 1)], [0.95, 0.05]);
        var wide = new SplitNode(SplitDirection.Column, [Pane("output"), Pane("quick", fixedRows: 1)], [0.2, 0.8]);

        await Assert.That(LayoutSolver.Solve(narrow, Screen)["quick"].Height).IsEqualTo(1);
        await Assert.That(LayoutSolver.Solve(wide, Screen)["quick"].Height).IsEqualTo(1);
        await Assert.That(LayoutSolver.Solve(wide, Screen)["output"].Height).IsEqualTo(29);
    }

    [Test]
    public async Task SharingPanesStillSplitWhatIsLeftByTheirFractions()
    {
        var root = new SplitNode(
            SplitDirection.Column,
            [Pane("strip", fixedRows: 2), Pane("a"), Pane("b")],
            [0.5, 0.25, 0.25]);

        var rects = LayoutSolver.Solve(root, Screen);

        // 30 − 1 divider (between a and b; none beside the strip) = 29; the strip takes 2, a and b split 27.
        await Assert.That(rects["strip"].Height).IsEqualTo(2);
        await Assert.That(rects["a"].Height + rects["b"].Height).IsEqualTo(27);
        await Assert.That(Math.Abs(rects["a"].Height - rects["b"].Height)).IsLessThanOrEqualTo(1);
    }

    [Test]
    public async Task NoDividerRowIsDrawnAboveAPaneThatDrawsItsOwnTabStrip()
    {
        var top = Pane("top");
        var below = Pane("below");
        below.DrawsHeader = true;
        var root = new SplitNode(SplitDirection.Column, [top, below]);

        var rects = LayoutSolver.Solve(root, Screen);

        await Assert.That(PaneExtent.DividerRows(root, 0)).IsEqualTo(0);
        await Assert.That(rects["top"]).IsEqualTo(new PaneRect(0, 0, 100, 15));
        await Assert.That(rects["below"]).IsEqualTo(new PaneRect(0, 15, 100, 15));
    }

    [Test]
    public async Task APaneWithNoTabStripStillGetsItsDividerRow()
    {
        var top = Pane("top");
        top.DrawsHeader = true; // the strip above a boundary is not the boundary
        var root = new SplitNode(SplitDirection.Column, [top, Pane("below")]);

        var rects = LayoutSolver.Solve(root, Screen);

        await Assert.That(PaneExtent.DividerRows(root, 0)).IsEqualTo(1);
        await Assert.That(rects["below"].Y).IsEqualTo(rects["top"].Height + 1);
    }

    [Test]
    public async Task SideBySideKeepsItsDividerColumnWhateverThePanesDraw()
    {
        var left = Pane("left");
        var right = Pane("right");
        left.DrawsHeader = right.DrawsHeader = true;
        var root = new SplitNode(SplitDirection.Row, [left, right]);

        var rects = LayoutSolver.Solve(root, Screen);

        await Assert.That(PaneExtent.DividerRows(root, 0)).IsEqualTo(1);
        await Assert.That(rects["right"].X).IsEqualTo(rects["left"].Width + 1);
    }

    [Test]
    public async Task ARowOfPanesIsTheBoundaryOnlyWhenEveryOneOfThemDrawsATabStrip()
    {
        var a = Pane("a");
        var b = Pane("b");
        a.DrawsHeader = true;
        var row = new SplitNode(SplitDirection.Row, [a, b]);
        var root = new SplitNode(SplitDirection.Column, [Pane("top"), row]);

        await Assert.That(PaneExtent.HeaderPanes(row)).IsEmpty();
        await Assert.That(PaneExtent.DividerRows(root, 0)).IsEqualTo(1);

        b.DrawsHeader = true;
        await Assert.That(PaneExtent.HeaderPanes(row).Select(p => p.Id)).IsEquivalentTo(["a", "b"]);
        await Assert.That(PaneExtent.DividerRows(root, 0)).IsEqualTo(0);
    }

    [Test]
    public async Task DraggingATabStripBoundaryPutsTheLowerPanesStripAtTheRowAskedFor()
    {
        var below = Pane("below");
        below.DrawsHeader = true;
        var root = new SplitNode(SplitDirection.Column, [Pane("top"), below]);
        var rects = LayoutSolver.Solve(root, Screen);

        var result = PaneResize.DragBorder(root, 0, 10, rects);
        var after = LayoutSolver.Solve(root, Screen);

        await Assert.That(result.Outcome).IsEqualTo(PaneResizeOutcome.Resized);
        await Assert.That(after["below"].Y).IsEqualTo(10);
        await Assert.That(after["top"].Height + after["below"].Height).IsEqualTo(30);
    }

    [Test]
    public async Task ApplyingPanelSizingSaysWhetherThePaneDrawsATabStrip()
    {
        var strip = new PaneNode("s", ["panel:vitals:W.C"]);
        var output = new PaneNode("o", ["main"]);

        PanelSizing.Apply(strip, PanelOf, tabStripRows: 1);
        PanelSizing.Apply(output, PanelOf, tabStripRows: 1);

        await Assert.That(strip.DrawsHeader).IsFalse();
        await Assert.That(output.DrawsHeader).IsTrue();
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
        await Assert.That(rects["b"].Height).IsEqualTo(29);
    }

    [Test]
    public async Task ASubtreeIsFixedOnlyWhenEveryPaneInItIs()
    {
        var strips = new SplitNode(SplitDirection.Row, [Pane("vitals", fixedRows: 1), Pane("quick", fixedRows: 2)]);
        var stack = new SplitNode(SplitDirection.Column, [Pane("vitals2", fixedRows: 1), Pane("quick2", fixedRows: 1)]);
        var mixed = new SplitNode(SplitDirection.Row, [Pane("vitals3", fixedRows: 1), Pane("output")]);

        await Assert.That(PaneExtent.FixedRows(strips)).IsEqualTo(2);
        await Assert.That(PaneExtent.FixedRows(stack)).IsEqualTo(2); // two rows, flush: strips need no divider
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
    public async Task AVitalsPaneInASideColumnIsACardAndAFullWidthOneIsAStrip()
    {
        // Row[ Column[ output, quick bar ], Column[ vitals, map ] ] with the side column at 30%.
        var side = new PaneNode("side", ["panel:vitals:W.C"]);
        var top = new PaneNode("top", ["panel:vitals:W.C"]);
        var root = new SplitNode(SplitDirection.Column,
        [
            top,
            new SplitNode(SplitDirection.Row, [new PaneNode("output", ["main"]), new SplitNode(SplitDirection.Column, [side, new PaneNode("map", ["map:W.C"])])], [0.7, 0.3]),
        ], [0.1, 0.9]);

        PanelSizing.ApplyAll(root, PanelOf, tabStripRows: 1, _ => 3);

        await Assert.That(side.FixedRows).IsEqualTo(PanelSizing.VitalsCardRows(3));
        await Assert.That(side.DrawsHeader).IsFalse();
        await Assert.That(top.FixedRows).IsEqualTo(1);
        await Assert.That(top.DrawsHeader).IsFalse();
    }

    [Test]
    public async Task AVitalsCardHoldsTwoRowsAMeterAndTheTargetsRowsWhetherOrNotThereIsOne()
    {
        await Assert.That(PanelSizing.VitalsCardRows(3)).IsEqualTo(12);
        await Assert.That(PanelSizing.VitalsCardRows(0)).IsEqualTo(6);
    }

    [Test]
    public async Task AGaugeCardHoldsTheTargetsRowsOnlyWhileThereIsOneAndTheHostsRuleSizesTheCard()
    {
        await Assert.That(PanelSizing.VitalsGaugeCardRows(3, target: false)).IsEqualTo(10);
        await Assert.That(PanelSizing.VitalsGaugeCardRows(3, target: true)).IsEqualTo(12);

        var side = new PaneNode("side", ["panel:vitals:W.C"]);
        var root = new SplitNode(SplitDirection.Row, [new PaneNode("output", ["main"]), side], [0.7, 0.3]);
        PanelSizing.ApplyAll(root, PanelOf, tabStripRows: 1, _ => 3, cardRowsOf: _ => 9);
        await Assert.That(side.FixedRows).IsEqualTo(9);
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
    public async Task AComposerPaneHasAFloorOfItsEditorAndNoTabStripUntilItHoldsAWindowToo()
    {
        var pane = new PaneNode("p", ["panel:composer:W.C"]);

        PanelSizing.Apply(pane, PanelOf, tabStripRows: 1);

        // Alone, the composer titles itself (COMPOSE): no tab strip over it.
        await Assert.That(pane.FixedRows).IsNull();
        await Assert.That(pane.DrawsHeader).IsFalse();
        await Assert.That(pane.MinRows).IsEqualTo(PanelSizing.ComposerMinRows);

        pane.Tabs.Add("main");
        PanelSizing.Apply(pane, PanelOf, tabStripRows: 1);
        await Assert.That(pane.DrawsHeader).IsTrue();
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
