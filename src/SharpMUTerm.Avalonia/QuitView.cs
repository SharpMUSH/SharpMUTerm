using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using SharpMUTerm.Client.Modals;
using SharpMUTerm.Tui;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// The window's quit prompt: the question, what quitting would end (<see cref="QuitModel.Consequences"/>, the cell
/// prompt's sentences), and two buttons with Stay chosen — the cell prompt's default, for its reason: ⌃Q is one key
/// from ⌃W and the window's close button is one stray click. Every key means what it means to the cell prompt,
/// because it is read by the same rules (<see cref="QuitPrompt.Interpret"/>): <c>y</c> quits, <c>n</c>, Esc and ⌃Q
/// stay, ←→ and ⇥ move between the buttons, ⏎ presses the chosen one.
/// </summary>
internal sealed class QuitView : DockPanel, IModalView
{
    private readonly QuitSession _session;
    private readonly NativeTokens _colours;
    private readonly Action _done;
    private readonly Button _quit;
    private readonly Button _stay;
    private readonly TextBlock _footer;
    private QuitChoice _selected = QuitChoice.Stay;

    public QuitView(QuitSession session, NativeTokens colours, Action done)
    {
        _session = session;
        _colours = colours;
        _done = done;
        var model = session.Model;

        var heading = ModalParts.Heading("Quit SharpMUTerm?", colours);
        heading.Margin = new Thickness(22, 18, 22, 8);
        SetDock(heading, Dock.Top);
        Children.Add(heading);

        var facts = new StackPanel { Margin = new Thickness(22, 2, 22, 4), Spacing = 6 };
        if (model.Consequences.Count == 0)
        {
            facts.Children.Add(new TextBlock { Text = QuitModel.NothingAtStake, Foreground = colours.Brush(colours.Dim), TextWrapping = TextWrapping.Wrap });
        }

        foreach (var consequence in model.Consequences)
        {
            var line = new DockPanel();
            var dot = new TextBlock { Text = "●", Foreground = colours.Brush(colours.Accent), Margin = new Thickness(0, 0, 10, 0), FontSize = NativeTokens.Small, VerticalAlignment = VerticalAlignment.Center };
            SetDock(dot, Dock.Left);
            line.Children.Add(dot);
            line.Children.Add(new TextBlock { Text = NativeKeys.Words(consequence), Foreground = colours.Brush(colours.Text), TextWrapping = TextWrapping.Wrap, FontSize = NativeTokens.Title });
            facts.Children.Add(line);
        }

        SetDock(facts, Dock.Top);
        Children.Add(facts);

        _footer = ModalParts.Footer(string.Empty, colours);
        SetDock(_footer, Dock.Bottom);

        _quit = ModalParts.Button("Quit", "Y", colours, () => Answer(true));
        _stay = ModalParts.Button("Stay", "N", colours, () => Answer(false));
        _quit.MinWidth = _stay.MinWidth = 96;
        _quit.GotFocus += (_, _) => Select(QuitChoice.Quit);
        _stay.GotFocus += (_, _) => Select(QuitChoice.Stay);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10,
            Margin = new Thickness(22, 14, 22, 0),
        };
        buttons.Children.Add(_quit);
        buttons.Children.Add(_stay);
        SetDock(buttons, Dock.Bottom);
        Children.Add(_footer);
        Children.Add(buttons);
        Children.Add(new Panel());

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Select(QuitChoice.Stay);
    }

    public string Title => "Quit";

    public double PreferredWidth => 460;

    public bool Centred => true;

    /// <summary>The button ⏎ presses now.</summary>
    public QuitChoice Selected => _selected;

    /// <summary>The two buttons, for a test.</summary>
    public Button QuitButton => _quit;

    public Button StayButton => _stay;

    /// <summary>Each consequence as drawn, for a test.</summary>
    public IReadOnlyList<string> Facts => _session.Model.Consequences;

    public void FocusFirst() => (_selected == QuitChoice.Quit ? _quit : _stay).Focus();

    public void Detach()
    {
    }

    /// <summary>Answers: quit (true) or stay.</summary>
    public void Answer(bool quit)
    {
        _session.Answer(quit);
        _done();
    }

    private void Select(QuitChoice choice)
    {
        _selected = choice;
        foreach (var (button, mine) in new[] { (_quit, QuitChoice.Quit), (_stay, QuitChoice.Stay) })
        {
            button.Classes.Set("accent", mine == choice);
        }

        _footer.Text = $"←→ Tab pick · Enter {(choice == QuitChoice.Quit ? "quits" : "stays")} · Esc stays · Ctrl+Q again stays";
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (ModalParts.Console(e) is not { } key)
        {
            return;
        }

        // Space presses the focused button, as a button does; everything else is the cell prompt's rule.
        var decision = QuitPrompt.Interpret(key, _selected);
        e.Handled = true;
        switch (decision.Action)
        {
            case QuitAction.Quit:
                Answer(true);
                break;
            case QuitAction.Stay:
                Answer(false);
                break;
            case QuitAction.Redraw:
                Select(decision.Selection);
                FocusFirst();
                break;
        }
    }
}
