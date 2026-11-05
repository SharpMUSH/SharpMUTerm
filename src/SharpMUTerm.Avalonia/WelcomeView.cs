using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Core.WorldIndex;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's first-run welcome as one page: a heading, then two cards side by side — <b>Find a world</b> (a search box
/// and mu*index's rows, asked only when the player opens the list, each with an Add that does what a pick in Browse worlds
/// does: fills in the connect form) and <b>Connect directly</b> (host, port, character, TLS, Connect, and the
/// <c>/connect</c> line it is the same as) — then the starting layout as two cards. Everything it says is
/// <see cref="WelcomeModel"/>'s and <see cref="WorldBrowseModel"/>'s; every button ends in the session: a choice and a
/// layout in <c>RunWelcomeAction</c>/<c>SetWelcomeLayout</c>, the form in <c>ConnectFromWelcome</c> (through
/// <c>/connect</c>'s own path, so a refusal comes back as the model's problem and the page stays up), the list in
/// <c>FindWorldsInWelcome</c> and a row's Add in <c>PickWorld</c>. ⏎ in the form connects, ⏎ in the search asks, Esc
/// closes. The cells keep their menu (<c>WelcomeSurface</c>): a page this wide does not fit a terminal.
/// </summary>
internal sealed class WelcomeView : DockPanel, IModalView
{
    /// <summary>The most rows the find card shows at once; the rest scroll.</summary>
    private const int ShownRows = 3;

    private readonly WelcomeSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly TextBox _host;
    private readonly TextBox _port;
    private readonly CheckBox _tls;
    private readonly TextBox _character;
    private readonly CheckBox _passwordLater;
    private readonly TextBlock _command;
    private readonly TextBlock _problem;
    private readonly TextBox _search;
    private readonly StackPanel _findBody = new() { Spacing = NativeTokens.Space8 };
    private readonly TextBlock _findNote;
    private readonly TextBlock _layoutTag;
    private readonly TextBlock _layoutReason;
    private readonly List<(WelcomeLayout Layout, Border Card)> _layoutCards = [];
    private readonly List<Border> _worldRows = [];
    private readonly List<Border> _addressRows = [];
    private readonly TextBlock _footer;
    private readonly StackPanel _page;
    private readonly TextBlock _pageIntro;
    private readonly StackPanel _layoutHeading;
    private readonly WrapPanel _others;
    private readonly Border _findCard;
    private readonly Border _connectCard;
    private readonly ScrollViewer _scroll;

    /// <summary>Whether the page is laid out tight, because the window is too short for it at the mockup's spacing.</summary>
    private bool _compact;
    private WelcomeModel _model;
    private WorldBrowseSession? _worlds;
    private WelcomeModel? _filledFrom;
    private WorldGame? _choiceDrawn;
    private int _address;

    /// <summary>The world row Enter adds, as an index into the rows the find card shows (<see cref="_shown"/>).</summary>
    private int _worldSelected;

    /// <summary>Whether an address row had the keyboard when the find card was last rebuilt, so the new row gets it back.</summary>
    private bool _addressHadFocus;

    /// <summary>The list's index of each row the find card shows, in the order shown.</summary>
    private readonly List<int> _shown = [];

    /// <summary>The intro the form shows once Browse worlds filled it in: which world, as listed, and the encoding it sets.</summary>
    private readonly TextBlock _formIntro;

    public WelcomeView(WelcomeSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        _model = session.Model;

        // The mockup's page: one column, at most ColumnWidth wide, centred on the ground with PageTop above it.
        var page = _page = new StackPanel
        {
            MaxWidth = ColumnWidth,
            Margin = new Thickness(NativeTokens.PageInset, PageTop, NativeTokens.PageInset, PageBottom),
            Spacing = 0,
            Background = Brushes.Transparent,
            Tag = ModalOverlay.GroundTag,
        };
        page.Children.Add(new TextBlock
        {
            Text = WelcomeModel.Heading,
            Tag = ModalOverlay.GroundTag,
            FontSize = NativeTokens.Display,
            FontWeight = FontWeight.SemiBold,
            Foreground = colours.Brush(colours.Text),
        });
        page.Children.Add(_pageIntro = new TextBlock
        {
            Text = WelcomeModel.PageIntro,
            FontSize = NativeTokens.Title + 1,
            Foreground = colours.Brush(colours.Dim),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, NativeTokens.Space8, 0, NativeTokens.Space28),
            Tag = ModalOverlay.GroundTag,
        });

        // ── Find a world ───────────────────────────────────────────────────────────────────────
        _search = new TextBox
        {
            PlaceholderText = WelcomeModel.FindPlaceholder,
            FontSize = NativeTokens.Title,
            Padding = new Thickness(NativeTokens.Space12, 0),
            Height = NativeTokens.FieldHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
            MaxLength = WorldDirectory.MaxQuery,
        };
        global::Avalonia.Automation.AutomationProperties.SetName(_search, WelcomeModel.FindHeading);
        // Sunk into the ground with the mockup's magnifier, as every search box is: on the raised card a field on Fluent's
        // default (the raised plane itself) had no plane of its own and on Light read as no field at all.
        ModalParts.Sunk(_search, colours);
        _search.TextChanged += (_, _) =>
        {
            _worldSelected = 0;
            DrawRows();
        };
        _findNote = new TextBlock { FontSize = NativeTokens.Small + 1, Foreground = colours.Brush(colours.Dim), TextWrapping = TextWrapping.Wrap };
        var find = new StackPanel { Spacing = NativeTokens.Space14 };
        // The heading, and beside it the door to the whole list (Browse worlds: every row, the order, each address), which
        // the welcome opens as the cells' Browse row does — so its Esc and Back come back here.
        var findHeading = new DockPanel();
        if (session.CanFindWorlds)
        {
            var browseAll = BrowseAllButton = ModalParts.Button(BrowseAll, null, colours, () => Choose(WelcomeChoice.Browse));
            browseAll.Name = "BrowseAllWorlds";
            browseAll.MinHeight = NativeTokens.ControlHeightSmall;
            browseAll.FontSize = NativeTokens.Body;
            browseAll.Margin = new Thickness(0, -NativeTokens.Space4, 0, -NativeTokens.Space4); // the title's row keeps its height
            browseAll.VerticalAlignment = VerticalAlignment.Center;
            SetDock(browseAll, Dock.Right);
            findHeading.Children.Add(browseAll);
        }

