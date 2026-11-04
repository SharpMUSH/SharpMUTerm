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
/// The window's script console: whose scripts and how many loaded, what they printed and the errors they raised, a
/// one-line Lua prompt with its history on ↑↓, the character's <c>store.*</c> with Delete, and Reload. Drawn from the
/// client's <see cref="ScriptConsoleModel"/>, the record the cells draw; every gesture goes back through the
/// <see cref="ScriptConsoleSession"/> to the client's own methods, and the client republishes after each.
/// </summary>
internal sealed class ScriptConsoleView : DockPanel, IModalView
{
    private readonly ScriptConsoleSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly TextBlock _summary;
    private readonly TextBox _prompt;
    private readonly StackPanel _output;
    private readonly ScrollViewer _outputScroller;
    private readonly StackPanel _store;
    private readonly TextBlock _storeHeading;
    private readonly Button _delete;
    private readonly List<Border> _storeRows = [];
    private readonly FontFamily _mono = new($"{CellFonts.BundledFamily}, DejaVu Sans Mono, Consolas, Menlo, monospace");
    private ScriptConsoleModel _model;
    private int _selectedKey = -1;
    private int _historyAt = -1;

    public ScriptConsoleView(ScriptConsoleSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        _model = session.Model;

        var heading = ModalParts.Heading("Script console", colours);
        SetDock(heading, Dock.Top);
        Children.Add(heading);

        _summary = ModalParts.Dim(string.Empty, colours);
        SetDock(_summary, Dock.Top);
        Children.Add(_summary);

        var footer = ModalParts.Footer(ScriptConsoleModel.Keys, colours);
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(14, 8, 14, 0), HorizontalAlignment = HorizontalAlignment.Right };
        _delete = ModalParts.Button("Delete key", "Del", colours, DeleteSelected);
        buttons.Children.Add(_delete);
        if (_session.CanEdit)
        {
            buttons.Children.Add(ModalParts.Button("Edit a script…", "Ctrl+E", colours, EditScript));
        }

        buttons.Children.Add(ModalParts.Button("Reload scripts", "Ctrl+L", colours, Reload));
        SetDock(buttons, Dock.Bottom);
        Children.Add(buttons);

        _prompt = new TextBox
        {
            FontFamily = _mono,
            FontSize = NativeTokens.Title,
            Margin = new Thickness(14, 8, 14, 0),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            MaxLength = ScriptConsoleModel.MaxInput,
            PlaceholderText = "Lua — try 1 + 1 or print(world.name)",
            InnerLeftContent = new TextBlock
            {
                Text = "lua",
                FontFamily = _mono,
                Foreground = colours.Brush(colours.Accent),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            },
        };
        global::Avalonia.Automation.AutomationProperties.SetName(_prompt, "Lua prompt");
        SetDock(_prompt, Dock.Bottom);
        Children.Add(_prompt);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,240"), MinHeight = 260, Margin = new Thickness(0, 4, 0, 0) };

        _output = new StackPanel { Margin = new Thickness(14, 6, 8, 6) };
        _outputScroller = new ScrollViewer
        {
            Content = _output,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        var outputFrame = new Border
        {
            BorderBrush = colours.Brush(colours.Rule),
            BorderThickness = new Thickness(0, 1, 1, 1),
            Child = _outputScroller,
        };
        Grid.SetColumn(outputFrame, 0);
        body.Children.Add(outputFrame);

        var storePanel = new DockPanel();
        _storeHeading = ModalParts.GroupHeader("Store", colours, first: true);
        SetDock(_storeHeading, Dock.Top);
        storePanel.Children.Add(_storeHeading);
        var (storeFrame, store, _) = ModalParts.List(colours);
        _store = store;
        storePanel.Children.Add(storeFrame);
        Grid.SetColumn(storePanel, 2);
        body.Children.Add(storePanel);
        Children.Add(body);

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _session.Changed += OnChanged;
        Draw();
    }

    public string Title => "Script console";

