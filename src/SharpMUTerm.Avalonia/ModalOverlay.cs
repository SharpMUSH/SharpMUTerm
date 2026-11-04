using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using SharpMUTerm.Client.Modals;

namespace SharpMUTerm.Avalonia;

/// <summary>
/// One native modal over the window: a faint scrim over everything, and a rounded card holding the modal's view
/// (<see cref="QuitView"/>, <see cref="SearchView"/>, <see cref="HistoryView"/>, <see cref="LayoutsView"/>,
/// <see cref="WelcomeView"/>, <see cref="HelpView"/>, <see cref="DictionaryOfferView"/>). Fluent is applied to the card alone, in the client's variant, with the client's accent;
/// the rest of the window keeps the Simple theme its menus and tooltips were built against — the palette's rule.
/// <para>
/// A press on the scrim closes the modal (the quit prompt answers it as Stay). ⌃Q in any modal but the prompt goes to
/// the client, which opens the prompt over it — the cells' rule, where ⌃Q is a global shortcut no window can keep.
/// </para>
/// </summary>
internal sealed class ModalOverlay : Panel
{
    private readonly Action<ModalOverlay> _remove;
    private readonly IModalView _view;
    private readonly Border _card;

    /// <param name="session">What to draw, and where its gestures go.</param>
    /// <param name="remove">Takes the overlay out of the window.</param>
    /// <param name="forward">Hands a key to the client, as the cells would receive it.</param>
    public ModalOverlay(ModalSession session, Action<ModalOverlay> remove, Action<ConsoleKeyInfo> forward)
    {
        Session = session;
        _remove = remove;
        Colours = NativeTokens.For(session.Theme, session.IsDark);
        Background = new SolidColorBrush(Colours.Scrim);
        Focusable = false;

        _view = session switch
        {
            QuitSession quit => new QuitView(quit, Colours, Done),
            SearchSession search => new SearchView(search, Colours, Done),
            HistorySession history => new HistoryView(history, Colours, Done),
            LayoutsSession layouts => new LayoutsView(layouts, Colours, Done),
            WelcomeSession welcome => new WelcomeView(welcome, Colours, Done),
            HelpSession help => new HelpView(help, Colours, Done),
            DictionaryOfferSession offer => new DictionaryOfferView(offer, Colours, Done),
            WorldBrowseSession browse => new WorldBrowseView(browse, Colours, Done),
            ImportReviewSession import => new ImportReviewView(import, Colours, Done),
            ScriptConsoleSession console => new ScriptConsoleView(console, Colours, Done),
            ScrollbackViewSession history => new ScrollbackView(history, Colours, Done),
            _ => throw new ArgumentException($"no native view for {session.GetType().Name}", nameof(session)),
        };
        AutomationProperties.SetName(this, _view.Title);

        _card = new Border
        {
            Background = new SolidColorBrush(Colours.Card),
            BorderBrush = new SolidColorBrush(Colours.Edge),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(NativeTokens.RadiusL),
            BoxShadow = NativeTokens.Shadow,
            ClipToBounds = true,
            Child = (Control)_view,
        };

        var scope = new ThemeVariantScope
        {
            RequestedThemeVariant = session.IsDark ? ThemeVariant.Dark : ThemeVariant.Light,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = _view.Centred ? VerticalAlignment.Center : VerticalAlignment.Top,
            Margin = new Thickness(16, _view.Centred ? 16 : 56, 16, 16),
            Child = _card,
        };
        scope.Styles.Add(new FluentTheme());
        Colours.Apply(scope.Resources);
        Children.Add(scope);

        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Q && e.KeyModifiers == KeyModifiers.Control && session is not QuitSession)
            {
                e.Handled = true;
                forward(new ConsoleKeyInfo('\x11', ConsoleKey.Q, false, false, true));
            }
        }, RoutingStrategies.Tunnel);
        PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, this))
            {
                e.Handled = true;
                Session.Dismiss();
                Done();
            }
        };
        SizeChanged += (_, _) => Fit();
    }

    /// <summary>The session shown.</summary>
    public ModalSession Session { get; }

    /// <summary>The colours the card is drawn in: the client's variant.</summary>
    public NativeTokens Colours { get; }

    /// <summary>The view inside the card, for a test.</summary>
    public Control View => (Control)_view;

    /// <summary>The card, for a test that measures where it sits.</summary>
    public Border Card => _card;

    /// <summary>What had the keyboard when the modal opened, given it back when it closes; set by the presenter.</summary>
    public IInputElement? ReturnFocusTo { get; set; }

    /// <summary>Gives the keyboard to the view's first control.</summary>
    public void FocusFirst() => _view.FocusFirst();

    /// <summary>Stops listening to the session; the overlay is leaving the window.</summary>
    public void Detach() => _view.Detach();

    /// <summary>A gesture has ended the session: the overlay goes.</summary>
    private void Done()
    {
        if (Session.IsEnded)
        {
            _remove(this);
        }
    }

    private void Fit()
    {
        _card.Width = Math.Max(260, Math.Min(_view.PreferredWidth, Bounds.Width - 32));
        _card.MaxHeight = Math.Max(180, Bounds.Height - (_view.Centred ? 32 : 88));
    }
}

/// <summary>What <see cref="ModalOverlay"/> asks of the view in its card.</summary>
internal interface IModalView
{
    /// <summary>What a screen reader calls the modal.</summary>
    string Title { get; }

    /// <summary>The widest the card is drawn.</summary>
    double PreferredWidth { get; }

    /// <summary>Centred in the window (a question), rather than near its top (a list that grows as it is filtered).</summary>
    bool Centred { get; }

    /// <summary>Gives the keyboard to the control a reader starts in.</summary>
    void FocusFirst();

    /// <summary>Stops listening to the session.</summary>
    void Detach();
}