        findHeading.Children.Add(CardTitle(WelcomeModel.FindHeading));
        find.Children.Add(findHeading);
        find.Children.Add(_search);
        find.Children.Add(_findBody);
        find.Children.Add(_findNote);

        // ── Connect directly ───────────────────────────────────────────────────────────────────
        _host = Field(WelcomeModel.FieldLabel(WelcomeField.Host), WelcomeModel.HostPlaceholder, WelcomeModel.MaxHost);
        _host.FontFamily = Mono;
        _port = Field(WelcomeModel.FieldLabel(WelcomeField.Port), "4000", 5);
        _port.FontFamily = Mono;
        _character = Field(WelcomeModel.FieldLabel(WelcomeField.Character), "Name", WelcomeModel.MaxCharacter);
        foreach (var box in new[] { _host, _port, _character })
        {
            ModalParts.Sunk(box, colours, magnifier: false);
        }

        _tls = new CheckBox { Content = "Encrypt the connection (TLS)", FontSize = NativeTokens.Body, CornerRadius = new CornerRadius(NativeTokens.RadiusS), Margin = CheckRow };
        _passwordLater = new CheckBox { Content = NativeKeys.Words("Password: " + char.ToLowerInvariant(WelcomeModel.PasswordOn[0]) + WelcomeModel.PasswordOn[1..]), FontSize = NativeTokens.Body, CornerRadius = new CornerRadius(NativeTokens.RadiusS), Margin = CheckRow };
        _command = new TextBlock { FontSize = NativeTokens.Small + 1, LineHeight = NativeTokens.Space16 + NativeTokens.Space2, Foreground = colours.Brush(colours.Dim), TextWrapping = TextWrapping.Wrap };
        _problem = new TextBlock { FontSize = NativeTokens.Body, Foreground = colours.Brush(colours.Warn), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _formIntro = new TextBlock { FontSize = NativeTokens.Body, Foreground = colours.Brush(colours.Dim), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        foreach (var box in new[] { _host, _port, _character })
        {
            box.TextChanged += (_, _) => DrawCommand();
        }

        _tls.IsCheckedChanged += (_, _) => DrawCommand();

        var portAndName = new Grid { ColumnDefinitions = new ColumnDefinitions("110,12,*") };
        var portColumn = Labelled(WelcomeModel.FieldLabel(WelcomeField.Port), _port);
        var nameColumn = Labelled("Character (optional)", _character);
        Grid.SetColumn(portColumn, 0);
        Grid.SetColumn(nameColumn, 2);
        portAndName.Children.Add(portColumn);
        portAndName.Children.Add(nameColumn);

        var connectButton = ModalParts.Button("Connect", null, colours, Connect, accent: true);
        connectButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        connectButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        connectButton.Padding = new Thickness(NativeTokens.Space14, 0);
        connectButton.Height = NativeTokens.FieldHeight; // the fields' height above it, as the mockup's primary button
        connectButton.FontSize = NativeTokens.Title;
        connectButton.Name = "Connect";

        var connect = new StackPanel { Spacing = NativeTokens.Space14 };
        connect.Children.Add(CardTitle(WelcomeModel.ConnectHeading));
        connect.Children.Add(_formIntro);
        connect.Children.Add(Labelled(WelcomeModel.FieldLabel(WelcomeField.Host), _host));
        connect.Children.Add(portAndName);
        connect.Children.Add(_tls);
        connect.Children.Add(_passwordLater);
        connect.Children.Add(connectButton);
        connect.Children.Add(_problem);
        connect.Children.Add(_command);

        // The mockup's 3 : 2: the list wants the room, the form is four short fields.
        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,24,2*") };
        var findCard = _findCard = Card(find);
        var connectCard = _connectCard = Card(connect);
        Grid.SetColumn(findCard, 0);
        Grid.SetColumn(connectCard, 2);
        cards.Children.Add(findCard);
        cards.Children.Add(connectCard);
        page.Children.Add(cards);

        // ── Starting layout ────────────────────────────────────────────────────────────────────
        var layoutHeading = _layoutHeading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space10, Margin = new Thickness(0, NativeTokens.Space28, 0, NativeTokens.Space12) };
        layoutHeading.Children.Add(new TextBlock
        {
            Text = WelcomeModel.LayoutsHeading,
            FontSize = NativeTokens.Title + 2,
            FontWeight = FontWeight.SemiBold,
            Foreground = colours.Brush(colours.Text),
            VerticalAlignment = VerticalAlignment.Center,
        });
        _layoutTag = new TextBlock
        {
            Text = WelcomeModel.SuggestedTag,
            FontSize = NativeTokens.Body,
            FontWeight = FontWeight.SemiBold,
            Foreground = colours.Brush(colours.Accent),
            VerticalAlignment = VerticalAlignment.Center,
        };
        layoutHeading.Children.Add(_layoutTag);
        page.Children.Add(layoutHeading);

        var layouts = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") };
        var column = 0;
        foreach (var layout in new[] { WelcomeLayout.Roleplay, WelcomeLayout.Combat })
        {
            var card = LayoutCard(layout);
            Grid.SetColumn(card, column);
            layouts.Children.Add(card);
            _layoutCards.Add((layout, card));
            column += 2;
        }

        page.Children.Add(layouts);
        _layoutReason = new TextBlock
        {
            FontSize = NativeTokens.Small + 1,
            Foreground = colours.Brush(colours.Dim),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, NativeTokens.Space12, 0, 0),
            Tag = ModalOverlay.GroundTag,
        };
        page.Children.Add(_layoutReason);

