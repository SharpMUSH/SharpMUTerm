using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;

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
    private static class ShellLink
    {
        public static void Save(string file, string target, string arguments, string workingDirectory, string description, string icon)
        {
            var type = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), throwOnError: true)!;
            var link = (IShellLinkW)Activator.CreateInstance(type)!;
            try
            {
                link.SetPath(target);
                link.SetArguments(arguments);
                link.SetWorkingDirectory(workingDirectory);
                link.SetDescription(description);
                link.SetIconLocation(icon, 0);
                ((IPersistFile)link).Save(file, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
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
