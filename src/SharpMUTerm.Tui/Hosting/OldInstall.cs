using Microsoft.Win32;

namespace SharpMUTerm.Tui;

/// <summary>
/// A copy put on this machine by the Inno Setup installer that came before Velopack (0.2.0 and earlier). It has no updater, so it
/// stays the version it was, and anything still pointing at it — a desktop shortcut, a taskbar pin, the user's
/// <c>PATH</c>, a <c>telnet://</c> handler — starts that old version while reading the same configuration. The window's
/// install and update hooks retire it (<c>OldInstallMigration</c>); one this user cannot remove (installed for everyone)
/// is said at start.
/// </summary>
/// <param name="Folder">Where it is installed: <c>%LocalAppData%\Programs\SharpMUTerm</c> for one user (the default), or
/// <c>Program Files\SharpMUTerm</c> for everyone.</param>
/// <param name="Uninstaller">Its <c>unins000.exe</c>, or null when the registry names none that exists.</param>
/// <param name="ForEveryone">Installed for every user: removing it needs an administrator.</param>
internal sealed record OldInstall(string Folder, string? Uninstaller, bool ForEveryone)
{
    /// <summary>The old installer's <c>AppId</c>, and so the name of its uninstall key (<c>{AppId}_is1</c>).</summary>
    public const string AppId = "{6F1E2C1A-4B7D-4F8E-9C55-2A7B3D9E8F10}";

    /// <summary>The uninstall entry the old installer wrote, under HKCU for one user and HKLM for everyone.</summary>
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId + "_is1";

    /// <summary>The names the old installer gave its shortcuts, in the Start menu and on the desktop.</summary>
    public const string ShortcutName = "SharpMUTerm.lnk";

    /// <summary>
    /// The old installs on this machine, this user's first. Empty anywhere but Windows, and where the registry cannot be
    /// read. <paramref name="current"/> is the running install's folder: an entry naming it, or a folder holding it, is
    /// never reported, so nothing here can mistake the new install for the old.
    /// </summary>
    public static IReadOnlyList<OldInstall> Find(string current)
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var found = new List<OldInstall>();
        foreach (var (hive, everyone) in new[] { (RegistryHive.CurrentUser, false), (RegistryHive.LocalMachine, true) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var key = root.OpenSubKey(UninstallKey);
                if (From(key?.GetValue("InstallLocation") as string, key?.GetValue("UninstallString") as string, everyone, current) is { } install)
                {
                    found.Add(install);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
            }
        }

        return found;
    }

    /// <summary>
    /// The install an uninstall entry describes, or null when it names no folder, a folder that is gone, or one that
    /// is (or holds, or sits inside) <paramref name="current"/>.
    /// </summary>
    internal static OldInstall? From(string? location, string? uninstallString, bool everyone, string current, Func<string, bool>? folderExists = null, Func<string, bool>? fileExists = null)
    {
        folderExists ??= Directory.Exists;
        fileExists ??= File.Exists;
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var folder = Trim(location);
        if (!folderExists(folder) || IsUnder(current, folder) || IsUnder(folder, current))
        {
            return null;
        }

        var uninstaller = Unquote(uninstallString);
        return new OldInstall(folder, uninstaller is not null && fileExists(uninstaller) ? uninstaller : null, everyone);
    }

    /// <summary>Whether <paramref name="path"/> is <paramref name="folder"/> or inside it, ignoring case and a trailing separator.</summary>
    public static bool IsUnder(string path, string folder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var p = Trim(path);
        var f = Trim(folder);
        return string.Equals(p, f, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(f + '\\', StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(f + '/', StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Where a shortcut into the old install should point now: the same program in <paramref name="current"/>, or null
    /// when <paramref name="target"/> is not in the old folder or is not one of the two programs (its uninstaller, a
    /// licence file).
    /// </summary>
    public string? Retarget(string target, string current)
    {
        if (!IsUnder(target, Folder))
        {
            return null;
        }

        var name = Trim(target).Split('\\', '/')[^1];
        return name.Equals("sharpmuterm-window.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("sharpmuterm.exe", StringComparison.OrdinalIgnoreCase)
            ? Trim(current) + '\\' + name.ToLowerInvariant()
            : null;
    }

    /// <summary>
    /// A <c>telnet://</c> handler's command (<c>"…\sharpmuterm-window.exe" --open-link "%1"</c>) with the old folder's
    /// program replaced by <paramref name="current"/>'s, or null when it does not start the old install.
    /// </summary>
    public string? RetargetCommand(string? command, string current)
    {
        var program = Unquote(command);
        if (program is null || Retarget(program, current) is not { } moved)
        {
            return null;
        }

        var rest = command!.TrimStart();
        rest = rest.StartsWith('"') ? rest[(rest.IndexOf('"', 1) + 1)..] : rest[program.Length..];
        return '"' + moved + '"' + rest;
    }

    /// <summary>The program a command line starts: its first argument, quoted or not.</summary>
    internal static string? Unquote(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : null;
        }

        var space = text.IndexOf(' ');
        return space < 0 ? text : text[..space];
    }

    private static string Trim(string path) => path.Trim().TrimEnd('\\', '/');
}
