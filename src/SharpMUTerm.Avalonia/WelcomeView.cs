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
/// The window's first-run welcome: the menu (connect to a host, browse games, open settings, the <c>/connect</c>
/// example and the starting layout) and the connect form (host, port, TLS, a character, a password reminder). A choice,
/// a layout and the filled form go back through the session; the client checks the form through <c>/connect</c>'s own
/// path, so a refusal comes back as the model's problem and the form stays up. ↑↓ and ⏎ work the menu, ←→ the
/// layout; on the form ⇥ moves, ⏎ connects and Esc goes back to the menu.
/// </summary>
internal sealed class WelcomeView : DockPanel, IModalView
{
    private static readonly WelcomeChoice[] Choices = [WelcomeChoice.Connect, WelcomeChoice.Browse, WelcomeChoice.Settings, WelcomeChoice.Import];
    private static readonly WelcomeLayout[] Layouts = [WelcomeLayout.Suggested, WelcomeLayout.Roleplay, WelcomeLayout.Combat];

    private readonly WelcomeSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly StackPanel _body = new();
    private readonly TextBlock _footer;
    private readonly TextBlock _heading;
    private readonly List<Border> _choiceRows = [];
    private readonly List<Button> _chips = [];
    private WelcomeModel _model;
    private int _selected;
    private TextBox? _host;
    private TextBox? _port;
    private CheckBox? _tls;
    private TextBox? _character;
    private CheckBox? _passwordLater;
    private bool _drawnInForm;

    public WelcomeView(WelcomeSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        _model = session.Model;
        _selected = Array.IndexOf(Choices, _model.Selected);

        var heading = _heading = ModalParts.Heading(WelcomeModel.Heading, colours);
        heading.Margin = new Thickness(22, 18, 22, 2);
        SetDock(heading, Dock.Top);
        Children.Add(heading);

        _footer = ModalParts.Footer(string.Empty, colours);
        SetDock(_footer, Dock.Bottom);
        Children.Add(_footer);

        Children.Add(_body);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _session.Changed += OnChanged;
        Draw();
    }

    public string Title => "Welcome";

    public double PreferredWidth => 560;

    public bool Centred => true;

    /// <summary>The model drawn now, for a test.</summary>
    public WelcomeModel Model => _model;

    /// <summary>The menu row ⏎ chooses.</summary>
    public WelcomeChoice Selected => Choices[Math.Clamp(_selected, 0, Choices.Length - 1)];

    /// <summary>The form's fields, for a test; null while the menu is shown.</summary>
    public TextBox? HostBox => _host;

    public TextBox? PortBox => _port;

    public CheckBox? TlsBox => _tls;

    public TextBox? CharacterBox => _character;

    public void FocusFirst()
    {
        if (_model.InForm && _host is not null)
        {
            _host.Focus();
        }
        else
        {
            Focusable = true;
            Focus();
        }
    }

    public void Detach() => _session.Changed -= OnChanged;

    /// <summary>A menu choice, as ⏎ on its row.</summary>
    public void Choose(WelcomeChoice choice)
    {
        _session.Choose(choice);
        if (_session.IsEnded)
        {
            _done();
        }
    }

    /// <summary>The form's Connect, with what its fields hold.</summary>
    public void Connect()
    {
        _session.Connect(new WelcomeForm(
            _host?.Text ?? string.Empty,
            _port?.Text ?? string.Empty,
            _tls?.IsChecked == true,
            _character?.Text ?? string.Empty,
            _passwordLater?.IsChecked == true));
        if (_session.IsEnded)
        {
            _done();
        }
    }

    private void OnChanged(WelcomeModel model) => Dispatcher.UIThread.Post(() =>
    {
        var wasForm = _model.InForm;
        _model = model;
        _selected = Array.IndexOf(Choices, model.Selected);
        if (model.InForm && wasForm && _drawnInForm)
        {
            DrawProblem();
            return;
        }

        Draw();
        FocusFirst();
    });

    private void Draw()
    {
        _body.Children.Clear();
        _choiceRows.Clear();
        _chips.Clear();
        _drawnInForm = _model.InForm;
        if (_model.InForm)
        {
            DrawForm();
        }
        else
        {
            DrawMenu();
        }
    }

    private void DrawMenu()
    {
        _host = _port = _character = null;
        _tls = _passwordLater = null;
        _heading.Text = WelcomeModel.Heading;
        _body.Children.Add(ModalParts.Dim(WelcomeModel.Intro, _colours, new Thickness(22, 0, 22, 10)));

        var list = new StackPanel { Margin = new Thickness(12, 0, 12, 4) };
        for (var i = 0; i < Choices.Length; i++)
        {
            var choice = Choices[i];
            var index = i;
            var (title, detail) = _model.Describe(choice);
            var words = new StackPanel();
            words.Children.Add(new TextBlock { Text = NativeKeys.Words(title), FontSize = NativeTokens.Title, FontWeight = FontWeight.Medium, Foreground = _colours.Brush(_colours.Text) });
            words.Children.Add(new TextBlock { Text = NativeKeys.Words(detail), FontSize = NativeTokens.Body, Foreground = _colours.Brush(_colours.Dim) });
            var row = ModalParts.Row(words, title, () => SelectChoice(index), () => Choose(choice));
            row.Padding = new Thickness(12, 8);
            _choiceRows.Add(row);
            list.Children.Add(row);
        }

        _body.Children.Add(list);

        var example = new TextBlock { Margin = new Thickness(22, 6, 22, 0), FontSize = NativeTokens.Body, Foreground = _colours.Brush(_colours.Dim), TextWrapping = TextWrapping.Wrap };
        example.Inlines!.Add(new global::Avalonia.Controls.Documents.Run(WelcomeModel.ExampleLead + " "));
        example.Inlines.Add(new global::Avalonia.Controls.Documents.Run(WelcomeModel.ExampleCommand)
        {
            Foreground = _colours.Brush(_colours.Accent),
            FontFamily = NativeTokens.MonoFont,
        });
        _body.Children.Add(example);

        _body.Children.Add(ModalParts.Dim(_model.LayoutHeading, _colours, new Thickness(22, 14, 22, 6)));
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(22, 0, 22, 4) };
        foreach (var layout in Layouts)
        {
            var chosen = layout;
            var chip = ModalParts.Button(WelcomeModel.LayoutName(layout), null, _colours, () => _session.SetLayout(chosen), accent: layout == _model.Layout);
            chip.Focusable = false;
            chip.Padding = new Thickness(12, 4);
            _chips.Add(chip);
            chips.Children.Add(chip);
        }

