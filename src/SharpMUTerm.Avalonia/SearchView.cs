using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's ⌃F: a search box, the three toggles as switches (⌥E regex, ⌥A every window, ⌥H older history), the
/// count with its bound and older history's progress, and the hits with the match marked (an older hit tagged so). The client searches (<see cref="SearchSession.Search"/>) and publishes the
/// hits; ⏎ on a row goes through the client's one path to a hit. The keys that are not typing mean what they mean in
/// cells, because they are read by the same rules (<see cref="SearchPrompt.Interpret"/>).
/// </summary>
internal sealed class SearchView : DockPanel, IModalView
{
    private readonly SearchSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly TextBox _box;
    private readonly ToggleButton _regex;
    private readonly ToggleButton _all;
    private readonly ToggleButton _older;
    private readonly TextBlock _state;
    private readonly TextBlock _note;
    private readonly TextBlock _count;
    private readonly StackPanel _list;
    private readonly ScrollViewer _scroller;
    private readonly List<Border> _rows = [];
    private SearchModel _model;
    private int _selected;
    private bool _syncing;

    public SearchView(SearchSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        _model = session.Model;

        _box = ModalParts.SearchBox("Search the output…", "Search", colours);
        _box.Text = _model.Query;
        SetDock(_box, Dock.Top);
        Children.Add(_box);

        var toggles = new DockPanel { Margin = new Thickness(16, 0, 16, 6) };
        _regex = Toggle("Regex", "Alt+E", _model.Regex);
        _all = Toggle("All windows", "Alt+A", _model.AllWindows);
        _older = Toggle("Older history", "Alt+H", _model.Older);
        var switches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        switches.Children.Add(_regex);
        switches.Children.Add(_all);
        switches.Children.Add(_older);
        SetDock(switches, Dock.Left);
        toggles.Children.Add(switches);
        _count = new TextBlock
        {
            FontSize = NativeTokens.Body,
            Foreground = colours.Brush(colours.Dim),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggles.Children.Add(_count);
        SetDock(toggles, Dock.Top);
        Children.Add(toggles);

        _state = ModalParts.Dim(string.Empty, colours, new Thickness(20, 0, 20, 6));
        SetDock(_state, Dock.Top);
        Children.Add(_state);

        _note = ModalParts.Dim(string.Empty, colours, new Thickness(20, 0, 20, 6));

        // While the pane cap trimmed what was searched, the note is the ⌥H offer, and a click on it is ⌥H.
        _note.PointerPressed += (_, e) =>
        {
            if (_model.OffersOlder)
            {
                e.Handled = true;
                _older.IsChecked = true;
            }
        };
        SetDock(_note, Dock.Top);
        Children.Add(_note);

        var footer = ModalParts.Footer("↑↓ pick · Enter go · Alt+E regex · Alt+A all windows · Alt+H older history · Esc cancel", colours);
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);

        var (frame, list, scroller) = ModalParts.List(colours);
        _list = list;
        _scroller = scroller;
        frame.MinHeight = 120;
        Children.Add(frame);

        _box.TextChanged += (_, _) =>
        {
            if (!_syncing)
            {
                _selected = 0;
                Search();
            }
        };
        _regex.IsCheckedChanged += (_, _) => Toggled();
        _all.IsCheckedChanged += (_, _) => Toggled();
        _older.IsCheckedChanged += (_, _) => Toggled();
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _session.Changed += OnChanged;

        // A model published while this view was being built (an older-history search that finished first) is read again
        // now that it listens, or the view would sit on the empty one it started from.
        _model = session.Model;
        SyncQuery();
        Draw();
    }

    /// <summary>Puts the model's query in an empty box: the client's own search (a snapshot, a reopening) typed it.</summary>
    private void SyncQuery()
    {
        if (string.IsNullOrEmpty(_box.Text) && _model.Query.Length > 0)
        {
            _syncing = true;
            _box.Text = _model.Query;
            _box.CaretIndex = _model.Query.Length;
            _syncing = false;
        }
    }

    public string Title => "Search the output";

    public double PreferredWidth => 760;

    public bool Centred => false;

    /// <summary>The model drawn now, for a test.</summary>
    public SearchModel Model => _model;

    /// <summary>The row ⏎ goes to.</summary>
    public int Selected => _selected;

    /// <summary>The search box, for a test.</summary>
    public TextBox Box => _box;

    /// <summary>The ⌥E and ⌥A switches, for a test.</summary>
    public ToggleButton RegexToggle => _regex;

    public ToggleButton AllWindowsToggle => _all;

    /// <summary>The ⌥H switch, for a test.</summary>
    public ToggleButton OlderToggle => _older;

    /// <summary>What older history says (its bound, or how far its search has got), for a test.</summary>
    public string Note => _note.Text ?? string.Empty;

    public void FocusFirst()
    {
        _box.Focus();
        _box.CaretIndex = _box.Text?.Length ?? 0;
    }

    public void Detach() => _session.Changed -= OnChanged;

