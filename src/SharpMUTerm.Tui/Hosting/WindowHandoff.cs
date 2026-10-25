using System.Diagnostics;

namespace SharpMUTerm.Tui;

/// <summary>
/// <c>sharpmuterm --window</c>: the terminal launcher hands its arguments to the desktop window's executable and
/// exits. Two executables rather than one with a mode switch, because a Windows executable is either a console
/// program (which flashes a console when started from Explorer) or a GUI one (which has no console to run a TUI
/// in), and because the terminal build then carries no Avalonia. The window is the same client on the same
/// files, so handing off loses nothing.
/// </summary>
internal static class WindowHandoff
{
    public const string Flag = "--window";

    /// <summary>The window executable's file name on this platform.</summary>
    public static string ExecutableName => OperatingSystem.IsWindows() ? "sharpmuterm-window.exe" : "sharpmuterm-window";

    /// <summary>What the window is started with: everything this launcher was given, less <see cref="Flag"/>.</summary>
    public static string[] Arguments(string[] args) => args.Where(a => a != Flag).ToArray();

    /// <summary>
    /// The window executable: beside this one first (a published bundle, or the window's own build output), then
    /// on <c>PATH</c>. Null when neither has it.
    /// </summary>
    public static string? Find(string baseDirectory, string? path, string executable)
    {
        var beside = Path.Combine(baseDirectory, executable);
        if (File.Exists(beside))
        {
            return beside;
        }

        foreach (var directory in (path ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim(), executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Starts the window and returns this launcher's exit code; says where it looked when it cannot.</summary>
    public static int Run(string[] args, TextWriter error)
    {
        var executable = Find(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"), ExecutableName);
        if (executable is null)
        {
            error.WriteLine(
                $"sharpmuterm: {Flag} needs {ExecutableName}, which is not beside this program ({AppContext.BaseDirectory}) or on PATH.");
            return 1;
        }

        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in Arguments(args))
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var _ = Process.Start(start);
            return 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error.WriteLine($"sharpmuterm: could not start {executable}: {e.Message}");
            return 1;
        }
    }
}
