using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Panels;
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
    /// <summary>How far the meter table's received-paths box sits in: under the path columns, past the label column.</summary>
    private const double MeterPathsIndent = 128;

    /// <summary>The received-paths panel's height before it scrolls: its heading and about six paths.</summary>
    private const double MeterPathsMaxHeight = 200;

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
    private readonly Border _bar;
    private readonly Border _searchFrame;
    private readonly TextBlock _searchCount;
    private readonly global::Avalonia.Controls.Shapes.Path _searchGlass;
    private readonly ContentControl _scope;
    private readonly Border _nav;
    private readonly TextBlock _navNote;
    private readonly Border _footer;
    private readonly TextBlock _footerLeft;
    private readonly TextBlock _footerRight;
    private SettingsForm _form;
    private NativeTokens _tokens;
    private IReadOnlyList<SettingsMatch> _matches = [];
    private string? _shown;
    private bool _syncing;
    private bool _choosing;
    private int _meterRows = -1;

    /// <summary>The size the window first opens at, before the player resizes it.</summary>
    internal const double DefaultWidth = 1120, DefaultHeight = 760;

    public SettingsWindow(SettingsForm form)
    {
        _form = form;
        _tokens = TokensFor(form);
        Title = "Settings";
        Width = DefaultWidth;
        Height = DefaultHeight;
        MinWidth = 640;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // The palette's frame, not the platform's: the window draws its own title, close button and edge, so it reads
        // as one of the client's surfaces (and looks the same under every window manager, or none).
        WindowDecorations = WindowDecorations.None;
        Styles.Add(new FluentTheme());

        // ── the header: title, search, the subject a scoped section is for, close ──
        _title = new TextBlock
        {
            Text = "Settings",
            FontSize = NativeTokens.CardTitle + 1,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 212,
        };
        CloseButton = new Button
        {
            Content = "✕",
            Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space4),
            Margin = new Thickness(NativeTokens.Space12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        AutomationProperties.SetName(CloseButton, "Close settings");
        ToolTip.SetTip(CloseButton, "Close (Esc)");
        CloseButton.Click += (_, _) => Close();

        _search = new TextBox
        {
            PlaceholderText = "Search settings",
            FontSize = NativeTokens.Label,
            Padding = new Thickness(0, NativeTokens.Space2),
            MinHeight = 0,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
        })
        {
            _search.Resources[key] = Brushes.Transparent;
        }

        AutomationProperties.SetName(_search, "Search settings");
        _search.TextChanged += (_, _) => OnQuery();
        _searchCount = new TextBlock { FontSize = NativeTokens.Body, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NativeTokens.Space8, 0, 0, 0) };
        _searchGlass = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M6,0 A6,6 0 1 1 6,12 A6,6 0 1 1 6,0 M10.5,10.5 L14,14"),
            StrokeThickness = 1.6,
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, NativeTokens.Space8, 0),
        };
        var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        searchRow.Children.Add(_searchGlass);
        Grid.SetColumn(_search, 1);
        searchRow.Children.Add(_search);
        Grid.SetColumn(_searchCount, 2);
        searchRow.Children.Add(_searchCount);
        _searchFrame = new Border
        {
            Child = searchRow,
            BorderThickness = new Thickness(NativeTokens.Hairline),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            Padding = new Thickness(NativeTokens.Space12, NativeTokens.Space6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Ibeam),
        };
        _searchFrame.PointerPressed += (_, _) => _search.Focus();

        _scope = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NativeTokens.Space16, 0, 0, 0) };
        var header = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(3, GridUnitType.Star) { MaxWidth = 480 }, new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)], Margin = new Thickness(NativeTokens.Space20, 0, NativeTokens.Space8, 0), Background = Brushes.Transparent };
        header.Children.Add(_title);
        Grid.SetColumn(_searchFrame, 1);
        header.Children.Add(_searchFrame);
        Grid.SetColumn(_scope, 3);
        header.Children.Add(_scope);
        Grid.SetColumn(CloseButton, 4);
        header.Children.Add(CloseButton);
        _bar = new Border { Height = 56, BorderThickness = new Thickness(0, 0, 0, NativeTokens.Hairline), Child = header };
        _bar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(_bar).Properties.IsLeftButtonPressed
                && (ReferenceEquals(e.Source, _bar) || ReferenceEquals(e.Source, header) || ReferenceEquals(e.Source, _title)))
            {
                BeginMoveDrag(e);
            }
        };
        DockPanel.SetDock(_bar, Dock.Top);

        // ── the footer ──
        _footerLeft = new TextBlock { Text = "Changes apply to connected sessions as you make them.", FontSize = NativeTokens.Body, VerticalAlignment = VerticalAlignment.Center };
        _footerRight = new TextBlock { Text = "Ctrl+F searches · Esc closes", FontSize = NativeTokens.Body, VerticalAlignment = VerticalAlignment.Center };
        var footerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(NativeTokens.Space20, 0) };
        footerRow.Children.Add(_footerLeft);
        Grid.SetColumn(_footerRight, 1);
        footerRow.Children.Add(_footerRight);
        _footer = new Border { Height = 36, BorderThickness = new Thickness(0, NativeTokens.Hairline, 0, 0), Child = footerRow };
        DockPanel.SetDock(_footer, Dock.Bottom);

        // ── the section navigation ──
        _sections = new ListBox { Background = Brushes.Transparent, Padding = new Thickness(0) };
        _sections.SelectionChanged += (_, _) => OnSectionChosen();
        _navNote = new TextBlock
        {
            Text = "Every page here is drawn from the same declared settings the terminal's F-screens use.",
            FontSize = NativeTokens.Body,
            LineHeight = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(NativeTokens.Space10, NativeTokens.Space8, NativeTokens.Space10, NativeTokens.Space4),
        };
        var navDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_navNote, Dock.Bottom);
        navDock.Children.Add(_navNote);
        navDock.Children.Add(_sections);
        _nav = new Border { Width = 232, BorderThickness = new Thickness(0, 0, NativeTokens.Hairline, 0), Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space12), Child = navDock };
        DockPanel.SetDock(_nav, Dock.Left);

        // ── the page ──
        _page = new StackPanel { Margin = new Thickness(NativeTokens.PageInset, NativeTokens.Space24, NativeTokens.PageInset, NativeTokens.Space28), Spacing = NativeTokens.Space20 };
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
        dock.Children.Add(_bar);
        dock.Children.Add(_footer);
        dock.Children.Add(_nav);
        dock.Children.Add(_scroller);
        _frame = new Border { BorderThickness = new Thickness(NativeTokens.Hairline), Child = dock };

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
        var page = tokens.Page;
        page.Apply(Resources);
        foreach (var key in new[] { "SystemControlHighlightListAccentLowBrush", "SystemControlHighlightListAccentMediumBrush", "SystemControlHighlightListAccentHighBrush" })
        {
            _sections.Resources[key] = tokens.Brush(tokens.Accent);
        }

        _sections.Resources["ListBoxItemBackgroundSelected"] = tokens.Brush(tokens.Accent);
        _sections.Resources["ListBoxItemBackgroundSelectedPointerOver"] = tokens.Brush(tokens.Accent);
        _sections.Resources["ListBoxItemBackgroundSelectedPressed"] = tokens.Brush(tokens.Accent);
        _sections.Resources["ListBoxItemForegroundSelected"] = tokens.Brush(tokens.AccentInk);
        _sections.Resources["ListBoxItemForegroundSelectedPointerOver"] = tokens.Brush(tokens.AccentInk);
        _sections.Resources["ListBoxItemBackgroundPointerOver"] = page.Brush(page.Button);
        _sections.Resources["ListBoxItemBackgroundPressed"] = page.Brush(page.PopupSelected);
        Background = page.Brush(page.Ground);
        Foreground = page.Brush(page.Text);
        _frame.BorderBrush = page.Brush(page.Rule);
        _frame.Background = page.Brush(page.Ground);
        _bar.Background = page.Brush(page.Card);
        _bar.BorderBrush = page.Brush(page.Rule);
        _footer.Background = page.Brush(page.Card);
        _footer.BorderBrush = page.Brush(page.Rule);
        _footerLeft.Foreground = page.Brush(page.Dim);
        _footerRight.Foreground = page.Brush(page.Dim);
        _nav.Background = page.Brush(page.Nav);
        _nav.BorderBrush = page.Brush(page.Rule);
        _navNote.Foreground = page.Brush(page.Dim);
        _searchFrame.Background = page.Brush(page.Ground);
        _searchFrame.BorderBrush = page.Brush(page.FocusEdge);
        _searchGlass.Stroke = page.Brush(page.Dim);
        _searchCount.Foreground = page.Brush(page.Dim);
        _title.Foreground = page.Brush(page.Text);
        CloseButton.Foreground = page.Brush(page.Dim);
    }

    /// <summary>The ids of the fields drawn on the right, in order, for a test.</summary>
    public IReadOnlyList<string> ShownFields { get; private set; } = [];

    /// <summary>The control drawn for each field on the right, by field id, for a test.</summary>
    public IReadOnlyDictionary<string, Control> FieldControls => _controls;

    /// <summary>The height a list editor takes: the page's viewport less the title above it.</summary>
    private double ListEditorHeight() => Math.Max(360, _scroller.Bounds.Height - 124);

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
            var title = new TextBlock { Text = section.Title, FontSize = NativeTokens.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var badge = new TextBlock { FontSize = NativeTokens.Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NativeTokens.Space8, 0, 0, 0) };
            _badges[section.Id] = badge;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(title);
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            var item = new ListBoxItem
            {
                Content = row,
                Tag = section.Id,
                Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space8),
                Margin = new Thickness(0, 0, 0, NativeTokens.Space2),
                MinHeight = 0,
                CornerRadius = new CornerRadius(NativeTokens.RadiusControl),
            };
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
            // While searching a section with matches says how many and one without says nothing (the mockup's rows):
            // a column of greyed zeros read as a column of disabled sections.
            _badges[section.Id].Text = searching
                ? count > 0 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty
                : SettingsForm.IsDeclared(section) ? string.Empty : "↗";
        }

        StyleNavigation();
        var total = _matches.Count(m => m.Field is not null);
        _searchCount.Text = searching ? $"{total} setting{(total == 1 ? string.Empty : "s")}" : string.Empty;
    }

    /// <summary>
    /// The chosen section's row wears the accent with its ink and weight, as the mockup's does; the rest wear the page's
    /// text, their counts quiet. (The row's fill is the list's own selected brush.)
    /// </summary>
    private void StyleNavigation()
    {
        var page = _tokens.Page;
        foreach (var item in Items)
        {
            var chosen = Equals(item.Tag, _shown);
            if (item.Content is Grid { Children: [TextBlock title, TextBlock badge] })
            {
                title.FontWeight = chosen ? FontWeight.SemiBold : FontWeight.Normal;
                title.Foreground = chosen ? _tokens.Brush(_tokens.AccentInk) : page.Brush(page.Text);
                badge.FontWeight = title.FontWeight;
                badge.Foreground = chosen ? _tokens.Brush(_tokens.AccentInk) : page.Brush(page.Dim);
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
        StyleNavigation();
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
            _scope.Content = null;
            ShownFields = [];
            return;
        }

        _scope.Content = section.Scope is SettingScope.World or SettingScope.Character ? SubjectRow(section) : null;
        _page.Children.Add(PageHead(section));

        if (section.Packages is not null)
        {
            if (_form.Packages is not { } manager)
            {
                _page.Children.Add(new TextBlock { Text = "This client offers no packages.", Foreground = DimInk, Margin = new Thickness(0, NativeTokens.Space16, 0, 0) });
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
                Margin = new Thickness(0, NativeTokens.Space8, 0, 0),
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

        if (section.Scope != SettingScope.App && !_form.HasSubject(section))
        {
            var noun = section.Scope == SettingScope.World ? "world" : "character";
            _page.Children.Add(new TextBlock { Text = $"There is no {noun} yet. Add one on its own screen.", Foreground = DimInk });
            ShownFields = [];
            return;
        }

        var visible = Searching
            ? section.Fields.Where(f => _matches.Any(m => ReferenceEquals(m.Field, f))).ToList()
            : section.Fields.ToList();
        if (visible.Count == 0)
        {
            _page.Children.Add(new TextBlock { Text = "No setting here matches the search.", Foreground = DimInk, Margin = new Thickness(0, NativeTokens.Space16, 0, 0) });
        }

        // A card per group, as the mockup draws its sections: the group's name heads it and its fields are rows inside,
        // a hairline between them.
        StackPanel? body = null;
        string? group = null;
        foreach (var field in visible)
        {
            if (body is null || !string.Equals(field.Group, group, StringComparison.Ordinal))
            {
                group = field.Group;
                body = new StackPanel();
                if (group is { } heading && !string.Equals(heading, field.Label, StringComparison.OrdinalIgnoreCase))
                {
                    body.Children.Add(new TextBlock
                    {
                        Text = heading,
                        FontSize = NativeTokens.CardTitle,
                        FontWeight = FontWeight.SemiBold,
                        Margin = new Thickness(0, NativeTokens.Space12, 0, 0),
                    });
                }

                _page.Children.Add(Card(body));
            }
            else
            {
                body.Children.Add(new Border { Height = NativeTokens.Hairline, Background = _tokens.Page.Brush(_tokens.Page.Rule) });
            }

            body.Children.Add(field switch
            {
                ListField { Id: BuiltInSettings.MetersField } meters => MeterTable(meters),
                ThemeField theme => ThemePicker(theme),
                _ => FieldRow(field),
            });
        }

        ShownFields = visible.Select(f => f.Id).ToList();
    }

    /// <summary>
    /// The page's heading: the section's title and, under it, what the page is showing — the screen it is in the
    /// terminal, or what the search matched with the way back to everything — and, for a scoped section, the way to its
    /// own cell screen for what is not a field (the lists of characters and trigger sets, adding and deleting).
    /// </summary>
    private StackPanel PageHead(SettingsSectionContribution section)
    {
        var head = new StackPanel { Spacing = NativeTokens.Space6 };
        head.Children.Add(new TextBlock { Text = section.Title, FontSize = NativeTokens.PageTitle, FontWeight = FontWeight.SemiBold });
        var line = new WrapPanel { Orientation = Orientation.Horizontal };
        if (Searching)
        {
            line.Children.Add(new TextBlock { Text = $"Showing settings that match “{_search.Text!.Trim()}”. ", FontSize = NativeTokens.Label, Foreground = DimInk, VerticalAlignment = VerticalAlignment.Center });
            var all = section.Fields.Count;
            var clear = LinkButton(all > 0 ? $"Show all {all}" : "Show all");
            AutomationProperties.SetName(clear, "Clear the search");
            clear.Click += (_, _) => _search.Text = string.Empty;
            line.Children.Add(clear);
        }
        else if (section.Key is { } key)
        {
            line.Children.Add(new TextBlock { Text = $"{key} in the client · changes apply as you make them", FontSize = NativeTokens.Label, Foreground = DimInk, VerticalAlignment = VerticalAlignment.Center });
        }

        if (section.Scope is SettingScope.World or SettingScope.Character && section.Key is { } screen)
        {
            var door = LinkButton($"Open the {screen} screen ↗");
            door.Margin = new Thickness(line.Children.Count > 0 ? NativeTokens.Space12 : 0, 0, 0, 0);
            ToolTip.SetTip(door, section.Scope == SettingScope.World
                ? "Adding and deleting worlds, their characters and trigger sets are on the cell screen"
                : "Adding and deleting characters is on the cell screen");
            AutomationProperties.SetName(door, $"Open the {screen} screen");
            var id = section.Id;
            door.Click += (_, _) => _form.OpenScreen(id);
            ScreenDoor = door;
            line.Children.Add(door);
        }

        if (line.Children.Count > 0)
        {
            head.Children.Add(line);
        }

        return head;
    }

    /// <summary>A card on the page: the card plane, a hairline edge, the card radius.</summary>
    private Border Card(Control body) => new()
    {
        Child = body,
        Background = _tokens.Page.Brush(_tokens.Page.Card),
        BorderBrush = _tokens.Page.Brush(_tokens.Page.Rule),
        BorderThickness = new Thickness(NativeTokens.Hairline),
        CornerRadius = new CornerRadius(NativeTokens.RadiusCard),
        Padding = new Thickness(NativeTokens.Space20, NativeTokens.Space6),
    };

    /// <summary>A button drawn as a link in the accent, underlined, as the mockup's "Reset to default" and "Show all".</summary>
    private Button LinkButton(string text)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, TextDecorations = TextDecorations.Underline, FontSize = NativeTokens.Body },
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = _tokens.Page.Brush(_tokens.Page.Link),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver" })
        {
            button.Resources[key] = Brushes.Transparent;
        }

        foreach (var key in new[] { "ButtonForegroundPointerOver", "ButtonForegroundPressed" })
        {
            button.Resources[key] = _tokens.Page.Brush(_tokens.Page.Link);
        }

        return button;
    }

    /// <summary>
    /// A scoped section's subject, in the header as the mockup's "Applies to": which world or character the fields are
    /// for, picked from every one there is.
    /// </summary>
    private Control SubjectRow(SettingsSectionContribution section)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space8 };
        var subjects = _form.Subjects(section.Scope);
        var noun = section.Scope == SettingScope.World ? "world" : "character";
        if (subjects.Count == 0)
        {
            row.Children.Add(new TextBlock { Text = $"No {noun} yet", VerticalAlignment = VerticalAlignment.Center, Foreground = DimInk, FontSize = NativeTokens.Label });
            return row;
        }

        var labels = subjects.Select(s => s.Label).ToList();
        var current = _form.Subject(section.Scope);
        var picker = new ComboBox { ItemsSource = labels, MinWidth = 180, MaxWidth = 280, SelectedItem = current?.Label, FontSize = NativeTokens.Label };
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
        row.Children.Add(new TextBlock { Text = section.Scope == SettingScope.World ? "Applies to world" : "Applies to character", VerticalAlignment = VerticalAlignment.Center, Foreground = DimInk, FontSize = NativeTokens.Label });
        row.Children.Add(picker);
        return row;
    }

    /// <summary>The theme field: a card per theme, its preview drawn from the client's model; a click uses it.</summary>
    private StackPanel ThemePicker(ThemeField field)
    {
        var outer = new StackPanel { Spacing = NativeTokens.Space6, Margin = new Thickness(0, NativeTokens.Space12, 0, NativeTokens.Space4) };
        var words = new StackPanel();
        words.Children.Add(LabelBlock(field.Label));
        if (field.Help is { } help)
        {
            words.Children.Add(HelpBlock(help));
        }

        outer.Children.Add(words);

        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, NativeTokens.Space6, 0, 0) };
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

    /// <summary>The accent the changed dot wears: the client's, as every native surface's, held on the page's planes.</summary>
    private SolidColorBrush Accent => _tokens.Page.Brush(_tokens.Page.Link);

    /// <summary>A quiet line's ink, held to the floor on the page's planes (an opacity would not be).</summary>
    private SolidColorBrush DimInk => _tokens.Page.Brush(_tokens.Page.Dim);

    /// <summary>
    /// A field's label, in the label weight, with every word of the search marked as the mockup marks "meter": the
    /// page's mark plane behind it and the mark ink on it.
    /// </summary>
    private TextBlock LabelBlock(string label)
    {
        var text = NativeKeys.Words(Capitalise(label));
        var block = new TextBlock { FontSize = NativeTokens.Label, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var words = Searching
            ? _search.Text!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
        var marks = new bool[text.Length];
        foreach (var word in words)
        {
            for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(word, at + word.Length, StringComparison.OrdinalIgnoreCase))
            {
                Array.Fill(marks, true, at, word.Length);
            }
        }

        if (!marks.Contains(true))
        {
            block.Text = text;
            return block;
        }

        var page = _tokens.Page;
        for (var i = 0; i < text.Length;)
        {
            var marked = marks[i];
            var j = i;
            while (j < text.Length && marks[j] == marked)
            {
                j++;
            }

            var run = new Run(text[i..j]);
            if (marked)
            {
                run.Background = page.Brush(page.Mark);
                run.Foreground = page.Brush(page.MarkInk);
            }

            block.Inlines!.Add(run);
            i = j;
        }

        return block;
    }

    /// <summary>A field's help line under its label: quiet, wrapped.</summary>
    private TextBlock HelpBlock(string help) =>
        new() { Text = NativeKeys.Words(Capitalise(help)), FontSize = NativeTokens.Body, Foreground = DimInk, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, NativeTokens.Space2, 0, 0) };

    /// <summary>
    /// One field, as a row of its group's card: the label (with the changed mark beside it) and its help on the left;
    /// the reset link and the control on the right.
    /// </summary>
    private Grid FieldRow(SettingsField field)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, NativeTokens.Space12), MinHeight = 32 };

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, NativeTokens.Space16, 0) };
        var (changed, reset) = ChangedMark(field);
        var top = new WrapPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(LabelBlock(field.Label));
        top.Children.Add(changed);
        words.Children.Add(top);
        if (field.Help is { } help)
        {
            words.Children.Add(HelpBlock(help));
        }

        // The note the cell F5 draws beside the value (join wraps' recommendation), from the same sentence, in the
        // cells' caution amber; gone once the field has been changed.
        var note = new TextBlock { FontSize = NativeTokens.Body, Foreground = _tokens.Brush(_tokens.Notice), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, NativeTokens.Space2, 0, 0) };
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

        reset.Margin = new Thickness(0, 0, NativeTokens.Space16, 0);
        Grid.SetColumn(reset, 1);
        grid.Children.Add(reset);

        var control = ControlFor(field);
        control.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetName(control, field.Label);
        Grid.SetColumn(control, 2);
        grid.Children.Add(control);
        _controls[field.Id] = control;
        return grid;
    }

    /// <summary>
    /// The changed mark for <paramref name="field"/> — a dot and the word "Changed", beside the label — and its reset
    /// link, both shown only while the field differs from its default.
    /// </summary>
    private (Control Changed, Button Reset) ChangedMark(SettingsField field)
    {
        var mark = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space6, Margin = new Thickness(NativeTokens.Space10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        mark.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Accent, VerticalAlignment = VerticalAlignment.Center });
        mark.Children.Add(new TextBlock { Text = "Changed", FontSize = NativeTokens.Body, Foreground = DimInk, VerticalAlignment = VerticalAlignment.Center });
        ToolTip.SetTip(mark, "Changed from its default");
        var reset = LinkButton("Reset to default");
        ToolTip.SetTip(reset, $"Back to the default: {SettingsForm.DefaultText(field)}");
        AutomationProperties.SetName(reset, $"Reset {field.Label}");
        reset.Click += (_, _) => _form.Reset(field);
        _resets[field.Id] = reset;

        void Refresh()
        {
            var changed = !_form.IsDefault(field);
            mark.IsVisible = changed;
            reset.IsVisible = changed;
        }

        Refresh();
        _refreshers.Add(Refresh);
        return (mark, reset);
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
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space8, Tag = name };
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
        var outer = new StackPanel { Spacing = NativeTokens.Space10, Margin = new Thickness(0, NativeTokens.Space12, 0, NativeTokens.Space4) };
        var page = _tokens.Page;

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var (changed, reset) = ChangedMark(field);
        var heading = new WrapPanel { Orientation = Orientation.Horizontal };
        heading.Children.Add(LabelBlock(field.Label));
        heading.Children.Add(changed);
        top.Children.Add(heading);
        Grid.SetColumn(reset, 1);
        top.Children.Add(reset);
        outer.Children.Add(top);
        if (field.Help is { } help)
        {
            outer.Children.Add(HelpBlock(help));
        }

        var received = _form.Received();
        var paths = received.Select(r => r.Path).ToList();
        var meters = _form.Meters(field);
        _meterRows = meters.Count;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*,*,130,120,32"), ColumnSpacing = 8, RowSpacing = 8 };
        MeterGrid = grid;
        _controls[field.Id] = grid;
        string[] headings = ["Label", "Value path", "Maximum path", "Colour", "Live value"];
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < headings.Length; c++)
        {
            var title = new TextBlock { Text = headings[c], FontSize = NativeTokens.Body, Foreground = DimInk };
            Grid.SetColumn(title, c);
            grid.Children.Add(title);
        }

        AutoCompleteBox? lastPath = null;
        void Commit() => _form.SetMeters(field, meters);

        for (var i = 0; i < meters.Count; i++)
        {
            var meter = meters[i];
            var row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = new TextBox { Text = meter.Label, MaxLength = StatusMeter.MaxLabelLength, FontSize = NativeTokens.Label };
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

            var now = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = Mono, FontSize = NativeTokens.Code };
            _meterNow.Add((meter, now));

            var remove = new Button
            {
                Content = "✕",
                Padding = new Thickness(NativeTokens.Space8, NativeTokens.Space4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = DimInk,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            ToolTip.SetTip(remove, $"Remove {meter.Label}");
            AutomationProperties.SetName(remove, $"Remove meter {meter.Label}");
            var index = i;
            remove.Click += (_, _) =>
            {
                meters.RemoveAt(index);
                Commit();
            };

            var colour = MeterColourBox(meter, row, Commit);
            Control[] cells = [label, value, max, colour, now, remove];
            for (var c = 0; c < cells.Length; c++)
            {
                Grid.SetRow(cells[c], row);
                Grid.SetColumn(cells[c], c);
                grid.Children.Add(cells[c]);
            }
        }

        outer.Children.Add(grid);

        // What this connection has received, to pick a path from: a click puts it in the path box last used. Drawn as the
        // mockup's drop-down panel, under the path columns.
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = "Paths this connection has received", FontSize = NativeTokens.Body, Foreground = DimInk, Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space4, NativeTokens.Space12, NativeTokens.Space4) });
        if (received.Count == 0)
        {
            box.Children.Add(new TextBlock { Text = "Nothing yet — a meter shows once its value arrives.", FontSize = NativeTokens.Body, Foreground = DimInk, Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space4, NativeTokens.Space12, NativeTokens.Space6) });
        }

        foreach (var item in received.Take(40))
        {
            var line = new TextBlock { FontFamily = Mono, FontSize = NativeTokens.Code, TextTrimming = TextTrimming.CharacterEllipsis };
            line.Inlines!.Add(new Run(item.Path) { Foreground = page.Brush(page.Text) });
            line.Inlines.Add(new Run($"  = {item.Value}") { Foreground = DimInk });
            var pick = new Button
            {
                Content = line,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(NativeTokens.Space12, NativeTokens.Space6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
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

        // The mockup's drop-down shows a handful of rows; a connection that has sent sixty paths scrolls inside the panel
        // rather than pushing "+ Add a meter" and the next card a screen down.
        var list = new ScrollViewer
        {
            Content = box,
            MaxHeight = MeterPathsMaxHeight,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        outer.Children.Add(new Border
        {
            Child = list,
            Background = page.Brush(page.Popup),
            BorderThickness = new Thickness(NativeTokens.Hairline),
            BorderBrush = page.Brush(page.FocusEdge),
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            Padding = new Thickness(0, NativeTokens.Space8),
            Margin = new Thickness(MeterPathsIndent, 0, 0, 0),
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        var add = new Button { Content = "+ Add a meter", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(NativeTokens.Space12, NativeTokens.Space6), FontSize = NativeTokens.Label };
        AutomationProperties.SetName(add, "Add a meter");
        add.Click += (_, _) =>
        {
            meters.Add(new StatusMeter { Label = "new" });
            Commit();
        };
        outer.Children.Add(add);

        Tick();
        return outer;
    }

    /// <summary>
    /// A meter's colour: <c>auto</c> (its label's default) or a highlight palette name, each item a swatch of the colour the
    /// meter is drawn in and its name. A colour typed in the cells that no name covers (<c>#rrggbb</c>) is listed too, so
    /// opening the page shows it. A choice is stored through <see cref="SettingsForm.SetMeters"/>, the funnel.
    /// </summary>
    private ComboBox MeterColourBox(StatusMeter meter, int row, Action commit)
    {
        var current = MeterColours.Label(meter);
        var names = MeterColours.Choices.Contains(current, StringComparer.OrdinalIgnoreCase)
            ? MeterColours.Choices.ToList()
            : [.. MeterColours.Choices, current];
        var box = new ComboBox
        {
            ItemsSource = names.Select(n => MeterColourItem(n, meter)).ToList(),
            SelectedIndex = Math.Max(0, names.FindIndex(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase))),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = NativeTokens.Label,
        };
        AutomationProperties.SetName(box, $"meter {row} colour");
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex < 0 || box.SelectedIndex >= names.Count)
            {
                return;
            }

            var picked = names[box.SelectedIndex];
            if (!string.Equals(picked, current, StringComparison.OrdinalIgnoreCase))
            {
                meter.Colour = MeterColours.TryNormalise(picked, out var stored) ? stored : null;
                commit();
            }
        };
        return box;
    }

    /// <summary>One colour in the meter colour box: a swatch (auto's is the label's default) and the name.</summary>
    private StackPanel MeterColourItem(string name, StatusMeter meter)
    {
        var hex = name == MeterColours.Auto ? MeterColours.Hex(StatusMeter.DefaultColour(meter.Label)) : MeterColours.Hex(name);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space8 };
        panel.Children.Add(new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(NativeTokens.RadiusS),
            Background = hex is not null && Color.TryParse(hex, out var colour) ? new SolidColorBrush(colour) : Brushes.Transparent,
            BorderThickness = hex is null ? new Thickness(NativeTokens.Hairline) : default,
            BorderBrush = DimInk,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, Foreground = _tokens.Page.Brush(_tokens.Page.Text) });
        return panel;
    }

    /// <summary>The page's monospace face, for paths and values (the mockup's game-text face).</summary>
    private static FontFamily Mono => NativeTokens.MonoFont;

    private static AutoCompleteBox PathBox(string? text, IReadOnlyList<string> paths, string name, Action<string?> set, Action commit)
    {
        var box = new AutoCompleteBox
        {
            Text = text ?? string.Empty,
            ItemsSource = paths,
            FontFamily = Mono,
            FontSize = NativeTokens.Code,
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
            now.Foreground = text is null ? DimInk : _tokens.Page.Brush(_tokens.Page.Text);
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
