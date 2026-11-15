using System.Diagnostics;

namespace SharpMUTerm.Tui;

/// <summary>
/// Opens a folder in the desktop's file manager: Explorer on Windows, Finder on macOS, whatever <c>xdg-open</c> picks
/// elsewhere. The terminal and the window both hand it in as <see cref="ClientHost.OpenFolder"/>; a test and a snapshot
/// hand in none.
/// <para>
/// The program is named and the folder is its one argument (<see cref="ProcessStartInfo.ArgumentList"/>), never a shell
/// string, so a folder name with spaces or quotes is a name. It is a <em>folder</em> or nothing: shell-executing a path
/// lets the desktop pick a handler, and for a file that handler may run it.
/// </para>
/// </summary>
internal static class FolderOpener
{
    /// <summary>Whether <paramref name="path"/> is an absolute path to a folder that exists, and its full form if so.</summary>
    internal static bool TryParseFolder(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.Contains('\0'))
        {
            return false;
        }

        var resolved = Path.GetFullPath(path);
        if (!Directory.Exists(resolved))
        {
            return false;
        }

        full = resolved;
        return true;
    }

    /// <summary>The program that shows a folder on this platform.</summary>
    internal static string Program =>
        OperatingSystem.IsWindows() ? "explorer.exe"
        : OperatingSystem.IsMacOS() ? "open"
        : "xdg-open";

    /// <summary>Shows <paramref name="path"/> in the file manager.</summary>
    /// <exception cref="ArgumentException">The path is not an existing folder (<see cref="TryParseFolder"/>).</exception>
    internal static void Open(string path)
    {
        if (!TryParseFolder(path, out var folder))
        {
            throw new ArgumentException($"not a folder: {path}", nameof(path));
        }

        // Explorer reads a trailing separator as "show what is inside", which is what is meant; without one it may
        // open the parent with the folder selected on some versions.
        var argument = OperatingSystem.IsWindows() && !folder.EndsWith(Path.DirectorySeparatorChar)
            ? folder + Path.DirectorySeparatorChar
            : folder;
        var start = new ProcessStartInfo(Program) { UseShellExecute = false };
        start.ArgumentList.Add(argument);
        Process.Start(start)?.Dispose();
    }
}