    public double PreferredWidth => 860;

    public bool Centred => false;

    /// <summary>The model drawn now, for a test.</summary>
    public ScriptConsoleModel Model => _model;

    /// <summary>The prompt, for a test.</summary>
    public TextBox Prompt => _prompt;

    /// <summary>The store key Delete removes, or null.</summary>
    public string? SelectedKey => _selectedKey >= 0 && _selectedKey < _model.Store.Count ? _model.Store[_selectedKey].Key : null;

    public void FocusFirst() => _prompt.Focus();

    public void Detach() => _session.Changed -= OnChanged;

    /// <summary>⏎ at the prompt: hands the line to the client and clears the box.</summary>
    public void Run()
    {
        var line = _prompt.Text ?? string.Empty;
        if (_session.Run(line))
        {
            _prompt.Text = string.Empty;
            _historyAt = -1;
        }
    }

    /// <summary>Picks store row <paramref name="index"/>.</summary>
    public void SelectKey(int index)
    {
        _selectedKey = index;
        Highlight();
    }

    /// <summary>Delete on the store key picked.</summary>
    public void DeleteSelected()
    {
        if (SelectedKey is { } key)
        {
            _session.Delete(key);
        }
    }

    /// <summary>Reload scripts.</summary>
    public void Reload() => _session.Reload();

    /// <summary>Edit a script: the console closes and the client lists the character's scripts (⌃P), the cells' ⌃E.</summary>
    public void EditScript()
    {
        if (_session.CanEdit)
        {
            _session.EditScript();
            _done();
        }
    }

    private void OnChanged(ScriptConsoleModel model) => Dispatcher.UIThread.Post(() =>
    {
        _model = model;
        Draw();
    });

