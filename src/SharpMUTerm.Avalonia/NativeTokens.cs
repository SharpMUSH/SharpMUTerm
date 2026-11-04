using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Core.Theming;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's one design language: every native surface — the palette, the modals, the welcome, the settings
/// window and its list editors, the packages page, the right-click menu, the composer — takes its planes, inks,
/// corner radii and type sizes from here, so they read as one application rather than as the waves they were built in.
/// <para>
/// <b>Derived from the client's theme the way <see cref="ScreenTheme"/> is.</b> Two canonical sets, one per side of
/// mid-grey (the palette's card as it was drawn before there were tokens), are laid onto the theme: its background's
/// hue shift is added to every plane (so Solarized Dark gets teal cards, not the Dark theme's grey ones), and then every
/// ink is held to <see cref="TextFloor"/> on every plane it is drawn on. The Dark and Light themes resolve to the
/// canonical sets exactly. The accent is the client's own (<see cref="ChromeInk.BaseAccent"/>, the cells' teal), moved
/// only as far as the floor asks; the warning and the notice are the cells' amber hues, likewise.
/// </para>
/// <para>
/// <b>The only literal colours and sizes in the window's views are here</b>; <c>NativeTokenLiteralTests</c> fails on a
/// <c>FontSize = 12</c> or a <c>Color.Parse("#…")</c> anywhere else in the assembly, apart from the few it lists with
/// their reasons.
/// </para>
/// </summary>
internal sealed record NativeTokens(
    bool IsDark,
    Color Scrim,
    Color Card,
    Color Edge,
    Color Rule,
    Color Text,
    Color Dim,
    Color Header,
    Color Selected,
    Color Accent,
    Color AccentInk,
    Color KeyFill,
    Color KeyInk,
    Color Warn,
    Color Notice)
{
    // ── the scales ──────────────────────────────────────────────────────────────────────────────

    /// <summary>The type ramp: captions and keys.</summary>
    public const double Small = 11;

    /// <summary>The type ramp: body text, help, rows.</summary>
    public const double Body = 12;

    /// <summary>The type ramp: a field's label, a list item's name, a search box.</summary>
    public const double Title = 14;

    /// <summary>The type ramp: a surface's or a page's heading.</summary>
    public const double Heading = 18;

    /// <summary>
    /// Not on the ramp: the theme cards' miniature of the cells (F7), which is a picture of a terminal at a quarter of
    /// its size rather than text to read.
    /// </summary>
    public const double Miniature = 9;

    /// <summary>
    /// The UI face of every native view: the bundled Inter (<see cref="BundledFonts"/>), with the platform's own
    /// sans as a fallback for a glyph Inter lacks. It is also the app's default family, so a view naming none gets it.
    /// </summary>
    public static FontFamily UiFont { get; } = new($"{BundledFonts.Ui}, Segoe UI, Cantarell, Helvetica Neue, sans-serif");

    /// <summary>
    /// The face game text and code are drawn in natively (the composer, ⌃F's lines, the history, the script console):
    /// the bundled JetBrains Mono, the cells' own default, with installed monospace faces as fallbacks for what it lacks.
    /// </summary>
    public static FontFamily MonoFont { get; } = new($"{BundledFonts.Mono}, DejaVu Sans Mono, Consolas, Menlo, monospace");

    /// <summary>
    /// The cells' own face for a native view that must match them (the composer): the bundled face when the cells use
    /// it, else the installed family the cells were given, with the bundle behind it.
    /// </summary>
    public static FontFamily CellFace(string cellFamily) =>
        string.Equals(cellFamily, CellFonts.BundledFamily, StringComparison.OrdinalIgnoreCase)
            ? MonoFont
            : new FontFamily($"{cellFamily}, {BundledFonts.Mono}, monospace");

    /// <summary>The native command line's prompt and the status footer's size (the mockup's 13 px UI line).</summary>
    public const double Input = 13;

    /// <summary>Corner radius of a key cap, a chip, a check.</summary>
    public const double RadiusS = 4;

    /// <summary>Corner radius of a row, an input, a button, a card inside a page.</summary>
    public const double RadiusM = 8;

    /// <summary>Corner radius of a surface: the palette, a modal, the settings frame.</summary>
    public const double RadiusL = 12;

    /// <summary>The floor every ink is held to on every plane it is drawn on: WCAG AA for body text.</summary>
    public const double TextFloor = 4.5;

    /// <summary>The shadow a surface casts on what it floats over.</summary>
    public static BoxShadows Shadow { get; } = new(new BoxShadow
    {
        OffsetY = 16,
        Blur = 40,
        Color = Color.FromArgb(0x66, 0, 0, 0),
    });

    /// <summary>The raised plane an input, a key cap or a hovered row sits on.</summary>
    public Color Raised => KeyFill;

    /// <summary>The Dark theme's tokens: the canonical dark set.</summary>
    public static NativeTokens Dark => DarkTokens.Value;

    /// <summary>The Light theme's tokens: the canonical light set.</summary>
    public static NativeTokens Light => LightTokens.Value;

    // Lazy, because a static initialiser here would run before the canonical sets below are assigned.
    private static readonly Lazy<NativeTokens> DarkTokens = new(() => For(ThemeLibrary.Dark()));
    private static readonly Lazy<NativeTokens> LightTokens = new(() => For(ThemeLibrary.Light()));

    /// <summary>The tokens for a theme, or for the built-in theme of the variant when the host was told no theme.</summary>
    public static NativeTokens For(Theme? theme, bool isDark) =>
        theme is not null ? For(theme) : isDark ? Dark : Light;

    /// <summary>The tokens for <paramref name="theme"/>.</summary>
    public static NativeTokens For(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var light = Contrast.RelativeLuminance(theme.Background) >= 0.18;
        var canon = light ? LightCanon : DarkCanon;
        var reference = light ? new Rgb(0xfa, 0xfa, 0xfa) : new Rgb(0x1e, 0x1e, 0x1e);
        var shift = (theme.Background.R - reference.R, theme.Background.G - reference.G, theme.Background.B - reference.B);

        var card = Shift(canon.Card, shift);
        var edge = Shift(canon.Edge, shift);
        var rule = Shift(canon.Rule, shift);
        var selected = Shift(canon.Selected, shift);
        var keyFill = Shift(canon.KeyFill, shift);
        Rgb[] planes = [card, selected, keyFill];

        var accent = Held(ChromeInk.BaseAccent, planes);
        var accentInk = Contrast.Legible(
            Contrast.RelativeLuminance(accent) < 0.18 ? new Rgb(0xff, 0xff, 0xff) : canon.AccentInk, accent, TextFloor);

        return new NativeTokens(
            !light,
            canon.Scrim,
            Colour(card),
            Colour(edge),
            Colour(rule),
            Colour(Held(canon.Text, planes)),
            Colour(Held(canon.Dim, planes)),
            Colour(Held(canon.Header, planes)),
            Colour(selected),
            Colour(accent),
            Colour(accentInk),
            Colour(keyFill),
            Colour(Held(canon.KeyInk, [keyFill])),
            Colour(Held(canon.Warn, planes)),
            Colour(Held(ChromeInk.BaseNotice, planes)));
    }

    /// <summary>A brush of <paramref name="colour"/>.</summary>
    public SolidColorBrush Brush(Color colour) => new(colour);

    /// <summary>
    /// <paramref name="colour"/> drawn as a quiet fill behind text in it (a chip, a tag): the colour at low alpha over
    /// the card, which keeps it the colour's tag on either variant.
    /// </summary>
    public SolidColorBrush Tint(Color colour) => new(Color.FromArgb(0x2a, colour.R, colour.G, colour.B));

    /// <summary>
    /// Hands Fluent the tokens, for a scope (the palette's card, a modal's, the settings window): the accent for its own
    /// accent buttons, checks, toggles and focus rings, with an ink on it that is legible in both variants, and the
    /// tokens' planes for its inputs, buttons, lists and menus — so a Fluent control inside a surface wears the surface's
    /// language and not Fluent's blue on black.
    /// </summary>
    public void Apply(IResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        resources["ContentControlThemeFontFamily"] = UiFont;
        foreach (var key in new[]
        {
            "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
            "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        })
        {
            resources[key] = Accent;
        }

        void Set(SolidColorBrush brush, params string[] keys)
        {
            foreach (var key in keys)
            {
                resources[key] = brush;
            }
        }

        Set(new SolidColorBrush(AccentInk),
            "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed",
            "ToggleButtonForegroundChecked", "ToggleButtonForegroundCheckedPointerOver", "ToggleButtonForegroundCheckedPressed",
            "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver",
            "CheckBoxCheckGlyphForegroundCheckedPressed", "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver",
            "ToggleSwitchKnobFillOnPressed");
        Set(Brush(Accent),
            "TextControlBorderBrushFocused", "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
            "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver",
            "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver",
            "SliderThumbBackground", "SliderTrackValueFill");
        Set(Brush(Raised),
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "ButtonBackground", "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundFocused",
            "RepeatButtonBackground", "ToggleSwitchFillOff", "ToggleSwitchFillOffPointerOver");
        Set(Brush(Selected),
            "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ComboBoxBackgroundPressed",
            "RepeatButtonBackgroundPointerOver", "ListBoxItemBackgroundPointerOver", "ComboBoxItemBackgroundPointerOver",
            "ListBoxItemBackgroundSelected", "ListBoxItemBackgroundSelectedPointerOver", "ListBoxItemBackgroundSelectedPressed",
            "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver", "MenuFlyoutItemBackgroundPointerOver",
            "TabItemHeaderBackgroundSelected");
        Set(Brush(Edge),
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "ButtonBorderBrush", "ButtonBorderBrushPointerOver",
            "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver", "RepeatButtonBorderBrush", "ToggleSwitchStrokeOff",
            "ToggleSwitchStrokeOffPointerOver", "CheckBoxCheckBackgroundStrokeUnchecked",
            "CheckBoxCheckBackgroundStrokeUncheckedPointerOver", "ComboBoxDropDownBorderBrush", "MenuFlyoutPresenterBorderBrush");
        Set(Brush(Card), "ComboBoxDropDownBackground", "MenuFlyoutPresenterBackground", "FlyoutPresenterBackground");
        Set(Brush(Text),
            "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused", "ButtonForeground",
            "ButtonForegroundPointerOver", "ButtonForegroundPressed", "ComboBoxForeground", "ListBoxItemForeground",
            "ListBoxItemForegroundSelected", "ListBoxItemForegroundPointerOver", "ComboBoxItemForeground",
            "ComboBoxItemForegroundSelected", "TabItemHeaderForegroundSelected", "TabItemHeaderForegroundSelectedPointerOver",
            "ToggleSwitchKnobFillOff", "ToggleSwitchContentForeground", "CheckBoxForegroundUnchecked", "CheckBoxForegroundChecked");
        Set(Brush(Dim),
            "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
            "TextControlPlaceholderForegroundFocused", "TabItemHeaderForegroundUnselected",
            "TabItemHeaderForegroundUnselectedPointerOver");
        Set(Brush(Accent), "TabItemHeaderSelectedPipeFill");
        resources["ControlCornerRadius"] = new CornerRadius(RadiusM);
        resources["OverlayCornerRadius"] = new CornerRadius(RadiusM);
    }

    /// <summary>
    /// Hands the Simple theme the tokens, for a control drawn in it rather than in Fluent (the right-click and header
    /// menus): its background, ink, borders and highlight, so a menu wears the palette's card and not Simple's grey.
    /// </summary>
    public void ApplySimple(IResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        resources["ThemeBackgroundColor"] = Card;
        resources["ThemeBackgroundBrush"] = Brush(Card);
        resources["ThemeForegroundColor"] = Text;
        resources["ThemeForegroundBrush"] = Brush(Text);
        resources["ThemeForegroundLowBrush"] = Brush(Dim);
        foreach (var key in new[] { "ThemeBorderLowBrush", "ThemeBorderMidBrush", "ThemeBorderHighBrush" })
        {
            resources[key] = Brush(Edge);
        }

        foreach (var key in new[]
        {
            "ThemeControlLowBrush", "ThemeControlMidBrush", "ThemeControlHighlightLowBrush", "ThemeControlHighlightMidBrush",
            "ThemeControlHighlightHighBrush", "ThemeAccentBrush3", "ThemeAccentBrush4",
        })
        {
            resources[key] = Brush(Selected);
        }

        resources["ThemeAccentBrush"] = Brush(Accent);
        resources["ThemeAccentBrush2"] = Brush(Accent);
        resources["HighlightBrush"] = Brush(Selected);
        resources["HighlightForegroundBrush"] = Brush(Text);
    }

    // ── the canonical sets ──────────────────────────────────────────────────────────────────────

    /// <summary>One side of mid-grey's set, before a theme is laid onto it.</summary>
    private sealed record Canon(
        Color Scrim, Rgb Card, Rgb Edge, Rgb Rule, Rgb Text, Rgb Dim, Rgb Header, Rgb Selected, Rgb AccentInk, Rgb KeyFill,
        Rgb KeyInk, Rgb Warn);

    /// <summary>The palette's dark card, as it was drawn before there were tokens.</summary>
    private static readonly Canon DarkCanon = new(
        Color.FromArgb(0x73, 0, 0, 0),
        new Rgb(0x1e, 0x1e, 0x25),
        new Rgb(0x3a, 0x3a, 0x46),
        new Rgb(0x2c, 0x2c, 0x36),
        new Rgb(0xec, 0xec, 0xf1),
        new Rgb(0x9a, 0x9a, 0xab),
        new Rgb(0x80, 0x80, 0x8f),
        new Rgb(0x22, 0x40, 0x3a),
        new Rgb(0x06, 0x23, 0x1b),
        new Rgb(0x2a, 0x2a, 0x34),
        new Rgb(0xcf, 0xcf, 0xdb),
        new Rgb(0xff, 0xb4, 0x54));

    /// <summary>The palette's light card.</summary>
    private static readonly Canon LightCanon = new(
        Color.FromArgb(0x4d, 0, 0, 0),
        new Rgb(0xff, 0xff, 0xff),
        new Rgb(0xd4, 0xd4, 0xdd),
        new Rgb(0xe6, 0xe6, 0xec),
        new Rgb(0x1d, 0x1d, 0x24),
        new Rgb(0x62, 0x62, 0x70),
        new Rgb(0x6a, 0x6a, 0x78),
        new Rgb(0xd5, 0xef, 0xe7),
        new Rgb(0xff, 0xff, 0xff),
        new Rgb(0xf1, 0xf1, 0xf5),
        new Rgb(0x40, 0x40, 0x4c),
        new Rgb(0xa3, 0x4a, 0x00));

    /// <summary>An ink moved, if it must be, until it clears <see cref="TextFloor"/> on every plane given.</summary>
    private static Rgb Held(Rgb ink, IReadOnlyList<Rgb> planes)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var plane in planes)
            {
                ink = Contrast.Legible(ink, plane, TextFloor);
            }
        }

        return ink;
    }

    private static Rgb Shift(Rgb rgb, (int R, int G, int B) d) =>
        new(Channel(rgb.R + d.R), Channel(rgb.G + d.G), Channel(rgb.B + d.B));

    private static byte Channel(int value) => (byte)Math.Clamp(value, 0, 255);

    private static Color Colour(Rgb rgb) => Color.FromRgb(rgb.R, rgb.G, rgb.B);
}
