using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary><see cref="IOldInstallDesktop"/> on Windows: the shell's shortcut files, this user's registry, the old uninstaller.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsDesktop : IOldInstallDesktop
{
    /// <summary>Our own key: the old installer's was <c>Software\SharpMUTerm</c>, which its uninstaller deletes with every subkey.</summary>
    public const string OwnKey = @"Software\SharpMUTerm\Updater";

    /// <summary>The PATH entry the install hook added, under <see cref="OwnKey"/>.</summary>
    public const string AddedToPath = "AddedToPathByUpdater";

    private static string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);

    private static string Pins(string where) => Path.Combine(Folder(Environment.SpecialFolder.ApplicationData),
        "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", where);

    /// <summary>The folders searched, each on its own level only, except the Start menu's, which is searched one level down.</summary>
    private static IEnumerable<(string Folder, bool OneDown)> Places() =>
    [
        (Folder(Environment.SpecialFolder.DesktopDirectory), false),
        (Folder(Environment.SpecialFolder.CommonDesktopDirectory), false),
        (Folder(Environment.SpecialFolder.Programs), true),
        (Folder(Environment.SpecialFolder.CommonPrograms), true),
        (Pins("TaskBar"), false),
        (Pins("StartMenu"), false),
    ];

    public IReadOnlyList<ShortcutFile> Shortcuts()
    {
        var found = new List<ShortcutFile>();
        foreach (var (folder, oneDown) in Places())
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = oneDown,
                    MaxRecursionDepth = 1,
                    IgnoreInaccessible = true,
                }).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (LaunchShortcuts.ShellLink.Read(file) is { } shortcut)
                {
                    found.Add(shortcut);
                }
            }
        }

        return found;
    }

    public IReadOnlyList<string> OldInstallerShortcuts(OldInstall install)
    {
        var programs = install.ForEveryone ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs;
        var desktop = install.ForEveryone ? Environment.SpecialFolder.CommonDesktopDirectory : Environment.SpecialFolder.DesktopDirectory;
        return
        [
            Path.Combine(Folder(programs), OldInstall.ShortcutName),
            Path.Combine(Folder(desktop), OldInstall.ShortcutName),
        ];
    }

    public bool Exists(string file) => File.Exists(file);

    public void Save(ShortcutFile shortcut, string? appId) =>
        LaunchShortcuts.ShellLink.Save(shortcut.File, shortcut.Target, shortcut.Arguments, shortcut.WorkingDirectory,
            shortcut.Description, shortcut.Icon, appId);

    public string? LinkCommand(string scheme)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\" + scheme + @"\shell\open\command");
        return key?.GetValue(null) as string;
    }

    public void SetLinkCommand(string scheme, string command, string icon)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + scheme);
        key.SetValue(null, scheme == "telnets" ? "URL:Telnet over TLS Protocol" : "URL:Telnet Protocol");
        key.SetValue("URL Protocol", string.Empty);
        using (var iconKey = key.CreateSubKey("DefaultIcon"))
        {
            iconKey.SetValue(null, '"' + icon + "\",0");
        }

        using var open = key.CreateSubKey(@"shell\open\command");
        open.SetValue(null, command);
    }

    public string? PathRecord
    {
        get
        {
            using var own = Registry.CurrentUser.OpenSubKey(OwnKey);
            return own?.GetValue(AddedToPath) as string;
        }
        set
        {
            using var own = Registry.CurrentUser.CreateSubKey(OwnKey);
            if (value is null)
            {
                own.DeleteValue(AddedToPath, throwOnMissingValue: false);
            }
            else
            {
                own.SetValue(AddedToPath, value);
            }
        }
    }

    public bool Uninstall(OldInstall install, TimeSpan timeout)
    {
        if (install.Uninstaller is null)
        {
            return false;
        }

        var start = new ProcessStartInfo(install.Uninstaller) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" })
        {
            start.ArgumentList.Add(argument);
        }

        var deadline = Stopwatch.StartNew();
        using (var process = Process.Start(start))
        {
            process?.WaitForExit(timeout);
        }

        // Inno's uninstaller hands its work to a copy of itself in the temp folder and may return before that copy is
        // done, so the entry's going is what says it has finished.
        while (deadline.Elapsed < timeout)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(OldInstall.UninstallKey))
            {
                if (key is null)
                {
                    return true;
                }
            }

            Thread.Sleep(250);
        }

        return false;
    }
}
