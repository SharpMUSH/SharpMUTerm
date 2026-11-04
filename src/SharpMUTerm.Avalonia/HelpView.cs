using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Documents;
using SharpMUTerm.Client.Help;
using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's help: a search box, the rows of <see cref="HelpModel"/> under their headings, and a how-to page in place
/// of the list once one is chosen. The rows are <see cref="HelpModel.View"/> for the typed query, the function the cells'
/// help lists, so both hosts list the same rows. ⏎ on a slash command places it on the command line and sends nothing; ⏎
/// on a key runs the command it names; ⏎ on a how-to opens its page. Every word is the window's (<see cref="NativeKeys"/>).
/// </summary>
internal sealed class HelpView : DockPanel, IModalView
{
    private readonly HelpSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly TextBox _box;
    private readonly TextBlock _count;
    private readonly TextBlock _footer;
    private readonly Border _frame;
    private readonly StackPanel _list;
    private readonly ScrollViewer _page;
    private readonly StackPanel _pageText;
    private readonly List<Border> _rows = [];
    private IReadOnlyList<HelpEntry> _view = [];
    private int _selected;
    private string _query;

    public HelpView(HelpSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;

        _box = ModalParts.SearchBox("Search help: keys, slash commands, how-tos…", "Search help", colours);
        SetDock(_box, Dock.Top);
        Children.Add(_box);

        _count = ModalParts.Dim(string.Empty, colours);
        _count.HorizontalAlignment = HorizontalAlignment.Right;
        SetDock(_count, Dock.Top);
        Children.Add(_count);

        _footer = ModalParts.Footer(string.Empty, colours);
        SetDock(_footer, Dock.Bottom);
        Children.Add(_footer);

        var panel = new Panel();
        var (frame, list, _) = ModalParts.List(colours);
        _frame = frame;
        _list = list;
        frame.MinHeight = 160;
        panel.Children.Add(frame);

        _pageText = new StackPanel { Margin = new Thickness(NativeTokens.Space24, NativeTokens.Space4, NativeTokens.Space24, NativeTokens.Space12), Spacing = NativeTokens.Space8 };
        _page = new ScrollViewer { Content = _pageText, IsVisible = false, MinHeight = 160 };
        panel.Children.Add(_page);
        Children.Add(panel);

        _box.Text = session.InitialQuery;
        _box.CaretIndex = session.InitialQuery.Length;
        _query = session.InitialQuery;
        _box.TextChanged += (_, _) =>
        {
            // Avalonia raises this after the fact, the initial text included; only a query that changed redraws.
            var query = _box.Text ?? string.Empty;
            if (query == _query)
            {
                return;
            }

            _query = query;
            _selected = 0;
            ShowList();
        };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);

