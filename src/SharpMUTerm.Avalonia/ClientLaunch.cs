using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// Starts the client on a driver. Two ways, and the difference is what may be written to disk.
/// <para>
/// <b>Live</b> goes through <see cref="Tui.Program.RunLive"/>, the terminal client's own composition root, so the
/// window is the same client: the same configuration, secrets, transcripts, restore log, MSSP cache and save on
/// exit. <b>Demo</b> builds the app the way a snapshot does — no save action, no log root, no restore log — so
/// running the demo worlds in a window cannot write them over the real configuration.
/// </para>
/// </summary>
internal static class ClientLaunch
{
    public static AppConfiguration LoadConfiguration(WindowOptions options, List<string> notices)
    {
        if (options.Demo)
        {
            var demo = DemoScene.Build();
            options.ApplyTheme(demo);
            return demo;
        }

        var config = Tui.Program.LoadConfiguration(notices.Add);
        options.ApplyTheme(config);
        return config;
    }

    /// <summary>Runs the client on <paramref name="driver"/> until it quits; blocks the calling thread.</summary>
    public static int Run(WindowOptions options, AppConfiguration config, IReadOnlyList<string> notices, AvaloniaConsoleDriver driver, Action<string>? clipboard)
    {
        if (options.Demo)
        {
            var app = Demo(options, config, driver, clipboard);
            return app.Run([]);
        }

        // Focus reporting is a terminal escape, and its focus-in arrives disguised as a Tab: in a window it
        // would only ever turn a real Tab after a pause into a "you were away" bar.
        return Tui.Program.RunLive(
            options.ClientArgs,
            config,
            WindowOptions.Capabilities,
            notices,
            new LiveHost(driver, clipboard, FocusReporting: false));
    }

    /// <summary>The demo client, with its <c>--view</c> scene laid out and not yet running.</summary>
    public static SharpMUTermApp Demo(WindowOptions options, AppConfiguration config, AvaloniaConsoleDriver driver, Action<string>? clipboard)
    {
        var app = new SharpMUTermApp(
            config,
            WindowOptions.Capabilities,
            driver,
            focusReporting: false,
            openUrl: ExternalBrowser.Open,
            clipboard: clipboard);

        // The snapshot's scene builder is the demo: it pours the demo output in and opens the named view. What
        // it returns is the headless driver's capture, which on this driver is empty and not wanted.
        _ = app.RenderSnapshot(options.View);
        return app;
    }
}
