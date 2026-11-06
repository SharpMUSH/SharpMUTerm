using Avalonia.Controls;
using Avalonia.Headless;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>Every window the host opens carries the logo, so an X11 window manager has an icon to show for it.</summary>
public class AppIconTests
{
    [Test]
    public async Task TheLogoIsEmbeddedAndSetOnAWindow()
    {
        var set = await Session.Dispatch(() =>
        {
            var window = new Window();
            AppIcon.Apply(window);
            return AppIcon.Value is not null && ReferenceEquals(window.Icon, AppIcon.Value);
        }, CancellationToken.None);

        await Assert.That(set).IsTrue();
    }

    private static HeadlessUnitTestSession Session => TerminalViewTests.Session;
}
