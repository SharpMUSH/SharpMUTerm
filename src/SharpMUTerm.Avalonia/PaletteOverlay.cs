using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using SharpMUTerm.Client.Palette;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's ⌃P: a rounded card near the top of the window over a faint scrim, with a search box that has the
/// keyboard, the status line the cell palette leads with, and the rows of <see cref="PaletteSession.View"/> — group
/// headers, each entry's title with the matched characters in the accent, its detail dimmed, and its key in a
/// right-aligned column in the host's words (<see cref="NativeKeys.Words"/>). ↑↓ and PgUp/PgDn walk the entries,
/// ⏎ runs the selected one, Esc (or ⌃P, or a click outside the card) closes. The pointer selects what it hovers
/// and runs what it clicks.
/// <para>
/// It lies over everything else in the window — cells, native panels and chrome — because it is the last child of
/// the window's root panel. Fluent is applied to the card only, and its variant follows the client's theme.
/// </para>
/// </summary>
internal sealed class PaletteOverlay : Panel
{
    /// <summary>The widest the card is drawn.</summary>
    private const double CardWidth = 680;

    /// <summary>How far below the window's top the card sits.</summary>
    private const double CardTop = 56;

    private readonly PaletteSession _session;
    private readonly Action<PaletteOverlay> _remove;
    private readonly NativeTokens _colours;
    private readonly Border _card;
    private readonly TextBox _search;
    private readonly TextBlock _status;
    private readonly StackPanel _list;
    private readonly ScrollViewer _scroller;
    private readonly List<Border> _entryRows = [];
    private readonly List<string> _rowTexts = [];
    private readonly List<string?> _keyTexts = [];
    private PaletteView _view;
    private int _selected = -1;

    /// <param name="session">What to list, and where a choice goes.</param>
    /// <param name="remove">Takes the overlay out of the window and gives the keyboard back to the cells.</param>
    /// <summary>
    /// What the empty search box says: the surface's name first, as the cell popup's title and the header's search box
    /// say it, then what can be typed.
    /// </summary>
    internal const string Placeholder = NativeHeader.SearchText + " — type a command, a character, a window or a setting";

