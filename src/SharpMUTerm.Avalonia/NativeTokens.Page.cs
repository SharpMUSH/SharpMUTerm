using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The page tokens: the planes a full page is built from (the settings window), as against a surface floating over the
/// cells (the palette, a modal). The settings mockup lays a page out on three planes — a ground, a navigation column a
/// step above it and cards a step above that — with hairline rules, inputs sunk into the ground and a yellow mark for a
/// search match. The Dark set is the mockup's own; the Light set mirrors it; any other theme gets the same shift of its
/// background the canonical card gets, and every ink is held to <see cref="TextFloor"/> on every page plane.
/// </summary>
internal sealed partial record NativeTokens
{
    /// <summary>A page's own heading (the settings section's title).</summary>
    public const double PageTitle = 20;

    /// <summary>A card's heading on a page (a settings group).</summary>
    public const double CardTitle = 15;

    /// <summary>A field's label on a page.</summary>
    public const double Label = 13;

    /// <summary>Monospace text on a page: a path, a live value (the mockup's 12 px JetBrains Mono).</summary>
    public const double Code = 12;

    /// <summary>Corner radius of an input, a button or a navigation row on a page.</summary>
    public const double RadiusControl = 6;

    /// <summary>Corner radius of a card on a page.</summary>
    public const double RadiusCard = 10;

    /// <summary>The page planes and inks for this theme.</summary>
    public PageTokens Page => PageFor(this);

    private sealed record PageCanon(
        Rgb Ground, Rgb Nav, Rgb Card, Rgb Rule, Rgb Field, Rgb FieldEdge, Rgb FocusEdge, Rgb Button, Rgb Popup,
        Rgb PopupSelected, Rgb Text, Rgb Dim, Rgb Mark, Rgb MarkInk);

    /// <summary>The settings mockup's dark page.</summary>
    private static readonly PageCanon DarkPage = new(
        new Rgb(0x10, 0x13, 0x1a),
        new Rgb(0x14, 0x18, 0x22),
        new Rgb(0x17, 0x1b, 0x24),
        new Rgb(0x2c, 0x34, 0x47),
        new Rgb(0x10, 0x13, 0x1a),
        new Rgb(0x2c, 0x34, 0x47),
        new Rgb(0x3a, 0x4f, 0x73),
        new Rgb(0x1d, 0x23, 0x33),
        new Rgb(0x1d, 0x23, 0x33),
        new Rgb(0x23, 0x30, 0x49),
        new Rgb(0xd7, 0xde, 0xec),
        new Rgb(0x9a, 0xa4, 0xb8),
        new Rgb(0x4a, 0x42, 0x20),
        new Rgb(0xf5, 0xe6, 0x63));

    /// <summary>The same page mirrored for a light theme.</summary>
    private static readonly PageCanon LightPage = new(
        new Rgb(0xf3, 0xf4, 0xf7),
        new Rgb(0xec, 0xee, 0xf3),
        new Rgb(0xff, 0xff, 0xff),
        new Rgb(0xdd, 0xe1, 0xe8),
        new Rgb(0xff, 0xff, 0xff),
        new Rgb(0xc9, 0xcf, 0xda),
        new Rgb(0x7d, 0x8f, 0xb0),
        new Rgb(0xf4, 0xf5, 0xf8),
        new Rgb(0xff, 0xff, 0xff),
        new Rgb(0xe3, 0xea, 0xf5),
        new Rgb(0x1d, 0x22, 0x2c),
        new Rgb(0x5a, 0x63, 0x73),
        new Rgb(0xfb, 0xea, 0xa0),
        new Rgb(0x4a, 0x3c, 0x00));

    private static PageTokens PageFor(NativeTokens tokens)
    {
        var canon = tokens.IsDark ? DarkCanon : LightCanon;
        var page = tokens.IsDark ? DarkPage : LightPage;

        // The theme's shift off the canonical background, recovered from the card it was laid onto.
        var shift = (tokens.Card.R - canon.Card.R, tokens.Card.G - canon.Card.G, tokens.Card.B - canon.Card.B);
        var ground = Shift(page.Ground, shift);
        var nav = Shift(page.Nav, shift);
        var card = Shift(page.Card, shift);
        var field = Shift(page.Field, shift);
        var button = Shift(page.Button, shift);
        var popup = Shift(page.Popup, shift);
        var popupSelected = Shift(page.PopupSelected, shift);
        Rgb[] planes = [ground, nav, card, field, button, popup, popupSelected];
        var accent = new Rgb(tokens.Accent.R, tokens.Accent.G, tokens.Accent.B);
        var mark = Shift(page.Mark, shift);

        return new PageTokens(
            Colour(ground),
            Colour(nav),
            Colour(card),
            Colour(Shift(page.Rule, shift)),
            Colour(field),
            Colour(Shift(page.FieldEdge, shift)),
            Colour(Shift(page.FocusEdge, shift)),
            Colour(button),
            Colour(popup),
            Colour(popupSelected),
            Colour(Held(page.Text, planes)),
            Colour(Held(page.Dim, planes)),
            Colour(Held(accent, planes)),
            Colour(mark),
            Colour(Held(page.MarkInk, [mark])));
    }
}

