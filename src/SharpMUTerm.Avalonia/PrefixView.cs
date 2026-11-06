using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SharpMUTerm.Client.Chrome;
using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The ⌃B which-key panel in the window: one row per key of the keymap the cell panel draws (<see cref="HeaderPrefix"/>),
/// its keys as caps, what it does, and why not when it cannot. It opens the moment ⌃B is pressed (the cells wait 400 ms
/// before drawing theirs). Any key, typed or clicked, ends it and goes to the client's prefix consumer, the method a key
/// pressed into the cell panel ends in; Esc cancels.
/// </summary>
internal sealed class PrefixView : DockPanel, IModalView
{
    private readonly PrefixPanelSession _session;
    private readonly Action _done;
    private readonly List<Border> _rows = [];

    public PrefixView(PrefixPanelSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _done = done;
        Focusable = true;
        AutomationProperties.SetName(this, Title);

        var heading = ModalParts.Heading(NativeKeys.Words(PrefixPanelSession.Title), colours);
        heading.Margin = new Thickness(NativeTokens.Space24, NativeTokens.Space16, NativeTokens.Space24, NativeTokens.Space8);
        SetDock(heading, Dock.Top);
        Children.Add(heading);

        var footer = ModalParts.Footer(NativeKeys.Words(session.Model.Exit), colours);
        SetDock(footer, Dock.Bottom);
        Children.Add(footer);

        var (frame, list, _) = ModalParts.List(colours);
        foreach (var key in session.Model.Keys)
        {
            list.Children.Add(RowFor(key, colours));
        }

        Children.Add(frame);
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent, OnText, RoutingStrategies.Tunnel);
    }

    public string Title => "Pane commands";

    public double PreferredWidth => 520;

    public bool Centred => true;

    /// <summary>The rows, one per key, for a test.</summary>
    public IReadOnlyList<Border> Rows => _rows;

    public void FocusFirst() => Focus();

    public void Detach()
    {
    }

    /// <summary>A row's key pressed by a click: the same as typing it.</summary>
    public void Press(char key)
    {
        _session.Press(key);
        _done();
    }

    private Border RowFor(HeaderPrefixKey key, NativeTokens colours)
    {
        var line = new DockPanel { LastChildFill = true };
        var caps = new StackPanel { Orientation = Orientation.Horizontal, Width = 112, VerticalAlignment = VerticalAlignment.Center };
        foreach (var part in key.Keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            caps.Children.Add(ModalParts.Chip(NativeKeys.Words(part), colours, new Thickness(0, 0, NativeTokens.Space6, 0)));
        }

        SetDock(caps, Dock.Left);
        line.Children.Add(caps);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = NativeKeys.Words(key.Title),
            FontSize = NativeTokens.Body,
            Foreground = colours.Brush(key.Available ? colours.Text : colours.Dim),
            TextWrapping = TextWrapping.Wrap,
        });
        if (key.Blocked is { } why)
        {
            words.Children.Add(new TextBlock
            {
                Text = NativeKeys.Words(why),
                FontSize = NativeTokens.Small,
                Foreground = colours.Brush(colours.Dim),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        line.Children.Add(words);
        var pressable = _session.KeysOf(key);
        Border? row = null;
        row = ModalParts.Row(
            line,
            key.Title,
            () => Select(row!, colours),
            () =>
            {
                if (key.Available && pressable.Count > 0)
                {
                    Press(pressable[0]);
                }
            });
        if (!key.Available)
        {
            row.Cursor = Cursor.Default;
        }

        _rows.Add(row);
        return row;
    }

    private void Select(Border row, NativeTokens colours)
    {
        foreach (var each in _rows)
        {
            ModalParts.Mark(each, ReferenceEquals(each, row), colours);
        }
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            _session.Dismiss();
            _done();
            return;
        }

        // A key that types nothing (an arrow, ⌃B again) goes to the client now; a printable one arrives as text.
        if (KeyMapper.FromKeyDown(e.Key, e.KeyModifiers) is { } mapped)
        {
            e.Handled = true;
            _session.Key(mapped);
            _done();
        }
    }

    private void OnText(object? sender, TextInputEventArgs e)
    {
        if (e.Text is { Length: 1 } text)
        {
            e.Handled = true;
            Press(text[0]);
        }
    }
}
