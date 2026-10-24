using System.Reflection;
using SharpConsoleUI.Drivers;

namespace SharpMUTerm.Tui;

/// <summary>
/// Captures the ANSI a <see cref="HeadlessConsoleDriver"/> composes for one frame — the snapshot and
/// every frame-reading test depend on it.
/// <para>
/// Through 2.5.x the driver's <see cref="ConsoleBuffer"/> wrote each frame to <c>Console.Out</c>, so
/// redirecting that for one render was the capture. 2.6 stopped the leak (<c>19c3810f</c>, "stop
/// writing rendered frames to the process's stdout") by setting <see cref="ConsoleBuffer.SuppressConsoleOutput"/>,
/// and its <c>OutputHistory</c> is documented as no longer populated, so there is no public way left to
/// read a frame. The buffer is held in a private field; it is lifted for exactly the one render whose
/// output is redirected into a <see cref="StringWriter"/>, and put back, so the driver's guarantee
/// holds everywhere else. If the field is renamed the capture comes back empty and every frame test
/// fails loudly on "too few painted cells" rather than passing against nothing.
/// </para>
/// </summary>
internal static class HeadlessFrameCapture
{
    private static readonly FieldInfo? BufferField =
        typeof(HeadlessConsoleDriver).GetField("_consoleBuffer", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Runs <paramref name="render"/> and returns what the driver wrote while it ran.</summary>
    public static string Capture(IConsoleDriver driver, Action render)
    {
        var buffer = driver is HeadlessConsoleDriver ? BufferField?.GetValue(driver) as ConsoleBuffer : null;
        var real = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            if (buffer is not null)
            {
                buffer.SuppressConsoleOutput = false;
            }

            render();
        }
        finally
        {
            if (buffer is not null)
            {
                buffer.SuppressConsoleOutput = true;
            }

            Console.SetOut(real);
        }

        return writer.ToString();
    }
}
