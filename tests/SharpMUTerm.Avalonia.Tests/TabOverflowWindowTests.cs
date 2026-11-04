using SharpConsoleUI.Drivers;
using static SharpMUTerm.Avalonia.Tests.DesktopLayerTests;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The tab strip's <c>+N ▾</c> chip in the window: a click on it reaches the client through the same driver handler a
/// terminal's does, and what the framework — subscribed after the client, as it is inside <c>Run()</c> — sees of the same
/// frame carries no button, so the tab drawn under the chip is neither selected nor closed.
/// </summary>
[NotInParallel]
public class TabOverflowWindowTests
{
    [Test]
    public async Task InTheWindowAClickOnTheChipLeavesTheFrameworkNoClick()
    {
        using var driver = new AvaloniaConsoleDriver(56, 20);
        await using var app = InlineWindowApp(driver);
        app.RenderSnapshot("tabs-many");
        var (paneId, _) = app.TabOverflowMenus.Single();
        var chip = app.TabOverflowChipAt(paneId)!.Value;

        // Stands where the framework stands: a later subscriber to the driver's mouse stream.
        var frameworkSaw = new List<MouseFlags>();
        driver.MouseEvent += (_, flags, _) => frameworkSaw.AddRange(flags);
        var at = new System.Drawing.Point(chip.X + 1, chip.Y);
        driver.RaiseMouse([MouseFlags.Button1Pressed], at);
        driver.RaiseMouse([MouseFlags.Button1Released, MouseFlags.Button1Clicked], at);

        await Assert.That(frameworkSaw.Any(f => f.ToString().StartsWith("Button1", StringComparison.Ordinal))).IsFalse();
        await Assert.That(app.AChipClickReachesTheTabUnderIt(paneId, filtered: true)).IsFalse();
    }
}
