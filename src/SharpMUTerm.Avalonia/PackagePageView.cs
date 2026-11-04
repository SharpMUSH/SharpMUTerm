using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Packages;
using SharpMUTerm.Client.Settings;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// F10 in the settings window: a package manager drawn from F10's declaration (<see cref="PackageCollection"/>) and the
/// client's <see cref="PackageManager"/>. <b>Installed</b> lists the packages with an on/off switch each and the selected
/// one's declared fields beside it, with <i>Copy sets to edit</i> and <i>Remove</i>; <b>Browse</b> lists the package index,
/// the focused window's world's packages first — fetched only when the player opens it; and an install opens the
/// review over the page, drawn from <see cref="PackageReviewModel"/>, the model the cell review draws.
/// <para>
/// Nothing here installs, removes or switches anything: every gesture goes to the manager, which hands it to the client
/// on the client's thread, and <see cref="Refresh"/> catches up once the client has published what happened.
/// </para>
/// </summary>
internal sealed class PackagePageView : Grid
{
    private readonly PackageManager _manager;
    private readonly PackageCollection _declaration;
    private readonly Func<SettingsField, bool> _visible;
    private readonly IBrush _accent;
    private readonly bool _dark;
    private readonly TabControl _tabs;
    private readonly ListBox _installed = new() { Background = Brushes.Transparent };
    private readonly StackPanel _detail = new() { Spacing = 2, Margin = new Thickness(4, 0, 4, 16) };
    private readonly ListBox _browse = new() { Background = Brushes.Transparent };
    private readonly StackPanel _browseDetail = new() { Spacing = 6, Margin = new Thickness(4, 0, 4, 16) };
    private readonly ContentControl _browseBody = new();
    private readonly Border _reviewLayer;
    private readonly TextBlock _status = new() { Opacity = 0.8, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Dictionary<string, TextBlock> _values = new(StringComparer.Ordinal);
    private readonly List<(PackageItem Item, ToggleSwitch Switch)> _rows = [];
    private string? _selected;
    private int _browseSelected;
    private bool _confirmRemove;
    private bool _syncing;
    private PackageBrowseModel? _shownBrowse;
    private bool _shownFetching;
    private PackageReviewModel? _shownReview;

    public PackagePageView(PackageManager manager, Func<SettingsField, bool> visible, IBrush accent, bool dark)
    {
        _manager = manager;
        _declaration = manager.Declaration;
        _visible = visible;
        _accent = accent;
        _dark = dark;
        RowDefinitions = new RowDefinitions("*,Auto");
        Margin = new Thickness(0, 6, 0, 0);

        _installed.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _installed.SelectedItem is ListBoxItem { Tag: string id })
            {
                _selected = id;
                _confirmRemove = false;
                BuildDetail();
            }
        };
        AutomationProperties.SetName(_installed, "Installed packages");
        _browse.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _browse.SelectedIndex >= 0)
            {
                _browseSelected = _browse.SelectedIndex;
                BuildBrowseDetail();
            }
        };
        AutomationProperties.SetName(_browse, "The package index");

        InstalledTab = new TabItem { Header = "Installed", Content = MasterDetail(_installed, _detail), FontSize = 17 };
        BrowseTab = new TabItem { Header = "Browse", Content = _browseBody, FontSize = 17 };
        foreach (var tab in new[] { InstalledTab, BrowseTab })
        {
            // The header's size only: the page under it keeps the window's own.
            if (tab.Content is Control content)
            {
                content.SetValue(global::Avalonia.Controls.Documents.TextElement.FontSizeProperty, 14d);
            }
        }
        _tabs = new TabControl { ItemsSource = new[] { InstalledTab, BrowseTab } };
        _tabs.SelectionChanged += (_, _) =>
        {
            if (!_syncing && ReferenceEquals(_tabs.SelectedItem, BrowseTab))
            {
                BuildBrowse();
            }
        };
        Children.Add(_tabs);

        SetRow(_status, 1);
        Children.Add(_status);

        _reviewLayer = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            IsVisible = false,
        };
        SetRowSpan(_reviewLayer, 2);
        Children.Add(_reviewLayer);

        Refresh();
    }

    /// <summary>The Installed tab.</summary>
    public TabItem InstalledTab { get; }

    /// <summary>The Browse tab.</summary>
    public TabItem BrowseTab { get; }

    /// <summary>Whether the Browse tab is in front.</summary>
    public bool Browsing => ReferenceEquals(_tabs.SelectedItem, BrowseTab);

    /// <summary>The ids of the fields drawn — the switch, the row's fields, then the detail's — for a test.</summary>
    public IReadOnlyList<string> ShownFields { get; private set; } = [];

    /// <summary>The installed packages listed, each with its switch, for a test.</summary>
    public IReadOnlyList<(PackageItem Item, ToggleSwitch Switch)> Rows => _rows;

    /// <summary>What each detail field says now, by id, for a test.</summary>
    public IReadOnlyDictionary<string, string> Values => _values.ToDictionary(v => v.Key, v => v.Value.Text ?? string.Empty, StringComparer.Ordinal);

    /// <summary>The remove button of the selected package, for a test.</summary>
    public Button? RemoveButton { get; private set; }

    /// <summary>The copy-its-sets button of the selected package, for a test.</summary>
    public Button? CopyButton { get; private set; }

    /// <summary>The button that fetches the index, while it has not been fetched, for a test.</summary>
    public Button? FetchButton { get; private set; }

    /// <summary>The selected index entry's install button, for a test.</summary>
    public Button? InstallButton { get; private set; }

    /// <summary>The review on show, for a test; null when none is.</summary>
    public PackageReviewModel? ShownReview => _shownReview;

    /// <summary>The review's lines as drawn, in order (headings, then each row's label and value), for a test.</summary>
    public IReadOnlyList<string> ReviewLines { get; private set; } = [];

    /// <summary>The review's install button, for a test.</summary>
    public Button? AcceptButton { get; private set; }

    /// <summary>The review's cancel button, for a test.</summary>
    public Button? CancelButton { get; private set; }

    /// <summary>Catches up with what the client published: the installed list, the index, the review, the status.</summary>
    public void Refresh()
    {
        BuildInstalled();
        if (_manager.BrowseRequests != _manager.BrowseRequestsSeen)
        {
            // The player asked for the index (here, or ⌃P ▸ Browse packages): the list it arrives in comes forward.
            _manager.BrowseRequestsSeen = _manager.BrowseRequests;
            _syncing = true;
            _tabs.SelectedItem = BrowseTab;
            _syncing = false;
            BuildBrowse();
        }
        else if (!ReferenceEquals(_manager.BrowseModel, _shownBrowse) || _manager.Fetching != _shownFetching)
        {
            BuildBrowse();
        }

        if (!ReferenceEquals(_manager.Review, _shownReview))
        {
            BuildReview();
        }

        _status.Text = NativeKeys.Words(_manager.Status);
        _status.IsVisible = _manager.Status is not null;
    }

    // ── installed ────────────────────────────────────────────────────────────────────────────────

    private Grid MasterDetail(Control list, Control detail)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*"), Margin = new Thickness(0, 10, 0, 0) };
        grid.Children.Add(new Border
        {
            Child = list,
            BorderThickness = new Thickness(1),
            BorderBrush = Hairline,
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 14, 0),
        });
        var scroller = new ScrollViewer { Content = detail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        SetColumn(scroller, 1);
        grid.Children.Add(scroller);
        return grid;
    }

    private IBrush Hairline => new SolidColorBrush(_dark ? Color.Parse("#46464e") : Color.Parse("#c8c8d0"));

    private IBrush Warning => new SolidColorBrush(_dark ? Color.Parse("#ffb454") : Color.Parse("#a4410e"));

    private void BuildInstalled()
    {
        var items = _manager.Items;
        _syncing = true;
        try
        {
            _rows.Clear();
            var rows = new List<ListBoxItem>();
            foreach (var item in items)
            {
                var toggle = new ToggleSwitch
                {
                    IsChecked = _manager.IsEnabled(item),
                    OnContent = null,
                    OffContent = null,
                    MinWidth = 0,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                AutomationProperties.SetName(toggle, $"{item.Package.Manifest.Name} on");
                ToolTip.SetTip(toggle, NativeKeys.Words(_declaration.Action(PackageCollection.Toggle).Help));
                var captured = item;
                toggle.IsCheckedChanged += (_, _) =>
                {
                    if (!_syncing && toggle.IsChecked is bool on)
                    {
                        _manager.SetEnabled(captured, on);
                    }
                };
                _rows.Add((item, toggle));

                var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(new TextBlock { Text = Text("name", item), FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
                var tail = string.Join(" · ", _declaration.RowFields.Skip(1).Select(id => Text(id, item)));
                words.Children.Add(new TextBlock { Text = tail, FontSize = 12, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis });
                if (item.Update is { } newer)
                {
                    words.Children.Add(new TextBlock { Text = $"update {newer} available", FontSize = 12, Foreground = _accent });
                }

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                row.Children.Add(toggle);
                SetColumn(words, 1);
                words.Margin = new Thickness(6, 0, 0, 0);
                row.Children.Add(words);
                rows.Add(new ListBoxItem { Content = row, Tag = item.Id, Padding = new Thickness(8, 6) });
            }

            _installed.ItemsSource = rows;
            if (items.Count > 0 && (_selected is null || items.All(i => i.Id != _selected)))
            {
                _selected = items[0].Id;
            }

            _installed.SelectedItem = rows.FirstOrDefault(r => Equals(r.Tag, _selected));
        }
        finally
        {
            _syncing = false;
        }

        BuildDetail();
    }

    private string Text(string id, PackageItem item) =>
        _declaration.Field(id) is InfoField info ? _manager.Text(info, item) : string.Empty;

    private void BuildDetail()
    {
        _detail.Children.Clear();
        _values.Clear();
        RemoveButton = null;
        CopyButton = null;
        var item = _manager.Items.FirstOrDefault(i => i.Id == _selected);
        if (item is null)
        {
            ShownFields = [];
            _detail.Children.Add(new TextBlock
            {
                Text = "No package is installed. Browse the index, or use Ctrl+P ▸ Install package… with a file or an https link.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Margin = new Thickness(0, 8, 0, 8),
            });
            _detail.Children.Add(BrowseDoor());
            return;
        }

        var shown = new List<string> { _declaration.EnabledField };
        shown.AddRange(_declaration.RowFields);
        _detail.Children.Add(new TextBlock { Text = Text("name", item), FontSize = 20, FontWeight = FontWeight.SemiBold });
        var subtitle = string.Join(" · ", _declaration.RowFields.Skip(1).Select(id => Text(id, item)));
        _detail.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 6) });

        string? group = null;
        foreach (var field in _declaration.DetailFields.Where(_visible))
        {
            if (field.Group is { } heading && heading != group)
            {
                group = heading;
                _detail.Children.Add(new TextBlock
                {
                    Text = heading,
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(0, 14, 0, 4),
                    Foreground = _accent,
                });
            }

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), Margin = new Thickness(0, 3) };
            var label = new TextBlock { Text = Capitalise(field.Label), Opacity = 0.75 };
            if (field.Help is { } help)
            {
                ToolTip.SetTip(label, NativeKeys.Words(Capitalise(help)));
            }

            var value = new TextBlock { Text = Text(field.Id, item), TextWrapping = TextWrapping.Wrap };
            if (field.Id == "update" && item.Update is not null)
            {
                value.Foreground = _accent;
            }

            AutomationProperties.SetName(value, field.Label);
            _values[field.Id] = value;
            row.Children.Add(label);
            SetColumn(value, 1);
            row.Children.Add(value);
            _detail.Children.Add(row);
            shown.Add(field.Id);
        }

        ShownFields = shown;

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
        var copy = ActionButton(PackageCollection.CopySets, "Copy sets to edit");
        copy.IsEnabled = item.Package.Sets.Count > 0;
        copy.Click += (_, _) => _manager.CopySets(item);
        CopyButton = copy;
        actions.Children.Add(copy);

        var remove = ActionButton(PackageCollection.Remove, _confirmRemove ? $"Remove {item.Package.Manifest.Name} and its files" : "Remove…");
        if (_confirmRemove)
        {
            remove.Foreground = Warning;
        }

        remove.Click += (_, _) =>
        {
            if (!_confirmRemove)
            {
                // The cell screen's Del, Del: the first press asks.
                _confirmRemove = true;
                BuildDetail();
                return;
            }

            _confirmRemove = false;
            _manager.Remove(item);
        };
        RemoveButton = remove;
        actions.Children.Add(remove);
        actions.Children.Add(BrowseDoor());
        _detail.Children.Add(actions);
        if (_confirmRemove)
        {
            _detail.Children.Add(new TextBlock
            {
                Text = "Click again to remove it; its sets stop applying and its folder is deleted.",
                Foreground = Warning,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private Button BrowseDoor()
    {
        var browse = ActionButton(PackageCollection.Browse, "Browse the index");
        browse.Click += (_, _) =>
        {
            _tabs.SelectedItem = BrowseTab;
        };
        return browse;
    }

    private Button ActionButton(string action, string text)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(12, 5) };
        var declared = _declaration.Action(action);
        ToolTip.SetTip(button, NativeKeys.Words($"{Capitalise(declared.Help)} ({declared.Key} on the cell screen)"));
        AutomationProperties.SetName(button, text);
        return button;
    }

    // ── browse ───────────────────────────────────────────────────────────────────────────────────

    private void BuildBrowse()
    {
        _shownBrowse = _manager.BrowseModel;
        _shownFetching = _manager.Fetching;
        FetchButton = null;
        InstallButton = null;
        if (_manager.BrowseModel is not { } model)
        {
            var empty = new StackPanel { Spacing = 10, Margin = new Thickness(4, 24, 4, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
            empty.Children.Add(new TextBlock { Text = "The package index", FontSize = 18, FontWeight = FontWeight.SemiBold });
            empty.Children.Add(new TextBlock
            {
                Text = "Rule sets, scripts and sounds other players have shared, this game's first. The index is fetched only when you ask, over https; every package is checked against it before you see its review.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
            });
            if (_manager.Fetching)
            {
                empty.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children = { new ProgressBar { IsIndeterminate = true, Width = 120 }, new TextBlock { Text = "Fetching the index…", VerticalAlignment = VerticalAlignment.Center } },
                });
            }
            else
            {
                var fetch = new Button { Content = "Fetch the index", Padding = new Thickness(14, 6), Classes = { "accent" } };
                AutomationProperties.SetName(fetch, "Fetch the package index");
                fetch.Click += (_, _) => _manager.Browse();
                FetchButton = fetch;
                empty.Children.Add(fetch);
            }

            _browseBody.Content = empty;
            return;
        }

        var head = new StackPanel { Spacing = 2, Margin = new Thickness(0, 10, 0, 0) };
        head.Children.Add(new TextBlock
        {
            Text = PackageCollection.Of(model.Rows.Count, "package") + (model.World is { } world ? $" · {world}'s first" : string.Empty),
            Opacity = 0.75,
        });
        if (model.Dropped > 0)
        {
            head.Children.Add(new TextBlock
            {
                Text = model.Dropped == 1 ? "1 entry in the index breaks its rules and is not listed" : $"{model.Dropped.ToString(CultureInfo.InvariantCulture)} entries in the index break its rules and are not listed",
                Foreground = Warning,
            });
        }

        _syncing = true;
        var rows = new List<ListBoxItem>();
        foreach (var row in model.Rows)
        {
            var words = new StackPanel { Spacing = 1 };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            title.Children.Add(new TextBlock { Text = row.Entry.Name, FontSize = 14 });
            if (row.ForWorld)
            {
                title.Children.Add(Chip(model.World ?? "this game", _accent));
            }

            if (row.State != PackageBrowseState.New)
            {
                title.Children.Add(Chip(row.State == PackageBrowseState.Update ? "update" : "installed", row.State == PackageBrowseState.Update ? _accent : Hairline));
            }

            words.Children.Add(title);
            words.Children.Add(new TextBlock
            {
                Text = $"{row.Entry.Version} · {string.Join(", ", row.Entry.Games)}" + (row.StateText.Length == 0 ? string.Empty : $" · {row.StateText}"),
                FontSize = 12,
                Opacity = 0.7,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            rows.Add(new ListBoxItem { Content = words, Padding = new Thickness(8, 6) });
        }

        _browse.ItemsSource = rows;
        _browseSelected = Math.Clamp(_browseSelected, 0, Math.Max(0, rows.Count - 1));
        _browse.SelectedIndex = rows.Count == 0 ? -1 : _browseSelected;
        _syncing = false;

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(MasterDetail(_browse, _browseDetail));
        _browseBody.Content = dock;
        BuildBrowseDetail();
    }

    private Border Chip(string text, IBrush brush) => new()
    {
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = brush },
        BorderBrush = brush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(6, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private void BuildBrowseDetail()
    {
        _browseDetail.Children.Clear();
        InstallButton = null;
        if (_manager.BrowseModel is not { Rows.Count: > 0 } model)
        {
            _browseDetail.Children.Add(new TextBlock { Text = "The index lists nothing.", Opacity = 0.75 });
            return;
        }

        var row = model.Rows[Math.Clamp(_browseSelected, 0, model.Rows.Count - 1)];
        var entry = row.Entry;
        _browseDetail.Children.Add(new TextBlock { Text = entry.Name, FontSize = 20, FontWeight = FontWeight.SemiBold });
        _browseDetail.Children.Add(new TextBlock
        {
            Text = entry.Version + (entry.Author is { } author ? $" · by {author}" : string.Empty),
            Opacity = 0.7,
        });
        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            _browseDetail.Children.Add(new TextBlock { Text = entry.Description, TextWrapping = TextWrapping.Wrap });
        }

        _browseDetail.Children.Add(Pair("Games", string.Join(", ", entry.Games)));
        _browseDetail.Children.Add(Pair("From", entry.Url));
        if (row.InstalledVersion is { } installed)
        {
            _browseDetail.Children.Add(Pair("Installed", installed));
        }

        var install = new Button
        {
            Content = row.State switch
            {
                PackageBrowseState.Update => $"Review update to {entry.Version}…",
                PackageBrowseState.Installed => "Review and reinstall…",
                _ => "Review and install…",
            },
            Padding = new Thickness(14, 6),
            Margin = new Thickness(0, 12, 0, 0),
            Classes = { "accent" },
        };
        ToolTip.SetTip(install, "Downloads it, checks it against the index's SHA-256, and shows what it adds — nothing is installed until you agree");
        install.Click += (_, _) => _manager.Install(row);
        InstallButton = install;
        _browseDetail.Children.Add(install);
    }

    private static Grid Pair(string label, string value)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*") };
        grid.Children.Add(new TextBlock { Text = label, Opacity = 0.75 });
        var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    // ── review ───────────────────────────────────────────────────────────────────────────────────

    private void BuildReview()
    {
        _shownReview = _manager.Review;
        AcceptButton = null;
        CancelButton = null;
        if (_manager.Review is not { } review)
        {
            _reviewLayer.IsVisible = false;
            _reviewLayer.Child = null;
            ReviewLines = [];
            return;
        }

        var lines = new List<string>();
        var header = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new TextBlock { Text = review.Title, FontSize = 13, Opacity = 0.7 });
        header.Children.Add(new TextBlock
        {
            Text = $"{review.Name} {review.Version}",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        });
        var byline = string.Join(" · ", new[]
        {
            review.Author is { } author ? $"by {author}" : null,
            review.Replaces is { } replaces ? $"replaces version {replaces}, and its sets with it" : null,
        }.Where(t => t is not null));
        if (byline.Length > 0)
        {
            header.Children.Add(new TextBlock { Text = byline, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
        }

        var card = new StackPanel { Spacing = 4 };
        if (review.Description is { } description)
        {
            card.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        }

        foreach (var section in review.Sections)
        {
            lines.Add(section.Heading);
            var acts = section.Heading == PackageReviewModel.ActsHeading;
            var box = new StackPanel { Spacing = 3 };
            box.Children.Add(new TextBlock
            {
                Text = Capitalise(section.Heading.ToLowerInvariant()),
                FontWeight = FontWeight.SemiBold,
                FontSize = 15,
                Foreground = acts && review.Acts ? Warning : _accent,
                Margin = new Thickness(0, 0, 0, 2),
            });
            foreach (var row in section.Rows)
            {
                lines.Add(row.Label.Length == 0 ? row.Value : $"{row.Label}: {row.Value}");
                var ink = row.Tone switch
                {
                    PackageReviewTone.Warning => Warning,
                    _ => null,
                };
                var value = new TextBlock
                {
                    Text = row.Value,
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = row.Tone == PackageReviewTone.Warning ? FontWeight.SemiBold : FontWeight.Normal,
                    Opacity = row.Tone == PackageReviewTone.Quiet ? 0.7 : 1,
                };
                if (ink is not null)
                {
                    value.Foreground = ink;
                }

                if (row.Label.Length == 0)
                {
                    box.Children.Add(value);
                    continue;
                }

                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*") };
                grid.Children.Add(new TextBlock { Text = Capitalise(row.Label), Opacity = 0.75 });
                SetColumn(value, 1);
                grid.Children.Add(value);
                box.Children.Add(grid);
            }

            card.Children.Add(new Border
            {
                Child = box,
                Padding = new Thickness(12, 10),
                Margin = new Thickness(0, 8, 0, 0),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(acts && review.Acts ? 1 : 0),
                BorderBrush = Warning,
                Background = new SolidColorBrush(_dark ? Color.Parse("#2a2a30") : Color.Parse("#f3f3f6")),
            });
        }

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var consent = new TextBlock
        {
            Text = Capitalise(PackageReviewModel.Consent) + ".",
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        var accept = new Button { Content = review.Replaces is null ? "Install" : "Update", Padding = new Thickness(18, 6), Classes = { "accent" } };
        AutomationProperties.SetName(accept, "Install the package");
        accept.Click += (_, _) => _manager.Accept();
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(18, 6) };
        cancel.Click += (_, _) => _manager.Cancel();
        AcceptButton = accept;
        CancelButton = cancel;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(12, 0, 0, 0),
            Children = { cancel, accept },
        };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(consent);

        // The title and the buttons stay put; only the facts between them scroll, so Install is always in reach.
        var layout = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(footer);
        layout.Children.Add(new ScrollViewer { Content = card, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        ReviewLines = lines;
        _reviewLayer.Child = new Border
        {
            Child = layout,
            Background = new SolidColorBrush(_dark ? Color.Parse("#202024") : Colors.White),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(22, 16),
            MaxWidth = 620,
            Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BoxShadow = BoxShadows.Parse("0 8 32 0 #66000000"),
        };
        _reviewLayer.IsVisible = true;
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
