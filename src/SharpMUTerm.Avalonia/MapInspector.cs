using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SharpMUTerm.Client.Panels;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's inspector for the room being edited (<see cref="MapViewModel.Selected"/>): a small card in the map's top
/// right corner with the room's name, area, note, symbol, weight, lock and exits, each edited in place, and nudge, delete,
/// undo and done buttons. It says what the cell map's side column says, from the same model, and every change it makes is
/// a <see cref="MapEdit"/> handed to the client (<see cref="PanelOverlay.EditMap"/>), which checks it and applies it the way
/// a cell key's edit is applied. It never edits the map itself: the fields show what the client published, and an edit the
/// client refuses is simply put back on the next model.
/// </summary>
internal sealed class MapInspector : Border
{
    internal const double CardWidth = 272;

    private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _where = new() { Opacity = 0.75 };
    private readonly TextBlock _pending = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly StackPanel _exits = new() { Spacing = 2 };
    private readonly TextBlock _mine = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.85, IsVisible = false };
    private readonly Button _undo;
    private readonly Button _delete;
    private MapViewModel _model = new(null, 0, [], [], null);
    private Action<MapEdit>? _edit;
    private Action<long?>? _select;
    private Color _ink = Colors.White;

    public MapInspector()
    {
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Width = CardWidth;
        Margin = new Thickness(0, 30, 8, 8);
        Padding = new Thickness(10, 8);
        CornerRadius = new CornerRadius(NativeTokens.RadiusM);
        BorderThickness = new Thickness(1);
        IsVisible = false;
        AutomationProperties.SetName(this, "Room inspector");

        NameBox = Field("Name");
        AreaBox = Field("Area");
        NoteBox = Field("Note");
        SymbolBox = Field("Symbol");
        SymbolBox.MaxLength = 1;
        WeightBox = Field("Weight");
        ExitBox = Field("Exit");
        ExitBox.PlaceholderText = "direction or command";
        TargetBox = Field("Leads to");
        TargetBox.PlaceholderText = "room number, or next";
        LockBox = new CheckBox { Content = "Locked: no walk goes through it" };
        LockBox.IsCheckedChanged += (_, _) =>
        {
            if (_model.Selected is { } room && LockBox.IsChecked is { } locked && locked != room.Locked)
            {
                _edit?.Invoke(MapEdit.Lock(room.Id, locked));
            }
        };

        Commit(NameBox, (room, text) => text != room.Name ? MapEdit.Rename(room.Id, text) : null);
        Commit(AreaBox, (room, text) => text != (room.Area ?? string.Empty) ? MapEdit.Area(room.Id, text) : null);
        Commit(NoteBox, (room, text) => text != (room.Note ?? string.Empty) ? MapEdit.Note(room.Id, text) : null);
        Commit(SymbolBox, (room, text) => text != (room.Symbol ?? string.Empty) ? MapEdit.Symbol(room.Id, text) : null);
        Commit(WeightBox, (room, text) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var weight) && weight != room.Weight ? MapEdit.Weight(room.Id, weight) : null);

        var add = Small("Add exit", AddExit);
        TargetBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                AddExit();
            }
        };

        _undo = Small("Undo", () => _edit?.Invoke(MapEdit.Undo));
        _delete = Small("Delete room", () =>
        {
            if (_model.Selected is { } room)
            {
                _edit?.Invoke(MapEdit.Delete(room.Id));
            }
        });
        var done = Small("Done", () => _select?.Invoke(null));
        ResetButton = Small("Reset to server", () =>
        {
            if (_model.Selected is { } room)
            {
                _edit?.Invoke(MapEdit.ResetToServer(room.Id));
            }
        });
        ToolTip.SetTip(ResetButton, "Let the server's room reports set the fields you edited by hand again (R on the cell map)");

        var nudge = new WrapPanel();
        foreach (var (label, dx, dy, dz) in new[] { ("West", -1, 0, 0), ("North", 0, -1, 0), ("South", 0, 1, 0), ("East", 1, 0, 0), ("Level up", 0, 0, 1), ("Level down", 0, 0, -1) })
        {
            nudge.Children.Add(Small(label, () =>
            {
                if (_model.Selected is { } room)
                {
                    _edit?.Invoke(MapEdit.Move(room.Id, dx, dy, dz));
                }
            }));
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 4, ColumnSpacing = 8 };
        var row = 0;
        void Row(string label, Control field)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 };
            Grid.SetRow(text, row);
            Grid.SetRow(field, row);
            Grid.SetColumn(field, 1);
            grid.Children.Add(text);
            grid.Children.Add(field);
            row++;
        }

        Row("Name", NameBox);
        Row("Area", AreaBox);
        Row("Note", NoteBox);
        Row("Symbol", SymbolBox);
        Row("Weight", WeightBox);

        var newExit = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,Auto"), ColumnSpacing = 4 };
        Grid.SetColumn(TargetBox, 1);
        Grid.SetColumn(add, 2);
        newExit.Children.Add(ExitBox);
        newExit.Children.Add(TargetBox);
        newExit.Children.Add(add);

        var buttons = new WrapPanel();
        buttons.Children.Add(_undo);
        buttons.Children.Add(_delete);
        buttons.Children.Add(ResetButton);
        buttons.Children.Add(done);

        Child = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                _title,
                _where,
                grid,
                _mine,
                LockBox,
                new TextBlock { Text = "Exits", Opacity = 0.75 },
                _exits,
                newExit,
                _pending,
                new TextBlock { Text = "Move", Opacity = 0.75 },
                nudge,
                buttons,
            },
        };

        // Nothing pressed on the card reaches the map under it: a press here is never a pan, a walk or a pane menu.
        PointerPressed += (_, e) => e.Handled = true;
        PointerReleased += (_, e) => e.Handled = true;
        PointerWheelChanged += (_, e) => e.Handled = true;
    }

    internal TextBox NameBox { get; }

    internal TextBox AreaBox { get; }

    internal TextBox NoteBox { get; }

    internal TextBox SymbolBox { get; }

    internal TextBox WeightBox { get; }

    internal TextBox ExitBox { get; }

    internal TextBox TargetBox { get; }

    internal CheckBox LockBox { get; }

    /// <summary>Gives the room's hand-edited fields back to the server; enabled only while it has some.</summary>
    internal Button ResetButton { get; }

    /// <summary>What the card says about the fields edited by hand, or empty. Internal for tests.</summary>
    internal string HandEditedText => _mine.IsVisible ? _mine.Text ?? string.Empty : string.Empty;

    /// <summary>The room it shows, or null. Internal for tests.</summary>
    internal MapRoomDetails? Room => _model.Selected;

    /// <summary>Shows the room the model says is being edited, or hides when none is.</summary>
    public void Show(MapViewModel model, Color plane, Color text, Action<MapEdit>? edit, Action<long?>? select)
    {
        _edit = edit;
        _select = select;
        var previous = _model.Selected;
        _model = model;
        IsVisible = model.Selected is not null;
        if (model.Selected is not { } room)
        {
            return;
        }

        if (text != _ink || Background is null)
        {
            _ink = text;
            var tokens = NativeTokens.For(null, (0.299 * plane.R) + (0.587 * plane.G) + (0.114 * plane.B) < 140);
            Background = tokens.Brush(tokens.Card);
            BorderBrush = tokens.Brush(tokens.Edge);
            SetValue(global::Avalonia.Controls.Documents.TextElement.ForegroundProperty, tokens.Brush(tokens.Text));
            tokens.ApplySimple(Resources);
        }

        _title.Text = $"#{room.Id.ToString(CultureInfo.InvariantCulture)} {room.Name}";
        _where.Text = $"at {room.X.ToString(CultureInfo.InvariantCulture)}, {room.Y.ToString(CultureInfo.InvariantCulture)} · level {room.Z.ToString(CultureInfo.InvariantCulture)}";
        var other = previous?.Id != room.Id;
        Put(NameBox, room.Name, other);
        Put(AreaBox, room.Area ?? string.Empty, other);
        Put(NoteBox, room.Note ?? string.Empty, other);
        Put(SymbolBox, room.Symbol ?? string.Empty, other);
        Put(WeightBox, room.Weight.ToString(CultureInfo.InvariantCulture), other);
        LockBox.IsChecked = room.Locked;
        if (other)
        {
            ExitBox.Text = string.Empty;
            TargetBox.Text = string.Empty;
        }

        if (!room.Exits.SequenceEqual(previous?.Exits ?? []) || other)
        {
            _exits.Children.Clear();
            foreach (var exit in room.Exits)
            {
                var line = new DockPanel();
                var remove = Small("Remove", () => _edit?.Invoke(MapEdit.RemoveExit(room.Id, exit.Exit)));
                ToolTip.SetTip(remove, $"Remove the {exit.Exit} exit");
                DockPanel.SetDock(remove, Dock.Right);
                line.Children.Add(remove);
                line.Children.Add(new TextBlock
                {
                    Text = $"{exit.Exit}  {(exit.To is null ? "not walked" : exit.Name ?? "#" + exit.To.Value.ToString(CultureInfo.InvariantCulture))}",
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                _exits.Children.Add(line);
            }
        }

        _pending.Text = model.Pending ?? string.Empty;
        _pending.IsVisible = model.Pending is not null;
        _mine.Text = room.HandEdited.Count == 0 ? string.Empty : $"Edited by hand, kept from the server: {string.Join(", ", room.HandEdited)}";
        _mine.IsVisible = room.HandEdited.Count > 0;
        ResetButton.IsEnabled = room.HandEdited.Count > 0;
        _undo.IsEnabled = model.UndoLabel is not null;
        ToolTip.SetTip(_undo, model.UndoLabel is { } undo ? $"Undo {undo}" : "Nothing to undo");
        _delete.IsEnabled = model.Current?.Id != room.Id;
    }

    /// <summary>Puts the keyboard in the field a menu verb names (<see cref="MapEditing.TextVerbs"/>).</summary>
    public void FocusField(string verb)
    {
        var box = verb switch
        {
            MapEditing.RenameVerb => NameBox,
            MapEditing.AreaVerb => AreaBox,
            MapEditing.NoteVerb => NoteBox,
            MapEditing.SymbolVerb => SymbolBox,
            MapEditing.WeightVerb => WeightBox,
            MapEditing.ExitVerb => ExitBox,
            _ => null,
        };
        if (box is not null)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private void AddExit()
    {
        if (_model.Selected is not { } room)
        {
            return;
        }

        var exit = ExitBox.Text?.Trim() ?? string.Empty;
        var target = TargetBox.Text?.Trim() ?? string.Empty;
        MapEdit? edit = target.Equals("next", StringComparison.OrdinalIgnoreCase)
            ? MapEdit.LinkNext(room.Id, exit)
            : long.TryParse(target, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var to) ? MapEdit.AddExit(room.Id, exit, to) : null;
        if (edit is not null)
        {
            _edit?.Invoke(edit);
            ExitBox.Text = string.Empty;
            TargetBox.Text = string.Empty;
        }
    }

    /// <summary>A field shows the model's value unless the player is typing in it; a room changed always shows its own.</summary>
    private static void Put(TextBox box, string value, bool force)
    {
        if (force || !box.IsFocused)
        {
            box.Text = value;
        }
    }

    /// <summary>Enter, or leaving the field, hands the client the edit the field's text asks for, if it asks for one.</summary>
    private void Commit(TextBox box, Func<MapRoomDetails, string, MapEdit?> make)
    {
        void Run()
        {
            if (_model.Selected is { } room && make(room, box.Text?.Trim() ?? string.Empty) is { } edit)
            {
                _edit?.Invoke(edit);
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Run();
            }
        };
        box.LostFocus += (_, _) => Run();
    }

    private static TextBox Field(string name)
    {
        var box = new TextBox { MinHeight = 24, Padding = new Thickness(6, 2), FontSize = NativeTokens.Body, MaxLength = 200 };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static Button Small(string label, Action run)
    {
        var button = new Button { Content = label, Padding = new Thickness(8, 2), Margin = new Thickness(0, 0, 4, 4), FontSize = NativeTokens.Body, Focusable = false };
        button.Click += (_, _) => run();
        return button;
    }
}
