using Avalonia;

namespace SharpMUTerm.Avalonia;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
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

    /// <summary>Also what the Avalonia designer looks for.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