/// <summary>A page's planes and inks (<see cref="NativeTokens.Page"/>).</summary>
internal sealed record PageTokens(
    Color Ground,
    Color Nav,
    Color Card,
    Color Rule,
    Color Field,
    Color FieldEdge,
    Color FocusEdge,
    Color Button,
    Color Popup,
    Color PopupSelected,
    Color Text,
    Color Dim,
    Color Link,
    Color Mark,
    Color MarkInk)
{
    /// <summary>A brush of <paramref name="colour"/>.</summary>
    public SolidColorBrush Brush(Color colour) => new(colour);

    /// <summary>
    /// Lays the page planes over what <see cref="NativeTokens.Apply"/> handed Fluent: inputs sunk into the ground with a
    /// hairline edge and the focus edge when focused, buttons on the raised button plane, drop-downs on the popup plane,
    /// and the control radius the page uses.
    /// </summary>
    public void Apply(IResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        void Set(Color colour, params string[] keys)
        {
            var brush = Brush(colour);
            foreach (var key in keys)
            {
                resources[key] = brush;
            }
        }

        Set(Field,
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "ComboBoxBackground", "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundFocused");
        Set(FieldEdge,
            "TextControlBorderBrush", "ComboBoxBorderBrush",
            "ButtonBorderBrush", "RepeatButtonBorderBrush", "ToggleSwitchStrokeOff",
            "CheckBoxCheckBackgroundStrokeUnchecked");
        Set(FocusEdge,
            "TextControlBorderBrushFocused", "ComboBoxBorderBrushPointerOver", "ButtonBorderBrushPointerOver",
            "TextControlBorderBrushPointerOver", "ComboBoxDropDownBorderBrush");
        Set(Button, "ButtonBackground", "RepeatButtonBackground", "ToggleSwitchFillOff", "ToggleSwitchFillOffPointerOver",
            "ButtonBackgroundDisabled", "RepeatButtonBackgroundDisabled");

        // A disabled button is the enabled one with its edge and ink quieter, never a darker fill: Fluent's own grey made
        // the one button that does nothing (the list's ↑ at the top) the heaviest thing in its row on a light page.
        Set(Rule, "ButtonBorderBrushDisabled", "RepeatButtonBorderBrushDisabled");
        Set(PopupSelected,
            "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "RepeatButtonBackgroundPointerOver",
            "ComboBoxItemBackgroundPointerOver", "ComboBoxItemBackgroundSelected", "ComboBoxItemBackgroundSelectedPointerOver",
            "ListBoxItemBackgroundPointerOver", "ListBoxItemBackgroundSelected", "ListBoxItemBackgroundSelectedPointerOver",
            "ListBoxItemBackgroundSelectedPressed", "SystemControlHighlightListAccentLowBrush",
            "SystemControlHighlightListAccentMediumBrush", "SystemControlHighlightListAccentHighBrush");
        Set(Card, "TabItemHeaderBackgroundSelected");
        Set(Popup, "ComboBoxDropDownBackground", "FlyoutPresenterBackground", "MenuFlyoutPresenterBackground");
        Set(Text,
            "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused", "ButtonForeground",
            "ButtonForegroundPointerOver", "ButtonForegroundPressed", "ComboBoxForeground", "ComboBoxItemForeground",
            "ComboBoxItemForegroundSelected", "ListBoxItemForeground", "ListBoxItemForegroundPointerOver",
            "ToggleSwitchContentForeground", "CheckBoxForegroundUnchecked", "CheckBoxForegroundChecked");
        Set(Dim, "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
            "TextControlPlaceholderForegroundFocused");
        resources["ControlCornerRadius"] = new CornerRadius(NativeTokens.RadiusControl);
    }
}
