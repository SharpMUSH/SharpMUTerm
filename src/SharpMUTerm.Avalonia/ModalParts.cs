using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace SharpMUTerm.Avalonia;

/// <summary>The pieces the native modals are built from, so the five read as one family with the palette.</summary>
internal static class ModalParts
{
    /// <summary>The modal's title line.</summary>
    public static TextBlock Heading(string text, NativeTokens colours) => new()
    {
        Text = NativeKeys.Words(text),
        FontSize = NativeTokens.Heading,
        FontWeight = FontWeight.SemiBold,
        Foreground = colours.Brush(colours.Text),
        Margin = new Thickness(NativeTokens.Space20, NativeTokens.Space16, NativeTokens.Space20, NativeTokens.Space4),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A quiet line: a count, a scope, an explanation.</summary>
    public static TextBlock Dim(string text, NativeTokens colours, Thickness? margin = null) => new()
    {
        Text = NativeKeys.Words(text),
        FontSize = NativeTokens.Body,
        Foreground = colours.Brush(colours.Dim),
        Margin = margin ?? new Thickness(NativeTokens.Space20, 0, NativeTokens.Space20, NativeTokens.Space6),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>
    /// The keys the modal answers, in the window's words, on a band along the card's foot — the mockup's footer: the
    /// ground the card's inputs sit in, ruled off above, the keys right-aligned on it.
    /// </summary>
    public static TextBlock Footer(string text, NativeTokens colours) => new()
    {
        Text = NativeKeys.Words(text),
        FontSize = NativeTokens.Small,
        Foreground = colours.Brush(colours.Dim),
        Background = colours.Brush(colours.Well),
        Padding = new Thickness(NativeTokens.Space20, NativeTokens.Space8, NativeTokens.Space20, NativeTokens.Space10),
        Margin = new Thickness(0, NativeTokens.Space10, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        TextAlignment = TextAlignment.Right,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A group header in a list (BUILT IN, YOURS), the palette's.</summary>
    public static TextBlock GroupHeader(string text, NativeTokens colours, bool first) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = NativeTokens.Small,
        FontWeight = FontWeight.SemiBold,
        LetterSpacing = 0.8,
        Foreground = colours.Brush(colours.Header),
        Margin = new Thickness(NativeTokens.Space12, first ? NativeTokens.Space8 : NativeTokens.Space14, NativeTokens.Space12, NativeTokens.Space4),
    };

    /// <summary>
    /// A search box, the palette's: the mockup's header search — sunk into the ground the page sits on, a rule for its
    /// edge that turns to the focused edge while it has the keyboard, a magnifier before the text.
    /// </summary>
    public static TextBox SearchBox(string placeholder, string name, NativeTokens? colours = null)
    {
        var box = new TextBox
        {
            PlaceholderText = placeholder,
            FontSize = NativeTokens.Title,
            Margin = new Thickness(NativeTokens.Space14, NativeTokens.Space14, NativeTokens.Space14, NativeTokens.Space8),
            Padding = new Thickness(NativeTokens.Space8, NativeTokens.Space8, NativeTokens.Space12, NativeTokens.Space8),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
        };
        if (colours is not null)
        {
            Sunk(box, colours);
        }

        global::Avalonia.Automation.AutomationProperties.SetName(box, name);
        return box;
    }

    /// <summary>
    /// Sinks <paramref name="box"/> into the ground (<see cref="NativeTokens.Well"/>), its edge a rule that becomes
    /// <see cref="NativeTokens.Focus"/> while it has the keyboard, with a magnifier before the text when
    /// <paramref name="magnifier"/>.
    /// </summary>
    public static void Sunk(TextBox box, NativeTokens colours, bool magnifier = true)
    {
        var well = colours.Brush(colours.Well);
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
        {
            box.Resources[key] = well;
        }

        box.Resources["TextControlBorderBrush"] = colours.Brush(colours.Edge);
        box.Resources["TextControlBorderBrushPointerOver"] = colours.Brush(colours.Focus);
        box.Resources["TextControlBorderBrushFocused"] = colours.Brush(colours.Focus);
        box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(NativeTokens.Hairline);
        if (magnifier)
        {
            box.InnerLeftContent = Magnifier(colours);
        }
    }

    /// <summary>The magnifier the mockup draws in its search box, in the quiet ink.</summary>
    public static Control Magnifier(NativeTokens colours) => new global::Avalonia.Controls.Shapes.Path
    {
        Data = Geometry.Parse("M 6.5 1 A 5.5 5.5 0 1 1 6.5 12 A 5.5 5.5 0 1 1 6.5 1 Z M 10.5 10.5 L 14 14"),
        Stroke = colours.Brush(colours.Dim),
        StrokeThickness = 1.6,
        StrokeLineCap = PenLineCap.Round,
        Width = 15,
        Height = 15,
        Stretch = Stretch.None,
        Margin = new Thickness(NativeTokens.Space12, 0, NativeTokens.Space2, 0),
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
    };

    /// <summary>A list of rows that scrolls, ruled above and below.</summary>
    public static (Border Frame, StackPanel List, ScrollViewer Scroller) List(NativeTokens colours)
    {
        var list = new StackPanel { Margin = new Thickness(NativeTokens.Space8, NativeTokens.Space4, NativeTokens.Space8, NativeTokens.Space4) };
        var scroller = new ScrollViewer
        {
            Content = list,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var frame = new Border
        {
            BorderBrush = colours.Brush(colours.Rule),
            BorderThickness = new Thickness(0, NativeTokens.Hairline, 0, NativeTokens.Hairline),
            Child = scroller,
        };
        return (frame, list, scroller);
    }

    /// <summary>A row the pointer can pick: hover selects on movement only, a click chooses.</summary>
    public static Border Row(Control content, string name, Action select, Action choose)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            Padding = new Thickness(NativeTokens.Space12 - NativeTokens.MarkWidth, NativeTokens.Space8, NativeTokens.Space12, NativeTokens.Space8),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(NativeTokens.MarkWidth, 0, 0, 0),
            Child = content,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        global::Avalonia.Automation.AutomationProperties.SetName(row, name);
        row.PointerMoved += (_, _) => select();
        row.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                select();
                choose();
            }
        };
        return row;
    }

    /// <summary>
    /// <paramref name="text"/> with the run at <paramref name="start"/> marked in the accent and bold — where a query
    /// matched, as the cells mark it.
    /// </summary>
    public static InlineCollection Marked(string text, int start, int length, NativeTokens colours)
    {
        var inlines = new InlineCollection();
        if (start < 0 || length <= 0 || start + length > text.Length)
        {
            inlines.Add(new Run(text));
            return inlines;
        }

        if (start > 0)
        {
            inlines.Add(new Run(text[..start]));
        }

        inlines.Add(new Run(text.Substring(start, length)) { FontWeight = FontWeight.Bold, Foreground = colours.Brush(colours.Accent) });
        if (start + length < text.Length)
        {
            inlines.Add(new Run(text[(start + length)..]));
        }

        return inlines;
    }

    /// <summary>A key chip, as the palette draws a key.</summary>
    public static Border Key(string key, NativeTokens colours) => Chip(NativeKeys.Words(key), colours, new Thickness(NativeTokens.Space6, 0, 0, 0));

    /// <summary>
    /// One key cap: the raised plane, a rule for its edge, the key in the cells' mono at the small size — the mockup's
    /// <c>Alt+1</c> given a cap. Every native list (⌃P, help, the modals) draws a key with this.
    /// </summary>
    public static Border Chip(string words, NativeTokens colours, Thickness margin = default) => new()
    {
        Background = colours.Brush(colours.KeyFill),
        BorderBrush = colours.Brush(colours.Edge),
        BorderThickness = new Thickness(NativeTokens.Hairline),
        CornerRadius = new CornerRadius(NativeTokens.RadiusS),
        Padding = new Thickness(NativeTokens.Space6, NativeTokens.Hairline, NativeTokens.Space6, NativeTokens.Space2),
        Margin = margin,
        MinWidth = 22,
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = words,
            FontFamily = NativeTokens.KeyFace,
            FontSize = NativeTokens.Small,
            Foreground = colours.Brush(colours.KeyInk),
            HorizontalAlignment = HorizontalAlignment.Center,
        },
    };

