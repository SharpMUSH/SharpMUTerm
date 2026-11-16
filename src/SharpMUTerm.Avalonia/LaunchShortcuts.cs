using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The ways an installed copy starts, one per mode, side by side. Velopack's own Start Menu shortcut is the first
/// (<c>SharpMUTerm</c>, the window as it opens with no flag). The Windows install hooks (<see cref="VelopackHooks"/>)
/// add the other two beside it and a Windows Terminal profile for the native one; the AppImage's desktop entry carries
/// the same two as actions (<c>packaging/linux/sharpmuterm.desktop</c>).
/// <para>
/// The native terminal is last on purpose: no Windows terminal draws Kitty graphics (ConPTY drops them), so on Windows
/// the window, or the terminal it emulates, is the better client.
/// </para>
/// </summary>
internal static class LaunchShortcuts
{
    /// <summary>One shortcut: its name in the Start Menu, the program in the install's <c>current</c> folder, its arguments.</summary>
    public sealed record Launcher(string Name, string Executable, string Arguments, string Description);

    /// <summary>The shortcuts the hooks make. Velopack makes <c>SharpMUTerm</c> itself, so it is not here.</summary>
    public static IReadOnlyList<Launcher> Added { get; } =
    [
        new("SharpMUTerm (Emulated)", "sharpmuterm-window.exe", WindowOptions.EmulatedFlag,
            "SharpMUTerm in a window, drawn as the terminal draws it"),
        new("SharpMUTerm (Native)", "sharpmuterm.exe", "",
            "SharpMUTerm in your terminal (no inline pictures on Windows)"),
    ];

    /// <summary>The Start menu entry for the window, which Velopack makes (<c>--shortcuts StartMenuRoot</c>).</summary>
    public const string MainShortcut = "SharpMUTerm.lnk";

    /// <summary>The Windows Terminal profile's name, and the folder its fragment lives in.</summary>
    public const string TerminalProfile = "SharpMUTerm";

    /// <summary>
    /// A Windows Terminal fragment (<c>Fragments\SharpMUTerm\sharpmuterm.json</c>) adding SharpMUTerm to its new-tab
    /// list, running the native terminal from <paramref name="current"/>.
    /// </summary>
    public static string TerminalFragment(string current)
    {
        var profile = new Dictionary<string, object>
        {
            ["name"] = TerminalProfile,
            ["commandline"] = "\"" + Path.Combine(current, "sharpmuterm.exe") + "\"",
            ["icon"] = Path.Combine(current, "sharpmuterm.png"),
            ["startingDirectory"] = "%USERPROFILE%",
        };
        return JsonSerializer.Serialize(
            new Dictionary<string, object> { ["profiles"] = new[] { profile } },
            new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Makes or replaces the added shortcuts and the fragment, pointed at <paramref name="current"/>.</summary>
    [SupportedOSPlatform("windows")]
    public static void Install(string current, Action<Action> attempt)
    {
        var menu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var icon = Path.Combine(current, "sharpmuterm-window.exe");

        // Velopack's own entry has the old installer's name, and that installer's uninstaller deletes it by name: a
        // player who removed the old copy by hand after installing this one was left with no way to start it but a
        // shortcut of their own. Put back when it is missing; Velopack replaces it with its own whenever it writes one.
        var main = Path.Combine(menu, MainShortcut);
        attempt(() =>
        {
            if (!File.Exists(main))
            {
                ShellLink.Save(main, icon, "", current, "SharpMUTerm", icon, WindowsAppId.Value);
            }
        });

        foreach (var launcher in Added)
        {
            attempt(() => ShellLink.Save(
                Path.Combine(menu, launcher.Name + ".lnk"),
                Path.Combine(current, launcher.Executable),
                launcher.Arguments,
                current,
                launcher.Description,
                icon));
        }

        attempt(() =>
        {
            var folder = FragmentFolder();
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "sharpmuterm.json"), TerminalFragment(current));
        });
    }

