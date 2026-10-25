using System.Runtime.InteropServices;
using Avalonia;

namespace SharpMUTerm.Avalonia;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // A window has no stdin to read, and the framework reads a redirected one as piped input, behind a modal
        // "Reading input" dialog that stays up for as long as the pipe is open. Launched from anything that
        // leaves stdin a pipe, the client would open on that dialog. The terminal snapshot does the same.
        Console.SetIn(TextReader.Null);
        AttachToParentConsole();

        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.Out.Write(WindowOptions.Usage);
            return 0;
        }

        var options = WindowOptions.Parse(args);
        if (options.RenderPng is not null)
        {
            return RenderPng.Run(options);
        }

        App.Options = options;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(options.ClientArgs);
    }

    /// <summary>
    /// A Windows GUI executable starts with no console, so <c>--help</c> and <c>--render-png</c>'s report would go
    /// nowhere when run from a prompt. Borrow the prompt's console when there is one and output is not already
    /// redirected (a pipe or a file works as it is). A no-op elsewhere, and when started from Explorer.
    /// </summary>
    private static void AttachToParentConsole()
    {
        if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected)
        {
            AttachConsole(AttachParentProcess);
        }
    }

    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    /// <summary>Also what the Avalonia designer looks for.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