        _body.Children.Add(chips);
        _body.Children.Add(ModalParts.Dim(NativeKeys.Words(WelcomeModel.LayoutNote), _colours, new Thickness(22, 2, 22, 0)));
        _footer.Text = NativeKeys.Words($"{WelcomeModel.MenuKeys.Left} · {WelcomeModel.MenuKeys.Right}");
        HighlightChoice();
    }

    private void DrawForm()
    {
        _heading.Text = WelcomeModel.FormHeading;
        _body.Children.Add(ModalParts.Dim(NativeKeys.Words(_model.FormIntroLine), _colours, new Thickness(22, 0, 22, 10)));
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"),
            Margin = new Thickness(22, 0, 22, 0),
        };

        TextBlock Label(string text, int row)
        {
            var label = new TextBlock { Text = text, Foreground = _colours.Brush(_colours.Dim), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            return label;
        }

        TextBox Field(string name, string text, string placeholder, int row, int max)
        {
            var box = new TextBox { Text = text, PlaceholderText = placeholder, MaxLength = max, Margin = new Thickness(0, 4), Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(NativeTokens.RadiusM) };
            global::Avalonia.Automation.AutomationProperties.SetName(box, name);
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            return box;
        }

        CheckBox Check(string text, bool on, int row)
        {
            var box = new CheckBox { Content = text, IsChecked = on, Margin = new Thickness(0, 2) };
            Grid.SetRow(box, row);
            Grid.SetColumn(box, 1);
            grid.Children.Add(box);
            return box;
        }

        string Named(WelcomeField field) => WelcomeModel.FieldLabel(field);
        Label(Named(WelcomeField.Host), 0);
        _host = Field(Named(WelcomeField.Host), _model.Host, WelcomeModel.HostPlaceholder, 0, WelcomeModel.MaxHost);
        Label(Named(WelcomeField.Port), 1);
        _port = Field(Named(WelcomeField.Port), _model.Port, "4000", 1, 5);
        _port.HorizontalAlignment = HorizontalAlignment.Left;
        _port.Width = 110;
        Label(Named(WelcomeField.Tls), 2);
        _tls = Check(WelcomeModel.TlsOn, _model.Tls, 2);
        Label(Named(WelcomeField.Character), 3);
        _character = Field(Named(WelcomeField.Character), _model.Character, WelcomeModel.CharacterPlaceholder, 3, WelcomeModel.MaxCharacter);
        Label(Named(WelcomeField.Password), 4);
        _passwordLater = Check(NativeKeys.Words(WelcomeModel.PasswordOn), _model.PasswordLater, 4);
        _body.Children.Add(grid);

        _body.Children.Add(ModalParts.Dim(_model.FormLayoutLine, _colours, new Thickness(22, 10, 22, 0)));
        var problem = new TextBlock { Name = "Problem", Margin = new Thickness(22, 6, 22, 0), TextWrapping = TextWrapping.Wrap, Foreground = _colours.Brush(_colours.Warn) };
        _body.Children.Add(problem);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10, Margin = new Thickness(22, 12, 22, 0) };
        var back = ModalParts.Button("Back", "Esc", _colours, () => _session.Back());
        var connect = ModalParts.Button("Connect", "Enter", _colours, Connect, accent: true);
        buttons.Children.Add(back);
        buttons.Children.Add(connect);
        _body.Children.Add(buttons);
        _footer.Text = "Tab moves between the fields";
        DrawProblem();
    }

    private void DrawProblem()
    {
        if (_body.Children.OfType<TextBlock>().FirstOrDefault(t => t.Name == "Problem") is { } problem)
        {
            problem.Text = NativeKeys.Words(_model.Problem);
            problem.IsVisible = _model.Problem is not null;
        }
    }

    private void SelectChoice(int index)
    {
        if (index != _selected)
        {
            _selected = index;
            HighlightChoice();
        }
    }

    private void HighlightChoice()
    {
        for (var i = 0; i < _choiceRows.Count; i++)
        {
            _choiceRows[i].Background = i == _selected ? _colours.Brush(_colours.Selected) : Brushes.Transparent;
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (_model.InForm)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _session.Back();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Connect();
            }

            return;
        }

        var handled = true;
        switch (e.Key)
        {
            case Key.Up:
                SelectChoice(ModalParts.Step(_selected, -1, Choices.Length));
                break;
            case Key.Down:
                SelectChoice(ModalParts.Step(_selected, 1, Choices.Length));
                break;
            case Key.Left:
            case Key.Right:
                var at = Array.IndexOf(Layouts, _model.Layout);
                _session.SetLayout(Layouts[ModalParts.Step(at, e.Key == Key.Left ? -1 : 1, Layouts.Length)]);
                break;
            case Key.Enter:
                Choose(Selected);
                break;
            case Key.Escape:
                _session.Dismiss();
                _done();
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }
}
