using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The desktop's settings window, generated from the declared sections (<see cref="SettingsForm"/>): a search box,
/// the sections down the left with match counts while searching, and the chosen section's fields on the right under
/// their group headings — each with its label, its help, the native control for its kind, a dot while it differs
/// from its default and a reset beside it. The meters are a table with a live <c>now</c> column and the paths this
/// connection has received to pick from.
/// <para>
/// Nothing here writes the configuration: every change goes to the form, which commits it through the client's own
/// save funnel on the client's thread. Changes apply as they are made; there is no OK and no Cancel, as there is
/// none on a cell screen. A section that declares no fields is listed and opens its own cell screen.
/// </para>
/// </summary>
internal sealed class SettingsWindow : Window
{
    private readonly TextBox _search;
    private readonly ListBox _sections;
    private readonly StackPanel _page;
    private readonly ScrollViewer _scroller;
    private readonly List<Action> _refreshers = [];
    private readonly DispatcherTimer _clock;
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Button> _resets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _notes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TextBlock> _badges = new(StringComparer.Ordinal);
    private readonly List<(StatusMeter Meter, TextBlock Now)> _meterNow = [];
    private readonly List<ThemeCardView> _themeCards = [];
    private readonly Border _frame;
    private readonly TextBlock _title;
    private SettingsForm _form;
    private NativeTokens _tokens;
    private IReadOnlyList<SettingsMatch> _matches = [];
    private string? _shown;
    private bool _syncing;
    private bool _choosing;
    private int _meterRows = -1;

    /// <summary>The size the window first opens at, before the player resizes it.</summary>
    internal const double DefaultWidth = 960, DefaultHeight = 680;

    public SettingsWindow(SettingsForm form)
    {
        _form = form;
        _tokens = TokensFor(form);
        Title = "Settings";
        Width = DefaultWidth;
        Height = DefaultHeight;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // The palette's frame, not the platform's: the window draws its own title, close button and edge, so it reads
        // as one of the client's surfaces (and looks the same under every window manager, or none).
        WindowDecorations = WindowDecorations.None;
        Styles.Add(new FluentTheme());

        _title = new TextBlock
        {
            Text = "Settings",
            FontSize = NativeTokens.Title,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        CloseButton = new Button
        {
            Content = "✕",
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(CloseButton, "Close settings");
        ToolTip.SetTip(CloseButton, "Close (Esc)");
        CloseButton.Click += (_, _) => Close();
        var bar = new DockPanel { Height = 40, LastChildFill = true, Background = Brushes.Transparent };
        DockPanel.SetDock(CloseButton, Dock.Right);
        bar.Children.Add(CloseButton);
        bar.Children.Add(_title);
        bar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed && !ReferenceEquals(e.Source, CloseButton))
            {
                BeginMoveDrag(e);
            }
        };
        DockPanel.SetDock(bar, Dock.Top);

        _search = new TextBox
        {
            PlaceholderText = "Search settings",
            Margin = new Thickness(16, 4, 16, 10),
            FontSize = NativeTokens.Title,
            Padding = new Thickness(12, 7),
        };
        AutomationProperties.SetName(_search, "Search settings");
        _search.TextChanged += (_, _) => OnQuery();
        DockPanel.SetDock(_search, Dock.Top);

        _sections = new ListBox { Width = 250, Margin = new Thickness(8, 0, 0, 12), Background = Brushes.Transparent };
        _sections.SelectionChanged += (_, _) => OnSectionChosen();
        DockPanel.SetDock(_sections, Dock.Left);

        _page = new StackPanel { Margin = new Thickness(28, 4, 28, 28), Spacing = 2 };
        _scroller = new ScrollViewer { Content = _page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        // A list editor fills the page below its title rather than scrolling with it: its list and its detail scroll
        // on their own, so the buttons over the list stay in reach.
        _scroller.SizeChanged += (_, _) =>
        {
            if (ListEditor is { } editor)
            {
                editor.Height = ListEditorHeight();
            }

            if (PackagePage is { } packages)
            {
                packages.Height = ListEditorHeight();
            }
        };

        var dock = new DockPanel { LastChildFill = true };
        dock.Children.Add(bar);
        dock.Children.Add(_search);
        dock.Children.Add(_sections);
        dock.Children.Add(_scroller);
        _frame = new Border { BorderThickness = new Thickness(1), Child = dock };

        // The frame is ours, so the resize border is too: a band along each edge, as the borderless main window has.
        CanResize = true;
        Content = WindowChrome.WithResizeBands(this, _frame);

        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick());
        Opened += (_, _) =>
        {
            _clock.Start();
            FocusFirst();
        };
        Closed += (_, _) =>
        {
            _clock.Stop();
            _form.Changed -= OnChanged;
            if (_form.Packages is { } packages)
            {
                packages.Changed -= OnPackagesChanged;
            }
        };
        KeyDown += OnKey;

        Load(form);
    }

