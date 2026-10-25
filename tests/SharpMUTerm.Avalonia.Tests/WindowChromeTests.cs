using System.Text;
using Avalonia;
using Avalonia.Controls;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>Where an undecorated window is taken hold of: the header's blank cells to move, the edges to resize.</summary>
public class WindowChromeTests
{
    [Test]
    public async Task OnlyABlankCellOfTheTopRowMovesTheWindow()
    {
        var frame = new CellGrid(4, 2);
        frame.Set(1, 0, GridCell.Blank with { Rune = new Rune('m') });
        var snapshot = frame.Snapshot(default);
        await Assert.That(WindowChrome.IsMoveHandle(snapshot, new(0, 0))).IsTrue();
        await Assert.That(WindowChrome.IsMoveHandle(snapshot, new(1, 0))).IsFalse();
        await Assert.That(WindowChrome.IsMoveHandle(snapshot, new(0, 1))).IsFalse();
    }

    [Test]
    public async Task TheEdgesAndCornersResize()
    {
        var size = new Size(800, 600);
        await Assert.That(WindowChrome.EdgeAt(new Point(1, 300), size)).IsEqualTo(WindowEdge.West);
        await Assert.That(WindowChrome.EdgeAt(new Point(799, 300), size)).IsEqualTo(WindowEdge.East);
        await Assert.That(WindowChrome.EdgeAt(new Point(400, 599), size)).IsEqualTo(WindowEdge.South);
        await Assert.That(WindowChrome.EdgeAt(new Point(1, 1), size)).IsEqualTo(WindowEdge.NorthWest);
        await Assert.That(WindowChrome.EdgeAt(new Point(799, 599), size)).IsEqualTo(WindowEdge.SouthEast);
        await Assert.That(WindowChrome.EdgeAt(new Point(400, 300), size)).IsNull();
    }
}
