using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Tui;
using static SharpMUTerm.Avalonia.Tests.DesktopLayerTests;

namespace SharpMUTerm.Avalonia.Tests;

/// <summary>
/// The settings window draws its own frame (title, ✕, Esc, drag to move), so it gets no resize border from the platform:
/// it carries its own bands along each edge, as the borderless main window does, and the size the player leaves it at is
/// the size it opens at for the rest of the run. And it is a window of its own, so opening or closing it re-checks the
/// main window's toasts at once.
/// </summary>
[NotInParallel]
public class SettingsWindowFrameTests
{
    private static SettingsForm Form(AppConfiguration config, string section = "text", bool beside = false) =>
        new(BuiltInSettings.All, config, c => c(), _ => { }, section, isDark: true) { BesideOutput = beside };

    [Test]
    public async Task TheFramelessSettingsWindowHasAResizeBandAlongEveryEdge()
    {
        var (resizable, bands, cursors) = await TerminalViewTests.Session.Dispatch(() =>
        {
            var presenter = new SettingsPresenter();
            var main = new Window { Content = new Panel(), Width = 1400, Height = 900 };
            presenter.Owner = main;
            main.Show();
            try
            {
                var window = presenter.ShowNow(Form(new AppConfiguration()));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var size = window.ClientSize;
                var bands = window.GetVisualDescendantsOf<Border>().Count(b => Equals(b.Tag, WindowChrome.ResizeBandTag));

                // What lies under each edge is a band, so a press there resizes rather than reaching the page.
                var cursors = new[]
                {
                    new Point(size.Width / 2, 1), new Point(size.Width / 2, size.Height - 1),
                    new Point(1, size.Height / 2), new Point(size.Width - 1, size.Height / 2),
                }.Select(p => window.InputHitTest(p) is Border { Tag: WindowChrome.ResizeBandTag }).ToArray();
                window.Close();
                return (window.CanResize, bands, cursors);
            }
            finally
            {
                main.Close();
            }
        }, CancellationToken.None);

        await Assert.That(resizable).IsTrue();
        await Assert.That(bands).IsEqualTo(4);
        await Assert.That(cursors.All(c => c)).IsTrue().Because("every edge is a resize band");
        await Assert.That(WindowChrome.EdgeAt(new Point(1, 1), new Size(800, 600))).IsEqualTo(WindowEdge.NorthWest);
    }

    [Test]
    public async Task TheSizeThePlayerLeavesItAtIsTheSizeItReopensAtButDockingBesideTheOutputIsNotRemembered()
    {
        var (reopened, afterDock, remembered) = await TerminalViewTests.Session.Dispatch(() =>
        {
            var presenter = new SettingsPresenter();
            var main = new Window { Content = new Panel(), Width = 1400, Height = 900 };
            presenter.Owner = main;
            main.Show();
            var config = new AppConfiguration();
            try
            {
                var window = presenter.ShowNow(Form(config));
                Dispatcher.UIThread.RunJobs();
                window.Width = 1100;
                window.Height = 720;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                window.Close();
                Dispatcher.UIThread.RunJobs();

                var again = presenter.ShowNow(Form(config));
                Dispatcher.UIThread.RunJobs();
                var reopened = new Size(again.Width, again.Height);
                again.Close();
                Dispatcher.UIThread.RunJobs();

                var beside = presenter.ShowNow(Form(config, "triggers", beside: true));
                Dispatcher.UIThread.RunJobs();
                beside.Close();
                Dispatcher.UIThread.RunJobs();
                var after = presenter.ShowNow(Form(config));
                Dispatcher.UIThread.RunJobs();
                var afterDock = new Size(after.Width, after.Height);
                after.Close();
                return (reopened, afterDock, presenter.RememberedSize);
            }
            finally
            {
                main.Close();
            }
        }, CancellationToken.None);

        await Assert.That(reopened).IsEqualTo(new Size(1100, 720));
        await Assert.That(afterDock).IsEqualTo(new Size(1100, 720)).Because("docked beside the output is the main window's half, not the player's size");
        await Assert.That(remembered).IsEqualTo(new Size(1100, 720));
    }

    /// <summary>
    /// A toast up when the settings window opens goes at once, and comes back when it closes — without waiting for the
    /// client to paint a frame, which opening another window does not cause.
    /// </summary>
    [Test]
    public async Task ToastsHideTheMomentTheSettingsWindowOpensAndReturnWhenItCloses()
    {
        var (before, whileOpen, after) = await Run((window, view, layer, driver) =>
        {
            driver.Settings.Owner = window;
            layer.ToastsHidden = () => driver.Settings.Window is not null;
            driver.Settings.OpenedOrClosed = layer.Sync;
            var palette = new NoticePalette("#333344", "#e0e0e0", "#5fafff", "#ff6b6b", "#ffaf5f");
            ((IPointerPresenter)driver.Pointer).Notice(new NoticeOverlay(
                new NoticeModel("saved", NoticeSeverity.Info, null, null, DateTimeOffset.UnixEpoch, 1), 8, palette, _ => { }));
            Drain();
            var before = layer.Toasts.IsVisible;
            var settings = driver.Settings.ShowNow(Form(new AppConfiguration()));
            Drain();
            var whileOpen = layer.Toasts.IsVisible;
            settings.Close();
            Drain();
            return (before, whileOpen, layer.Toasts.IsVisible);
        }, columns: 80, rows: 20);

        await Assert.That(before).IsTrue();
        await Assert.That(whileOpen).IsFalse().Because("nothing of a toast shows beside the settings window");
        await Assert.That(after).IsTrue();
    }
}
