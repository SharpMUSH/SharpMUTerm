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
    private readonly NativeTokens _tokens;
    private readonly ListBox _list;
    private readonly StackPanel _detail;
    private readonly List<Action> _refreshers = [];
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private readonly List<(object Item, ListBoxItem Row, CheckBox Enabled)> _rows = [];
    private readonly List<Button> _copyButtons = [];
    private readonly List<string> _headings = [];
    private readonly StackPanel _preview = new() { Spacing = NativeTokens.Space4 };
    private object? _shownItem;
    private TriggerSet? _chosenSet;
    private TriggerSet? _shownSet;
    private bool _chosenSetLanded;
    private bool _confirmingSetDelete;
    private readonly List<(TriggerSet Set, ListBoxItem Row)> _setRows = [];
    private readonly List<(SetUser User, CheckBox Box)> _userBoxes = [];
    private bool _syncing;
    private bool _choosing;

    public ListEditorView(SettingsForm form, SettingsSectionContribution section, Func<SettingsField, bool> visible, NativeTokens tokens)
    {
        _form = form;
        _section = section;
        _collection = section.Collection!;
        _visible = visible;
        _tokens = tokens;
        _accent = tokens.Brush(tokens.Accent);
        ColumnDefinitions = new ColumnDefinitions("316,*");
        Margin = new Thickness(0, NativeTokens.Space10, 0, 0);

        AddButton = ToolButton("+ Add", $"Add a {_collection.Noun} to the selected set");
        NewSetButton = ToolButton("+ Set", "Make a new trigger set, used by the character in front");
        DuplicateButton = ToolButton("Copy", $"Copy the selected {_collection.Noun}");
        DeleteButton = ToolButton("Delete", $"Delete the selected {_collection.Noun}");
        UpButton = ToolButton("↑", "Move up within its set");
        DownButton = ToolButton("↓", "Move down within its set");
        AddButton.Click += (_, _) => Add();
        NewSetButton.Click += (_, _) => NewSet();
        DuplicateButton.Click += (_, _) => Act(item => _form.Duplicate(_section, item));
        DeleteButton.Click += (_, _) => Act(item => _form.Delete(_section, item) ? null : item);
        UpButton.Click += (_, _) => Act(item => _form.Move(_section, item, -1) ? item : item);
        DownButton.Click += (_, _) => Act(item => _form.Move(_section, item, 1) ? item : item);
        DuplicateButton.IsVisible = _collection.CanDuplicate;

        var tools = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, NativeTokens.Space6) };
        foreach (var button in new[] { AddButton, DuplicateButton, DeleteButton, UpButton, DownButton, NewSetButton })
        {
            button.Margin = new Thickness(0, 0, NativeTokens.Space4, NativeTokens.Space4);
            tools.Children.Add(button);
        }

        _list = new ListBox { Background = Brushes.Transparent };
        AutomationProperties.SetName(_list, _collection.Plural);
        _list.SelectionChanged += (_, _) => OnRowChosen();
        _list.KeyDown += OnListKey;

        // Both halves are cards on the page, as the settings mockup draws its sections: the list with its tools over it,
        // and the selected item's fields.
        var page = tokens.Page;
        var left = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(tools, Dock.Top);
        tools.Margin = new Thickness(NativeTokens.Space10, NativeTokens.Space10, NativeTokens.Space10, NativeTokens.Space4);
        left.Children.Add(tools);
        _list.Margin = new Thickness(NativeTokens.Space4, 0, NativeTokens.Space4, NativeTokens.Space6);
        left.Children.Add(_list);
        Children.Add(PageCard(left, page, new Thickness(0), new Thickness(0, 0, NativeTokens.Space16, 0)));

        _detail = new StackPanel { Spacing = NativeTokens.Space2, Margin = new Thickness(NativeTokens.Space20, NativeTokens.Space16, NativeTokens.Space20, NativeTokens.Space16) };
        var scroller = new ScrollViewer { Content = _detail, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var detailCard = PageCard(scroller, page, new Thickness(0), new Thickness(0));
        SetColumn(detailCard, 1);
        Children.Add(detailCard);

        // A form opened on a set (the terminal's `s` key has one, ⌃P's way into a set) opens that set's page.
        var openSet = _form.Item as TriggerSet;

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

        if (openSet is not null && _form.IsOwnSet(openSet))
        {
            _chosenSet = openSet;
        }

        BuildList();
        BuildDetail();
    }

    /// <summary>The add button.</summary>
    public Button AddButton { get; }

    /// <summary>The button that makes a new trigger set.</summary>
    public Button NewSetButton { get; }

    /// <summary>The set whose own page is shown (its name, who uses it), or null while an item's fields are.</summary>
    public TriggerSet? ShownSet => _shownSet;

    /// <summary>The set page's check box for each character, for a test.</summary>
    public IReadOnlyList<(SetUser User, CheckBox Box)> SetUserBoxes => _userBoxes;

    /// <summary>The set page's name box, or null while no set page is shown.</summary>
    public TextBox? SetNameBox { get; private set; }

    /// <summary>The set page's description box, or null while no set page is shown.</summary>
    public TextBox? SetDescriptionBox { get; private set; }

    /// <summary>The set page's delete button (first press asks), or null while no set page is shown.</summary>
    public Button? SetDeleteButton { get; private set; }

    /// <summary>Shows <paramref name="set"/>'s own page: its name, its description and who uses it.</summary>
    public void ChooseSet(TriggerSet set)
    {
        _chosenSet = set;
        _chosenSetLanded = _form.IsOwnSet(set);
        _confirmingSetDelete = false;
        SelectRow(null);
        BuildDetail();
    }

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

    /// <summary>Each set heading as it reads (<c>Comms, 3 triggers</c>), in list order, for a test.</summary>
    public IReadOnlyList<string> SetHeadings => _headings;

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
        _chosenSet = null;
        Select(item);
        SelectRow(item);
        BuildDetail();
    }

    /// <summary>
    /// Catches up with a change that has landed: rows rebuilt, the detail re-read (or rebuilt for another item). A field
    /// being typed in is left alone unless <paramref name="focusedToo"/> (the change is an undo or redo taken from it).
    /// </summary>
    public void Refresh(bool focusedToo = false)
    {
        _refreshFocused = focusedToo;
        try
        {
            RefreshNow();
        }
        finally
        {
            _refreshFocused = false;
        }
    }

    /// <summary>True while <see cref="Refresh"/> rewrites a focused field too.</summary>
    private bool _refreshFocused;

    /// <summary>Whether a refresh may rewrite <paramref name="control"/>: not while it is being typed in, unless the change is a history step.</summary>
    private bool Rewritable(Control control) => _refreshFocused || !control.IsKeyboardFocusWithin;

    private void RefreshNow()
    {
        if (_chosenSet is { } chosen)
        {
            if (_form.IsOwnSet(chosen))
            {
                _chosenSetLanded = true;
            }
            else if (_chosenSetLanded)
            {
                // The set went (deleted, or an undo took back its making): back to the items.
                _chosenSet = null;
            }
        }


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
        if (!ReferenceEquals(_shownItem, _form.Item) || !ReferenceEquals(_shownSet, _chosenSet) || _chosenSet is not null && _userBoxes.Count != _form.UsersOf(_chosenSet).Count)
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

    private IBrush Hairline => _tokens.Page.Brush(_tokens.Page.Rule);

    /// <summary>A card on the settings page: the card plane, a hairline edge, the card radius.</summary>
    internal static Border PageCard(Control child, PageTokens page, Thickness padding, Thickness margin) => new()
    {
        Child = child,
        Background = page.Brush(page.Card),
        BorderBrush = page.Brush(page.Rule),
        BorderThickness = new Thickness(NativeTokens.Hairline),
        CornerRadius = new CornerRadius(NativeTokens.RadiusCard),
        Padding = padding,
        Margin = margin,
        ClipToBounds = true,
    };

    /// <summary>A quiet line's ink, held to the floor (an opacity would not be).</summary>
    private IBrush DimInk => _tokens.Page.Brush(_tokens.Page.Dim);

    private static Button ToolButton(string text, string tip)
    {
        var button = new Button { Content = text, Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space4), MinWidth = 32, FontSize = NativeTokens.Label, HorizontalContentAlignment = HorizontalAlignment.Center };
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
        _setRows.Clear();
        _copyButtons.Clear();
        _headings.Clear();
        var items = new List<ListBoxItem>();
        var groups = _form.Groups(_section);
        foreach (var group in groups)
        {
            // An empty set's heading already says "no triggers", so nothing is listed under it.
            items.Add(GroupHeader(group));
            foreach (var item in group.Items)
            {
                items.Add(ItemRow(group, item));
            }
        }

        if (groups.Count == 0)
        {
            items.Add(new ListBoxItem
            {
                Content = new TextBlock { Text = $"No trigger set yet. {TriggerSetEditing.Explainer} Make one with + Set.", Foreground = DimInk, TextWrapping = TextWrapping.Wrap },
                IsEnabled = false,
            });
        }

        _choosing = true;
        _list.ItemsSource = items;
        _choosing = false;
        SelectRow(_chosenSet is null ? _form.Item : null);
        UpdateTools();
    }

    /// <summary>
    /// A set's heading over its items: a band on the page's ground across the list, the set's name at the list's
    /// item size in the body ink, and how many items it holds at the right (<see cref="SettingsCollection.CountOf"/>,
    /// the words the cell heading says). A package's set says so under its name and carries <c>Copy to edit</c>.
    /// It was an 11 px all-caps label in the heading ink, quieter than the rows under it.
    /// </summary>
    private ListBoxItem GroupHeader(SettingsItemGroup group)
    {
        var page = _tokens.Page;
        var row = new DockPanel { LastChildFill = true };
        if (group.ReadOnly)
        {
            var copy = new Button { Content = "Copy to edit", Padding = new Thickness(NativeTokens.Space8, NativeTokens.Hairline), FontSize = NativeTokens.Small, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NativeTokens.Space8, 0, 0, 0) };
            ToolTip.SetTip(copy, "A package's set is replaced when the package updates; copy it into your own sets to change it");
            AutomationProperties.SetName(copy, $"Copy {group.Set.Name} to edit");
            var set = group.Set;
            copy.Click += (_, _) => _form.CopyToEdit(set);
            _copyButtons.Add(copy);
            DockPanel.SetDock(copy, Dock.Right);
            row.Children.Add(copy);
        }

        var count = new TextBlock
        {
            Text = _collection.CountOf(group.Items.Count),
            FontSize = NativeTokens.Small,
            Foreground = DimInk,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(NativeTokens.Space8, 0, 0, 0),
        };
        DockPanel.SetDock(count, Dock.Right);
        row.Children.Add(count);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space8 };
        name.Children.Add(new Border
        {
            Width = NativeTokens.Space8,
            Height = NativeTokens.Space8,
            CornerRadius = new CornerRadius(NativeTokens.Hairline),
            Background = _tokens.Brush(_tokens.Accent),
            VerticalAlignment = VerticalAlignment.Center,
        });
        name.Children.Add(new TextBlock { Text = group.Label, FontWeight = FontWeight.SemiBold, FontSize = NativeTokens.Title, Foreground = page.Brush(page.Text), TextTrimming = TextTrimming.CharacterEllipsis });
        words.Children.Add(name);
        if (group.ReadOnly)
        {
            words.Children.Add(new TextBlock { Text = "package · read-only", FontSize = NativeTokens.Small, Foreground = DimInk });
        }
        else
        {
            // Who runs these rules: a set no character uses does nothing, and that is the commonest surprise.
            var usage = _form.UsageOf(group.Set);
            var unused = !_form.IsUsed(group.Set);
            words.Children.Add(new TextBlock
            {
                Text = usage,
                FontSize = NativeTokens.Small,
                Foreground = unused ? page.Brush(page.Text) : DimInk,
                FontWeight = unused ? FontWeight.SemiBold : FontWeight.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        row.Children.Add(words);
        var band = new Border
        {
            Background = page.Brush(page.Ground),
            BorderBrush = page.Brush(page.Rule),
            BorderThickness = new Thickness(NativeTokens.Hairline),
            CornerRadius = new CornerRadius(NativeTokens.RadiusS),
            Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space6),
            Child = row,
        };
        _headings.Add($"{group.Label}, {_collection.CountOf(group.Items.Count)}");
        AutomationProperties.SetName(band, $"Set {_headings[^1]}");
        var header = new ListBoxItem
        {
            Content = band,
            Focusable = !group.ReadOnly,
            IsHitTestVisible = true,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0, NativeTokens.Space12, 0, NativeTokens.Space4),
            Background = Brushes.Transparent,
        };
        if (!group.ReadOnly)
        {
            // A heading of one of the player's own sets opens the set's page: its name, its description, who uses it.
            header.Tag = group.Set;
            ToolTip.SetTip(band, "Rename this set, describe it or choose which characters use it");
            AutomationProperties.SetName(header, $"Set {group.Label}, {_form.UsageOf(group.Set)}");
            _setRows.Add((group.Set, header));
        }

        return header;
    }

    private ListBoxItem ItemRow(SettingsItemGroup group, object item)
    {
        var enabled = new CheckBox
        {
            IsChecked = _form.IsEnabled(_section, item),
            IsEnabled = !group.ReadOnly,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 0,
            Margin = new Thickness(0, 0, NativeTokens.Space4, 0),
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
                FontSize = NativeTokens.Small,
                Opacity = 0.65,
                FontFamily = Mono,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(enabled, Dock.Left);
        row.Children.Add(enabled);
        row.Children.Add(words);
        var listItem = new ListBoxItem { Content = row, Tag = item, Padding = new Thickness(NativeTokens.Space14, NativeTokens.Space4, NativeTokens.Space8, NativeTokens.Space4) };
        AutomationProperties.SetName(listItem, _collection.TitleOf(item));
        if (group.ReadOnly)
        {
            listItem.Opacity = 0.8;
        }

        _rows.Add((item, listItem, enabled));
        return listItem;
    }

    private static FontFamily Mono => NativeTokens.MonoFont;

    private void SelectRow(object? item)
    {
        _choosing = true;
        _list.SelectedItem = _chosenSet is { } set
            ? _setRows.FirstOrDefault(r => ReferenceEquals(r.Set, set)).Row
            : _rows.FirstOrDefault(r => ReferenceEquals(r.Item, item)).Row;
        _choosing = false;
    }

    private void OnRowChosen()
    {
        if (_choosing || _list.SelectedItem is not ListBoxItem { Tag: { } tag })
        {
            return;
        }

        if (tag is TriggerSet set)
        {
            if (!ReferenceEquals(set, _chosenSet))
            {
                ChooseSet(set);
            }

            return;
        }

        if (ReferenceEquals(tag, _form.Item) && _chosenSet is null)
        {
            return;
        }

        _chosenSet = null;
        Select(tag);
        BuildDetail();
    }

    private void OnListKey(object? sender, KeyEventArgs e)
    {
        if (_chosenSet is not null || _form.Item is not { } item)
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
        var set = _chosenSet is { } chosen && _form.IsOwnSet(chosen)
            ? chosen
            : (_form.Item is { } item ? FindGroup(item) : null) is { ReadOnly: false } own
                ? own.Set
                : groups.FirstOrDefault(g => !g.ReadOnly)?.Set;
        if (set is not null)
        {
            _chosenSet = null;
            _form.Add(_section, set);
        }
    }

    /// <summary>Makes a new set and shows its page, so it can be named and given its characters at once.</summary>
    private void NewSet()
    {
        _chosenSet = _form.NewSet();
        _chosenSetLanded = _form.IsOwnSet(_chosenSet);
        _confirmingSetDelete = false;
        BuildList();
        BuildDetail();
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
        var editable = group is { ReadOnly: false } && _chosenSet is null;
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
        _shownSet = _chosenSet;
        _userBoxes.Clear();
        SetNameBox = null;
        SetDescriptionBox = null;
        SetDeleteButton = null;
        UpdateTools();

        if (_chosenSet is { } chosenSet)
        {
            ShownFields = [];
            BuildSetDetail(chosenSet);
            return;
        }

        if (_form.Item is not { } item || FindGroup(item) is not { } group)
        {
            ShownFields = [];
            _detail.Children.Add(new TextBlock
            {
                Text = _form.Groups(_section).Any(g => !g.ReadOnly)
                    ? $"No {_collection.Noun} selected. Add one with + Add."
                    : $"There is no trigger set to hold {_collection.Plural} yet. Make one with + Set.",
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        var title = _collection.TitleOf(item);
        _detail.Children.Add(new TextBlock { Text = title.Length == 0 ? "(unnamed)" : NativeKeys.Words(title), FontSize = NativeTokens.PageTitle - 2, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (group.ReadOnly)
        {
            var banner = new DockPanel { LastChildFill = true, Margin = new Thickness(0, NativeTokens.Space6, 0, NativeTokens.Space4) };
            var copy = new Button { Content = "Copy to edit", Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space4) };
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
                Margin = new Thickness(0, 0, NativeTokens.Space10, 0),
            });
            _detail.Children.Add(banner);
        }
        else
        {
            // Where the item lives and who runs it, with the way to the set's page: an item in a set no character uses
            // does nothing, and this line is where that is said.
            var owner = group.Set;
            var line = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, NativeTokens.Space4) };
            line.Children.Add(new TextBlock { Text = $"in {group.Label} · {_form.UsageOf(owner)}", Foreground = DimInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, NativeTokens.Space8, 0) });
            var open = new Button
            {
                Content = "Set…",
                Padding = new Thickness(NativeTokens.Space8, NativeTokens.Hairline),
                FontSize = NativeTokens.Small,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(open, $"Rename {owner.Name}, or choose which characters use it");
            AutomationProperties.SetName(open, $"Open set {owner.Name}");
            open.Click += (_, _) => ChooseSet(owner);
            line.Children.Add(open);
            _detail.Children.Add(line);
            if (!_form.IsUsed(owner))
            {
                _detail.Children.Add(new TextBlock
                {
                    Text = $"No character uses {owner.Name}, so this {_collection.Noun} does nothing yet. Open the set to choose who uses it.",
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 0, 0, NativeTokens.Space4),
                });
            }
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
                _detail.Children.Add(new TextBlock { Text = g.ToUpperInvariant(), FontSize = NativeTokens.Small, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = _tokens.Brush(_tokens.Header), Margin = new Thickness(0, NativeTokens.Space16, 0, NativeTokens.Space4) });
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
                    BorderThickness = new Thickness(NativeTokens.Hairline),
                    BorderBrush = Hairline,
                    CornerRadius = new CornerRadius(NativeTokens.RadiusM),
                    Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space6),
                    Margin = new Thickness(0, NativeTokens.Space2, 0, NativeTokens.Space6),
                });
                ShowPreview(null);
            }
        }

        ShownFields = shown;
    }

    /// <summary>
    /// A set's own page: its name and description (committed on Enter or leaving the box), what it holds, a check box
    /// per character for who uses it, and Delete, which asks once first.
    /// </summary>
    private void BuildSetDetail(TriggerSet set)
    {
        _detail.Children.Add(new TextBlock { Text = set.Name, FontSize = NativeTokens.PageTitle - 2, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        _detail.Children.Add(new TextBlock { Text = $"Trigger set · {TriggerSetEditing.Holds(set)}", Foreground = DimInk, Margin = new Thickness(0, 0, 0, NativeTokens.Space4) });
        _detail.Children.Add(new TextBlock { Text = TriggerSetEditing.Explainer, Foreground = DimInk, TextWrapping = TextWrapping.Wrap, FontSize = NativeTokens.PageSmall, Margin = new Thickness(0, 0, 0, NativeTokens.Space8) });

        var name = new TextBox { Text = set.Name };
        AutomationProperties.SetName(name, "Set name");
        void CommitName()
        {
            if (_syncing || name.Text == set.Name)
            {
                return;
            }

            if (_form.RenameSet(set, name.Text ?? string.Empty) is { } refused)
            {
                name.Text = set.Name;
                ToolTip.SetTip(name, refused);
                ToolTip.SetIsOpen(name, true);
            }
        }

        name.LostFocus += (_, _) => CommitName();
        name.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitName();
                e.Handled = true;
            }
        };
        _refreshers.Add(() =>
        {
            if (Rewritable(name))
            {
                name.Text = set.Name;
            }
        });
        SetNameBox = name;
        _detail.Children.Add(LabelledRow("Name", "What characters use it by; renaming it keeps every character using it.", name));

        var description = new TextBox { Text = set.Description ?? string.Empty, PlaceholderText = "What these rules are for" };
        AutomationProperties.SetName(description, "Set description");
        void CommitDescription()
        {
            if (!_syncing && (description.Text ?? string.Empty) != (set.Description ?? string.Empty))
            {
                _form.DescribeSet(set, description.Text);
            }
        }

        description.LostFocus += (_, _) => CommitDescription();
        description.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitDescription();
                e.Handled = true;
            }
        };
        _refreshers.Add(() =>
        {
            if (Rewritable(description))
            {
                description.Text = set.Description ?? string.Empty;
            }
        });
        SetDescriptionBox = description;
        _detail.Children.Add(LabelledRow("Description", null, description));

        _detail.Children.Add(new TextBlock { Text = "USED BY", FontSize = NativeTokens.Small, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = _tokens.Brush(_tokens.Header), Margin = new Thickness(0, NativeTokens.Space16, 0, NativeTokens.Space4) });
        var users = _form.UsersOf(set);
        if (users.Count == 0)
        {
            _detail.Children.Add(new TextBlock { Text = "There are no characters yet. Add one on Worlds & Characters, then tick it here.", Foreground = DimInk, TextWrapping = TextWrapping.Wrap });
        }

        foreach (var user in users)
        {
            var box = new CheckBox { Content = user.Name, IsChecked = user.Uses, Margin = new Thickness(0, NativeTokens.Space2) };
            AutomationProperties.SetName(box, $"{user.Name} uses {set.Name}");
            var character = user.Character;
            box.IsCheckedChanged += (_, _) =>
            {
                if (!_syncing && box.IsChecked is { } on && on != TriggerSetEditing.Uses(character, set.Name))
                {
                    _form.Assign(character, set, on);
                }
            };
            _refreshers.Add(() => box.IsChecked = TriggerSetEditing.Uses(character, set.Name));
            _userBoxes.Add((user, box));
            _detail.Children.Add(box);
        }

        var delete = new Button { Content = "Delete set…", Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space4), Margin = new Thickness(0, NativeTokens.Space16, NativeTokens.Space8, 0) };
        AutomationProperties.SetName(delete, $"Delete set {set.Name}");
        var question = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, NativeTokens.Space16, 0, 0) };
        delete.Click += (_, _) =>
        {
            if (!_confirmingSetDelete)
            {
                _confirmingSetDelete = true;
                question.Text = $"Delete {_form.SetDeletion(set)}? Press again to delete.";
                question.IsVisible = true;
                delete.Content = "Delete";
                return;
            }

            _confirmingSetDelete = false;
            _chosenSet = null;
            _form.DeleteSet(set);
        };
        SetDeleteButton = delete;
        var foot = new WrapPanel { Orientation = Orientation.Horizontal };
        foot.Children.Add(delete);
        foot.Children.Add(question);
        _detail.Children.Add(foot);
    }

    /// <summary>A set page's row: its label (with the help as a tooltip) and the box.</summary>
    private static Grid LabelledRow(string label, string? help, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("118,*"), Margin = new Thickness(0, NativeTokens.Space4), MinHeight = 34 };
        var words = new TextBlock { Text = label, FontSize = NativeTokens.Label, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        if (help is not null)
        {
            ToolTip.SetTip(words, help);
            ToolTip.SetTip(control, help);
        }

        grid.Children.Add(words);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static Grid Row(SettingsField field, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("118,*"), Margin = new Thickness(0, NativeTokens.Space4), MinHeight = 34 };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, NativeTokens.Space12, 0) };
        words.Children.Add(new TextBlock { Text = NativeKeys.Words(Capitalise(field.Label)), FontSize = NativeTokens.Label, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
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

        var summary = new TextBlock { FontSize = NativeTokens.Body, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
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
            var line = new TextBlock { FontFamily = Mono, FontSize = NativeTokens.Body, TextTrimming = TextTrimming.CharacterEllipsis };
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
                // Stretched as the text boxes are, so every control in the column shares both edges: a combo capped at 280 px was
                // centred in a wider cell, 25 px in from the Name box above it.
                var combo = new ComboBox { ItemsSource = _form.Choices(choice), SelectedItem = _form.Get(named), HorizontalAlignment = HorizontalAlignment.Stretch };
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
                    if (Rewritable(box))
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
                    HorizontalAlignment = HorizontalAlignment.Stretch,
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
                    var box = new CheckBox { Content = name, Margin = new Thickness(0, 0, NativeTokens.Space14, 0), IsChecked = FlagWords.IsListed(_form.Get(words), name) };
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
                return new TextBlock { Text = "set on its own screen", Foreground = DimInk };
        }
    }

    private Control TextControl(TextField text)
    {
        var suggestions = _form.Suggestions(text);
        TextBox? box = null;
        ComboBox? auto = null;
        if (suggestions.Count > 0)
        {
            // An editable combo box: the cell screen offers these as a list ((none), main, the windows), so the reserved
            // words have to be visible here too, not only reachable by typing; anything else can still be typed.
            auto = new ComboBox
            {
                IsEditable = true,
                ItemsSource = suggestions,
                Text = _form.Text(text),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            AutomationProperties.SetName(auto, text.Label);
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
        if (auto is not null)
        {
            // A pick from the list commits as ⏎ would; typing commits on ⏎ or leaving the box.
            auto.SelectionChanged += (_, _) =>
            {
                if (!_syncing && auto.SelectedItem is string picked)
                {
                    auto.Text = picked;
                    Commit();
                }
            };
        }

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
            if (Rewritable(control))
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
        var note = new TextBlock { FontSize = NativeTokens.Small, Foreground = DimInk, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, NativeTokens.Space2, 0, 0) };
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
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space8, Tag = name };
        var hex = HighlightColours.TryParse(name, out var colour) ? HighlightColours.Hex(colour) : null;
        panel.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(NativeTokens.RadiusS),
            Background = hex is null ? Brushes.Transparent : new SolidColorBrush(Color.Parse(hex)),
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(hex is null ? NativeTokens.Hairline : 0),
        });
        panel.Children.Add(new TextBlock { Text = name });
        return panel;
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