    /// <summary>
    /// Takes the keyboard: the window comes forward and the search box has focus, as the palette's does. Without it a
    /// window manager that does not activate a new window (or none, under Xvfb) left the keys in the main window, where
    /// the focus pin put them on the command line — so what the player typed into "the settings" went into a line ⏎
    /// would send to the game.
    /// </summary>
    public void FocusFirst()
    {
        Activate();
        _search.Focus();
    }

    /// <summary>The section on the right, by id.</summary>
    public string? ShownSection => _shown;

    /// <summary>The frame's close button, for a test.</summary>
    public Button CloseButton { get; }

    /// <summary>The tokens the window is drawn in now: the client's theme, followed when one is picked here.</summary>
    public NativeTokens Tokens => _tokens;

    private static NativeTokens TokensFor(SettingsForm form) => NativeTokens.For(form.Theme?.Invoke(), form.IsDark);

    /// <summary>Wears <paramref name="tokens"/>: the variant, the planes, Fluent's resources and the frame.</summary>
    private void ApplyTokens(NativeTokens tokens)
    {
        _tokens = tokens;
        RequestedThemeVariant = tokens.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        tokens.Apply(Resources);
        Background = tokens.Brush(tokens.Card);
        Foreground = tokens.Brush(tokens.Text);
        _frame.BorderBrush = tokens.Brush(tokens.Edge);
        _title.Foreground = tokens.Brush(tokens.Text);
        CloseButton.Foreground = tokens.Brush(tokens.Dim);
    }

    /// <summary>The ids of the fields drawn on the right, in order, for a test.</summary>
    public IReadOnlyList<string> ShownFields { get; private set; } = [];

    /// <summary>The control drawn for each field on the right, by field id, for a test.</summary>
    public IReadOnlyDictionary<string, Control> FieldControls => _controls;

    /// <summary>The height a list editor takes: the page's viewport less the title above it.</summary>
    private double ListEditorHeight() => Math.Max(360, _scroller.Bounds.Height - 96);

    /// <summary>F10's package manager page, or null on any other page.</summary>
    internal PackagePageView? PackagePage { get; private set; }

    /// <summary>The master-detail editor a list section (F2, F3, F4, F6) is drawn as, or null on any other page.</summary>
    internal ListEditorView? ListEditor { get; private set; }

    /// <summary>The reset button of each field on the right, by field id, for a test.</summary>
    public IReadOnlyDictionary<string, Button> ResetButtons => _resets;

    /// <summary>What each section's row says beside its title — its match count while searching — by id, for a test.</summary>
    public IReadOnlyDictionary<string, string> SectionBadges =>
        _badges.ToDictionary(b => b.Key, b => b.Value.Text ?? string.Empty, StringComparer.Ordinal);

    /// <summary>The search box.</summary>
    public TextBox SearchBox => _search;

    /// <summary>The section list.</summary>
    public ListBox SectionList => _sections;

    /// <summary>The theme cards on the page, for a test.</summary>
    public IReadOnlyList<ThemeCardView> ThemeCards => _themeCards;

    /// <summary>The world or character picker of a scoped section, for a test; null on an app-wide one.</summary>
    public ComboBox? SubjectPicker { get; private set; }

    /// <summary>The button that opens the section's own cell screen, for a test; null where there is none.</summary>
    public Button? ScreenDoor { get; private set; }

    /// <summary>The meter table, for a test: a heading row and one row per meter.</summary>
    public Grid? MeterGrid { get; private set; }