    private void Draw()
    {
        _summary.Text = NativeKeys.Words(_model.Summary);
        _summary.Foreground = _colours.Brush(_model.CanRun ? _colours.Dim : _colours.Warn);
        _prompt.IsEnabled = _model.CanRun;

        _output.Children.Clear();
        if (_model.Lines.Count == 0)
        {
            _output.Children.Add(Line(_model.CanRun ? ScriptConsoleModel.Empty : _model.Unavailable ?? string.Empty, _colours.Dim, string.Empty));
        }

        for (var i = 0; i < _model.Lines.Count; i++)
        {
            var line = _model.Lines[i];
            var colour = line.Kind switch
            {
                ScriptConsoleKind.Error => _colours.Warn,
                ScriptConsoleKind.Result => _colours.Accent,
                ScriptConsoleKind.Input or ScriptConsoleKind.Note => _colours.Dim,
                _ => _colours.Text,
            };
            var index = i;
            _output.Children.Add(_model.ErrorAt(i) is { } at
                ? ErrorLink(line.Text, colour, at, () => OpenError(index))
                : Line(line.Text, colour, ScriptConsoleModel.Mark(line.Kind)));
        }

        _outputScroller.ScrollToEnd();

        _storeHeading.Text = _model.StoreCount > _model.Store.Count
            ? $"STORE · {_model.Store.Count} OF {_model.StoreCount}"
            : $"STORE · {_model.StoreCount}";
        _store.Children.Clear();
        _storeRows.Clear();
        if (_model.Store.Count == 0)
        {
            _store.Children.Add(ModalParts.Dim(_model.CanRun ? "nothing stored" : string.Empty, _colours, new Thickness(12, 8)));
        }

        for (var i = 0; i < _model.Store.Count; i++)
        {
            var entry = _model.Store[i];
            var index = i;
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = entry.Key, FontWeight = FontWeight.SemiBold, Foreground = _colours.Brush(_colours.Text), TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = entry.Value, FontFamily = _mono, FontSize = NativeTokens.Small, Foreground = _colours.Brush(_colours.Dim), TextTrimming = TextTrimming.CharacterEllipsis });
            var row = ModalParts.Row(text, entry.Key, () => SelectKey(index), () => SelectKey(index));
            _storeRows.Add(row);
            _store.Children.Add(row);
        }

        _selectedKey = _model.Store.Count == 0 ? -1 : Math.Clamp(_selectedKey, -1, _model.Store.Count - 1);
        Highlight();
    }

    /// <summary>
    /// A click on error row <paramref name="index"/>: the console closes and the client opens the script it names on its
    /// line (<see cref="ScriptConsoleSession.OpenError"/>), the cells' link.
    /// </summary>
    public void OpenError(int index)
    {
        if (_session.OpenError(index))
        {
            _done();
        }
    }

    /// <summary>An error row naming a script line: underlined, a hand under the pointer, and a click opens that line.</summary>
    private Control ErrorLink(string text, Color colour, (string File, int Line) at, Action open)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        var gutter = new TextBlock { Text = ScriptConsoleModel.Mark(ScriptConsoleKind.Error), Width = 18, FontFamily = _mono, Foreground = _colours.Brush(colour), FontSize = NativeTokens.Body };
        SetDock(gutter, Dock.Left);
        row.Children.Add(gutter);
        row.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = _mono,
            FontSize = NativeTokens.Body,
            Foreground = _colours.Brush(colour),
            TextWrapping = TextWrapping.Wrap,
            TextDecorations = TextDecorations.Underline,
        });
        ToolTip.SetTip(row, $"Open {at.File} at line {at.Line.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        global::Avalonia.Automation.AutomationProperties.SetName(row, text);
        row.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                open();
            }
        };
        return row;
    }

    private Control Line(string text, Color colour, string mark)
    {
        var row = new DockPanel { Margin = new Thickness(0, 1) };
        var gutter = new TextBlock { Text = mark, Width = 18, FontFamily = _mono, Foreground = _colours.Brush(colour), FontSize = NativeTokens.Body };
        SetDock(gutter, Dock.Left);
        row.Children.Add(gutter);
        row.Children.Add(new SelectableTextBlock
        {
            Text = text,
            FontFamily = _mono,
            FontSize = NativeTokens.Body,
            Foreground = _colours.Brush(colour),
            TextWrapping = TextWrapping.Wrap,
        });
        return row;
    }

    private void Highlight()
    {
        for (var i = 0; i < _storeRows.Count; i++)
        {
            _storeRows[i].Background = i == _selectedKey ? _colours.Brush(_colours.Selected) : Brushes.Transparent;
        }

        _delete.IsEnabled = SelectedKey is not null;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                _session.Dismiss();
                _done();
                return;
            case Key.L when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                Reload();
                return;
            case Key.E when e.KeyModifiers == KeyModifiers.Control && _session.CanEdit:
                e.Handled = true;
                EditScript();
                return;
            case Key.Enter when _prompt.IsFocused:
                e.Handled = true;
                Run();
                return;
            case Key.Up or Key.Down when _prompt.IsFocused && e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                Recall(e.Key == Key.Up ? -1 : 1);
                return;
            case Key.Delete when !_prompt.IsFocused && SelectedKey is not null:
                e.Handled = true;
                DeleteSelected();
                return;
            case Key.Up or Key.Down when !_prompt.IsFocused && _model.Store.Count > 0:
                e.Handled = true;
                SelectKey(Math.Clamp((_selectedKey < 0 ? 0 : _selectedKey) + (e.Key == Key.Up ? -1 : 1), 0, _model.Store.Count - 1));
                return;
        }
    }

    /// <summary>↑↓ at the prompt: walks what was typed, newest first, and back to an empty line.</summary>
    private void Recall(int delta)
    {
        var history = _model.History;
        if (history.Count == 0)
        {
            return;
        }

        var at = _historyAt < 0 ? history.Count : _historyAt;
        at = Math.Clamp(at + delta, 0, history.Count);
        _historyAt = at == history.Count ? -1 : at;
        _prompt.Text = _historyAt < 0 ? string.Empty : history[_historyAt];
        _prompt.CaretIndex = _prompt.Text.Length;
    }
}
