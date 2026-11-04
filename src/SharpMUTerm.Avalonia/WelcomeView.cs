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
    private WelcomeModel _model;
    private WorldBrowseSession? _worlds;
    private WelcomeModel? _filledFrom;
    private WorldGame? _choiceDrawn;
    private int _address;

    public WelcomeView(WelcomeSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        _model = session.Model;

        // The mockup's page: one column, at most ColumnWidth wide, centred on the ground with PageTop above it.
        var page = new StackPanel
        {
            MaxWidth = ColumnWidth,
            Margin = new Thickness(NativeTokens.PageInset, PageTop, NativeTokens.PageInset, NativeTokens.PageInset),
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
        page.Children.Add(new TextBlock
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
        _search.TextChanged += (_, _) => DrawRows();
        _findNote = new TextBlock { FontSize = NativeTokens.Small + 1, Foreground = colours.Brush(colours.Dim), TextWrapping = TextWrapping.Wrap };
        var find = new StackPanel { Spacing = NativeTokens.Space14 };
        find.Children.Add(CardTitle(WelcomeModel.FindHeading));
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

        _tls = new CheckBox { Content = "Encrypt the connection (TLS)", FontSize = NativeTokens.Body, CornerRadius = new CornerRadius(NativeTokens.RadiusS) };
        _passwordLater = new CheckBox { Content = NativeKeys.Words("Password: " + char.ToLowerInvariant(WelcomeModel.PasswordOn[0]) + WelcomeModel.PasswordOn[1..]), FontSize = NativeTokens.Body, CornerRadius = new CornerRadius(NativeTokens.RadiusS) };
        _command = new TextBlock { FontSize = NativeTokens.Small + 1, LineHeight = NativeTokens.Space16 + NativeTokens.Space2, Foreground = colours.Brush(colours.Dim), TextWrapping = TextWrapping.Wrap };
        _problem = new TextBlock { FontSize = NativeTokens.Body, Foreground = colours.Brush(colours.Warn), TextWrapping = TextWrapping.Wrap, IsVisible = false };
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
        connect.Children.Add(Labelled(WelcomeModel.FieldLabel(WelcomeField.Host), _host));
        connect.Children.Add(portAndName);
        connect.Children.Add(_tls);
        connect.Children.Add(_passwordLater);
        connect.Children.Add(connectButton);
        connect.Children.Add(_problem);
        connect.Children.Add(_command);

        // The mockup's 3 : 2: the list wants the room, the form is four short fields.
        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,24,2*") };
        var findCard = Card(find);
        var connectCard = Card(connect);
        Grid.SetColumn(findCard, 0);
        Grid.SetColumn(connectCard, 2);
        cards.Children.Add(findCard);
        cards.Children.Add(connectCard);
        page.Children.Add(cards);

        // ── Starting layout ────────────────────────────────────────────────────────────────────
        var layoutHeading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space10, Margin = new Thickness(0, NativeTokens.Space28, 0, NativeTokens.Space12) };
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
        var others = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, NativeTokens.Space20, 0, 0) };
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
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);
        Children.Add(new ScrollViewer
        {
            Content = new Panel { Background = Brushes.Transparent, Tag = ModalOverlay.GroundTag, Children = { page } },
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });

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

    /// <summary>The ground above the heading: the mockup's 56 px.</summary>
    private const double PageTop = NativeTokens.Space28 * 2;

    /// <summary>The model drawn now, for a test.</summary>
    public WelcomeModel Model => _model;

    /// <summary>The menu row ⏎ would choose in the cells: the page has no menu, so it is the model's.</summary>
    public WelcomeChoice Selected => _model.Selected;

    /// <summary>The form's fields, for a test.</summary>
    public TextBox? HostBox => _host;

    public TextBox? PortBox => _port;

    public CheckBox? TlsBox => _tls;

    public TextBox? CharacterBox => _character;

    /// <summary>The find card's search box, for a test.</summary>
    public TextBox SearchBox => _search;

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
        if (_model.InForm || _model.Host.Length > 0 || !_session.CanFindWorlds)
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

    private static FontFamily Mono => NativeTokens.MonoFont;

    private void OnChanged(WelcomeModel model) => Dispatcher.UIThread.Post(() =>
    {
        _model = model;
        Fill(model);
        DrawFind();
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
        _search.IsVisible = true;
        _footer.Text = PageKeys;
        if (choosingWasDrawn)
        {
            _search.Focus();
        }

        var query = _search.Text ?? string.Empty;
        var shown = model.View(query, WorldDirectorySort.Players);
        var list = new StackPanel { Spacing = NativeTokens.Space8 };
        foreach (var world in shown)
        {
            var index = IndexOf(model, world);
            if (index < 0)
            {
                continue;
            }

            var row = WorldRow(world, index, model.Opening is null);
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

        var list = new StackPanel { Spacing = NativeTokens.Space2 };
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

        // The Add pressed to get here has left the page; the keys stay with the choice.
        if (fresh)
        {
            back.Focus();
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
    private Border WorldRow(WorldListing world, int index, bool enabled)
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

        var add = ModalParts.Button(WelcomeModel.AddButton, null, _colours, () => Add(index), accent: index == 0);
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
        // The mockup's rows: the first, the one Enter would add, raised with the focused edge; the rest only outlined.
        var row = new Border
        {
            Child = grid,
            Padding = new Thickness(NativeTokens.Space14, NativeTokens.Space12),
            CornerRadius = new CornerRadius(NativeTokens.RadiusCard),
            BorderThickness = new Thickness(NativeTokens.Hairline),
            BorderBrush = _colours.Brush(index == 0 ? _colours.Focus : _colours.Edge),
            Background = index == 0 ? _colours.Brush(_colours.Raised) : Brushes.Transparent,
        };
        ToolTip.SetTip(row, world.Tagline.Length > 0 ? world.Tagline : world.Name);
        global::Avalonia.Automation.AutomationProperties.SetName(row, world.Name);
        return row;
    }

    private Border LayoutCard(WelcomeLayout layout)
    {
        var words = new StackPanel { Spacing = NativeTokens.Space4 };
        words.Children.Add(new TextBlock
        {
            Text = layout.ToString(),
            FontSize = NativeTokens.Title,
            FontWeight = FontWeight.SemiBold,
            Foreground = _colours.Brush(_colours.Text),
        });
        words.Children.Add(new TextBlock
        {
            Text = WelcomeModel.LayoutDetail(layout),
            LineHeight = NativeTokens.Space16 + NativeTokens.Space2 + NativeTokens.Hairline,
            FontSize = NativeTokens.Body,
            Foreground = _colours.Brush(_colours.Dim),
            TextWrapping = TextWrapping.Wrap,
        });
        var card = new Border
        {
            Child = words,
            CornerRadius = new CornerRadius(NativeTokens.RadiusL),
            Cursor = new Cursor(StandardCursorType.Hand),
            Name = layout.ToString(),
        };
        global::Avalonia.Automation.AutomationProperties.SetName(card, $"{layout} layout");
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
                    return;
                case Key.Enter when !IsWithinForm(e.Source):
                    e.Handled = true;
                    ChooseAddress(_address);
                    return;
            }
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
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
            Find();
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
