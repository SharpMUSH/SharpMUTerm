using System.Drawing;
using SharpConsoleUI.Drivers;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>The flag lists are UnixStdinReader's, clicks included, because the framework's controls key off them.</summary>
public class MouseMapperTests
{
    [Test]
    public async Task APressAndAReleaseInOneCellIsAClick()
    {
        var mouse = new MouseMapper();
        var press = mouse.Pressed(PointerButton.Left, new Point(3, 4), 1, PointerModifiers.None);
        var release = mouse.Released(PointerButton.Left, new Point(3, 4), PointerModifiers.None);
        await Assert.That(press).IsEquivalentTo(new[] { MouseFlags.Button1Pressed });
        await Assert.That(release!).IsEquivalentTo(new[] { MouseFlags.Button1Released, MouseFlags.Button1Clicked });
    }

    [Test]
    public async Task AReleaseInAnotherCellIsNotAClick()
    {
        var mouse = new MouseMapper();
        mouse.Pressed(PointerButton.Left, new Point(3, 4), 1, PointerModifiers.None);
        var release = mouse.Released(PointerButton.Left, new Point(9, 4), PointerModifiers.None);
        await Assert.That(release!).IsEquivalentTo(new[] { MouseFlags.Button1Released });
    }

    [Test]
    public async Task MotionWithTheButtonHeldIsTheDragShape()
    {
        var mouse = new MouseMapper();
        mouse.Pressed(PointerButton.Left, new Point(3, 4), 1, PointerModifiers.None);
        var drag = mouse.Moved(new Point(5, 4), PointerModifiers.None);
        await Assert.That(drag!).IsEquivalentTo(new[] { MouseFlags.Button1Pressed, MouseFlags.Button1Dragged, MouseFlags.ReportMousePosition });
    }

    [Test]
    public async Task MotionInsideOneCellIsNotReported()
    {
        var mouse = new MouseMapper();
        await Assert.That(mouse.Moved(new Point(1, 1), PointerModifiers.None)).IsNotNull();
        await Assert.That(mouse.Moved(new Point(1, 1), PointerModifiers.None)).IsNull();
    }

    [Test]
    public async Task SecondAndThirdPressesAreDoubleAndTripleClicks()
    {
        var mouse = new MouseMapper();
        await Assert.That(mouse.Pressed(PointerButton.Left, new Point(0, 0), 2, PointerModifiers.None))
            .IsEquivalentTo(new[] { MouseFlags.Button1DoubleClicked });
        mouse.Released(PointerButton.Left, new Point(0, 0), PointerModifiers.None);
        await Assert.That(mouse.Pressed(PointerButton.Left, new Point(0, 0), 3, PointerModifiers.None))
            .IsEquivalentTo(new[] { MouseFlags.Button1TripleClicked });

        // A double click's release is not another click.
        await Assert.That(mouse.Released(PointerButton.Left, new Point(0, 0), PointerModifiers.None)!)
            .IsEquivalentTo(new[] { MouseFlags.Button1Released });
    }

    [Test]
    public async Task ModifiersLeadTheList()
    {
        var mouse = new MouseMapper();
        var press = mouse.Pressed(PointerButton.Right, new Point(0, 0), 1, PointerModifiers.Ctrl | PointerModifiers.Shift);
        await Assert.That(press).IsEquivalentTo(new[] { MouseFlags.ButtonShift, MouseFlags.ButtonCtrl, MouseFlags.Button3Pressed });
    }

    [Test]
    public async Task AHeldButtonRepeatsAsABarePress()
    {
        var mouse = new MouseMapper();
        await Assert.That(mouse.Repeat()).IsNull();
        mouse.Pressed(PointerButton.Left, new Point(2, 2), 1, PointerModifiers.None);
        mouse.Moved(new Point(6, 2), PointerModifiers.None);
        var repeat = mouse.Repeat();
        await Assert.That(repeat!.Value.Flags).IsEquivalentTo(new[] { MouseFlags.Button1Pressed });
        await Assert.That(repeat.Value.Cell).IsEqualTo(new Point(6, 2));
    }
}
