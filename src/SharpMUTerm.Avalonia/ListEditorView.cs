using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;
using SharpMUTerm.Core.Text;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// A list section (F2 triggers, F3 aliases, F4 key bindings, F6 timers) as a master-detail editor, drawn from its
/// <see cref="SettingsCollection"/>: the items down the left grouped by trigger set, each with its enable check box,
/// and add, duplicate, delete and move above them; the selected item's declared fields on the right under their group
/// headings. A package's sets are listed read-only, with a button that copies one into the player's sets to edit.
/// F2's pattern carries the live test: how many of the recent lines it matches, and the newest few with the match
/// marked, re-run as the pattern is typed.
/// <para>
/// Nothing here writes the configuration: every change goes to the form, which commits it through the client's own
/// save funnel; <see cref="Refresh"/> catches up once the change has landed.
/// </para>
/// </summary>
internal sealed class ListEditorView : Grid
{
    private readonly SettingsForm _form;
    private readonly SettingsSectionContribution _section;
    private readonly SettingsCollection _collection;
    private readonly Func<SettingsField, bool> _visible;
    private readonly IBrush _accent;
    private readonly ListBox _list;
    private readonly StackPanel _detail;
    private readonly List<Action> _refreshers = [];
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private readonly List<(object Item, ListBoxItem Row, CheckBox Enabled)> _rows = [];
    private readonly List<Button> _copyButtons = [];
    private readonly StackPanel _preview = new() { Spacing = 3 };
    private object? _shownItem;
    private bool _syncing;
    private bool _choosing;

    public ListEditorView(SettingsForm form, SettingsSectionContribution section, Func<SettingsField, bool> visible, IBrush accent)
    {
        _form = form;
        _section = section;
        _collection = section.Collection!;
        _visible = visible;
        _accent = accent;
        ColumnDefinitions = new ColumnDefinitions("292,*");
        Margin = new Thickness(0, 10, 0, 0);

        AddButton = ToolButton("+ Add", $"Add a {_collection.Noun} to the selected set");
        DuplicateButton = ToolButton("Copy", $"Copy the selected {_collection.Noun}");
        DeleteButton = ToolButton("Delete", $"Delete the selected {_collection.Noun}");
        UpButton = ToolButton("↑", "Move up within its set");
        DownButton = ToolButton("↓", "Move down within its set");
        AddButton.Click += (_, _) => Add();
        DuplicateButton.Click += (_, _) => Act(item => _form.Duplicate(_section, item));
        DeleteButton.Click += (_, _) => Act(item => _form.Delete(_section, item) ? null : item);
        UpButton.Click += (_, _) => Act(item => _form.Move(_section, item, -1) ? item : item);
        DownButton.Click += (_, _) => Act(item => _form.Move(_section, item, 1) ? item : item);
        DuplicateButton.IsVisible = _collection.CanDuplicate;

        var tools = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var button in new[] { AddButton, DuplicateButton, DeleteButton, UpButton, DownButton })
        {
            button.Margin = new Thickness(0, 0, 4, 4);
            tools.Children.Add(button);
        }

        _list = new ListBox { Background = Brushes.Transparent };
        AutomationProperties.SetName(_list, _collection.Plural);
        _list.SelectionChanged += (_, _) => OnRowChosen();
        _list.KeyDown += OnListKey;

