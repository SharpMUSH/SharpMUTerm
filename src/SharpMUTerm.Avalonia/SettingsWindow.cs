using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
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
internal sealed partial class SettingsWindow : Window
{
    /// <summary>
    /// How far the meter table's received-paths box sits in: under the value path column, past the table's 4 px inset,
    /// the label column and the gap after it.
    /// </summary>
    private const double MeterPathsIndent = NativeTokens.Space4 + MeterLabelWidth + NativeTokens.Space8;

    /// <summary>The meter table's label column (the mockup's 120 px).</summary>
    private const double MeterLabelWidth = 120;

    /// <summary>The received-paths panel's width (the mockup's 360 px).</summary>
    private const double MeterPathsWidth = 360;

    /// <summary>The header's search text (the mockup's 480 px; its padding, glass and edge are outside it).</summary>
    private const double SearchTextWidth = 480;

    /// <summary>
    /// The navigation column's width inside its padding (the mockup's 232 px). The column is that, its padding and its
    /// rule, less the window's own edge, so the page starts where the mockup's does.
    /// </summary>
    private const double NavContentWidth = 232;

    /// <summary>The received-paths panel's height before it scrolls: its 24 px heading and five and a half 28 px paths, so the half row says there is more.</summary>
    private const double MeterPathsMaxHeight = 178;

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
    private readonly Panel _layers;
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
        NativeTokens.ApplyTextRendering(this);
        AppIcon.Apply(this);
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
        _searchCount = new TextBlock { FontSize = NativeTokens.PageSmall, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NativeTokens.Space8, 0, 0, 0) };

        // The mockup's glass: a 24-unit icon (a circle of radius 7 at 11,11 and a handle to 20,20, stroke 2) drawn at
        // 14 px, so a small lens and a short handle rather than a lens filling the box.
        _searchGlass = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M6.42,2.33 A4.08,4.08 0 1 1 6.42,10.5 A4.08,4.08 0 1 1 6.42,2.33 M11.67,11.67 L9.63,9.63"),
            StrokeThickness = 1.17,
            StrokeLineCap = PenLineCap.Round,
            Width = 14,
            Height = 14,
            Stretch = Stretch.None,
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
            Margin = new Thickness(NativeTokens.Space16, 0, 0, 0),
        };
        _searchFrame.PointerPressed += (_, _) => _search.Focus();

        _scope = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NativeTokens.Space16, 0, 0, 0) };
        // The search column is the mockup's box: its gap after the title, its 480 px (glass, text and count), the padding
        // round them and its edge. The left inset is the mockup's 20 px less the window's own edge.
        var searchColumn = NativeTokens.Space16 + SearchTextWidth + (2 * NativeTokens.Space12) + (2 * NativeTokens.Hairline);
        var header = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(3, GridUnitType.Star) { MaxWidth = searchColumn }, new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto)], Margin = new Thickness(NativeTokens.Space20 - NativeTokens.Hairline, 0, NativeTokens.Space8, 0), Background = Brushes.Transparent };
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
        _footerLeft = new TextBlock { Text = FooterSentence, FontSize = NativeTokens.PageSmall, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _footerRight = new TextBlock { Text = FooterKeys, FontSize = NativeTokens.PageSmall, VerticalAlignment = VerticalAlignment.Center };
        var footerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(NativeTokens.Space20 - NativeTokens.Hairline, 0) };
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
            FontSize = NativeTokens.PageSmall,
            LineHeight = 18,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(NativeTokens.Space10, NativeTokens.Space8),
        };
        var navDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_navNote, Dock.Bottom);
        navDock.Children.Add(_navNote);
        navDock.Children.Add(_sections);
        _nav = new Border { Width = NavContentWidth + (2 * NativeTokens.Space10), BorderThickness = new Thickness(0, 0, NativeTokens.Hairline, 0), Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space12), Child = navDock };
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

            if (WorldsGrid is { } worlds)
            {
                worlds.Height = ListEditorHeight();
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
        _layers = WindowChrome.WithResizeBands(this, _frame);
        Content = _layers;

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
        AddHandler(KeyDownEvent, OnUndoKey, RoutingStrategies.Tunnel);

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
        tokens.ApplyFocusRing(this);
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
    /// <remarks>
    /// Measured off the page's own margins, spacing and the title as laid out, so the editor ends at the page's bottom
    /// margin and the page itself does not scroll (a fixed allowance left it a few pixels too tall, and the page grew a
    /// scroll bar beside the editor's own).
    /// </remarks>
    private double ListEditorHeight()
    {
        var head = _page.Children.Count > 0 ? _page.Children[0] : null;
        if (head is not null && head.DesiredSize.Height <= 0)
        {
            head.Measure(new Size(Math.Max(0, _scroller.Bounds.Width - _page.Margin.Left - _page.Margin.Right), double.PositiveInfinity));
        }

        var above = _page.Margin.Top + _page.Margin.Bottom + _page.Spacing + NativeTokens.Space8 + (head?.DesiredSize.Height ?? 0);
        return Math.Max(360, Math.Floor(_scroller.Bounds.Height - above));
    }

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
        var first = form.Section(form.Initial) is { } initial && SettingsForm.IsDeclared(initial)
            ? initial.Id
            : form.Sections.FirstOrDefault(SettingsForm.IsDeclared)?.Id;
        if (first is not null)
        {
            // F9 opens the worlds page on the character the form chose; F5 on its world.
            SeedWorldsSelection(first);
        }

        Choose(first);
    }

    // ── the section list ─────────────────────────────────────────────────────────────────────────

    private IEnumerable<ListBoxItem> Items => _sections.ItemsSource as IEnumerable<ListBoxItem> ?? [];

    private void BuildSectionList()
    {
        _badges.Clear();
        var items = new List<ListBoxItem>();
        foreach (var section in _form.Sections)
        {
            if (!string.Equals(SettingsForm.PageOf(section.Id), section.Id, StringComparison.Ordinal))
            {
                // Folded into another page (F9's characters are on the worlds page), so it has no row of its own.
                continue;
            }

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
            AutomationProperties.SetName(item, section.Title);
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
            if (!_badges.ContainsKey(section.Id))
            {
                continue;
            }

            // A page counts the matches of every section folded into it.
            var count = _form.Sections
                .Where(s => string.Equals(SettingsForm.PageOf(s.Id), section.Id, StringComparison.Ordinal))
                .Sum(s => counts.GetValueOrDefault(s.Id));
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
        id = id is null ? null : SettingsForm.PageOf(id);
        _shown = id;
        _form.Showing = id;
        _form.HistoryNote = null;
        Select(id);
        BuildPage();
        ShowNote();
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
            && !_matches.Any(m => m.Field is not null && SettingsForm.PageOf(m.Section.Id) == _shown)
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
        ListEditor = null;
        PackagePage = null;
        MeterGrid = null;
        _meterRows = -1;
        WorldsGrid = null;
        _worldTree = null;
        if (_shown is null || _form.Section(_shown) is not { } section)
        {
            _scope.Content = null;
            ShownFields = [];
            return;
        }

        if (IsWorldsPage(section))
        {
            // The tree on the page is the subject picker, and adding and deleting are on it: no picker, no door.
            _scope.Content = null;
            _page.Children.Add(PageHead(section));
            BuildWorldsPage(section);
            return;
        }

        _scope.Content = null;
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
                editor.Refresh(_refreshFocused);
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
        // a hairline between them. A card holding one field draws no heading while searching (the mockup's result cards:
        // the field's own label says what it is), nor when the group is named after its field.
        StackPanel? body = null;
        string? group = null;
        var headed = false;
        for (var i = 0; i < visible.Count; i++)
        {
            var field = visible[i];
            if (body is null || !string.Equals(field.Group, group, StringComparison.Ordinal))
            {
                group = field.Group;
                body = new StackPanel();
                var alone = (i + 1 == visible.Count || !string.Equals(visible[i + 1].Group, group, StringComparison.Ordinal))
                    && (i == 0 || !string.Equals(visible[i - 1].Group, group, StringComparison.Ordinal));
                headed = group is { } named && !string.Equals(named, field.Label, StringComparison.OrdinalIgnoreCase) && !(alone && Searching);
                if (headed && group is { } heading)
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
                ListField { Id: BuiltInSettings.MetersField } meters => MeterTable(meters, headsCard: !headed),
                ThemeField theme => ThemePicker(theme),
                _ => FieldRow(field),
            });
        }

        ShownFields = visible.Select(f => f.Id).ToList();
    }

    /// <summary>
    /// The page's heading: the section's title and, under it, what the page is showing — the screen it is in the
    /// terminal, or what the search matched with the way back to everything.
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
    private Button LinkButton(string text, double size = NativeTokens.Body)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, TextDecorations = TextDecorations.Underline, FontSize = size },
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
    private TextBlock LabelBlock(string label, double size = NativeTokens.Label)
    {
        var text = NativeKeys.Words(Capitalise(label));
        var block = new TextBlock { FontSize = size, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
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
    private TextBlock HelpBlock(string help, double size = NativeTokens.Body) =>
        new() { Text = NativeKeys.Words(Capitalise(help)), FontSize = size, Foreground = DimInk, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, NativeTokens.Space2, 0, 0) };

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
            words.Children.Add(HelpBlock(help, NativeTokens.PageSmall));
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
        mark.Children.Add(new TextBlock { Text = "Changed", FontSize = NativeTokens.PageSmall, Foreground = DimInk, VerticalAlignment = VerticalAlignment.Center });
        ToolTip.SetTip(mark, "Changed from its default");
        var reset = LinkButton("Reset to default", NativeTokens.PageSmall);
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
                    if (Rewritable(box))
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
                var box = new TextBox { Text = _form.Text(text), Width = _shown == BuiltInSettings.World.Id ? WorldsTextBoxWidth : 260 };
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
                    if (Rewritable(box))
                    {
                        box.Text = _form.Text(text);
                    }
                });
                if (text.Rule == TextRule.Image)
                {
                    // A picture is usually a file on this machine: the window's own picker fills the box, and the box's
                    // own rule (TextRules.Check) decides, exactly as if the path had been typed.
                    var choose = new Button { Content = "Choose…", VerticalAlignment = VerticalAlignment.Center };
                    AutomationProperties.SetName(choose, $"Choose a picture for {field.Label}");
                    ToolTip.SetTip(choose, "Pick an image file");
                    choose.Click += async (_, _) =>
                    {
                        var (path, pickProblem) = await PickPictureAsync();
                        if (path is null)
                        {
                            if (pickProblem is not null)
                            {
                                ToolTip.SetTip(box, pickProblem);
                                ToolTip.SetIsOpen(box, true);
                            }

                            return;
                        }

                        // Every picture goes through the crop step. One this window cannot open is not set: the box
                        // keeps its value and says why, rather than taking a path the sidebar could never draw.
                        var (bitmap, problem) = OpenPicture(path);
                        if (bitmap is null)
                        {
                            ToolTip.SetTip(box, problem);
                            ToolTip.SetIsOpen(box, true);
                            return;
                        }

                        using (bitmap)
                        {
                            if (await CropAsync(bitmap) is not { } crop)
                            {
                                return;
                            }

                            _form.PictureCropped?.Invoke(path, crop);
                        }

                        box.Text = path;
                        Commit();
                    };
                    box.Width -= 96;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space8 };
                    row.Children.Add(box);
                    row.Children.Add(choose);
                    return row;
                }

                return box;
            }

            default:
                return new TextBlock { Text = "set on its own screen", Foreground = DimInk };
        }
    }

    /// <summary>The crop step open over the page, if one is.</summary>
    internal AvatarCropView? CropStep { get; private set; }

    /// <summary>
    /// Shows the crop step for <paramref name="picture"/> over the page and waits for it: the square chosen, or null when
    /// it was cancelled. The keyboard goes to the picture while it is up and back to what had it after.
    /// </summary>
    internal async Task<SharpMUTerm.Core.Avatars.AvatarCrop?> CropAsync(Bitmap picture)
    {
        CropStep?.Finish(null);
        var had = FocusManager?.GetFocusedElement() as Control;
        var step = new AvatarCropView(picture, _tokens);
        CropStep = step;
        _layers.Children.Add(step);
        step.Stage.Focus();
        try
        {
            return await step.Result;
        }
        finally
        {
            _layers.Children.Remove(step);
            if (ReferenceEquals(CropStep, step))
            {
                CropStep = null;
            }

            had?.Focus();
        }
    }

    /// <summary>The longest side a picture is decoded at for the crop step; the crop is in shares, so this loses nothing.</summary>
    internal const int CropDecodeSide = 2048;

    /// <summary>
    /// The file at <paramref name="path"/> as a bitmap no larger than <see cref="CropDecodeSide"/> a side, or null and why:
    /// refused by the field's rule, too large, or not a picture this window can decode.
    /// </summary>
    internal static (Bitmap? Bitmap, string? Problem) OpenPicture(string path)
    {
        if (SharpMUTerm.Core.Avatars.AvatarSource.Problem(path) is { } problem)
        {
            return (null, problem);
        }

        try
        {
            int width, height;
            using (var codec = SkiaSharp.SKCodec.Create(path))
            {
                if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                {
                    return (null, "that file is not a picture this client can read");
                }

                (width, height) = (codec.Info.Width, codec.Info.Height);
            }

            using var stream = File.OpenRead(path);
            var bitmap = Math.Max(width, height) <= CropDecodeSide
                ? new Bitmap(stream)
                : width >= height
                    ? Bitmap.DecodeToWidth(stream, CropDecodeSide, BitmapInterpolationMode.HighQuality)
                    : Bitmap.DecodeToHeight(stream, CropDecodeSide, BitmapInterpolationMode.HighQuality);
            return (bitmap, null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The decoder's own failure for bytes that are not a picture is not one documented type.
            return (null, "that file is not a picture this client can read");
        }
    }

    /// <summary>
    /// An image file picked with the window's own open dialog; no path and no problem when the dialog was cancelled, and
    /// a problem when there is no dialog or the file picked has no path on this machine (a portal or a cloud file).
    /// </summary>
    private async Task<(string? Path, string? Problem)> PickPictureAsync()
    {
        if (StorageProvider is not { CanOpen: true } storage)
        {
            return (null, "this window has no file picker here: type the picture's full path instead");
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a picture",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Pictures")
                    {
                        Patterns = [.. SharpMUTerm.Core.Avatars.AvatarSource.Extensions.Select(e => "*" + e)],
                    },
                ],
            });
            if (files.Count == 0)
            {
                return (null, null);
            }

            return files[0].TryGetLocalPath() is { } local
                ? (local, null)
                : (null, "that file has no path on this computer: save a copy locally and choose that");
        }
        catch (InvalidOperationException)
        {
            return (null, "the file picker could not open: type the picture's full path instead");
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

    /// <summary>
    /// The meter table, laid out as the mockup's: the label (the card's heading when no group heading is drawn above it)
    /// with its changed mark and reset link, the help, a 4 px-inset grid of rows 12 px apart, the received paths under the
    /// value path column and the add button, each 12 px from the next.
    /// </summary>
    private StackPanel MeterTable(ListField field, bool headsCard = false)
    {
        var outer = new StackPanel { Spacing = NativeTokens.Space12, Margin = new Thickness(0, NativeTokens.Space12, 0, NativeTokens.Space12) };
        var page = _tokens.Page;

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var (changed, reset) = ChangedMark(field);
        var heading = new WrapPanel { Orientation = Orientation.Horizontal };
        heading.Children.Add(LabelBlock(field.Label, headsCard ? NativeTokens.CardTitle : NativeTokens.Label));
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

        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(MeterLabelWidth, GridUnitType.Pixel), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(130, GridUnitType.Pixel),
                new ColumnDefinition(120, GridUnitType.Pixel), new ColumnDefinition(32, GridUnitType.Pixel),
            ],
            ColumnSpacing = NativeTokens.Space8,
            RowSpacing = NativeTokens.Space12,
            Margin = new Thickness(NativeTokens.Space4, 0),
        };
        MeterGrid = grid;
        _controls[field.Id] = grid;
        string[] headings = ["Label", "Value path", "Maximum path", "Colour", "Live value"];
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < headings.Length; c++)
        {
            var title = new TextBlock { Text = headings[c], FontSize = NativeTokens.PageSmall, Foreground = DimInk };
            Grid.SetColumn(title, c);
            grid.Children.Add(title);
        }

        AutoCompleteBox? lastPath = null;
        void Commit() => _form.SetMeters(field, meters);

        // The received paths the box being edited already reads are marked in the panel under it, as the mockup marks
        // Char.Vitals.mv while the mv value path has focus.
        var picks = new List<(string Path, Button Row)>();
        void PointAt(AutoCompleteBox box)
        {
            lastPath = box;
            var reads = (box.Text ?? string.Empty).Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var (path, pickRow) in picks)
            {
                pickRow.Background = reads.Contains(path, StringComparer.Ordinal) ? page.Brush(page.PopupSelected) : Brushes.Transparent;
            }
        }

        for (var i = 0; i < meters.Count; i++)
        {
            var meter = meters[i];
            var row = i + 1;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var label = new TextBox { Text = meter.Label, MaxLength = StatusMeter.MaxLabelLength, FontSize = NativeTokens.Label, Padding = new Thickness(NativeTokens.Space8, NativeTokens.Space6), VerticalAlignment = VerticalAlignment.Center };
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
            value.GotFocus += (_, _) => PointAt(value);
            max.GotFocus += (_, _) => PointAt(max);

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
        box.Children.Add(new TextBlock { Text = "Paths this connection has received", FontSize = NativeTokens.PageSmall, Foreground = DimInk, Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space4) });
        if (received.Count == 0)
        {
            box.Children.Add(new TextBlock { Text = "Nothing yet — a meter shows once its value arrives.", FontSize = NativeTokens.PageSmall, Foreground = DimInk, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space4, NativeTokens.Space12, NativeTokens.Space6) });
        }

        foreach (var item in received.Take(40))
        {
            var line = new TextBlock { FontFamily = Mono, FontSize = NativeTokens.Code, TextTrimming = TextTrimming.CharacterEllipsis };
            line.Inlines!.Add(new Run(item.Path) { Foreground = page.Brush(page.Text) });
            line.Inlines.Add(new Run($" = {item.Value}") { Foreground = DimInk });
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
            picks.Add((item.Path, pick));
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
            Width = MeterPathsWidth,
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
            Padding = new Thickness(NativeTokens.Space8),
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
            now.Foreground = text is null ? DimInk : _tokens.Page.Brush(NowInk(meter, text));
        }
    }

    /// <summary>
    /// The ink a meter's live value is drawn in, as the mockup's are: the warning colour while it is under
    /// <see cref="VitalsModel.LowShare"/> of its maximum (the gauges' rule), else the meter's own colour held to the floor
    /// on the card, else the page's text.
    /// </summary>
    private Color NowInk(StatusMeter meter, string text)
    {
        var page = _tokens.Page;
        var parts = text.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var max)
            && max > 0 && value / max < VitalsModel.LowShare)
        {
            // The low red every meter host draws (WorkspacePalette.Low), held on the card.
            var card = new SharpMUTerm.Core.Text.Rgb(page.Card.R, page.Card.G, page.Card.B);
            var low = SharpMUTerm.Tui.WorkspacePalette.Low(_form.Theme?.Invoke() ?? SharpMUTerm.Core.Theming.ThemeLibrary.Dark(), card);
            return page.OnCard(Color.FromRgb(low.R, low.G, low.B));
        }

        return MeterColours.For(meter) is { } hex && Color.TryParse(hex, out var own) ? page.OnCard(own) : page.Text;
    }

    // ── changes landing ──────────────────────────────────────────────────────────────────────────

    /// <summary>Raised on the client's thread once a change has landed; the page catches up on this one.</summary>
    private void OnChanged() => Dispatcher.UIThread.Post(() =>
    {
        // A change landed, so whatever the focused box held has been committed (or replaced by an undo): its own typing
        // is no longer its to undo, and Ctrl+Z from here is the section's. After an undo or redo taken from that box the
        // box is refreshed too, focused or not, or it would go on showing the value the step took away.
        var typedIn = FocusManager?.GetFocusedElement() as TextBox;
        _refreshFocused = _steppedFromBox;
        _steppedFromBox = false;
        try
        {
            Refresh();
        }
        finally
        {
            _refreshFocused = false;
        }

        if (typedIn is not null)
        {
            ForgetTyping(typedIn);
        }

        ShowNote();
    });

    /// <summary>Set by a section undo or redo taken while a text box had focus, until the change it makes lands.</summary>
    private bool _steppedFromBox;

    /// <summary>True while a refresh should rewrite a focused control too (the change is an undo or redo, not typing).</summary>
    private bool _refreshFocused;

    /// <summary>Whether a refresh may rewrite <paramref name="control"/>: not while it is being typed in, unless the change is a history step.</summary>
    private bool Rewritable(Control control) => _refreshFocused || !control.IsKeyboardFocusWithin;

    /// <summary>Empties <paramref name="box"/>'s own undo history (Avalonia clears it when undo is switched off).</summary>
    private static void ForgetTyping(TextBox box)
    {
        box.IsUndoEnabled = false;
        box.IsUndoEnabled = true;
    }

    /// <summary>The footer's sentence while nothing has been undone.</summary>
    internal const string FooterSentence = "Changes apply to connected sessions as you make them.";

    /// <summary>The footer's keys: the mockup's <c>Undo Ctrl+Z</c>, then redo, search and the way out.</summary>
    internal const string FooterKeys = "Undo Ctrl+Z · Redo Ctrl+Shift+Z · Search Ctrl+F · Esc closes";

    /// <summary>What the footer says on the left now: the last undo's sentence, else the standing one.</summary>
    public string FooterNote => _footerLeft.Text ?? string.Empty;

    /// <summary>
    /// Puts what the last undo or redo said in the footer (<c>Undid port.</c>, <c>Nothing to undo in Timers.</c>) in place
    /// of the standing sentence, until the next change or section.
    /// </summary>
    private void ShowNote()
    {
        var note = _form.HistoryNote;
        _footerLeft.Text = note ?? FooterSentence;
        _footerLeft.FontWeight = note is null ? FontWeight.Normal : FontWeight.SemiBold;
        _footerLeft.Foreground = note is null ? _tokens.Page.Brush(_tokens.Page.Dim) : _tokens.Page.Brush(_tokens.Page.Text);
    }

    /// <summary>
    /// Ctrl+Z / Ctrl+Shift+Z (or Ctrl+Y) on the page: the section's committed edits, through the client. A text box being
    /// typed in keeps the chords while it has typing of its own not yet committed, as any editor does; a box holding the
    /// value the page put there, or typing already committed, hands them to the section.
    /// </summary>
    private void OnUndoKey(object? sender, KeyEventArgs e)
    {
        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0 && (e.KeyModifiers & KeyModifiers.Alt) == 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var undo = ctrl && !shift && e.Key == Key.Z;
        var redo = ctrl && ((shift && e.Key == Key.Z) || (!shift && e.Key == Key.Y));
        if ((!undo && !redo) || _shown is not { } section)
        {
            return;
        }

        // A box's own history holds only typing not yet committed (OnChanged empties it when a change lands), so while it
        // has some the chord is the box's, and otherwise the section's.
        var focused = FocusManager?.GetFocusedElement() as TextBox;
        if (focused is not null && (undo ? focused.CanUndo : focused.CanRedo))
        {
            return;
        }

        e.Handled = redo ? _form.Redo(section) : _form.Undo(section);
        _steppedFromBox |= e.Handled && focused is not null;
    }

    /// <summary>Undoes the last edit on the section shown, as Ctrl+Z does — for a test.</summary>
    public bool UndoNow() => _shown is { } section && _form.Undo(section);

    /// <summary>Redoes the last undone edit on the section shown, as Ctrl+Shift+Z does — for a test.</summary>
    public bool RedoNow() => _shown is { } section && _form.Redo(section);

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
