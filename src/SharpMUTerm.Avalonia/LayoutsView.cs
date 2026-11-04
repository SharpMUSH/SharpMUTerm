using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's ⌥M: the character's layouts, BUILT IN then YOURS, with what each is in use for, and a row of buttons
/// for what can be done to the one pointed at. Every button and key is a <see cref="LayoutVerb"/> the client runs
/// through the method the cell list's key runs, and what it said comes back as the list's status. The keys are the
/// cell list's: ⏎ apply, S save changes, N save as new, W use for the world, C use for this character, R rename and D
/// delete (a layout of the player's own only), ↑↓ move, Esc closes.
/// </summary>
internal sealed class LayoutsView : DockPanel, IModalView
{
    private readonly LayoutsSession _session;
    private readonly ModalColours _colours;
    private readonly Action _done;
    private readonly StackPanel _list;
    private readonly TextBlock _status;
    private readonly WrapPanel _buttons;
    private readonly TextBox _rename;
    private readonly List<Border> _rows = [];
    private readonly List<int> _rowIndex = [];
    private LayoutsModel _model;
    private int _selected;
    private bool _renaming;
    private bool _confirming;

    public LayoutsView(LayoutsSession session, ModalColours colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        _model = session.Model;
        _selected = _model.Selected;

        var heading = ModalParts.Heading("Layouts", colours);
        SetDock(heading, Dock.Top);
        Children.Add(heading);
        var whose = ModalParts.Dim($"How {_model.Subject}'s panes are arranged. Applying one never closes a window.", colours);
        SetDock(whose, Dock.Top);
        Children.Add(whose);

        _rename = new TextBox
        {
            Margin = new Thickness(14, 4, 14, 4),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(7),
            IsVisible = false,
            PlaceholderText = "New name",
        };
        global::Avalonia.Automation.AutomationProperties.SetName(_rename, "New name");

        var footer = ModalParts.Footer("↑↓ pick · Enter apply · Esc close", colours);
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);

        _buttons = new WrapPanel { Margin = new Thickness(14, 8, 14, 0), HorizontalAlignment = HorizontalAlignment.Right };
        SetDock(_buttons, Dock.Bottom);
        Children.Add(_buttons);

        _status = ModalParts.Dim(string.Empty, colours, new Thickness(20, 8, 20, 0));
        SetDock(_status, Dock.Bottom);
        Children.Add(_status);

        SetDock(_rename, Dock.Bottom);
        Children.Add(_rename);