    /// <summary>Shows <paramref name="form"/>, on its initial section, replacing the form shown before.</summary>
    public void Load(SettingsForm form)
    {
        _form.Changed -= OnChanged;
        if (_form.Packages is { } before)
        {
            before.Changed -= OnPackagesChanged;
        }

        _form = form;
        _form.Changed += OnChanged;
        if (_form.Packages is { } after)
        {
            after.Changed += OnPackagesChanged;
        }
        ApplyTokens(TokensFor(form));
        _shown = null;
        _matches = _form.Search(_search.Text);
        BuildSectionList();
        Choose(form.Section(form.Initial) is { } initial && SettingsForm.IsDeclared(initial)
            ? initial.Id
            : form.Sections.FirstOrDefault(SettingsForm.IsDeclared)?.Id);
    }

    // ── the section list ─────────────────────────────────────────────────────────────────────────

    private IEnumerable<ListBoxItem> Items => _sections.ItemsSource as IEnumerable<ListBoxItem> ?? [];

    private void BuildSectionList()
    {
        _badges.Clear();
        var items = new List<ListBoxItem>();
        foreach (var section in _form.Sections)
        {
            var title = new TextBlock { Text = section.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var badge = new TextBlock { Foreground = DimInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            _badges[section.Id] = badge;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(title);
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            var item = new ListBoxItem { Content = row, Tag = section.Id, Padding = new Thickness(12, 9) };
            ToolTip.SetTip(item, SettingsForm.IsDeclared(section) ? section.Key : $"Opens the {section.Key} screen");
            items.Add(item);
        }

        _choosing = true;
        _sections.ItemsSource = items;
        _choosing = false;
        UpdateBadges();
    }

    private void UpdateBadges()
    {
        var counts = SettingsSearch.CountBySection(_matches);
        var searching = Searching;
        foreach (var section in _form.Sections)
        {
            var count = counts.GetValueOrDefault(section.Id);
            _badges[section.Id].Text = searching
                ? count.ToString(CultureInfo.InvariantCulture)
                : SettingsForm.IsDeclared(section) ? string.Empty : "↗";
            if (Items.FirstOrDefault(i => Equals(i.Tag, section.Id)) is { } item)
            {
                item.Opacity = searching && count == 0 ? 0.45 : 1;
            }
        }
    }

    private void OnSectionChosen()
    {
        if (_choosing || _sections.SelectedItem is not ListBoxItem { Tag: string id } || _form.Section(id) is not { } section)
        {
            return;
        }

        if (!SettingsForm.IsDeclared(section))
        {
            // Not drawn here yet: its own screen is the way to it, and this window gets out of its way.
            Select(_shown);
            _form.OpenScreen(id);
            return;
        }

        if (!string.Equals(id, _shown, StringComparison.Ordinal))
        {
            Choose(id);
        }
    }

    private void Choose(string? id)
    {
        _shown = id;
        Select(id);
        BuildPage();
    }

    private void Select(string? id)
    {
        _choosing = true;
        _sections.SelectedItem = Items.FirstOrDefault(i => Equals(i.Tag, id));
        _choosing = false;
    }

    // ── search ───────────────────────────────────────────────────────────────────────────────────

    private bool Searching => !string.IsNullOrWhiteSpace(_search.Text);

    private void OnQuery()
    {
        _matches = _form.Search(_search.Text);
        UpdateBadges();
        if (Searching
            && !_matches.Any(m => m.Field is not null && m.Section.Id == _shown)
            && _matches.FirstOrDefault(m => m.Field is not null) is { } first)
        {
            // The section on show has nothing matching: go to the first that has.
            Choose(first.Section.Id);
            return;
        }

        BuildPage();
    }

    // ── the page ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The note line under a field on the page, by field id (shown or not), for tests.</summary>
    internal TextBlock? NoteFor(string id) => _notes.GetValueOrDefault(id);

    private void BuildPage()
    {
        _page.Children.Clear();
        _refreshers.Clear();
        _controls.Clear();
        _resets.Clear();
        _notes.Clear();
        _meterNow.Clear();
        _themeCards.Clear();
        SubjectPicker = null;
        ListEditor = null;
        PackagePage = null;
        ScreenDoor = null;
        MeterGrid = null;
        _meterRows = -1;
        if (_shown is null || _form.Section(_shown) is not { } section)
        {
            ShownFields = [];
            return;
        }

        _page.Children.Add(new TextBlock { Text = section.Title, FontSize = NativeTokens.Heading, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
        if (section.Key is { } key)
        {
            _page.Children.Add(new TextBlock { Text = $"{key} in the client · changes apply as you make them", Foreground = DimInk, Margin = new Thickness(0, 0, 0, 6) });
        }

        if (section.Packages is not null)
        {
            if (_form.Packages is not { } manager)
            {
                _page.Children.Add(new TextBlock { Text = "This client offers no packages.", Foreground = DimInk, Margin = new Thickness(0, 16, 0, 0) });
                ShownFields = [];
                return;
            }

            var packages = new PackagePageView(
                manager,
                f => !Searching || _matches.Any(m => ReferenceEquals(m.Field, f)),
                _tokens)
            {
                Height = ListEditorHeight(),
            };
            PackagePage = packages;
            _page.Children.Add(packages);
            ShownFields = packages.ShownFields;
            return;
        }

        if (section.Collection is not null)
        {
            var editor = new ListEditorView(
                _form,
                section,
                f => !Searching || _matches.Any(m => ReferenceEquals(m.Field, f)),
                _tokens)
            {
                Height = ListEditorHeight(),
                Margin = new Thickness(0, 8, 0, 0),
            };
            ListEditor = editor;
            _page.Children.Add(editor);
            foreach (var (id, control) in editor.FieldControls)
            {
                _controls[id] = control;
            }

            _refreshers.Add(() =>
            {
                editor.Refresh();
                _controls.Clear();
                foreach (var (id, control) in editor.FieldControls)
                {
                    _controls[id] = control;
                }

                ShownFields = editor.ShownFields;
            });
            ShownFields = editor.ShownFields;
            return;
        }

        if (section.Scope != SettingScope.App)
        {
            _page.Children.Add(SubjectRow(section));
            if (!_form.HasSubject(section))
            {
                ShownFields = [];
                return;
            }
        }

        var visible = Searching
            ? section.Fields.Where(f => _matches.Any(m => ReferenceEquals(m.Field, f))).ToList()
            : section.Fields.ToList();
        if (visible.Count == 0)
        {
            _page.Children.Add(new TextBlock { Text = "No setting here matches the search.", Foreground = DimInk, Margin = new Thickness(0, 16, 0, 0) });
        }

        string? group = null;
        foreach (var field in visible)
        {
            if (field.Group is { } heading && !string.Equals(heading, group, StringComparison.Ordinal))
            {
                group = heading;
                _page.Children.Add(new TextBlock
                {
                    Text = heading.ToUpperInvariant(),
                    FontSize = NativeTokens.Small,
                    FontWeight = FontWeight.SemiBold,
                    LetterSpacing = 0.8,
                    Margin = new Thickness(0, 20, 0, 6),
                    Foreground = _tokens.Brush(_tokens.Header),
                });
            }

            _page.Children.Add(field switch
            {
                ListField { Id: BuiltInSettings.MetersField } meters => MeterTable(meters),
                ThemeField theme => ThemePicker(theme),
                _ => FieldRow(field),
            });
        }

        ShownFields = visible.Select(f => f.Id).ToList();
    }

    /// <summary>
    /// A scoped section's header row: which world or character its fields are for, picked from every one there is, and
    /// the way to the section's own cell screen for what is not a field — the lists of characters and trigger sets,
    /// adding and deleting. With nothing to edit yet it says so and offers only the screen.
    /// </summary>
    private StackPanel SubjectRow(SettingsSectionContribution section)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 6, 0, 2) };
        var subjects = _form.Subjects(section.Scope);
        var noun = section.Scope == SettingScope.World ? "world" : "character";
        if (subjects.Count == 0)
        {
            row.Children.Add(new TextBlock { Text = $"There is no {noun} yet.", VerticalAlignment = VerticalAlignment.Center, Foreground = DimInk });
        }
        else
        {
            var labels = subjects.Select(s => s.Label).ToList();
            var current = _form.Subject(section.Scope);
            var picker = new ComboBox { ItemsSource = labels, MinWidth = 240, SelectedItem = current?.Label };
            AutomationProperties.SetName(picker, $"The {noun} these settings are for");
            picker.SelectionChanged += (_, _) =>
            {
                if (!_syncing && picker.SelectedIndex is >= 0 and var i && i < subjects.Count
                    && !string.Equals(subjects[i].Label, _form.Subject(section.Scope)?.Label, StringComparison.Ordinal))
                {
                    _form.Choose(subjects[i]);
                    var offset = _scroller.Offset;
                    BuildPage();
                    _scroller.Offset = offset;
                }
            };
            SubjectPicker = picker;
            row.Children.Add(new TextBlock { Text = section.Scope == SettingScope.World ? "World" : "Character", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold });
            row.Children.Add(picker);
        }

        if (section.Key is { } key)
        {
            var door = new Button { Content = $"Open the {key} screen ↗", VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(door, section.Scope == SettingScope.World
                ? "Adding and deleting worlds, their characters and trigger sets are on the cell screen"
                : "Adding and deleting characters is on the cell screen");
            AutomationProperties.SetName(door, $"Open the {key} screen");
            var id = section.Id;
            door.Click += (_, _) => _form.OpenScreen(id);
            ScreenDoor = door;
            row.Children.Add(door);
        }

        return row;
    }

    /// <summary>The theme field: a card per theme, its preview drawn from the client's model; a click uses it.</summary>
    private StackPanel ThemePicker(ThemeField field)
    {
        var outer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 4) };
        var words = new StackPanel { Spacing = 1 };
        words.Children.Add(new TextBlock { Text = NativeKeys.Words(Capitalise(field.Label)), FontSize = NativeTokens.Title });
        if (field.Help is { } help)
        {
            words.Children.Add(new TextBlock { Text = NativeKeys.Words(Capitalise(help)), FontSize = NativeTokens.Body, Foreground = DimInk, TextWrapping = TextWrapping.Wrap });
        }

        outer.Children.Add(words);

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var inUse = CurrentTheme(field);
        foreach (var card in _form.Themes())
        {
            var view = new ThemeCardView(card, string.Equals(card.Name, inUse, StringComparison.OrdinalIgnoreCase), _tokens);
            view.Click += (_, _) =>
            {
                if (!string.Equals(card.Name, CurrentTheme(field), StringComparison.OrdinalIgnoreCase))
                {
                    _form.SetTheme(field, card.Name);
                }
            };
            _themeCards.Add(view);
            wrap.Children.Add(view);
        }

        _controls[field.Id] = wrap;
        _refreshers.Add(() =>
        {
            var now = CurrentTheme(field);
            foreach (var view in _themeCards)
            {
                view.SetInUse(string.Equals(view.Card.Name, now, StringComparison.OrdinalIgnoreCase), _tokens);
            }
        });
        outer.Children.Add(wrap);
        return outer;
    }

