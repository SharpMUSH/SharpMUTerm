using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.WorldIndex;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's Browse worlds: a search box, mu*index's rows for what is typed in the order chosen, the credit the
/// directory's licence asks for, and the keys. The rows are <see cref="WorldBrowseModel.View"/>, the function the cell
/// surface lists, so both hosts list the same rows. Enter on a row hands the pick to the client, which asks where the
/// world is and opens the connect form in this modal's place; Enter on a search the rows cannot answer asks the
/// directory. A world that answers at several addresses lists them in the same card (<see cref="WorldBrowseModel.Choosing"/>):
/// Enter hands the chosen one to the client, Esc goes back to the list. Every word is the window's (<see cref="NativeKeys"/>).
/// </summary>
internal sealed class WorldBrowseView : DockPanel, IModalView
{
    private readonly WorldBrowseSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly TextBox _box;
    private readonly TextBlock _count;
    private readonly Button _sort;
    private readonly TextBlock _note;
    private readonly TextBlock _footer;
    private readonly DockPanel _bar;
    private readonly StackPanel _list;
    private readonly List<Border> _rows = [];
    private WorldBrowseModel _model;
    private IReadOnlyList<WorldListing> _view = [];
    private WorldDirectorySort _order = WorldDirectorySort.Players;
    private int _selected;
    private string _query;

    public WorldBrowseView(WorldBrowseSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        _model = session.Model;

        var heading = ModalParts.Heading(WorldBrowseModel.Heading, colours);
        SetDock(heading, Dock.Top);
        Children.Add(heading);

        var credit = ModalParts.Dim(WorldBrowseModel.AttributionLine, colours);
        SetDock(credit, Dock.Top);
        Children.Add(credit);

        _box = ModalParts.SearchBox(WorldBrowseModel.SearchPlaceholder, "Search worlds");
        SetDock(_box, Dock.Top);
        Children.Add(_box);

        var bar = _bar = new DockPanel { Margin = new Thickness(20, 0, 14, 4) };
        _sort = ModalParts.Button(SortLabel(), "Tab", colours, ToggleSort);
        SetDock(_sort, Dock.Right);
        bar.Children.Add(_sort);
        _count = ModalParts.Dim(string.Empty, colours, new Thickness(0));
        _count.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(_count);
        SetDock(bar, Dock.Top);
        Children.Add(bar);

        var footer = _footer = ModalParts.Footer(KeysText(WorldBrowseModel.Keys), colours);
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);

        _note = ModalParts.Dim(string.Empty, colours, new Thickness(20, 6, 20, 0));
        SetDock(_note, Dock.Bottom);
        Children.Add(_note);

        var (frame, list, _) = ModalParts.List(colours);
        _list = list;
        frame.MinHeight = 220;
        Children.Add(frame);

        _box.Text = session.InitialQuery;
        _box.CaretIndex = session.InitialQuery.Length;
        _query = session.InitialQuery;
        _box.TextChanged += (_, _) =>
        {
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
        _session.Changed += OnChanged;
        ShowList();
    }

    public string Title => WorldBrowseModel.Heading;

    public double PreferredWidth => 820;

    public bool Centred => false;

    /// <summary>The rows listed now, for a test.</summary>
    public IReadOnlyList<WorldListing> Rows => _view;

    /// <summary>The row Enter acts on.</summary>
    public int Selected => _selected;

    /// <summary>The order the rows are in, for a test.</summary>
    public WorldDirectorySort Order => _order;

    /// <summary>Whether a picked world's addresses are listed in place of the worlds, for a test.</summary>
    public bool ChoosingAddress => _model.Choosing is not null;

    /// <summary>The search box, for a test.</summary>
    public TextBox Box => _box;

    /// <summary>What the list says under it now, for a test.</summary>
    public string Note => _note.Text ?? string.Empty;

    public void FocusFirst() => _box.Focus();

    public void Detach() => _session.Changed -= OnChanged;

