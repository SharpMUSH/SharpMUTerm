using SharpConsoleUI;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Helpers;

namespace SharpMUTerm.Tui;

/// <summary>
/// The few things the snapshot and the tests do to the headless driver's own API: re-arm its buffer for a whole
/// frame, feed it mouse events, read the caret it was handed. Each is a no-op (or null) on any other driver, so
/// the app asks for the effect and never learns which driver it has.
/// </summary>
internal static class HeadlessDriverAccess
{
    /// <summary>Gives a headless driver a fresh buffer, so the next render is emitted whole rather than as a delta.</summary>
    public static void Rearm(IConsoleDriver driver, ConsoleWindowSystem system)
    {
        if (driver is HeadlessConsoleDriver headless)
        {
            headless.Initialize(system);
        }
    }

    /// <summary>
    /// Raises mouse events through a driver that can be handed them — the headless driver, or any driver that is an
    /// <see cref="IMouseInjector"/> — so the <c>drag</c> snapshot draws the same frame in every host. False, and
    /// nothing raised, on a driver that reads a real mouse.
    /// </summary>
    public static bool TrySimulateMouse(IConsoleDriver driver, params (List<MouseFlags> Flags, System.Drawing.Point At)[] events)
    {
        Action<List<MouseFlags>, System.Drawing.Point>? raise = driver switch
        {
            HeadlessConsoleDriver headless => headless.SimulateMouseEvent,
            IMouseInjector injector => injector.InjectMouse,
            _ => null,
        };
        if (raise is null)
        {
            return false;
        }

        foreach (var (flags, at) in events)
        {
            raise(flags, at);
        }

        return true;
    }

    /// <summary>The caret a headless driver was last handed. Throws on any other driver: only a test asks.</summary>
    public static (bool Visible, int X, int Y) Caret(IConsoleDriver driver)
    {
        var headless = (HeadlessConsoleDriver)driver;
        return (headless.CursorVisible, headless.CursorPosition.X, headless.CursorPosition.Y);
    }
}

/// <summary>A driver whose mouse input can be raised from code — a window raises what its pointer did.</summary>
internal interface IMouseInjector
{
    void InjectMouse(List<MouseFlags> flags, System.Drawing.Point cell);
}
