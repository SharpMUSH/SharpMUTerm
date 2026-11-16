using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>A shortcut as it is on disk: its file and what it starts.</summary>
internal sealed record ShortcutFile(string File, string Target, string Arguments, string WorkingDirectory, string Description, string Icon);

/// <summary>
/// What <see cref="OldInstallMigration"/> reads and changes on the desktop. One implementation is Windows'
/// (<see cref="WindowsDesktop"/>); the tests use their own, so the migration's rules are checked on any machine.
/// </summary>
internal interface IOldInstallDesktop
{
    /// <summary>Every shortcut in the places a player keeps one: the desktop, the Start menu, the taskbar's pins.</summary>
    IReadOnlyList<ShortcutFile> Shortcuts();

    /// <summary>The shortcut files the old installer made itself, which its uninstaller deletes by name.</summary>
    IReadOnlyList<string> OldInstallerShortcuts(OldInstall install);

    /// <summary>Whether a shortcut file is still there.</summary>
    bool Exists(string file);

    /// <summary>Writes (or replaces) a shortcut. <paramref name="appId"/> is the AppUserModelID it carries, or null.</summary>
    void Save(ShortcutFile shortcut, string? appId);

    /// <summary>The command a <c>telnet</c>/<c>telnets</c> link runs for this user, or null.</summary>
    string? LinkCommand(string scheme);

    /// <summary>Makes <paramref name="scheme"/> links run <paramref name="command"/> for this user.</summary>
    void SetLinkCommand(string scheme, string command, string icon);

    /// <summary>Our own record of the PATH entry the install hook added, which the old uninstaller deletes with its key.</summary>
    string? PathRecord { get; set; }

    /// <summary>Runs the old uninstaller without a window and waits for it, at most <paramref name="timeout"/>. True once it is gone.</summary>
    bool Uninstall(OldInstall install, TimeSpan timeout);
}

/// <summary>
/// Retires a copy the old Inno Setup installer left (<see cref="OldInstall"/>), from the window's install and update
/// hooks, so nothing on this machine still starts it. The report that led here: Setup installed the new client, the
/// desktop shortcut the old installer had made still started the old one, and every setting saved in the new one
/// seemed to be lost on the next start, because the old version was what came up.
/// <list type="number">
/// <item>Every shortcut that starts the old copy is noted, and so are the ones the old uninstaller deletes by name
/// (its Start menu entry is the same file as Velopack's own).</item>
/// <item>A copy installed for this user is uninstalled, silently. That takes its folder off <c>PATH</c> too, which
/// otherwise came before the new one, so <c>sharpmuterm</c> in a terminal was the old version as well.</item>
/// <item>The noted shortcuts are written back, pointed at the same program in <c>current\</c>: a desktop shortcut or
/// a pin the player made stays where they put it and starts the new version.</item>
/// <item>A <c>telnet://</c> handler the player chose in the old installer is pointed at the new copy.</item>
/// </list>
/// A copy installed for everyone needs an administrator to remove, so it is left, its shortcuts in this user's folders
/// are still pointed at the new copy, and the client says at start that it is there.
/// </summary>
internal static class OldInstallMigration
{
    /// <summary>
    /// How long the hook waits for the old uninstaller. Velopack gives a hook 30 seconds and kills it after; the rest
    /// of the hook (a shortcut, a font, a registry key) needs a second of it.
    /// </summary>
    public static readonly TimeSpan UninstallTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The link schemes the old installer could register.</summary>
    public static readonly string[] LinkSchemes = ["telnet", "telnets"];

    /// <summary>Retires every old install found. Each step is its own <paramref name="attempt"/>, so one failing costs no other.</summary>
    public static void Run(IReadOnlyList<OldInstall> installs, string current, IOldInstallDesktop desktop, Action<Action> attempt)
    {
        foreach (var install in installs)
        {
            Run(install, current, desktop, attempt);
        }
    }

    private static void Run(OldInstall install, string current, IOldInstallDesktop desktop, Action<Action> attempt)
    {
        IReadOnlyList<ShortcutFile> shortcuts = [];
        IReadOnlyList<string> owned = [];
        attempt(() => shortcuts = desktop.Shortcuts());
        attempt(() => owned = desktop.OldInstallerShortcuts(install));

        // Noted before the uninstaller runs: it deletes its own shortcuts by name, whatever they start by then.
        var keep = shortcuts
            .Where(s => install.Retarget(s.Target, current) is not null || owned.Contains(s.File, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var links = new Dictionary<string, string>();
        foreach (var scheme in LinkSchemes)
        {
            attempt(() =>
            {
                if (install.RetargetCommand(desktop.LinkCommand(scheme), current) is { } moved)
                {
                    links[scheme] = moved;
                }
            });
        }

        string? pathRecord = null;
        attempt(() => pathRecord = desktop.PathRecord);

        if (!install.ForEveryone && install.Uninstaller is not null)
        {
            attempt(() => desktop.Uninstall(install, UninstallTimeout));
        }

        foreach (var shortcut in keep)
        {
            // One that starts something else (Velopack's own entry, of the same name as the old one) is written back
            // only if the uninstaller took it, as it was: rewriting it would lose what the shell knows about it.
            if (install.Retarget(shortcut.Target, current) is null && desktop.Exists(shortcut.File))
            {
                continue;
            }

            var target = install.Retarget(shortcut.Target, current) ?? shortcut.Target;
            var moved = shortcut with
            {
                Target = target,
                WorkingDirectory = OldInstall.IsUnder(shortcut.WorkingDirectory, install.Folder) ? current : shortcut.WorkingDirectory,
                Icon = install.Retarget(shortcut.Icon, current) ?? (OldInstall.IsUnder(shortcut.Icon, install.Folder) ? target : shortcut.Icon),
            };
            attempt(() => desktop.Save(moved, AppIdFor(moved)));
        }

        foreach (var (scheme, command) in links)
        {
            attempt(() => desktop.SetLinkCommand(scheme, command, OldInstall.Unquote(command)!));
        }

        if (pathRecord is not null)
        {
            attempt(() => desktop.PathRecord = pathRecord);
        }
    }

    /// <summary>
    /// The AppUserModelID a written-back shortcut carries: the window's, when it starts the window as it opens with no
    /// flag or is the <c>SharpMUTerm</c> entry (so the taskbar groups it with the window, and a toast is shown under it),
    /// else none.
    /// </summary>
    internal static string? AppIdFor(ShortcutFile shortcut) =>
        (shortcut.Target.EndsWith("sharpmuterm-window.exe", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(shortcut.Arguments))
            || shortcut.File.EndsWith('\\' + OldInstall.ShortcutName, StringComparison.OrdinalIgnoreCase)
            ? WindowsAppId.Value
            : null;
}