    /// <summary>⏎ on row <paramref name="index"/>.</summary>
    public void Go(int index)
    {
        _session.Go(index);
        _done();
    }

    private ToggleButton Toggle(string label, string key, bool on)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock { Text = NativeKeys.Words(label), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = NativeKeys.Words(key), FontSize = NativeTokens.Small, Opacity = 0.7, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var toggle = new ToggleButton
        {
            Content = content,
            IsChecked = on,
            Padding = new Thickness(10, 4),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            Focusable = false,
        };
        global::Avalonia.Automation.AutomationProperties.SetName(toggle, label);
        return toggle;
    }

    private void Toggled()
    {
        if (!_syncing)
        {
            _selected = 0;
            Search();
            FocusFirst();
        }
    }

    private void Search() =>
        _session.Search(_box.Text ?? string.Empty, _regex.IsChecked == true, _all.IsChecked == true, _older.IsChecked == true);

    private void OnChanged(SearchModel model) => Dispatcher.UIThread.Post(() =>
    {
        _model = model;
        SyncQuery();
        Draw();
    });

    private void Draw()
    {
        _syncing = true;
        try
        {
            if (_regex.IsChecked != _model.Regex)
            {
                _regex.IsChecked = _model.Regex;
            }

            if (_all.IsChecked != _model.AllWindows)
            {
                _all.IsChecked = _model.AllWindows;
            }

            if (_older.IsChecked != _model.Older)
            {
                _older.IsChecked = _model.Older;
            }
        }
        finally
        {
            _syncing = false;
        }

        _state.Text = NativeKeys.Words(_model.StateText);
        _count.Text = _model.CountText;
        _note.Text = NativeKeys.Words(_model.HistoryNote ?? string.Empty);
        _note.IsVisible = _model.HistoryNote is not null;
        _note.Foreground = _colours.Brush(_model.HistoryRunning || _model.OffersOlder ? _colours.Accent : _colours.Dim);
        _note.Cursor = _model.OffersOlder ? new Cursor(StandardCursorType.Hand) : null;
        _list.Children.Clear();
        _rows.Clear();
        _selected = _model.Rows.Count == 0 ? 0 : Math.Clamp(_selected, 0, _model.Rows.Count - 1);
        if (_model.EmptyText is { } empty)
        {
            _list.Children.Add(new TextBlock
            {
                Text = empty,
                Foreground = _colours.Brush(_model.Error is null ? _colours.Dim : _colours.Warn),
                Margin = new Thickness(12, 10),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var labelled = _model.Labelled;
        for (var i = 0; i < _model.Rows.Count; i++)
        {
            var row = _model.Rows[i];
            var index = i;
            var line = new DockPanel();
            if (labelled)
            {
                // The cells' window column (SearchModel.LabelFor): the window in ⌥A scope, how far back an older row is
                // (⏎ opens it in the history view), and "here" on a row the pane holds.
                var label = _model.LabelFor(row);
                var window = new TextBlock
                {
                    Text = label,
                    Width = 130,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = _colours.Brush(_colours.Header),
                    FontSize = NativeTokens.Body,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                };
                SetDock(window, Dock.Left);
                line.Children.Add(window);
            }

            var text = new TextBlock
            {
                Foreground = _colours.Brush(_colours.Text),
                FontFamily = NativeTokens.MonoFont,
                FontSize = NativeTokens.Title,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            text.Inlines = ModalParts.Marked(row.Text, row.MatchStart, row.MatchLength, _colours);
            line.Children.Add(text);
            var border = ModalParts.Row(line, row.Text, () => Select(index), () => Go(index));
            _rows.Add(border);
            _list.Children.Add(border);
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
        if (ModalParts.Console(e) is not { } key)
        {
            return;
        }

        // Typing and ⌫ are the box's; every other key is the cell surface's rule.
        var bare = key.Modifiers == 0;
        if (bare && key.Key is not (ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape))
        {
            return;
        }

        var decision = SearchPrompt.Interpret(
            key, _box.Text ?? string.Empty, _selected, _model.Rows.Count, _regex.IsChecked == true, _all.IsChecked == true, _older.IsChecked == true);
        if (!bare && key.Modifiers.HasFlag(ConsoleModifiers.Control) && decision.Action == SearchAction.None)
        {
            // ⌃A, ⌃C, ⌃V and the rest stay the box's.
            return;
        }

        e.Handled = true;
        switch (decision.Action)
        {
            case SearchAction.Go:
                Go(_selected);
                break;
            case SearchAction.Cancel:
                _session.Dismiss();
                _done();
                break;
            case SearchAction.Redraw when decision.Regex != (_regex.IsChecked == true) || decision.AllWindows != (_all.IsChecked == true)
                                          || decision.Older != (_older.IsChecked == true):
                _syncing = true;
                _regex.IsChecked = decision.Regex;
                _all.IsChecked = decision.AllWindows;
                _older.IsChecked = decision.Older;
                _syncing = false;
                _selected = 0;
                Search();
                break;
            case SearchAction.Redraw:
                Select(decision.Selected);
                break;
        }
    }
}