    public PaletteOverlay(PaletteSession session, Action<PaletteOverlay> remove)
    {
        _session = session;
        _remove = remove;
        _colours = NativeTokens.For(session.Theme, session.IsDark);
        _view = session.View(string.Empty);

        Background = new SolidColorBrush(_colours.Scrim);
        Focusable = false;
        AutomationProperties.SetName(this, "Command palette");

        _search = new TextBox
        {
            PlaceholderText = Placeholder,
            FontSize = NativeTokens.Title,
            Margin = new Thickness(14, 14, 14, 6),
            Padding = new Thickness(12, 9),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
        };
        AutomationProperties.SetName(_search, "Command palette");
        _search.TextChanged += (_, _) => Refresh(resetSelection: true);
        DockPanel.SetDock(_search, Dock.Top);

        _status = new TextBlock
        {
            FontSize = NativeTokens.Body,
            Foreground = new SolidColorBrush(_colours.Dim),
            Margin = new Thickness(20, 0, 20, 6),
        };
        DockPanel.SetDock(_status, Dock.Top);

        var footer = new TextBlock
        {
            Text = "↑↓ move   Enter run   Esc close",
            FontSize = NativeTokens.Small,
            Foreground = new SolidColorBrush(_colours.Dim),
            Margin = new Thickness(20, 6, 20, 10),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        _list = new StackPanel { Margin = new Thickness(8, 0, 8, 4) };
        _scroller = new ScrollViewer
        {
            Content = _list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var dock = new DockPanel { LastChildFill = true };
        dock.Children.Add(_search);
        dock.Children.Add(_status);
        dock.Children.Add(footer);
        dock.Children.Add(new Border
        {
            BorderBrush = new SolidColorBrush(_colours.Rule),
            BorderThickness = new Thickness(0, 1, 0, 1),
            Child = _scroller,
        });

        _card = new Border
        {
            Background = new SolidColorBrush(_colours.Card),
            BorderBrush = new SolidColorBrush(_colours.Edge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(NativeTokens.RadiusL),
            BoxShadow = NativeTokens.Shadow,
            ClipToBounds = true,
            Child = dock,
        };

        // Fluent for the card alone, in the client's variant; the rest of the window keeps the Simple theme its
        // menus and tooltips were built against.
        var scope = new ThemeVariantScope
        {
            RequestedThemeVariant = session.IsDark ? ThemeVariant.Dark : ThemeVariant.Light,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, CardTop, 16, 16),
            Child = _card,
        };
        scope.Styles.Add(new FluentTheme());
        _colours.Apply(scope.Resources);
        Children.Add(scope);

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        PointerPressed += OnScrimPressed;
        SizeChanged += (_, _) => Fit();
        Loaded += (_, _) => _search.Focus();

        _search.Text = session.InitialQuery;
        _search.CaretIndex = session.InitialQuery.Length;
        Refresh(resetSelection: true);
    }

    /// <summary>The session shown.</summary>
    public PaletteSession Session => _session;

    /// <summary>The search box, which has the keyboard while the palette is open.</summary>
    public TextBox SearchBox => _search;

    /// <summary>The card, for a test that measures where it sits.</summary>
    public Border Card => _card;

    /// <summary>What is listed now: the model's view for the query in the box.</summary>
    public PaletteView View => _view;

    /// <summary>The selected entry's index in <see cref="PaletteView.Entries"/>, or -1 with nothing listed.</summary>
    public int Selected => _selected;

    /// <summary>Each row as drawn, in order: a header's label, an entry's title.</summary>
    public IReadOnlyList<string> RowTexts => _rowTexts;

    /// <summary>Each entry's key column as drawn, in entry order; null where the entry has no key.</summary>
    public IReadOnlyList<string?> KeyTexts => _keyTexts;

    /// <summary>The drawn row of each entry, in entry order, for a test that hovers or clicks one.</summary>
    public IReadOnlyList<Border> EntryRows => _entryRows;

    /// <summary>The line over the list.</summary>
    public string StatusText => _status.Text ?? string.Empty;

    /// <summary>Runs the selected entry, as ⏎ does.</summary>
    public void Commit()
    {
        if (_selected < 0 || _selected >= _view.Entries.Count)
        {
            return;
        }

        var id = _view.Entries[_selected].Item.Id;
        _remove(this);
        _session.Run(id);
    }

    /// <summary>Closes without running anything, as Esc does.</summary>
    public void Dismiss()
    {
        _remove(this);
        _session.Dismiss();
    }

    /// <summary>Moves the selection by <paramref name="delta"/> entries, wrapping as the cell palette does.</summary>
    public void Move(int delta)
    {
        var count = _view.Entries.Count;
        if (count == 0)
        {
            return;
        }

        Select(((_selected + delta) % count + count) % count, reveal: true);
    }

    private void Fit()
    {
        var width = Math.Max(240, Math.Min(CardWidth, Bounds.Width - 32));
        _card.Width = width;
        _card.MaxHeight = Math.Max(160, Bounds.Height - CardTop - 32);
    }

    private void Refresh(bool resetSelection)
    {
        _view = _session.View(_search.Text ?? string.Empty);
        _status.Text = _session.Status(_view);
        _list.Children.Clear();
        _entryRows.Clear();
        _rowTexts.Clear();
        _keyTexts.Clear();

        var first = true;
        foreach (var row in _view.Rows)
        {
            switch (row)
            {
                case PaletteHeader header:
                    _rowTexts.Add(header.Label);
                    _list.Children.Add(new TextBlock
                    {
                        Text = header.Label,
                        FontSize = NativeTokens.Small,
                        FontWeight = FontWeight.SemiBold,
                        LetterSpacing = 0.8,
                        Foreground = new SolidColorBrush(_colours.Header),
                        Margin = new Thickness(12, first ? 8 : 14, 12, 4),
                    });
                    break;
                case PaletteEntry entry:
                    _rowTexts.Add(entry.Item.Title);
                    var key = entry.Key is null ? null : NativeKeys.Words(entry.Key);
                    _keyTexts.Add(key);
                    var drawn = EntryRow(entry, key, _entryRows.Count);
                    _entryRows.Add(drawn);
                    _list.Children.Add(drawn);
                    break;
            }

            first = false;
        }

        if (_view.Entries.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "No matching commands",
                Foreground = new SolidColorBrush(_colours.Dim),
                Margin = new Thickness(12, 14),
            });
        }

        var selected = resetSelection ? 0 : _selected;
        _selected = -1;
        if (_view.Entries.Count > 0)
        {
            Select(Math.Clamp(selected, 0, _view.Entries.Count - 1), reveal: false);
            _scroller.Offset = default;
        }
    }

