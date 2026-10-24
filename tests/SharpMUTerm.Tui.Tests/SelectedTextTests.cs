using System.Drawing;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using Color = SharpConsoleUI.Color;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// <see cref="SelectedText"/> over a bare control, so each wrap is placed exactly: the app-level pin is in
/// <c>PaneSelectionTests</c>, and this is where the run lengths and the edges of a selection are held.
/// </summary>
public class SelectedTextTests
{
    /// <summary>A control holding <paramref name="lines"/>, painted at <paramref name="width"/> so it wraps.</summary>
    private static MarkupControl Painted(int width, params string[] lines)
    {
        var control = new MarkupControl(lines.ToList()) { EnableSelection = true };
        var rect = new LayoutRect(0, 0, width, 20);
        control.PaintDOM(new CharacterBuffer(width, 20), rect, rect, Color.White, Color.Black);
        return control;
    }

    private static void Drag(MarkupControl control, int fromX, int fromY, int toX, int toY)
    {
        Send([MouseFlags.Button1Pressed], fromX, fromY);
        Send([MouseFlags.Button1Pressed, MouseFlags.Button1Dragged], toX, toY);
        Send([MouseFlags.Button1Released], toX, toY);

        void Send(List<MouseFlags> flags, int x, int y) =>
            control.ProcessMouseEvent(new MouseEventArgs(flags, new Point(x, y), new Point(x, y), new Point(x, y)));
    }

    [Test]
    public async Task TheFrameworkAloneClosesUpAWrap()
    {
        // The defect this type exists for, stated against the framework so a release that fixes it says so.
        var control = Painted(6, "alpha beta gamma");
        Drag(control, 0, 0, 99, 99);

        await Assert.That(control.GetSelectedText()).IsEqualTo("alphabetagamma");
    }

    [Test]
    public async Task EverySpaceAWrapTookIsPutBack()
    {
        var control = Painted(6, "alpha beta gamma");
        Drag(control, 0, 0, 99, 99);

        await Assert.That(SelectedText.Of(control)).IsEqualTo("alpha beta gamma");
    }

    [Test]
    public async Task ARunOfSpacesComesBackWhole()
    {
        var control = Painted(10, "alpha    beta");
        Drag(control, 0, 0, 99, 99);

        await Assert.That(SelectedText.Of(control)).IsEqualTo("alpha    beta");
    }

    [Test]
    public async Task AHardBreakMidWordAddsNothing()
    {
        // No space to break at: the row is cut at the width and nothing is consumed.
        var control = Painted(5, "abcdefghij");
        Drag(control, 0, 0, 99, 99);

        await Assert.That(SelectedText.Of(control)).IsEqualTo("abcdefghij");
    }

    [Test]
    public async Task LinesStayLinesAndOnlyTheWrapsAreJoined()
    {
        var control = Painted(6, "alpha beta gamma", "one two three");
        Drag(control, 0, 0, 99, 99);

        await Assert.That(SelectedText.Of(control)).IsEqualTo("alpha beta gamma\none two three");
    }

    [Test]
    public async Task ASelectionStartingAtTheEndOfARowKeepsTheSpaceBeforeTheNext()
    {
        // "alpha" | "beta" | "gamma": from just past "alpha" to the end of "beta".
        var control = Painted(6, "alpha beta gamma");
        Drag(control, 5, 0, 4, 1);

        await Assert.That(SelectedText.Of(control)).IsEqualTo(" beta");
    }

    [Test]
    public async Task ASelectionInsideOneRowIsTheFrameworksOwn()
    {
        var control = Painted(6, "alpha beta gamma");
        Drag(control, 1, 1, 3, 1);

        await Assert.That(SelectedText.Of(control)).IsEqualTo(control.GetSelectedText());
        await Assert.That(SelectedText.Of(control)).IsEqualTo("et");
    }

    [Test]
    public async Task NothingSelectedIsEmpty()
    {
        var control = Painted(6, "alpha beta gamma");

        await Assert.That(SelectedText.Of(control)).IsEmpty();
    }
}
