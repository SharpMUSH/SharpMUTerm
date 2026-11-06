using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Velopack;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// What the Windows installer does beside copying files, run by Velopack's <c>Update.exe</c> as
/// <c>sharpmuterm-window --veloapp-install|updated|uninstall &lt;version&gt;</c>. It takes over what the Inno Setup
/// installer's tasks did, for the per-user install under <c>%LocalAppData%\SharpMUTerm</c>:
/// <list type="bullet">
/// <item>the toast name and icon for the AppUserModelID the window and its shortcut carry (<see cref="WindowsAppId"/>);</item>
/// <item>the install's <c>current</c> folder on the user's PATH, so <c>sharpmuterm</c> runs from any terminal (only an entry
/// this put there is ever taken off);</item>
/// <item>the Symbols Nerd Font Mono font for this user, so the terminal client can draw its icons (never removed, as before:
/// a terminal profile may rely on it).</item>
/// </list>
/// Opening telnet:// links is not registered: Inno offered it unticked, and there is no page here to offer it on.
/// Linux runs no hooks: the AppImage installs nothing.
/// </summary>
internal static class VelopackHooks
{
    /// <summary>Whether this launch is one of Velopack's hook calls rather than the player's.</summary>
    public static bool IsHook(string[] args) =>
        args.Length >= 2 && args[0].StartsWith("--veloapp-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Runs the hook the arguments name and ends the process (Velopack's own <c>Run</c> exits on a hook).</summary>
    public static void Run(string[] args)
    {
        var app = VelopackApp.Build().SetArgs(args).SetAutoApplyOnStartup(false).SetAppUserModelId(WindowsAppId.Value);
        if (OperatingSystem.IsWindows())
        {
            // Registered only on Windows, so the lambdas only run there; the analyzer cannot follow the guard into them.
#pragma warning disable CA1416
            app = app.OnAfterInstallFastCallback(_ => Windows.Install())
                .OnAfterUpdateFastCallback(_ => Windows.Install())
                .OnBeforeUninstallFastCallback(_ => Windows.Uninstall());
#pragma warning restore CA1416
        }

        app.Run();
    }

    [SupportedOSPlatform("windows")]
    private static class Windows
    {
        private const string OwnKey = @"Software\SharpMUTerm\Updater"; // not the Inno installer's key, which its uninstaller deletes
        private const string AddedToPath = "AddedToPathByUpdater";
        private const string AppIdKey = @"Software\Classes\AppUserModelId\" + WindowsAppId.Value;
        private const string FontName = "Symbols Nerd Font Mono Regular (TrueType)";
        private const string FontFile = "SymbolsNerdFontMono-Regular.ttf";

        /// <summary>The install's <c>current</c> folder: where this program is while a hook runs, and the same path every version.</summary>
        private static string Current => Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        public static void Install()
        {
            // Each step on its own: one failing (a locked font file, a policy on the registry) must not cost the others.
            Try(RegisterAppId);
            Try(() => AddToPath(Current));
            Try(InstallFont);
        }

        public static void Uninstall()
        {
            Try(() => Registry.CurrentUser.DeleteSubKeyTree(AppIdKey, throwOnMissingSubKey: false));
            Try(() =>
            {
                using var own = Registry.CurrentUser.OpenSubKey(OwnKey, writable: true);
                if (own?.GetValue(AddedToPath) is string added)
                {
                    RemoveFromPath(added);
                    own.DeleteValue(AddedToPath, throwOnMissingValue: false);
                }
            });
        }

        private static void RegisterAppId()
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppIdKey);
            key.SetValue("DisplayName", "SharpMUTerm");
            key.SetValue("IconUri", Path.Combine(Current, "sharpmuterm.png"));
        }

        private static void AddToPath(string folder)
        {
            using var environment = Registry.CurrentUser.CreateSubKey("Environment");
            var path = environment.GetValue("Path", string.Empty, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? string.Empty;
            if (Entries(path).Any(e => Same(e, folder)))
            {
                return;
            }

            var joined = path.Length == 0 || path.EndsWith(';') ? path + folder : path + ";" + folder;
            environment.SetValue("Path", joined, RegistryValueKind.ExpandString);
            using (var own = Registry.CurrentUser.CreateSubKey(OwnKey))
            {
                own.SetValue(AddedToPath, folder);
            }

            AnnounceEnvironment();
        }

        private static void RemoveFromPath(string folder)
        {
            using var environment = Registry.CurrentUser.OpenSubKey("Environment", writable: true);
            if (environment?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string path)
            {
                return;
            }

            var kept = string.Join(';', Entries(path).Where(e => !Same(e, folder)));
            environment.SetValue("Path", kept, RegistryValueKind.ExpandString);
            AnnounceEnvironment();
        }

        private static IEnumerable<string> Entries(string path) => path.Split(';', StringSplitOptions.RemoveEmptyEntries);

        private static bool Same(string entry, string folder) =>
            string.Equals(Path.TrimEndingDirectorySeparator(entry.Trim()), Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase);

        /// <summary>A per-user font: the file in the user's own fonts folder and its name in the user's font list, unless it is there.</summary>
        private static void InstallFont()
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
            var target = Path.Combine(folder, FontFile);
            using var fonts = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts");
            if (File.Exists(target) && fonts.GetValue(FontName) is not null)
            {
                return;
            }

            using var source = typeof(VelopackHooks).Assembly.GetManifestResourceStream("Fonts." + FontFile);
            if (source is null)
            {
                return;
            }

            Directory.CreateDirectory(folder);
            if (!File.Exists(target))
            {
                using var file = File.Create(target);
                source.CopyTo(file);
            }

            fonts.SetValue(FontName, target);
            _ = AddFontResourceW(target);
        }

        private static void AnnounceEnvironment() =>
            _ = SendMessageTimeoutW(HwndBroadcast, WmSettingChange, 0, "Environment", AbortIfHung, 5000, out _);

        private static void Try(Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // The rest of the install goes on; Velopack logs the hook's run.
            }
        }

        private const int WmSettingChange = 0x001A;
        private const int AbortIfHung = 0x0002;
        private static readonly IntPtr HwndBroadcast = new(0xFFFF);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeoutW(IntPtr hWnd, int msg, nint wParam, string lParam, int flags, int timeout, out nint result);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResourceW(string file);
    }
}
