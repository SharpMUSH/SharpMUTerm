using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Text;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// One theme in the settings window's theme field: its preview drawn natively — three panes with their tab strips on
/// the workspace backdrop and the armed command line under them — and its name, kind and whether it is in use. Every
/// colour is the client's (<see cref="ThemeCard.Preview"/>), the instance the cell picker draws, so the two cannot
/// disagree. A click is the card's <see cref="Button.Click"/>; the window hands it to the form.
/// </summary>
internal sealed class ThemeCardView : Button
{
    private readonly Border _frame;
    private readonly TextBlock _state;

    public ThemeCardView(ThemeCard card, bool inUse, NativeTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(card);
        Card = card;
        Padding = new Thickness(0);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Margin = new Thickness(0, 0, 14, 14);
        Width = 300;
        AutomationProperties.SetName(this, $"Use the {card.Name} theme");
        ToolTip.SetTip(this, $"Use {card.Name}");

        var name = new TextBlock { Text = card.Name, FontSize = NativeTokens.Title, FontWeight = FontWeight.SemiBold };
        _state = new TextBlock { FontSize = NativeTokens.Body };
        var words = new StackPanel { Margin = new Thickness(10, 8, 10, 8), Spacing = 1 };
        words.Children.Add(name);
        words.Children.Add(_state);

        var body = new StackPanel();
        body.Children.Add(Preview(card.Preview));
        body.Children.Add(words);

        _frame = new Border
        {
            Child = body,
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            ClipToBounds = true,
            BorderThickness = new Thickness(2),
        };
        Content = _frame;
        SetInUse(inUse, tokens);
    }

    /// <summary>The theme this card shows.</summary>
    public ThemeCard Card { get; }

    /// <summary>Whether the card is marked as the theme in use, for a test.</summary>
    public bool InUse { get; private set; }

    /// <summary>Marks the card as the theme in use, or not.</summary>
    public void SetInUse(bool inUse, NativeTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _frame.Background = tokens.Brush(tokens.Raised);
        _state.Foreground = tokens.Brush(tokens.Dim);
        InUse = inUse;
        var kind = $"{(Card.IsDark ? "dark" : "light")} · {(Card.IsCustom ? "your own" : "built in")}";
        _state.Text = inUse ? $"In use · {kind}" : kind;
        _frame.BorderBrush = tokens.Brush(inUse ? tokens.Accent : tokens.Edge);
    }

    private static readonly FontFamily Mono = FontFamily.Parse("monospace");

    private static IBrush Brush(Rgb colour) => new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));

    /// <summary>The backdrop with the panes side by side on it and the command line under them.</summary>
    private static Border Preview(ThemePreview preview)
    {
        var panes = new Grid { ColumnSpacing = 3 };
        for (var i = 0; i < preview.Panes.Count; i++)
        {
            panes.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var pane = Pane(preview.Panes[i]);
            Grid.SetColumn(pane, i);
            panes.Children.Add(pane);
        }

        var line = preview.CommandLine;
        var command = new TextBlock { FontFamily = Mono, FontSize = NativeTokens.Miniature, Padding = new Thickness(4, 2), TextTrimming = TextTrimming.CharacterEllipsis };
        command.Inlines!.Add(new Run(line.Prompt + " ") { Foreground = Brush(line.PromptInk), FontWeight = FontWeight.Bold });
        command.Inlines.Add(new Run(line.Text) { Foreground = Brush(line.Ink) });

        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(panes);
        stack.Children.Add(new Border { Background = Brush(line.Band), Child = command });

        return new Border { Background = Brush(preview.Backdrop), Padding = new Thickness(3), Child = stack };
    }

    private static Border Pane(PreviewPane pane)
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        foreach (var tab in pane.Tabs)
        {
            var title = new TextBlock { FontFamily = Mono, FontSize = NativeTokens.Miniature, Padding = new Thickness(3, 1) };
            title.Inlines!.Add(new Run(tab.Title) { Foreground = Brush(tab.Ink), FontWeight = tab.Selected ? FontWeight.Bold : FontWeight.Normal });
            if (tab.Badge is { } badge)
            {
                title.Inlines.Add(new Run($" {badge}") { Foreground = Brush(tab.BadgeInk ?? tab.Ink) });
            }

            tabs.Children.Add(new Border { Background = Brush(tab.Chip), Child = title });
        }

        var lines = new StackPanel { Margin = new Thickness(4, 3, 4, 4) };
        foreach (var runs in pane.Lines)
        {
            var text = new TextBlock { FontFamily = Mono, FontSize = NativeTokens.Miniature, TextTrimming = TextTrimming.CharacterEllipsis };
            foreach (var run in runs)
            {
                text.Inlines!.Add(new Run(run.Text) { Foreground = Brush(run.Ink), FontWeight = run.Bold ? FontWeight.Bold : FontWeight.Normal });
            }

            lines.Children.Add(text);
        }

        var stack = new StackPanel();
        stack.Children.Add(new Border { Child = tabs, ClipToBounds = true });
        stack.Children.Add(lines);
        var plane = new Border { Background = Brush(pane.Plane), Child = stack, MinHeight = 70 };
        ToolTip.SetTip(plane, pane.Role);
        return plane;
    }
}