        var (frame, list, _) = ModalParts.List(colours);
        _list = list;
        Children.Add(frame);

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        _session.Changed += OnChanged;
        Draw();
    }

    public string Title => "Layouts";

    public double PreferredWidth => 640;

    public bool Centred => false;

    /// <summary>The model drawn now, for a test.</summary>
    public LayoutsModel Model => _model;

    /// <summary>The row pointed at.</summary>
    public int Selected => _selected;

    /// <summary>The buttons offered now, by label, for a test.</summary>
    public IReadOnlyList<Button> Buttons => _buttons.Children.OfType<Button>().ToList();

    public void FocusFirst() => (_renaming ? (Control)_rename : this).Focus();

    public void Detach() => _session.Changed -= OnChanged;

    /// <summary>Runs <paramref name="verb"/> on the row pointed at.</summary>
    public void Run(LayoutVerb verb, string? name = null)
    {
        if (_selected < 0 || _selected >= _model.Rows.Count)
        {
            return;
        }

        var id = _model.Rows[_selected].Id;
        _renaming = false;
        _confirming = false;
        _session.Run(verb, id, name);
        if (_session.IsEnded)
        {
            _done();
        }
        else
        {
            Draw();
        }
    }

    private void OnChanged(LayoutsModel model) => Dispatcher.UIThread.Post(() =>
    {
        _model = model;
        _selected = model.Selected;
        Draw();
    });

    private void Draw()
    {
        Focusable = true;
        _list.Children.Clear();
        _rows.Clear();
        _rowIndex.Clear();
        _selected = _model.Rows.Count == 0 ? -1 : Math.Clamp(_selected, 0, _model.Rows.Count - 1);

        _list.Children.Add(ModalParts.GroupHeader("Built in", _colours, first: true));
        for (var i = 0; i < _model.Rows.Count; i++)
        {
            if (_model.Rows[i].BuiltIn)
            {
                AddRow(i);
            }
        }

        _list.Children.Add(ModalParts.GroupHeader("Yours", _colours, first: false));
        var own = 0;
        for (var i = 0; i < _model.Rows.Count; i++)
        {
            if (!_model.Rows[i].BuiltIn)
            {
                AddRow(i);
                own++;
            }
        }

        if (own == 0)
        {
            _list.Children.Add(ModalParts.Dim("None yet — Save as new keeps the panes as they are now.", _colours, new Thickness(12, 2, 12, 6)));
        }

        _status.Text = _confirming && _model.Current is { } doomed
            ? $"Delete {doomed.Name}? Y deletes, any other key keeps it."
            : _model.Status ?? string.Empty;
        _status.Foreground = _colours.Brush(_confirming ? _colours.Warn : _colours.Dim);
        _status.IsVisible = _status.Text.Length > 0;
        _rename.IsVisible = _renaming;
        DrawButtons();
        Highlight();
    }

    private void AddRow(int index)
    {
        var layout = _model.Rows[index];
        var line = new DockPanel();
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (layout.InUse)
        {
            tags.Children.Add(Chip("in use"));
        }

        if (layout.WorldDefault)
        {
            tags.Children.Add(Chip("world default"));
        }

        SetDock(tags, Dock.Right);
        line.Children.Add(tags);
        var words = new StackPanel();
        words.Children.Add(new TextBlock { Text = layout.Name, FontSize = 14, FontWeight = FontWeight.Medium, Foreground = _colours.Brush(_colours.Text) });
        words.Children.Add(new TextBlock
        {
            Text = layout.Description,
            FontSize = 12,
            Foreground = _colours.Brush(_colours.Dim),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        line.Children.Add(words);
        var row = ModalParts.Row(line, layout.Name, () => Select(index), () =>
        {
            Select(index);
            Run(LayoutVerb.Apply);
        });
        _rows.Add(row);
        _rowIndex.Add(index);
        _list.Children.Add(row);
    }

    private Border Chip(string text) => new()
    {
        Background = _colours.Brush(_colours.Selected),
        CornerRadius = new CornerRadius(9),
        Padding = new Thickness(8, 1),
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = _colours.Brush(_colours.Accent) },
    };

    private void DrawButtons()
    {
        _buttons.Children.Clear();
        if (_model.Current is not { } current)
        {
            return;
        }

        void Add(string label, string key, Action click, bool accent = false)
        {
            var button = ModalParts.Button(label, key, _colours, click, accent);
            button.Margin = new Thickness(6, 0, 0, 6);
            button.Focusable = false;
            _buttons.Children.Add(button);
        }

        if (_renaming)
        {
            Add("Rename", "Enter", () => Run(LayoutVerb.Rename, _rename.Text ?? string.Empty), accent: true);
            Add("Cancel", "Esc", CancelInline);
            return;
        }

        if (_confirming)
        {
            Add("Delete", "Y", () => Run(LayoutVerb.Delete), accent: true);
            Add("Keep", "N", CancelInline);
            return;
        }

        Add("Apply", "Enter", () => Run(LayoutVerb.Apply), accent: true);
        Add("Save changes", "S", () => Run(LayoutVerb.SaveChanges));
        Add("Save as new", "N", () => Run(LayoutVerb.SaveAsNew));
        Add("Use for world", "W", () => Run(LayoutVerb.UseForWorld));
        Add("Use for character", "C", () => Run(LayoutVerb.UseForCharacter));
        if (!current.BuiltIn)
        {
            Add("Rename", "R", StartRename);
            Add("Delete", "D", StartDelete);
        }
    }

    private void StartRename()
    {
        if (_model.Current is not { BuiltIn: false } current)
        {
            return;
        }

        _renaming = true;
        _confirming = false;
        _rename.Text = current.Name;
        Draw();
        _rename.Focus();
        _rename.SelectAll();
    }

    private void StartDelete()
    {
        if (_model.Current is { BuiltIn: false })
        {
            _confirming = true;
            _renaming = false;
            Draw();
            Focus();
        }
    }

    private void CancelInline()
    {
        _renaming = false;
        _confirming = false;
        Draw();
        Focus();
    }

    private void Select(int index)
    {
        if (index != _selected && !_renaming)
        {
            _selected = index;
            _confirming = false;
            Draw();
        }
    }

    private void Highlight()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            var mine = _rowIndex[i] == _selected;
            _rows[i].Background = mine ? _colours.Brush(_colours.Selected) : Brushes.Transparent;
            if (mine)
            {
                _rows[i].BringIntoView();
            }
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (_renaming)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Run(LayoutVerb.Rename, _rename.Text ?? string.Empty);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelInline();
            }

            return;
        }

        if (e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (_confirming)
        {
            e.Handled = true;
            if (e.Key == Key.Y)
            {
                Run(LayoutVerb.Delete);
            }
            else
            {
                CancelInline();
            }

            return;
        }

        var handled = true;
        switch (e.Key)
        {
            case Key.Up:
                Select(Visual(-1));
                break;
            case Key.Down:
                Select(Visual(1));
                break;
            case Key.Enter:
                Run(LayoutVerb.Apply);
                break;
            case Key.Escape:
                _session.Dismiss();
                _done();
                break;
            case Key.S:
                Run(LayoutVerb.SaveChanges);
                break;
            case Key.N:
                Run(LayoutVerb.SaveAsNew);
                break;
            case Key.W:
                Run(LayoutVerb.UseForWorld);
                break;
            case Key.C:
                Run(LayoutVerb.UseForCharacter);
                break;
            case Key.R:
                StartRename();
                break;
            case Key.D:
                StartDelete();
                break;
            default:
                handled = false;
                break;
        }

        e.Handled = handled;
    }

    /// <summary>The model index one row up or down in the order drawn (built-ins, then the player's), stopping at the ends as the cell list does.</summary>
    private int Visual(int delta)
    {
        if (_rowIndex.Count == 0)
        {
            return _selected;
        }

        var at = Math.Max(0, _rowIndex.IndexOf(_selected));
        return _rowIndex[Math.Clamp(at + delta, 0, _rowIndex.Count - 1)];
    }
}
