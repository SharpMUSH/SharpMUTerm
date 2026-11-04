using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's read-only history view (⏎ on an older-history hit in ⌃F): the page of a session's scrollback the client
/// read, the found line on the selection plane, the query marked in every line. The page is the client's
/// <see cref="ScrollbackViewModel"/>, the record the cells draw; a key or the wheel asks the client for another page
/// (<see cref="ScrollbackViewSession.Key"/>), so the two hosts always show the same lines.
/// </summary>
internal sealed class ScrollbackView : DockPanel, IModalView
{
    private readonly ScrollbackViewSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly TextBlock _title;
    private readonly TextBlock _position;
    private readonly StackPanel _list;
    private ScrollbackViewModel _model;

    public ScrollbackView(ScrollbackViewSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        _model = session.Model;
        Focusable = true;

        var header = new DockPanel { Margin = new Thickness(NativeTokens.Space20, NativeTokens.Space14, NativeTokens.Space20, NativeTokens.Space8) };
        _title = ModalParts.Heading(string.Empty, colours);
        _position = new TextBlock
        {
            FontSize = NativeTokens.Body,
            Foreground = colours.Brush(colours.Dim),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        SetDock(_title, Dock.Left);
        header.Children.Add(_title);
        header.Children.Add(_position);
        SetDock(header, Dock.Top);
        Children.Add(header);

        var footer = ModalParts.Footer(NativeKeys.Words(ScrollbackViewModel.Keys), colours);
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);

        _list = new StackPanel { Margin = new Thickness(NativeTokens.Space8, NativeTokens.Space4, NativeTokens.Space8, NativeTokens.Space4) };
        var frame = new Border
        {
            BorderBrush = colours.Brush(colours.Rule),
            BorderThickness = new Thickness(0, NativeTokens.Hairline, 0, NativeTokens.Hairline),
            Child = _list,
        };
        Children.Add(frame);

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        PointerWheelChanged += (_, e) =>
        {
            e.Handled = true;
            _session.Key(e.Delta.Y > 0 ? ConsoleKey.UpArrow : ConsoleKey.DownArrow);
        };
        _session.Changed += OnChanged;
        _model = session.Model; // a page published while the view was being built
        Draw();
    }

    public string Title => "Older history";

    public double PreferredWidth => 900;

    public bool Centred => false;

    /// <summary>The page drawn now, for a test.</summary>
    public ScrollbackViewModel Model => _model;

    public void FocusFirst() => Focus();

    public void Detach() => _session.Changed -= OnChanged;

    private void OnChanged(ScrollbackViewModel model) => Dispatcher.UIThread.Post(() =>
    {
        _model = model;
        Draw();
    });

    private void Draw()
    {
        _title.Text = _model.Title;
        _position.Text = _model.Position;
        _list.Children.Clear();
        var mono = NativeTokens.MonoFont;
        foreach (var line in _model.Lines)
        {
            // The hit on the selection plane, its context in the body ink behind a rule, the rest of the page in the dim ink:
            // the model's roles, which the cells draw too.
            var role = _model.Role(line);
            var marked = role == ScrollbackLineRole.Hit;
            var text = new TextBlock
            {
                FontFamily = mono,
                FontSize = NativeTokens.Body,
                Foreground = _colours.Brush(role == ScrollbackLineRole.Page ? _colours.Dim : _colours.Text),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            text.Inlines = ModalParts.Marked(line.Text, line.MatchStart, line.MatchLength, _colours);
            _list.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(NativeTokens.RadiusM),
                Padding = new Thickness(NativeTokens.Space12, NativeTokens.Space2),
                Background = marked ? _colours.Brush(_colours.Selected) : Brushes.Transparent,
                BorderBrush = role == ScrollbackLineRole.Context ? _colours.Brush(_colours.Rule) : null,
                BorderThickness = new Thickness(role == ScrollbackLineRole.Context ? NativeTokens.BorderStrong : 0, 0, 0, 0),
                Child = text,
            });
        }

        for (var i = _model.Lines.Count; i < _model.PageRows; i++)
        {
            _list.Children.Add(new TextBlock { Text = " ", FontFamily = mono, FontSize = NativeTokens.Body, Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space2) });
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (ModalParts.FocusedButtonTakes(e))
        {
            return;
        }

        if (ModalParts.Console(e) is not { } key)
        {
            return;
        }

        if (key.Key == ConsoleKey.Escape)
        {
            e.Handled = true;
            _session.Dismiss();
            _done();
            return;
        }

        if (key.Modifiers == 0 && _session.Key(key.Key))
        {
            e.Handled = true;
        }
    }
}