    /// <summary>
    /// Paints <paramref name="row"/> selected or not: the selected row's plane, and the accent bar down its left edge
    /// that the mockup's selected navigation wears — so the selection reads at a glance on every theme, not only by a
    /// step in luminance.
    /// </summary>
    public static void Mark(Border row, bool selected, NativeTokens colours)
    {
        row.Background = selected ? colours.Brush(colours.Selected) : Brushes.Transparent;
        row.BorderBrush = selected ? colours.Brush(colours.Accent) : Brushes.Transparent;
        row.BorderThickness = new Thickness(NativeTokens.MarkWidth, 0, 0, 0);
    }

    /// <summary>A button whose label is followed by the key that presses it.</summary>
    public static Button Button(string label, string? key, NativeTokens colours, Action click, bool accent = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new TextBlock { Text = NativeKeys.Words(label), VerticalAlignment = VerticalAlignment.Center });
        if (key is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = NativeKeys.Words(key),
                FontSize = NativeTokens.Small,
                Opacity = 0.7,
                Margin = new Thickness(NativeTokens.Space8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(NativeTokens.Space14, 0),
            MinHeight = NativeTokens.ControlHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            FontWeight = accent ? FontWeight.SemiBold : FontWeight.Normal,
        };
        if (accent)
        {
            button.Classes.Add("accent");
        }

        global::Avalonia.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Moves a list's selection, wrapping, as ⌃P's and the cells' lists do.</summary>
    public static int Step(int selected, int delta, int count) =>
        count == 0 ? -1 : ((selected + delta) % count + count) % count;

    /// <summary>A key the pure prompt models read, for a key that types nothing (or a letter with no modifier).</summary>
    public static ConsoleKeyInfo? Console(KeyEventArgs e)
    {
        if (KeyMapper.FromKeyDown(e.Key, e.KeyModifiers) is { } mapped)
        {
            return mapped;
        }

        // A bare letter (y, n on the quit prompt) arrives as text; its key is enough for a prompt that reads letters.
        if (e.Key is >= global::Avalonia.Input.Key.A and <= global::Avalonia.Input.Key.Z && e.KeyModifiers == KeyModifiers.None)
        {
            var c = (char)('a' + (e.Key - global::Avalonia.Input.Key.A));
            return new ConsoleKeyInfo(c, KeyMapper.CharToConsoleKey(c), false, false, false);
        }

        return null;
    }

    /// <summary>
    /// True when Enter or Space belongs to the button the keyboard is on rather than to the modal: a surface's own Enter
    /// (go to the hit, run the row) is its default, and Tab having reached a button means the player chose that button.
    /// Every native modal asks this first, so Enter and Space mean the same on every surface's buttons.
    /// </summary>
    public static bool FocusedButtonTakes(global::Avalonia.Input.KeyEventArgs e) =>
        e.KeyModifiers == global::Avalonia.Input.KeyModifiers.None
        && e.Key is global::Avalonia.Input.Key.Enter or global::Avalonia.Input.Key.Space
        && e.Source is global::Avalonia.Controls.Button or global::Avalonia.Controls.Primitives.ToggleButton;
}