    private Border EntryRow(PaletteEntry entry, string? key, int index)
    {
        var title = new TextBlock
        {
            FontSize = NativeTokens.Title,
            Foreground = new SolidColorBrush(_colours.Text),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.Inlines = TitleInlines(entry);

        var left = new DockPanel { LastChildFill = true };
        if (entry.Detail is { Length: > 0 } detail)
        {
            var words = new TextBlock
            {
                Text = NativeKeys.Words(detail),
                FontSize = NativeTokens.Body,
                Foreground = new SolidColorBrush(_colours.Dim),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            DockPanel.SetDock(title, Dock.Left);
            left.Children.Add(title);
            left.Children.Add(words);
        }
        else
        {
            left.Children.Add(title);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(left);
        if (key is not null)
        {
            // A sequence (⌃B |) is a chip per key, so the second key does not read as a caret in the first.
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(16, 0, 0, 0) };
            foreach (var part in key.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                chips.Children.Add(new Border
                {
                    Background = new SolidColorBrush(_colours.KeyFill),
                    BorderBrush = new SolidColorBrush(_colours.Edge),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(NativeTokens.RadiusS),
                    Padding = new Thickness(7, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = part, FontSize = NativeTokens.Body, Foreground = new SolidColorBrush(_colours.KeyInk) },
                });
            }

            Grid.SetColumn(chips, 1);
            grid.Children.Add(chips);
        }

        var row = new Border
        {
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            Padding = new Thickness(12, 7),
            Background = Brushes.Transparent,
            Child = grid,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        AutomationProperties.SetName(row, entry.Item.Title);
        // On movement only: a row that appears under a pointer resting where the card opened is not chosen.
        row.PointerMoved += (_, _) => Select(index, reveal: false);
        row.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                Select(index, reveal: false);
                Commit();
            }
        };
        return row;
    }

    private InlineCollection TitleInlines(PaletteEntry entry)
    {
        var text = entry.Item.Title;
        var inlines = new InlineCollection();
        var at = 0;
        foreach (var range in entry.TitleMatches)
        {
            if (range.Start < at || range.Start + range.Length > text.Length)
            {
                continue;
            }

            if (range.Start > at)
            {
                inlines.Add(new Run(text[at..range.Start]));
            }

            inlines.Add(new Run(text.Substring(range.Start, range.Length))
            {
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(_colours.Accent),
            });
            at = range.Start + range.Length;
        }

        if (at < text.Length)
        {
            inlines.Add(new Run(text[at..]));
        }

        return inlines;
    }

    private void Select(int index, bool reveal)
    {
        if (index == _selected)
        {
            return;
        }

        if (_selected >= 0 && _selected < _entryRows.Count)
        {
            _entryRows[_selected].Background = Brushes.Transparent;
        }

        _selected = index;
        if (index >= 0 && index < _entryRows.Count)
        {
            var row = _entryRows[index];
            row.Background = new SolidColorBrush(_colours.Selected);
            if (reveal)
            {
                row.BringIntoView();
            }
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Move(1);
                break;
            case Key.Up:
                Move(-1);
                break;
            case Key.PageDown:
                Page(1);
                break;
            case Key.PageUp:
                Page(-1);
                break;
            case Key.Enter:
                Commit();
                break;
            case Key.Escape:
                Dismiss();
                break;
            case Key.P when e.KeyModifiers == KeyModifiers.Control:
                // ⌃P is a toggle in the cell palette too.
                Dismiss();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    /// <summary>Moves about a screenful of entries; a page stops at either end rather than wrapping.</summary>
    private void Page(int direction)
    {
        var count = _view.Entries.Count;
        if (count == 0)
        {
            return;
        }

        var rowHeight = _entryRows[0].Bounds.Height > 0 ? _entryRows[0].Bounds.Height : 32;
        var page = (int)Math.Max(1, (_scroller.Viewport.Height / rowHeight) - 1);
        Select(Math.Clamp(_selected + (direction * page), 0, count - 1), reveal: true);
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        // Only a press on the scrim itself; one on the card has already been handled or belongs to the card.
        if (ReferenceEquals(e.Source, this))
        {
            e.Handled = true;
            Dismiss();
        }
    }
}
