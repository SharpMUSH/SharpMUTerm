using System.Diagnostics;
using System.Runtime.InteropServices;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace SharpMUTerm.Tui;

/// <summary>
/// <see cref="IUpdates"/> through Velopack, over the GitHub releases this repository publishes. The release workflow
/// attaches each release's Velopack feed (<c>releases.win.json</c>, <c>releases.linux.json</c>) and packages, so the
/// feed is the Releases page itself and nothing else is hosted.
/// <para>
/// Only an installed copy updates: the Windows installer's (<c>%LocalAppData%\SharpMUTerm</c>, with <c>Update.exe</c>
/// beside <c>current\</c>) or the AppImage's (<c>UpdateNix</c> in it, and <c>$APPIMAGE</c> set). A copy unpacked from
/// the <c>.tar.gz</c>, a build run from the repository, and an install by the old Inno Setup installer have neither,
/// and get no updater at all, so nothing is offered that cannot be done.
/// </para>
/// <para>
/// How each host comes back: the window is started again by Velopack's own updater once this process has gone. The
/// terminal on Linux applies the new AppImage itself, then replaces this process with it (<c>execv</c>), so it comes
/// back in the same terminal. The terminal on Windows cannot: a console program's console is not something a new
/// program can be handed, so the update is applied once it has gone and the player starts it again.
/// </para>
/// </summary>
internal sealed class VelopackUpdates : IUpdates
{
    /// <summary>Where the releases are.</summary>
    public const string Repository = "https://github.com/SharpMUSH/SharpMUTerm";

    private readonly IVelopackLocator _locator;
    private readonly bool _window;

    private VelopackUpdates(IVelopackLocator locator, bool window, string version)
    {
        _locator = locator;
        _window = window;
        CurrentVersion = version;
    }

    public string CurrentVersion { get; }

    public bool Relaunches => _window || !OperatingSystem.IsWindows();

    public string? RestartNote => OperatingSystem.IsWindows()
        ? _window
            ? "Any SharpMUTerm terminal still open closes too."
            : "SharpMUTerm closes and installs the update; run it again in a moment. Any SharpMUTerm window still open closes too."
        : null;

    /// <summary>
    /// The updater for an installed copy, or null for any other. Looks for the installed layout on disk before asking
    /// Velopack anything, because Velopack's locator writes a log file as soon as it is made, and a build run from a
    /// folder has no business leaving one.
    /// </summary>
    /// <param name="window">True for <c>sharpmuterm-window</c>, false for the terminal.</param>
    public static IUpdates? Detect(bool window)
    {
        if (!LooksInstalled())
        {
            return null;
        }

        try
        {
            var locator = VelopackLocator.CreateDefaultForPlatform();
            if (locator.CurrentlyInstalledVersion is not { } version || string.IsNullOrEmpty(locator.UpdateExePath))
            {
                return null;
            }

            return new VelopackUpdates(locator, window, version.ToNormalizedString());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The installed layout, read off the disk: the package's manifest and updater beside this program.</summary>
    private static bool LooksInstalled()
    {
        var here = AppContext.BaseDirectory;
        if (!File.Exists(Path.Combine(here, "sq.version")))
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return File.Exists(Path.Combine(here, "..", "Update.exe"));
        }

        return OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"))
            && File.Exists(Path.Combine(here, "UpdateNix"));
    }

    private UpdateManager Manager(bool prereleases) =>
        new(new GithubSource(Repository, accessToken: null, prerelease: prereleases), new UpdateOptions(), _locator);

    public async Task<UpdateOffer?> CheckAsync(bool prereleases, CancellationToken cancel)
    {
        var manager = Manager(prereleases);
        var info = await manager.CheckForUpdatesAsync().WaitAsync(cancel).ConfigureAwait(false);
        if (info is null || info.IsDowngrade)
        {
            return null;
        }

        var target = info.TargetFullRelease;
        return new UpdateOffer(target.Version.ToNormalizedString(), new Pending(manager, info))
        {
            Notes = string.IsNullOrWhiteSpace(target.NotesMarkdown) ? null : target.NotesMarkdown,
        };
    }

    public Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken cancel)
    {
        var pending = (Pending)offer.Handle;
        return pending.Manager.DownloadUpdatesAsync(pending.Info, progress, cancel);
    }

    public string? ApplyAfterExit(UpdateOffer offer)
    {
        var pending = (Pending)offer.Handle;
        var asset = pending.Info.TargetFullRelease;
        if (_window)
        {
            // Velopack's updater waits for this process to end, puts the new version in place and starts the window.
            pending.Manager.WaitExitThenApplyUpdates(asset, silent: false, restart: true, restartArgs: RestartArguments());
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            pending.Manager.WaitExitThenApplyUpdates(asset, silent: true, restart: false);
            return $"SharpMUTerm {offer.Version} is being installed. Run sharpmuterm again in a moment.";
        }

        return ApplyAndExecAppImage(offer, asset);
    }

    /// <summary>
    /// The Linux terminal: run the AppImage's updater now, while this process waits (the running program is the old
    /// AppImage's mount, which stays readable after the file is replaced), then become the new AppImage in this
    /// terminal. If either step fails the old version is still in place, and the line says what to do.
    /// </summary>
    private string? ApplyAndExecAppImage(UpdateOffer offer, VelopackAsset asset)
    {
        var appImage = _locator.RootAppDir;
        var packages = _locator.PackagesDir;
        if (string.IsNullOrEmpty(appImage) || string.IsNullOrEmpty(packages) || string.IsNullOrEmpty(_locator.UpdateExePath))
        {
            return "SharpMUTerm could not find its AppImage to update.";
        }

        var start = new ProcessStartInfo(_locator.UpdateExePath) { UseShellExecute = false };
        foreach (var argument in new[] { "--silent", "apply", "--norestart", "--package", Path.Combine(packages, asset.FileName), "--rootDir", appImage, "--packageDir", packages })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var updater = Process.Start(start);
            if (updater is null)
            {
                return "SharpMUTerm could not start its updater.";
            }

            updater.WaitForExit();
            if (updater.ExitCode != 0)
            {
                return $"The update to {offer.Version} did not apply (updater exit code {updater.ExitCode}); the log is in {Path.GetTempPath()}.";
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return $"SharpMUTerm could not start its updater: {ex.Message}";
        }

        Console.Out.Flush();
        string?[] argv = [appImage, "--terminal", .. RestartArguments(), null];
        _ = execv(appImage, argv);

        // execv returns only on failure, leaving this process as it was.
        return $"SharpMUTerm {offer.Version} is installed. Run it again to use it.";
    }

    /// <summary>
    /// What this launch was started with, for the new version to start with. A launch that followed a link is restarted
    /// with nothing: it has been followed, and following it again would dial it twice.
    /// </summary>
    private static string[] RestartArguments()
    {
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        return args.Contains(ClientOptions.OpenLink, StringComparer.Ordinal) || Program.LinkArgument(args) is not null ? [] : args;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int execv(string path, string?[] argv);

    /// <summary>A checked release: the manager that found it (its source fixes the prerelease choice) and what it found.</summary>
    private sealed record Pending(UpdateManager Manager, UpdateInfo Info);
}
