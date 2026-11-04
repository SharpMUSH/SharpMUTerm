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
        Margin = new Thickness(20, 16, 20, 4),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A quiet line: a count, a scope, an explanation.</summary>
    public static TextBlock Dim(string text, NativeTokens colours, Thickness? margin = null) => new()
    {
        Text = NativeKeys.Words(text),
        FontSize = NativeTokens.Body,
        Foreground = colours.Brush(colours.Dim),
        Margin = margin ?? new Thickness(20, 0, 20, 6),
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>The keys the modal answers, right-aligned under it, in the window's words.</summary>
    public static TextBlock Footer(string text, NativeTokens colours) => new()
    {
        Text = NativeKeys.Words(text),
        FontSize = NativeTokens.Small,
        Foreground = colours.Brush(colours.Dim),
        Margin = new Thickness(20, 8, 20, 12),
        HorizontalAlignment = HorizontalAlignment.Right,
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
        Margin = new Thickness(12, first ? 8 : 14, 12, 4),
    };

    /// <summary>A search box, the palette's.</summary>
    public static TextBox SearchBox(string placeholder, string name)
    {
        var box = new TextBox
        {
            PlaceholderText = placeholder,
            FontSize = NativeTokens.Title,
            Margin = new Thickness(14, 14, 14, 6),
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
        };
        global::Avalonia.Automation.AutomationProperties.SetName(box, name);
        return box;
    }

    /// <summary>A list of rows that scrolls, ruled above and below.</summary>
    public static (Border Frame, StackPanel List, ScrollViewer Scroller) List(NativeTokens colours)
    {
        var list = new StackPanel { Margin = new Thickness(8, 4, 8, 4) };
        var scroller = new ScrollViewer
        {
            Content = list,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var frame = new Border
        {
            BorderBrush = colours.Brush(colours.Rule),
            BorderThickness = new Thickness(0, 1, 0, 1),
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
            Padding = new Thickness(12, 6),
            Background = Brushes.Transparent,
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
    public static Border Key(string key, NativeTokens colours) => new()
    {
        Background = colours.Brush(colours.KeyFill),
        BorderBrush = colours.Brush(colours.Edge),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(NativeTokens.RadiusS),
        Padding = new Thickness(6, 0),
        Margin = new Thickness(6, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = NativeKeys.Words(key), FontSize = NativeTokens.Small, Foreground = colours.Brush(colours.KeyInk) },
    };

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
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var button = new Button
        {
            Content = content,
            Padding = new Thickness(14, 6),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
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
}