        var left = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 14, 0) };
        DockPanel.SetDock(tools, Dock.Top);
        left.Children.Add(tools);
        left.Children.Add(new Border
        {
            Child = _list,
            BorderThickness = new Thickness(1),
            BorderBrush = Hairline,
            CornerRadius = new CornerRadius(6),
        });
        Children.Add(left);

        _detail = new StackPanel { Spacing = 2, Margin = new Thickness(4, 0, 4, 16) };
        var scroller = new ScrollViewer { Content = _detail, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        SetColumn(scroller, 1);
        Children.Add(scroller);

        // The item this section had selected last (switched away from, or the window closed) wins over the first —
        // unless the form opened on an item of this list (the trigger builder's new rule).
        if (_form.Item is not { } chosen || !_collection.IsItem(chosen) || FindGroup(chosen) is null)
        {
            Select(_form.Recall(_section.Id) is { } kept && _collection.IsItem(kept) && FindGroup(kept) is not null ? kept : FirstItem());
        }
        else
        {
            _form.Remember(_section.Id, chosen);
        }

        BuildList();
        BuildDetail();
    }

    /// <summary>The add button.</summary>
    public Button AddButton { get; }

    /// <summary>The duplicate button (hidden where the list offers none).</summary>
    public Button DuplicateButton { get; }

    /// <summary>The delete button.</summary>
    public Button DeleteButton { get; }

    /// <summary>The move-up button.</summary>
    public Button UpButton { get; }

    /// <summary>The move-down button.</summary>
    public Button DownButton { get; }

    /// <summary>The list.</summary>
    public ListBox List => _list;

    /// <summary>The items listed, in order, each with its enable check box, for a test.</summary>
    public IReadOnlyList<(object Item, CheckBox Enabled)> Rows => _rows.Select(r => (r.Item, r.Enabled)).ToList();

    /// <summary>The copy-to-edit button of each package set listed, for a test.</summary>
    public IReadOnlyList<Button> CopyButtons => _copyButtons;

    /// <summary>The ids of the item fields drawn — the list's enable check box first — for a test.</summary>
    public IReadOnlyList<string> ShownFields { get; private set; } = [];

    /// <summary>The control drawn for each item field, by id.</summary>
    public IReadOnlyDictionary<string, Control> FieldControls => _controls;

    /// <summary>The live test's lines: its summary, then each sample, for a test.</summary>
    public IReadOnlyList<TextBlock> PreviewLines => _preview.Children.OfType<TextBlock>().ToList();

    /// <summary>The item selected.</summary>
    public object? SelectedItem => _form.Item;

    /// <summary>Selects <paramref name="item"/> and shows its fields.</summary>
    public void Choose(object? item)
    {
        Select(item);
        SelectRow(item);
        BuildDetail();
    }

    /// <summary>Catches up with a change that has landed: rows rebuilt, the detail re-read (or rebuilt for another item).</summary>
    public void Refresh()
    {
        if (_form.Item is { } item && FindGroup(item) is null)
        {
            // The selected item went (deleted, or moved by another surface): take the first there is.
            Select(FirstItem());
        }
        else
        {
            // An add or a duplicate selected its new item through the form.
            _form.Remember(_section.Id, _form.Item);
        }

        BuildList();
        if (!ReferenceEquals(_shownItem, _form.Item))
        {
            BuildDetail();
            return;
        }

        _syncing = true;
        try
        {
            foreach (var refresh in _refreshers)
            {
                refresh();
            }
        }
        finally
        {
            _syncing = false;
        }

        UpdateTools();
    }

    // ── the list ─────────────────────────────────────────────────────────────────────────────────

    private IBrush Hairline => new SolidColorBrush(_form.IsDark ? Color.Parse("#46464e") : Color.Parse("#c8c8d0"));

    private static Button ToolButton(string text, string tip)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 3), MinWidth = 32, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(button, NativeKeys.Words(tip));
        AutomationProperties.SetName(button, tip);
        return button;
    }

    // Every selection goes through here, so the section remembers it for the run.
    private void Select(object? item)
    {
        _form.Select(item);
        _form.Remember(_section.Id, item);
    }

    private object? FirstItem() => _form.Groups(_section).SelectMany(g => g.Items).FirstOrDefault();

    private SettingsItemGroup? FindGroup(object item) =>
        _form.Groups(_section).FirstOrDefault(g => g.Items.Any(i => ReferenceEquals(i, item)));

    private void BuildList()
    {
        _rows.Clear();
        _copyButtons.Clear();
        var items = new List<ListBoxItem>();
        var groups = _form.Groups(_section);
        foreach (var group in groups)
        {
            items.Add(GroupHeader(group));
            if (group.Items.Count == 0)
            {
                items.Add(new ListBoxItem
                {
                    Content = new TextBlock { Text = $"no {_collection.Plural}", Opacity = 0.6, FontStyle = FontStyle.Italic },
                    IsEnabled = false,
                    Padding = new Thickness(30, 2),
                });
            }

            foreach (var item in group.Items)
            {
                items.Add(ItemRow(group, item));
            }
        }

        if (groups.Count == 0)
        {
            items.Add(new ListBoxItem
            {
                Content = new TextBlock { Text = "No trigger set yet — make one on F5.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
                IsEnabled = false,
            });
        }

        _choosing = true;
        _list.ItemsSource = items;
        _choosing = false;
        SelectRow(_form.Item);
        UpdateTools();
    }

    private ListBoxItem GroupHeader(SettingsItemGroup group)
    {
        var row = new DockPanel { LastChildFill = true };
        if (group.ReadOnly)
        {
            var copy = new Button { Content = "Copy to edit", Padding = new Thickness(8, 1), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(copy, "A package's set is replaced when the package updates; copy it into your own sets to change it");
            AutomationProperties.SetName(copy, $"Copy {group.Set.Name} to edit");
            var set = group.Set;
            copy.Click += (_, _) => _form.CopyToEdit(set);
            _copyButtons.Add(copy);
            DockPanel.SetDock(copy, Dock.Right);
            row.Children.Add(copy);
        }

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = group.Label, FontWeight = FontWeight.SemiBold, FontSize = 12, Foreground = _accent, TextTrimming = TextTrimming.CharacterEllipsis });
        if (group.ReadOnly)
        {
            words.Children.Add(new TextBlock { Text = "package · read-only", FontSize = 11, Opacity = 0.65 });
        }

        row.Children.Add(words);
        return new ListBoxItem { Content = row, Focusable = false, IsHitTestVisible = group.ReadOnly, Padding = new Thickness(8, 8, 8, 2) };
    }

    private ListBoxItem ItemRow(SettingsItemGroup group, object item)
    {
        var enabled = new CheckBox
        {
            IsChecked = _form.IsEnabled(_section, item),
            IsEnabled = !group.ReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            Margin = new Thickness(0, 0, 4, 0),
        };
        AutomationProperties.SetName(enabled, $"Enable {_collection.TitleOf(item)}");
        enabled.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing && enabled.IsChecked is { } on && on != _form.IsEnabled(_section, item))
            {
                _form.SetEnabled(_section, item, on);
            }
        };

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = _collection.TitleOf(item);
        words.Children.Add(new TextBlock { Text = title.Length == 0 ? "(unnamed)" : NativeKeys.Words(title), TextTrimming = TextTrimming.CharacterEllipsis });
        if (_collection.DetailOf(item) is { Length: > 0 } detail)
        {
            words.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 11,
                Opacity = 0.65,
                FontFamily = Mono,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(enabled, Dock.Left);
        row.Children.Add(enabled);
        row.Children.Add(words);
        var listItem = new ListBoxItem { Content = row, Tag = item, Padding = new Thickness(14, 4, 8, 4) };
        if (group.ReadOnly)
        {
            listItem.Opacity = 0.8;
        }

        _rows.Add((item, listItem, enabled));
        return listItem;
    }

    private static FontFamily Mono => FontFamily.Parse("JetBrains Mono, Cascadia Mono, DejaVu Sans Mono, monospace");

    private void SelectRow(object? item)
    {
        _choosing = true;
        _list.SelectedItem = _rows.FirstOrDefault(r => ReferenceEquals(r.Item, item)).Row;
        _choosing = false;
    }

    private void OnRowChosen()
    {
        if (_choosing || _list.SelectedItem is not ListBoxItem { Tag: { } item } || ReferenceEquals(item, _form.Item))
        {
            return;
        }

        Select(item);
        BuildDetail();
    }

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        if (_form.Item is not { } item)
        {
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down)
        {
            _form.Move(_section, item, e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            _form.Delete(_section, item);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            _form.SetEnabled(_section, item, !_form.IsEnabled(_section, item));
            e.Handled = true;
        }
    }

    private void Add()
    {
        var groups = _form.Groups(_section);
        var set = (_form.Item is { } item ? FindGroup(item) : null) is { ReadOnly: false } own
            ? own.Set
            : groups.FirstOrDefault(g => !g.ReadOnly)?.Set;
        if (set is not null)
        {
            _form.Add(_section, set);
        }
    }

    private void Act(Func<object, object?> action)
    {
        if (_form.Item is { } item)
        {
            action(item);
        }
    }

    private void UpdateTools()
    {
        var item = _form.Item;
        var group = item is null ? null : FindGroup(item);
        var editable = group is { ReadOnly: false };
        var at = group is null ? -1 : group.Items.ToList().FindIndex(i => ReferenceEquals(i, item));
        AddButton.IsEnabled = _form.Groups(_section).Any(g => !g.ReadOnly);
        DuplicateButton.IsEnabled = editable;
        DeleteButton.IsEnabled = editable;
        UpButton.IsEnabled = editable && at > 0;
        DownButton.IsEnabled = editable && group is not null && at < group.Items.Count - 1;
    }

    // ── the detail ───────────────────────────────────────────────────────────────────────────────

    private void BuildDetail()
    {
        _detail.Children.Clear();
        _refreshers.Clear();
        _controls.Clear();
        _preview.Children.Clear();
        if (_preview.Parent is Border previous)
        {
            // The live test's panel is kept across items; the border it sat in last time goes with the old detail.
            previous.Child = null;
        }

        _shownItem = _form.Item;
        UpdateTools();

        if (_form.Item is not { } item || FindGroup(item) is not { } group)
        {
            ShownFields = [];
            _detail.Children.Add(new TextBlock
            {
                Text = _form.Groups(_section).Any(g => !g.ReadOnly)
                    ? $"No {_collection.Noun} selected. Add one with + Add."
                    : $"There is no trigger set to hold {_collection.Plural} yet.",
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var title = _collection.TitleOf(item);
        _detail.Children.Add(new TextBlock { Text = title.Length == 0 ? "(unnamed)" : NativeKeys.Words(title), FontSize = 18, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (group.ReadOnly)
        {
            var banner = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 4) };
            var copy = new Button { Content = "Copy to edit", Padding = new Thickness(10, 3) };
            var set = group.Set;
            copy.Click += (_, _) => _form.CopyToEdit(set);
            DockPanel.SetDock(copy, Dock.Right);
            banner.Children.Add(copy);
            banner.Children.Add(new TextBlock
            {
                Text = $"From a package — read-only. {char.ToUpper(_collection.Noun[0], CultureInfo.InvariantCulture)}{_collection.Noun[1..]}s in it are replaced when the package updates.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            });
            _detail.Children.Add(banner);
        }
        else
        {
            _detail.Children.Add(new TextBlock { Text = $"in {group.Label}", Opacity = 0.65, Margin = new Thickness(0, 0, 0, 4) });
        }

        var shown = new List<string>();
        if (_visible(_collection.Field(_collection.EnabledField)))
        {
            shown.Add(_collection.EnabledField);
            if (_rows.FirstOrDefault(r => ReferenceEquals(r.Item, item)).Enabled is { } check)
            {
                _controls[_collection.EnabledField] = check;
            }
        }

        string? heading = null;
        foreach (var field in _collection.Fields)
        {
            if (field.Id == _collection.EnabledField || !_visible(field))
            {
                continue;
            }

            if (field.Group is { } g && !string.Equals(g, heading, StringComparison.Ordinal))
            {
                heading = g;
                _detail.Children.Add(new TextBlock { Text = g, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = _accent, Margin = new Thickness(0, 14, 0, 2) });
            }

            var control = ControlFor(field);
            control.IsEnabled = !group.ReadOnly;
            AutomationProperties.SetName(control, field.Label);
            _controls[field.Id] = control;
            shown.Add(field.Id);
            _detail.Children.Add(Row(field, control));
            if (field is TextField { Rule: TextRule.TriggerPattern })
            {
                _detail.Children.Add(new Border
                {
                    Child = _preview,
                    BorderThickness = new Thickness(1),
                    BorderBrush = Hairline,
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6),
                    Margin = new Thickness(0, 2, 0, 6),
                });
                ShowPreview(null);
            }
        }

        ShownFields = shown;
    }

    private static Grid Row(SettingsField field, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("118,*"), Margin = new Thickness(0, 3), MinHeight = 34 };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        words.Children.Add(new TextBlock { Text = NativeKeys.Words(Capitalise(field.Label)), FontSize = 13, TextWrapping = TextWrapping.Wrap });
        if (field.Help is { } help)
        {
            ToolTip.SetTip(words, NativeKeys.Words(Capitalise(help)));
            ToolTip.SetTip(control, NativeKeys.Words(Capitalise(help)));
        }

        grid.Children.Add(words);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = control is CheckBox or ToggleSwitch ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    /// <summary>The live test, for the pattern in force or the one being typed.</summary>
    private void ShowPreview(string? typed)
    {
        _preview.Children.Clear();
        if (_form.Preview(typed) is not { } result)
        {
            return;
        }

        var summary = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        if (result.Problem is { } problem)
        {
            summary.Text = $"Live test: {problem}";
            summary.Opacity = 0.75;
        }
        else if (result.Tested == 0)
        {
            summary.Text = "Live test: no output yet to test against";
            summary.Opacity = 0.75;
        }
        else
        {
            summary.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"Live test: matches {result.Matched} of the last {result.Tested} lines");
        }

        _preview.Children.Add(summary);
        foreach (var sample in result.Samples)
        {
            var line = new TextBlock { FontFamily = Mono, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            var text = sample.Text;
            var start = Math.Clamp(sample.MatchStart, 0, text.Length);
            var length = Math.Clamp(sample.MatchLength, 0, text.Length - start);
            line.Inlines = new InlineCollection
            {
                new Run(text[..start]) { Foreground = line.Foreground },
                new Run(length == 0 ? "▏" : text.Substring(start, length))
                {
                    FontWeight = FontWeight.Bold,
                    Background = new SolidColorBrush(((ISolidColorBrush)_accent).Color, 0.28),
                },
                new Run(text[(start + length)..]),
            };
            _preview.Children.Add(line);
        }
    }

    /// <summary>The native control for an item field's kind, wired to the form and refreshed from it.</summary>
    private Control ControlFor(SettingsField field)
    {
        switch (field)
        {
            case BoolField when field.Binding is SettingBinding<bool> flag:
            {
                var box = new CheckBox { IsChecked = _form.Get(flag) };
                box.IsCheckedChanged += (_, _) =>
                {
                    if (!_syncing && box.IsChecked is { } on && on != _form.Get(flag))
                    {
                        _form.Set(flag, on);
                    }
                };
                _refreshers.Add(() => box.IsChecked = _form.Get(flag));
                return box;
            }

            case ChoiceField choice when field.Binding is SettingBinding<string> named:
            {
                var combo = new ComboBox { ItemsSource = _form.Choices(choice), SelectedItem = _form.Get(named), MaxWidth = 280 };
                combo.SelectionChanged += (_, _) =>
                {
                    if (!_syncing && combo.SelectedItem is string value && value != _form.Get(named))
                    {
                        _form.Set(named, value);
                    }
                };
                _refreshers.Add(() =>
                {
                    combo.ItemsSource = _form.Choices(choice);
                    combo.SelectedItem = _form.Get(named);
                });
                return combo;
            }

            case DecimalField number when field.Binding is SettingBinding<double> value:
            {
                var box = new NumericUpDown
                {
                    Minimum = (decimal)number.Minimum,
                    Maximum = (decimal)number.Maximum,
                    Increment = 1,
                    FormatString = "0.####",
                    Value = (decimal)_form.Get(value),
                    ClipValueToMinMax = true,
                    MaxWidth = 200,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                box.ValueChanged += (_, e) =>
                {
                    if (!_syncing && e.NewValue is { } v && (double)v != _form.Get(value))
                    {
                        _form.SetDecimal(number, (double)v);
                    }
                };
                _refreshers.Add(() =>
                {
                    if (!box.IsKeyboardFocusWithin)
                    {
                        box.Value = (decimal)_form.Get(value);
                    }
                });
                return box;
            }

            case HighlightField when field.Binding is SettingBinding<TerminalColor?> colour:
            {
                var names = HighlightColours.Palette.ToList();
                var current = HighlightColours.Format(_form.Get(colour));
                if (!names.Contains(current, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(current);
                }

                // Names, drawn as swatches by a template: a control as an item cannot also be the closed box's content.
                var combo = new ComboBox
                {
                    ItemsSource = names,
                    ItemTemplate = new FuncDataTemplate<string>((name, _) => Swatch(name)),
                    MaxWidth = 280,
                };
                void Pick() => combo.SelectedItem = names.FirstOrDefault(n =>
                    string.Equals(n, HighlightColours.Format(_form.Get(colour)), StringComparison.OrdinalIgnoreCase));
                Pick();
                combo.SelectionChanged += (_, _) =>
                {
                    if (!_syncing && combo.SelectedItem is string name && HighlightColours.TryParse(name, out var parsed)
                        && parsed != _form.Get(colour))
                    {
                        _form.Set(colour, parsed);
                    }
                };
                _refreshers.Add(Pick);
                return combo;
            }

            case FlagsField flags when field.Binding is SettingBinding<string> words:
            {
                var panel = new WrapPanel { Orientation = Orientation.Horizontal };
                var boxes = new List<CheckBox>();
                foreach (var name in flags.Names)
                {
                    var box = new CheckBox { Content = name, Margin = new Thickness(0, 0, 14, 0), IsChecked = FlagWords.IsListed(_form.Get(words), name) };
                    box.IsCheckedChanged += (_, _) =>
                    {
                        if (_syncing)
                        {
                            return;
                        }

                        var set = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content!).ToArray();
                        var value = set.Length == 0 ? FlagWords.NoFlags : string.Join(' ', set);
                        if (value != _form.Get(words))
                        {
                            _form.Set(words, value);
                        }
                    };
                    boxes.Add(box);
                    panel.Children.Add(box);
                }

                _refreshers.Add(() =>
                {
                    foreach (var box in boxes)
                    {
                        box.IsChecked = FlagWords.IsListed(_form.Get(words), (string)box.Content!);
                    }
                });
                return panel;
            }

            case KeyField key when field.Binding is SettingBinding<string> chord:
                return KeyCapture(key, chord);

            case TextField text when field.Binding is SettingBinding<string?>:
                return TextControl(text);

            default:
                return new TextBlock { Text = "set on its own screen", Opacity = 0.7 };
        }
    }

    private Control TextControl(TextField text)
    {
        var suggestions = _form.Suggestions(text);
        TextBox? box = null;
        AutoCompleteBox? auto = null;
        if (suggestions.Count > 0)
        {
            auto = new AutoCompleteBox
            {
                Text = _form.Text(text),
                ItemsSource = suggestions,
                FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
                MinimumPrefixLength = 0,
            };
        }
        else
        {
            box = new TextBox { Text = _form.Text(text) };
            if (text.Rule == TextRule.Lines)
            {
                box.AcceptsReturn = true;
                box.TextWrapping = TextWrapping.Wrap;
                box.MinHeight = 70;
            }

            if (text.Rule is TextRule.TriggerPattern or TextRule.RequiredPattern)
            {
                box.FontFamily = Mono;
            }
        }

        Control control = (Control?)auto ?? box!;
        string Current() => auto?.Text ?? box!.Text ?? string.Empty;
        void Put(string value)
        {
            if (auto is not null)
            {
                auto.Text = value;
            }
            else
            {
                box!.Text = value;
            }
        }

        void Commit()
        {
            if (_syncing || Current() == _form.Text(text))
            {
                return;
            }

            if (_form.SetText(text, Current()) is { } refused)
            {
                // Refused by the rule the cell screen's field applies: put the value back and say why.
                Put(_form.Text(text));
                ToolTip.SetTip(control, refused);
                ToolTip.SetIsOpen(control, true);
            }
            else
            {
                ToolTip.SetTip(control, NativeKeys.Words(text.Help));
            }
        }

        control.LostFocus += (_, _) => Commit();
        control.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (text.Rule != TextRule.Lines || e.KeyModifiers == KeyModifiers.Control))
            {
                Commit();
                e.Handled = true;
            }
        };
        if (text.Rule == TextRule.TriggerPattern && box is not null)
        {
            box.TextChanged += (_, _) =>
            {
                if (!_syncing)
                {
                    ShowPreview(box.Text);
                }
            };
        }

        _refreshers.Add(() =>
        {
            if (!control.IsKeyboardFocusWithin)
            {
                Put(_form.Text(text));
            }

            if (text.Rule == TextRule.TriggerPattern)
            {
                ShowPreview(control.IsKeyboardFocusWithin ? Current() : null);
            }
        });
        return control;
    }

    /// <summary>
    /// A key binding's chord: a button that takes the next chord pressed on it, refused with this host's reason when
    /// the window cannot deliver it or another binding holds it — the cell capture's two refusals.
    /// </summary>
    private Control KeyCapture(KeyField field, SettingBinding<string> chord)
    {
        var button = new Button { Content = NativeKeys.Words(_form.Get(chord)), MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left };
        var note = new TextBlock { FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        var capturing = false;
        void Show()
        {
            button.Content = capturing ? "Press a key… (Esc cancels)" : NativeKeys.Words(_form.Get(chord));
            if (!capturing)
            {
                note.Text = _form.Context.KeyProblem(_form.Get(chord)) is { } problem ? $"▲ {NativeKeys.Words(problem)}" : string.Empty;
            }
        }

        button.Click += (_, _) =>
        {
            capturing = true;
            Show();
        };
        button.LostFocus += (_, _) =>
        {
            capturing = false;
            Show();
        };
        button.KeyDown += (_, e) =>
        {
            if (!capturing)
            {
                return;
            }

            e.Handled = true;
            if (e.Key == Key.Escape)
            {
                capturing = false;
                Show();
                return;
            }

            if (KeyMapper.FromKeyDown(e.Key, e.KeyModifiers) is not { } info || MacroKeys.Capture(info) is not { } descriptor)
            {
                return;
            }

            capturing = false;
            var refused = _form.SetKey(field, descriptor);
            Show();
            if (refused is not null)
            {
                note.Text = NativeKeys.Words($"{descriptor}: {refused}");
            }
        };
        Show();
        _refreshers.Add(Show);
        var stack = new StackPanel();
        stack.Children.Add(button);
        stack.Children.Add(note);
        return stack;
    }

    private static StackPanel Swatch(string name)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Tag = name };
        var hex = HighlightColours.TryParse(name, out var colour) ? HighlightColours.Hex(colour) : null;
        panel.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(3),
            Background = hex is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hex)),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(hex is null ? 1 : 0),
        });
        panel.Children.Add(new TextBlock { Text = name });
        return panel;
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
