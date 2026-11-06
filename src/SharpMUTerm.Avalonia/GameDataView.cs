using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The game data inspector in the window: every GMCP package and MSDP variable the connection held when it opened, one
/// row each (name, age, payload on one line), and the selected one's payload in full under the list. Choosing a row is
/// the view's own; nothing goes back to the client, as nothing does from the cell inspector.
/// </summary>
internal sealed class GameDataView : DockPanel, IModalView
{
    private readonly GameDataSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly List<Border> _rows = [];
    private readonly TextBlock _payload;
    private int _selected = -1;

    public GameDataView(GameDataSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        var model = session.Model;

        var title = model.Owner.Length == 0 ? GameDataModel.Title : $"{GameDataModel.Title} · {model.Owner}";
        var heading = ModalParts.Heading(title, colours);
        heading.Margin = new Thickness(NativeTokens.Space24, NativeTokens.Space16, NativeTokens.Space24, NativeTokens.Space2);
        SetDock(heading, Dock.Top);
        Children.Add(heading);

        var count = ModalParts.Dim(model.Count, colours, new Thickness(NativeTokens.Space24, 0, NativeTokens.Space24, NativeTokens.Space8));
        SetDock(count, Dock.Top);
        Children.Add(count);

        var footer = ModalParts.Footer(NativeKeys.Words(GameDataModel.Keys), colours);
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);

        _payload = new TextBlock
        {
            FontFamily = NativeTokens.MonoFont,
            FontSize = NativeTokens.Code,
            Foreground = colours.Brush(colours.Text),
            TextWrapping = TextWrapping.Wrap,
        };
        var payload = new Border
        {
            Background = colours.Brush(colours.Well),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            Padding = new Thickness(NativeTokens.Space12, NativeTokens.Space8),
            Margin = new Thickness(NativeTokens.Space24, NativeTokens.Space12, NativeTokens.Space24, NativeTokens.Space12),
            Child = new ScrollViewer
            {
                Content = _payload,
                MaxHeight = 220,
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            },
            IsVisible = model.Rows.Count > 0,
        };
        SetDock(payload, Dock.Bottom);
        Children.Add(payload);

        var (frame, list, scroller) = ModalParts.List(colours);
        scroller.MaxHeight = 300;
        if (model.Rows.Count == 0)
        {
            list.Children.Add(ModalParts.Dim(GameDataModel.Empty, colours, new Thickness(NativeTokens.Space8)));
        }

        for (var i = 0; i < model.Rows.Count; i++)
        {
            var index = i;
            var row = model.Rows[i];
            var line = new DockPanel();
            var age = new TextBlock
            {
                Text = row.Age,
                FontSize = NativeTokens.Small,
                Foreground = colours.Brush(colours.Dim),
                Margin = new Thickness(NativeTokens.Space8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetDock(age, Dock.Right);
            line.Children.Add(age);
            var name = new TextBlock
            {
                Text = row.Package,
                FontWeight = FontWeight.SemiBold,
                FontSize = NativeTokens.Body,
                Foreground = colours.Brush(colours.Text),
                Width = 200,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            SetDock(name, Dock.Left);
            line.Children.Add(name);
            line.Children.Add(new TextBlock
            {
                Text = row.Preview,
                FontFamily = NativeTokens.MonoFont,
                FontSize = NativeTokens.Code,
                Foreground = colours.Brush(colours.Dim),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var border = ModalParts.Row(line, row.Package, () => Select(index), () => { });
            _rows.Add(border);
            list.Children.Add(border);
        }

        Children.Add(frame);
        Focusable = true;
        AutomationProperties.SetName(this, title);
        Select(model.Rows.Count > 0 ? 0 : -1);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    public string Title => GameDataModel.Title;

    public double PreferredWidth => 760;

    public bool Centred => true;

    /// <summary>The row selected, and the payload shown, for a test.</summary>
    public int Selected => _selected;

    public string Payload => _payload.Text ?? string.Empty;

    public void FocusFirst() => Focus();

    public void Detach()
    {
    }

    /// <summary>Selects row <paramref name="index"/> and shows its payload.</summary>
    public void Select(int index)
    {
        _selected = index;
        for (var i = 0; i < _rows.Count; i++)
        {
            ModalParts.Mark(_rows[i], i == index, _colours);
        }

        _payload.Text = index >= 0 && index < _session.Model.Rows.Count ? _session.Model.Rows[index].Payload : string.Empty;
        if (index >= 0 && index < _rows.Count)
        {
            _rows[index].BringIntoView();
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        var count = _rows.Count;
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                _session.Dismiss();
                _done();
                break;
            case Key.Up when count > 0:
                e.Handled = true;
                Select(Math.Max(0, _selected - 1));
                break;
            case Key.Down when count > 0:
                e.Handled = true;
                Select(Math.Min(count - 1, _selected + 1));
                break;
            case Key.PageUp when count > 0:
                e.Handled = true;
                Select(Math.Max(0, _selected - 10));
                break;
            case Key.PageDown when count > 0:
                e.Handled = true;
                Select(Math.Min(count - 1, _selected + 10));
                break;
            case Key.Home when count > 0:
                e.Handled = true;
                Select(0);
                break;
            case Key.End when count > 0:
                e.Handled = true;
                Select(count - 1);
                break;
        }
    }
}
