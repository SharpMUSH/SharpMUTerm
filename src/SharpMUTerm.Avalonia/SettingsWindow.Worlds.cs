using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Contributions;
using SharpMUTerm.Client.Settings;
using SharpMUTerm.Core.Configuration;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The Worlds &amp; Characters page: every world with its characters as one tree on the left, and on the right whichever
/// is chosen — a world's address, its declared fields in their group cards, its characters with a connect button each and
/// what its server says about itself; or a character's connect button, its declared fields and the trigger sets it uses,
/// in the order that decides a clash. Adding, copying, deleting and reordering worlds and characters are here too, so the
/// window has no need of the cell F5 screen it used to send the player to. F9's character fields are drawn here, beside
/// the world they play on, as the cell F5 draws them.
/// <para>
/// Nothing here writes the configuration: every change goes to <see cref="SettingsForm"/>, which commits it through the
/// client's funnel, and the page catches up on <see cref="SettingsForm.Changed"/>.
/// </para>
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>
    /// The tree's column: narrower than the list editors' 316 px, because the fields beside it are a world's or a
    /// character's whole form rather than one item's.
    /// </summary>
    private const double WorldTreeWidth = 264;

    /// <summary>A text field's box on this page, narrower than the field pages' so its label keeps a readable column.</summary>
    private const double WorldsTextBoxWidth = 220;

    /// <summary>What the page shows on the right: a world (no character) or a character of it.</summary>
    private WorldDefinition? _shownWorld;

    private CharacterDefinition? _shownCharacter;

    /// <summary>A deletion waiting for its yes: the world or character it would delete, or null.</summary>
    private object? _pendingDeletion;

    /// <summary>
    /// A world or character asked to be shown before the configuration holds it: the client commits on its own thread,
    /// so a world or character just added can reach the list a frame after the page asked for it. It is shown once it
    /// arrives, rather than the page falling back to the first world.
    /// </summary>
    private (WorldDefinition World, CharacterDefinition? Character)? _pendingShow;

    private ListBox? _worldTree;
    private StackPanel? _worldHead;
    private StackPanel? _worldCharacters;
    private StackPanel? _worldSets;
    private string _worldShape = string.Empty;

    /// <summary>The tree of worlds and characters, for a test; null on any other page.</summary>
    internal ListBox? WorldTree => _worldTree;

    /// <summary>The world or character the page shows on the right, for a test.</summary>
    internal object? WorldsShown => (object?)_shownCharacter ?? _shownWorld;

    /// <summary>The list's and the detail's buttons, for a test; the detail's are those of the world or character shown.</summary>
    internal Button? AddWorldButton { get; private set; }

    internal Button? AddCharacterButton { get; private set; }

    internal Button? CopyCharacterButton { get; private set; }

    internal Button? DeleteButton { get; private set; }

    internal Button? MoveUpButton { get; private set; }

    internal Button? MoveDownButton { get; private set; }

    /// <summary>The + on each world's row in the list, by world, for a test.</summary>
    internal Dictionary<WorldDefinition, Button> AddCharacterRowButtons { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>The detail's connect button, for a test; null when none is shown.</summary>
    internal Button? ConnectButton { get; private set; }

    /// <summary>The yes of a pending deletion, for a test; null while none is asked.</summary>
    internal Button? ConfirmDeleteButton { get; private set; }

    /// <summary>Each set's check box on a character's page, by set name, for a test.</summary>
    internal Dictionary<string, CheckBox> SetBoxes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The server-information card's rows, as text, for a test.</summary>
    internal List<string> ServerInfoLines { get; } = [];

    /// <summary>Whether <paramref name="section"/> is drawn as this page (F5, and F9 folded into it).</summary>
    private static bool IsWorldsPage(SettingsSectionContribution section) =>
        string.Equals(SettingsForm.PageOf(section.Id), BuiltInSettings.World.Id, StringComparison.Ordinal);

    /// <summary>Shows <paramref name="world"/> (and <paramref name="character"/>, when given) on the right.</summary>
    internal void ShowWorld(WorldDefinition? world, CharacterDefinition? character)
    {
        _shownWorld = world;
        _shownCharacter = character;
        _pendingDeletion = null;
        _pendingShow = world is not null && !Holds(_form.WorldList(), world, character) ? (world, character) : null;
        if (world is not null)
        {
            _form.Choose(world, character);
        }

        var offset = _scroller.Offset;
        BuildPage();
        _scroller.Offset = offset;
    }

    /// <summary>Takes the world and character the form opened on, F9 opening on the character.</summary>
    private void SeedWorldsSelection(string requested)
    {
        _shownWorld = _form.ChosenWorld;
        _shownCharacter = string.Equals(requested, BuiltInSettings.Character.Id, StringComparison.Ordinal) ? _form.ChosenCharacter : null;
        _pendingDeletion = null;
    }

    private void BuildWorldsPage(SettingsSectionContribution section)
    {
        SetBoxes.Clear();
        ServerInfoLines.Clear();
        ConnectButton = null;
        ConfirmDeleteButton = null;
        var worlds = _form.WorldList();
        if (_pendingShow is { } pending && Holds(worlds, pending.World, pending.Character))
        {
            _shownWorld = pending.World;
            _shownCharacter = pending.Character;
            _pendingShow = null;
        }

        // A pending addition stays shown while its commit is on its way: the client applies changes in order, so an edit
        // or a + Character made on it meanwhile lands after it, under it, and never under a world the page fell back to.
        var waiting = _pendingShow is { } held && ReferenceEquals(held.World, _shownWorld);

        // The shown world or character went (deleted here or elsewhere, or undone away): fall back to what the form chose.
        if (!waiting && _shownWorld is not null && !worlds.Any(w => ReferenceEquals(w.World, _shownWorld)))
        {
            _shownWorld = _form.ChosenWorld is { } chosen && worlds.Any(w => ReferenceEquals(w.World, chosen)) ? chosen : worlds.FirstOrDefault()?.World;
            _shownCharacter = null;
        }

        _shownWorld ??= worlds.FirstOrDefault()?.World;
        if (!waiting && _shownCharacter is not null && _shownWorld?.Characters.Contains(_shownCharacter) != true)
        {
            _shownCharacter = null;
        }

        // While searching, the page goes to where the matches are: a character's page when only its fields match.
        if (Searching && _shownWorld is not null)
        {
            var worldHits = _matches.Any(m => m.Field is not null && m.Section.Id == BuiltInSettings.World.Id);
            var characterHits = _matches.Any(m => m.Field is not null && m.Section.Id == BuiltInSettings.Character.Id);
            if (_shownCharacter is null && !worldHits && characterHits && _shownWorld.Characters.FirstOrDefault() is { } first)
            {
                _shownCharacter = first;
            }
            else if (_shownCharacter is not null && !characterHits && worldHits)
            {
                _shownCharacter = null;
            }
        }

        if (_shownWorld is not null)
        {
            _form.Choose(_shownWorld, _shownCharacter);
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{WorldTreeWidth.ToString(CultureInfo.InvariantCulture)},*"), Height = ListEditorHeight(), Margin = new Thickness(0, NativeTokens.Space8, 0, 0) };
        WorldsGrid = grid;
        grid.Children.Add(ListEditorView.PageCard(TreeColumn(), _tokens.Page, new Thickness(0), new Thickness(0, 0, NativeTokens.Space16, 0)));

        var detail = new StackPanel { Spacing = NativeTokens.Space16, Margin = new Thickness(NativeTokens.Space4, 0, NativeTokens.Space4, NativeTokens.Space16) };
        var scroller = new ScrollViewer { Content = detail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroller, 1);
        grid.Children.Add(scroller);
        _page.Children.Add(grid);

        if (_shownWorld is not { } world)
        {
            detail.Children.Add(EmptyWorlds());
            ShownFields = [];
            _worldShape = WorldShape();
            _refreshers.Add(RefreshWorldsPage);
            return;
        }

        _worldHead = new StackPanel { Spacing = NativeTokens.Space8 };
        detail.Children.Add(_worldHead);
        FillHead();

        // What a world or character *is* comes first — a world's characters with their connect buttons, a character's
        // trigger sets in the order that decides a clash — and its settings after, because those are the long part.
        if (!Searching)
        {
            if (_shownCharacter is null)
            {
                _worldCharacters = new StackPanel();
                detail.Children.Add(Card(_worldCharacters));
                FillCharacters(world);
            }
            else
            {
                _worldSets = new StackPanel();
                detail.Children.Add(Card(_worldSets));
                FillSets(_shownCharacter);
            }
        }

        var fields = _shownCharacter is null ? BuiltInSettings.World.Fields : BuiltInSettings.Character.Fields;
        var visible = Searching ? fields.Where(f => _matches.Any(m => ReferenceEquals(m.Field, f))).ToList() : fields.ToList();
        if (Searching && visible.Count == 0)
        {
            detail.Children.Add(new TextBlock { Text = $"No {(_shownCharacter is null ? "world" : "character")} setting here matches the search.", Foreground = DimInk });
        }

        foreach (var card in FieldCards(visible))
        {
            detail.Children.Add(card);
        }

        if (_shownCharacter is null && !Searching)
        {
            detail.Children.Add(ServerInfoCard(world));
        }

        ShownFields = visible.Select(f => f.Id).ToList();
        _worldShape = WorldShape();
        _refreshers.Add(RefreshWorldsPage);
    }

    /// <summary>The page's grid, for a test (its height is the list editors').</summary>
    internal Grid? WorldsGrid { get; private set; }

    /// <summary>
    /// Which field groups are open on the Worlds page, by group name, for the run. A group not named here is open when it
    /// is the first (Connection, which a new world needs filled in) and closed otherwise.
    /// </summary>
    private static readonly Dictionary<string, bool> OpenGroups = new(StringComparer.Ordinal);

    /// <summary>The group headings of the fields on show, for a test: each one's name and whether its rows are open.</summary>
    internal Dictionary<string, Button> GroupToggles { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The declared fields as the field pages draw them: a card per group, a hairline between rows. Each card folds to its
    /// heading and a line saying how many settings it holds and how many are changed, so a world reads as its sections
    /// first; the first is open. While searching every card is open, since only matching fields are on show.
    /// </summary>
    private List<Border> FieldCards(IReadOnlyList<SettingsField> visible)
    {
        GroupToggles.Clear();
        var cards = new List<Border>();
        StackPanel? rows = null;
        string? group = null;
        var first = true;
        foreach (var field in visible)
        {
            if (rows is null || !string.Equals(field.Group, group, StringComparison.Ordinal))
            {
                group = field.Group;
                rows = new StackPanel();
                var body = new StackPanel();
                if (group is { } heading)
                {
                    if (Searching)
                    {
                        body.Children.Add(CardHeading(heading));
                    }
                    else
                    {
                        var members = visible.Where(f => string.Equals(f.Group, heading, StringComparison.Ordinal)).ToList();
                        body.Children.Add(GroupHeading(heading, members, rows, OpenGroups.TryGetValue(heading, out var open) ? open : first));
                    }
                }

                body.Children.Add(rows);
                cards.Add(Card(body));
                first = false;
            }
            else
            {
                rows.Children.Add(new Border { Height = NativeTokens.Hairline, Background = _tokens.Page.Brush(_tokens.Page.Rule) });
            }

            rows.Children.Add(FieldRow(field));
        }

        return cards;
    }

    /// <summary>A card's heading that opens and closes its rows: a chevron, the name, and while closed what it holds.</summary>
    private Button GroupHeading(string heading, IReadOnlyList<SettingsField> members, StackPanel rows, bool open)
    {
        var chevron = new global::Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 0,2 L 4,6 L 8,2"),
            Stroke = DimInk,
            StrokeThickness = 1.5,
            Width = 8,
            Height = 8,
            Stretch = Stretch.None,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, NativeTokens.Space8, 0),
            RenderTransform = new RotateTransform(),
        };
        var title = new TextBlock { Text = heading, FontSize = NativeTokens.CardTitle, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var summary = new TextBlock { FontSize = NativeTokens.PageSmall, Foreground = DimInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NativeTokens.Space10, 0, 0, 0) };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(chevron);
        line.Children.Add(title);
        line.Children.Add(summary);
        var button = new Button
        {
            Content = line,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, NativeTokens.Space12, 0, NativeTokens.Space12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        AutomationProperties.SetName(button, heading);
        GroupToggles[heading] = button;

        void Show()
        {
            ((RotateTransform)chevron.RenderTransform!).Angle = rows.IsVisible ? 0 : -90;
            var changed = members.Count(f => !_form.IsDefault(f));
            var count = members.Count == 1 ? "1 setting" : $"{members.Count} settings";
            summary.Text = rows.IsVisible ? (changed > 0 ? $"{changed} changed" : "") : changed > 0 ? $"{count} · {changed} changed" : count;
            ToolTip.SetTip(button, rows.IsVisible ? $"Hide the {heading} settings" : $"Show the {heading} settings");
            AutomationProperties.SetHelpText(button, rows.IsVisible ? "expanded" : "collapsed");
        }

        rows.IsVisible = open;
        button.Click += (_, _) =>
        {
            rows.IsVisible = !rows.IsVisible;
            OpenGroups[heading] = rows.IsVisible;
            Show();
        };
        Show();
        _refreshers.Add(Show);
        return button;
    }

    private TextBlock CardHeading(string text) => new()
    {
        Text = text,
        FontSize = NativeTokens.CardTitle,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, NativeTokens.Space12, 0, NativeTokens.Space4),
    };

    // ── the tree ─────────────────────────────────────────────────────────────────────────────────

    private DockPanel TreeColumn()
    {
        // The list's own head says what it holds and offers the one thing that makes more of it. A world's characters are
        // added from the + on its row and from the world's own page; everything that acts on the one chosen (copy, move,
        // delete) is on its page, beside its name, so no button here acts on something the eye has to go and find.
        AddWorldButton = AccentButton("+ Add world");
        AddWorldButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        AddWorldButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(AddWorldButton, "Add a world by hand: a name, a host and a port");
        AutomationProperties.SetName(AddWorldButton, "Add a world");
        AddWorldButton.Click += (_, _) => ShowWorld(_form.AddWorld(), null);

        var head = new StackPanel { Spacing = NativeTokens.Space10, Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space12, NativeTokens.Space12, NativeTokens.Space8) };
        var count = _form.WorldList().Count;
        var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        title.Children.Add(new TextBlock { Text = "WORLDS", FontSize = NativeTokens.PageSmall, FontWeight = FontWeight.SemiBold, Foreground = DimInk, VerticalAlignment = VerticalAlignment.Center });
        var tally = new TextBlock { Text = count.ToString(CultureInfo.InvariantCulture), FontSize = NativeTokens.PageSmall, Foreground = DimInk, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(tally, 1);
        title.Children.Add(tally);
        head.Children.Add(title);
        head.Children.Add(AddWorldButton);

        _worldTree = new ListBox { Background = Brushes.Transparent, Margin = new Thickness(NativeTokens.Space4, 0, NativeTokens.Space4, NativeTokens.Space6) };
        AutomationProperties.SetName(_worldTree, "Worlds and characters");
        _worldTree.SelectionChanged += (_, _) => OnTreeChosen();
        _worldTree.KeyDown += OnTreeKey;

        var foot = new StackPanel { Spacing = NativeTokens.Space6, Margin = new Thickness(NativeTokens.Space12, NativeTokens.Space8, NativeTokens.Space12, NativeTokens.Space12) };
        if (_form.RunCommand is not null)
        {
            foot.Children.Add(new Border { Height = NativeTokens.Hairline, Background = _tokens.Page.Brush(_tokens.Page.Rule), Margin = new Thickness(0, 0, 0, NativeTokens.Space4) });
            var browse = WideButton("Browse worlds…", "Find a world in the mu*index directory");
            AutomationProperties.SetName(browse, "Browse worlds");
            browse.Click += (_, _) => _form.Run(SettingsForm.BrowseWorlds);
            var import = WideButton("Import from another client…", "Bring worlds and automation over from MUSHclient, Mudlet or TinTin++");
            AutomationProperties.SetName(import, "Import from another client");
            import.Click += (_, _) => _form.Run(SettingsForm.ImportWorlds);
            foot.Children.Add(browse);
            foot.Children.Add(import);
        }

        var column = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        column.Children.Add(head);
        DockPanel.SetDock(foot, Dock.Bottom);
        column.Children.Add(foot);
        column.Children.Add(_worldTree);
        FillTree();
        return column;
    }

    /// <summary>A secondary button as wide as the list, for the list's foot.</summary>
    private static Button WideButton(string text, string tip)
    {
        var button = new Button
        {
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(NativeTokens.Space12, NativeTokens.Space6),
            FontSize = NativeTokens.Label,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Button TreeButton(string text, string tip)
    {
        var button = new Button { Content = text, Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space4), MinWidth = 32, FontSize = NativeTokens.Label, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        return button;
    }

    private void FillTree()
    {
        if (_worldTree is not { } tree)
        {
            return;
        }

        var items = new List<ListBoxItem>();
        AddCharacterRowButtons.Clear();
        ListBoxItem? selected = null;
        foreach (var world in _form.WorldList())
        {
            var row = WorldRow(world);
            items.Add(row);
            if (_shownCharacter is null && ReferenceEquals(world.World, _shownWorld))
            {
                selected = row;
            }

            foreach (var character in world.Characters)
            {
                var item = CharacterRow(character);
                items.Add(item);
                if (ReferenceEquals(character.Character, _shownCharacter))
                {
                    selected = item;
                }
            }
        }

        if (items.Count == 0)
        {
            items.Add(new ListBoxItem
            {
                Content = new TextBlock { Text = "No world yet.", Foreground = DimInk },
                IsEnabled = false,
                Focusable = false,
            });
        }

        _choosing = true;
        tree.ItemsSource = items;
        tree.SelectedItem = selected;
        _choosing = false;
        UpdateTreeTools();
    }

    private ListBoxItem WorldRow(WorldListEntry world)
    {
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = world.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var address = world.Tls ? $"{world.Address} · TLS" : world.Address;
        words.Children.Add(new TextBlock { Text = address, FontSize = NativeTokens.Small, Foreground = DimInk, TextTrimming = TextTrimming.CharacterEllipsis });

        // A character is added where it will appear: the + on its world's row. The button takes the press, so the row is
        // not chosen by it; the new character is shown once it lands.
        var add = new Button
        {
            Content = "+",
            FontSize = NativeTokens.Heading,
            FontWeight = FontWeight.SemiBold,
            Width = NativeTokens.ControlHeightSmall,
            Height = NativeTokens.ControlHeightSmall,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            CornerRadius = new CornerRadius(NativeTokens.RadiusControl),
        };
        ToolTip.SetTip(add, $"Add a character to {world.Name}");
        AutomationProperties.SetName(add, $"Add a character to {world.Name}");
        var owner = world.World;
        add.Click += (_, _) => ShowWorld(owner, _form.AddCharacter(owner));
        AddCharacterRowButtons[owner] = add;
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        line.Children.Add(words);
        Grid.SetColumn(add, 1);
        line.Children.Add(add);

        var item = new ListBoxItem
        {
            Content = line,
            Tag = world.World,
            Padding = new Thickness(NativeTokens.Space10, NativeTokens.Space6),
            Margin = new Thickness(0, NativeTokens.Space4, 0, 0),
            MinHeight = 0,
            CornerRadius = new CornerRadius(NativeTokens.RadiusControl),
        };
        var count = world.Characters.Count;
        AutomationProperties.SetName(item, $"World {world.Name}, {address}, {count.ToString(CultureInfo.InvariantCulture)} character{(count == 1 ? string.Empty : "s")}");
        return item;
    }

    private ListBoxItem CharacterRow(CharacterListEntry character)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space8 };
        row.Children.Add(PresenceDot(character.Connected));
        row.Children.Add(new TextBlock { Text = character.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var tags = new List<string>();
        if (character.Connected)
        {
            tags.Add("connected");
        }

        if (character.AtStart)
        {
            tags.Add("at start");
        }

        if (tags.Count > 0)
        {
            row.Children.Add(new TextBlock { Text = string.Join(" · ", tags), FontSize = NativeTokens.Small, Foreground = DimInk, VerticalAlignment = VerticalAlignment.Center });
        }

        var item = new ListBoxItem
        {
            Content = row,
            Tag = character.Character,
            Padding = new Thickness(NativeTokens.Space28, NativeTokens.Space6, NativeTokens.Space10, NativeTokens.Space6),
            MinHeight = 0,
            CornerRadius = new CornerRadius(NativeTokens.RadiusControl),
        };
        AutomationProperties.SetName(item, $"Character {character.Name}{(tags.Count > 0 ? ", " + string.Join(", ", tags) : string.Empty)}");
        return item;
    }

    /// <summary>The sidebar's presence mark: the accent filled while connected, a ring while not.</summary>
    private Ellipse PresenceDot(bool connected)
    {
        var accent = _tokens.Brush(_tokens.Accent);
        return new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = connected ? accent : Brushes.Transparent,
            Stroke = accent,
            StrokeThickness = connected ? 0 : NativeTokens.Hairline,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void OnTreeChosen()
    {
        if (_choosing || _worldTree?.SelectedItem is not ListBoxItem { Tag: { } tag })
        {
            return;
        }

        switch (tag)
        {
            case WorldDefinition world when !ReferenceEquals(world, _shownWorld) || _shownCharacter is not null:
                ShowWorld(world, null);
                break;
            case CharacterDefinition character when !ReferenceEquals(character, _shownCharacter):
                ShowWorld(_form.WorldList().FirstOrDefault(w => w.World.Characters.Contains(character))?.World, character);
                break;
        }

        _worldTree?.Focus();
    }

    private void OnTreeKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            AskDelete();
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Alt && e.Key is Key.Up or Key.Down)
        {
            MoveShown(e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
        }
    }

    private void UpdateTreeTools()
    {
        var hasWorld = _shownWorld is not null;
        if (AddCharacterButton is not null)
        {
            AddCharacterButton.IsEnabled = hasWorld;
        }

        if (CopyCharacterButton is not null)
        {
            CopyCharacterButton.IsEnabled = _shownCharacter is not null;
        }

        if (DeleteButton is not null)
        {
            DeleteButton.IsEnabled = hasWorld;
        }

        var (count, at) = _shownCharacter is { } character && _shownWorld is { } owner
            ? (owner.Characters.Count, owner.Characters.IndexOf(character))
            : (_form.WorldList().Count, _form.WorldList().ToList().FindIndex(w => ReferenceEquals(w.World, _shownWorld)));
        if (MoveUpButton is not null)
        {
            MoveUpButton.IsEnabled = hasWorld && at > 0;
        }

        if (MoveDownButton is not null)
        {
            MoveDownButton.IsEnabled = hasWorld && at >= 0 && at < count - 1;
        }
    }

    private void MoveShown(int delta)
    {
        if (_shownCharacter is { } character)
        {
            _form.MoveCharacter(character, delta);
        }
        else if (_shownWorld is { } world)
        {
            _form.MoveWorld(world, delta);
        }
    }

    // ── the detail ───────────────────────────────────────────────────────────────────────────────

    private Control EmptyWorlds()
    {
        var body = new StackPanel { Spacing = NativeTokens.Space8, Margin = new Thickness(0, NativeTokens.Space16) };
        body.Children.Add(new TextBlock { Text = "No world yet", FontSize = NativeTokens.CardTitle, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock { Text = "Add one by hand, find one in the directory, or bring your worlds over from another client.", Foreground = DimInk, TextWrapping = TextWrapping.Wrap });
        var add = new Button { Content = "Add a world", Margin = new Thickness(0, NativeTokens.Space8, 0, 0) };
        AutomationProperties.SetName(add, "Add a world");
        add.Click += (_, _) => ShowWorld(_form.AddWorld(), null);
        body.Children.Add(add);
        return Card(body);
    }

    /// <summary>
    /// The detail's head: where it is (a character's world, as a way back), the name, what it is, and a row of what can be
    /// done to it — the call to action first (Connect, or + Add character), then copy, move and delete — and a deletion
    /// waiting for its yes.
    /// </summary>
    private void FillHead()
    {
        if (_worldHead is not { } head || _shownWorld is not { } world)
        {
            return;
        }

        head.Children.Clear();
        ConnectButton = null;
        ConfirmDeleteButton = null;
        AddCharacterButton = null;
        CopyCharacterButton = null;
        var entry = _form.WorldList().FirstOrDefault(w => ReferenceEquals(w.World, world));
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, NativeTokens.Space6, 0, 0) };
        if (_shownCharacter is { } character)
        {
            var back = LinkButton($"‹ {world.Name}", NativeTokens.Label);
            ToolTip.SetTip(back, $"Back to {world.Name}");
            AutomationProperties.SetName(back, $"Back to {world.Name}");
            back.Click += (_, _) => ShowWorld(world, null);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            head.Children.Add(back);

            var listed = entry?.Characters.FirstOrDefault(c => ReferenceEquals(c.Character, character));
            var connected = listed?.Connected == true;
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space10 };
            title.Children.Add(PresenceDot(connected));
            title.Children.Add(new TextBlock { Text = character.Name, FontSize = NativeTokens.PageTitle, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            head.Children.Add(title);
            head.Children.Add(new TextBlock
            {
                Text = $"Character on {world.Name} · {WorldEditing.Address(world)} · {(connected ? "connected" : "not connected")}",
                FontSize = NativeTokens.Label,
                Foreground = DimInk,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            if (_form.CanConnect && !string.IsNullOrWhiteSpace(world.Host))
            {
                var connect = AccentButton(connected ? "Go to" : "Connect");
                AutomationProperties.SetName(connect, connected ? $"Go to {character.Name}" : $"Connect {character.Name}");
                ToolTip.SetTip(connect, connected ? "Bring this character's window forward" : $"Connect {character.Name} to {world.Name}");
                connect.Click += (_, _) => _form.ConnectTo(world, character);
                ConnectButton = connect;
                AddAction(actions, connect);
            }

            CopyCharacterButton = HeadButton("Duplicate", $"Add a copy of {character.Name} to {world.Name}");
            CopyCharacterButton.Click += (_, _) =>
            {
                if (_shownCharacter is { } shown && _form.DuplicateCharacter(shown) is { } copy)
                {
                    ShowWorld(_shownWorld, copy);
                }
            };
            AddAction(actions, CopyCharacterButton);
        }
        else
        {
            head.Children.Add(new TextBlock { Text = world.Name, FontSize = NativeTokens.PageTitle, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var count = world.Characters.Count;
            var online = entry?.Connected ?? 0;
            var parts = new List<string> { "World", WorldEditing.Address(world) };
            if (world.UseTls)
            {
                parts.Add("TLS");
            }

            parts.Add($"{count.ToString(CultureInfo.InvariantCulture)} character{(count == 1 ? string.Empty : "s")}");
            if (online > 0)
            {
                parts.Add($"{online.ToString(CultureInfo.InvariantCulture)} connected");
            }

            head.Children.Add(new TextBlock { Text = string.Join(" · ", parts), FontSize = NativeTokens.Label, Foreground = DimInk, TextTrimming = TextTrimming.CharacterEllipsis });

            AddCharacterButton = AccentButton("+ Add character");
            ToolTip.SetTip(AddCharacterButton, $"Add a character to {world.Name}");
            AutomationProperties.SetName(AddCharacterButton, $"Add a character to {world.Name}");
            AddCharacterButton.Click += (_, _) =>
            {
                if (_shownWorld is { } shown)
                {
                    ShowWorld(shown, _form.AddCharacter(shown));
                }
            };
            AddAction(actions, AddCharacterButton);
        }

        var noun = _shownCharacter is null ? "world" : "character";
        MoveUpButton = HeadButton("Move up", $"Move this {noun} up the list (Alt+Up)");
        MoveUpButton.Click += (_, _) => MoveShown(-1);
        MoveDownButton = HeadButton("Move down", $"Move this {noun} down the list (Alt+Down)");
        MoveDownButton.Click += (_, _) => MoveShown(1);
        DeleteButton = HeadButton($"Delete {noun}", $"Delete this {noun}; Ctrl+Z brings it back");
        DeleteButton.Foreground = _tokens.Brush(_tokens.Warn);
        DeleteButton.Click += (_, _) => AskDelete();
        AddAction(actions, MoveUpButton);
        AddAction(actions, MoveDownButton);
        AddAction(actions, DeleteButton);
        head.Children.Add(actions);
        UpdateTreeTools();

        if (_pendingDeletion is { } doomed)
        {
            head.Children.Add(DeletionQuestion(doomed));
        }
    }

    private static void AddAction(WrapPanel actions, Button button)
    {
        button.Margin = new Thickness(0, 0, NativeTokens.Space8, NativeTokens.Space4);
        actions.Children.Add(button);
    }

    /// <summary>A secondary action in the detail's head.</summary>
    private static Button HeadButton(string text, string tip)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(NativeTokens.Space12, NativeTokens.Space6),
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(NativeTokens.RadiusControl),
        };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        return button;
    }

    /// <summary>A button in the accent: the page's one call to action.</summary>
    private Button AccentButton(string text)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(NativeTokens.Space16, NativeTokens.Space6),
            Background = _tokens.Brush(_tokens.Accent),
            Foreground = _tokens.Brush(_tokens.AccentInk),
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(NativeTokens.RadiusControl),
        };
        button.Resources["ButtonBackgroundPointerOver"] = _tokens.Brush(_tokens.Accent);
        button.Resources["ButtonForegroundPointerOver"] = _tokens.Brush(_tokens.AccentInk);
        button.Resources["ButtonBackgroundPressed"] = _tokens.Brush(_tokens.Accent);
        button.Resources["ButtonForegroundPressed"] = _tokens.Brush(_tokens.AccentInk);
        return button;
    }

    private void AskDelete()
    {
        var doomed = (object?)_shownCharacter ?? _shownWorld;
        if (doomed is null)
        {
            return;
        }

        _pendingDeletion = doomed;
        FillHead();
        ConfirmDeleteButton?.Focus();
    }

    private Border DeletionQuestion(object doomed)
    {
        var what = doomed switch
        {
            WorldDefinition w => WorldEditing.Deletion(w),
            CharacterDefinition c => WorldEditing.Deletion(c),
            _ => string.Empty,
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock { Text = $"Delete {what}?", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        words.Children.Add(new TextBlock { Text = "Ctrl+Z on this page brings it back.", FontSize = NativeTokens.PageSmall, Foreground = DimInk });
        row.Children.Add(words);
        var yes = new Button { Content = "Delete", Foreground = _tokens.Brush(_tokens.Warn), Margin = new Thickness(NativeTokens.Space12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(yes, $"Delete {what}");
        yes.Click += (_, _) => Delete(doomed);
        ConfirmDeleteButton = yes;
        Grid.SetColumn(yes, 1);
        row.Children.Add(yes);
        var no = new Button { Content = "Keep", Margin = new Thickness(NativeTokens.Space8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(no, $"Keep {what}");
        no.Click += (_, _) =>
        {
            _pendingDeletion = null;
            FillHead();
        };
        Grid.SetColumn(no, 2);
        row.Children.Add(no);
        return new Border
        {
            Child = row,
            Background = _tokens.Page.Brush(_tokens.Page.Card),
            BorderBrush = _tokens.Brush(_tokens.Warn),
            BorderThickness = new Thickness(NativeTokens.Hairline),
            CornerRadius = new CornerRadius(NativeTokens.RadiusCard),
            Padding = new Thickness(NativeTokens.Space16, NativeTokens.Space10),
        };
    }

    private void Delete(object doomed)
    {
        _pendingDeletion = null;
        switch (doomed)
        {
            case CharacterDefinition character when _shownWorld is { } world:
                _form.DeleteCharacter(character);
                _shownCharacter = null;
                ShowWorld(world, null);
                break;
            case WorldDefinition world:
                var worlds = _form.WorldList();
                var at = worlds.ToList().FindIndex(w => ReferenceEquals(w.World, world));
                var next = worlds.Count > 1 ? worlds[at + 1 < worlds.Count ? at + 1 : Math.Max(0, at - 1)].World : null;
                _form.DeleteWorld(world);
                ShowWorld(ReferenceEquals(next, world) ? null : next, null);
                break;
        }
    }

    /// <summary>A world's characters, each a row with its state, a connect button, and a click that opens it.</summary>
    private void FillCharacters(WorldDefinition world)
    {
        if (_worldCharacters is not { } body)
        {
            return;
        }

        body.Children.Clear();
        body.Children.Add(CardHeading("Characters"));

        var entry = _form.WorldList().FirstOrDefault(w => ReferenceEquals(w.World, world));
        if (entry is null || entry.Characters.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "No character yet. Add one to log in on connect; without one the world connects with no login.",
                Foreground = DimInk,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, NativeTokens.Space4, 0, NativeTokens.Space12),
            });
            return;
        }

        var first = true;
        foreach (var character in entry.Characters)
        {
            if (!first)
            {
                body.Children.Add(new Border { Height = NativeTokens.Hairline, Background = _tokens.Page.Brush(_tokens.Page.Rule) });
            }

            first = false;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, NativeTokens.Space8), MinHeight = NativeTokens.ControlHeight };
            row.Children.Add(PresenceDot(character.Connected));
            var words = new StackPanel { Margin = new Thickness(NativeTokens.Space10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(new TextBlock { Text = character.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var facts = new List<string> { character.Connected ? "connected" : "not connected" };
            if (character.AtStart)
            {
                facts.Add("connects at start");
            }

            facts.Add(character.Sets == 1 ? "1 trigger set" : $"{character.Sets.ToString(CultureInfo.InvariantCulture)} trigger sets");
            words.Children.Add(new TextBlock { Text = string.Join(" · ", facts), FontSize = NativeTokens.PageSmall, Foreground = DimInk, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(words, 1);
            row.Children.Add(words);

            var open = LinkButton("Settings", NativeTokens.PageSmall);
            AutomationProperties.SetName(open, $"{character.Name}'s settings");
            var target = character.Character;
            open.Click += (_, _) => ShowWorld(world, target);
            open.Margin = new Thickness(NativeTokens.Space12, 0);
            Grid.SetColumn(open, 2);
            row.Children.Add(open);

            if (_form.CanConnect && !string.IsNullOrWhiteSpace(world.Host))
            {
                var connect = new Button { Content = character.Connected ? "Go to" : "Connect", VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(NativeTokens.Space12, NativeTokens.Space4) };
                AutomationProperties.SetName(connect, character.Connected ? $"Go to {character.Name}" : $"Connect {character.Name}");
                connect.Click += (_, _) => _form.ConnectTo(world, target);
                Grid.SetColumn(connect, 3);
                row.Children.Add(connect);
            }

            body.Children.Add(row);
        }
    }

    /// <summary>
    /// A character's trigger sets: a check box each, those it uses first in the order that decides a clash (with the
    /// arrows that change it), then the rest, then a package's for this world, read-only.
    /// </summary>
    private void FillSets(CharacterDefinition character)
    {
        if (_worldSets is not { } body)
        {
            return;
        }

        body.Children.Clear();
        SetBoxes.Clear();
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(CardHeading("Trigger sets"));
        if (_form.Section("triggers") is { } triggers && SettingsForm.IsDeclared(triggers))
        {
            var edit = LinkButton("Edit their rules", NativeTokens.PageSmall);
            AutomationProperties.SetName(edit, "Edit the sets' rules on the Triggers page");
            edit.Click += (_, _) => Choose(triggers.Id);
            Grid.SetColumn(edit, 1);
            heading.Children.Add(edit);
        }

        body.Children.Add(heading);
        body.Children.Add(new TextBlock
        {
            Text = "The sets this character's triggers, aliases, keys and timers come from. Where two sets have a rule for the same thing, the one higher in this list wins.",
            FontSize = NativeTokens.PageSmall,
            Foreground = DimInk,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, NativeTokens.Space8),
        });

        var assignments = _form.Assignments(character);
        if (assignments.Count == 0)
        {
            body.Children.Add(new TextBlock { Text = "There is no trigger set yet. Make one on the Triggers page.", Foreground = DimInk, Margin = new Thickness(0, 0, 0, NativeTokens.Space12) });
            return;
        }

        var used = assignments.Count(a => a.On && a.Package is null);
        foreach (var set in assignments)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, NativeTokens.Space4), MinHeight = NativeTokens.ControlHeight };
            var box = new CheckBox { IsChecked = set.On, IsEnabled = set.Package is null, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
            AutomationProperties.SetName(box, set.Package is null ? $"Use {set.Name}" : $"{set.Name}, from {set.Package}, always on for this world");
            var assigned = set.Set;
            box.IsCheckedChanged += (_, _) =>
            {
                if (!_syncing && box.IsChecked is { } on && on != set.On)
                {
                    _form.Assign(character, assigned, on);
                }
            };
            if (set.Package is null)
            {
                SetBoxes[set.Name] = box;
            }

            row.Children.Add(box);

            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(NativeTokens.Space4, 0, NativeTokens.Space8, 0) };
            var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space8 };
            if (set.On)
            {
                title.Children.Add(RankBadge(set.Rank));
            }

            title.Children.Add(new TextBlock { Text = set.Name, FontWeight = set.On ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            words.Children.Add(title);
            var facts = new List<string> { set.Rules == 1 ? "1 rule" : $"{set.Rules.ToString(CultureInfo.InvariantCulture)} rules" };
            if (set.Package is { } package)
            {
                facts.Add($"from {package}, for this world");
            }

            if (set.Description is { } description)
            {
                facts.Add(description);
            }

            words.Children.Add(new TextBlock { Text = string.Join(" · ", facts), FontSize = NativeTokens.PageSmall, Foreground = DimInk, TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(words, 1);
            row.Children.Add(words);

            if (set.On && set.Package is null && used > 1)
            {
                var arrows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = NativeTokens.Space4, VerticalAlignment = VerticalAlignment.Center };
                var up = TreeButton("↑", $"Let {set.Name} win more clashes");
                up.IsEnabled = set.Rank > 1;
                up.Click += (_, _) => _form.MoveAssignment(character, assigned, -1);
                var down = TreeButton("↓", $"Let {set.Name} win fewer clashes");
                down.IsEnabled = set.Rank < used;
                down.Click += (_, _) => _form.MoveAssignment(character, assigned, 1);
                arrows.Children.Add(up);
                arrows.Children.Add(down);
                Grid.SetColumn(arrows, 2);
                row.Children.Add(arrows);
            }

            body.Children.Add(row);
        }

        body.Children.Add(new Border { Height = NativeTokens.Space8 });
    }

    /// <summary>A set's place in the clash order, as a small accent pill.</summary>
    private Border RankBadge(int rank) => new()
    {
        Child = new TextBlock { Text = rank.ToString(CultureInfo.InvariantCulture), FontSize = NativeTokens.Small, FontWeight = FontWeight.SemiBold, Foreground = _tokens.Brush(_tokens.AccentInk), HorizontalAlignment = HorizontalAlignment.Center },
        Background = _tokens.Brush(_tokens.Accent),
        CornerRadius = new CornerRadius(NativeTokens.RadiusM),
        MinWidth = 18,
        Padding = new Thickness(NativeTokens.Space4, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>What the world's server has said about itself: the cell report's model, drawn as a card.</summary>
    private Border ServerInfoCard(WorldDefinition world)
    {
        var body = new StackPanel();
        body.Children.Add(CardHeading("Server information"));
        var model = _form.ServerInfoFor(world);
        void Line(string text, bool quiet = true)
        {
            ServerInfoLines.Add(text);
            body.Children.Add(new TextBlock { Text = text, Foreground = quiet ? DimInk : null, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, NativeTokens.Space8) });
        }

        switch (model?.State)
        {
            case null:
            case ServerInfoState.NeverConnected:
                Line(ServerInfoModel.NeverConnected);
                break;
            case ServerInfoState.PublishesNothing:
                Line(ServerInfoModel.NoMssp);
                if (model.LastSeen is { } seen)
                {
                    Line($"Last seen {seen}");
                }

                break;
            default:
                Line($"What the server said about itself (MSSP), {model.Captured}.");
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*"), Margin = new Thickness(0, 0, 0, NativeTokens.Space12) };
                var row = 0;
                foreach (var info in model.Headline.Concat(model.Others).Where(r => r.Values is not [ServerInfoModel.Unreported]))
                {
                    grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    var label = new TextBlock { Text = info.Unofficial ? $"{info.Label} ·" : info.Label, Foreground = DimInk, FontSize = NativeTokens.PageSmall, Margin = new Thickness(0, NativeTokens.Space2), TextTrimming = TextTrimming.CharacterEllipsis };
                    if (info.Unofficial)
                    {
                        ToolTip.SetTip(label, ServerInfoModel.UnofficialLegend);
                    }

                    Grid.SetRow(label, row);
                    grid.Children.Add(label);
                    var text = string.Join(", ", info.Values) + (info.More > 0 ? $" … {info.More.ToString(CultureInfo.InvariantCulture)} more" : string.Empty);
                    var value = new SelectableTextBlock { Text = text, FontSize = NativeTokens.PageSmall, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, NativeTokens.Space2) };
                    Grid.SetRow(value, row);
                    Grid.SetColumn(value, 1);
                    grid.Children.Add(value);
                    ServerInfoLines.Add($"{info.Label}: {text}");
                    row++;
                }

                body.Children.Add(grid);
                break;
        }

        return Card(body);
    }

    // ── catching up ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What the page's own parts depend on beyond the fields' values: the worlds and characters there are, their names,
    /// addresses, who is connected, and the shown character's sets. The fields refresh themselves; these are refilled
    /// when it moves.
    /// </summary>
    private string WorldShape()
    {
        var parts = _form.WorldList().Select(w =>
            $"{w.Name}|{w.Address}|{w.Tls}|{string.Join(",", w.Characters.Select(c => $"{c.Name}:{c.Connected}:{c.AtStart}:{c.Sets}"))}");
        var sets = _shownCharacter is { } character
            ? string.Join(",", _form.Assignments(character).Select(a => $"{a.Name}:{a.On}:{a.Rank}:{a.Rules}"))
            : string.Empty;
        return string.Join(";", parts) + "#" + sets;
    }

    /// <summary>Whether <paramref name="worlds"/> holds <paramref name="world"/>, and <paramref name="character"/> under it when one is named.</summary>
    private static bool Holds(IReadOnlyList<WorldListEntry> worlds, WorldDefinition world, CharacterDefinition? character) =>
        worlds.Any(w => ReferenceEquals(w.World, world)) && (character is null || world.Characters.Contains(character));

    private void RefreshWorldsPage()
    {
        var worlds = _form.WorldList();
        var arrived = _pendingShow is { } pending && Holds(worlds, pending.World, pending.Character);
        var gone = arrived
            || (_shownWorld is not null && !worlds.Any(w => ReferenceEquals(w.World, _shownWorld)))
            || (_shownCharacter is not null && !worlds.Any(w => w.World.Characters.Contains(_shownCharacter)))
            || (_shownWorld is null && worlds.Count > 0);
        if (gone)
        {
            // What was shown went (an undo, or another surface): the page is rebuilt around what is left, after this refresh.
            Dispatcher.UIThread.Post(() =>
            {
                var offset = _scroller.Offset;
                BuildPage();
                _scroller.Offset = offset;
            });
            return;
        }

        var shape = WorldShape();
        if (string.Equals(shape, _worldShape, StringComparison.Ordinal))
        {
            UpdateTreeTools();
            return;
        }

        _worldShape = shape;
        FillTree();
        FillHead();
        if (_shownWorld is { } world && _shownCharacter is null)
        {
            FillCharacters(world);
        }

        if (_shownCharacter is { } character)
        {
            FillSets(character);
        }
    }
}
