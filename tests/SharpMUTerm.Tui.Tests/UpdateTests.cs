using SharpConsoleUI.Drivers;
using SharpMUTerm.Client.Commands;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Graphics;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// Updating an installed copy: ⌃P ▸ Check for updates finds a release and says so with an Update action, Update
/// downloads it, and the next Update ends the loop with the release handed to <see cref="Program"/> to put in place.
/// A copy the host cannot update says so and offers neither entry.
/// </summary>
[NotInParallel]
public class UpdateTests
{
    private static readonly TerminalCapabilities Caps =
        new(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);

    private sealed class FakeUpdates(string? restartNote = null) : IUpdates
    {
        public int Downloads;

        public string CurrentVersion => "1.0.0";

        public bool Relaunches => true;

        public string? RestartNote => restartNote;

        public Task<UpdateOffer?> CheckAsync(bool prereleases, CancellationToken cancel) =>
            Task.FromResult<UpdateOffer?>(new UpdateOffer("1.1.0", new object()));

        public Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken cancel)
        {
            Downloads++;
            progress(50);
            progress(100);
            return Task.CompletedTask;
        }

        public string? ApplyAfterExit(UpdateOffer offer) => null;
    }

    private static SharpMUTermApp App(IUpdates? updates)
    {
        Console.SetIn(TextReader.Null);
        return new SharpMUTermApp(new AppConfiguration(), ClientHost.Headless(new HeadlessConsoleDriver(120, 32), Caps) with { Updates = updates });
    }

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        if (!condition())
        {
            throw new TimeoutException("The condition did not hold within two seconds.");
        }
    }

    [Test]
    public async Task ACheckFindsTheReleaseDownloadsItAndRestartsIntoIt()
    {
        var updates = new FakeUpdates();
        await using var app = App(updates);

        await Assert.That(app.DispatchCommand(BuiltInCommands.CheckUpdatesCommand)).IsTrue();
        await Until(() => app.LastNotice?.Contains("1.1.0 is out") == true);
        await Assert.That(app.LastNotice).Contains("SharpMUTerm 1.1.0 is out (you have 1.0.0)");
        await Assert.That(app.LastNoticeModel?.Action?.Command).IsEqualTo(BuiltInCommands.UpdateCommand);

        await Assert.That(app.DispatchCommand(BuiltInCommands.UpdateCommand)).IsTrue();
        await Until(() => app.LastNotice?.Contains("ready") == true);
        await Assert.That(updates.Downloads).IsEqualTo(1);
        await Assert.That(app.LastNotice).Contains("SharpMUTerm 1.1.0 is ready; restart to use it");
        await Assert.That(app.UpdateToApply).IsNull().Because("nothing is put in place until the player restarts");

        // Nothing would be lost and the host has nothing to add, so it restarts at once.
        await Assert.That(app.DispatchCommand(BuiltInCommands.UpdateCommand)).IsTrue();
        await Assert.That(app.QuitPromptOpen).IsFalse();
        await Assert.That(app.UpdateToApply?.Version).IsEqualTo("1.1.0");
    }

    [Test]
    public async Task ARestartTheHostHasSomethingToSayAboutAsksFirst()
    {
        await using var app = App(new FakeUpdates("Any SharpMUTerm terminal still open closes too."));
        app.DispatchCommand(BuiltInCommands.CheckUpdatesCommand);
        await Until(() => app.LastNotice?.Contains("is out") == true);
        app.DispatchCommand(BuiltInCommands.UpdateCommand);
        await Until(() => app.LastNotice?.Contains("ready") == true);

        app.DispatchCommand(BuiltInCommands.UpdateCommand);

        await Assert.That(app.QuitPromptOpen).IsTrue();
        await Assert.That(app.QuitPromptModel?.Heading).IsEqualTo("Restart to update to SharpMUTerm 1.1.0?");
        await Assert.That(app.QuitPromptModel?.Yes).IsEqualTo("Restart");
        await Assert.That(app.QuitPromptModel?.RestartNote).IsEqualTo("Any SharpMUTerm terminal still open closes too.");
        await Assert.That(app.UpdateToApply).IsNull().Because("the player has not answered");
    }

    [Test]
    public async Task ACopyThatCannotUpdateSaysSoAndOffersNeitherEntry()
    {
        await using var app = App(null);

        await Assert.That(app.DispatchCommand(BuiltInCommands.CheckUpdatesCommand)).IsFalse();
        await Assert.That(app.LastNotice).Contains("cannot update itself");
        var offered = app.BuildCatalog().Select(c => c.Id).ToList();
        await Assert.That(offered).DoesNotContain(BuiltInCommands.CheckUpdatesCommand);
        await Assert.That(offered).DoesNotContain(BuiltInCommands.UpdateCommand);
    }

    [Test]
    public async Task TheQuitQuestionIsAboutRestartingOnlyWhenARestartIsAsked()
    {
        var quit = QuitModel.Nothing;
        await Assert.That(quit.Heading).IsEqualTo("Quit SharpMUTerm?");
        await Assert.That(quit.Yes).IsEqualTo("Quit");
        var restart = quit with { RestartVersion = "2.0.0" };
        await Assert.That(restart.Heading).IsEqualTo("Restart to update to SharpMUTerm 2.0.0?");
        await Assert.That(restart.Yes).IsEqualTo("Restart");
        await Assert.That(restart).IsNotEqualTo(quit);
    }
}
