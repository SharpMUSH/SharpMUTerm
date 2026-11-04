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
/// The window's review of an import from another client: what can act as the player, what will be added, what was left
/// out and why (<see cref="ImportReviewModel"/>, the model the cells draw), a check box for the passwords the file held,
/// and Import / Cancel. Import ends in the client's <c>AcceptImport</c>, the method the cells' Enter ends in; the check
/// box ends in <c>ToggleImportPasswords</c>, and the client republishes. Nothing is written before Import.
/// </summary>
internal sealed class ImportReviewView : DockPanel, IModalView
{
    private readonly ImportReviewSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly StackPanel _body = new() { Spacing = NativeTokens.Space4, Margin = new Thickness(NativeTokens.Space20, NativeTokens.Space2, NativeTokens.Space20, NativeTokens.Space4) };
    private readonly Button _import;
    private readonly Button _cancel;
    private readonly CheckBox _passwords;
    private ImportReviewModel _model;

    public ImportReviewView(ImportReviewSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        _model = session.Model;

        var heading = ModalParts.Heading(_model.Title, colours);
        SetDock(heading, Dock.Top);
        Children.Add(heading);
        var intro = ModalParts.Dim(_model.Intro, colours);
        SetDock(intro, Dock.Top);
        Children.Add(intro);

        var footer = ModalParts.Footer(_model.Empty ? "Esc close" : _model.Keys, colours);
        SetDock(footer, Dock.Bottom);

        _import = ModalParts.Button("Import", "Enter", colours, Accept, accent: true);
        _import.IsEnabled = !_model.Empty;
        _cancel = ModalParts.Button("Cancel", "Esc", colours, Decline);
        _passwords = new CheckBox
        {
            Content = "Store the passwords",
            IsVisible = _model.Passwords > 0,
            IsChecked = _model.StorePasswords,
            Foreground = colours.Brush(colours.Text),
            FontSize = NativeTokens.Body,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = true,
        };
        _passwords.IsCheckedChanged += (_, _) =>
        {
            if ((_passwords.IsChecked == true) != _model.StorePasswords)
            {
                _session.TogglePasswords();
            }
        };

        var buttons = new DockPanel { Margin = new Thickness(NativeTokens.Space20, NativeTokens.Space12, NativeTokens.Space20, 0), LastChildFill = false };
        DockPanel.SetDock(_passwords, Dock.Left);
        buttons.Children.Add(_passwords);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space10 };
        right.Children.Add(_import);
        right.Children.Add(_cancel);
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        SetDock(buttons, Dock.Bottom);
        Children.Add(footer);
        Children.Add(buttons);

        Children.Add(new ScrollViewer
        {
            Content = _body,
            MaxHeight = 460,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        });

        Draw();
        _session.Changed += OnChanged;
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }

    public string Title => _model.Title;

    public double PreferredWidth => 680;

    public bool Centred => true;

    /// <summary>The buttons and the check box, for a test.</summary>
    public Button ImportButton => _import;

    public Button CancelButton => _cancel;

    public CheckBox PasswordsBox => _passwords;

    /// <summary>The text the body shows, for a test.</summary>
    public IReadOnlyList<string> BodyText => _body.Children.OfType<TextBlock>()
        .Select(t => t.Inlines is { Count: > 0 } inlines
            ? string.Concat(inlines.OfType<global::Avalonia.Controls.Documents.Run>().Select(r => r.Text))
            : t.Text ?? string.Empty)
        .ToArray();

    public void FocusFirst() => (_import.IsEnabled ? _import : _cancel).Focus();

    public void Detach() => _session.Changed -= OnChanged;

    /// <summary>Import: the session ends and the client writes the import.</summary>
    public void Accept()
    {
        if (_model.Empty)
        {
            return;
        }

        _session.Accept();
        _done();
    }

    /// <summary>Cancel: nothing is written.</summary>
    public void Decline()
    {
        _session.Dismiss();
        _done();
    }

    private void OnChanged(ImportReviewModel model) => Dispatcher.UIThread.Post(() =>
    {
        _model = model;
        _passwords.IsChecked = model.StorePasswords;
        Draw();
    });

    private void Draw()
    {
        _body.Children.Clear();
        if (_model.Empty)
        {
            _body.Children.Add(Line("There is nothing in it this client can import.", _colours.Dim));
        }
        else
        {
            _body.Children.Add(ModalParts.GroupHeader(ImportReviewModel.ActsHeading, _colours, first: true));
            if (_model.ActsAsYou.Count == 0)
            {
                _body.Children.Add(Line("Nothing: no rule in it sends a command.", _colours.Dim));
            }

            foreach (var act in _model.ActsAsYou)
            {
                _body.Children.Add(Line("•  " + act, _colours.Text));
            }

            _body.Children.Add(ModalParts.GroupHeader(ImportReviewModel.AddsHeading, _colours, first: false));
            foreach (var row in _model.Adds)
            {
                _body.Children.Add(Row(row, _colours.Text));
            }
        }

        if (_model.LeftOutCount > 0)
        {
            _body.Children.Add(ModalParts.GroupHeader($"{ImportReviewModel.LeftOutHeading} ({_model.LeftOutCount})", _colours, first: false));
            foreach (var row in _model.LeftOut)
            {
                _body.Children.Add(Row(row, _colours.Dim));
            }

            if (_model.MoreLeftOut is { } more)
            {
                _body.Children.Add(Line(more, _colours.Dim));
            }
        }

        if (_model.PasswordLine is { } passwords)
        {
            var line = Line(passwords, _model.StorePasswords ? _colours.Text : _colours.Warn);
            line.Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space12, NativeTokens.Space12, 0);
            _body.Children.Add(line);
        }
    }

    private TextBlock Line(string text, Color colour) => new()
    {
        Text = NativeKeys.Words(text),
        Foreground = _colours.Brush(colour),
        FontSize = NativeTokens.Body,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(NativeTokens.Space12, NativeTokens.Hairline, NativeTokens.Space12, NativeTokens.Hairline),
    };

    private TextBlock Row(ImportReviewRow row, Color detail)
    {
        var block = new TextBlock
        {
            FontSize = NativeTokens.Body,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space2, NativeTokens.Space12, NativeTokens.Space2),
        };
        block.Inlines ??= new global::Avalonia.Controls.Documents.InlineCollection();
        block.Inlines.Add(new global::Avalonia.Controls.Documents.Run(row.Kind + "  ") { Foreground = _colours.Brush(_colours.Accent) });
        block.Inlines.Add(new global::Avalonia.Controls.Documents.Run(row.Name) { Foreground = _colours.Brush(_colours.Text), FontWeight = FontWeight.SemiBold });
        block.Inlines.Add(new global::Avalonia.Controls.Documents.Run("  " + NativeKeys.Words(row.Detail)) { Foreground = _colours.Brush(detail) });
        return block;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Decline();
        }
        else if (e.Key == Key.Enter && e.Source is not Button and not CheckBox)
        {
            e.Handled = true;
            Accept();
        }
        else if (e.Key == Key.P && _model.Passwords > 0)
        {
            e.Handled = true;
            _session.TogglePasswords();
        }
    }
}
