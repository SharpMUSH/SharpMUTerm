using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.Diagnostics;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The client's own messages in the window: what is kept, then every message oldest first (time, severity, text), the
/// list scrolled to the newest. More and Less are the cells' <c>+</c> and <c>-</c>, through the same
/// <c>StepMessageLevel</c>; the client republishes the model and the list is redrawn from it.
/// </summary>
internal sealed class MessageLogView : DockPanel, IModalView
{
    private readonly MessageLogSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly TextBlock _capturing;
    private readonly StackPanel _list;
    private readonly ScrollViewer _scroller;
    private readonly Button _more;

    public MessageLogView(MessageLogSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;

        var heading = ModalParts.Heading(MessageLogModel.Title, colours);
        heading.Margin = new Thickness(NativeTokens.Space24, NativeTokens.Space16, NativeTokens.Space24, NativeTokens.Space2);
        SetDock(heading, Dock.Top);
        Children.Add(heading);

        _capturing = ModalParts.Dim(string.Empty, colours, new Thickness(NativeTokens.Space24, 0, NativeTokens.Space24, NativeTokens.Space8));
        SetDock(_capturing, Dock.Top);
        Children.Add(_capturing);

        var footer = ModalParts.Footer(NativeKeys.Words(MessageLogModel.Keys), colours);
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);

        _more = ModalParts.Button("Keep more", "+", colours, () => _session.StepLevel(-1));
        var less = ModalParts.Button("Keep less", "-", colours, () => _session.StepLevel(1));
        var close = ModalParts.Button("Close", "Esc", colours, Close);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = NativeTokens.Space10,
            Margin = new Thickness(NativeTokens.Space24, NativeTokens.Space12, NativeTokens.Space24, 0),
        };
        buttons.Children.Add(_more);
        buttons.Children.Add(less);
        buttons.Children.Add(close);
        SetDock(buttons, Dock.Bottom);
        Children.Add(buttons);

        var (frame, list, scroller) = ModalParts.List(colours);
        _list = list;
        _scroller = scroller;
        scroller.MaxHeight = 420;
        Children.Add(frame);

        Show(session.Model);
        _session.Changed += OnChanged;
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnText, RoutingStrategies.Tunnel);
    }

    public string Title => MessageLogModel.Title;

    public double PreferredWidth => 760;

    public bool Centred => true;

    /// <summary>The rows drawn, for a test.</summary>
    public IReadOnlyList<Control> Rows => [.. _list.Children];

    public void FocusFirst() => _more.Focus();

    public void Detach() => _session.Changed -= OnChanged;

    private void Close()
    {
        _session.Dismiss();
        _done();
    }

    private void OnChanged(MessageLogModel model) => Dispatcher.UIThread.Post(() => Show(model));

    private void Show(MessageLogModel model)
    {
        _capturing.Text = model.Capturing;
        _list.Children.Clear();
        if (model.Entries.Count == 0)
        {
            _list.Children.Add(ModalParts.Dim(MessageLogModel.Empty, _colours, new Thickness(NativeTokens.Space8)));
            return;
        }

        foreach (var message in model.Entries)
        {
            _list.Children.Add(RowFor(message));
        }

        Dispatcher.UIThread.Post(() => _scroller.ScrollToEnd(), DispatcherPriority.Background);
    }

    private Control RowFor(ClientMessage message)
    {
        var row = new DockPanel { Margin = new Thickness(NativeTokens.Space4, NativeTokens.Space2) };
        var stamp = new TextBlock
        {
            Text = MessageLogModel.Stamp(message),
            FontFamily = NativeTokens.MonoFont,
            FontSize = NativeTokens.Small,
            Foreground = _colours.Brush(_colours.Dim),
            Width = 64,
            VerticalAlignment = VerticalAlignment.Top,
        };
        SetDock(stamp, Dock.Left);
        row.Children.Add(stamp);
        var label = new TextBlock
        {
            Text = MessageLogModel.Label(message.Severity),
            FontSize = NativeTokens.Small,
            FontWeight = FontWeight.SemiBold,
            Foreground = _colours.Brush(message.Severity switch
            {
                MessageSeverity.Error => _colours.Warn,
                MessageSeverity.Warning => _colours.Notice,
                _ => _colours.Dim,
            }),
            Width = 44,
            VerticalAlignment = VerticalAlignment.Top,
        };
        SetDock(label, Dock.Left);
        row.Children.Add(label);
        row.Children.Add(new TextBlock
        {
            Text = NativeKeys.Words(message.Text),
            FontSize = NativeTokens.Body,
            Foreground = _colours.Brush(_colours.Text),
            TextWrapping = TextWrapping.Wrap,
        });
        AutomationProperties.SetName(row, $"{MessageLogModel.Label(message.Severity)}: {message.Text}");
        return row;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (ModalParts.FocusedButtonTakes(e))
        {
            return;
        }

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Close();
        }
    }

    private void OnText(object? sender, TextInputEventArgs e)
    {
        switch (e.Text)
        {
            case "+":
                e.Handled = true;
                _session.StepLevel(-1);
                break;
            case "-":
                e.Handled = true;
                _session.StepLevel(1);
                break;
        }
    }
}