        if (session.Model.Topic(session.InitialTopic) is { } topic)
        {
            ShowPage(topic);
        }
        else
        {
            ShowList();
        }
    }

    public string Title => "Help";

    public double PreferredWidth => 760;

    public bool Centred => false;

    /// <summary>The rows listed now, for a test.</summary>
    public IReadOnlyList<HelpEntry> Rows => _view;

    /// <summary>The row ⏎ acts on.</summary>
    public int Selected => _selected;

    /// <summary>The page showing, or null for the list.</summary>
    public string? Page { get; private set; }

    /// <summary>The search box, for a test.</summary>
    public TextBox Box => _box;

    public void FocusFirst() => _box.Focus();

    public void Detach()
    {
    }

    /// <summary>⏎ on listed row <paramref name="row"/>: a how-to opens its page, anything else ends help and acts.</summary>
    public void Choose(int row)
    {
        if (row < 0 || row >= _view.Count)
        {
            return;
        }

        var entry = _view[row];
        if (entry.Kind == HelpKind.Topic)
        {
            if (_session.Model.Topic(entry.Target) is { } topic)
            {
                ShowPage(topic);
            }

            return;
        }

        var index = IndexOf(entry);
        if (index >= 0 && _session.Choose(index))
        {
            _done();
        }
    }

    /// <summary>Esc: a page goes back to the list; from the list help closes.</summary>
    public void Back()
    {
        if (Page is not null)
        {
            ShowList();
            _box.Focus();
            return;
        }

        _session.Dismiss();
        _done();
    }

    private int IndexOf(HelpEntry entry)
    {
        var entries = _session.Model.Entries;
        for (var i = 0; i < entries.Count; i++)
        {
            if (ReferenceEquals(entries[i], entry))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The face a help page's table rows (the map's keys) are set in, so their columns line up.</summary>
    private static FontFamily TableFace { get; } = NativeTokens.MonoFont;

    private void ShowPage(HelpTopic topic)
    {
        Page = topic.Id;
        _frame.IsVisible = false;
        _page.IsVisible = true;
        _box.IsVisible = false;
        _count.Text = string.Empty;
        _pageText.Children.Clear();
        _pageText.Children.Add(new TextBlock
        {
            Text = NativeKeys.Words(topic.Title),
            FontSize = NativeTokens.Heading,
            FontWeight = FontWeight.SemiBold,
            Foreground = _colours.Brush(_colours.Text),
            TextWrapping = TextWrapping.Wrap,
        });
        _pageText.Children.Add(new TextBlock
        {
            Text = NativeKeys.Words(topic.Summary),
            FontSize = NativeTokens.Body,
            Foreground = _colours.Brush(_colours.Dim),
            TextWrapping = TextWrapping.Wrap,
        });
        // The cells' blocks (HelpPage): a heading over each paragraph, keys as key caps, the map's table rows in the
        // cell face. No blank paragraphs: the stack's spacing and a heading's top margin are the gaps, so the page has no
        // tall empty bands.
        HelpBlock? previous = null;
        foreach (var block in HelpPage.Blocks(topic))
        {
            if (block.Heading is { } heading)
            {
                _pageText.Children.Add(new TextBlock
                {
                    Text = NativeKeys.Words(heading),
                    FontSize = NativeTokens.Title,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = _colours.Brush(_colours.Header),
                    Margin = new Thickness(0, previous is null ? NativeTokens.Space4 : NativeTokens.Space8, 0, -NativeTokens.Space4),
                });
            }

            var text = new TextBlock
            {
                FontSize = NativeTokens.Body + 1,
                Foreground = _colours.Brush(_colours.Text),
                TextWrapping = block.Preformatted ? TextWrapping.NoWrap : TextWrapping.Wrap,
                LineHeight = (NativeTokens.Body + 1) * 1.45,
                Margin = block.Preformatted && previous is { Preformatted: true } ? new Thickness(NativeTokens.Space12, -NativeTokens.Space6, 0, 0)
                    : block.Preformatted ? new Thickness(NativeTokens.Space12, 0, 0, 0) : default,
            };
            if (block.Preformatted)
            {
                text.FontFamily = TableFace;
            }

            foreach (var span in block.Spans)
            {
                text.Inlines!.Add(span.IsKey
                    ? new Run(" " + NativeKeys.Words(span.Text) + " ")
                    {
                        Background = _colours.Brush(_colours.KeyFill),
                        Foreground = _colours.Brush(_colours.KeyInk),
                        FontWeight = FontWeight.SemiBold,
                        FontSize = NativeTokens.Body,
                    }
                    : new Run(NativeKeys.Words(span.Text)));
            }

            _pageText.Children.Add(text);
            previous = block;
        }

        var back = ModalParts.Button("Back to help", "Esc", _colours, Back);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new Thickness(0, NativeTokens.Space10, 0, 0);
        _pageText.Children.Add(back);
        _page.Offset = default;
        _footer.Text = "Up and Down scroll · Esc back to the list";
    }

    private void ShowList()
    {
        Page = null;
        _frame.IsVisible = true;
        _page.IsVisible = false;
        _box.IsVisible = true;
        var model = _session.Model;
        _view = model.View(_box.Text ?? string.Empty);
        _count.Text = _view.Count == model.Entries.Count ? $"{_view.Count} entries" : $"{_view.Count} of {model.Entries.Count}";
        _footer.Text = "Up and Down pick · Enter opens, places or runs it · Esc closes";
        _list.Children.Clear();
        _rows.Clear();
        _selected = _view.Count == 0 ? 0 : Math.Clamp(_selected, 0, _view.Count - 1);
        if (_view.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = NativeKeys.Words(HelpModel.NoMatches),
                Foreground = _colours.Brush(_colours.Dim),
                Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space10),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        string? section = null;
        for (var i = 0; i < _view.Count; i++)
        {
            var entry = _view[i];
            if (entry.Section != section)
            {
                _list.Children.Add(ModalParts.GroupHeader(entry.Section, _colours, section is null));
                section = entry.Section;
            }

            var index = i;
            var line = new DockPanel();
            if (entry.Key.Length > 0)
            {
                var chip = ModalParts.Key(entry.Key, _colours);
                chip.Margin = new Thickness(NativeTokens.Space10, 0, 0, 0);
                SetDock(chip, Dock.Right);
                line.Children.Add(chip);
            }

            var title = new TextBlock
            {
                Text = NativeKeys.Words(entry.Title),
                Foreground = _colours.Brush(_colours.Text),
                FontSize = NativeTokens.Title,
                Width = 240,
                Margin = new Thickness(0, 0, NativeTokens.Space14, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetDock(title, Dock.Left);
            line.Children.Add(title);
            line.Children.Add(new TextBlock
            {
                Text = NativeKeys.Words(entry.Detail),
                Foreground = _colours.Brush(_colours.Dim),
                FontSize = NativeTokens.Body,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var row = ModalParts.Row(line, NativeKeys.Words(entry.Title), () => Select(index), () => Choose(index));
            _rows.Add(row);
            _list.Children.Add(row);
        }

        Highlight();
    }

    private void Select(int index)
    {
        if (index != _selected)
        {
            _selected = index;
            Highlight();
        }
    }

    private void Highlight()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            ModalParts.Mark(_rows[i], i == _selected, _colours);
        }

        if (_selected < _rows.Count)
        {
            _rows[_selected].BringIntoView();
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (ModalParts.FocusedButtonTakes(e))
        {
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Back();
                return;
            case Key.Up when Page is null:
                e.Handled = true;
                Select(ModalParts.Step(_selected, -1, _view.Count));
                return;
            case Key.Down when Page is null:
                e.Handled = true;
                Select(ModalParts.Step(_selected, 1, _view.Count));
                return;
            case Key.Up:
                e.Handled = true;
                _page.LineUp();
                return;
            case Key.Down:
                e.Handled = true;
                _page.LineDown();
                return;
            case Key.Enter when Page is null:
                e.Handled = true;
                Choose(_selected);
                return;
            case Key.Back when Page is not null:
                e.Handled = true;
                Back();
                return;
        }
    }
}
