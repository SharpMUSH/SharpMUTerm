using System.Diagnostics;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// Maximising and restoring is two resizes, grow then shrink, arriving from the window while the client's loop
/// runs. Whatever size the client ends at, its frame must be the one it draws when it starts at that size.
/// </summary>
public class ClientResizeTests
{
    [Test]
    public async Task GrowingThenShrinkingBackDrawsWhatStartingAtThatSizeDraws()
    {
        var fresh = Rows(Run(93, 24, driver => { }));
        var cycled = Rows(Run(93, 24, driver =>
        {
            driver.Resize(145, 40);
            Settle(driver);
            driver.Resize(93, 24);
        }));

        await Assert.That(string.Join('\n', cycled)).IsEqualTo(string.Join('\n', fresh));
    }

    /// <summary>The client's rows when settled, then again after <paramref name="act"/>.</summary>
    internal static (ScreenFrame Before, ScreenFrame After) RunPair(int columns, int rows, Action<AvaloniaConsoleDriver> act)
    {
        ScreenFrame? before = null;
        var after = Run(columns, rows, driver =>
        {
            before = driver.Frame;
            act(driver);
        });
        return (before!, after);
    }

    private static ScreenFrame Run(int columns, int rows, Action<AvaloniaConsoleDriver> act)
    {
        var options = WindowOptions.Parse(["--demo-config", "--size", $"{columns}x{rows}"]);
        var config = ClientLaunch.LoadConfiguration(options, []);
        var driver = new AvaloniaConsoleDriver(columns, rows);
        var app = ClientLaunch.Demo(options, config, driver, clipboard: null);
        new Thread(() => app.Run([])) { IsBackground = true }.Start();
        Settle(driver);
        act(driver);
        Settle(driver);
        return driver.Frame;
    }

    internal static string[] Rows(ScreenFrame frame) =>
        Enumerable.Range(0, frame.Height).Select(frame.RowText).ToArray();

    /// <summary>Waits until what is drawn stops changing. Frames keep arriving (the caret), so their count cannot say.</summary>
    private static void Settle(AvaloniaConsoleDriver driver)
    {
        var clock = Stopwatch.StartNew();
        var last = "";
        var quietSince = clock.Elapsed;
        while (clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(50);
            var text = driver.FrameCount == 0 ? "" : string.Join('\n', Rows(driver.Frame));
            if (text != last)
            {
                last = text;
                quietSince = clock.Elapsed;
            }
            else if (text.Length > 0 && clock.Elapsed - quietSince > TimeSpan.FromMilliseconds(700))
            {
                return;
            }
        }
    }
}
