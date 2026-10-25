using SharpMUTerm.Core.Workspaces;

namespace SharpMUTerm.Core.Tests.Workspaces;

/// <summary>
/// Dragging a divider with the mouse: the border goes to the cell the pointer is on, as far as the floors
/// allow, and nothing outside the two panes either side of it moves. Asserted on re-solved rectangles,
/// for <see cref="PaneResizeTests"/>' reason.
/// </summary>
public class PaneBorderDragTests
{
    private static (SplitNode Split, WorkspaceLayout Layout) Three(SplitDirection direction)
    {
        var split = new SplitNode(direction, new LayoutNode[]
        {
            new PaneNode("p1", new[] { "w1" }), new PaneNode("p2", new[] { "w2" }), new PaneNode("p3", new[] { "w3" }),
        });
        return (split, new WorkspaceLayout(split, "p1"));
    }

    private static readonly PaneRect Bounds = new(0, 0, 91, 40);

    private static IReadOnlyDictionary<string, PaneRect> Solve(WorkspaceLayout layout) =>
        LayoutSolver.Solve(layout.Root, Bounds, layout.ZoomedPaneId);

    [Test]
    public async Task TheBorderLandsOnTheCellThePointerIsOn()
    {
        var (split, layout) = Three(SplitDirection.Row);
        var before = Solve(layout);

        var result = PaneResize.DragBorder(split, 0, 40, before);
        var after = Solve(layout);

        await Assert.That(result.Outcome).IsEqualTo(PaneResizeOutcome.Resized);
        await Assert.That(after["p1"].X + after["p1"].Width).IsEqualTo(40);
        await Assert.That(after["p2"].X).IsEqualTo(40 + LayoutSolver.DividerThickness);
        await Assert.That(after["p3"]).IsEqualTo(before["p3"]);
    }

    [Test]
    public async Task ABorderDraggedPastAFloorStopsAtIt()
    {
        var (split, layout) = Three(SplitDirection.Column);
        var before = Solve(layout);

        PaneResize.DragBorder(split, 1, 0, before);
        var after = Solve(layout);

        await Assert.That(after["p2"].Height).IsEqualTo(PaneResize.MinimumPaneRows);
        await Assert.That(after["p1"]).IsEqualTo(before["p1"]);
    }

    [Test]
    public async Task ABorderAlreadyAtTheCellReportsNoMove()
    {
        var (split, layout) = Three(SplitDirection.Row);
        var before = Solve(layout);
        var at = before["p1"].X + before["p1"].Width;

        await Assert.That(PaneResize.DragBorder(split, 0, at, before).Changed).IsFalse();
    }

    [Test]
    public async Task EvenGivesEveryChildTheSameShareAgain()
    {
        var (split, layout) = Three(SplitDirection.Row);
        PaneResize.DragBorder(split, 0, 20, Solve(layout));

        await Assert.That(PaneResize.Even(split)).IsTrue();
        await Assert.That(split.Sizes.All(s => Math.Abs(s - (1.0 / 3)) < 1e-9)).IsTrue();
        await Assert.That(PaneResize.Even(split)).IsFalse();
    }
}
