using SharpConsoleUI.Drivers;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Diagnostics;
using SharpMUTerm.Graphics;
using FrameworkLogService = SharpConsoleUI.Logging.LogService;

namespace SharpMUTerm.Tui.Tests;

/// <summary>
/// A failure has to reach the diagnostics file. The framework catches what its loop and its queued UI actions
/// throw and logs it to a log service of its own that writes nowhere, so the window closed with no record of why.
/// </summary>
[NotInParallel]
public class CrashLoggingTests
{
    [Test]
    public async Task TheFrameworksErrorsReachTheClientLog()
    {
        using var diagnostics = ClientDiagnostics.InMemory();
        var framework = new FrameworkLogService();
        using (diagnostics.Forward(framework))
        {
            framework.LogCritical("Unhandled exception in main loop: boom", new InvalidOperationException("boom"), "System");
        }

        var entry = diagnostics.Messages.Entries.Single();
        await Assert.That(entry.Severity).IsEqualTo(MessageSeverity.Error);
        await Assert.That(entry.Text).Contains("Unhandled exception in main loop");
        await Assert.That(entry.Text).Contains("boom");
        await Assert.That(entry.Source).IsEqualTo("SharpConsoleUI");
    }

    [Test]
    public async Task TheFrameworksChatterStaysOut()
    {
        using var diagnostics = ClientDiagnostics.InMemory();
        var framework = new FrameworkLogService { MinimumLevel = SharpConsoleUI.Logging.LogLevel.Trace };
        using (diagnostics.Forward(framework))
        {
            framework.LogDebug("Console window system shutting down");
            framework.LogInfo("started");
        }

        await Assert.That(diagnostics.Messages.Entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task NothingIsForwardedOnceTheSubscriptionIsDisposed()
    {
        using var diagnostics = ClientDiagnostics.InMemory();
        var framework = new FrameworkLogService();
        diagnostics.Forward(framework).Dispose();
        framework.LogError("late");

        await Assert.That(diagnostics.Messages.Entries.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TheAppForwardsItsFrameworksLog()
    {
        using var diagnostics = ClientDiagnostics.InMemory();
        var config = new AppConfiguration();
        var caps = new TerminalCapabilities(GraphicsProtocol.None, supportsTrueColor: true, supportsKittyGraphics: false, supportsSixel: false);
        await using var app = new SharpMUTermApp(
            config,
            ClientHost.Headless(new HeadlessConsoleDriver(80, 24), caps),
            diagnostics: diagnostics);

        app.FrameworkLog.LogError("Error in EnqueueOnUIThread action: boom", new InvalidOperationException("boom"), "System");

        await Assert.That(diagnostics.Messages.Entries.Any(e => e.Text.Contains("EnqueueOnUIThread"))).IsTrue();
    }
}
