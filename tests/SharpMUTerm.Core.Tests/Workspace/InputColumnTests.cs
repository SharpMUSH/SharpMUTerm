using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Tests.Workspaces;

/// <summary>Where the command line goes when a host puts it in the output's column (<see cref="InputColumn"/>).</summary>
public class InputColumnTests
{
    private static bool Output(PaneNode pane) => pane.Tabs.Any(t => t.StartsWith("main", StringComparison.Ordinal));

    [Test]
    public async Task ASideColumnOfFurnitureRunsDownBesideTheBars()
    {
        // Roleplay: the output (with Chat behind it) beside a column of composer over who is here.
        var output = new PaneNode("out", ["main", "chat"]);
        var side = new SplitNode(SplitDirection.Column, [new PaneNode("compose", ["panel:composer"]), new PaneNode("who", ["panel:who"])]);
        var root = new SplitNode(SplitDirection.Row, [output, side]);

        await Assert.That(InputColumn.Target(root, "out", Output)).IsSameReferenceAs(output);
    }

    [Test]
    public async Task AStripUnderTheOutputIsInsideTheColumn()
    {
        // Combat: the output over a quick bar, beside the vitals over the map with Chat behind it — a capture is not output.
        var column = new SplitNode(SplitDirection.Column, [new PaneNode("out", ["main"]), new PaneNode("quick", ["panel:quick"])]);
        var side = new SplitNode(SplitDirection.Column, [new PaneNode("vitals", ["panel:vitals"]), new PaneNode("map", ["map:x", "chat"])]);
        var root = new SplitNode(SplitDirection.Row, [column, side]);

        await Assert.That(InputColumn.Target(root, "out", Output)).IsSameReferenceAs(column);
    }

    [Test]
    public async Task AStripAcrossTheTopIsPassedThrough()
    {
        var output = new PaneNode("out", ["main"]);
        var row = new SplitNode(SplitDirection.Row, [output, new PaneNode("map", ["map:x"])]);
        var root = new SplitNode(SplitDirection.Column, [new PaneNode("vitals", ["panel:vitals"]), row]);

        await Assert.That(InputColumn.Target(root, "out", Output)).IsSameReferenceAs(output);
    }

    [Test]
    public async Task TwoCharactersSideBySideShareTheBars()
    {
        var root = new SplitNode(SplitDirection.Row, [new PaneNode("a", ["main:a"]), new PaneNode("b", ["main:b"])]);

        await Assert.That(InputColumn.Target(root, "a", Output)).IsSameReferenceAs(root);
    }

    [Test]
    public async Task NoAnchorMeansTheWholeArea()
    {
        var root = new SplitNode(SplitDirection.Row, [new PaneNode("a", ["main"]), new PaneNode("b", ["panel:who"])]);

        await Assert.That(InputColumn.Target(root, null, Output)).IsSameReferenceAs(root);
        await Assert.That(InputColumn.Target(root, "gone", Output)).IsSameReferenceAs(root);
    }

    [Test]
    public async Task AColumnUnderTheFloorGivesTheBarsTheSplit()
    {
        // Three panes side by side in 120 columns: 39 each, under the floor, so the bars span the split.
        var output = new PaneNode("out", ["main"]);
        var root = new SplitNode(SplitDirection.Row, [new PaneNode("chat", ["chat"]), output, new PaneNode("scenes", ["scenes"])]);

        await Assert.That(InputColumn.Target(root, "out", Output, 120)).IsSameReferenceAs(root);
        await Assert.That(InputColumn.Target(root, "out", Output, 200)).IsSameReferenceAs(output);
    }

    [Test]
    public async Task HalfTheDefaultWindowKeepsItsColumn()
    {
        var output = new PaneNode("out", ["main"]);
        var root = new SplitNode(SplitDirection.Row, [output, new PaneNode("chat", ["chat"])]);

        await Assert.That(InputColumn.Target(root, "out", Output, 120)).IsSameReferenceAs(output);
        await Assert.That(InputColumn.Target(root, "out", Output, 100)).IsSameReferenceAs(root);
    }

    [Test]
    public async Task TheFloorStopsAtTheWidestNodeThatStillFits()
    {
        // The output in a narrow column nested inside a wide one: the bars go under the wide one, not the root.
        var output = new PaneNode("out", ["main"]);
        var inner = new SplitNode(SplitDirection.Row, [output, new PaneNode("chat", ["chat"])], [0.4, 0.6]);
        var root = new SplitNode(SplitDirection.Row, [inner, new PaneNode("map", ["map:x"])], [0.7, 0.3]);

        // 140 columns: inner is 97 wide, the output 38 — under the floor.
        await Assert.That(InputColumn.Target(root, "out", Output, 140)).IsSameReferenceAs(inner);
    }

    [Test]
    public async Task TheFloorIsMeasuredOnTheSolversWidths()
    {
        var output = new PaneNode("out", ["main"]);
        var root = new SplitNode(SplitDirection.Row, [output, new PaneNode("chat", ["chat"])]);
        var width = LayoutSolver.Solve(root, new PaneRect(0, 0, 109, 10))["out"].Width;

        await Assert.That(width).IsEqualTo(InputColumn.MinimumColumns);
        await Assert.That(InputColumn.Target(root, "out", Output, 109)).IsSameReferenceAs(output);
        await Assert.That(InputColumn.Target(root, "out", Output, 107)).IsSameReferenceAs(root);
    }

    [Test]
    public async Task AnUnarrangedAreaAppliesNoFloor()
    {
        var output = new PaneNode("out", ["main"]);
        var root = new SplitNode(SplitDirection.Row, [new PaneNode("chat", ["chat"]), output, new PaneNode("scenes", ["scenes"])]);

        await Assert.That(InputColumn.Target(root, "out", Output, 0)).IsSameReferenceAs(output);
        await Assert.That(InputColumn.Target(root, "out", Output)).IsSameReferenceAs(output);
    }
}
