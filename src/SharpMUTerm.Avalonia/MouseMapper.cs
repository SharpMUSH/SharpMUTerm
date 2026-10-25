using System.Drawing;
using SharpConsoleUI.Drivers;

namespace SharpMUTerm.Avalonia;

/// <summary>Which button a pointer event is about.</summary>
internal enum PointerButton
{
    None,
    Left,
    Middle,
    Right,
}

/// <summary>Modifier keys held during a pointer event.</summary>
[Flags]
internal enum PointerModifiers
{
    None = 0,
    Shift = 1,
    Alt = 2,
    Ctrl = 4,
}

/// <summary>
/// Pointer events, in cells, as the mouse flag lists SharpConsoleUI's Unix reader dispatches — including the
/// clicks it <em>synthesises</em>, because a terminal reports only presses and releases and the framework's
/// controls key off <c>Button1Clicked</c>, <c>Button1DoubleClicked</c> and <c>Button1TripleClicked</c>.
/// <para>
/// The shapes are that reader's (<c>UnixStdinReader.ProcessMouseEvent</c>): a press is the bare
/// <c>ButtonNPressed</c>; motion with the button held is <c>Pressed | Dragged | ReportMousePosition</c>, the
/// form the client's drag paths were written for; a release where the press was is <c>Released</c> then
/// <c>Clicked</c>; a second or third press is the double or triple click in place of the press. The count
/// comes from the window system, which knows the platform's double-click time, rather than from a timer
/// here. Modifiers lead the list, as they do there.
/// </para>
/// </summary>
internal sealed class MouseMapper
{
    private PointerButton _held;
    private Point _pressedAt;
    private Point _lastAt = new(-1, -1);
    private int _clickCount;

    /// <summary>Whether a button is down — while it is, the window repeats the press (see <see cref="Repeat"/>).</summary>
    public bool IsHeld => _held != PointerButton.None;

    public List<MouseFlags> Pressed(PointerButton button, Point cell, int clickCount, PointerModifiers modifiers)
    {
        _held = button;
        _pressedAt = cell;
        _lastAt = cell;
        _clickCount = clickCount;
        var flag = clickCount switch
        {
            2 => Flag(button, MouseFlags.Button1DoubleClicked, MouseFlags.Button2DoubleClicked, MouseFlags.Button3DoubleClicked),
            >= 3 => Flag(button, MouseFlags.Button1TripleClicked, MouseFlags.Button2TripleClicked, MouseFlags.Button3TripleClicked),
            _ => Flag(button, MouseFlags.Button1Pressed, MouseFlags.Button2Pressed, MouseFlags.Button3Pressed),
        };
        return With(modifiers, flag);
    }

    /// <summary>Motion. Null when the pointer has not left the cell it was in, which a terminal would not report.</summary>
    public List<MouseFlags>? Moved(Point cell, PointerModifiers modifiers)
    {
        if (cell == _lastAt)
        {
            return null;
        }

        _lastAt = cell;
        if (_held == PointerButton.None)
        {
            return With(modifiers, MouseFlags.ReportMousePosition);
        }

        return With(
            modifiers,
            Flag(_held, MouseFlags.Button1Pressed, MouseFlags.Button2Pressed, MouseFlags.Button3Pressed),
            Flag(_held, MouseFlags.Button1Dragged, MouseFlags.Button2Dragged, MouseFlags.Button3Dragged),
            MouseFlags.ReportMousePosition);
    }

    public List<MouseFlags>? Released(PointerButton button, Point cell, PointerModifiers modifiers)
    {
        if (_held == PointerButton.None || button != _held)
        {
            return null;
        }

        var released = Flag(button, MouseFlags.Button1Released, MouseFlags.Button2Released, MouseFlags.Button3Released);
        var click = cell == _pressedAt && _clickCount <= 1;
        _held = PointerButton.None;
        return click
            ? With(modifiers, released, Flag(button, MouseFlags.Button1Clicked, MouseFlags.Button2Clicked, MouseFlags.Button3Clicked))
            : With(modifiers, released);
    }

    /// <summary>
    /// The reader re-raises a bare press at the pointer's cell every 100 ms while a button is held, and the
    /// framework's drag-to-scroll leans on it; the window calls this on the same interval.
    /// </summary>
    public (List<MouseFlags> Flags, Point Cell)? Repeat()
    {
        if (_held == PointerButton.None)
        {
            return null;
        }

        return ([Flag(_held, MouseFlags.Button1Pressed, MouseFlags.Button2Pressed, MouseFlags.Button3Pressed)], _lastAt);
    }

    /// <summary>
    /// Whole wheel notches in <paramref name="delta"/> plus what earlier events left over, signed (up is positive);
    /// the fraction is kept in <paramref name="remainder"/>. A precision touchpad sends a stream of small deltas,
    /// and counting each as a notch scrolled several rows for every hundredth of one. A reversal starts afresh.
    /// </summary>
    public static int WheelNotches(ref double remainder, double delta)
    {
        if (delta == 0)
        {
            return 0;
        }

        if (Math.Sign(remainder) == -Math.Sign(delta))
        {
            remainder = 0;
        }

        remainder += delta;
        var notches = (int)Math.Truncate(remainder);
        remainder -= notches;
        return notches;
    }

    public static List<MouseFlags> Wheel(bool up, PointerModifiers modifiers) =>
        With(modifiers, up ? MouseFlags.WheeledUp : MouseFlags.WheeledDown);

    private static MouseFlags Flag(PointerButton button, MouseFlags left, MouseFlags middle, MouseFlags right) => button switch
    {
        PointerButton.Middle => middle,
        PointerButton.Right => right,
        _ => left,
    };

    private static List<MouseFlags> With(PointerModifiers modifiers, params MouseFlags[] flags)
    {
        var list = new List<MouseFlags>(flags.Length + 3);
        if ((modifiers & PointerModifiers.Shift) != 0) list.Add(MouseFlags.ButtonShift);
        if ((modifiers & PointerModifiers.Alt) != 0) list.Add(MouseFlags.ButtonAlt);
        if ((modifiers & PointerModifiers.Ctrl) != 0) list.Add(MouseFlags.ButtonCtrl);
        list.AddRange(flags);
        return list;
    }
}
