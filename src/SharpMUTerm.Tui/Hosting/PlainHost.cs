using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Accessibility;
using SharpMUTerm.Core.Presence;
using SharpMUTerm.Audio;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui;

/// <summary>
/// <c>sharpmuterm --plain</c>: the client with no compositor on the terminal, for a screen reader. The app runs
/// unchanged on a headless driver; what reaches the terminal is plain text, a line at a time and in the order it
/// arrived, and what is typed is read a line at a time and sent with ⏎. Blightmud's screen-reader mode and
/// Mudlet's screen-reader setup come to the same thing: a cursor-addressed screen reads as noise.
/// </summary>
internal static class PlainHost
{
    /// <summary>The flag that selects this host.</summary>
    public const string Flag = "--plain";

    /// <summary>The host, writing to <paramref name="output"/>.</summary>
    public static ClientHost Create(TerminalCapabilities capabilities, TextWriter output)
    {
        var (width, height) = TerminalSize();
        return ClientHost.Headless(new HeadlessConsoleDriver(width, height), capabilities) with
        {
            Name = "plain",
            OpenUrl = ExternalBrowser.Open,
            RunsInline = false,
            Audio = () => DesktopAudio.TryCreate(),
            Presence = () => new DiscordIpc(),
            Announcer = new PlainAnnouncer(output),
        };
    }

    /// <summary>
    /// Reads <paramref name="input"/> a line at a time and types each into the command line followed by ⏎. At the
    /// end of input it asks to quit and confirms, the way ⌃Q then Y would. Runs on a thread of its own, started
    /// from the client's loop: a key raised before the loop runs has no listener and is lost.
    /// </summary>
    public static void PumpInput(TextReader input, HeadlessConsoleDriver driver, SharpMUTermApp app)
    {
        var thread = new Thread(() =>
        {
            while (input.ReadLine() is { } line)
            {
                foreach (var c in line)
                {
                    driver.SimulateKeyPress(new ConsoleKeyInfo(c, ConsoleKey.NoName, false, false, false));
                }

                driver.SimulateKeyPress(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));
            }

            driver.SimulateKeyPress(new ConsoleKeyInfo('\x11', ConsoleKey.Q, false, false, true));
            driver.SimulateKeyPress(new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false));
        })
        {
            IsBackground = true,
            Name = "plain input",
        };
        app.OnUiThread(thread.Start);
    }

    /// <summary>The terminal's size when it has one, so NAWS tells a server something true; 80×24 otherwise.</summary>
    private static (int Width, int Height) TerminalSize()
    {
        try
        {
            return (Math.Max(Console.WindowWidth, 40), Math.Max(Console.WindowHeight, 10));
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or InvalidOperationException)
        {
            return (80, 24);
        }
    }
}

/// <summary>
/// Writes announcements as lines of plain text. Output is written as it came; the client's own messages and
/// window changes are marked, so a listener can tell them from the game.
/// </summary>
internal sealed class PlainAnnouncer(TextWriter output) : IAnnouncer
{
    private readonly object _gate = new();

    public void Announce(string text, AnnouncementKind kind)
    {
        var line = kind switch
        {
            AnnouncementKind.Notice => "[client] " + text,
            AnnouncementKind.Focus => "[window] " + text,
            _ => text,
        };

        lock (_gate)
        {
            output.WriteLine(line);
            output.Flush();
        }
    }
}
