using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Core.Theming;

/// <summary>Built-in themes and lookup by name (yazi-style named flavours).</summary>
public static class ThemeLibrary
{
    /// <summary>
    /// The default dark theme: the UX mockups' navy (<c>ux-mockups/Main.dc.html</c>). Its values are chosen so the planes
    /// the workspace derives from them land on the mockup — a pane on <c>#151921</c>, focused <c>#212835</c>, the
    /// backdrop <c>#10131a</c> (the ground), the header <c>#13161e</c>, the armed command line <c>#233049</c>, rules <c>#2c3447</c>,
    /// ink <c>#c8d0e0</c> and <c>#d7deec</c>. <see cref="Theme.Prompt"/> is the blue the armed band leans to, picked so
    /// that lean lands on <c>#233049</c>.
    /// </summary>
    public static Theme Dark() => new()
    {
        Name = "Dark",
        Foreground = new Rgb(0xc8, 0xd0, 0xe0),
        Background = new Rgb(0x22, 0x28, 0x35),
        StatusForeground = new Rgb(0xd7, 0xde, 0xec),
        StatusBackground = new Rgb(0x13, 0x16, 0x1e),
        Border = new Rgb(0x49, 0x55, 0x73),
        SystemMessage = new Rgb(0x6a, 0x99, 0x55),
        LocalEcho = new Rgb(0xdc, 0xdc, 0xaa),
        Prompt = new Rgb(0x3f, 0x61, 0xa3),
    };

    /// <summary>A light theme for bright terminals.</summary>
    public static Theme Light() => new()
    {
        Name = "Light",
        Foreground = new Rgb(0x2b, 0x2b, 0x2b),
        Background = new Rgb(0xfa, 0xfa, 0xfa),
        StatusForeground = new Rgb(0x1a, 0x1a, 0x1a),
        StatusBackground = new Rgb(0xdc, 0xdc, 0xe6),
        Border = new Rgb(0xb0, 0xb0, 0xc0),
        SystemMessage = new Rgb(0x1a, 0x7f, 0x37),
        LocalEcho = new Rgb(0x8a, 0x6d, 0x00),
        Prompt = new Rgb(0x1f, 0x5f, 0xbf),
    };

    /// <summary>The classic Solarized Dark palette.</summary>
    public static Theme SolarizedDark() => new()
    {
        Name = "Solarized Dark",
        Foreground = new Rgb(0x83, 0x94, 0x96),
        Background = new Rgb(0x00, 0x2b, 0x36),
        StatusForeground = new Rgb(0x93, 0xa1, 0xa1),
        StatusBackground = new Rgb(0x07, 0x36, 0x42),
        Border = new Rgb(0x58, 0x6e, 0x75),
        SystemMessage = new Rgb(0x85, 0x99, 0x00),
        LocalEcho = new Rgb(0xb5, 0x89, 0x00),
        Prompt = new Rgb(0x26, 0x8b, 0xd2),
        Palette16 =
        [
            new Rgb(0x07, 0x36, 0x42), new Rgb(0xdc, 0x32, 0x2f), new Rgb(0x85, 0x99, 0x00), new Rgb(0xb5, 0x89, 0x00),
            new Rgb(0x26, 0x8b, 0xd2), new Rgb(0xd3, 0x36, 0x82), new Rgb(0x2a, 0xa1, 0x98), new Rgb(0xee, 0xe8, 0xd5),
            new Rgb(0x00, 0x2b, 0x36), new Rgb(0xcb, 0x4b, 0x16), new Rgb(0x58, 0x6e, 0x75), new Rgb(0x65, 0x7b, 0x83),
            new Rgb(0x83, 0x94, 0x96), new Rgb(0x6c, 0x71, 0xc4), new Rgb(0x93, 0xa1, 0xa1), new Rgb(0xfd, 0xf6, 0xe3),
        ],
    };

    private static readonly Dictionary<string, Func<Theme>> Builtins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dark"] = Dark,
        ["Light"] = Light,
        ["Solarized Dark"] = SolarizedDark,
        ["SolarizedDark"] = SolarizedDark,
    };

    public static IReadOnlyCollection<string> Names => new[] { "Dark", "Light", "Solarized Dark" };

    /// <summary>Returns the built-in theme by name, or the dark default if unknown.</summary>
    public static Theme Get(string? name) =>
        name is not null && Builtins.TryGetValue(name, out var factory) ? factory() : Dark();
}