    /// <summary>Takes away what <see cref="Install"/> made.</summary>
    [SupportedOSPlatform("windows")]
    public static void Uninstall(Action<Action> attempt)
    {
        var menu = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        foreach (var launcher in Added)
        {
            attempt(() => File.Delete(Path.Combine(menu, launcher.Name + ".lnk")));
        }

        // The main entry, if it is the one Install put back (it starts this install); Velopack removes its own.
        attempt(() =>
        {
            var main = Path.Combine(menu, MainShortcut);
            if (ShellLink.Read(main) is { } link && OldInstall.IsUnder(link.Target, Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)))
            {
                File.Delete(main);
            }
        });

        attempt(() =>
        {
            var folder = FragmentFolder();
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        });
    }

    private static string FragmentFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows Terminal", "Fragments", TerminalProfile);

    /// <summary>A <c>.lnk</c> through the shell's own <c>ShellLink</c> object.</summary>
    [SupportedOSPlatform("windows")]
    internal static class ShellLink
    {
        private const int MaxPath = 1024;

        /// <summary>The AppUserModelID property (<c>PKEY_AppUserModel_ID</c>).</summary>
        private static readonly PropertyKey AppUserModelId = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

        public static void Save(string file, string target, string arguments, string workingDirectory, string description, string icon, string? appId = null)
        {
            var link = Create();
            try
            {
                link.SetPath(target);
                link.SetArguments(arguments);
                link.SetWorkingDirectory(workingDirectory);
                link.SetDescription(description);
                link.SetIconLocation(icon, 0);
                if (appId is not null)
                {
                    SetAppId((IPropertyStore)link, appId);
                }

                ((IPersistFile)link).Save(file, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        /// <summary>What a <c>.lnk</c> starts, or null when it cannot be read.</summary>
        public static ShortcutFile? Read(string file)
        {
            var link = Create();
            try
            {
                ((IPersistFile)link).Load(file, 0);
                var target = new System.Text.StringBuilder(MaxPath);
                link.GetPath(target, MaxPath, IntPtr.Zero, 0x4); // SLGP_RAWPATH: as written, not resolved
                var arguments = new System.Text.StringBuilder(MaxPath);
                link.GetArguments(arguments, MaxPath);
                var directory = new System.Text.StringBuilder(MaxPath);
                link.GetWorkingDirectory(directory, MaxPath);
                var description = new System.Text.StringBuilder(MaxPath);
                link.GetDescription(description, MaxPath);
                var icon = new System.Text.StringBuilder(MaxPath);
                link.GetIconLocation(icon, MaxPath, out _);
                return new ShortcutFile(file, Environment.ExpandEnvironmentVariables(target.ToString()), arguments.ToString(),
                    Environment.ExpandEnvironmentVariables(directory.ToString()), description.ToString(), Environment.ExpandEnvironmentVariables(icon.ToString()));
            }
            catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
            {
                return null;
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }

        private static IShellLinkW Create()
        {
            var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!;
            return (IShellLinkW)Activator.CreateInstance(type)!;
        }

        private static void SetAppId(IPropertyStore store, string appId)
        {
            var value = new PropVariant { Type = 31 /* VT_LPWSTR */, Pointer = Marshal.StringToCoTaskMemUni(appId) };
            try
            {
                var key = AppUserModelId;
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally
            {
                Marshal.FreeCoTaskMem(value.Pointer);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public IntPtr Pointer;
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        private interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
            void SetValue(ref PropertyKey key, ref PropVariant value);
            void Commit();
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int size, IntPtr data, uint flags);
            void GetIDList(out IntPtr list);
            void SetIDList(IntPtr list);
            void GetDescription([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
            void GetArguments([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
            void GetHotkey(out short key);
            void SetHotkey(short key);
            void GetShowCmd(out int show);
            void SetShowCmd(int show);
            void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder path, int size, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr hwnd, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid clsid);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
        }
    }
}