        // ── The other ways in ─────────────────────────────────────────────────────────────────
        var others = _others = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, NativeTokens.Space20, 0, 0) };
        others.Children.Add(new TextBlock
        {
            Text = WelcomeModel.OtherWays,
            FontSize = NativeTokens.Body,
            Foreground = colours.Brush(colours.Dim),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, NativeTokens.Space10, 0),
        });
        var settings = ModalParts.Button("Open settings", "F5", colours, () => Choose(WelcomeChoice.Settings));
        settings.Margin = new Thickness(0, 0, NativeTokens.Space8, 0);
        others.Children.Add(settings);
        others.Children.Add(ModalParts.Button("Import…", null, colours, () => Choose(WelcomeChoice.Import)));
        page.Children.Add(others);

        var footer = _footer = ModalParts.Footer(PageKeys, colours);
        // Under the page's column, ending at its right edge as the cards do — not at the window's edge, where the keys sat
        // apart from everything else on the page on any window wider than the column.
        footer.MaxWidth = ColumnWidth;
        footer.Padding = new Thickness(0, NativeTokens.Space8, 0, NativeTokens.Space10);
        footer.Margin = new Thickness(NativeTokens.PageInset, 0); // the page's foot, flush: no card for the band to stand off
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);
        Children.Add(_scroll = new ScrollViewer
        {
            Content = new Panel { Background = Brushes.Transparent, Tag = ModalOverlay.GroundTag, Children = { page } },
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        _scroll.SizeChanged += (_, _) => FitPage();
        page.SizeChanged += (_, _) => FitPage();

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _session.Changed += OnChanged;
        _session.WorldsAttached += OnWorldsAttached;
        Fill(_model);
        DrawFind();
        if (_session.Worlds is { } already)
        {
            Attach(already);
        }
    }

    public string Title => "Welcome";

    public double PreferredWidth => ColumnWidth;

    public bool Centred => true;

    /// <summary>The welcome is the window's whole first page, the mockup's, not a card over the cells.</summary>
    public bool FullPage => true;

    /// <summary>The page's column: the mockup's 1,040 px.</summary>
    private const double ColumnWidth = 1040;

    /// <summary>
    /// A check box row's margin: four pixels taken off above and below Fluent's 32 px check box (its template fixes that
    /// height), so the row is the mockup's 24 and the two rows and the Connect button sit the card's 14 px apart.
    /// </summary>
    private static Thickness CheckRow => new(0, -NativeTokens.Space4, 0, -NativeTokens.Space4);

    /// <summary>The ground above the heading: the mockup's 56 px.</summary>
    private const double PageTop = NativeTokens.Space28 * 2;

    /// <summary>The ground under the last row, above the key band: enough to end the page, little enough to fit 900 px.</summary>
    private const double PageBottom = NativeTokens.Space16;

    /// <summary>The heading's ground on a window too short for the page at the mockup's spacing.</summary>
    private const double CompactTop = NativeTokens.Space24;

    /// <summary>The gaps under the intro and above the layout heading: the mockup's 28 px, and tight.</summary>
    private const double SectionGap = NativeTokens.Space28;

    private const double CompactSectionGap = NativeTokens.Space16;

    /// <summary>The gap above the other ways in, and tight.</summary>
    private const double OthersGap = NativeTokens.Space20;

    private const double CompactOthersGap = NativeTokens.Space12;

    /// <summary>The two upper cards' padding: the mockup's 22 px inside, and tight.</summary>
    private const double CardPadding = NativeTokens.Space20 + NativeTokens.Space2;

    private const double CompactCardPadding = NativeTokens.Space16 + NativeTokens.Space2;

    /// <summary>How much shorter the tight page is: the ground above, two section gaps, the gap above the other ways, and the cards' padding above and below.</summary>
    private const double CompactSaving =
        (PageTop - CompactTop) + (2 * (SectionGap - CompactSectionGap)) + (OthersGap - CompactOthersGap) + (2 * (CardPadding - CompactCardPadding));

    /// <summary>Whether the page has given up the /connect example and the layouts' sentences to fit, for a test.</summary>
    internal bool Short => _short;

    private bool _short;

    private double _shortSaving;

    /// <summary>The layout cards' sentences, which a short page leaves to the cards' tooltips.</summary>
    private readonly List<TextBlock> _layoutDetails = [];

    /// <summary>What hiding the example and the layouts' sentences gives back: each with the spacing above it.</summary>
    private double ShortSaving() =>
        (_command.IsVisible ? _command.Bounds.Height + NativeTokens.Space14 : 0)
        + _layoutDetails.Where(d => d.IsVisible).Select(d => d.Bounds.Height + NativeTokens.Space4).DefaultIfEmpty(0).Max();

    /// <summary>Whether the page is laid out tight, for a test.</summary>
    public bool Compact => _compact;

    /// <summary>Whether the page fits its window with nothing to scroll, for a test.</summary>
    public bool FitsWithoutScrolling => _scroll.Extent.Height <= _scroll.Viewport.Height + 0.5;

    /// <summary>The model drawn now, for a test.</summary>
    public WelcomeModel Model => _model;

    /// <summary>The menu row ⏎ would choose in the cells: the page has no menu, so it is the model's.</summary>
    public WelcomeChoice Selected => _model.Selected;

    /// <summary>The form's fields, for a test.</summary>
    public TextBox? HostBox => _host;

    public TextBox? PortBox => _port;

    public CheckBox? TlsBox => _tls;

    public TextBox? CharacterBox => _character;

    /// <summary>The line under the form's heading once a pick filled it in, for a test.</summary>
    public TextBlock FormIntro => _formIntro;

    /// <summary>The find card's search box, for a test.</summary>
    public TextBox SearchBox => _search;

    /// <summary>The key band at the foot as drawn, for a test.</summary>
    public string FooterText => _footer.Text ?? string.Empty;

    /// <summary>The find card's door to Browse worlds, for a test; null where the page cannot list worlds.</summary>
    public Button? BrowseAllButton { get; }

    /// <summary>The world row Enter adds (raised), for a test.</summary>
    public int SelectedWorldRow => _worldSelected;

    /// <summary>The world rows the find card shows now, for a test.</summary>
    public IReadOnlyList<Border> WorldRows => _worldRows;

    /// <summary>The address rows the find card shows while a picked world's addresses are chosen from, for a test.</summary>
    public IReadOnlyList<Border> AddressRows => _addressRows;

    /// <summary>Whether the find card lists a picked world's addresses in place of the worlds, for a test.</summary>
    public bool ChoosingAddress => _worlds?.Model.Choosing is not null;

    /// <summary>The address Enter would choose, for a test.</summary>
    public int SelectedAddress => _address;

    /// <summary>The <c>/connect</c> line under the form as drawn, for a test.</summary>
    public string CommandLine => string.Concat(_command.Inlines?.OfType<Run>().Select(r => r.Text) ?? []);

    public void FocusFirst()
    {
        // Filled in by a pick: host, port and TLS are the directory's, so the keyboard waits where the player has to type.
        if (_model.WorldName is not null)
        {
            _character.Focus();
        }
        else if (_model.InForm || _model.Host.Length > 0 || !_session.CanFindWorlds)
        {
            _host.Focus();
        }
        else
        {
            _search.Focus();
        }
    }

    public void Detach()
    {
        _session.Changed -= OnChanged;
        _session.WorldsAttached -= OnWorldsAttached;
        if (_worlds is not null)
        {
            _worlds.Changed -= OnWorldsChanged;
        }
    }

    /// <summary>A choice the page offers as a button (settings, import, or Browse worlds where it cannot list in place).</summary>
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
            _host.Text ?? string.Empty,
            _port.Text ?? string.Empty,
            _tls.IsChecked == true,
            _character.Text ?? string.Empty,
            _passwordLater.IsChecked == true));
        if (_session.IsEnded)
        {
            _done();
        }
    }

    /// <summary>The find card's ask: the list the first time, the directory's own search for words the rows do not hold.</summary>
    public void Find() => _session.FindWorlds(_search.Text ?? string.Empty);

    /// <summary>A row's Add: Browse worlds' pick of row <paramref name="index"/> of the list's rows.</summary>
    public void Add(int index) => _worlds?.Pick(index);

    /// <summary>
    /// An address row, or Enter on the selected one, while a picked world's addresses are listed: Browse worlds' own
    /// choice, which reopens the welcome with the form filled in from it.
    /// </summary>
    public void ChooseAddress(int index) => _worlds?.Choose(index);

    /// <summary>Back, or Esc, while the addresses are listed: the worlds again, as Browse worlds' Esc does.</summary>
    public void LeaveAddresses() => _worlds?.Back();

    /// <summary>The page's keys, spelt in words.</summary>
    private const string PageKeys = "Tab moves · Enter connects, or searches · Esc closes";

    /// <summary>The page's keys once the find card lists worlds: the arrows move the row Enter adds.</summary>
    private const string ListKeys = "Tab moves · ↑↓ pick a world · Enter adds it, or connects · Esc closes";

    /// <summary>The find card's door to Browse worlds.</summary>
    private const string BrowseAll = WelcomeModel.BrowseAllButton;

    /// <summary>What Esc says while a pick has filled the form in: it goes back to the worlds rather than closing.</summary>
    private const string EscBack = "Esc back to the worlds";

    /// <summary>The page's keys now: the list's once it lists worlds, with Esc going back while a pick fills the form.</summary>
    private string Keys()
    {
        var keys = _worlds is null ? PageKeys : ListKeys;
        return _model.WorldName is null ? keys : keys.Replace("Esc closes", EscBack, StringComparison.Ordinal);
    }

    /// <summary>
    /// The page at the mockup's spacing while the window has the height for it, else tight (less ground above the heading,
    /// narrower section gaps and card padding), so a 900 px window and the default one show it whole with nothing to
    /// scroll. Measured on the natural height, so laying it out tight cannot flip it back.
    /// </summary>
    private void FitPage()
    {
        var viewport = _scroll.Bounds.Height;
        if (viewport <= 0)
        {
            return;
        }

        var natural = _page.Bounds.Height + _page.Margin.Top + _page.Margin.Bottom + (_compact ? CompactSaving : 0) + (_short ? _shortSaving : 0);
        var compact = natural > viewport + 0.5;

        // Shorter still (the default window at 150% on a 1080p screen is 690 px tall): the /connect example and the layout
        // cards' sentences go, the cells' rule for a short welcome. Both are said elsewhere: the example in help and on
        // the command line, a layout's sentence in its card's tooltip. Measured before they go, so it cannot flip back.
        var saving = _short ? _shortSaving : ShortSaving();
        var shorter = compact && natural - CompactSaving > viewport + 0.5;
        if (shorter != _short)
        {
            _shortSaving = saving;
            _short = shorter;
            _command.IsVisible = !shorter;
            foreach (var detail in _layoutDetails)
            {
                detail.IsVisible = !shorter;
            }
        }

        if (compact == _compact)
        {
            return;
        }

        _compact = compact;
        var gap = compact ? CompactSectionGap : SectionGap;
        _page.Margin = new Thickness(NativeTokens.PageInset, compact ? CompactTop : PageTop, NativeTokens.PageInset, PageBottom);
        _pageIntro.Margin = new Thickness(0, NativeTokens.Space8, 0, gap);
        _layoutHeading.Margin = new Thickness(0, gap, 0, NativeTokens.Space12);
        _others.Margin = new Thickness(0, compact ? CompactOthersGap : OthersGap, 0, 0);
        _findCard.Padding = _connectCard.Padding = new Thickness(compact ? CompactCardPadding : CardPadding);
    }

    private static FontFamily Mono => NativeTokens.MonoFont;

    private void OnChanged(WelcomeModel model) => Dispatcher.UIThread.Post(() =>
    {
        var picked = _model.WorldName is null && model.WorldName is not null;
        var givenBack = _model.WorldName is not null && model.WorldName is null;
        _model = model;
        Fill(model);
        DrawFind();

        // A pick in the find card filled the form in on this page: the keyboard goes where the player has to type, as it
        // does on a page opened filled in. Esc gave the pick back: the keyboard goes back to the list, as it was left.
        if (picked)
        {
            _character.Focus();
        }
        else if (givenBack && _search.IsVisible && _search.IsEnabled)
        {
            _search.Focus();
        }
    });

    private void OnWorldsAttached(WorldBrowseSession worlds) => Dispatcher.UIThread.Post(() => Attach(worlds));

    private void OnWorldsChanged(WorldBrowseModel model) => Dispatcher.UIThread.Post(DrawFind);

    private void Attach(WorldBrowseSession worlds)
    {
        if (ReferenceEquals(worlds, _worlds))
        {
            return;
        }

        if (_worlds is not null)
        {
            _worlds.Changed -= OnWorldsChanged;
        }

        _worlds = worlds;
        worlds.Changed += OnWorldsChanged;
        DrawFind();
    }

    /// <summary>
    /// The model's fields into the form, but only when the client changed them (a pick prefilling the form), so the
    /// player's typing is never overwritten by a republish that only carried a problem or a layout.
    /// </summary>
    private void Fill(WelcomeModel model)
    {
        var previous = _filledFrom;
        _filledFrom = model;
        if (previous is null || previous.Host != model.Host || previous.WorldName != model.WorldName)
        {
            _host.Text = model.Host;
        }

        if (previous is null || previous.Port != model.Port)
        {
            _port.Text = model.Port;
        }

        if (previous is null || previous.Tls != model.Tls)
        {
            _tls.IsChecked = model.Tls;
        }

        if (previous is null || previous.Character != model.Character)
        {
            _character.Text = model.Character;
        }

        if (previous is null || previous.PasswordLater != model.PasswordLater)
        {
            _passwordLater.IsChecked = model.PasswordLater;
        }

        // The cells' line under the form's heading once a pick filled it in: which world, as listed, and the encoding set.
        _formIntro.Text = model.WorldName is null ? null : NativeKeys.Words(model.FormIntroLine);
        _formIntro.IsVisible = model.WorldName is not null;
        _problem.Text = NativeKeys.Words(model.Problem);
        _problem.IsVisible = model.Problem is not null;
        _layoutTag.IsVisible = model.Layout == WelcomeLayout.Suggested;
        _layoutReason.Text = NativeKeys.Words(model.LayoutReason);
        foreach (var (layout, card) in _layoutCards)
        {
            var chosen = layout == model.Layout;
            card.BorderBrush = _colours.Brush(chosen ? _colours.Accent : _colours.Edge);
            card.BorderThickness = new Thickness(chosen ? NativeTokens.BorderStrong : NativeTokens.Hairline);
            // The mockup's: the chosen card raised with the accent edge, the other on the card plane; the padding takes
            // up the thicker edge so the words do not move.
            card.Padding = chosen
                ? new Thickness(NativeTokens.Space16 + NativeTokens.Space2, NativeTokens.Space16)
                : new Thickness(NativeTokens.Space16 + NativeTokens.Space2 + NativeTokens.Hairline, NativeTokens.Space16 + NativeTokens.Hairline);
            card.Background = _colours.Brush(chosen ? _colours.Raised : _colours.Card);
        }

        if (_worlds?.Model.Choosing is null)
        {
            _footer.Text = NativeKeys.Words(Keys());
        }

        DrawCommand();
    }

    /// <summary>Same as typing <c>/connect host port tls</c> in any window — from what the fields hold now.</summary>
    private void DrawCommand()
    {
        var (lead, tail) = WelcomeModel.CommandWords;
        _command.Inlines!.Clear();
        _command.Inlines.Add(new Run(lead + " "));
        _command.Inlines.Add(new Run(WelcomeModel.CommandFor(_host.Text ?? string.Empty, _port.Text ?? string.Empty, _tls.IsChecked == true))
        {
            FontFamily = Mono,
            Foreground = _colours.Brush(_colours.Text),
        });
        _command.Inlines.Add(new Run(" " + tail));
    }

    /// <summary>The find card's body: the button that asks, then the rows and what the list says about itself.</summary>
    private void DrawFind()
    {
        _addressHadFocus |= _addressRows.Any(r => r.IsKeyboardFocusWithin);
        _findBody.Children.Clear();
        _worldRows.Clear();
        _addressRows.Clear();
        _search.IsVisible = true;
        if (!_session.CanFindWorlds)
        {
            // No directory set (or a host that cannot list in place): the model's reason, and Browse worlds as a choice.
            _search.IsEnabled = false;
            _findNote.Text = _model.IndexProblem ?? WelcomeModel.FindNote;
            var browse = ModalParts.Button(_model.Describe(WelcomeChoice.Browse).Title, null, _colours, () => Choose(WelcomeChoice.Browse));
            browse.IsEnabled = _model.IndexProblem is null;
            _findBody.Children.Add(browse);
            return;
        }

        _search.IsEnabled = true;
        if (_worlds is null)
        {
            var show = ModalParts.Button(WelcomeModel.FindButton, "Enter", _colours, Find);
            show.Name = "ShowWorlds";
            _findBody.Children.Add(show);
            _findNote.Text = WelcomeModel.FindNote;
            return;
        }

        DrawRows();
    }

    private void DrawRows()
    {
        if (_worlds is not { } worlds)
        {
            return;
        }

        _addressHadFocus |= _addressRows.Any(r => r.IsKeyboardFocusWithin);
        _findBody.Children.Clear();
        _worldRows.Clear();
        _addressRows.Clear();
        var model = worlds.Model;
        if (model.Choosing is { } game)
        {
            DrawChoice(model, game);
            return;
        }

        var choosingWasDrawn = _choiceDrawn is not null;
        _choiceDrawn = null;
        _addressHadFocus = false;
        _search.IsVisible = true;
        _footer.Text = NativeKeys.Words(Keys());
        if (choosingWasDrawn)
        {
            _search.Focus();
        }

        var query = _search.Text ?? string.Empty;
        var shown = model.View(query, WorldDirectorySort.Players);
        var list = new StackPanel { Spacing = NativeTokens.Space8 };
        _shown.Clear();
        foreach (var world in shown)
        {
            var index = IndexOf(model, world);
            if (index >= 0)
            {
                _shown.Add(index);
            }
        }

        _worldSelected = Math.Clamp(_worldSelected, 0, Math.Max(0, _shown.Count - 1));
        for (var i = 0; i < _shown.Count; i++)
        {
            var index = _shown[i];
            var row = WorldRow(model.Rows[index], index, model.Opening is null, i == _worldSelected);
            _worldRows.Add(row);
            list.Children.Add(row);
        }

        if (list.Children.Count > 0)
        {
            _findBody.Children.Add(new ScrollViewer
            {
                Content = list,
                MaxHeight = (ShownRows * 62) + ((ShownRows - 1) * 8),
                HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            });
            if (_worldSelected < _worldRows.Count)
            {
                var selectedRow = _worldRows[_worldSelected];
                Dispatcher.UIThread.Post(() => selectedRow.BringIntoView(), DispatcherPriority.Background);
            }
        }

        // What the list says about itself: asking, why it is empty, or how many and from where.
        var status = model.Opening is not null ? model.OpeningLine
            : model.Problem is { } problem && model.State == WorldBrowseState.Ready ? problem
            : model.Empty(query);
        _findNote.Text = status is not null
            ? NativeKeys.Words(status)
            : $"{model.CountLine(query)} · {WorldBrowseModel.AttributionLine}";
        _findNote.Foreground = _colours.Brush(model.State is WorldBrowseState.Failed || (model.Problem is not null && model.State == WorldBrowseState.Ready) ? _colours.Warn : _colours.Dim);
    }

    /// <summary>
    /// A picked world's addresses in the find card, in place of the worlds: Browse worlds' choice, with its words — whose
    /// addresses and how many, the charset, then a row per address (<c>host:port</c>, TLS or plain, its state) — and Back.
    /// </summary>
    private void DrawChoice(WorldBrowseModel model, WorldGame game)
    {
        if (!ReferenceEquals(_choiceDrawn, game))
        {
            _address = Math.Max(0, game.FirstConnectable);
        }

        var fresh = _choiceDrawn is null;
        var hadRowFocus = _addressHadFocus;
        _addressHadFocus = false;
        _choiceDrawn = game;
        _search.IsVisible = false;
        _footer.Text = NativeKeys.Words(string.Join(" · ", WorldBrowseModel.ChoiceKeys.Select(k => $"{k.Key} {k.Label}")));
        _findBody.Children.Add(new TextBlock
        {
            Text = model.ChoiceLine,
            FontSize = NativeTokens.Title,
            FontWeight = FontWeight.SemiBold,
            Foreground = _colours.Brush(_colours.Text),
            TextWrapping = TextWrapping.Wrap,
        });
        _findBody.Children.Add(new TextBlock
        {
            Text = model.CharsetLine,
            FontSize = NativeTokens.Body,
            Foreground = _colours.Brush(_colours.Dim),
            TextWrapping = TextWrapping.Wrap,
        });

        // Entered once by Tab and walked with the arrows, as every native list is; the row with the keyboard is the one
        // Enter chooses, so the Back button below is never what Enter presses by default.
        var list = new StackPanel { Spacing = NativeTokens.Space2 };
        KeyboardNavigation.SetTabNavigation(list, KeyboardNavigationMode.Once);
        _address = Math.Clamp(_address, 0, game.Endpoints.Count - 1);
        for (var i = 0; i < game.Endpoints.Count; i++)
        {
            var endpoint = game.Endpoints[i];
            var (address, security, state) = WorldBrowseModel.Endpoint(endpoint);
            var index = i;
            var line = new DockPanel();
            var host = new TextBlock
            {
                Text = address,
                FontFamily = Mono,
                FontSize = NativeTokens.Body + 1,
                Foreground = _colours.Brush(_colours.Text),
                Width = 210,
                Margin = new Thickness(0, 0, NativeTokens.Space10, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(host, Dock.Left);
            line.Children.Add(host);
            var kind = new TextBlock
            {
                Text = security,
                FontSize = NativeTokens.Body,
                FontWeight = endpoint.Tls ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = _colours.Brush(endpoint.Tls ? _colours.Accent : _colours.Dim),
                Width = 44,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(kind, Dock.Left);
            line.Children.Add(kind);
            line.Children.Add(new TextBlock
            {
                Text = state,
                FontSize = NativeTokens.Body,
                Foreground = _colours.Brush(endpoint.Stale || !endpoint.Connectable ? _colours.Warn : _colours.Dim),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var row = ModalParts.Row(line, address, () => SelectAddress(index), () => ChooseAddress(index));
            row.Focusable = true;
            row.GotFocus += (_, _) => SelectAddress(index);
            ToolTip.SetTip(row, WorldBrowseModel.EndpointText(endpoint));
            _addressRows.Add(row);
            list.Children.Add(row);
        }

        _findBody.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = ShownRows * 40,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });

        var back = ModalParts.Button("Back to the worlds", "Esc", _colours, LeaveAddresses);
        back.Name = "BackToWorlds";
        back.HorizontalAlignment = HorizontalAlignment.Left;
        _findBody.Children.Add(back);

        _findNote.Text = NativeKeys.Words(model.Problem ?? WorldBrowseModel.ChoiceNote);
        _findNote.Foreground = _colours.Brush(model.Problem is null ? _colours.Dim : _colours.Warn);
        HighlightAddress();

        // The Add pressed to get here has left the page; the keys stay with the choice, on the address Enter takes.
        // Posted: the rows sit in a scroll viewer whose template puts them in the tree only at the next layout, and a
        // control not in the tree refuses the focus (which then fell to the cells behind the page).
        if (fresh || hadRowFocus)
        {
            var row = _addressRows[_address];
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (row.IsAttachedToVisualTree())
                    {
                        row.Focus(NavigationMethod.Directional);
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    private void SelectAddress(int index)
    {
        if (index != _address)
        {
            _address = index;
            HighlightAddress();
        }
    }

    private void HighlightAddress()
    {
        for (var i = 0; i < _addressRows.Count; i++)
        {
            _addressRows[i].Background = i == _address ? _colours.Brush(_colours.Selected) : Brushes.Transparent;
        }
    }

    private static int IndexOf(WorldBrowseModel model, WorldListing world)
    {
        for (var i = 0; i < model.Rows.Count; i++)
        {
            if (ReferenceEquals(model.Rows[i], world) || model.Rows[i] == world)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>One listed world: its name over its codebase, players and tagline, with Add at the right.</summary>
    /// <summary>↑↓ in the search: the row Enter adds moves, the cells' Browse worlds selection.</summary>
    private void SelectWorld(int delta)
    {
        if (_shown.Count == 0)
        {
            return;
        }

        _worldSelected = ModalParts.Step(_worldSelected, delta, _shown.Count);
        DrawRows();
    }

    /// <summary>
    /// Enter in the search once the list is shown: the words none of the rows hold ask the directory, as Browse worlds'
    /// ⏎ does; otherwise the raised row is added, the pick ⏎ makes in the cells.
    /// </summary>
    private void FindOrAdd()
    {
        var query = _search.Text ?? string.Empty;
        if (_worlds is { } worlds && worlds.Model.Choosing is null && !worlds.Model.AsksDirectory(query)
            && _worldSelected < _shown.Count)
        {
            if (worlds.Model.Opening is null)
            {
                Add(_shown[_worldSelected]);
            }

            return;
        }

        Find();
    }

    private Border WorldRow(WorldListing world, int index, bool enabled, bool selected)
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = world.Name,
            FontSize = NativeTokens.Title,
            FontWeight = FontWeight.SemiBold,
            Foreground = _colours.Brush(_colours.Text),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var detail = string.Join(" · ", new[]
        {
            world.Codebase,
            world.Players is null ? null : $"{WorldBrowseModel.Players(world)} playing",
            world.Tagline,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
        words.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = NativeTokens.Body,
            Foreground = _colours.Brush(_colours.Dim),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, NativeTokens.Space2, 0, 0),
        });

        var add = ModalParts.Button(WelcomeModel.AddButton, null, _colours, () => Add(index), accent: selected);
        add.IsEnabled = enabled;
        add.CornerRadius = new CornerRadius(NativeTokens.RadiusControl);
        add.MinHeight = NativeTokens.ControlHeightSmall + NativeTokens.Space4;
        add.Padding = new Thickness(NativeTokens.Space14, 0);
        add.VerticalAlignment = VerticalAlignment.Center;
        global::Avalonia.Automation.AutomationProperties.SetName(add, $"{WelcomeModel.AddButton} {world.Name}");

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(add, 1);
        add.Margin = new Thickness(NativeTokens.Space12, 0, 0, 0);
        grid.Children.Add(words);
        grid.Children.Add(add);
        // The mockup's rows: the one Enter would add (the first, until ↑↓ moves it) raised with the focused edge; the rest only outlined.
        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(NativeTokens.Space14, NativeTokens.Space12),
            CornerRadius = new CornerRadius(NativeTokens.RadiusCard),
            BorderThickness = new Thickness(NativeTokens.Hairline),
            BorderBrush = _colours.Brush(selected ? _colours.Focus : _colours.Edge),
            Background = selected ? _colours.Brush(_colours.Raised) : Brushes.Transparent,
        };
        ToolTip.SetTip(row, world.Tagline.Length > 0 ? world.Tagline : world.Name);
        global::Avalonia.Automation.AutomationProperties.SetName(row, world.Name);
        return row;
    }

    private Border LayoutCard(WelcomeLayout layout)
    {
        // Centred in the card, as the mockup's buttons centre theirs: the shorter card's words sit in its middle.
        var words = new StackPanel { Spacing = NativeTokens.Space4, VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = layout.ToString(),
            FontSize = NativeTokens.Title,
            FontWeight = FontWeight.SemiBold,
            Foreground = _colours.Brush(_colours.Text),
        });
        var detail = new TextBlock
        {
            Text = WelcomeModel.LayoutDetail(layout),
            LineHeight = NativeTokens.Space16 + NativeTokens.Space2 + NativeTokens.Hairline,
            FontSize = NativeTokens.Body,
            Foreground = _colours.Brush(_colours.Dim),
            TextWrapping = TextWrapping.Wrap,
        };
        words.Children.Add(detail);
        _layoutDetails.Add(detail);
        var card = new Border
        {
            Child = words,
            CornerRadius = new CornerRadius(NativeTokens.RadiusL),
            Cursor = new Cursor(StandardCursorType.Hand),
            Name = layout.ToString(),
        };
        global::Avalonia.Automation.AutomationProperties.SetName(card, $"{layout} layout");
        ToolTip.SetTip(card, WelcomeModel.LayoutDetail(layout));
        card.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                PickLayout(layout);
            }
        };
        return card;
    }

    /// <summary>A layout card: chooses it, or, chosen already, goes back to letting the game suggest one.</summary>
    public void PickLayout(WelcomeLayout layout) =>
        _session.SetLayout(_model.Layout == layout ? WelcomeLayout.Suggested : layout);

    private TextBlock CardTitle(string text) => new()
    {
        Text = text,
        FontSize = NativeTokens.Title + 2,
        FontWeight = FontWeight.SemiBold,
        Foreground = _colours.Brush(_colours.Text),
    };

    /// <summary>A card on the page's ground: the card plane, a hairline edge, the mockup's 22 px inside.</summary>
    private Border Card(Control content) => new()
    {
        Child = content,
        Padding = new Thickness(NativeTokens.Space20 + NativeTokens.Space2),
        CornerRadius = new CornerRadius(NativeTokens.RadiusL),
        BorderThickness = new Thickness(NativeTokens.Hairline),
        BorderBrush = _colours.Brush(_colours.Edge),
        Background = _colours.Brush(_colours.Card),
    };

    private StackPanel Labelled(string label, Control field)
    {
        var column = new StackPanel { Spacing = NativeTokens.Space6 };
        column.Children.Add(new TextBlock { Text = label, FontSize = NativeTokens.Body, Foreground = _colours.Brush(_colours.Dim) });
        column.Children.Add(field);
        return column;
    }

    private static TextBox Field(string name, string placeholder, int max)
    {
        var box = new TextBox
        {
            PlaceholderText = placeholder,
            MaxLength = max,
            FontSize = NativeTokens.Title,
            Padding = new Thickness(NativeTokens.Space10, 0),
            Height = NativeTokens.FieldHeight,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(NativeTokens.RadiusM),
        };
        global::Avalonia.Automation.AutomationProperties.SetName(box, name);
        return box;
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (ModalParts.FocusedButtonTakes(e))
        {
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (_worlds?.Model.Choosing is { } game)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    LeaveAddresses();
                    return;
                case Key.Up when !IsWithinForm(e.Source):
                case Key.Down when !IsWithinForm(e.Source):
                    e.Handled = true;
                    SelectAddress(ModalParts.Step(_address, e.Key == Key.Up ? -1 : 1, game.Endpoints.Count));
                    if (_address < _addressRows.Count)
                    {
                        _addressRows[_address].Focus(NavigationMethod.Directional);
                    }

                    return;
                case Key.Enter when !IsWithinForm(e.Source):
                    e.Handled = true;
                    ChooseAddress(_address);
                    return;
            }
        }

        if (e.Key is Key.Up or Key.Down && _worlds is not null && e.Source is Visual inSearch && IsWithin(inSearch, _search))
        {
            e.Handled = true;
            SelectWorld(e.Key == Key.Up ? -1 : 1);
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;

            // One step back, as the cells' Esc. A form a pick filled in goes back to the list it came from: the client's
            // to decide (Browse worlds as it was left, or the pick given back to the find card beside it)...
            if (_model.WorldName is not null)
            {
                _session.Back();
                return;
            }

            // ...a form typed by hand goes back to the page's menu, the find card, keeping what was typed...
            if (e.Source is Visual inForm && IsWithin(inForm, _connectCard) && _search.IsEnabled && _search.IsVisible)
            {
                _session.Back();
                _search.Focus();
                return;
            }

            // ...and with no step left, Esc closes.
            _session.Dismiss();
            _done();
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        if (ReferenceEquals(e.Source, _search) || (e.Source is Visual v && IsWithin(v, _search)))
        {
            FindOrAdd();
        }
        else if (e.Source is Visual row && _addressRows.Any(r => IsWithin(row, r)))
        {
            ChooseAddress(_address);
        }
        else
        {
            Connect();
        }
    }

    /// <summary>Whether a key came from the connect card's fields, where Enter connects whatever the find card shows.</summary>
    private bool IsWithinForm(object? source) =>
        source is Visual v && new Visual[] { _host, _port, _character, _tls, _passwordLater }.Any(f => IsWithin(v, f));

    private static bool IsWithin(Visual visual, Visual ancestor)
    {
        for (Visual? at = visual; at is not null; at = at.GetVisualParent())
        {
            if (ReferenceEquals(at, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