    private string? CurrentTheme(ThemeField field) =>
        field.Binding is SettingBinding<string> binding ? _form.Get(binding) : null;

    /// <summary>The accent the changed dot wears: the client's, as every native surface's.</summary>
    private SolidColorBrush Accent => _tokens.Brush(_tokens.Accent);

    /// <summary>A quiet line's ink, held to the floor (an opacity would not be).</summary>
    private SolidColorBrush DimInk => _tokens.Brush(_tokens.Dim);

    /// <summary>One field: label and help on the left, its control, then the changed dot and the reset.</summary>
    private Grid FieldRow(SettingsField field)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 4), MinHeight = 44 };

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Margin = new Thickness(0, 0, 16, 0) };
        words.Children.Add(new TextBlock { Text = NativeKeys.Words(Capitalise(field.Label)), FontSize = NativeTokens.Title, TextWrapping = TextWrapping.Wrap });
        if (field.Help is { } help)
        {
            words.Children.Add(new TextBlock { Text = NativeKeys.Words(Capitalise(help)), FontSize = NativeTokens.Body, Foreground = DimInk, TextWrapping = TextWrapping.Wrap });
        }

        // The note the cell F5 draws beside the value (join wraps' recommendation), from the same sentence, in the
        // cells' caution amber; gone once the field has been changed.
        var note = new TextBlock { FontSize = NativeTokens.Body, Foreground = _tokens.Brush(_tokens.Notice), TextWrapping = TextWrapping.Wrap };
        void ShowNote()
        {
            var text = _form.Note(field);
            note.Text = text is null ? null : NativeKeys.Words(Capitalise(text));
            note.IsVisible = text is not null;
        }

        ShowNote();
        _refreshers.Add(ShowNote);
        _notes[field.Id] = note;
        words.Children.Add(note);

        grid.Children.Add(words);

        var control = ControlFor(field);
        control.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(control, field.Label);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        _controls[field.Id] = control;

        grid.Children.Add(ChangedMark(field, column: 2));
        return grid;
    }

    /// <summary>The dot and reset button for <paramref name="field"/>, shown only while it differs from its default.</summary>
    private StackPanel ChangedMark(SettingsField field, int column)
    {
        var dot = new Ellipse { Width = 8, Height = 8, Fill = Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
        ToolTip.SetTip(dot, "Changed from its default");
        var reset = new Button { Content = "Reset", Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(reset, $"Back to the default: {SettingsForm.DefaultText(field)}");
        AutomationProperties.SetName(reset, $"Reset {field.Label}");
        reset.Click += (_, _) => _form.Reset(field);
        _resets[field.Id] = reset;

        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, MinWidth = 100 };
        panel.Children.Add(dot);
        panel.Children.Add(reset);
        Grid.SetColumn(panel, column);

        void Refresh()
        {
            var changed = !_form.IsDefault(field);
            dot.Opacity = changed ? 1 : 0;
            reset.IsVisible = changed;
        }

        Refresh();
        _refreshers.Add(Refresh);
        return panel;
    }

    /// <summary>The native control for a field's kind, wired to the form and refreshed from it.</summary>
    private Control ControlFor(SettingsField field)
    {
        switch (field)
        {
            case BoolField when field.Binding is SettingBinding<bool> flag:
            {
                var toggle = new ToggleSwitch { OnContent = "On", OffContent = "Off", IsChecked = _form.Get(flag) };
                toggle.IsCheckedChanged += (_, _) =>
                {
                    if (!_syncing && toggle.IsChecked is { } on && on != _form.Get(flag))
                    {
                        _form.Set(flag, on);
                    }
                };
                _refreshers.Add(() => toggle.IsChecked = _form.Get(flag));
                return toggle;
            }

            case NumberField number when field.Binding is SettingBinding<int> count:
            {
                var box = new NumericUpDown
                {
                    Minimum = number.Minimum,
                    Maximum = number.Maximum,
                    Increment = 1,
                    FormatString = "0",
                    Value = _form.Get(count),
                    Width = 150,
                    ClipValueToMinMax = true,
                };
                box.ValueChanged += (_, e) =>
                {
                    if (!_syncing && e.NewValue is { } value && (int)value != _form.Get(count))
                    {
                        _form.SetNumber(number, (int)value);
                    }
                };
                _refreshers.Add(() =>
                {
                    if (!box.IsKeyboardFocusWithin)
                    {
                        box.Value = _form.Get(count);
                    }
                });
                return box;
            }

            case ChoiceField choice when field.Binding is SettingBinding<string> named:
            {
                var combo = new ComboBox { ItemsSource = _form.Choices(choice), SelectedItem = _form.Get(named), MinWidth = 180 };
                combo.SelectionChanged += (_, _) =>
                {
                    if (!_syncing && combo.SelectedItem is string value && value != _form.Get(named))
                    {
                        _form.Set(named, value);
                    }
                };
                _refreshers.Add(() => combo.SelectedItem = _form.Get(named));
                return combo;
            }

            case ColourField colour when field.Binding is SettingBinding<string> named:
            {
                var swatches = colour.Names.Select(Swatch).ToList();
                var combo = new ComboBox { ItemsSource = swatches, MinWidth = 180 };
                void Pick() => combo.SelectedItem = swatches.FirstOrDefault(c => Equals(c.Tag, _form.Get(named)));
                Pick();
                combo.SelectionChanged += (_, _) =>
                {
                    if (!_syncing && combo.SelectedItem is Control { Tag: string value } && value != _form.Get(named))
                    {
                        _form.Set(named, value);
                    }
                };
                _refreshers.Add(Pick);
                return combo;
            }

            case TextField text when field.Binding is SettingBinding<string?>:
            {
                var box = new TextBox { Text = _form.Text(text), Width = 260 };
                if (text.Masked)
                {
                    box.PasswordChar = '•';
                }

                if (text.MaxLength is { } max)
                {
                    box.MaxLength = max;
                }

                void Commit()
                {
                    if (!_syncing && (box.Text ?? string.Empty) != _form.Text(text))
                    {
                        if (_form.SetText(text, box.Text) is { } refused)
                        {
                            // Refused by the rule the cell screen's field applies: put the value back and say why.
                            box.Text = _form.Text(text);
                            ToolTip.SetTip(box, refused);
                            ToolTip.SetIsOpen(box, true);
                        }
                        else
                        {
                            ToolTip.SetTip(box, null);
                        }
                    }
                }

                box.LostFocus += (_, _) => Commit();
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        Commit();
                        e.Handled = true;
                    }
                };
                _refreshers.Add(() =>
                {
                    if (!box.IsKeyboardFocusWithin)
                    {
                        box.Text = _form.Text(text);
                    }
                });
                return box;
            }

            default:
                return new TextBlock { Text = "set on its own screen", Foreground = DimInk };
        }
    }

    private static StackPanel Swatch(string name)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Tag = name };
        if (Color.TryParse(name, out var colour))
        {
            panel.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(NativeTokens.RadiusS), Background = new SolidColorBrush(colour) });
        }

        panel.Children.Add(new TextBlock { Text = name });
        return panel;
    }

    // ── the meter table ──────────────────────────────────────────────────────────────────────────

    private StackPanel MeterTable(ListField field)
    {
        var outer = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 4) };

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var words = new StackPanel { Spacing = 1 };
        words.Children.Add(new TextBlock { Text = NativeKeys.Words(Capitalise(field.Label)), FontSize = NativeTokens.Title });
        if (field.Help is { } help)
        {
            words.Children.Add(new TextBlock { Text = NativeKeys.Words(Capitalise(help)), FontSize = NativeTokens.Body, Foreground = DimInk, TextWrapping = TextWrapping.Wrap });
        }

        top.Children.Add(words);
        top.Children.Add(ChangedMark(field, column: 1));
        outer.Children.Add(top);

        var received = _form.Received();
        var paths = received.Select(r => r.Path).ToList();
        var meters = _form.Meters(field);
        _meterRows = meters.Count;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,*,100,Auto"), ColumnSpacing = 8, RowSpacing = 6 };
        MeterGrid = grid;
        _controls[field.Id] = grid;
        string[] headings = ["Label", "Value path", "Maximum path", "Now"];
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < headings.Length; c++)
        {
            var heading = new TextBlock { Text = headings[c], FontSize = NativeTokens.Body, FontWeight = FontWeight.SemiBold, Foreground = DimInk };
            Grid.SetColumn(heading, c);
            grid.Children.Add(heading);
        }

        AutoCompleteBox? lastPath = null;
        void Commit() => _form.SetMeters(field, meters);

        for (var i = 0; i < meters.Count; i++)
        {
            var meter = meters[i];
            var row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = new TextBox { Text = meter.Label, MaxLength = StatusMeter.MaxLabelLength };
            AutomationProperties.SetName(label, $"meter {row} label");
            label.LostFocus += (_, _) =>
            {
                if ((label.Text ?? string.Empty) != meter.Label)
                {
                    meter.Label = label.Text ?? string.Empty;
                    Commit();
                }
            };

            var value = PathBox(meter.Value, paths, $"meter {row} value path", v => meter.Value = v ?? string.Empty, Commit);
            var max = PathBox(meter.Max, paths, $"meter {row} maximum path", v => meter.Max = v, Commit);
            value.GotFocus += (_, _) => lastPath = value;
            max.GotFocus += (_, _) => lastPath = max;

            var now = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
            _meterNow.Add((meter, now));

            var remove = new Button { Content = "✕", Padding = new Thickness(9, 3) };
            ToolTip.SetTip(remove, $"Remove {meter.Label}");
            AutomationProperties.SetName(remove, $"Remove meter {meter.Label}");
            var index = i;
            remove.Click += (_, _) =>
            {
                meters.RemoveAt(index);
                Commit();
            };

            Control[] cells = [label, value, max, now, remove];
            for (var c = 0; c < cells.Length; c++)
            {
                Grid.SetRow(cells[c], row);
                Grid.SetColumn(cells[c], c);
                grid.Children.Add(cells[c]);
            }
        }

        outer.Children.Add(grid);

        var add = new Button { Content = "+ Meter", HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(add, "Add a meter");
        add.Click += (_, _) =>
        {
            meters.Add(new StatusMeter { Label = "new" });
            Commit();
        };
        outer.Children.Add(add);

        // What this connection has received, to pick a path from: a click puts it in the path box last used.
        var box = new StackPanel { Spacing = 2 };
        box.Children.Add(new TextBlock { Text = "Received on this connection", FontSize = NativeTokens.Body, FontWeight = FontWeight.SemiBold, Foreground = DimInk, Margin = new Thickness(0, 2, 0, 2) });
        if (received.Count == 0)
        {
            box.Children.Add(new TextBlock { Text = "Nothing yet — a meter shows once its value arrives.", FontSize = NativeTokens.Body, Foreground = DimInk });
        }

        foreach (var item in received.Take(40))
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            line.Children.Add(new TextBlock { Text = item.Path, FontFamily = NativeTokens.MonoFont, FontSize = NativeTokens.Body });
            var v = new TextBlock { Text = item.Value, FontSize = NativeTokens.Body, Foreground = DimInk, Margin = new Thickness(16, 0, 0, 0) };
            Grid.SetColumn(v, 1);
            line.Children.Add(v);
            var pick = new Button
            {
                Content = line,
                Background = Brushes.Transparent,
                Padding = new Thickness(6, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            ToolTip.SetTip(pick, "Use this path in the path box you were last in");
            pick.Click += (_, _) =>
            {
                if (lastPath is { } target)
                {
                    target.Text = item.Path;
                    target.Focus();
                }
            };
            box.Children.Add(pick);
        }

        outer.Children.Add(new Border
        {
            Child = box,
            BorderThickness = new Thickness(1),
            BorderBrush = _tokens.Brush(_tokens.Edge),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            Padding = new Thickness(10, 6, 10, 8),
            Margin = new Thickness(0, 4, 0, 0),
        });

        Tick();
        return outer;
    }

    private static AutoCompleteBox PathBox(string? text, IReadOnlyList<string> paths, string name, Action<string?> set, Action commit)
    {
        var box = new AutoCompleteBox
        {
            Text = text ?? string.Empty,
            ItemsSource = paths,
            FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
            MinimumPrefixLength = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(box, name);
        var held = string.IsNullOrWhiteSpace(text) ? null : text;
        void Commit()
        {
            var now = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
            if (!string.Equals(now, held, StringComparison.Ordinal))
            {
                held = now;
                set(now);
                commit();
            }
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
            }
        };
        return box;
    }

    /// <summary>The meters' <c>now</c> column, re-read each second while the window is open.</summary>
    private void Tick()
    {
        foreach (var (meter, now) in _meterNow)
        {
            var text = _form.Now(meter);
            now.Text = text ?? "waiting";
            now.Opacity = text is null ? 0.6 : 1;
        }
    }

    // ── changes landing ──────────────────────────────────────────────────────────────────────────

    /// <summary>Raised on the client's thread once a change has landed; the page catches up on this one.</summary>
    private void OnChanged() => Dispatcher.UIThread.Post(Refresh);

    /// <summary>Raised on the client's thread when the packages moved; F10's page catches up on this one.</summary>
    private void OnPackagesChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (PackagePage is { } page)
        {
            page.Refresh();
            ShownFields = page.ShownFields;
        }
    });

    /// <summary>Re-reads every value and indicator on the page. A meter added or removed rebuilds the page, whose rows moved.</summary>
    internal void Refresh()
    {
        var tokens = TokensFor(_form);
        var flipped = tokens != _tokens;
        if (flipped)
        {
            ApplyTokens(tokens);
        }

        if (flipped
            || (_meterRows >= 0
                && _form.Section(_shown ?? string.Empty)?.Fields.OfType<ListField>().FirstOrDefault() is { } meters
                && _form.Meters(meters).Count != _meterRows))
        {
            var offset = _scroller.Offset;
            BuildPage();
            _scroller.Offset = offset;
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
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (Searching)
            {
                _search.Text = string.Empty;
            }
            else
            {
                Close();
            }

            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            _search.Focus();
            _search.SelectAll();
            e.Handled = true;
        }
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
