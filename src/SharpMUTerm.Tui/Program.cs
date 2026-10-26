using System.Globalization;
using SharpMUTerm.Core.Theming;
using Microsoft.Extensions.Logging;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Mapping;
using SharpMUTerm.Core.Media;
using SharpMUTerm.Core.Telnet.Mssp;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Graphics;
using SharpConsoleUI.Drivers;

namespace SharpMUTerm.Tui;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
        }

        if (args.Contains(WindowHandoff.Flag))
        {
            return WindowHandoff.Run(args, Console.Error);
        }

        // Anything the load wants to say about the secrets file is collected here and logged once diagnostics
        // exist — the pipeline is built further down, and this runs before it. It is deliberately a list that
        // is drained exactly once: a secrets problem is a startup fact, not a recurring notice.
        var loadNotices = new List<string>();
        var config = LoadConfiguration(loadNotices.Add);
        var capabilities = DetectCapabilities(config);

        // Headless snapshot: render one demo frame to ANSI (for docs images / CI golden files) and
        // exit, without a terminal or a connection. See tools/ansi_frame_to_image.py.
        if (args.Contains("--snapshot"))
        {
            // Detach stdin before constructing the window system: even with a headless driver it can
            // start reading the console for input, which BLOCKS FOREVER when stdin is an interactive
            // TTY or an open pipe (the frame never renders). A null reader returns EOF immediately, so
            // the snapshot is deterministic however it's launched (terminal, pipe, or CI redirect).
            Console.SetIn(TextReader.Null);

            // A snapshot shows your own configuration, like every other way of running the client.
            // `--demo-config` swaps in the built-in demo worlds instead: that is what the docs images
            // and golden frames use, because a golden file that changes with the developer's own
            // worlds isn't one. Opting in keeps the demo where it belongs — an explicit request,
            // never the default state of the app.
            if (args.Contains("--demo-config"))
            {
                config = DemoScene.Build();
            }

            // No save action: a snapshot renders, it does not edit. The settings screens persist each
            // committed change now, and a --view that drives keys into a field would otherwise write the
            // demo worlds straight over the real configuration.
            // --theme names a built-in flavour for this render only. It exists because the client's own
            // chrome is derived from the theme and held to a legibility floor against it, and every frame
            // in the gallery renders Dark — which is exactly how the Light theme's accent (1.42:1), draft
            // pen (1.26:1) and notice (1.73:1) stayed unreadable without anybody seeing them.
            if (GetOption(args, "--theme") is { } themeName)
            {
                // Both, because ResolveTheme treats an inline Theme whose Name disagrees with ThemeName
                // as a customised one and prefers it — so setting the name alone would select the built-in
                // and then be overruled by the default Dark still sitting in Theme.
                config.ThemeName = themeName;
                config.Theme = ThemeLibrary.Get(themeName);
            }

            // --transparency <0-100> renders with F7's background transparency at that level. A headless
            // driver is no kitty, so the frame shows the fallback: the cells handed to the terminal's own
            // background (SGR 49), which can be counted rather than taken on trust.
            if (GetOption(args, "--transparency") is { } level
                && int.TryParse(level, NumberStyles.None, CultureInfo.InvariantCulture, out var percent))
            {
                config.Text.BackgroundTransparency = Math.Clamp(percent, 0, TextSettings.MaxBackgroundTransparency);
            }

            var (width, height) = ParseSize(args);
            var app = new SharpMUTermApp(config, ClientHost.Snapshot(width, height, capabilities));
            var frame = app.RenderSnapshot(GetOption(args, "--view"));
            var outPath = GetOption(args, "--out");
            if (outPath is not null)
            {
                File.WriteAllText(outPath, frame);
            }
            else
            {
                Console.Out.Write(frame);
                Console.Out.Flush();
            }

            // The framework keeps foreground worker threads alive; the frame is captured, so exit
            // hard rather than waiting on them (keeps the snapshot fast + deterministic in CI).
            Environment.Exit(0);
        }

        return RunLive(args, config, loadNotices, ClientHost.Terminal(capabilities));
    }

    /// <summary>
    /// The live client, past argument handling: what a terminal launch runs, and what the windowed host in
    /// <c>SharpMUTerm.Avalonia</c> runs with its own driver. One composition root for both, so a window is the
    /// same client as a terminal — same files, same diagnostics, same save on exit — and differs only in what
    /// <paramref name="host"/> supplies.
    /// </summary>
    internal static int RunLive(
        string[] args,
        AppConfiguration config,
        IReadOnlyList<string> loadNotices,
        ClientHost host)
    {
        // What this launch connects: the command line's host if one was typed, else whatever is marked
        // `at start` on F5, else nothing at all. The precedence lives in Core (StartupConnections) so it
        // can be asserted without a terminal; the parsing stays here.
        var startup = StartupConnections.Resolve(config, CommandLineWorld(args));

        // The one directory this client writes its own files into, resolved once, here, because this is
        // the only code that knows it is the live client. Everything below is handed it rather than
        // reaching for it: an app that is not this entry point — a snapshot, a test — is given none and so
        // writes no transcript at all. See SharpMUTermApp's `logRoot` parameter for the defect that
        // resolving it inside the app caused.
        var logRoot = Path.Combine(Path.GetDirectoryName(ConfigurationStore.DefaultPath)!, "logs");

        // Client diagnostics: an in-memory history behind ⌃P ▸ Show client messages, plus a rolling
        // file beside the session logs but plainly not one of them (client-diagnostics-*.log next to
        // the World.Character-*.log transcripts). Never a console sink — this app owns the screen.
        using var diagnostics = ClientDiagnostics.Create(logRoot);

        // A crash leaves its stack in that file. Without this an exception nothing caught ended the process with
        // nothing written anywhere a user could find, which in the window (no console) meant it simply vanished.
        using var crashes = diagnostics.RecordUnhandled();

        // Drain what the configuration load had to say now that there is somewhere to say it. A secrets file
        // that could not be read means characters start with no password — worth one line in the client
        // message log (⌃P), and never more than that: the client still runs, still connects, and the password
        // can simply be typed again.
        var loadLogger = diagnostics.For("SharpMUTerm.Configuration");
        foreach (var notice in loadNotices)
        {
            loadLogger.LogWarning("{Notice}", notice);
        }

        // The panes' own memory between runs, beside the configuration whose LastSession says where each
        // pane goes. Resolved here for the same reason logRoot is: only this code knows it is the live
        // client, so only it hands over a directory to write in. It is created unconditionally even when
        // the feature is off — constructing one touches no disk, and the ⌃P purge has to be able to
        // clear what an earlier, enabled run left behind.
        using var restore = new RestoreLog(
            string.IsNullOrWhiteSpace(config.RestoreLog.Directory)
                ? RestoreLog.DefaultRoot(ConfigurationStore.DefaultPath)
                : config.RestoreLog.Directory!,
            config.RestoreLog)
        {
            Logger = diagnostics.For("SharpMUTerm.RestoreLog"),
        };

        // What every server has said about itself, beside the configuration and deliberately not in it:
        // config.json is what the user asked for and is hand-edited, and a write per connect has no
        // business landing there. Resolved here for the same reason logRoot and the restore log are —
        // only this code knows it is the live client. Anything else gets a memory-only cache and so
        // provably writes nothing.
        var mssp = new MsspCache(MsspCache.PathFor(ConfigurationStore.DefaultPath))
        {
            Logger = diagnostics.For("SharpMUTerm.Mssp"),
        };
        if (mssp.Problem is { } msspProblem)
        {
            loadLogger.LogWarning("{Notice}", msspProblem);
        }

        // Lua files are read from <config>/scripts and nowhere else (SessionScripts.Resolve), so a trigger
        // set naming a path cannot make the client read anything outside it. Created here so a new user has
        // somewhere obvious to put a file; a failure leaves the folder missing, and each load then says so.
        var scriptsRoot = Path.Combine(Path.GetDirectoryName(ConfigurationStore.DefaultPath)!, "scripts");
        try
        {
            Directory.CreateDirectory(scriptsRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            loadLogger.LogWarning("Could not create the scripts folder {Folder}: {Reason}", scriptsRoot, ex.Message);
        }

        var liveApp = new SharpMUTermApp(
            config,
            host,
            diagnostics: diagnostics,
            save: saved => ConfigurationStore.Save(ConfigurationStore.DefaultPath, saved),
            logRoot: logRoot,
            restore: restore,
            mssp: mssp,
            scriptsRoot: scriptsRoot,
            maps: new MapStore(MapStore.PathFor(ConfigurationStore.DefaultPath)) { Logger = diagnostics.For("SharpMUTerm.Map") },
            media: new MediaCache(MediaCache.DefaultRoot));
        int exitCode;
        try
        {
            exitCode = liveApp.Run(startup); // blocks on the SharpConsoleUI main loop until exit
        }
        catch (Exception ex)
        {
            // Run itself catches what its loop throws; this is what escapes it (startup, shutdown). The window's
            // client thread catches it next, and has nowhere but a console that does not exist to write it.
            diagnostics.Logger.LogCritical(ex, "The client stopped on an exception");
            throw;
        }

        if (exitCode != 0)
        {
            diagnostics.Logger.LogWarning("The client loop ended with exit code {ExitCode}", exitCode);
        }

        // Persist the workspace so the next launch resumes where this one left off.
        try
        {
            config.LastSession = liveApp.CaptureSession();
            ConfigurationStore.Save(ConfigurationStore.DefaultPath, config);
        }
        catch
        {
            // A failed save must never change the exit code — the session is a convenience, not critical.
        }

        return exitCode;
    }

    /// <summary>Parses <c>--size WxH</c> (default 160x48) for the snapshot frame.</summary>
    private static (int Width, int Height) ParseSize(string[] args)
    {
        var size = GetOption(args, "--size");
        if (size is not null)
        {
            var parts = size.Split('x', 'X');
            if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
            {
                return (Math.Clamp(w, 20, 400), Math.Clamp(h, 8, 200));
            }
        }

        return (160, 48);
    }

    /// <summary>
    /// Loads the configuration, collecting anything the store wants reported into
    /// <paramref name="report"/> for the caller to log once diagnostics exist.
    /// </summary>
    internal static AppConfiguration LoadConfiguration(Action<string> report)
    {
        try
        {
            return ConfigurationStore.Load(ConfigurationStore.DefaultPath, report);
        }
        catch
        {
            return new AppConfiguration();
        }
    }

    internal static TerminalCapabilities DetectCapabilities(AppConfiguration config)
    {
        // A config graphics override maps onto the same SHARPMUTERM_GRAPHICS mechanism the probe reads.
        if (!string.IsNullOrEmpty(config.GraphicsOverride))
        {
            Environment.SetEnvironmentVariable("SHARPMUTERM_GRAPHICS", config.GraphicsOverride);
        }

        return CapabilityProbe.DetectFromEnvironment();
    }

    /// <summary>
    /// The world named on the command line — <c>host [port]</c> plus its switches — or null when no host
    /// was given.
    /// <para>
    /// It used to fall back to <c>config.Worlds.FirstOrDefault()</c>, and that fallback was the whole of
    /// the client's startup policy: the first world's first character, dialled unconditionally, with no
    /// way to name a different one and no way to decline. Choosing is now
    /// <see cref="CharacterDefinition.ConnectAtStartup"/> and it belongs in
    /// <see cref="StartupConnections"/>, so this function is left doing only the thing its name says.
    /// </para>
    /// </summary>
    private static WorldDefinition? CommandLineWorld(string[] args)
    {
        var positional = ClientOptions.Positional(args);
        if (positional.Length >= 1)
        {
            var host = positional[0];
            var port = positional.Length >= 2 && int.TryParse(positional[1], out var p) ? p : 4000;
            return new WorldDefinition
            {
                Name = GetOption(args, "--name") ?? host,
                Host = host,
                Port = port,
                UseTls = args.Contains("--tls"),
                AllowInvalidCertificates = args.Contains("--insecure"),
            };
        }

        return null;
    }

    internal static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void PrintUsage() => Console.Write(UsageText);

    /// <summary>
    /// The <c>--help</c> text. Built into a string rather than written straight to the console so a test
    /// can hold it to the honesty rule the settings screens are already held to: every key this page names
    /// has to be one that works, and — the half that bit here — it must not name one that cannot fire.
    /// Shift+⏎ and Ctrl+⏎ are the case in point: they are what was asked for and no Unix terminal reports
    /// them distinctly through SharpConsoleUI's input parser, so naming them would send the reader to press
    /// a key that does nothing.
    /// </summary>
    internal static string UsageText
    {
        get
        {
            var text = new StringWriter();
            ClientUsage.Write(text, ClientUsage.Terminal, HostKeys.Terminal);
            return text.ToString();
        }
    }
}
