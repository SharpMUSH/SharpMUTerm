using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Input;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's ⌃R: whose list it is, a filter box, and the command line's history newest first with the match
/// marked. The list is the client's snapshot (<see cref="HistoryModel"/>) filtered by the same function the cells use
/// (<see cref="HistoryModel.View"/>), so a query lists the same rows; ⏎ places the entry on the command line and
/// sends nothing. The keys that are not typing are read by the cells' rules (<see cref="HistorySearchPrompt.Interpret"/>).
/// </summary>
internal sealed class HistoryView : DockPanel, IModalView
{
    private readonly HistorySession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly TextBox _box;
    private readonly TextBlock _count;
    private readonly StackPanel _list;
    private readonly List<Border> _rows = [];
    private IReadOnlyList<HistoryMatch> _matches = [];
    private int _selected;

    public HistoryView(HistorySession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        var model = session.Model;

        _box = ModalParts.SearchBox("Filter the history…", "Filter the history", colours);
        SetDock(_box, Dock.Top);
        Children.Add(_box);

        var header = new DockPanel { Margin = new Thickness(NativeTokens.Space20, 0, NativeTokens.Space20, NativeTokens.Space6) };
        var whose = new TextBlock { Text = model.BarLabel, FontSize = NativeTokens.Body, Foreground = colours.Brush(colours.Dim) };
        _count = new TextBlock { FontSize = NativeTokens.Body, Foreground = colours.Brush(colours.Dim), HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right };
        SetDock(whose, Dock.Left);
        header.Children.Add(whose);
        header.Children.Add(_count);
        SetDock(header, Dock.Top);
        Children.Add(header);

        var footer = ModalParts.Footer("↑↓ pick · Enter places it on the command line · Esc cancel", colours);
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);

        var (frame, list, _) = ModalParts.List(colours);
        _list = list;
        frame.MinHeight = 120;
        Children.Add(frame);

        _box.Text = session.InitialQuery;
        _box.CaretIndex = session.InitialQuery.Length;
        _box.TextChanged += (_, _) =>
        {
            _selected = 0;
            Draw();
        };
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Draw();
    }

    public string Title => "Command history";

    public double PreferredWidth => 680;

    public bool Centred => false;

    /// <summary>The rows listed now, for a test.</summary>
    public IReadOnlyList<HistoryMatch> Matches => _matches;

    /// <summary>The row ⏎ places.</summary>
    public int Selected => _selected;

    /// <summary>The filter box, for a test.</summary>
    public TextBox Box => _box;

    public void FocusFirst() => _box.Focus();

    public void Detach()
    {
    }

    /// <summary>⏎ on listed row <paramref name="row"/>.</summary>
    public void Insert(int row)
    {
        if (row >= 0 && row < _matches.Count)
        {
            _session.Insert(_matches[row].Index);
            _done();
        }
    }

    private void Draw()
    {
        var model = _session.Model;
        _matches = model.View(_box.Text ?? string.Empty);
        _count.Text = model.CountText(_matches.Count);
        _list.Children.Clear();
        _rows.Clear();
        _selected = _matches.Count == 0 ? 0 : Math.Clamp(_selected, 0, _matches.Count - 1);
        if (_matches.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = model.Entries.Count == 0 ? HistoryModel.NothingEntered : HistoryModel.NoMatches,
                Foreground = _colours.Brush(_colours.Dim),
                Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space10),
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var mono = NativeTokens.MonoFont;
        for (var i = 0; i < _matches.Count; i++)
        {
            var match = _matches[i];
            var index = i;
            var text = new TextBlock
            {
                Foreground = _colours.Brush(_colours.Text),
                FontFamily = mono,
                FontSize = NativeTokens.Title,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            text.Inlines = ModalParts.Marked(match.Text, match.MatchStart, match.MatchLength, _colours);
            var row = ModalParts.Row(text, match.Text, () => Select(index), () => Insert(index));
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
        if (ModalParts.Console(e) is not { } key)
        {
            return;
        }

        var bare = key.Modifiers == 0;
        if (bare && key.Key is not (ConsoleKey.UpArrow or ConsoleKey.DownArrow or ConsoleKey.Enter or ConsoleKey.Escape))
        {
            return;
        }

        var decision = HistorySearchPrompt.Interpret(key, _box.Text ?? string.Empty, _selected, _matches.Count);
        if (!bare && decision.Action == HistoryAction.None)
        {
            return;
        }

        e.Handled = true;
        switch (decision.Action)
        {
            case HistoryAction.Insert:
                Insert(_selected);
                break;
            case HistoryAction.Cancel:
                _session.Dismiss();
                _done();
                break;
            case HistoryAction.Redraw:
                Select(decision.Selected);
                break;
        }
    }
}
