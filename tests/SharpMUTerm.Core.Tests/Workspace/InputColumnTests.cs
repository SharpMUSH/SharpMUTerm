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
}
