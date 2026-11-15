using System.Text.Json;
using Avalonia;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The window opens where it was last closed: the same pixels on the desktop, kept on the screens there are now, at the
/// scaling of the screen it lands on, and maximised again if it was left maximised.
/// </summary>
public class WindowPlacementTests
{
    private static readonly PixelRect Left = new(0, 0, 1920, 1040);
    private static readonly PixelRect Right = new(1920, 0, 2560, 1400);

    [Test]
    public async Task AWindowLeftOnAScreenComesBackWhereItWasAtThatScreensScaling()
    {
        var saved = new WindowPlacement(2100, 120, 1500, 900);
        var at = WindowFit.Remembered(saved, [(Left, 1.0), (Right, 1.5)]);

        await Assert.That(at).IsNotNull();
        await Assert.That(at!.Value.Position).IsEqualTo(new PixelPoint(2100, 120));
        await Assert.That(at.Value.Size).IsEqualTo(new Size(1000, 600)).Because("1,500 × 900 pixels at 150% are 1,000 × 600 logical");
    }

    [Test]
    public async Task AWindowLeftOnAScreenSinceUnpluggedOpensOnOneThereIs()
    {
        var saved = new WindowPlacement(2100, 120, 1500, 900);
        var at = WindowFit.Remembered(saved, [(Left, 1.25)]);

        await Assert.That(at).IsNotNull();
        var right = at!.Value.Position.X + (at.Value.Size.Width * 1.25);
        await Assert.That(right).IsLessThanOrEqualTo(Left.Right + WindowFit.RememberedSlack);
        await Assert.That(at.Value.Position.X).IsGreaterThanOrEqualTo(Left.X - WindowFit.RememberedSlack);
    }

    [Test]
    public async Task AWindowLargerThanItsScreenIsShrunkToIt()
    {
        var saved = new WindowPlacement(0, 0, 3000, 2000);
        var at = WindowFit.Remembered(saved, [(Left, 1.0)]);

        await Assert.That(at!.Value.Size.Width).IsLessThanOrEqualTo(Left.Width + (2 * WindowFit.RememberedSlack));
        await Assert.That(at.Value.Size.Height).IsLessThanOrEqualTo(Left.Height + (2 * WindowFit.RememberedSlack));
    }

    [Test]
    public async Task AWindowSnappedToAnEdgeDoesNotCreepInwardEachRun()
    {
        // Windows puts a snapped window's invisible resize border outside the working area.
        var saved = new WindowPlacement(-7, 0, 967, 1047);
        var at = WindowFit.Remembered(saved, [(Left, 1.0)]);
        await Assert.That(at!.Value.Position).IsEqualTo(new PixelPoint(-7, 0));
    }

    [Test]
    public async Task NothingUsableSavedOpensAtTheGivenSize()
    {
        await Assert.That(WindowFit.Remembered(null, [(Left, 1.0)])).IsNull();
        await Assert.That(WindowFit.Remembered(new WindowPlacement(0, 0, 10, 10), [(Left, 1.0)])).IsNull();
        await Assert.That(WindowFit.Remembered(new WindowPlacement(0, 0, 800, 600), [])).IsNull();
    }

    [Test]
    public async Task AMaximisedWindowRecordsTheBoundsItRestoresTo()
    {
        var normal = new PixelRect(100, 80, 1200, 800);
        await Assert.That(MainWindow.PlacementFor(normal, maximised: true, saved: null))
            .IsEqualTo(new WindowPlacement(100, 80, 1200, 800, Maximised: true));

        // Opened maximised: the bounds it was saved with stay, and only the state changes.
        var saved = new WindowPlacement(10, 20, 900, 700);
        await Assert.That(MainWindow.PlacementFor(null, maximised: true, saved)).IsEqualTo(saved with { Maximised = true });
        await Assert.That(MainWindow.PlacementFor(null, maximised: false, saved: null)).IsNull();
    }

    [Test]
    public async Task ThePlacementIsSavedAndReadBack()
    {
        var config = new AppConfiguration { Window = new WindowPlacement(-1280, 40, 1200, 800, Maximised: true) };
        var json = JsonSerializer.Serialize(config, ConfigurationStore.SerializerOptions);
        var back = JsonSerializer.Deserialize<AppConfiguration>(json, ConfigurationStore.SerializerOptions)!;
        await Assert.That(back.Window).IsEqualTo(config.Window);

        var none = JsonSerializer.Serialize(new AppConfiguration(), ConfigurationStore.SerializerOptions);
        await Assert.That(none).DoesNotContain("\"window\"").Because("nothing is written until a window has been opened");
    }

    [Test]
    public async Task MovingTheWindowIsNotASettingsEditToUndo()
    {
        var config = new AppConfiguration();
        var history = new SettingsHistory(config);
        var before = history.Capture();
        config.Window = new WindowPlacement(0, 0, 800, 600);
        config.Text.TabWidth = config.Text.TabWidth + 1;
        history.Record("text", before, "tab width");
        history.Undo("text");

        await Assert.That(config.Window).IsEqualTo(new WindowPlacement(0, 0, 800, 600));
    }

    [Test]
    public async Task SizeOnTheCommandLineIsForThisRun()
    {
        await Assert.That(WindowOptions.Parse(["--size", "100x30"]).SizeGiven).IsTrue();
        await Assert.That(WindowOptions.Parse([]).SizeGiven).IsFalse();
    }
}