    /// <summary>Enter on listed row <paramref name="row"/>, or on a search the rows cannot answer — or, while a world's addresses are listed, on address <paramref name="row"/>.</summary>
    public void Choose(int row)
    {
        if (_model.Choosing is not null)
        {
            _session.Choose(row);
            return;
        }

        if (_model.AsksDirectory(_query))
        {
            _session.Ask(_query);
            return;
        }

        if (row < 0 || row >= _view.Count || _model.Opening is not null)
        {
            return;
        }

        for (var i = 0; i < _model.Rows.Count; i++)
        {
            if (ReferenceEquals(_model.Rows[i], _view[row]))
            {
                _session.Pick(i);
                return;
            }
        }
    }

    /// <summary>Tab, and the order button: by players or by name.</summary>
    public void ToggleSort()
    {
        _order = WorldBrowseModel.Next(_order);
        _sort.Content = SortLabel();
        _selected = 0;
        ShowList();
    }

    private string SortLabel()
    {
        var name = WorldBrowseModel.SortName(_order);
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private void OnChanged(WorldBrowseModel model) => Dispatcher.UIThread.Post(() =>
    {
        var rowsChanged = !ReferenceEquals(model.Rows, _model.Rows) || !ReferenceEquals(model.Choosing, _model.Choosing);
        var leftChoice = _model.Choosing is not null && model.Choosing is null;
        _model = model;
        if (rowsChanged)
        {
            _selected = Math.Max(0, model.Choosing?.FirstConnectable ?? 0);
        }

        ShowList();
        if (leftChoice)
        {
            _box.Focus();
        }
    });

    private static string KeysText(IReadOnlyList<(string Key, string Label)> keys) =>
        string.Join(" · ", keys.Select(k => $"{k.Key} {k.Label}"));

    /// <summary>A picked world's addresses, one row each: <c>host:port</c>, TLS or plain, and its state.</summary>
    private void ShowChoice(WorldGame game)
    {
        _box.IsVisible = false;
        _bar.IsVisible = false;
        _footer.Text = NativeKeys.Words(KeysText(WorldBrowseModel.ChoiceKeys));
        _note.Text = NativeKeys.Words(_model.Problem ?? WorldBrowseModel.ChoiceNote);
        _note.Foreground = _colours.Brush(_model.Problem is null ? _colours.Dim : _colours.Warn);
        _list.Children.Clear();
        _rows.Clear();
        _list.Children.Add(new TextBlock
        {
            Text = _model.ChoiceLine,
            Foreground = _colours.Brush(_colours.Text),
            FontSize = NativeTokens.Title,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 8, 12, 2),
            TextWrapping = TextWrapping.Wrap,
        });
        _list.Children.Add(new TextBlock
        {
            Text = _model.CharsetLine,
            Foreground = _colours.Brush(_colours.Dim),
            FontSize = NativeTokens.Body,
            Margin = new Thickness(12, 0, 12, 8),
            TextWrapping = TextWrapping.Wrap,
        });
        _selected = Math.Clamp(_selected, 0, game.Endpoints.Count - 1);
        for (var i = 0; i < game.Endpoints.Count; i++)
        {
            var endpoint = game.Endpoints[i];
            var (address, security, state) = WorldBrowseModel.Endpoint(endpoint);
            var index = i;
            var line = new DockPanel();
            var host = new TextBlock
            {
                Text = address,
                Foreground = _colours.Brush(_colours.Text),
                FontSize = NativeTokens.Title,
                Width = 300,
                Margin = new Thickness(0, 0, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetDock(host, Dock.Left);
            line.Children.Add(host);
            var kind = new TextBlock
            {
                Text = security,
                Foreground = _colours.Brush(endpoint.Tls ? _colours.Accent : _colours.Dim),
                FontSize = NativeTokens.Body,
                FontWeight = endpoint.Tls ? FontWeight.SemiBold : FontWeight.Normal,
                Width = 64,
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetDock(kind, Dock.Left);
            line.Children.Add(kind);
            line.Children.Add(new TextBlock
            {
                Text = state,
                Foreground = _colours.Brush(endpoint.Stale || !endpoint.Connectable ? _colours.Warn : _colours.Dim),
                FontSize = NativeTokens.Body,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var row = ModalParts.Row(line, address, () => Select(index), () => Choose(index));
            ToolTip.SetTip(row, WorldBrowseModel.EndpointText(endpoint));
            _rows.Add(row);
            _list.Children.Add(row);
        }

        Highlight();
    }

    private void ShowList()
    {
        if (_model.Choosing is { } game)
        {
            ShowChoice(game);
            return;
        }

        _box.IsVisible = true;
        _bar.IsVisible = true;
        _footer.Text = NativeKeys.Words(KeysText(WorldBrowseModel.Keys));
        _view = _model.View(_query, _order);
        _count.Text = _model.CountLine(_query);
        _note.Text = NativeKeys.Words(
            _model.Opening is not null ? _model.OpeningLine
            : _model.State == WorldBrowseState.Ready && _model.Problem is { } problem ? problem
            : _model.State == WorldBrowseState.Ready && _view.Count > 0
                ? _model.MarksNote + WorldBrowseModel.PickNote
                : string.Empty);
        _note.Foreground = _colours.Brush(_model.State == WorldBrowseState.Ready && _model.Problem is not null && _model.Opening is null
            ? _colours.Warn
            : _colours.Dim);
        _list.Children.Clear();
        _rows.Clear();
        _selected = _view.Count == 0 ? 0 : Math.Clamp(_selected, 0, _view.Count - 1);
        if (_model.Empty(_query) is { } empty)
        {
            _list.Children.Add(new TextBlock
            {
                Text = NativeKeys.Words(empty),
                Foreground = _colours.Brush(_model.State is WorldBrowseState.Failed or WorldBrowseState.Unavailable ? _colours.Warn : _colours.Dim),
                FontSize = NativeTokens.Body,
                Margin = new Thickness(12, 10),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        for (var i = 0; i < _view.Count; i++)
        {
            var world = _view[i];
            var index = i;
            var line = new DockPanel();
            var players = new TextBlock
            {
                Text = WorldBrowseModel.Players(world),
                Foreground = _colours.Brush(_colours.Accent),
                FontSize = NativeTokens.Title,
                FontWeight = FontWeight.SemiBold,
                Width = 48,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetDock(players, Dock.Left);
            line.Children.Add(players);

            var title = new TextBlock
            {
                Text = world.Name,
                Foreground = _colours.Brush(_colours.Text),
                FontSize = NativeTokens.Title,
                Width = 220,
                Margin = new Thickness(0, 0, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetDock(title, Dock.Left);
            line.Children.Add(title);

            var codebase = new TextBlock
            {
                Text = world.Codebase,
                Foreground = _colours.Brush(_colours.Dim),
                FontSize = NativeTokens.Small,
                Width = 96,
                Margin = new Thickness(0, 0, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetDock(codebase, Dock.Left);
            line.Children.Add(codebase);

            line.Children.Add(new TextBlock
            {
                Text = world.Tagline,
                Foreground = _colours.Brush(_colours.Dim),
                FontSize = NativeTokens.Body,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var row = ModalParts.Row(line, world.Name, () => Select(index), () => Choose(index));
            ToolTip.SetTip(row, world.Tagline.Length == 0 ? world.Name : $"{world.Name} — {world.Tagline}");
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
            _rows[i].Background = i == _selected ? _colours.Brush(_colours.Selected) : Brushes.Transparent;
        }

        if (_selected < _rows.Count)
        {
            _rows[_selected].BringIntoView();
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (_model.Choosing is not null)
                {
                    _session.Back();
                    return;
                }

                _session.Dismiss();
                _done();
                return;
            case Key.Up:
                e.Handled = true;
                Select(ModalParts.Step(_selected, -1, _rows.Count));
                return;
            case Key.Down:
                e.Handled = true;
                Select(ModalParts.Step(_selected, 1, _rows.Count));
                return;
            case Key.Tab:
                e.Handled = true;
                if (_model.Choosing is null)
                {
                    ToggleSort();
                }

                return;
            case Key.Enter:
                e.Handled = true;
                Choose(_selected);
                return;
        }
    }
}
