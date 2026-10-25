using System.Globalization;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Graphics;
using SharpMUTerm.Tui;
using SkiaSharp;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// This host's own switches, separated from the client's. What is left over (<see cref="ClientArgs"/>) goes to
/// the client unchanged — and it has to be left over rather than passed whole, because the client reads every
/// argument that does not start with <c>-</c> as a host name, and <c>--font Consolas</c> would dial Consolas.
/// </summary>
internal sealed record WindowOptions(
    string[] ClientArgs,
    string? FontFamily,
    float FontPoints,
    bool Demo,
    string? View,
    string? ThemeName,
    string? RenderPng,
    int Columns,
    int Rows,
    bool TitleBar = false)
{
    /// <summary>
    /// What the client is told about this "terminal": Kitty graphics and truecolour. Both are true by
    /// construction — this driver is the thing drawing the pixels — so nothing is probed.
    /// </summary>
    public static TerminalCapabilities Capabilities { get; } =
        new(GraphicsProtocol.Kitty, supportsTrueColor: true, supportsKittyGraphics: true, supportsSixel: false);

    public static WindowOptions Parse(string[] args)
    {
        var rest = new List<string>();
        string? font = null, view = null, theme = null, png = null;
        var points = 14f;
        var demo = false;
        var titleBar = false;
        var (columns, rows) = (120, 36);
        for (var i = 0; i < args.Length; i++)
        {
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (args[i])
            {
                case "--font": font = Next(); break;
                case "--font-size":
                    if (float.TryParse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    {
                        points = Math.Clamp(p, 6, 48);
                    }

                    break;
                case "--demo-config": demo = true; break;
                case "--title-bar": titleBar = true; break;
                case "--view": view = Next(); break;
                case "--theme": theme = Next(); break;
                case "--render-png": png = Next(); break;
                case "--size":
                    if (Next()?.Split('x', 'X') is [var w, var h] && int.TryParse(w, out var cw) && int.TryParse(h, out var ch))
                    {
                        (columns, rows) = (Math.Clamp(cw, 20, 400), Math.Clamp(ch, 8, 200));
                    }

                    break;
                default: rest.Add(args[i]); break;
            }
        }

        return new WindowOptions([.. rest], font, points, demo, view, theme, png, columns, rows, titleBar);
    }

    /// <summary>Applies <c>--theme</c>, the way the snapshot does: name and inline theme both.</summary>
    public void ApplyTheme(AppConfiguration config)
    {
        if (ThemeName is { } name)
        {
            config.ThemeName = name;
            config.Theme = ThemeLibrary.Get(name);
        }
    }

    /// <summary>The theme's default foreground and background — what a terminal's own colours would have been.</summary>
    public static (SKColor Foreground, SKColor Background) DefaultColours(AppConfiguration config)
    {
        // SharpMUTermApp.ResolveTheme's rule: an inline theme whose name disagrees is a customised one.
        var theme = config.Theme is { } inline && !string.Equals(inline.Name, config.ThemeName, StringComparison.OrdinalIgnoreCase)
            ? inline
            : ThemeLibrary.Get(config.ThemeName);
        var fg = theme.Resolve(TerminalColor.Default, isBackground: false);
        var bg = theme.Resolve(TerminalColor.Default, isBackground: true);
        return (new SKColor(fg.R, fg.G, fg.B), new SKColor(bg.R, bg.G, bg.B));
    }

    /// <summary>
    /// The window's help page: the client's own (<see cref="ClientUsage"/>) — its options, files and keys, with
    /// this host's keys — plus the switches above.
    /// </summary>
    public static string Usage
    {
        get
        {
            var text = new StringWriter();
            ClientUsage.Write(text, Launcher, WindowHost.Keys);
            return text.ToString();
        }
    }

    internal static UsageLauncher Launcher { get; } = new(
        "sharpmuterm-window — SharpMUTerm in a desktop window.",
        "sharpmuterm-window",
        [
            ("Window:",
            [
                new("--font <family>", "Monospace font (default: the first installed Nerd Font, then Cascadia, Consolas…)."),
                new("--font-size <pt>", "Font size (default 14)."),
                new("--size <WxH>", "The window's first size in cells, and the grid for --render-png (default 120x36)."),
                new("--title-bar", "Keep the system title bar. Without it, drag the blank part of the top row",
                    "to move the window, double-click it to maximise, and drag an edge to resize."),
            ]),
            ("Demo:",
            [
                new("--demo-config", "Run the built-in demo worlds instead of your own; saves nothing."),
                new("--view <name>", "With --demo-config: open on one of the snapshot views (e.g. mxp-image)."),
                new("--theme <name>", "With --demo-config: Dark, Light or Solarized Dark."),
                new("--render-png <file>", "With --demo-config: draw one frame to a PNG with no window, and exit."),
            ]),
        ]);

}
