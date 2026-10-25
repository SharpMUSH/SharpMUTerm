using System.Diagnostics;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>Helpers for tests that run the client's real loop on this project's driver.</summary>
internal static class LiveLoop
{
    /// <summary>Waits until what is drawn stops changing. Frames keep arriving (the caret), so their count cannot say.</summary>
    internal static void Settle(AvaloniaConsoleDriver driver)
    {
        var clock = Stopwatch.StartNew();
        var last = "";
        var quietSince = clock.Elapsed;
        while (clock.Elapsed < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(50);
            var text = driver.FrameCount == 0 ? "" : string.Join('\n', ClientResizeTests.Rows(driver.Frame));
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
